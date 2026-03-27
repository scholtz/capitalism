using System.ComponentModel.DataAnnotations;

namespace Api.Data.Entities;

/// <summary>
/// Represents a registered game player who can own companies and buildings.
/// Maps to the "Players" table.
/// </summary>
public sealed class Player
{
    /// <summary>Unique identifier for the player.</summary>
    public Guid Id { get; set; }

    /// <summary>Email address used for authentication. Must be unique.</summary>
    [Required, MaxLength(256)]
    public string Email { get; set; } = string.Empty;

    /// <summary>Display name shown in the game UI and rankings.</summary>
    [Required, MaxLength(100)]
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Hashed password for authentication.</summary>
    [Required]
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>Player role: PLAYER or ADMIN.</summary>
    [Required, MaxLength(20)]
    public string Role { get; set; } = PlayerRole.Player;

    /// <summary>UTC timestamp when the player registered.</summary>
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>UTC timestamp of the player's last login.</summary>
    public DateTime? LastLoginAtUtc { get; set; }

    /// <summary>UTC timestamp when the player completed the onboarding journey. Null if not yet completed.</summary>
    public DateTime? OnboardingCompletedAtUtc { get; set; }

    /// <summary>Companies owned by this player.</summary>
    public ICollection<Company> Companies { get; set; } = [];
}

/// <summary>Defines the available player roles.</summary>
public static class PlayerRole
{
    public const string Player = "PLAYER";
    public const string Admin = "ADMIN";
}
