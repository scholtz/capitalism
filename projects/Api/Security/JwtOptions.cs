using System.ComponentModel.DataAnnotations;

namespace Api.Security;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    [Required]
    public string Issuer { get; init; } = "Capitalism";

    [Required]
    public string Audience { get; init; } = "Capitalism";

    [Required]
    [MinLength(32)]
    public string SigningKey { get; init; } = "ChangeThisSigningKeyBeforeProduction123!";

    [Range(5, 1440)]
    public int ExpiresMinutes { get; init; } = 120;
}
