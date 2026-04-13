namespace Api.Types;

public sealed class BuildingUnitInventory
{
    public Guid Id { get; set; }
    public Guid BuildingUnitId { get; set; }
    public Guid? ResourceTypeId { get; set; }
    public Guid? ProductTypeId { get; set; }
    public decimal Quantity { get; set; }
    public decimal SourcingCostTotal { get; set; }
    public decimal SourcingCostPerUnit { get; set; }
    public decimal Quality { get; set; }
}

public sealed class BuildingUnitResourceHistoryPoint
{
    public Guid BuildingUnitId { get; set; }
    public Guid? ResourceTypeId { get; set; }
    public Guid? ProductTypeId { get; set; }
    public long Tick { get; set; }
    public decimal InflowQuantity { get; set; }
    public decimal OutflowQuantity { get; set; }
    public decimal ConsumedQuantity { get; set; }
    public decimal ProducedQuantity { get; set; }
}

/// <summary>
/// Operational status for a single building unit: whether it is actively
/// processing, idle, blocked due to a missing input, or not yet configured.
/// </summary>
public sealed class BuildingUnitOperationalStatus
{
    public Guid BuildingUnitId { get; set; }

    /// <summary>
    /// Machine-readable status code.
    /// Values: ACTIVE, IDLE, BLOCKED, FULL, UNCONFIGURED.
    /// </summary>
    public string Status { get; set; } = "IDLE";

    /// <summary>
    /// Machine-readable blocked reason code, null when Status is not BLOCKED.
    /// Values: NO_INPUTS, OUTPUT_FULL, NO_INVENTORY, PRICE_TOO_HIGH,
    ///         UNCONFIGURED, AWAITING_STOCK, NO_DEMAND.
    /// </summary>
    public string? BlockedCode { get; set; }

    /// <summary>Human-readable explanation shown to the player.</summary>
    public string? BlockedReason { get; set; }

    /// <summary>Number of consecutive ticks the unit has had no activity.</summary>
    public int IdleTicks { get; set; }

    /// <summary>
    /// Estimated base labor cost charged against the company each tick for this unit,
    /// in game currency. Computed from the unit's base manhours and the company's
    /// effective hourly wage for the building's city. Null if the unit type carries
    /// no labor cost (e.g. unconfigured or zero-labor unit).
    /// Note: does not include the administration overhead applied to manufacturing units.
    /// </summary>
    public decimal? NextTickLaborCost { get; set; }

    /// <summary>
    /// Estimated energy cost charged against the company each tick for this unit,
    /// in game currency. Computed from the unit's base energy consumption and the
    /// fixed energy price per MWh. Null if the unit type carries no energy cost.
    /// </summary>
    public decimal? NextTickEnergyCost { get; set; }
}

/// <summary>Result of evaluating a purchase unit's next-tick behavior without executing it.</summary>
public sealed class ProcurementPreview
{
    /// <summary>The type of source that would be used.</summary>
    public string SourceType { get; set; } = ProcurementSourceType.NoSource;

    /// <summary>Source city ID (for global exchange sources).</summary>
    public Guid? SourceCityId { get; set; }

    /// <summary>Human-readable source city name.</summary>
    public string? SourceCityName { get; set; }

    /// <summary>Vendor company ID (for local or vendor-locked sources).</summary>
    public Guid? SourceVendorCompanyId { get; set; }

    /// <summary>Human-readable vendor name.</summary>
    public string? SourceVendorName { get; set; }

    /// <summary>Exchange price per unit at the source city (before transit).</summary>
    public decimal? ExchangePricePerUnit { get; set; }

    /// <summary>Transit cost per unit from source city to destination.</summary>
    public decimal? TransitCostPerUnit { get; set; }

    /// <summary>Total delivered price per unit (exchange price + transit cost).</summary>
    public decimal? DeliveredPricePerUnit { get; set; }

    /// <summary>Estimated quality of the goods that would be purchased (0–1).</summary>
    public decimal? EstimatedQuality { get; set; }

    /// <summary>Whether the purchase would execute on the next tick.</summary>
    public bool CanExecute { get; set; }

    /// <summary>Machine-readable block reason code.</summary>
    public string? BlockReason { get; set; }

    /// <summary>Human-readable explanation of why the purchase is blocked.</summary>
    public string? BlockMessage { get; set; }
}

