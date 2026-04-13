using Api.Data.Entities;

namespace Api.Engine.Phases;

/// <summary>
/// Produces raw materials in MINING units inside MINE buildings.
/// Production rate depends on unit level and the city's resource abundance.
/// Output is stored in the mining unit's own inventory up to its storage capacity.
/// </summary>
public sealed class MiningPhase : ITickPhase
{
    public string Name => "Mining";
    public int Order => 500;

    public Task ProcessAsync(TickContext context)
    {
        if (!context.BuildingsByType.TryGetValue(BuildingType.Mine, out var mines))
            return Task.CompletedTask;

        foreach (var building in mines)
        {
            if (!context.UnitsByBuilding.TryGetValue(building.Id, out var units))
                continue;

            // Skip buildings with no power.
            var efficiency = TickContext.GetPowerEfficiency(building);
            if (efficiency <= 0m) continue;

            // Get city resource abundances.
            context.ResourcesByCity.TryGetValue(building.CityId, out var cityResources);
            var abundanceMap = cityResources?
                .ToDictionary(cr => cr.ResourceTypeId, cr => cr.Abundance)
                ?? [];

            foreach (var unit in units)
            {
                if (unit.UnitType != UnitType.Mining) continue;
                if (context.UnitsUnderUpgrade.Contains(unit.Id)) continue;
                if (!unit.ResourceTypeId.HasValue) continue;

                var abundance = abundanceMap.GetValueOrDefault(unit.ResourceTypeId.Value, 0m);
                if (abundance <= 0m) continue;

                var production = GameConstants.MiningRate(unit.Level) * abundance * efficiency;
                var space = context.GetUnitFreeSpace(unit);
                var actual = Math.Min(production, space);
                if (actual <= 0m) continue;

                var inv = context.GetOrCreateUnitInventory(
                    building.Id, unit.Id, unit.ResourceTypeId, null);
                context.AddInventory(inv, actual, 0m, null);
                context.RecordUnitResourceHistory(
                    building.Id,
                    unit.Id,
                    unit.ResourceTypeId,
                    null,
                    producedQuantity: actual);
            }
        }

        return Task.CompletedTask;
    }
}
