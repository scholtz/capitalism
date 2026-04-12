using Api.Data.Entities;

namespace Api.Types;

/// <summary>Payload for player ranking.</summary>
public sealed class PlayerRanking
{
    /// <summary>Player identifier.</summary>
    public Guid PlayerId { get; set; }

    /// <summary>Player display name.</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Total wealth = PersonalCash + SharesValue.
    /// See <see cref="Query.GetRankings"/> for the full valuation formula.
    /// </summary>
    public decimal TotalWealth { get; set; }

    /// <summary>Cash held in the player's personal account.</summary>
    public decimal PersonalCash { get; set; }

    /// <summary>Market value of all shares held by the player's personal account.</summary>
    public decimal SharesValue { get; set; }

    /// <summary>Number of companies owned.</summary>
    public int CompanyCount { get; set; }
}

/// <summary>Individual company ranking for the leaderboard.</summary>
public sealed class CompanyRanking
{
    /// <summary>Company identifier.</summary>
    public Guid CompanyId { get; set; }

    /// <summary>Company display name.</summary>
    public string CompanyName { get; set; } = string.Empty;

    /// <summary>Owner player identifier.</summary>
    public Guid PlayerId { get; set; }

    /// <summary>Owner player display name.</summary>
    public string OwnerDisplayName { get; set; } = string.Empty;

    /// <summary>Total company wealth = Cash + BuildingValue + InventoryValue.</summary>
    public decimal TotalWealth { get; set; }

    /// <summary>Cash on hand for this company.</summary>
    public decimal Cash { get; set; }

    /// <summary>Estimated value of company buildings.</summary>
    public decimal BuildingValue { get; set; }

    /// <summary>Estimated value of inventory in company buildings.</summary>
    public decimal InventoryValue { get; set; }

    /// <summary>Number of buildings owned by this company.</summary>
    public int BuildingCount { get; set; }
}

/// <summary>Payload for starter industries.</summary>
public sealed class StarterIndustriesPayload
{
    /// <summary>Available starter industry values.</summary>
    public List<string> Industries { get; set; } = [];
}

/// <summary>Type values for scheduled actions visible to the player.</summary>
public static class ScheduledActionType
{
    /// <summary>A queued building configuration upgrade (layout/unit change).</summary>
    public const string BuildingUpgrade = "BUILDING_UPGRADE";
}

/// <summary>Summary of a single pending scheduled action for the player.</summary>
public sealed class ScheduledActionSummary
{
    /// <summary>Unique identifier (matches the underlying plan or entity).</summary>
    public Guid Id { get; set; }

    /// <summary>Category of the scheduled action. See <see cref="ScheduledActionType"/>.</summary>
    public string ActionType { get; set; } = string.Empty;

    /// <summary>Building the action belongs to.</summary>
    public Guid BuildingId { get; set; }

    /// <summary>Human-readable building name for display in the UI.</summary>
    public string BuildingName { get; set; } = string.Empty;

    /// <summary>Building type string (e.g. FACTORY, SALES_SHOP).</summary>
    public string BuildingType { get; set; } = string.Empty;

    /// <summary>UTC timestamp when the action was submitted.</summary>
    public DateTime SubmittedAtUtc { get; set; }

    /// <summary>Game tick when the action was submitted.</summary>
    public long SubmittedAtTick { get; set; }

    /// <summary>Game tick when the action is scheduled to apply.</summary>
    public long AppliesAtTick { get; set; }

    /// <summary>Number of ticks remaining until the action applies.</summary>
    public long TicksRemaining { get; set; }

    /// <summary>Total ticks this action required from submission to application.</summary>
    public int TotalTicksRequired { get; set; }
}

/// <summary>Projected supply offer at a city's global exchange.</summary>
public sealed class GlobalExchangeOffer
{
    public Guid CityId { get; set; }
    public string CityName { get; set; } = string.Empty;
    public Guid ResourceTypeId { get; set; }
    public string ResourceName { get; set; } = string.Empty;
    public string ResourceSlug { get; set; } = string.Empty;
    public string UnitSymbol { get; set; } = string.Empty;
    public decimal LocalAbundance { get; set; }
    public decimal ExchangePricePerUnit { get; set; }
    public decimal EstimatedQuality { get; set; }
    public decimal TransitCostPerUnit { get; set; }
    public decimal DeliveredPricePerUnit { get; set; }
    public decimal DistanceKm { get; set; }
}

/// <summary>
/// A product marketplace listing from a player-placed SELL exchange order.
/// Represents a specific offer to sell a manufactured or intermediate product.
/// </summary>
public sealed class GlobalExchangeProductListing
{
    /// <summary>The exchange order ID backing this listing.</summary>
    public Guid OrderId { get; set; }

