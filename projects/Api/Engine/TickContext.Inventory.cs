using Api.Data.Entities;

namespace Api.Engine;

public sealed partial class TickContext
{
    /// <summary>
    /// Gets or creates a unit-level inventory row for the given resource or product.
    /// New rows are tracked in <see cref="NewInventory"/> for later persistence.
    /// </summary>
    public Inventory GetOrCreateUnitInventory(
        Guid buildingId, Guid unitId, Guid? resourceTypeId, Guid? productTypeId)
    {
        if (!InventoryByUnit.TryGetValue(unitId, out var unitInventories))
        {
            unitInventories = [];
            InventoryByUnit[unitId] = unitInventories;
        }

        var existing = unitInventories.FirstOrDefault(i =>
            i.ResourceTypeId == resourceTypeId && i.ProductTypeId == productTypeId);

        if (existing is not null) return existing;

        var inventory = new Inventory
        {
            Id = Guid.NewGuid(),
            BuildingId = buildingId,
            BuildingUnitId = unitId,
            ResourceTypeId = resourceTypeId,
            ProductTypeId = productTypeId,
            Quantity = 0m,
            SourcingCostTotal = 0m,
            Quality = 0.5m
        };

        unitInventories.Add(inventory);
        NewInventory.Add(inventory);
        TickStartRemainingQuantityByInventoryId[inventory.Id] = 0m;

        if (!InventoryByBuilding.TryGetValue(buildingId, out var buildingInventories))
        {
            buildingInventories = [];
            InventoryByBuilding[buildingId] = buildingInventories;
        }
        buildingInventories.Add(inventory);

        return inventory;
    }

    /// <summary>Returns the total quantity stored in a unit across all item types.</summary>
    public decimal GetUnitCurrentLoad(Guid unitId)
    {
        if (!InventoryByUnit.TryGetValue(unitId, out var inventories)) return 0m;
        return inventories.Sum(i => i.Quantity);
    }

    /// <summary>Returns the remaining storage space in a unit based on its level.</summary>
    public decimal GetUnitFreeSpace(BuildingUnit unit)
    {
        var capacity = GameConstants.StorageCapacity(unit.Level);
        return Math.Max(0m, capacity - GetUnitCurrentLoad(unit.Id));
    }

    /// <summary>
    /// Returns the remaining space a specific inventory item can occupy in a target unit.
    /// Manufacturing inputs are capped per ingredient so one input cannot block the whole unit.
    /// </summary>
    public decimal GetUnitReceivingSpace(BuildingUnit unit, Guid? resourceTypeId, Guid? productTypeId)
    {
        var totalFreeSpace = GetUnitFreeSpace(unit);
        if (totalFreeSpace <= 0m || unit.UnitType != UnitType.Manufacturing)
        {
            return totalFreeSpace;
        }

        if (!unit.ProductTypeId.HasValue)
        {
            return totalFreeSpace;
        }

        if (productTypeId == unit.ProductTypeId && !resourceTypeId.HasValue)
        {
            return totalFreeSpace;
        }

        if (!RecipesByProduct.TryGetValue(unit.ProductTypeId.Value, out var recipes) || recipes.Count == 0)
        {
            return totalFreeSpace;
        }

        var isRecipeInput = recipes.Any(recipe =>
            recipe.ResourceTypeId == resourceTypeId
            || recipe.InputProductTypeId == productTypeId);
        if (!isRecipeInput)
        {
            return totalFreeSpace;
        }

        var distinctInputCount = recipes
            .Select(recipe => recipe.ResourceTypeId ?? recipe.InputProductTypeId)
            .Where(ingredientId => ingredientId.HasValue)
            .Select(ingredientId => ingredientId!.Value)
            .Distinct()
            .Count();
        if (distinctInputCount <= 0)
        {
            return totalFreeSpace;
        }

        var perIngredientCapacity = GameConstants.StorageCapacity(unit.Level) / (distinctInputCount + 1m);
        var currentMatchingLoad = InventoryByUnit.TryGetValue(unit.Id, out var inventories)
            ? inventories
                .Where(inventory => inventory.ResourceTypeId == resourceTypeId && inventory.ProductTypeId == productTypeId)
                .Sum(inventory => inventory.Quantity)
            : 0m;

        var ingredientFreeSpace = Math.Max(0m, perIngredientCapacity - currentMatchingLoad);
        return Math.Min(totalFreeSpace, ingredientFreeSpace);
    }

    /// <summary>
    /// Returns how much of an inventory row was already present at tick start and
    /// remains unconsumed by earlier phases in the current tick.
    /// </summary>
    public decimal GetTickStartRemainingQuantity(Inventory inventory)
    {
        return TickStartRemainingQuantityByInventoryId.GetValueOrDefault(inventory.Id);
    }

