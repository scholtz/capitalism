using System.ComponentModel.DataAnnotations;

namespace Api.Types;

/// <summary>Input for player registration.</summary>
public sealed class RegisterInput
{
    /// <summary>Email address for the new account.</summary>
    [Required, MaxLength(256)]
    public string Email { get; set; } = string.Empty;

    /// <summary>Display name shown in game.</summary>
    [Required, MaxLength(100)]
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Password (minimum 8 characters).</summary>
    [Required, MinLength(8)]
    public string Password { get; set; } = string.Empty;
}

/// <summary>Input for player login.</summary>
public sealed class LoginInput
{
    /// <summary>Email address.</summary>
    [Required]
    public string Email { get; set; } = string.Empty;

    /// <summary>Password.</summary>
    [Required]
    public string Password { get; set; } = string.Empty;
}

/// <summary>Input for creating a new company.</summary>
public sealed class CreateCompanyInput
{
    /// <summary>Company name.</summary>
    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;
}

/// <summary>Input for placing a building on the map.</summary>
public sealed class PlaceBuildingInput
{
    /// <summary>Company that will own the building.</summary>
    public Guid CompanyId { get; set; }

    /// <summary>City where the building is placed.</summary>
    public Guid CityId { get; set; }

    /// <summary>Building type (MINE, FACTORY, SALES_SHOP, etc.).</summary>
    [Required, MaxLength(30)]
    public string Type { get; set; } = string.Empty;

    /// <summary>Display name for the building.</summary>
    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    /// <summary>First product to manufacture (for onboarding factory setup).</summary>
    public Guid? InitialProductTypeId { get; set; }
}

/// <summary>Input for selecting onboarding choices.</summary>
public sealed class OnboardingInput
{
    /// <summary>Industry the player wants to start with: FURNITURE, FOOD_PROCESSING, HEALTHCARE.</summary>
    [Required, MaxLength(50)]
    public string Industry { get; set; } = string.Empty;

    /// <summary>City where the player wants to start.</summary>
    public Guid CityId { get; set; }

    /// <summary>First product type to manufacture.</summary>
    public Guid ProductTypeId { get; set; }

    /// <summary>Name for the player's first company.</summary>
    [Required, MaxLength(200)]
    public string CompanyName { get; set; } = string.Empty;
}

/// <summary>Input for starting lot-based onboarding by purchasing the first factory lot.</summary>
public sealed class StartOnboardingCompanyInput
{
    /// <summary>Industry the player wants to start with: FURNITURE, FOOD_PROCESSING, HEALTHCARE.</summary>
    [Required, MaxLength(50)]
    public string Industry { get; set; } = string.Empty;

    /// <summary>City where the player wants to start.</summary>
    public Guid CityId { get; set; }

    /// <summary>Name for the player's first company.</summary>
    [Required, MaxLength(200)]
    public string CompanyName { get; set; } = string.Empty;

    /// <summary>Factory-capable lot chosen for the first building.</summary>
    public Guid FactoryLotId { get; set; }
}

/// <summary>Input for finishing lot-based onboarding by choosing a starter product and first shop lot.</summary>
public sealed class FinishOnboardingInput
{
    /// <summary>First product type to manufacture and sell.</summary>
    public Guid ProductTypeId { get; set; }

    /// <summary>Sales-shop-capable lot chosen for the first retail building.</summary>
    public Guid ShopLotId { get; set; }
}

/// <summary>Input for claiming the post-onboarding startup pack.</summary>
public sealed class ClaimStartupPackInput
{
    /// <summary>Company that should receive the startup capital grant.</summary>
    public Guid CompanyId { get; set; }
}

/// <summary>Input for storing a queued building configuration update.</summary>
public sealed class StoreBuildingConfigurationInput
{
    /// <summary>Building that should receive the queued configuration.</summary>
    public Guid BuildingId { get; set; }

    /// <summary>Queued unit layout snapshot. Server-controlled fields such as level and activation tick are not accepted.</summary>
    [Required]
    public List<BuildingConfigurationUnitInput> Units { get; set; } = [];
}

/// <summary>Input for cancelling a queued building configuration plan with rollback timing.</summary>
public sealed class CancelBuildingConfigurationInput
{
    /// <summary>Building whose pending configuration should be cancelled.</summary>
    public Guid BuildingId { get; set; }
}

