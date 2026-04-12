using System.Security.Claims;
using MasterApi.Configuration;
using MasterApi.Data;
using MasterApi.Data.Entities;
using MasterApi.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MasterApi.Types;

public sealed partial class Mutation
{
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

        var player = await Query.GetCurrentUserAsync(claimsPrincipal, db)
            ?? throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Player not found.")
                    .SetCode("PLAYER_NOT_FOUND")
                    .Build());
        var userId = player.Id;
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
        var player = await Query.GetCurrentUserAsync(claimsPrincipal, db)
            ?? throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Player not found.")
                    .SetCode("PLAYER_NOT_FOUND")
                    .Build());
        var userId = player.Id;
        var now = DateTime.UtcNow;

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
}
