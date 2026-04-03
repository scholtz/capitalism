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

        return new MasterPlayerProfile
        {
            Id = player.Id,
            Email = player.Email,
            DisplayName = player.DisplayName,
            CreatedAtUtc = player.CreatedAtUtc,
        };
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
}