    /// <summary>
    /// Removes quantity from an inventory row and returns the proportional
    /// sourcing cost carried by the withdrawn quantity.
    /// </summary>
    public (decimal Quantity, decimal SourcingCostTotal) WithdrawInventory(
        Inventory inventory,
        decimal requestedQuantity)
    {
        if (requestedQuantity <= 0m || inventory.Quantity <= 0m)
        {
            return (0m, 0m);
        }

        var actualQuantity = Math.Min(inventory.Quantity, requestedQuantity);
        var previousQuantity = inventory.Quantity;
        var costRemoved = previousQuantity > 0m && inventory.SourcingCostTotal > 0m
            ? RoundInventoryDecimal(inventory.SourcingCostTotal * (actualQuantity / previousQuantity))
            : 0m;

        inventory.Quantity = RoundInventoryDecimal(previousQuantity - actualQuantity);
        inventory.SourcingCostTotal = inventory.Quantity <= 0m
            ? 0m
            : Math.Max(0m, RoundInventoryDecimal(inventory.SourcingCostTotal - costRemoved));

        if (TickStartRemainingQuantityByInventoryId.TryGetValue(inventory.Id, out var remainingTickStartQuantity))
        {
            TickStartRemainingQuantityByInventoryId[inventory.Id] = Math.Max(
                0m,
                RoundInventoryDecimal(remainingTickStartQuantity - actualQuantity));
        }

        return (RoundInventoryDecimal(actualQuantity), costRemoved);
    }

    /// <summary>
    /// Adds quantity and sourcing cost to an inventory row while blending
    /// quality by quantity when a value is supplied.
    /// </summary>
    public void AddInventory(
        Inventory inventory,
        decimal quantity,
        decimal sourcingCostTotal,
        decimal? quality)
    {
        if (quantity <= 0m)
        {
            return;
        }

        var previousQuantity = inventory.Quantity;
        var newQuantity = RoundInventoryDecimal(previousQuantity + quantity);

        if (quality.HasValue && newQuantity > 0m)
        {
            inventory.Quality = previousQuantity > 0m
                ? RoundInventoryDecimal(((inventory.Quality * previousQuantity) + (quality.Value * quantity)) / newQuantity)
                : RoundInventoryDecimal(quality.Value);
        }

        inventory.Quantity = newQuantity;
        inventory.SourcingCostTotal = RoundInventoryDecimal(inventory.SourcingCostTotal + sourcingCostTotal);
    }

    /// <summary>
    /// Aggregates per-tick movement history for a unit and tracked item.
    /// </summary>
    public void RecordUnitResourceHistory(
        Guid buildingId,
        Guid unitId,
        Guid? resourceTypeId,
        Guid? productTypeId,
        decimal inflowQuantity = 0m,
        decimal outflowQuantity = 0m,
        decimal consumedQuantity = 0m,
        decimal producedQuantity = 0m)
    {
        if (!resourceTypeId.HasValue && !productTypeId.HasValue)
        {
            return;
        }

        inflowQuantity = RoundInventoryDecimal(inflowQuantity);
        outflowQuantity = RoundInventoryDecimal(outflowQuantity);
        consumedQuantity = RoundInventoryDecimal(consumedQuantity);
        producedQuantity = RoundInventoryDecimal(producedQuantity);

        if (inflowQuantity <= 0m && outflowQuantity <= 0m && consumedQuantity <= 0m && producedQuantity <= 0m)
        {
            return;
        }

        var key = (unitId, resourceTypeId, productTypeId, CurrentTick);
        if (!_unitResourceHistoryByKey.TryGetValue(key, out var history))
        {
            history = new BuildingUnitResourceHistory
            {
                Id = Guid.NewGuid(),
                BuildingId = buildingId,
                BuildingUnitId = unitId,
                ResourceTypeId = resourceTypeId,
                ProductTypeId = productTypeId,
                Tick = CurrentTick,
            };

            _unitResourceHistoryByKey[key] = history;
            NewUnitResourceHistories.Add(history);
        }

        history.InflowQuantity = RoundInventoryDecimal(history.InflowQuantity + inflowQuantity);
        history.OutflowQuantity = RoundInventoryDecimal(history.OutflowQuantity + outflowQuantity);
        history.ConsumedQuantity = RoundInventoryDecimal(history.ConsumedQuantity + consumedQuantity);
        history.ProducedQuantity = RoundInventoryDecimal(history.ProducedQuantity + producedQuantity);
    }
}
