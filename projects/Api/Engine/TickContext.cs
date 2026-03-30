using Api.Data;
using Api.Data.Entities;

namespace Api.Engine;

/// <summary>
/// Holds all pre-loaded game data for a single tick, indexed for O(1) lookups.
/// Created by <see cref="TickProcessor"/> at the start of each tick.
/// </summary>
public sealed class TickContext
{
    private readonly Dictionary<(Guid BuildingUnitId, Guid? ResourceTypeId, Guid? ProductTypeId, long Tick), BuildingUnitResourceHistory> _unitResourceHistoryByKey = [];

    private static decimal RoundInventoryDecimal(decimal value) =>
        decimal.Round(value, 4, MidpointRounding.AwayFromZero);

    public required AppDbContext Db { get; init; }
    public required GameState GameState { get; init; }
    public long CurrentTick => GameState.CurrentTick;

    // ── Indexed game data ──

    public Dictionary<Guid, Building> BuildingsById { get; init; } = [];
    public Dictionary<string, List<Building>> BuildingsByType { get; init; } = [];
    public Dictionary<Guid, List<BuildingUnit>> UnitsByBuilding { get; init; } = [];
    public Dictionary<Guid, Dictionary<(int GridX, int GridY), BuildingUnit>> UnitsByBuildingPosition { get; init; } = [];
    public Dictionary<Guid, List<Inventory>> InventoryByUnit { get; init; } = [];
    public Dictionary<Guid, List<Inventory>> InventoryByBuilding { get; init; } = [];
    public Dictionary<Guid, Company> CompaniesById { get; init; } = [];
    public Dictionary<Guid, City> CitiesById { get; init; } = [];
    public Dictionary<Guid, List<CityResource>> ResourcesByCity { get; init; } = [];
    public Dictionary<Guid, ResourceType> ResourceTypesById { get; init; } = [];
    public Dictionary<Guid, ProductType> ProductTypesById { get; init; } = [];
    public Dictionary<Guid, List<ProductRecipe>> RecipesByProduct { get; init; } = [];
    public Dictionary<Guid, List<Brand>> BrandsByCompany { get; init; } = [];
    public List<ExchangeOrder> ActiveExchangeOrders { get; init; } = [];
    public Dictionary<Guid, decimal> TickStartRemainingQuantityByInventoryId { get; init; } = [];

    // ── Pending mutations ──

    public List<Inventory> NewInventory { get; } = [];
    public List<BuildingUnitResourceHistory> NewUnitResourceHistories { get; } = [];

    // ── Helpers ──

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

    /// <summary>
    /// Returns the operational efficiency factor for a building based on its PowerStatus.
    /// <list type="bullet">
    /// <item>POWERED → 1.0 (full capacity)</item>
    /// <item>CONSTRAINED → <see cref="GameConstants.ConstrainedEfficiencyFactor"/> (partial capacity)</item>
    /// <item>OFFLINE → 0.0 (completely stopped)</item>
    /// </list>
    /// </summary>
    public static decimal GetPowerEfficiency(Building building) => building.PowerStatus switch
    {
        Data.Entities.PowerStatus.Constrained => GameConstants.ConstrainedEfficiencyFactor,
        Data.Entities.PowerStatus.Offline     => 0m,
        _                                     => 1m
    };

    /// <summary>Returns units that this unit pushes resources TO (outgoing links).</summary>
    public List<BuildingUnit> GetOutgoingLinkedUnits(BuildingUnit unit)
    {
        if (!UnitsByBuildingPosition.TryGetValue(unit.BuildingId, out var posMap))
            return [];

        var neighbors = new List<BuildingUnit>();
        if (unit.LinkRight && posMap.TryGetValue((unit.GridX + 1, unit.GridY), out var right))
            neighbors.Add(right);
        if (unit.LinkLeft && posMap.TryGetValue((unit.GridX - 1, unit.GridY), out var left))
            neighbors.Add(left);
        if (unit.LinkDown && posMap.TryGetValue((unit.GridX, unit.GridY + 1), out var down))
            neighbors.Add(down);
        if (unit.LinkUp && posMap.TryGetValue((unit.GridX, unit.GridY - 1), out var up))
            neighbors.Add(up);
        if (unit.LinkDownRight && posMap.TryGetValue((unit.GridX + 1, unit.GridY + 1), out var downRight))
            neighbors.Add(downRight);
        if (unit.LinkDownLeft && posMap.TryGetValue((unit.GridX - 1, unit.GridY + 1), out var downLeft))
            neighbors.Add(downLeft);
        if (unit.LinkUpRight && posMap.TryGetValue((unit.GridX + 1, unit.GridY - 1), out var upRight))
            neighbors.Add(upRight);
        if (unit.LinkUpLeft && posMap.TryGetValue((unit.GridX - 1, unit.GridY - 1), out var upLeft))
            neighbors.Add(upLeft);

        return neighbors;
    }

