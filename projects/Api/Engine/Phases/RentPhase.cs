using Api.Data.Entities;

namespace Api.Engine.Phases;

/// <summary>
/// Collects rent revenue from APARTMENT and COMMERCIAL buildings and
/// adjusts occupancy toward equilibrium based on the price-per-sqm
/// relative to the city average.
/// </summary>
public sealed class RentPhase : ITickPhase
{
    public string Name => "Rent";
    public int Order => 900;

    public Task ProcessAsync(TickContext context)
    {
        ProcessBuildingType(context, BuildingType.Apartment);
        ProcessBuildingType(context, BuildingType.Commercial);
        return Task.CompletedTask;
    }

    private static void ProcessBuildingType(TickContext context, string buildingType)
    {
        if (!context.BuildingsByType.TryGetValue(buildingType, out var buildings))
            return;

        foreach (var building in buildings)
        {
            // Activate pending rent change if the activation tick has been reached.
            if (building.PendingPricePerSqm.HasValue
                && building.PendingPriceActivationTick.HasValue
                && context.CurrentTick >= building.PendingPriceActivationTick.Value)
            {
                building.PricePerSqm = building.PendingPricePerSqm.Value;
                building.PendingPricePerSqm = null;
                building.PendingPriceActivationTick = null;
            }

            if (building.PricePerSqm is null || building.TotalAreaSqm is null || building.OccupancyPercent is null)
                continue;
            if (!context.CompaniesById.TryGetValue(building.CompanyId, out var company))
                continue;
            if (!context.CitiesById.TryGetValue(building.CityId, out var city))
                continue;

            // Collect rent for this tick.
            var rentIncome = building.PricePerSqm.Value
                             * building.TotalAreaSqm.Value
                             * building.OccupancyPercent.Value / 100m;

            if (rentIncome > 0m)
            {
                company.Cash += rentIncome;

                // Record rent income in the ledger.
                context.Db.LedgerEntries.Add(new LedgerEntry
                {
                    Id = Guid.NewGuid(),
                    CompanyId = company.Id,
                    BuildingId = building.Id,
                    Category = LedgerCategory.RentIncome,
                    Description = $"Rent income – {building.Name}",
                    Amount = rentIncome,
                    RecordedAtTick = context.CurrentTick,
                    RecordedAtUtc = DateTime.UtcNow
                });
            }

            // Adjust occupancy toward equilibrium.
            var avgRent = city.AverageRentPerSqm;
            if (avgRent <= 0m) continue;

            var priceDiff = (building.PricePerSqm.Value - avgRent) / avgRent;

            if (priceDiff > 0m)
            {
                // Overpriced → occupancy drifts down.
                building.OccupancyPercent = Math.Max(0m,
                    building.OccupancyPercent.Value - priceDiff * GameConstants.OccupancyAdjustmentRate);
            }
            else
            {
                // Underpriced → occupancy drifts up (harder to reach 100%).
                building.OccupancyPercent = Math.Min(100m,
                    building.OccupancyPercent.Value - priceDiff * GameConstants.OccupancyAdjustmentRate * 0.5m);
            }
        }
    }
}
