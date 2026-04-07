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

/// <summary>Input for updating company profile and salary settings.</summary>
public sealed class UpdateCompanySettingsInput
{
    public Guid CompanyId { get; set; }

    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Portion of post-tax annual profit paid out as dividends. 0.2 means 20%.</summary>
    public decimal? DividendPayoutRatio { get; set; }

    [Required]
    public List<CompanyCitySalarySettingInput> CitySalarySettings { get; set; } = [];
}

public sealed class CompanyCitySalarySettingInput
{
    public Guid CityId { get; set; }
    public decimal SalaryMultiplier { get; set; }
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

    /// <summary>
    /// Display name for the building.
    /// Optional — when empty or omitted, a natural name is auto-generated.
    /// </summary>
    [MaxLength(200)]
    public string? Name { get; set; }

    /// <summary>First product to manufacture (for onboarding factory setup).</summary>
    public Guid? InitialProductTypeId { get; set; }

    /// <summary>Media type for MEDIA_HOUSE buildings: NEWSPAPER, RADIO, TV. Required when Type is MEDIA_HOUSE.</summary>
    [MaxLength(20)]
    public string? MediaType { get; set; }
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

    /// <summary>
    /// Optional IPO raise target for the starter company. Supported values: 400000, 600000, 800000.
    /// When omitted, the onboarding flow defaults to the 400000 raise / 50% founder-share option.
    /// </summary>
    public decimal? IpoRaiseTarget { get; set; }
}

/// <summary>Input for finishing lot-based onboarding by choosing a starter product and first shop lot.</summary>
public sealed class FinishOnboardingInput
{
    /// <summary>First product type to manufacture and sell.</summary>
    public Guid ProductTypeId { get; set; }

    /// <summary>Sales-shop-capable lot chosen for the first retail building.</summary>
    public Guid ShopLotId { get; set; }
}

/// <summary>Input for switching the authenticated player's active acting account.</summary>
public sealed class SwitchAccountContextInput
{
    [Required, MaxLength(20)]
    public string AccountType { get; set; } = string.Empty;

    public Guid? CompanyId { get; set; }
}

/// <summary>Input for purchasing company shares from the public stock exchange.</summary>
public sealed class BuySharesInput
{
    public Guid CompanyId { get; set; }

    public decimal ShareCount { get; set; }
}

/// <summary>Input for selling company shares back to the public stock exchange.</summary>
public sealed class SellSharesInput
{
    public Guid CompanyId { get; set; }

    public decimal ShareCount { get; set; }
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

/// <summary>Input for setting the rent per m² on an apartment or commercial building.</summary>
public sealed class SetRentPerSqmInput
{
    /// <summary>Apartment or commercial building to configure.</summary>
    public Guid BuildingId { get; set; }

    /// <summary>New rent per m² to apply after one in-game day (24 ticks).</summary>
    public decimal RentPerSqm { get; set; }
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

    /// <summary>
    /// Display name for the new building.
    /// Optional — when empty or omitted, a natural name is auto-generated
    /// from the building type and a sequential number (e.g. "Factory #2").
    /// </summary>
    [MaxLength(200)]
    public string? BuildingName { get; set; }

    /// <summary>
    /// Power plant subtype (COAL, GAS, SOLAR, WIND, NUCLEAR).
    /// Required when BuildingType is POWER_PLANT; ignored otherwise.
    /// </summary>
    [MaxLength(20)]
    public string? PowerPlantType { get; set; }

    /// <summary>
    /// Media house channel type: NEWSPAPER, RADIO, TV.
    /// Required when BuildingType is MEDIA_HOUSE; ignored otherwise.
    /// </summary>
    [MaxLength(20)]
    public string? MediaType { get; set; }
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

    /// <summary>Lock exchange purchases to a specific source city ID (for Purchase units with EXCHANGE source).</summary>
    public Guid? LockedCityId { get; set; }
}

/// <summary>Input for publishing a new loan offer from a bank building.</summary>
public sealed class PublishLoanOfferInput
{
    /// <summary>The bank building that will publish the offer.</summary>
    public Guid BankBuildingId { get; set; }

    /// <summary>Annual interest rate as a percentage (e.g. 12.5 = 12.5%). Must be between 0.1 and 200.</summary>
    public decimal AnnualInterestRatePercent { get; set; }

    /// <summary>Maximum principal any single borrower can take (must be >= 1000).</summary>
    public decimal MaxPrincipalPerLoan { get; set; }

    /// <summary>Total capital committed to this offer across all borrowers (must be >= MaxPrincipalPerLoan).</summary>
    public decimal TotalCapacity { get; set; }

    /// <summary>Repayment duration in ticks (must be between 24 and 87600, i.e. 1 in-game day to 10 in-game years).</summary>
    public long DurationTicks { get; set; }
}

/// <summary>Input for updating an existing loan offer.</summary>
public sealed class UpdateLoanOfferInput
{
    /// <summary>ID of the loan offer to update.</summary>
    public Guid LoanOfferId { get; set; }

    /// <summary>Updated annual interest rate. Must be between 0.1 and 200.</summary>
    public decimal? AnnualInterestRatePercent { get; set; }

    /// <summary>Updated maximum principal per loan.</summary>
    public decimal? MaxPrincipalPerLoan { get; set; }

    /// <summary>Updated total capacity.</summary>
    public decimal? TotalCapacity { get; set; }

    /// <summary>Updated duration in ticks.</summary>
    public long? DurationTicks { get; set; }

    /// <summary>Whether the offer should be active (visible to borrowers).</summary>
    public bool? IsActive { get; set; }
}

/// <summary>Input for accepting a loan offer.</summary>
public sealed class AcceptLoanInput
{
    /// <summary>The loan offer to accept.</summary>
    public Guid LoanOfferId { get; set; }

    /// <summary>The company that will borrow the money.</summary>
    public Guid BorrowerCompanyId { get; set; }

    /// <summary>Principal amount to borrow (must be <= offer MaxPrincipalPerLoan and <= remaining capacity).</summary>
    public decimal PrincipalAmount { get; set; }
}

/// <summary>Input for instantly updating the minimum sale price on a PUBLIC_SALES unit.</summary>
public sealed class UpdatePublicSalesPriceInput
{
    /// <summary>The PUBLIC_SALES building unit to update.</summary>
    public Guid UnitId { get; set; }

    /// <summary>New minimum sale price per unit. Must be greater than zero.</summary>
    public decimal NewMinPrice { get; set; }
}

/// <summary>Input for flushing inventory from a storage-capable building unit.</summary>
public sealed class FlushStorageInput
{
    /// <summary>The building unit whose inventory should be discarded.</summary>
    public Guid BuildingUnitId { get; set; }
}

/// <summary>Input for scheduling a level upgrade on a building unit.</summary>
public sealed class ScheduleUnitUpgradeInput
{
    /// <summary>The building unit to upgrade.</summary>
    public Guid UnitId { get; set; }
}
