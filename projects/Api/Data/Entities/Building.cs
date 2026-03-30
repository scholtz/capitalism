using System.ComponentModel.DataAnnotations;

namespace Api.Data.Entities;

/// <summary>
/// Represents a building on the game map. Buildings are owned by companies
/// and contain a 4x4 unit grid for internal configuration.
/// </summary>
public sealed class Building
{
    /// <summary>Unique identifier for the building.</summary>
    public Guid Id { get; set; }

    /// <summary>The company that owns this building.</summary>
    public Guid CompanyId { get; set; }

    /// <summary>Navigation property to the owning company.</summary>
    public Company Company { get; set; } = null!;

    /// <summary>The city where this building is located.</summary>
    public Guid CityId { get; set; }

    /// <summary>Navigation property to the city.</summary>
    public City City { get; set; } = null!;

    /// <summary>Building type: MINE, FACTORY, SALES_SHOP, etc.</summary>
    [Required, MaxLength(30)]
    public string Type { get; set; } = string.Empty;

    /// <summary>Display name of the building.</summary>
    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Latitude position on the game map.</summary>
    public double Latitude { get; set; }

    /// <summary>Longitude position on the game map.</summary>
    public double Longitude { get; set; }

    /// <summary>Current building level affecting stats.</summary>
    public int Level { get; set; } = 1;

    /// <summary>Power consumption in MW.</summary>
    public decimal PowerConsumption { get; set; }

    /// <summary>Whether the building is listed for sale to other players.</summary>
    public bool IsForSale { get; set; }

    /// <summary>Asking price if the building is for sale.</summary>
    public decimal? AskingPrice { get; set; }

    /// <summary>Price per m² for apartment and commercial buildings.</summary>
    public decimal? PricePerSqm { get; set; }

    /// <summary>Occupancy percentage (0-100) for apartment/commercial buildings.</summary>
    public decimal? OccupancyPercent { get; set; }

    /// <summary>Total area in m² for apartment/commercial buildings.</summary>
    public decimal? TotalAreaSqm { get; set; }

    /// <summary>Power plant type: COAL, GAS, NUCLEAR, SOLAR, WIND (only for power plants).</summary>
    [MaxLength(20)]
    public string? PowerPlantType { get; set; }

    /// <summary>Power output in MW (only for power plants).</summary>
    public decimal? PowerOutput { get; set; }

    /// <summary>
    /// Power supply status set each tick by the PowerDistributionPhase.
    /// Values: POWERED, CONSTRAINED, OFFLINE.
    /// Power plants themselves are always POWERED (they produce, not consume).
    /// </summary>
    [MaxLength(20)]
    public string PowerStatus { get; set; } = Entities.PowerStatus.Powered;

    /// <summary>Media type: NEWSPAPER, RADIO, TV (only for media houses).</summary>
    [MaxLength(20)]
    public string? MediaType { get; set; }

    /// <summary>Interest rate percentage for banks.</summary>
    public decimal? InterestRate { get; set; }

    /// <summary>UTC timestamp when the building was constructed.</summary>
    public DateTime BuiltAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Units installed in this building's 4x4 grid.</summary>
    public ICollection<BuildingUnit> Units { get; set; } = [];

    /// <summary>Queued building configuration that will replace the active units on a future tick.</summary>
    public BuildingConfigurationPlan? PendingConfiguration { get; set; }
}
