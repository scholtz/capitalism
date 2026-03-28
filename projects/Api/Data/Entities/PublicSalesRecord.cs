namespace Api.Data.Entities;

/// <summary>Per-tick sales snapshot for analytics.</summary>
public sealed class PublicSalesRecord
{
    public Guid Id { get; set; }
    public Guid BuildingUnitId { get; set; }
    public BuildingUnit BuildingUnit { get; set; } = null!;
    public Guid BuildingId { get; set; }
    public Building Building { get; set; } = null!;
    public Guid CompanyId { get; set; }
    public Company Company { get; set; } = null!;
    public Guid CityId { get; set; }
    public City City { get; set; } = null!;
    public Guid? ProductTypeId { get; set; }
    public ProductType? ProductType { get; set; }
    public Guid? ResourceTypeId { get; set; }
    public ResourceType? ResourceType { get; set; }
    public long Tick { get; set; }
    public DateTime RecordedAtUtc { get; set; } = DateTime.UtcNow;
    public decimal QuantitySold { get; set; }
    public decimal PricePerUnit { get; set; }
    public decimal Revenue { get; set; }
    public decimal Demand { get; set; }
    public decimal SalesCapacity { get; set; }
}
