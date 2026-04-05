using System.ComponentModel.DataAnnotations;

namespace Api.Data.Entities;

/// <summary>
/// Snapshot of a single unit inside a queued building configuration upgrade.
/// </summary>
public sealed class BuildingConfigurationPlanUnit
{
    /// <summary>Unique identifier for the queued unit snapshot.</summary>
    public Guid Id { get; set; }

    /// <summary>The queued configuration this unit belongs to.</summary>
    public Guid BuildingConfigurationPlanId { get; set; }

    /// <summary>Navigation property to the queued configuration.</summary>
    public BuildingConfigurationPlan BuildingConfigurationPlan { get; set; } = null!;

    /// <summary>Unit type that will be active after the upgrade applies.</summary>
    [Required, MaxLength(30)]
    public string UnitType { get; set; } = string.Empty;

    /// <summary>Grid column position (0-3).</summary>
    public int GridX { get; set; }

    /// <summary>Grid row position (0-3).</summary>
    public int GridY { get; set; }

    /// <summary>Upgrade level that will be active after the upgrade applies.</summary>
    public int Level { get; set; } = 1;

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

    /// <summary>The tick when this pending work started.</summary>
    public long StartedAtTick { get; set; }

    /// <summary>The tick when this target state becomes active.</summary>
    public long AppliesAtTick { get; set; }

    /// <summary>Total ticks required for this pending unit change.</summary>
    public int TicksRequired { get; set; }

    /// <summary>Whether this snapshot differs from the currently active unit at the same grid position.</summary>
    public bool IsChanged { get; set; }

    /// <summary>Whether this entry represents canceling a previously queued change back to the current live state.</summary>
    public bool IsReverting { get; set; }

    // ── Unit-specific configuration ──

    /// <summary>Resource type this unit works with.</summary>
    public Guid? ResourceTypeId { get; set; }

    /// <summary>Product type this unit works with.</summary>
    public Guid? ProductTypeId { get; set; }

    /// <summary>Minimum selling price for B2B Sales or Public Sales units.</summary>
    public decimal? MinPrice { get; set; }

    /// <summary>Maximum purchase price for Purchase units.</summary>
    public decimal? MaxPrice { get; set; }

    /// <summary>Purchase source: EXCHANGE, LOCAL, OPTIMAL.</summary>
    [MaxLength(20)]
    public string? PurchaseSource { get; set; }

    /// <summary>Visibility: PUBLIC, COMPANY, GROUP.</summary>
    [MaxLength(20)]
    public string? SaleVisibility { get; set; }

    /// <summary>Marketing budget per tick.</summary>
    public decimal? Budget { get; set; }

    /// <summary>Media house building ID for Marketing units.</summary>
    public Guid? MediaHouseBuildingId { get; set; }

    /// <summary>Minimum product quality for Purchase units (0.0-1.0).</summary>
    public decimal? MinQuality { get; set; }

    /// <summary>Brand scope: PRODUCT, CATEGORY, COMPANY.</summary>
    [MaxLength(20)]
    public string? BrandScope { get; set; }

    /// <summary>Lock purchases to a specific vendor company ID.</summary>
    public Guid? VendorLockCompanyId { get; set; }

    /// <summary>Lock exchange purchases to a specific source city ID. Applies when PurchaseSource is EXCHANGE.</summary>
    public Guid? LockedCityId { get; set; }
}
