using Api.Data.Entities;
using Api.Utilities;

namespace Api.Engine.Phases;

/// <summary>
/// Applies per-tick labor and energy costs for active units in powered buildings.
/// </summary>
public sealed class OperatingCostPhase : ITickPhase
{
    public string Name => "OperatingCosts";
    public int Order => 450;

    public Task ProcessAsync(TickContext context)
    {
        var maxCompanyAssetValue = context.CompaniesById.Keys
            .Select(context.GetCompanyAssetValue)
            .DefaultIfEmpty(0m)
            .Max();

        foreach (var building in context.BuildingsById.Values)
        {
            if (!context.UnitsByBuilding.TryGetValue(building.Id, out var units) || units.Count == 0)
            {
                continue;
            }

            var efficiency = TickContext.GetPowerEfficiency(building);
            if (efficiency <= 0m)
            {
                continue;
            }

            if (!context.CompaniesById.TryGetValue(building.CompanyId, out var company)
                || !context.CitiesById.TryGetValue(building.CityId, out var city))
            {
                continue;
            }

            var salarySettings = context.CitySalarySettingsByCompany.GetValueOrDefault(company.Id, []);
            var salaryMultiplier = CompanyEconomyCalculator.GetSalaryMultiplier(salarySettings, city.Id);
            var hourlyWage = CompanyEconomyCalculator.GetEffectiveHourlyWage(city, salaryMultiplier);
            var companyAssetValue = context.GetCompanyAssetValue(company.Id);
            var overheadRate = CompanyEconomyCalculator.ComputeAdministrationOverheadRate(
                company,
                companyAssetValue,
                maxCompanyAssetValue,
                context.CurrentTick);

            foreach (var unit in units)
            {
                var baseLaborHours = CompanyEconomyCalculator.GetBaseUnitLaborHours(unit.UnitType, unit.Level) * efficiency;
                var baseEnergyMwh = CompanyEconomyCalculator.GetBaseUnitEnergyMwh(unit.UnitType, unit.Level) * efficiency;

                if (baseLaborHours <= 0m && baseEnergyMwh <= 0m)
                {
                    continue;
                }

                var laborCost = decimal.Round(
                    baseLaborHours * hourlyWage * (unit.UnitType == UnitType.Manufacturing ? 1m + overheadRate : 1m),
                    2,
                    MidpointRounding.AwayFromZero);
                var energyCost = decimal.Round(
                    baseEnergyMwh * GameConstants.EnergyPricePerMwh,
                    2,
                    MidpointRounding.AwayFromZero);

                if (laborCost > 0m)
                {
                    company.Cash -= laborCost;
                    context.Db.LedgerEntries.Add(new LedgerEntry
                    {
                        Id = Guid.NewGuid(),
                        CompanyId = company.Id,
                        BuildingId = building.Id,
                        BuildingUnitId = unit.Id,
                        Category = LedgerCategory.LaborCost,
                        Description = $"Operating labor for {unit.UnitType}",
                        Amount = -laborCost,
                        RecordedAtTick = context.CurrentTick,
                        RecordedAtUtc = DateTime.UtcNow,
                    });
                }

                if (energyCost > 0m)
                {
                    company.Cash -= energyCost;
                    context.Db.LedgerEntries.Add(new LedgerEntry
                    {
                        Id = Guid.NewGuid(),
                        CompanyId = company.Id,
                        BuildingId = building.Id,
                        BuildingUnitId = unit.Id,
                        Category = LedgerCategory.EnergyCost,
                        Description = $"Operating energy for {unit.UnitType}",
                        Amount = -energyCost,
                        RecordedAtTick = context.CurrentTick,
                        RecordedAtUtc = DateTime.UtcNow,
                    });
                }
            }
        }

        return Task.CompletedTask;
    }
}