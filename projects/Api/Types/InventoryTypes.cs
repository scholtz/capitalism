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