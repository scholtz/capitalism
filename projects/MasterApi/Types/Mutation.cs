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
            Player = ToProfile(player),
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
            Player = ToProfile(player),
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

        var activeSub = await db.ProSubscriptions
            .Where(s => s.PlayerAccountId == userId && s.Status == SubscriptionStatus.Active)
            .OrderByDescending(s => s.ExpiresAtUtc)
            .FirstOrDefaultAsync();

        if (activeSub is not null && activeSub.ExpiresAtUtc > now)
        {
            // Extend existing active subscription
            activeSub.ExpiresAtUtc = activeSub.ExpiresAtUtc.AddMonths(input.Months);
            activeSub.UpdatedAtUtc = now;
        }
        else
        {
            // Create a new subscription starting now
            var newSub = new ProSubscription
            {
                Id = Guid.NewGuid(),
                PlayerAccountId = userId,
                Tier = SubscriptionTier.Pro,
                Status = SubscriptionStatus.Active,
                StartsAtUtc = now,
                ExpiresAtUtc = now.AddMonths(input.Months),
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
            };
            db.ProSubscriptions.Add(newSub);
            activeSub = newSub;
        }

        await db.SaveChangesAsync();

        return Query.BuildSubscriptionInfo(activeSub, now);
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

    private static MasterPlayerProfile ToProfile(PlayerAccount player) =>
        new()
        {
            Id = player.Id,
            Email = player.Email,
            DisplayName = player.DisplayName,
            CreatedAtUtc = player.CreatedAtUtc,
        };

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
}