    /// <summary>The product type being offered.</summary>
    public Guid ProductTypeId { get; set; }

    /// <summary>Human-readable product name.</summary>
    public string ProductName { get; set; } = string.Empty;

    /// <summary>URL-friendly product identifier.</summary>
    public string ProductSlug { get; set; } = string.Empty;

    /// <summary>Industry category: FURNITURE, FOOD_PROCESSING, HEALTHCARE, etc.</summary>
    public string ProductIndustry { get; set; } = string.Empty;

    /// <summary>Short display symbol for the produced unit (e.g. pcs).</summary>
    public string UnitSymbol { get; set; } = string.Empty;

    /// <summary>Display name for the produced unit (e.g. Piece, Crate).</summary>
    public string UnitName { get; set; } = string.Empty;

    /// <summary>Base market price per unit from the product catalogue.</summary>
    public decimal BasePrice { get; set; }

    /// <summary>Asking price per unit for this specific listing.</summary>
    public decimal PricePerUnit { get; set; }

    /// <summary>Remaining quantity available in this order.</summary>
    public decimal RemainingQuantity { get; set; }

    /// <summary>City where the selling exchange building is located.</summary>
    public Guid SellerCityId { get; set; }

    /// <summary>Name of the seller's city.</summary>
    public string SellerCityName { get; set; } = string.Empty;

    /// <summary>Company that placed this sell order.</summary>
    public Guid SellerCompanyId { get; set; }

    /// <summary>Name of the selling company.</summary>
    public string SellerCompanyName { get; set; } = string.Empty;

    /// <summary>When this order was created.</summary>
    public DateTime CreatedAtUtc { get; set; }
}

/// <summary>
/// A single line in the shared in-game chat feed.
/// </summary>
public sealed class InGameChatMessage
{
    public Guid Id { get; set; }
    public Guid PlayerId { get; set; }
    public string PlayerDisplayName { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public DateTime SentAtUtc { get; set; }
    public bool IsOwnMessage { get; set; }
}

/// <summary>Inventory fill information for a single building unit.</summary>
public sealed class BuildingUnitInventorySummary
{
    public Guid BuildingUnitId { get; set; }
    public decimal Quantity { get; set; }
    public decimal Capacity { get; set; }
    public decimal FillPercent { get; set; }
    public decimal? AverageQuality { get; set; }
    public decimal TotalSourcingCost { get; set; }
    public decimal SourcingCostPerUnit { get; set; }
}

/// <summary>
/// City-level power balance snapshot.
/// Computed on demand from current building data.
/// </summary>
public sealed class CityPowerBalance
{
    /// <summary>The city this balance applies to.</summary>
    public Guid CityId { get; set; }

    /// <summary>Total power output in MW from all power plants in the city.</summary>
    public decimal TotalSupplyMw { get; set; }

    /// <summary>Total power demand in MW from all consuming buildings in the city.</summary>
    public decimal TotalDemandMw { get; set; }

    /// <summary>Reserve capacity in MW (supply minus demand; negative means shortage).</summary>
    public decimal ReserveMw { get; set; }

    /// <summary>Reserve as a percentage of demand (negative means shortage).</summary>
    public decimal ReservePercent { get; set; }

    /// <summary>
    /// Overall power status for the city:
    /// BALANCED = supply &gt;= demand,
    /// CONSTRAINED = supply &lt; demand but &gt;= 50%,
    /// CRITICAL = supply &lt; 50% of demand.
    /// </summary>
    public string Status { get; set; } = "BALANCED";

    /// <summary>Summary of each power plant in the city.</summary>
    public List<PowerPlantSummary> PowerPlants { get; set; } = [];

    /// <summary>Number of power plants in the city.</summary>
    public int PowerPlantCount { get; set; }

    /// <summary>Number of consuming buildings in the city.</summary>
    public int ConsumerBuildingCount { get; set; }
}

/// <summary>Summary of a single power plant building for the city power balance view.</summary>
public sealed class PowerPlantSummary
{
    /// <summary>Building identifier.</summary>
    public Guid BuildingId { get; set; }

    /// <summary>Building display name.</summary>
    public string BuildingName { get; set; } = string.Empty;

    /// <summary>Plant type: COAL, GAS, SOLAR, WIND, or NUCLEAR.</summary>
    public string PlantType { get; set; } = string.Empty;

    /// <summary>Current power output in MW.</summary>
    public decimal OutputMw { get; set; }

