using System.Security.Claims;
using MasterApi.Configuration;
using MasterApi.Data;
using MasterApi.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MasterApi.Types;

public sealed class Query
{
    public async Task<List<GameServerSummary>> GetGameServers(
        [Service] MasterDbContext db,
        [Service] IOptions<MasterServerOptions> options)
    {
        var now = DateTime.UtcNow;
        var cutoff = now.AddSeconds(-Math.Max(5, options.Value.ActiveThresholdSeconds));

        var servers = await db.GameServers
            .OrderByDescending(server => server.LastHeartbeatAtUtc)
            .ThenBy(server => server.DisplayName)
            .ToListAsync();

        return servers
            .Select(server => ToSummary(server, cutoff))
            .OrderByDescending(server => server.IsOnline)
            .ThenBy(server => server.DisplayName)
            .ToList();
    }

    [HotChocolate.Authorization.Authorize]
    public async Task<MasterPlayerProfile> GetMe(
        ClaimsPrincipal claimsPrincipal,
        [Service] MasterDbContext db)
    {
        var userId = GetCurrentUserId(claimsPrincipal);
        var player = await db.PlayerAccounts.FirstOrDefaultAsync(p => p.Id == userId)
            ?? throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Player not found.")
                    .SetCode("PLAYER_NOT_FOUND")
                    .Build());