/// <summary>Input for setting a building's for-sale status.</summary>
public sealed class SetBuildingForSaleInput
{
    /// <summary>Building to update.</summary>
    public Guid BuildingId { get; set; }

    /// <summary>Whether the building is listed for sale.</summary>
    public bool IsForSale { get; set; }

    /// <summary>Asking price (required when IsForSale is true).</summary>
    public decimal? AskingPrice { get; set; }
}

/// <summary>Input for purchasing a building lot and placing a building on it.</summary>
public sealed class PurchaseLotInput
{
    /// <summary>Company that will purchase the lot and own the building.</summary>
    public Guid CompanyId { get; set; }

    /// <summary>Building lot to purchase.</summary>
    public Guid LotId { get; set; }

    /// <summary>Building type to place on the lot (must be one of the lot's suitable types).</summary>
    [Required, MaxLength(30)]
    public string BuildingType { get; set; } = string.Empty;

    /// <summary>Display name for the new building.</summary>
    [Required, MaxLength(200)]
    public string BuildingName { get; set; } = string.Empty;
}

/// <summary>User-editable portion of a building unit configuration.</summary>
public sealed class BuildingConfigurationUnitInput
{
    /// <summary>Unit type for the grid cell.</summary>
    [Required, MaxLength(30)]
    public string UnitType { get; set; } = string.Empty;

    /// <summary>Grid column position (0-3).</summary>
    public int GridX { get; set; }

    /// <summary>Grid row position (0-3).</summary>
    public int GridY { get; set; }

    /// <summary>Whether the link to the unit above is active.</summary>
    public bool LinkUp { get; set; }

    /// <summary>Whether the link to the unit below is active.</summary>
    public bool LinkDown { get; set; }

    /// <summary>Whether the link to the unit on the left is active.</summary>
    public bool LinkLeft { get; set; }

    /// <summary>Whether the link to the unit on the right is active.</summary>
    public bool LinkRight { get; set; }

    /// <summary>Whether the diagonal link to the unit above-left is active.</summary>
    public bool LinkUpLeft { get; set; }

    /// <summary>Whether the diagonal link to the unit above-right is active.</summary>
    public bool LinkUpRight { get; set; }

    /// <summary>Whether the diagonal link to the unit below-left is active.</summary>
    public bool LinkDownLeft { get; set; }

    /// <summary>Whether the diagonal link to the unit below-right is active.</summary>
    public bool LinkDownRight { get; set; }

    // ── Unit-specific configuration ──

    /// <summary>Resource type this unit works with (optional, for Mining/Purchase/Storage/B2B Sales).</summary>
    public Guid? ResourceTypeId { get; set; }

    /// <summary>Product type this unit works with (optional, for Manufacturing/Purchase/Public Sales/Branding).</summary>
    public Guid? ProductTypeId { get; set; }

    /// <summary>Minimum selling price (for B2B Sales or Public Sales units).</summary>
    public decimal? MinPrice { get; set; }

    /// <summary>Maximum purchase price (for Purchase units).</summary>
    public decimal? MaxPrice { get; set; }

    /// <summary>Purchase source: EXCHANGE, LOCAL, OPTIMAL.</summary>
    [MaxLength(20)]
    public string? PurchaseSource { get; set; }

    /// <summary>Visibility: PUBLIC, COMPANY, GROUP.</summary>
    [MaxLength(20)]
    public string? SaleVisibility { get; set; }

    /// <summary>Marketing budget per tick (for Marketing units).</summary>
    public decimal? Budget { get; set; }

    /// <summary>Media house building ID (for Marketing units).</summary>
    public Guid? MediaHouseBuildingId { get; set; }

    /// <summary>Minimum product quality for Purchase units (0.0-1.0).</summary>
    public decimal? MinQuality { get; set; }

    /// <summary>Brand scope: PRODUCT, CATEGORY, COMPANY (for Branding units).</summary>
    [MaxLength(20)]
    public string? BrandScope { get; set; }

    /// <summary>Lock purchases to a specific vendor company ID (for Purchase units).</summary>
    public Guid? VendorLockCompanyId { get; set; }
}
