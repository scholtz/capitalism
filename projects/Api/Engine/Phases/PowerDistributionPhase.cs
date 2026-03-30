using Api.Data.Entities;

namespace Api.Engine.Phases;

/// <summary>
/// Computes the city-level power balance each tick and updates each building's
/// <see cref="Building.PowerStatus"/> accordingly.
///
/// Decision rationale: power is balanced at city level (not company level) so that
/// players understand the city grid as a shared resource. A company that builds more
/// power plants benefits the whole city but must still compete for prime lots.
///
/// Balance rules (per city, applied once per tick before production phases):
///   - Supply = sum of PowerOutput across all POWER_PLANT buildings in the city
///             (using GameConstants.DefaultPowerOutputMw if PowerOutput is null).
///   - Demand = sum of PowerConsumption across all non-power-plant buildings in the city
///             (using GameConstants.PowerDemandMw(type, level) if PowerConsumption is 0).
///   - If supply &gt;= demand:          all consumer buildings → POWERED.
///   - If supply &gt;= 50% of demand:   all consumer buildings → CONSTRAINED.
///   - If supply &lt; 50% of demand:    all consumer buildings → OFFLINE.
///
/// Power plants themselves are always POWERED (they produce electricity, not consume it).
/// </summary>
public sealed class PowerDistributionPhase : ITickPhase
{
    public string Name => "PowerDistribution";

    /// <summary>
    /// Runs before all production phases so that manufacturing, mining, and sales
    /// can read the updated PowerStatus without needing to recalculate it.
    /// </summary>
    public int Order => 10;

    public Task ProcessAsync(TickContext context)
    {
        // Group all buildings by city.
        var buildingsByCity = context.BuildingsById.Values
            .GroupBy(b => b.CityId);

        foreach (var cityGroup in buildingsByCity)
        {
            var buildings = cityGroup.ToList();

            var powerPlants = buildings
                .Where(b => b.Type == BuildingType.PowerPlant)
                .ToList();

            var consumers = buildings
                .Where(b => b.Type != BuildingType.PowerPlant)
                .ToList();

            // If there are no power plants in this city yet, buildings operate on
            // municipal / legacy power and are always considered POWERED.
            // Power constraints only apply once at least one power plant exists.
            if (powerPlants.Count == 0)
            {
                foreach (var consumer in consumers)
                {
                    consumer.PowerStatus = PowerStatus.Powered;
                }
                continue;
            }

            // Total supply from all power plants in this city.
            var totalSupplyMw = powerPlants.Sum(plant =>
                plant.PowerOutput > 0m
                    ? plant.PowerOutput.Value
                    : GameConstants.DefaultPowerOutputMw(plant.PowerPlantType));

            // Total demand from all consuming buildings in this city.
            // PowerConsumption == 0 means the building predates the power system
            // and operates without explicit power requirements (legacy/grandfathered).
            var totalDemandMw = consumers.Sum(building => building.PowerConsumption);

            // Determine city-wide power status.
            string cityStatus;
            if (totalDemandMw == 0m || totalSupplyMw >= totalDemandMw)
            {
                cityStatus = PowerStatus.Powered;
            }
            else if (totalSupplyMw >= totalDemandMw * 0.5m)
            {
                cityStatus = PowerStatus.Constrained;
            }
            else
            {
                cityStatus = PowerStatus.Offline;
            }

            // Apply status: power plants are always POWERED.
            foreach (var plant in powerPlants)
            {
                plant.PowerStatus = PowerStatus.Powered;
            }

            // All consumers share the same city-level status in this first slice.
            foreach (var consumer in consumers)
            {
                consumer.PowerStatus = cityStatus;
            }
        }

        return Task.CompletedTask;
    }
}
