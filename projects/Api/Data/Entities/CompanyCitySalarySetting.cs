using System.ComponentModel.DataAnnotations;

namespace Api.Data.Entities;

/// <summary>
/// Player-controlled salary multiplier for a company in a specific city.
/// </summary>
public sealed class CompanyCitySalarySetting
{
    public Guid Id { get; set; }

    public Guid CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    public Guid CityId { get; set; }
    public City City { get; set; } = null!;

    /// <summary>
    /// Multiplier applied to the city's base hourly wage. 1.0 = baseline city salary.
    /// </summary>
    [Range(0.5, 2.0)]
    public decimal SalaryMultiplier { get; set; } = 1m;
}