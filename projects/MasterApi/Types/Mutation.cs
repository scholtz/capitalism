using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using MasterApi.Configuration;
using MasterApi.Data;
using MasterApi.Data.Entities;
using MasterApi.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace MasterApi.Types;

public sealed class Mutation
{
    private const int StartupPackDurationMonths = 3;

    /// <summary>Registers a new master-portal account.</summary>
    public async Task<MasterAuthPayload> Register(
        RegisterInput input,
        [Service] MasterDbContext db,
        [Service] IOptions<JwtOptions> jwtOptions)
    {
        var email = input.Email.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(email) || !IsValidEmail(email))
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("A valid email address is required.")
                    .SetCode("INVALID_EMAIL")
                    .Build());
        }

        if (string.IsNullOrWhiteSpace(input.DisplayName))
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Display name is required.")
                    .SetCode("DISPLAY_NAME_REQUIRED")
                    .Build());
        }

        if (input.Password.Length < 8)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Password must be at least 8 characters.")
                    .SetCode("PASSWORD_TOO_SHORT")
                    .Build());
        }

        if (await db.PlayerAccounts.AnyAsync(p => p.Email == email))
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("An account with this email already exists.")
                    .SetCode("DUPLICATE_EMAIL")
                    .Build());
        }

        var now = DateTime.UtcNow;
        var player = new PlayerAccount
        {
            Id = Guid.NewGuid(),
            Email = email,
            DisplayName = input.DisplayName.Trim(),
            CreatedAtUtc = now,
        };

        var hasher = new PasswordHasher<PlayerAccount>();
        player.PasswordHash = hasher.HashPassword(player, input.Password);

        db.PlayerAccounts.Add(player);
        await db.SaveChangesAsync();

        var session = GenerateToken(player, jwtOptions.Value);

        return new MasterAuthPayload
        {
            Token = session.Token,
            ExpiresAtUtc = session.ExpiresAtUtc,
            Player = Query.ToProfile(player),
        };
    }

    /// <summary>Authenticates an existing master-portal account.</summary>
    public async Task<MasterAuthPayload> Login(
        LoginInput input,
        [Service] MasterDbContext db,
        [Service] IOptions<JwtOptions> jwtOptions)
    {
        var email = input.Email.Trim().ToLowerInvariant();
        var player = await db.PlayerAccounts.FirstOrDefaultAsync(p => p.Email == email);
        if (player is null)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Invalid email or password.")
                    .SetCode("INVALID_CREDENTIALS")
                    .Build());
        }

        var hasher = new PasswordHasher<PlayerAccount>();
        var result = hasher.VerifyHashedPassword(player, player.PasswordHash, input.Password);
        if (result == PasswordVerificationResult.Failed)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Invalid email or password.")
                    .SetCode("INVALID_CREDENTIALS")
                    .Build());
        }

        player.LastLoginAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();

        var session = GenerateToken(player, jwtOptions.Value);

        return new MasterAuthPayload
        {
            Token = session.Token,
            ExpiresAtUtc = session.ExpiresAtUtc,
            Player = Query.ToProfile(player),
        };
    }

    /// <summary>Prolongs (or creates) a Pro subscription for the authenticated player.</summary>
    [HotChocolate.Authorization.Authorize]
    public async Task<SubscriptionInfo> ProlongSubscription(
        ProlongSubscriptionInput input,
        ClaimsPrincipal claimsPrincipal,
        [Service] MasterDbContext db)
    {
        if (input.Months < 1 || input.Months > 12)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Months must be between 1 and 12.")
                    .SetCode("INVALID_MONTHS")
                    .Build());
        }

        var userId = Query.GetCurrentUserId(claimsPrincipal);
        var now = DateTime.UtcNow;

        var latestSub = await GetLatestSubscriptionAsync(db, userId);
        var resultSub = GrantOrCreateProSubscription(db, userId, now, input.Months, latestSub);

        await db.SaveChangesAsync();

        return Query.BuildSubscriptionInfo(resultSub, now);
    }

    /// <summary>Claims the one-time startup pack on the master portal and grants 3 months of Pro access.</summary>
    [HotChocolate.Authorization.Authorize]
    public async Task<SubscriptionInfo> ClaimStartupPack(
        ClaimsPrincipal claimsPrincipal,
        [Service] MasterDbContext db)
    {
        var userId = Query.GetCurrentUserId(claimsPrincipal);
        var now = DateTime.UtcNow;

        var player = await db.PlayerAccounts.FirstOrDefaultAsync(candidate => candidate.Id == userId)
            ?? throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Player not found.")
                    .SetCode("PLAYER_NOT_FOUND")
                    .Build());

        var latestSub = await GetLatestSubscriptionAsync(db, userId);

        if (player.StartupPackClaimedAtUtc is not null)
        {
            return Query.BuildSubscriptionInfo(latestSub, now);
        }

        var resultSub = GrantOrCreateProSubscription(db, userId, now, StartupPackDurationMonths, latestSub);
        player.StartupPackClaimedAtUtc = now;

        await db.SaveChangesAsync();

        return Query.BuildSubscriptionInfo(resultSub, now);
    }

    public async Task<GameNewsEntryInfo> UpsertGameNewsEntry(
        UpsertGameNewsEntryInput input,
        [Service] MasterDbContext db,
        [Service] IOptions<MasterServerOptions> masterServerOptions,
        [Service] IOptions<GameAdministrationOptions> gameAdministrationOptions)
    {
        Query.EnsureServiceAccess(input, masterServerOptions);

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
        MarkGameNewsReadInput input,
        [Service] MasterDbContext db,
        [Service] IOptions<MasterServerOptions> masterServerOptions)
    {
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

    public async Task<GlobalGameAdminGrantInfo> AssignGlobalGameAdmin(
        GlobalGameAdminGrantInput input,
        [Service] MasterDbContext db,
        [Service] IOptions<MasterServerOptions> masterServerOptions,
        [Service] IOptions<GameAdministrationOptions> gameAdministrationOptions)
    {
        Query.EnsureServiceAccess(input, masterServerOptions);

        var requesterEmail = Query.NormalizeEmail(input.RequesterEmail, "INVALID_REQUESTER_EMAIL");
        var targetEmail = Query.NormalizeEmail(input.TargetEmail, "INVALID_TARGET_EMAIL");
        var requesterAccess = await Query.BuildGameAdministrationAccessAsync(db, gameAdministrationOptions.Value, requesterEmail);
        if (!requesterAccess.IsRootAdministrator)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Only root administrators can assign the global game administrator role.")
                    .SetCode("ROOT_ADMIN_REQUIRED")
                    .Build());
        }

        var now = DateTime.UtcNow;
        var grant = await db.GlobalGameAdminGrants.FirstOrDefaultAsync(candidate => candidate.Email == targetEmail);
        if (grant is null)
        {
            grant = new GlobalGameAdminGrant
            {
                Id = Guid.NewGuid(),
                Email = targetEmail,
                GrantedByEmail = requesterEmail,
                GrantedAtUtc = now,
                UpdatedAtUtc = now,
            };
            db.GlobalGameAdminGrants.Add(grant);
        }
        else
        {
            grant.GrantedByEmail = requesterEmail;
            grant.UpdatedAtUtc = now;
        }

        await db.SaveChangesAsync();

        return new GlobalGameAdminGrantInfo
        {
            Id = grant.Id,
            Email = grant.Email,
            GrantedByEmail = grant.GrantedByEmail,
            GrantedAtUtc = grant.GrantedAtUtc,
            UpdatedAtUtc = grant.UpdatedAtUtc,
        };
    }

    public async Task<bool> RemoveGlobalGameAdmin(
        GlobalGameAdminGrantInput input,
        [Service] MasterDbContext db,
        [Service] IOptions<MasterServerOptions> masterServerOptions,
        [Service] IOptions<GameAdministrationOptions> gameAdministrationOptions)
    {
        Query.EnsureServiceAccess(input, masterServerOptions);

        var requesterEmail = Query.NormalizeEmail(input.RequesterEmail, "INVALID_REQUESTER_EMAIL");
        var targetEmail = Query.NormalizeEmail(input.TargetEmail, "INVALID_TARGET_EMAIL");
        var requesterAccess = await Query.BuildGameAdministrationAccessAsync(db, gameAdministrationOptions.Value, requesterEmail);
        if (!requesterAccess.IsRootAdministrator)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Only root administrators can remove the global game administrator role.")
                    .SetCode("ROOT_ADMIN_REQUIRED")
                    .Build());
        }

        var grant = await db.GlobalGameAdminGrants.FirstOrDefaultAsync(candidate => candidate.Email == targetEmail);
        if (grant is null)
        {
            return true;
        }

        db.GlobalGameAdminGrants.Remove(grant);
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<GameServerSummary> RegisterGameServer(
        RegisterGameServerInput input,
        [Service] MasterDbContext db,
        [Service] IOptions<MasterServerOptions> options)
    {
        var expectedKey = options.Value.RegistrationKey.Trim();
        if (string.IsNullOrWhiteSpace(expectedKey) || !string.Equals(expectedKey, input.RegistrationKey?.Trim(), StringComparison.Ordinal))
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Invalid game server registration key.")
                    .SetCode("INVALID_REGISTRATION_KEY")
                    .Build());
        }

        var serverKey = input.ServerKey.Trim();
        if (string.IsNullOrWhiteSpace(serverKey))
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Server key is required.")
                    .SetCode("SERVER_KEY_REQUIRED")
                    .Build());
        }

        var backendUrl = NormalizeRequiredUrl(input.BackendUrl, "BACKEND_URL_REQUIRED");
        var graphqlUrl = NormalizeRequiredUrl(input.GraphqlUrl, "GRAPHQL_URL_REQUIRED");
        var frontendUrl = NormalizeRequiredUrl(input.FrontendUrl, "FRONTEND_URL_REQUIRED");

        var now = DateTime.UtcNow;
        var server = await db.GameServers.FirstOrDefaultAsync(candidate => candidate.ServerKey == serverKey);

        if (server is null)
        {
            server = new GameServerNode
            {
                Id = Guid.NewGuid(),
                ServerKey = serverKey,
                RegisteredAtUtc = now,
            };

            db.GameServers.Add(server);
        }

        server.DisplayName = input.DisplayName.Trim();
        server.Description = input.Description?.Trim() ?? string.Empty;
        server.Region = input.Region.Trim();
        server.Environment = input.Environment.Trim();
        server.BackendUrl = backendUrl;
        server.GraphqlUrl = graphqlUrl;
        server.FrontendUrl = frontendUrl;
        server.Version = input.Version.Trim();
        server.PlayerCount = Math.Max(0, input.PlayerCount);
        server.CompanyCount = Math.Max(0, input.CompanyCount);
        server.CurrentTick = Math.Max(0, input.CurrentTick);
        server.LastHeartbeatAtUtc = now;
        server.UpdatedAtUtc = now;

        await db.SaveChangesAsync();

        return Query.ToSummary(server, now.AddSeconds(-Math.Max(5, options.Value.ActiveThresholdSeconds)));
    }

    private static string NormalizeRequiredUrl(string url, string errorCode)
    {
        var trimmedUrl = url.Trim();
        if (string.IsNullOrWhiteSpace(trimmedUrl)
            || !Uri.TryCreate(trimmedUrl, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("A valid absolute URL is required.")
                    .SetCode(errorCode)
                    .Build());
        }

        return trimmedUrl.TrimEnd('/');
    }

    private static (string Token, DateTime ExpiresAtUtc) GenerateToken(PlayerAccount player, JwtOptions options)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.SigningKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var expires = DateTime.UtcNow.AddMinutes(options.ExpiresMinutes);

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, player.Id.ToString()),
            new Claim(ClaimTypes.Email, player.Email),
            new Claim(ClaimTypes.Name, player.DisplayName),
        };

        var token = new JwtSecurityToken(
            issuer: options.Issuer,
            audience: options.Audience,
            claims: claims,
            expires: expires,
            signingCredentials: credentials);

        return (new JwtSecurityTokenHandler().WriteToken(token), expires);
    }

    private static async Task<ProSubscription?> GetLatestSubscriptionAsync(MasterDbContext db, Guid userId)
    {
        return await db.ProSubscriptions
            .Where(subscription => subscription.PlayerAccountId == userId)
            .OrderByDescending(subscription => subscription.ExpiresAtUtc)
            .FirstOrDefaultAsync();
    }

    private static ProSubscription GrantOrCreateProSubscription(
        MasterDbContext db,
        Guid userId,
        DateTime now,
        int months,
        ProSubscription? latestSub)
    {
        if (latestSub is not null
            && latestSub.Status == SubscriptionStatus.Active
            && latestSub.ExpiresAtUtc > now)
        {
            latestSub.ExpiresAtUtc = latestSub.ExpiresAtUtc.AddMonths(months);
            latestSub.UpdatedAtUtc = now;
            return latestSub;
        }

        if (latestSub is not null && latestSub.Status == SubscriptionStatus.Active)
        {
            latestSub.Status = SubscriptionStatus.Expired;
            latestSub.UpdatedAtUtc = now;
        }

        var newSub = new ProSubscription
        {
            Id = Guid.NewGuid(),
            PlayerAccountId = userId,
            Tier = SubscriptionTier.Pro,
            Status = SubscriptionStatus.Active,
            StartsAtUtc = now,
            ExpiresAtUtc = now.AddMonths(months),
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };

        db.ProSubscriptions.Add(newSub);
        return newSub;
    }

    private static bool IsValidEmail(string email)
    {
        try
        {
            var addr = new System.Net.Mail.MailAddress(email);
            return string.Equals(addr.Address, email, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static string NormalizeLocale(string locale)
    {
        var normalizedLocale = locale.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalizedLocale))
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Localization locale is required.")
                    .SetCode("INVALID_LOCALE")
                    .Build());
        }

        return normalizedLocale;
    }

    /// <summary>Saves (creates or updates) a reusable building layout template.</summary>
    [HotChocolate.Authorization.Authorize]
    public async Task<BuildingLayoutTemplateInfo> SaveBuildingLayout(
        SaveBuildingLayoutInput input,
        ClaimsPrincipal claimsPrincipal,
        [Service] MasterDbContext db)
    {
        var userId = Query.GetCurrentUserId(claimsPrincipal);

        if (string.IsNullOrWhiteSpace(input.Name))
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Layout name is required.")
                    .SetCode("LAYOUT_NAME_REQUIRED")
                    .Build());
        }

        if (string.IsNullOrWhiteSpace(input.BuildingType))
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Building type is required.")
                    .SetCode("BUILDING_TYPE_REQUIRED")
                    .Build());
        }

        var unitsJson = input.UnitsJson ?? "[]";
        if (unitsJson.Length > 32_768)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("UnitsJson payload exceeds the 32 KB limit.")
                    .SetCode("UNITS_JSON_TOO_LARGE")
                    .Build());
        }

        try
        {
            System.Text.Json.JsonDocument.Parse(unitsJson);
        }
        catch (System.Text.Json.JsonException)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("UnitsJson must be valid JSON.")
                    .SetCode("UNITS_JSON_INVALID")
                    .Build());
        }

        var now = DateTime.UtcNow;

        if (input.ExistingId.HasValue)
        {
            var existing = await db.BuildingLayoutTemplates
                .FirstOrDefaultAsync(l => l.Id == input.ExistingId.Value && l.PlayerAccountId == userId);

            if (existing is null)
            {
                throw new GraphQLException(
                    ErrorBuilder.New()
                        .SetMessage("Layout template not found or you do not own it.")
                        .SetCode("LAYOUT_NOT_FOUND")
                        .Build());
            }

            existing.Name = input.Name.Trim();
            existing.Description = string.IsNullOrWhiteSpace(input.Description) ? null : input.Description.Trim();
            existing.BuildingType = input.BuildingType.Trim().ToUpperInvariant();
            existing.UnitsJson = unitsJson;
            existing.UpdatedAtUtc = now;

            await db.SaveChangesAsync();

            return new BuildingLayoutTemplateInfo
            {
                Id = existing.Id,
                Name = existing.Name,
                Description = existing.Description,
                BuildingType = existing.BuildingType,
                UnitsJson = existing.UnitsJson,
                CreatedAtUtc = existing.CreatedAtUtc,
                UpdatedAtUtc = existing.UpdatedAtUtc,
            };
        }
        else
        {
            var layout = new Data.Entities.BuildingLayoutTemplate
            {
                Id = Guid.NewGuid(),
                PlayerAccountId = userId,
                Name = input.Name.Trim(),
                Description = string.IsNullOrWhiteSpace(input.Description) ? null : input.Description.Trim(),
                BuildingType = input.BuildingType.Trim().ToUpperInvariant(),
                UnitsJson = unitsJson,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
            };

            db.BuildingLayoutTemplates.Add(layout);
            await db.SaveChangesAsync();

            return new BuildingLayoutTemplateInfo
            {
                Id = layout.Id,
                Name = layout.Name,
                Description = layout.Description,
                BuildingType = layout.BuildingType,
                UnitsJson = layout.UnitsJson,
                CreatedAtUtc = layout.CreatedAtUtc,
                UpdatedAtUtc = layout.UpdatedAtUtc,
            };
        }
    }

    /// <summary>Deletes a reusable building layout template owned by the current user.</summary>
    [HotChocolate.Authorization.Authorize]
    public async Task<bool> DeleteBuildingLayout(
        DeleteBuildingLayoutInput input,
        ClaimsPrincipal claimsPrincipal,
        [Service] MasterDbContext db)
    {
        var userId = Query.GetCurrentUserId(claimsPrincipal);

        var layout = await db.BuildingLayoutTemplates
            .FirstOrDefaultAsync(l => l.Id == input.Id && l.PlayerAccountId == userId);

        if (layout is null)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Layout template not found or you do not own it.")
                    .SetCode("LAYOUT_NOT_FOUND")
                    .Build());
        }

        db.BuildingLayoutTemplates.Remove(layout);
        await db.SaveChangesAsync();
        return true;
    }
}