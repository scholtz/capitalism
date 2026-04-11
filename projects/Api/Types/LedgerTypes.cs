namespace Api.Types;

/// <summary>Top-level ledger summary for a company.</summary>
public sealed class CompanyLedgerSummary
{
    public Guid CompanyId { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    public int GameYear { get; set; }
    public bool IsCurrentGameYear { get; set; }
    public decimal CurrentCash { get; set; }
    // Income Statement
    public decimal TotalRevenue { get; set; }
    public decimal TotalPurchasingCosts { get; set; }
    public decimal TotalShippingCosts { get; set; }
    public decimal TotalLaborCosts { get; set; }
    public decimal TotalEnergyCosts { get; set; }
    public decimal TotalMarketingCosts { get; set; }
    public decimal TotalTaxPaid { get; set; }
    public decimal TotalOtherCosts { get; set; }
    public decimal TaxableIncome { get; set; }
    public decimal EstimatedIncomeTax { get; set; }
    public decimal NetIncome { get; set; }
    // Balance Sheet
    public decimal PropertyValue { get; set; }
    public decimal PropertyAppreciation { get; set; }
    public decimal BuildingValue { get; set; }
    public decimal InventoryValue { get; set; }
    public decimal TotalAssets { get; set; }
    public decimal TotalPropertyPurchases { get; set; }
    public decimal TotalStockPurchaseCashOut { get; set; }
    public decimal TotalStockSaleCashIn { get; set; }
    // Cash Flow
    public decimal CashFromOperations { get; set; }
    public decimal CashFromInvestments { get; set; }
    public long FirstRecordedTick { get; set; }
    public long LastRecordedTick { get; set; }
    public long IncomeTaxDueAtTick { get; set; }
    public DateTime IncomeTaxDueGameTimeUtc { get; set; }
    public int IncomeTaxDueGameYear { get; set; }
    public bool IsIncomeTaxSettled { get; set; }
    public List<BuildingLedgerSummary> BuildingSummaries { get; set; } = [];
    public List<CompanyLedgerHistoryYear> History { get; set; } = [];
}

public sealed class CompanyLedgerHistoryYear
{
    public int GameYear { get; set; }
    public bool IsCurrentGameYear { get; set; }
    public decimal TotalRevenue { get; set; }
    public decimal TotalLaborCosts { get; set; }
    public decimal TotalEnergyCosts { get; set; }
    public decimal NetIncome { get; set; }
    public decimal TotalTaxPaid { get; set; }
    public decimal TaxableIncome { get; set; }
    public decimal EstimatedIncomeTax { get; set; }
    public long FirstRecordedTick { get; set; }
    public long LastRecordedTick { get; set; }
}

public sealed class BuildingLedgerSummary
{
    public Guid BuildingId { get; set; }
    public string BuildingName { get; set; } = string.Empty;
    public string BuildingType { get; set; } = string.Empty;
    public decimal Revenue { get; set; }
    public decimal Costs { get; set; }
}

public sealed class BuildingFinancialTimeline
{
    public Guid BuildingId { get; set; }
    public string BuildingName { get; set; } = string.Empty;
    public long DataFromTick { get; set; }
    public long DataToTick { get; set; }
    public decimal TotalSales { get; set; }
    public decimal TotalCosts { get; set; }
    public decimal TotalProfit { get; set; }
    public List<BuildingFinancialTickSnapshot> Timeline { get; set; } = [];
}

public sealed class BuildingFinancialTickSnapshot
{
    public long Tick { get; set; }
    public decimal Sales { get; set; }
    public decimal Costs { get; set; }
    public decimal Profit { get; set; }
}

public sealed class LedgerEntryResult
{
    public Guid Id { get; set; }
    public string Category { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public long RecordedAtTick { get; set; }
    public DateTime RecordedAtUtc { get; set; }
    public Guid? BuildingId { get; set; }
    public string? BuildingName { get; set; }
    public Guid? BuildingUnitId { get; set; }
    public Guid? ProductTypeId { get; set; }
    public string? ProductName { get; set; }
    public Guid? ResourceTypeId { get; set; }
    public string? ResourceName { get; set; }
}

public sealed class PublicSalesAnalytics
{
    public Guid BuildingUnitId { get; set; }
    public Guid BuildingId { get; set; }
    public string BuildingName { get; set; } = string.Empty;
    public string CityName { get; set; } = string.Empty;
    /// <summary>Product type ID being tracked by this unit. Null when no product has been configured or sold.</summary>
    public Guid? ProductTypeId { get; set; }
    /// <summary>Display name of the product being sold. Null when no product data is available.</summary>
    public string? ProductName { get; set; }
    public decimal TotalRevenue { get; set; }
    public decimal TotalQuantitySold { get; set; }
    public decimal AveragePricePerUnit { get; set; }
    public decimal CurrentSalesCapacity { get; set; }
    public long DataFromTick { get; set; }
    public long DataToTick { get; set; }
    public List<SalesTickSnapshot> RevenueHistory { get; set; } = [];
    public List<MarketShareEntry> MarketShare { get; set; } = [];
    public List<PriceTickSnapshot> PriceHistory { get; set; } = [];
    /// <summary>
    /// Revenue trend direction comparing the most-recent 5 ticks vs the prior 5 ticks.
    /// Values: UP | FLAT | DOWN | NO_DATA (fewer than 2 ticks of history).
    /// </summary>
    public string TrendDirection { get; set; } = "NO_DATA";
    /// <summary>
    /// Demand/elasticity signal derived from recent sales data.
    /// Values: NO_DATA | SUPPLY_CONSTRAINED | STRONG | MODERATE | WEAK
    /// </summary>
    public string DemandSignal { get; set; } = "NO_DATA";
    /// <summary>Short player-facing recommended action based on current market conditions.</summary>
    public string ActionHint { get; set; } = string.Empty;
    /// <summary>Average demand signal across recent ticks (0-1 ratio vs sales capacity).</summary>
    public decimal RecentUtilization { get; set; }
    /// <summary>
    /// Price elasticity index approximated from the demand model.
    /// Defined as (dQ/Q) / (dP/P) at the current price point.
    /// Negative values indicate normal goods (higher price → lower demand).
    /// Typical range: -3.0 (very elastic) to 0.0 (perfectly inelastic).
    /// Null when insufficient data or no base price available.
    /// </summary>
    public decimal? ElasticityIndex { get; set; }
    /// <summary>
    /// Fraction of estimated total city demand that was unserved (demand > total sold by all players).
    /// 0 = all demand satisfied. 1 = all demand unmet. Null when no demand data available.
    /// </summary>
    public decimal? UnmetDemandShare { get; set; }
    /// <summary>Population index of the building lot (1.0 = city average). Higher is better for sales.</summary>
    public decimal? PopulationIndex { get; set; }
    /// <summary>Current inventory quality of the product in this unit (0.0–1.0). Affects demand directly.</summary>
    public decimal? InventoryQuality { get; set; }
    /// <summary>Brand awareness of the selling company for this product (0.0–1.0). Null if no brand set.</summary>
    public decimal? BrandAwareness { get; set; }
    /// <summary>
    /// Total gross profit across the analytics window (TotalRevenue - QuantitySold × BasePrice).
    /// Positive when selling above the product base price. Null when base price is unavailable.
    /// </summary>
    public decimal? TotalProfit { get; set; }
    /// <summary>Per-tick gross profit history, ordered by tick ascending. Null when base price is unavailable.</summary>
    public List<ProfitTickSnapshot>? ProfitHistory { get; set; }
    /// <summary>
    /// Ordered list of demand drivers (positive and negative) explaining the current sales outcome.
    /// Each entry carries a factor name, impact direction, score, and player-facing description.
    /// </summary>
    public List<DemandDriverEntry> DemandDrivers { get; set; } = [];
    /// <summary>
    /// Current market trend factor for this product in this city.
    /// Range [0.5, 1.5]; 1.0 = neutral. Values above 1.0 indicate a hot market
    /// (trend is boosting demand); values below 1.0 indicate a cold market.
    /// Null when no trend state exists yet (first tick).
    /// </summary>
    public decimal? TrendFactor { get; set; }
}

public sealed class ProfitTickSnapshot
{
    public long Tick { get; set; }
    /// <summary>Gross profit for the tick (revenue − quantity × basePrice).</summary>
    public decimal Profit { get; set; }
    /// <summary>Gross margin percentage (0–100+). Null when basePrice is zero.</summary>
    public decimal? GrossMarginPct { get; set; }
}

/// <summary>
/// Explains a single demand-influencing factor for a public sales unit.
/// Factors: PRICE | QUALITY | BRAND | LOCATION | SATURATION | COMPETITION
/// Impact:  POSITIVE | NEUTRAL | NEGATIVE
/// Score:   0.0 – 1.0 (strength of the factor, regardless of direction).
/// </summary>
public sealed class DemandDriverEntry
{
    /// <summary>Factor identifier: PRICE | QUALITY | BRAND | LOCATION | SATURATION | COMPETITION</summary>
    public string Factor { get; set; } = string.Empty;
    /// <summary>POSITIVE | NEUTRAL | NEGATIVE</summary>
    public string Impact { get; set; } = string.Empty;
    /// <summary>Strength of the factor (0.0–1.0).</summary>
    public decimal Score { get; set; }
    /// <summary>Short player-facing description of this driver.</summary>
    public string Description { get; set; } = string.Empty;
}

public sealed class SalesTickSnapshot
{
    public long Tick { get; set; }
    public decimal Revenue { get; set; }
    public decimal QuantitySold { get; set; }
}

public sealed class PriceTickSnapshot
{
    public long Tick { get; set; }
    public decimal PricePerUnit { get; set; }
}

public sealed class MarketShareEntry
{
    /// <summary>Company name, or "Unmet Demand" for unsatisfied city demand.</summary>
    public string Label { get; set; } = string.Empty;
    public Guid? CompanyId { get; set; }
    public decimal Share { get; set; }
    /// <summary>True when this entry represents unserved/unmet market demand, not an actual seller.</summary>
    public bool IsUnmet { get; set; }
}

/// <summary>
/// Product-level analytics for a MANUFACTURING unit.
/// Shows cost history, production quantity, and estimated economics per tick.
/// </summary>
public sealed class UnitProductAnalytics
{
    public Guid BuildingUnitId { get; set; }
    /// <summary>The unit type (e.g. MANUFACTURING).</summary>
    public string UnitType { get; set; } = string.Empty;
    /// <summary>Product type being produced. Null when no product is configured.</summary>
    public Guid? ProductTypeId { get; set; }
    /// <summary>Display name of the product being produced. Null when no product data is available.</summary>
    public string? ProductName { get; set; }
    /// <summary>First tick in the analytics window (oldest data).</summary>
    public long DataFromTick { get; set; }
    /// <summary>Last tick in the analytics window (most recent data).</summary>
    public long DataToTick { get; set; }
    /// <summary>Total labor + energy cost over the analytics window.</summary>
    public decimal TotalCost { get; set; }
    /// <summary>Total units produced over the analytics window.</summary>
    public decimal TotalQuantityProduced { get; set; }
    /// <summary>
    /// Estimated total revenue = TotalQuantityProduced × product.BasePrice.
    /// Null when base price is unavailable.
    /// </summary>
    public decimal? EstimatedRevenue { get; set; }
    /// <summary>
    /// Estimated total profit = EstimatedRevenue − TotalCost.
    /// Null when base price is unavailable.
    /// </summary>
    public decimal? EstimatedProfit { get; set; }
    /// <summary>Per-tick snapshots ordered by tick ascending.</summary>
    public List<UnitProductTickSnapshot> Snapshots { get; set; } = [];
}

/// <summary>Per-tick cost and production snapshot for a MANUFACTURING unit.</summary>
public sealed class UnitProductTickSnapshot
{
    public long Tick { get; set; }
    /// <summary>Labor cost charged to the company for this unit on this tick.</summary>
    public decimal LaborCost { get; set; }
    /// <summary>Energy cost charged to the company for this unit on this tick.</summary>
    public decimal EnergyCost { get; set; }
    /// <summary>Total operating cost (labor + energy) for this tick.</summary>
    public decimal TotalCost { get; set; }
    /// <summary>Quantity of the product produced on this tick.</summary>
    public decimal QuantityProduced { get; set; }
    /// <summary>
    /// Estimated revenue = QuantityProduced × product.BasePrice.
    /// Null when base price is unavailable.
    /// </summary>
    public decimal? EstimatedRevenue { get; set; }
    /// <summary>
    /// Estimated profit = EstimatedRevenue − TotalCost.
    /// Null when base price is unavailable.
    /// </summary>
    public decimal? EstimatedProfit { get; set; }
}
