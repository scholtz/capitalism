using Api.Data.Entities;

namespace Api.Types;

/// <summary>Auth response payload.</summary>
public sealed class AuthPayload
{
    /// <summary>JWT bearer token.</summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>Token expiration time in UTC.</summary>
    public DateTime ExpiresAtUtc { get; set; }

    /// <summary>Authenticated player profile.</summary>
    public Player Player { get; set; } = null!;
}

/// <summary>Onboarding completion result.</summary>
public sealed class OnboardingResult
{
    /// <summary>The newly created company.</summary>
    public Company Company { get; set; } = null!;

    /// <summary>The factory building with default units.</summary>
    public Building Factory { get; set; } = null!;

    /// <summary>The sales shop building with default units.</summary>
    public Building SalesShop { get; set; } = null!;

    /// <summary>The product selected for manufacturing.</summary>
    public ProductType SelectedProduct { get; set; } = null!;
}

/// <summary>Result of purchasing the first factory during lot-based onboarding.</summary>
public sealed class OnboardingStartResult
{
    /// <summary>The newly created company.</summary>
    public Company Company { get; set; } = null!;

    /// <summary>The first factory placed on the selected lot.</summary>
    public Building Factory { get; set; } = null!;

    /// <summary>The lot that now hosts the player's first factory.</summary>
    public BuildingLot FactoryLot { get; set; } = null!;

    /// <summary>The next onboarding step the client should guide the player to.</summary>
    public string NextStep { get; set; } = string.Empty;
}

/// <summary>Result of purchasing a building lot.</summary>
public sealed class PurchaseLotResult
{
    /// <summary>The purchased lot with updated ownership.</summary>
    public BuildingLot Lot { get; set; } = null!;

    /// <summary>The building placed on the lot.</summary>
    public Building Building { get; set; } = null!;

    /// <summary>The company with updated cash balance.</summary>
    public Company Company { get; set; } = null!;
}