        return ToProfile(player);
    }

    [HotChocolate.Authorization.Authorize]
    public async Task<SubscriptionInfo> GetMySubscription(
        ClaimsPrincipal claimsPrincipal,
        [Service] MasterDbContext db)
    {
        var userId = GetCurrentUserId(claimsPrincipal);
        var now = DateTime.UtcNow;

        // Return the most recent subscription regardless of DB status so that
        // players with an expired Pro plan see "EXPIRED" rather than "FREE/NONE".
        // BuildSubscriptionInfo uses the expiry timestamp to compute the live state.
        var latestSub = await db.ProSubscriptions
            .Where(s => s.PlayerAccountId == userId)
            .OrderByDescending(s => s.ExpiresAtUtc)
            .FirstOrDefaultAsync();

        return BuildSubscriptionInfo(latestSub, now);
    }

    public async Task<GameAdministrationAccessInfo> GetGameAdministrationAccess(
        GetGameAdministrationAccessInput input,
        [Service] MasterDbContext db,
        [Service] IOptions<MasterServerOptions> masterServerOptions,
        [Service] IOptions<GameAdministrationOptions> gameAdministrationOptions)
    {
        EnsureServiceAccess(input, masterServerOptions);
        var email = NormalizeEmail(input.Email, "INVALID_EMAIL");

        return await BuildGameAdministrationAccessAsync(db, gameAdministrationOptions.Value, email);
    }

    public async Task<List<GlobalGameAdminGrantInfo>> GetGlobalGameAdminGrants(
        GetGlobalGameAdminGrantsInput input,
        [Service] MasterDbContext db,
        [Service] IOptions<MasterServerOptions> masterServerOptions,
        [Service] IOptions<GameAdministrationOptions> gameAdministrationOptions)
    {
        EnsureServiceAccess(input, masterServerOptions);

        var requesterEmail = NormalizeEmail(input.RequesterEmail, "INVALID_REQUESTER_EMAIL");
        var requesterAccess = await BuildGameAdministrationAccessAsync(db, gameAdministrationOptions.Value, requesterEmail);
        if (!requesterAccess.IsRootAdministrator)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Only root administrators can manage global game administrators.")
                    .SetCode("ROOT_ADMIN_REQUIRED")
                    .Build());
        }

        return await db.GlobalGameAdminGrants
            .AsNoTracking()
            .OrderBy(grant => grant.Email)
            .Select(grant => new GlobalGameAdminGrantInfo
            {
                Id = grant.Id,
                Email = grant.Email,
                GrantedByEmail = grant.GrantedByEmail,
                GrantedAtUtc = grant.GrantedAtUtc,
                UpdatedAtUtc = grant.UpdatedAtUtc,
            })
            .ToListAsync();
    }

    public async Task<GameNewsFeedResult> GetGameNewsFeed(
        GetGameNewsFeedInput input,
        [Service] MasterDbContext db,
        [Service] IOptions<MasterServerOptions> masterServerOptions)
    {
        EnsureServiceAccess(input, masterServerOptions);

        if (input.IncludeDrafts && string.IsNullOrWhiteSpace(input.RequesterEmail))
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Requester email is required when draft entries are requested.")
                    .SetCode("REQUESTER_EMAIL_REQUIRED")
                    .Build());
        }

        var playerEmail = string.IsNullOrWhiteSpace(input.PlayerEmail)
            ? null
            : NormalizeEmail(input.PlayerEmail, "INVALID_PLAYER_EMAIL");
        var limit = Math.Clamp(input.Limit, 1, 100);

        var entries = await db.GameNewsEntries
            .AsNoTracking()
            .Include(entry => entry.Localizations)
            .Include(entry => entry.ReadReceipts)
            .Where(entry => entry.TargetServerKey == null || entry.TargetServerKey == input.ServerKey)
            .Where(entry => input.IncludeDrafts || entry.Status == GameNewsEntryStatus.Published)
            .OrderByDescending(entry => entry.PublishedAtUtc ?? entry.UpdatedAtUtc)
            .ThenByDescending(entry => entry.CreatedAtUtc)
            .Take(limit)
            .AsSplitQuery()
            .ToListAsync();

        var items = entries
            .Select(entry => ToGameNewsEntryInfo(entry, playerEmail, input.ServerKey))
            .ToList();

        var unreadCount = 0;
        if (playerEmail is not null)
        {
            var publishedEntryIds = await db.GameNewsEntries
                .AsNoTracking()
                .Where(entry => entry.Status == GameNewsEntryStatus.Published)
                .Where(entry => entry.TargetServerKey == null || entry.TargetServerKey == input.ServerKey)
                .Select(entry => entry.Id)
                .ToListAsync();

            if (publishedEntryIds.Count > 0)
            {
                var readEntryIds = await db.GameNewsReadReceipts
                    .AsNoTracking()
                    .Where(receipt => receipt.PlayerEmail == playerEmail && receipt.ServerKey == input.ServerKey)
                    .Where(receipt => publishedEntryIds.Contains(receipt.GameNewsEntryId))
                    .Select(receipt => receipt.GameNewsEntryId)
                    .Distinct()
                    .CountAsync();

                unreadCount = Math.Max(0, publishedEntryIds.Count - readEntryIds);
            }
        }

        return new GameNewsFeedResult
        {
            Items = items,
            UnreadCount = unreadCount,
        };
    }

    internal static GameServerSummary ToSummary(Data.Entities.GameServerNode server, DateTime cutoff)
    {
        return new GameServerSummary
        {
            Id = server.Id,
            ServerKey = server.ServerKey,
            DisplayName = server.DisplayName,
            Description = server.Description,
            Region = server.Region,
            Environment = server.Environment,
            BackendUrl = server.BackendUrl,
            GraphqlUrl = server.GraphqlUrl,
            FrontendUrl = server.FrontendUrl,
            Version = server.Version,
            PlayerCount = server.PlayerCount,
            CompanyCount = server.CompanyCount,
            CurrentTick = server.CurrentTick,
            RegisteredAtUtc = server.RegisteredAtUtc,
            LastHeartbeatAtUtc = server.LastHeartbeatAtUtc,
            IsOnline = server.LastHeartbeatAtUtc >= cutoff,
        };
    }

    internal static Guid GetCurrentUserId(ClaimsPrincipal principal)
    {
        var idClaim = principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Authenticated user identity is missing.")
                    .SetCode("IDENTITY_MISSING")
                    .Build());

        return Guid.Parse(idClaim);
    }

    internal static MasterPlayerProfile ToProfile(PlayerAccount player)
    {
        return new MasterPlayerProfile
        {
            Id = player.Id,
            Email = player.Email,
            DisplayName = player.DisplayName,
            CreatedAtUtc = player.CreatedAtUtc,
            StartupPackClaimedAtUtc = player.StartupPackClaimedAtUtc,
            CanClaimStartupPack = player.StartupPackClaimedAtUtc is null,
        };
    }

    internal static void EnsureServiceAccess(
        MasterServerServiceInput input,
        IOptions<MasterServerOptions> masterServerOptions)
    {
        var expectedKey = masterServerOptions.Value.RegistrationKey.Trim();
        if (string.IsNullOrWhiteSpace(expectedKey)
            || !string.Equals(expectedKey, input.RegistrationKey?.Trim(), StringComparison.Ordinal))
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Invalid game server registration key.")
                    .SetCode("INVALID_REGISTRATION_KEY")
                    .Build());
        }

        if (string.IsNullOrWhiteSpace(input.ServerKey))
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Server key is required.")
                    .SetCode("SERVER_KEY_REQUIRED")
                    .Build());
        }
    }

    internal static async Task<GameAdministrationAccessInfo> BuildGameAdministrationAccessAsync(
        MasterDbContext db,
        GameAdministrationOptions options,
        string normalizedEmail)
    {
        var rootAdminEmails = BuildRootAdministratorEmailSet(options);
        var hasGlobalAdminRole = await db.GlobalGameAdminGrants
            .AsNoTracking()
            .AnyAsync(grant => grant.Email == normalizedEmail);
        var isRootAdministrator = rootAdminEmails.Contains(normalizedEmail);

        return new GameAdministrationAccessInfo
        {
            Email = normalizedEmail,
            IsRootAdministrator = isRootAdministrator,
            HasGlobalAdminRole = hasGlobalAdminRole,
            CanAccessEveryGameDashboard = isRootAdministrator || hasGlobalAdminRole,
        };
    }

    internal static HashSet<string> BuildRootAdministratorEmailSet(GameAdministrationOptions options)
    {
        return options.RootAdministratorEmails
            .Select(email => email?.Trim().ToLowerInvariant())
            .Where(email => !string.IsNullOrWhiteSpace(email))
            .Cast<string>()
            .ToHashSet(StringComparer.Ordinal);
    }

    internal static GameNewsEntryInfo ToGameNewsEntryInfo(
        GameNewsEntry entry,
        string? normalizedPlayerEmail,
        string serverKey)
    {
        var isRead = normalizedPlayerEmail is not null
            && entry.ReadReceipts.Any(receipt => receipt.PlayerEmail == normalizedPlayerEmail && receipt.ServerKey == serverKey);

        return new GameNewsEntryInfo
        {
            Id = entry.Id,
            EntryType = entry.EntryType,
            Status = entry.Status,
            TargetServerKey = entry.TargetServerKey,
            CreatedByEmail = entry.CreatedByEmail,
            UpdatedByEmail = entry.UpdatedByEmail,
            CreatedAtUtc = entry.CreatedAtUtc,
            UpdatedAtUtc = entry.UpdatedAtUtc,
            PublishedAtUtc = entry.PublishedAtUtc,
            IsRead = isRead,
            Localizations = entry.Localizations
                .OrderBy(localization => localization.Locale)
                .Select(localization => new GameNewsLocalizationInfo
                {
                    Locale = localization.Locale,
                    Title = localization.Title,
                    Summary = localization.Summary,
                    HtmlContent = localization.HtmlContent,
                })
                .ToList(),
        };
    }

    internal static SubscriptionInfo BuildSubscriptionInfo(ProSubscription? sub, DateTime now)
    {
        if (sub is null)
        {
            return new SubscriptionInfo
            {
                Tier = "FREE",
                Status = "NONE",
                IsActive = false,
                CanProlong = true,
            };
        }

        var isActive = sub.ExpiresAtUtc > now;
        var daysRemaining = isActive ? (int)Math.Ceiling((sub.ExpiresAtUtc - now).TotalDays) : 0;

        return new SubscriptionInfo
        {
            Tier = sub.Tier.ToString().ToUpperInvariant(),
            Status = isActive ? "ACTIVE" : "EXPIRED",
            StartsAtUtc = sub.StartsAtUtc,
            ExpiresAtUtc = sub.ExpiresAtUtc,
            IsActive = isActive,
            DaysRemaining = isActive ? daysRemaining : null,
            CanProlong = true,
        };
    }

    [HotChocolate.Authorization.Authorize]
    public async Task<List<BuildingLayoutTemplateInfo>> GetMyBuildingLayouts(
        ClaimsPrincipal claimsPrincipal,
        [Service] MasterDbContext db)
    {
        var userId = GetCurrentUserId(claimsPrincipal);

        return await db.BuildingLayoutTemplates
            .AsNoTracking()
            .Where(l => l.PlayerAccountId == userId)
            .OrderByDescending(l => l.UpdatedAtUtc)
            .Select(l => new BuildingLayoutTemplateInfo
            {
                Id = l.Id,
                Name = l.Name,
                Description = l.Description,
                BuildingType = l.BuildingType,
                UnitsJson = l.UnitsJson,
                CreatedAtUtc = l.CreatedAtUtc,
                UpdatedAtUtc = l.UpdatedAtUtc,
            })
            .ToListAsync();
    }

    internal static string NormalizeEmail(string email, string errorCode)
    {
        var normalizedEmail = email.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalizedEmail))
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("A valid email address is required.")
                    .SetCode(errorCode)
                    .Build());
        }

        try
        {
            var mailAddress = new System.Net.Mail.MailAddress(normalizedEmail);
            if (!string.Equals(mailAddress.Address, normalizedEmail, StringComparison.OrdinalIgnoreCase))
            {
                throw new FormatException();
            }
        }
        catch
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("A valid email address is required.")
                    .SetCode(errorCode)
                    .Build());
        }

        return normalizedEmail;
    }
}