    /// <summary>Returns units that push resources INTO this unit (incoming links).</summary>
    public List<BuildingUnit> GetIncomingLinkedUnits(BuildingUnit unit)
    {
        if (!UnitsByBuildingPosition.TryGetValue(unit.BuildingId, out var posMap))
            return [];

        var incoming = new List<BuildingUnit>();
        if (posMap.TryGetValue((unit.GridX - 1, unit.GridY), out var left) && left.LinkRight)
            incoming.Add(left);
        if (posMap.TryGetValue((unit.GridX + 1, unit.GridY), out var right) && right.LinkLeft)
            incoming.Add(right);
        if (posMap.TryGetValue((unit.GridX, unit.GridY - 1), out var up) && up.LinkDown)
            incoming.Add(up);
        if (posMap.TryGetValue((unit.GridX, unit.GridY + 1), out var down) && down.LinkUp)
            incoming.Add(down);
        if (posMap.TryGetValue((unit.GridX - 1, unit.GridY - 1), out var upLeft) && upLeft.LinkDownRight)
            incoming.Add(upLeft);
        if (posMap.TryGetValue((unit.GridX + 1, unit.GridY - 1), out var upRight) && upRight.LinkDownLeft)
            incoming.Add(upRight);
        if (posMap.TryGetValue((unit.GridX - 1, unit.GridY + 1), out var downLeft) && downLeft.LinkUpRight)
            incoming.Add(downLeft);
        if (posMap.TryGetValue((unit.GridX + 1, unit.GridY + 1), out var downRight) && downRight.LinkUpLeft)
            incoming.Add(downRight);

        return incoming;
    }

    /// <summary>Finds the best matching brand for a company and product.</summary>
    public Brand? FindBrand(Guid companyId, Guid? productTypeId, string? industry)
    {
        if (!BrandsByCompany.TryGetValue(companyId, out var brands))
            return null;

        // Prefer product-specific, then category, then company-wide
        if (productTypeId.HasValue)
        {
            var productBrand = brands.FirstOrDefault(b =>
                b.Scope == BrandScope.Product && b.ProductTypeId == productTypeId);
            if (productBrand is not null) return productBrand;
        }

        if (!string.IsNullOrEmpty(industry))
        {
            var categoryBrand = brands.FirstOrDefault(b =>
                b.Scope == BrandScope.Category && b.IndustryCategory == industry);
            if (categoryBrand is not null) return categoryBrand;
        }

        return brands.FirstOrDefault(b => b.Scope == BrandScope.Company);
    }

    /// <summary>Finds or creates a brand for a company and product.</summary>
    public Brand GetOrCreateBrand(Guid companyId, Guid productTypeId, string brandName)
    {
        if (!BrandsByCompany.TryGetValue(companyId, out var brands))
        {
            brands = [];
            BrandsByCompany[companyId] = brands;
        }

        var existing = brands.FirstOrDefault(b =>
            b.Scope == BrandScope.Product && b.ProductTypeId == productTypeId);
        if (existing is not null) return existing;

        var brand = new Brand
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            Name = brandName,
            Scope = BrandScope.Product,
            ProductTypeId = productTypeId,
            Awareness = 0m,
            Quality = 0m
        };
        brands.Add(brand);
        Db.Brands.Add(brand);
        return brand;
    }
}
