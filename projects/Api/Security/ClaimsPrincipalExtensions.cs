using System.Security.Claims;

namespace Api.Security;

public static class ClaimsPrincipalExtensions
{
    public static Guid GetRequiredUserId(this ClaimsPrincipal principal)
    {
        var value = principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("The current user is missing an identifier claim.");

        return Guid.Parse(value);
    }

    /// <summary>Returns the current user ID if authenticated, or null if not.</summary>
    public static Guid? GetUserId(this ClaimsPrincipal principal)
    {
        var value = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (value is null) return null;
        if (Guid.TryParse(value, out var id)) return id;
        return null;
    }
}
