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

    [MaxLength(120)]
    public string? PersonalAccountName { get; set; }

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

    /// <summary>When true, the player's chat messages are only visible to themselves and administrators.</summary>
    public bool IsInvisibleInChat { get; set; }

    /// <summary>Cash held in the player's personal account outside any company.</summary>
    public decimal PersonalCash { get; set; }

    /// <summary>UTC timestamp when the player completed the onboarding journey. Null if not yet completed.</summary>
    public DateTime? OnboardingCompletedAtUtc { get; set; }

    /// <summary>UTC timestamp until which the player's Pro subscription remains active. Null when inactive.</summary>
    public DateTime? ProSubscriptionEndsAtUtc { get; set; }

    /// <summary>Application-managed concurrency token for optimistic concurrency control.</summary>
    public Guid ConcurrencyToken { get; set; } = Guid.NewGuid();

    /// <summary>The currently selected acting account context: PERSON or COMPANY.</summary>
    public string ActiveAccountType { get; set; } = AccountContextType.Person;

    /// <summary>The company currently selected when <see cref="ActiveAccountType"/> is COMPANY.</summary>
    public Guid? ActiveCompanyId { get; set; }

    /// <summary>Current resumable onboarding step. Null when onboarding has not started or is completed.</summary>
    public string? OnboardingCurrentStep { get; set; }

    /// <summary>Starter industry chosen for an in-progress onboarding journey.</summary>
    public string? OnboardingIndustry { get; set; }

    /// <summary>Selected city for an in-progress onboarding journey.</summary>
    public Guid? OnboardingCityId { get; set; }

    /// <summary>Company being created during a resumable onboarding journey.</summary>
    public Guid? OnboardingCompanyId { get; set; }

    /// <summary>Factory lot already acquired during an in-progress onboarding journey.</summary>
    public Guid? OnboardingFactoryLotId { get; set; }

    /// <summary>
    /// Sales shop building created at the end of the onboarding journey.
    /// Used to resume the post-completion first-sale guidance step after a refresh.
    /// Cleared when <see cref="OnboardingFirstSaleCompletedAtUtc"/> is set.
    /// </summary>
    public Guid? OnboardingShopBuildingId { get; set; }

    /// <summary>UTC timestamp when the player completed the first-sale/first-profit onboarding milestone. Null when not yet achieved.</summary>
    public DateTime? OnboardingFirstSaleCompletedAtUtc { get; set; }

    /// <summary>Companies owned by this player.</summary>
    public ICollection<Company> Companies { get; set; } = new List<Company>();

    /// <summary>Share positions held directly by the player's personal account.</summary>
    public ICollection<Shareholding> Shareholdings { get; set; } = new List<Shareholding>();

    /// <summary>Dividend payments received directly into the player's personal account.</summary>
    public ICollection<DividendPayment> DividendPayments { get; set; } = new List<DividendPayment>();
}

/// <summary>Defines the available player roles.</summary>
public static class PlayerRole
{
    public const string Player = "PLAYER";
    public const string Admin = "ADMIN";
}

/// <summary>Defines the available acting account contexts for a player session.</summary>
public static class AccountContextType
{
    public const string Person = "PERSON";
    public const string Company = "COMPANY";
}

/// <summary>Resumable onboarding step identifiers stored on the player profile.</summary>
public static class OnboardingProgressStep
{
    public const string ShopSelection = "SHOP_SELECTION";
}
