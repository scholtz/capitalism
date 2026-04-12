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
    private static AuthenticatedSession GenerateToken(
        Player player,
        JwtOptions options,
        AdminImpersonationTokenContext? impersonation = null)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.SigningKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var expires = DateTime.UtcNow.AddMinutes(options.ExpiresMinutes);

        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, player.Id.ToString()),
            new Claim(ClaimTypes.Email, player.Email),
            new Claim(ClaimTypes.Name, player.DisplayName),
            new Claim(ClaimTypes.Role, player.Role)
        };

        if (impersonation is not null)
        {
            claims.Add(new Claim(ClaimsPrincipalExtensions.EffectivePlayerIdClaimType, impersonation.EffectivePlayer.Id.ToString()));
            claims.Add(new Claim(ClaimsPrincipalExtensions.EffectivePlayerEmailClaimType, impersonation.EffectivePlayer.Email));
            claims.Add(new Claim(ClaimsPrincipalExtensions.EffectivePlayerNameClaimType, impersonation.EffectivePlayer.DisplayName));
            claims.Add(new Claim(ClaimsPrincipalExtensions.EffectiveAccountTypeClaimType, impersonation.EffectiveAccountType));

            if (impersonation.EffectiveCompanyId.HasValue)
            {
                claims.Add(new Claim(ClaimsPrincipalExtensions.EffectiveCompanyIdClaimType, impersonation.EffectiveCompanyId.Value.ToString()));
            }

            if (!string.IsNullOrWhiteSpace(impersonation.EffectiveCompanyName))
            {
                claims.Add(new Claim(ClaimsPrincipalExtensions.EffectiveCompanyNameClaimType, impersonation.EffectiveCompanyName));
            }
        }

        var token = new JwtSecurityToken(
            issuer: options.Issuer,
            audience: options.Audience,
            claims: claims,
            expires: expires,
            signingCredentials: credentials);

        return new AuthenticatedSession(
            new JwtSecurityTokenHandler().WriteToken(token),
            expires);
    }

    private sealed record ImpersonationAccountContext(
        string EffectiveAccountType,
        Guid? EffectiveCompanyId,
        string? EffectiveCompanyName);

    private sealed record AdminImpersonationTokenContext(
        Player EffectivePlayer,
        string EffectiveAccountType,
        Guid? EffectiveCompanyId,
        string? EffectiveCompanyName);
}