/// <summary>Well-known source type strings for <see cref="ProcurementPreview"/>.</summary>
public static class ProcurementSourceType
{
    public const string GlobalExchange = "GLOBAL_EXCHANGE";
    public const string PlayerExchangeOrder = "PLAYER_EXCHANGE_ORDER";
    public const string LocalB2B = "LOCAL_B2B";
    public const string LockedVendor = "LOCKED_VENDOR";
    public const string NoSource = "NO_SOURCE";
}

/// <summary>Well-known block reason strings for <see cref="ProcurementPreview"/>.</summary>
public static class ProcurementBlockReason
{
    public const string NotConfigured = "NOT_CONFIGURED";
    public const string MaxPriceExceeded = "MAX_PRICE_EXCEEDED";
    public const string MinQualityFailed = "MIN_QUALITY_FAILED";
    public const string NoStock = "NO_STOCK";
    public const string LockedSourceUnavailable = "LOCKED_SOURCE_UNAVAILABLE";
    public const string InsufficientCash = "INSUFFICIENT_CASH";
}

/// <summary>
/// A single sourcing candidate evaluated for a purchase unit.
/// Each candidate represents one possible supply route (a global exchange city,
/// a player-placed exchange sell order, or a local B2B source).
/// The list of candidates is ranked by landed cost so the player can compare
/// options and understand why one route may be cheaper after transport.
/// </summary>
public sealed class SourcingCandidate
{
    /// <summary>Source type: GLOBAL_EXCHANGE, PLAYER_EXCHANGE_ORDER, LOCAL_B2B, or LOCKED_VENDOR.</summary>
    public string SourceType { get; set; } = ProcurementSourceType.NoSource;

    /// <summary>Source city ID (for exchange or city-level B2B candidates).</summary>
    public Guid? SourceCityId { get; set; }

    /// <summary>Human-readable source city name.</summary>
    public string? SourceCityName { get; set; }

    /// <summary>Vendor company ID (for player exchange order or locked-vendor candidates).</summary>
    public Guid? SourceVendorCompanyId { get; set; }

    /// <summary>Human-readable vendor company name.</summary>
    public string? SourceVendorName { get; set; }

    /// <summary>Exchange price per unit at the source city (before transit cost).</summary>
    public decimal? ExchangePricePerUnit { get; set; }

    /// <summary>Transit cost per unit from source city to destination building.</summary>
    public decimal? TransitCostPerUnit { get; set; }

    /// <summary>Total landed cost per unit = ExchangePricePerUnit + TransitCostPerUnit.</summary>
    public decimal? DeliveredPricePerUnit { get; set; }

    /// <summary>Estimated quality of goods at this source (0.0–1.0).</summary>
    public decimal? EstimatedQuality { get; set; }

    /// <summary>
    /// Minimum quality in the variability band for exchange sources.
    /// Null for non-exchange sourcing types.
    /// </summary>
    public decimal? QualityMin { get; set; }

    /// <summary>
    /// Maximum quality in the variability band for exchange sources.
    /// Null for non-exchange sourcing types.
    /// </summary>
    public decimal? QualityMax { get; set; }

    /// <summary>Straight-line distance in km from source city to destination city.</summary>
    public double? DistanceKm { get; set; }

    /// <summary>Whether this candidate passes all active filters (max price, min quality, vendor lock).</summary>
    public bool IsEligible { get; set; }

    /// <summary>Machine-readable reason code when IsEligible is false.</summary>
    public string? BlockReason { get; set; }

    /// <summary>Human-readable explanation when IsEligible is false.</summary>
    public string? BlockMessage { get; set; }

    /// <summary>True for the single best valid candidate (lowest landed cost among eligible).</summary>
    public bool IsRecommended { get; set; }

    /// <summary>1-based rank among all candidates when sorted by delivered price (eligible first, then ineligible).</summary>
    public int Rank { get; set; }
}

/// <summary>
/// A single human-readable event from a building's recent tick history.
/// Covers purchasing, manufacturing, resource movement, and public sales.
/// </summary>
public sealed class BuildingRecentActivityEvent
{
    public long Tick { get; set; }
    public Guid BuildingUnitId { get; set; }

    /// <summary>
    /// Machine-readable event type.
    /// Values: PURCHASED, MANUFACTURED, MOVED, SOLD, IDLE, BLOCKED.
    /// </summary>
    public string EventType { get; set; } = "";

    /// <summary>Human-readable one-line description.</summary>
    public string Description { get; set; } = "";

    public decimal? Quantity { get; set; }
    public decimal? Amount { get; set; }
    public Guid? ResourceTypeId { get; set; }
    public Guid? ProductTypeId { get; set; }
}