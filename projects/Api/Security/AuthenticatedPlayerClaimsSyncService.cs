using System.Security.Claims;
using Api.Data;
using Api.Data.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Api.Security;

public sealed class AuthenticatedPlayerClaimsSyncService(AppDbContext db)
{
    public async Task SyncAsync(
        ClaimsPrincipal principal,
        ClaimsIdentity identity,
        CancellationToken cancellationToken = default)
    {
        var email = principal.FindFirstValue(ClaimTypes.Email)?.Trim();
        if (string.IsNullOrWhiteSpace(email))
        {
            return;
        }

        var normalizedEmail = email.ToLowerInvariant();
        var claimedDisplayName = principal.FindFirstValue(ClaimsPrincipalExtensions.EffectivePlayerNameClaimType)
            ?? principal.FindFirstValue(ClaimTypes.Name)
            ?? normalizedEmail;

        var player = await db.Players.FirstOrDefaultAsync(
            candidate => candidate.Email == email || candidate.Email.ToLower() == normalizedEmail,
            cancellationToken);

        var changed = false;
        if (player is null)
        {
            var actorIdClaim = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            var playerId = Guid.TryParse(actorIdClaim, out var parsedId) ? parsedId : Guid.NewGuid();
            var displayName = claimedDisplayName.Trim();

            player = new Player
            {
                Id = playerId,
                Email = normalizedEmail,
                DisplayName = displayName,
                Role = PlayerRole.Player,
                PersonalCash = 200_000m,
                ActiveAccountType = AccountContextType.Person,
                CreatedAtUtc = DateTime.UtcNow,
            };
            player.PasswordHash = new PasswordHasher<Player>().HashPassword(player, Guid.NewGuid().ToString("N"));

            db.Players.Add(player);
            changed = true;
        }
        else
        {
            if (!string.Equals(player.Email, normalizedEmail, StringComparison.Ordinal))
            {
                player.Email = normalizedEmail;
                changed = true;
            }

            var displayName = claimedDisplayName.Trim();
            if (!string.IsNullOrWhiteSpace(displayName)
                && !string.Equals(player.DisplayName, displayName, StringComparison.Ordinal))
            {
                player.DisplayName = displayName;
                changed = true;
            }
        }

        if (changed)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        EnsureClaim(identity, ClaimsPrincipalExtensions.AuthenticatedActorPlayerIdClaimType, player.Id.ToString());
        if (!principal.HasClaim(claim => claim.Type == ClaimsPrincipalExtensions.EffectivePlayerIdClaimType))
        {
            EnsureClaim(identity, ClaimsPrincipalExtensions.EffectivePlayerIdClaimType, player.Id.ToString());
        }

        if (!principal.HasClaim(claim => claim.Type == ClaimsPrincipalExtensions.EffectivePlayerEmailClaimType))
        {
            EnsureClaim(identity, ClaimsPrincipalExtensions.EffectivePlayerEmailClaimType, player.Email);
        }

        if (!principal.HasClaim(claim => claim.Type == ClaimsPrincipalExtensions.EffectivePlayerNameClaimType))
        {
            EnsureClaim(identity, ClaimsPrincipalExtensions.EffectivePlayerNameClaimType, player.DisplayName);
        }

        if (!principal.HasClaim(claim => claim.Type == ClaimTypes.Role))
        {
            EnsureClaim(identity, ClaimTypes.Role, player.Role);
        }
    }

    private static void EnsureClaim(ClaimsIdentity identity, string type, string value)
    {
        if (!identity.HasClaim(type, value))
        {
            identity.AddClaim(new Claim(type, value));
        }
    }
}
