using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Api.Data;
using Api.Data.Entities;
using Api.Engine;
using Api.Security;
using Api.Utilities;
using HotChocolate.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Api.Types;

public sealed partial class Mutation
{
    /// <summary>Registers a new player account and returns an auth token.</summary>
    public async Task<AuthPayload> Register(
        RegisterInput input,
        [Service] AppDbContext db,
        [Service] IOptions<JwtOptions> jwtOptions)
    {
        if (await db.Players.AnyAsync(p => p.Email == input.Email))
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("A player with this email already exists.")
                    .SetCode("DUPLICATE_EMAIL")
                    .Build());
        }

        var player = new Player
        {
            Id = Guid.NewGuid(),
            Email = input.Email,
            DisplayName = input.DisplayName,
            Role = PlayerRole.Player,
            PersonalCash = 200_000m,
            ActiveAccountType = AccountContextType.Person,
            CreatedAtUtc = DateTime.UtcNow
        };

        var hasher = new PasswordHasher<Player>();
        player.PasswordHash = hasher.HashPassword(player, input.Password);

        db.Players.Add(player);
        await db.SaveChangesAsync();

        var session = GenerateToken(player, jwtOptions.Value);
        return new AuthPayload
        {
            Token = session.Token,
            ExpiresAtUtc = session.ExpiresAtUtc,
            Player = player
        };
    }

    /// <summary>Authenticates a player and returns an auth token.</summary>
    public async Task<AuthPayload> Login(
        LoginInput input,
        [Service] AppDbContext db,
        [Service] IOptions<JwtOptions> jwtOptions)
    {
        var player = await db.Players.FirstOrDefaultAsync(p => p.Email == input.Email);
        if (player is null)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Invalid email or password.")
                    .SetCode("INVALID_CREDENTIALS")
                    .Build());
        }

        var hasher = new PasswordHasher<Player>();
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
        return new AuthPayload
        {
            Token = session.Token,
            ExpiresAtUtc = session.ExpiresAtUtc,
            Player = player
        };
    }

    [Authorize]
    public async Task<AuthPayload> StartAdminImpersonation(
        StartAdminImpersonationInput input,
        [Service] AppDbContext db,
        [Service] IHttpContextAccessor httpContextAccessor,
        [Service] IOptions<JwtOptions> jwtOptions,
        [Service] GameAdminAuthorizationService gameAdminAuthorizationService)
    {
        var principal = httpContextAccessor.HttpContext!.User;
        var accessContext = await gameAdminAuthorizationService.RequireAdminDashboardAccessAsync(db, principal, httpContextAccessor.HttpContext.RequestAborted);
        var targetPlayer = await db.Players
            .AsNoTracking()
            .Include(player => player.Companies)
            .FirstOrDefaultAsync(player => player.Id == input.TargetPlayerId, httpContextAccessor.HttpContext.RequestAborted)
            ?? throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Player not found.")
                    .SetCode("PLAYER_NOT_FOUND")
                    .Build());

        var impersonationContext = ResolveImpersonationAccountContext(targetPlayer, input);
        targetPlayer.ActiveAccountType = impersonationContext.EffectiveAccountType;
        targetPlayer.ActiveCompanyId = impersonationContext.EffectiveCompanyId;

        var session = GenerateToken(accessContext.ActorPlayer, jwtOptions.Value, new AdminImpersonationTokenContext(
            targetPlayer,
            impersonationContext.EffectiveAccountType,
            impersonationContext.EffectiveCompanyId,
            impersonationContext.EffectiveCompanyName));

        return new AuthPayload
        {
            Token = session.Token,
            ExpiresAtUtc = session.ExpiresAtUtc,
            Player = targetPlayer,
        };
    }

    [Authorize]
    public async Task<AuthPayload> StopAdminImpersonation(
        [Service] AppDbContext db,
        [Service] IHttpContextAccessor httpContextAccessor,
        [Service] IOptions<JwtOptions> jwtOptions)
    {
        var actorUserId = httpContextAccessor.HttpContext!.User.GetAuthenticatedActorUserId();
        var actorPlayer = await db.Players
            .AsNoTracking()
            .Include(player => player.Companies)
            .FirstOrDefaultAsync(player => player.Id == actorUserId, httpContextAccessor.HttpContext.RequestAborted)
            ?? throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Player not found.")
                    .SetCode("PLAYER_NOT_FOUND")
                    .Build());

        var session = GenerateToken(actorPlayer, jwtOptions.Value);
        return new AuthPayload
        {
            Token = session.Token,
            ExpiresAtUtc = session.ExpiresAtUtc,
            Player = actorPlayer,
        };
    }
}