    /// <summary>Power supply status of this plant (always POWERED).</summary>
    public string PowerStatus { get; set; } = Data.Entities.PowerStatus.Powered;
}

/// <summary>
/// Snapshot of a company brand accumulated by R&amp;D research and marketing spend.
/// Exposed by the companyBrands query so the frontend can render research progress.
/// </summary>
public sealed class ResearchBrandState
{
    /// <summary>Brand identifier.</summary>
    public Guid Id { get; set; }

    /// <summary>Company that owns this brand.</summary>
    public Guid CompanyId { get; set; }

    /// <summary>Display name of the brand (product name or company name).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Scope: PRODUCT, CATEGORY, or COMPANY.</summary>
    public string Scope { get; set; } = BrandScope.Product;

    /// <summary>The specific product type this brand applies to (if scope is PRODUCT or CATEGORY).</summary>
    public Guid? ProductTypeId { get; set; }

    /// <summary>Human-readable product name for this brand (null for company-wide brands).</summary>
    public string? ProductName { get; set; }

    /// <summary>Industry category for CATEGORY-scoped brands.</summary>
    public string? IndustryCategory { get; set; }

    /// <summary>
    /// Brand awareness level (0.0–1.0). Driven by marketing unit spend.
    /// Higher awareness translates to more sales driven by brand recognition.
    /// </summary>
    public decimal Awareness { get; set; }

    /// <summary>
    /// Brand quality level (0.0–1.0). Driven by PRODUCT_QUALITY R&amp;D.
    /// Higher quality improves manufactured product output quality.
    /// </summary>
    public decimal Quality { get; set; }

    /// <summary>
    /// Marketing efficiency multiplier (≥ 1.0). Driven by BRAND_QUALITY R&amp;D.
    /// A value of 1.5 means each unit of marketing budget generates 50% more brand awareness than baseline.
    /// This is NOT a direct brand gain — it only amplifies the effect of marketing spend.
    /// </summary>
    public decimal MarketingEfficiencyMultiplier { get; set; } = 1m;
}

/// <summary>Phase values for the first-sale onboarding mission.</summary>
public static class FirstSaleMissionPhase
{
    /// <summary>Onboarding is not complete or no shop building is being tracked.</summary>
    public const string NoShop = "NO_SHOP";

    /// <summary>Shop exists but has at least one configuration blocker preventing the first sale.</summary>
    public const string ConfigureShop = "CONFIGURE_SHOP";

    /// <summary>Shop is fully configured; waiting for the next simulation tick to record a sale.</summary>
    public const string AwaitingFirstSale = "AWAITING_FIRST_SALE";

    /// <summary>A real PublicSalesRecord with QuantitySold &gt; 0 exists for the onboarding shop.</summary>
    public const string FirstSaleRecorded = "FIRST_SALE_RECORDED";

    /// <summary>The player has already acknowledged the first-sale milestone (OnboardingFirstSaleCompletedAtUtc is set).</summary>
    public const string AlreadyCompleted = "ALREADY_COMPLETED";
}

/// <summary>Blocker codes returned when the first-sale mission phase is CONFIGURE_SHOP.</summary>
public static class FirstSaleMissionBlocker
{
    /// <summary>The sales shop building is still under construction and cannot operate yet.</summary>
    public const string BuildingUnderConstruction = "BUILDING_UNDER_CONSTRUCTION";

    /// <summary>No PUBLIC_SALES unit is present in the shop building.</summary>
    public const string PublicSalesUnitMissing = "PUBLIC_SALES_UNIT_MISSING";

    /// <summary>The PUBLIC_SALES unit does not have a selling price set (MinPrice is null or zero).</summary>
    public const string PriceNotSet = "PRICE_NOT_SET";

    /// <summary>The PUBLIC_SALES unit has no inventory to sell yet (factory has not produced anything).</summary>
    public const string NoInventory = "NO_INVENTORY";
}

/// <summary>
/// Mission-status view model for the post-onboarding first-sale mission.
/// Returned by the <c>firstSaleMission</c> query.
/// </summary>
public sealed class FirstSaleMissionStatus
{
    /// <summary>
    /// Current phase of the first-sale mission.
    /// One of: NO_SHOP, CONFIGURE_SHOP, AWAITING_FIRST_SALE, FIRST_SALE_RECORDED, ALREADY_COMPLETED.
    /// </summary>
    public string Phase { get; set; } = FirstSaleMissionPhase.NoShop;

    /// <summary>The onboarding sales shop building ID being tracked (null when phase is NO_SHOP).</summary>
    public Guid? ShopBuildingId { get; set; }

    /// <summary>Display name of the onboarding sales shop (null when phase is NO_SHOP).</summary>
    public string? ShopName { get; set; }

    /// <summary>
    /// List of blocker codes explaining why the shop is not yet ready.
    /// Only populated when phase is CONFIGURE_SHOP.
    /// See <see cref="FirstSaleMissionBlocker"/> for possible values.
    /// </summary>
    public List<string> Blockers { get; set; } = [];

