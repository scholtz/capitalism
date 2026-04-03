using System.ComponentModel.DataAnnotations;

namespace MasterApi.Security;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    /// <summary>
    /// The default signing key shipped in appsettings.json for local development only.
    /// Any other environment that still has this value will be rejected at startup.
    /// </summary>
    public const string DefaultSigningKey = "ChangeThisSigningKeyBeforeProduction123!";

    [Required]
    public string Issuer { get; init; } = "MasterApi";

    [Required]
    public string Audience { get; init; } = "MasterFrontend";

    [Required]
    [MinLength(32)]
    public string SigningKey { get; init; } = DefaultSigningKey;

    [Range(5, 1440)]
    public int ExpiresMinutes { get; init; } = 120;
}
