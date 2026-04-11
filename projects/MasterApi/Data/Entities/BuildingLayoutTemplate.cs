namespace MasterApi.Data.Entities;

public sealed class BuildingLayoutTemplate
{
    public Guid Id { get; set; }
    public Guid PlayerAccountId { get; set; }
    public PlayerAccount PlayerAccount { get; set; } = null!;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string BuildingType { get; set; } = string.Empty;
    // JSON-serialized array of unit slots
    public string UnitsJson { get; set; } = "[]";
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
