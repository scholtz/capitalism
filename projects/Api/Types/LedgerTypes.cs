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
