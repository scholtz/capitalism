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

public sealed partial class Mutation
{
    private const int StartupPackDurationMonths = 3;
    internal const string PersonalAccountNameClaimType = "capitalism/personal-account-name";
    private const int MinPersonalAccountNameLength = 3;
    private const int MaxPersonalAccountNameLength = 60;


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

        var effectiveName = string.IsNullOrWhiteSpace(player.PersonalAccountName)
            ? player.DisplayName
            : player.PersonalAccountName;

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, player.Id.ToString()),
            new Claim(ClaimTypes.Email, player.Email),
            new Claim(ClaimTypes.Name, effectiveName),
            new Claim(PersonalAccountNameClaimType, effectiveName),
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

    internal static string NormalizePersonalAccountName(string personalAccountName)
    {
        var normalized = string.Join(' ', personalAccountName
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Personal account name is required.")
                    .SetCode("PERSONAL_ACCOUNT_NAME_REQUIRED")
                    .Build());
        }

        if (normalized.Length < MinPersonalAccountNameLength)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Personal account name is too short.")
                    .SetCode("PERSONAL_ACCOUNT_NAME_TOO_SHORT")
                    .Build());
        }

        if (normalized.Length > MaxPersonalAccountNameLength)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Personal account name is too long.")
                    .SetCode("PERSONAL_ACCOUNT_NAME_TOO_LONG")
                    .Build());
        }

        if (normalized.Any(character =>
            !char.IsLetter(character)
            && !char.IsWhiteSpace(character)
            && character != '\''
            && character != '-'))
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Personal account name contains invalid characters.")
                    .SetCode("PERSONAL_ACCOUNT_NAME_INVALID_CHARACTERS")
                    .Build());
        }

        return normalized;
    }


}
