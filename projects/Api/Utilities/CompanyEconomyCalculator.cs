using Api.Data.Entities;

namespace Api.Utilities;

/// <summary>
/// Shared economy formulas for salary configuration, company overhead, and per-unit operating costs.
/// </summary>
public static class CompanyEconomyCalculator
{
    public const decimal DefaultSalaryMultiplier = 1m;
    public const decimal MinimumSalaryMultiplier = 0.5m;
    public const decimal MaximumSalaryMultiplier = 2m;
    public const decimal MaximumAdministrationOverheadRate = 0.5m;

    public static decimal ClampSalaryMultiplier(decimal value)
    {
        return Math.Clamp(value, MinimumSalaryMultiplier, MaximumSalaryMultiplier);
    }

    public static decimal GetSalaryMultiplier(Company company, Guid cityId)
    {
        return GetSalaryMultiplier(company.CitySalarySettings, cityId);
    }

    public static decimal GetSalaryMultiplier(IEnumerable<CompanyCitySalarySetting> settings, Guid cityId)
    {
        var configured = settings.FirstOrDefault(setting => setting.CityId == cityId);

        return ClampSalaryMultiplier(configured?.SalaryMultiplier ?? DefaultSalaryMultiplier);
    }

    public static decimal GetEffectiveHourlyWage(City city, decimal salaryMultiplier)
    {
        return decimal.Round(
            city.BaseSalaryPerManhour * ClampSalaryMultiplier(salaryMultiplier),
            4,
            MidpointRounding.AwayFromZero);
    }

    public static decimal ComputeAdministrationOverheadRate(
        Company company,
        decimal companyAssetValue,
        decimal maxCompanyAssetValue,
        long currentTick)
    {
        var ageTicks = Math.Max(0L, currentTick - company.FoundedAtTick);
        var ageFactor = Math.Min(1m, ageTicks / (decimal)(2 * Engine.GameConstants.TicksPerYear));
        var assetFactor = maxCompanyAssetValue > 0m
            ? Math.Min(1m, companyAssetValue / maxCompanyAssetValue)
            : 0m;

        return decimal.Round(
            MaximumAdministrationOverheadRate * ageFactor * assetFactor,
            4,
            MidpointRounding.AwayFromZero);
    }

    public static decimal GetBaseUnitLaborHours(string unitType, int level)
    {
        var normalizedLevel = Math.Max(level, 1);
        var baseHours = unitType switch
        {
            UnitType.Mining => 1.4m,
            UnitType.Storage => 0.15m,
            UnitType.B2BSales => 0.45m,
            UnitType.Purchase => 0.35m,
            UnitType.Manufacturing => 0.85m,
            UnitType.Branding => 0.3m,
            UnitType.Marketing => 0.6m,
            UnitType.PublicSales => 0.7m,
            UnitType.ProductQuality => 0.55m,
            UnitType.BrandQuality => 0.55m,
            _ => 0m,
        };

        return decimal.Round(baseHours * normalizedLevel, 4, MidpointRounding.AwayFromZero);
    }

    public static decimal GetBaseUnitEnergyMwh(string unitType, int level)
    {
        var normalizedLevel = Math.Max(level, 1);
        var baseEnergy = unitType switch
        {
            UnitType.Mining => 0.45m,
            UnitType.Storage => 0.04m,
            UnitType.B2BSales => 0.08m,
            UnitType.Purchase => 0.06m,
            UnitType.Manufacturing => 0.18m,
            UnitType.Branding => 0.05m,
            UnitType.Marketing => 0.07m,
            UnitType.PublicSales => 0.12m,
            UnitType.ProductQuality => 0.09m,
            UnitType.BrandQuality => 0.09m,
            _ => 0m,
        };

        return decimal.Round(baseEnergy * normalizedLevel, 4, MidpointRounding.AwayFromZero);
    }
}