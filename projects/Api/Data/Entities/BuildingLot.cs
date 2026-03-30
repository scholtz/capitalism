using System.ComponentModel.DataAnnotations;

namespace Api.Data.Entities;

/// <summary>
/// Represents a purchasable building lot within a city.
/// Each lot has fixed coordinates on the real-world map, a price, a district classification,
/// and a set of building types that are suitable for the location.
/// Once purchased, the lot is linked to the owning company and the building placed on it.
/// </summary>
public sealed class BuildingLot
{
    /// <summary>Unique identifier for the building lot.</summary>
    public Guid Id { get; set; }

    /// <summary>The city where this lot is located.</summary>
    public Guid CityId { get; set; }

    /// <summary>Navigation property to the city.</summary>
    public City City { get; set; } = null!;

    /// <summary>Display name for the lot (e.g. "Industrial Plot A1").</summary>
    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Short description explaining the location context.</summary>
    [MaxLength(500)]
    public string Description { get; set; } = string.Empty;

    /// <summary>District or zone name (e.g. "Industrial Zone", "Commercial District").</summary>
    [Required, MaxLength(100)]
    public string District { get; set; } = string.Empty;

    /// <summary>Latitude position on the map.</summary>
    public double Latitude { get; set; }

    /// <summary>Longitude position on the map.</summary>
    public double Longitude { get; set; }

    /// <summary>
    /// Dynamic density score for the lot. Higher values represent land with stronger
    /// consumer foot traffic and better access to populated neighborhoods.
    /// </summary>
    public decimal PopulationIndex { get; set; } = 1m;

    /// <summary>
    /// Long-term anchor value for this land parcel. The current appraised price moves
    /// around this base as the local population index changes over time.
    /// </summary>
    public decimal BasePrice { get; set; }

    /// <summary>Purchase price in game currency.</summary>
    public decimal Price { get; set; }

    /// <summary>Comma-separated building types suitable for this lot (e.g. "FACTORY,MINE").</summary>
    [Required, MaxLength(200)]
    public string SuitableTypes { get; set; } = string.Empty;

    /// <summary>Company that owns this lot (null if available for purchase).</summary>
    public Guid? OwnerCompanyId { get; set; }

    /// <summary>Navigation property to the owning company.</summary>
    public Company? OwnerCompany { get; set; }

    /// <summary>Building placed on this lot (null if not yet built).</summary>
    public Guid? BuildingId { get; set; }

    /// <summary>Navigation property to the building on this lot.</summary>
    public Building? Building { get; set; }

    /// <summary>
    /// Optional raw material available for extraction on this lot.
    /// When set, a MINE building can extract this resource here.
    /// </summary>
    public Guid? ResourceTypeId { get; set; }

    /// <summary>Navigation property for the raw material type (null if no resource on this lot).</summary>
    public ResourceType? ResourceType { get; set; }

    /// <summary>
    /// Quality of the raw material on this lot (0.0 – 1.0, where 1.0 is highest grade).
    /// Null when no resource is present.
    /// </summary>
    public decimal? MaterialQuality { get; set; }

    /// <summary>
    /// Estimated total extractable quantity of the raw material on this lot (in tonnes).
    /// Null when no resource is present.
    /// </summary>
    public decimal? MaterialQuantity { get; set; }

    /// <summary>
    /// Application-managed concurrency token that makes lot purchase atomic.
    /// Only one request may persist a transition from available to owned.
    /// </summary>
    public Guid ConcurrencyToken { get; set; } = Guid.NewGuid();
}
