using System.Security.Claims;
using MasterApi.Configuration;
using MasterApi.Data;
using MasterApi.Data.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MasterApi.Types;

public sealed partial class Mutation
{
    public async Task<GameNewsEntryInfo> UpsertGameNewsEntry(
        UpsertGameNewsEntryInput input,
        [Service] MasterDbContext db,
        [Service] IOptions<MasterServerOptions> masterServerOptions,
        [Service] IOptions<GameAdministrationOptions> gameAdministrationOptions)
    {
        Query.EnsureServiceAccess(input, masterServerOptions, false, false);

        var requesterEmail = Query.NormalizeEmail(input.RequesterEmail, "INVALID_REQUESTER_EMAIL");
        var requesterAccess = await Query.BuildGameAdministrationAccessAsync(db, gameAdministrationOptions.Value, requesterEmail);
        var entryType = input.EntryType.Trim().ToUpperInvariant();
        var status = input.Status.Trim().ToUpperInvariant();

        if (!GameNewsEntryType.All.Contains(entryType))
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Entry type must be NEWS or CHANGELOG.")
                    .SetCode("INVALID_ENTRY_TYPE")
                    .Build());
        }

        if (!GameNewsEntryStatus.All.Contains(status))
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Entry status must be DRAFT or PUBLISHED.")
                    .SetCode("INVALID_ENTRY_STATUS")
                    .Build());
        }

        if (input.Localizations.Count == 0)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("At least one localization is required.")
                    .SetCode("LOCALIZATION_REQUIRED")
                    .Build());
        }

        var requestedTargetServerKey = string.IsNullOrWhiteSpace(input.TargetServerKey)
            ? null
            : input.TargetServerKey.Trim();
        if (!requesterAccess.CanAccessEveryGameDashboard && requestedTargetServerKey is null)
        {
            requestedTargetServerKey = input.ServerKey;
        }

        var now = DateTime.UtcNow;
        var entry = input.EntryId.HasValue
            ? await db.GameNewsEntries
                .Include(candidate => candidate.Localizations)
                .Include(candidate => candidate.ReadReceipts)
                .FirstOrDefaultAsync(candidate => candidate.Id == input.EntryId.Value)
            : null;

        if (input.EntryId.HasValue && entry is null)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("News entry not found.")
                    .SetCode("NEWS_ENTRY_NOT_FOUND")
                    .Build());
        }

        var touchesGlobalOrOtherServerScope = requestedTargetServerKey is null
            || requestedTargetServerKey != input.ServerKey
            || (entry is not null && (entry.TargetServerKey is null || entry.TargetServerKey != input.ServerKey));

        if (touchesGlobalOrOtherServerScope && !requesterAccess.CanAccessEveryGameDashboard)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Only global or root administrators can edit global or cross-server news entries.")
                    .SetCode("GLOBAL_ADMIN_REQUIRED")
                    .Build());
        }

        entry ??= new GameNewsEntry
        {
            Id = Guid.NewGuid(),
            CreatedAtUtc = now,
            CreatedByEmail = requesterEmail,
        };

        if (db.Entry(entry).State == EntityState.Detached)
        {
            db.GameNewsEntries.Add(entry);
        }

        entry.EntryType = entryType;
        entry.Status = status;
        entry.TargetServerKey = requestedTargetServerKey;
        entry.UpdatedByEmail = requesterEmail;
        entry.UpdatedAtUtc = now;
        entry.PublishedAtUtc = status == GameNewsEntryStatus.Published
            ? entry.PublishedAtUtc ?? now
            : null;

        var submittedLocalizations = input.Localizations
            .Select(localization => new
            {
                Locale = NormalizeLocale(localization.Locale),
                Title = localization.Title.Trim(),
                Summary = localization.Summary.Trim(),
                HtmlContent = localization.HtmlContent.Trim(),
            })
            .GroupBy(localization => localization.Locale)
            .Select(group => group.Last())
            .ToList();

        foreach (var localization in submittedLocalizations)
        {
            if (string.IsNullOrWhiteSpace(localization.Title) || string.IsNullOrWhiteSpace(localization.HtmlContent))
            {
                throw new GraphQLException(
                    ErrorBuilder.New()
                        .SetMessage("Each localization requires a title and HTML content.")
                        .SetCode("INVALID_LOCALIZATION")
                        .Build());
            }
        }

        var submittedLocales = submittedLocalizations
            .Select(localization => localization.Locale)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var existingLocalization in entry.Localizations.Where(localization => !submittedLocales.Contains(localization.Locale)).ToList())
        {
            db.GameNewsEntryLocalizations.Remove(existingLocalization);
        }

        var localizationsByLocale = entry.Localizations.ToDictionary(localization => localization.Locale, StringComparer.Ordinal);
        foreach (var localization in submittedLocalizations)
        {
            if (!localizationsByLocale.TryGetValue(localization.Locale, out var currentLocalization))
            {
                currentLocalization = new GameNewsEntryLocalization
                {
                    Id = Guid.NewGuid(),
                    GameNewsEntryId = entry.Id,
                    Locale = localization.Locale,
                };
                entry.Localizations.Add(currentLocalization);
            }

            currentLocalization.Title = localization.Title;
            currentLocalization.Summary = localization.Summary;
            currentLocalization.HtmlContent = localization.HtmlContent;
        }

        await db.SaveChangesAsync();

        return Query.ToGameNewsEntryInfo(entry, null, input.ServerKey);
    }

    public async Task<bool> MarkGameNewsRead(
        [Service] MasterDbContext db,
        [Service] IOptions<MasterServerOptions> masterServerOptions,
        MarkGameNewsReadInput? input = null)
    {
        if(input is null)
        {
            input = new MarkGameNewsReadInput()
            {

            };
        }

        Query.EnsureServiceAccess(input, masterServerOptions);
        
        if (input.EntryIds.Count == 0)
        {
            return true;
        }

        var playerEmail = Query.NormalizeEmail(input.PlayerEmail, "INVALID_PLAYER_EMAIL");
        var validEntryIds = await db.GameNewsEntries
            .AsNoTracking()
            .Where(entry => input.EntryIds.Contains(entry.Id))
            .Where(entry => entry.Status == GameNewsEntryStatus.Published)
            .Where(entry => entry.TargetServerKey == null || entry.TargetServerKey == input.ServerKey)
            .Select(entry => entry.Id)
            .ToListAsync();

        if (validEntryIds.Count == 0)
        {
            return true;
        }

        var existingReadEntryIds = await db.GameNewsReadReceipts
            .AsNoTracking()
            .Where(receipt => receipt.PlayerEmail == playerEmail && receipt.ServerKey == input.ServerKey)
            .Where(receipt => validEntryIds.Contains(receipt.GameNewsEntryId))
            .Select(receipt => receipt.GameNewsEntryId)
            .ToListAsync();

        var now = DateTime.UtcNow;
        foreach (var entryId in validEntryIds.Except(existingReadEntryIds))
        {
            db.GameNewsReadReceipts.Add(new GameNewsReadReceipt
            {
                Id = Guid.NewGuid(),
                GameNewsEntryId = entryId,
                PlayerEmail = playerEmail,
                ServerKey = input.ServerKey,
                ReadAtUtc = now,
            });
        }

        await db.SaveChangesAsync();
        return true;
    }
}