    /// <summary>Revenue from the first recorded sale (null until phase is FIRST_SALE_RECORDED).</summary>
    public decimal? FirstSaleRevenue { get; set; }

    /// <summary>Name of the product sold in the first sale (null until phase is FIRST_SALE_RECORDED).</summary>
    public string? FirstSaleProductName { get; set; }

    /// <summary>Game tick at which the first sale occurred (null until phase is FIRST_SALE_RECORDED).</summary>
    public long? FirstSaleTick { get; set; }

    /// <summary>Quantity sold in the first sale (null until phase is FIRST_SALE_RECORDED).</summary>
    public decimal? FirstSaleQuantity { get; set; }

    /// <summary>Price per unit in the first sale (null until phase is FIRST_SALE_RECORDED).</summary>
    public decimal? FirstSalePricePerUnit { get; set; }
}

/// <summary>
/// Read model for a media house building in a city.
/// Returned by the <c>cityMediaHouses</c> query.
/// </summary>
public sealed class CityMediaHouseInfo
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid CityId { get; set; }

    /// <summary>Channel type: NEWSPAPER, RADIO, TV. Null if not configured.</summary>
    public string? MediaType { get; set; }

    public Guid OwnerCompanyId { get; set; }
    public string OwnerCompanyName { get; set; } = string.Empty;

    /// <summary>
    /// Awareness multiplier applied when this media house is selected as the campaign channel.
    /// 1.0 = Newspaper, 1.5 = Radio, 2.0 = TV.
    /// </summary>
    public decimal EffectivenessMultiplier { get; set; }

    /// <summary>POWERED, CONSTRAINED, or OFFLINE.</summary>
    public string PowerStatus { get; set; } = Data.Entities.PowerStatus.Powered;

    public bool IsUnderConstruction { get; set; }
}

/// <summary>Read model for a loan offer visible to borrowers or bank owners.</summary>
public sealed class LoanOfferSummary
{
    public Guid Id { get; set; }
    public Guid BankBuildingId { get; set; }
    public string BankBuildingName { get; set; } = string.Empty;
    public Guid CityId { get; set; }
    public string CityName { get; set; } = string.Empty;
    public Guid LenderCompanyId { get; set; }
    public string LenderCompanyName { get; set; } = string.Empty;
    public decimal AnnualInterestRatePercent { get; set; }
    public decimal MaxPrincipalPerLoan { get; set; }
    public decimal TotalCapacity { get; set; }
    public decimal UsedCapacity { get; set; }
    public decimal RemainingCapacity { get; set; }
    public long DurationTicks { get; set; }
    public bool IsActive { get; set; }
    public long CreatedAtTick { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

/// <summary>Read model for an active or historical loan (borrower or lender view).</summary>
public sealed class LoanSummary
{
    public Guid Id { get; set; }
    public Guid LoanOfferId { get; set; }
    public Guid BorrowerCompanyId { get; set; }
    public string BorrowerCompanyName { get; set; } = string.Empty;
    public Guid LenderCompanyId { get; set; }
    public string LenderCompanyName { get; set; } = string.Empty;
    public Guid BankBuildingId { get; set; }
    public string BankBuildingName { get; set; } = string.Empty;
    public decimal OriginalPrincipal { get; set; }
    public decimal RemainingPrincipal { get; set; }
    public decimal AnnualInterestRatePercent { get; set; }
    public long DurationTicks { get; set; }
    public long StartTick { get; set; }
    public long DueTick { get; set; }
    public long NextPaymentTick { get; set; }
    public decimal PaymentAmount { get; set; }
    public int PaymentsMade { get; set; }
    public int TotalPayments { get; set; }
    public string Status { get; set; } = string.Empty;
    public int MissedPayments { get; set; }
    public decimal AccumulatedPenalty { get; set; }
    public DateTime AcceptedAtUtc { get; set; }
    public DateTime? ClosedAtUtc { get; set; }
}

/// <summary>Upgrade info for a single building unit: cost, timing, and stat projections.</summary>
public sealed class UnitUpgradeInfo
{
    public Guid UnitId { get; set; }
    public string UnitType { get; set; } = string.Empty;
    public int CurrentLevel { get; set; }
    public int NextLevel { get; set; }
    public bool IsMaxLevel { get; set; }
    public bool IsUpgradable { get; set; }
    public decimal UpgradeCost { get; set; }
    public int UpgradeTicks { get; set; }
    public decimal CurrentStat { get; set; }
    public decimal NextStat { get; set; }
    public string StatLabel { get; set; } = string.Empty;
}
