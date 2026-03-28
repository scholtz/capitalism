namespace Api.Types;

/// <summary>Top-level ledger summary for a company.</summary>
public sealed class CompanyLedgerSummary
{
    public Guid CompanyId { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    public decimal CurrentCash { get; set; }
    // Income Statement
    public decimal TotalRevenue { get; set; }
    public decimal TotalPurchasingCosts { get; set; }
    public decimal TotalMarketingCosts { get; set; }
    public decimal TotalTaxPaid { get; set; }
    public decimal TotalOtherCosts { get; set; }
    public decimal NetIncome { get; set; }
    // Balance Sheet
    public decimal BuildingValue { get; set; }
    public decimal InventoryValue { get; set; }
    public decimal TotalAssets { get; set; }
    public decimal TotalPropertyPurchases { get; set; }
    // Cash Flow
    public decimal CashFromOperations { get; set; }
    public decimal CashFromInvestments { get; set; }
    public long FirstRecordedTick { get; set; }
    public long LastRecordedTick { get; set; }
    public List<BuildingLedgerSummary> BuildingSummaries { get; set; } = [];
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
    /// <summary>Company name or "Market" for unmet demand.</summary>
    public string Label { get; set; } = string.Empty;
    public Guid? CompanyId { get; set; }
    public decimal Share { get; set; }
}
