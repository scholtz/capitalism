using System.Security.Claims;

namespace Api.Security;

public static class ClaimsPrincipalExtensions
{
    public const string AuthenticatedActorPlayerIdClaimType = "capitalism/authenticated-actor-player-id";
    public const string EffectivePlayerIdClaimType = "capitalism/effective-player-id";
    public const string EffectivePlayerEmailClaimType = "capitalism/effective-player-email";
    public const string EffectivePlayerNameClaimType = "capitalism/effective-player-name";
    public const string EffectiveAccountTypeClaimType = "capitalism/effective-account-type";
    public const string EffectiveCompanyIdClaimType = "capitalism/effective-company-id";
    public const string EffectiveCompanyNameClaimType = "capitalism/effective-company-name";

    public static Guid GetRequiredUserId(this ClaimsPrincipal principal)
    {
        var value = principal.FindFirstValue(EffectivePlayerIdClaimType)
            ?? principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("The current user is missing an identifier claim.");

        return Guid.Parse(value);
    }

    public static Guid GetAuthenticatedActorUserId(this ClaimsPrincipal principal)
    {
        var value = principal.FindFirstValue(AuthenticatedActorPlayerIdClaimType)
            ?? principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("The current user is missing an identifier claim.");

        return Guid.Parse(value);
    }

    /// <summary>Returns the current user ID if authenticated, or null if not.</summary>
    public static Guid? GetUserId(this ClaimsPrincipal principal)
    {
        var value = principal.FindFirstValue(EffectivePlayerIdClaimType)
            ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (value is null) return null;
        if (Guid.TryParse(value, out var id)) return id;
        return null;
    }

    public static bool IsImpersonating(this ClaimsPrincipal principal)
    {
        var actorUserId = principal.GetAuthenticatedActorUserId();
        var effectiveUserId = principal.GetRequiredUserId();
        return actorUserId != effectiveUserId;
    }

    public static string? GetEffectiveAccountType(this ClaimsPrincipal principal)
        => principal.FindFirstValue(EffectiveAccountTypeClaimType);

    public static Guid? GetEffectiveCompanyId(this ClaimsPrincipal principal)
    {
        var value = principal.FindFirstValue(EffectiveCompanyIdClaimType);
        return Guid.TryParse(value, out var companyId) ? companyId : null;
    }

    public static string? GetEffectiveCompanyName(this ClaimsPrincipal principal)
        => principal.FindFirstValue(EffectiveCompanyNameClaimType);
}
