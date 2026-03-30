using Api.Data.Entities;

namespace Api.Engine.Phases;

/// <summary>
/// Runs manufacturing units inside FACTORY buildings.
/// For each MANUFACTURING unit, consumes recipe inputs from incoming-linked units
/// and produces the configured product up to the unit's batch capacity.
/// </summary>
public sealed class ManufacturingPhase : ITickPhase
{
    public string Name => "Manufacturing";
    public int Order => 300;

    public Task ProcessAsync(TickContext context)
    {
        if (!context.BuildingsByType.TryGetValue(BuildingType.Factory, out var factories))
            return Task.CompletedTask;

        foreach (var building in factories)
        {
            if (!context.UnitsByBuilding.TryGetValue(building.Id, out var units))
                continue;

            // Skip buildings with no power.
            var efficiency = TickContext.GetPowerEfficiency(building);
            if (efficiency <= 0m) continue;

            foreach (var unit in units)
            {
                if (unit.UnitType != UnitType.Manufacturing) continue;
                ProcessManufacturingUnit(context, building, unit, efficiency);
            }
        }

        return Task.CompletedTask;
    }

    private static void ProcessManufacturingUnit(
        TickContext context,
        Building building,
        BuildingUnit unit,
        decimal efficiency)
    {
        if (!unit.ProductTypeId.HasValue) return;
        if (!context.ProductTypesById.TryGetValue(unit.ProductTypeId.Value, out var productType))
            return;
        if (!context.RecipesByProduct.TryGetValue(productType.Id, out var recipes) || recipes.Count == 0)
            return;

        var maxBatches = (int)Math.Floor(GameConstants.ManufacturingBatches(unit.Level) * efficiency);

        // Collect available inputs from the manufacturing unit's own inventory
        // (filled by resource movement) AND from incoming-linked units.
        var inputInventories = new Dictionary<Guid, List<Inventory>>();

        // Own inventory first.
        if (context.InventoryByUnit.TryGetValue(unit.Id, out var ownInv))
        {
            foreach (var item in ownInv)
            {
                // Skip the output product – only consume recipe inputs.
                if (item.ProductTypeId == productType.Id && item.ResourceTypeId is null) continue;
                var key = item.ResourceTypeId ?? item.ProductTypeId ?? Guid.Empty;
                if (key == Guid.Empty) continue;
                if (!inputInventories.TryGetValue(key, out var list))
                {
                    list = [];
                    inputInventories[key] = list;
                }
                list.Add(item);
            }
        }

        // Then incoming-linked units.
        var incomingUnits = context.GetIncomingLinkedUnits(unit);
        foreach (var incoming in incomingUnits)
        {
            if (!context.InventoryByUnit.TryGetValue(incoming.Id, out var inv)) continue;
            foreach (var item in inv)
            {
                var key = item.ResourceTypeId ?? item.ProductTypeId ?? Guid.Empty;
                if (key == Guid.Empty) continue;
                if (!inputInventories.TryGetValue(key, out var list))
                {
                    list = [];
                    inputInventories[key] = list;
                }
                list.Add(item);
            }
        }

        // Determine how many batches can actually run based on available inputs.
        var possibleBatches = maxBatches;
        foreach (var recipe in recipes)
        {
            var ingredientId = recipe.ResourceTypeId ?? recipe.InputProductTypeId;
            if (ingredientId is null) continue;

            var available = 0m;
            if (inputInventories.TryGetValue(ingredientId.Value, out var items))
                available = items.Sum(i => i.Quantity);

            var canMake = recipe.Quantity > 0m
                ? (int)Math.Floor(available / recipe.Quantity)
                : int.MaxValue;
            possibleBatches = Math.Min(possibleBatches, canMake);
        }

        if (possibleBatches <= 0) return;

        // Check output space.
        var outputQuantity = possibleBatches * productType.OutputQuantity;
        var space = context.GetUnitFreeSpace(unit);
        if (space <= 0m) return;

        if (outputQuantity > space)
        {
            // Reduce batches to fit available space.
            possibleBatches = productType.OutputQuantity > 0m
                ? (int)Math.Floor(space / productType.OutputQuantity)
                : 0;
            outputQuantity = possibleBatches * productType.OutputQuantity;
        }

        if (possibleBatches <= 0) return;

        // Consume inputs.
        decimal avgInputQuality = 0m;
        decimal totalInputSourcingCost = 0m;
        int qualitySamples = 0;
        foreach (var recipe in recipes)
        {
            var ingredientId = recipe.ResourceTypeId ?? recipe.InputProductTypeId;
            if (ingredientId is null) continue;

            var needed = possibleBatches * recipe.Quantity;
            if (!inputInventories.TryGetValue(ingredientId.Value, out var items)) continue;

            foreach (var item in items)
            {
                if (needed <= 0m) break;
                var consume = Math.Min(item.Quantity, needed);
                var withdrawn = context.WithdrawInventory(item, consume);
                if (withdrawn.Quantity <= 0m) continue;

                 if (item.BuildingUnitId.HasValue && item.BuildingUnitId.Value != unit.Id)
                 {
                    context.RecordUnitResourceHistory(
                        building.Id,
                        item.BuildingUnitId.Value,
                        item.ResourceTypeId,
                        item.ProductTypeId,
                        outflowQuantity: withdrawn.Quantity);
                    context.RecordUnitResourceHistory(
                        building.Id,
                        unit.Id,
                        item.ResourceTypeId,
                        item.ProductTypeId,
                        inflowQuantity: withdrawn.Quantity);
                 }

                context.RecordUnitResourceHistory(
                    building.Id,
                    unit.Id,
                    item.ResourceTypeId,
                    item.ProductTypeId,
                    consumedQuantity: withdrawn.Quantity);

                avgInputQuality += item.Quality * consume;
                totalInputSourcingCost += withdrawn.SourcingCostTotal;
                qualitySamples++;
                needed -= withdrawn.Quantity;
            }
        }

        // Calculate output quality: average of input quality, boosted by R&D brand quality.
        var baseQuality = qualitySamples > 0 && outputQuantity > 0m
            ? avgInputQuality / (possibleBatches * recipes.Sum(r => r.Quantity))
            : 0.5m;

        var brand = context.FindBrand(building.CompanyId, productType.Id, productType.Industry);
        var rdBonus = brand?.Quality ?? 0m;
        var quality = Math.Min(1m, baseQuality * 0.7m + rdBonus * 0.3m);

        // Produce output.
        var output = context.GetOrCreateUnitInventory(
            building.Id, unit.Id, null, productType.Id);
        context.AddInventory(output, outputQuantity, totalInputSourcingCost, quality);
        context.RecordUnitResourceHistory(
            building.Id,
            unit.Id,
            null,
            productType.Id,
            producedQuantity: outputQuantity);
    }
}
