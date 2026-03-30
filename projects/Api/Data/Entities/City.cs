using System.ComponentModel.DataAnnotations;

namespace Api.Data.Entities;

/// <summary>
/// Represents a city on the game map where buildings can be placed.
/// Cities have an average rent price that affects occupancy of residential/commercial buildings.
/// </summary>
public sealed class City
{
    /// <summary>Unique identifier for the city.</summary>
    public Guid Id { get; set; }

    /// <summary>Name of the city.</summary>
    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Country code (ISO 3166-1 alpha-2).</summary>
    [Required, MaxLength(2)]
    public string CountryCode { get; set; } = string.Empty;

    /// <summary>Latitude of the city center.</summary>
    public double Latitude { get; set; }

    /// <summary>Longitude of the city center.</summary>
    public double Longitude { get; set; }

    /// <summary>Population of the city affecting demand for products.</summary>
    public int Population { get; set; }

    /// <summary>Average rent price per m² in the city.</summary>
    public decimal AverageRentPerSqm { get; set; }

    /// <summary>Base wage per labor-hour used to price company salary settings.</summary>
    public decimal BaseSalaryPerManhour { get; set; }

    /// <summary>Buildings located in this city.</summary>
    public ICollection<Building> Buildings { get; set; } = [];

    /// <summary>Purchasable building lots in this city.</summary>
    public ICollection<BuildingLot> Lots { get; set; } = [];

    /// <summary>Resources available for mining near this city.</summary>
    public ICollection<CityResource> Resources { get; set; } = [];
}
