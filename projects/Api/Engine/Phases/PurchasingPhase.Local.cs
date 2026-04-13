using Api.Data.Entities;
using Api.Utilities;

namespace Api.Engine.Phases;

public sealed partial class PurchasingPhase
{
    private static List<(Building Building, BuildingUnit Unit, Inventory Inventory, decimal PricePerUnit, decimal TransitCostPerUnit, decimal DeliveredPricePerUnit)> GetLocalB2BSupplies(
        TickContext context,
        Building destinationBuilding,
        BuildingUnit purchaseUnit,
        Guid? resourceId,
        Guid? productId,
        decimal itemWeightPerUnit,
        decimal maxPrice,
        decimal minQuality)
    {
        var matchingSupplies = new List<(Building Building, BuildingUnit Unit, Inventory Inventory, decimal PricePerUnit, decimal TransitCostPerUnit, decimal DeliveredPricePerUnit)>();

        foreach (var (buildingId, units) in context.UnitsByBuilding)
        {
            if (!context.BuildingsById.TryGetValue(buildingId, out var building))
                continue;
            if (building.Id == destinationBuilding.Id || building.CityId != destinationBuilding.CityId)
                continue;

            foreach (var unit in units)
            {
                if (unit.UnitType != UnitType.B2BSales)
                    continue;
                if (context.UnitsUnderUpgrade.Contains(unit.Id))
                    continue;
                if (purchaseUnit.VendorLockCompanyId.HasValue && building.CompanyId != purchaseUnit.VendorLockCompanyId.Value)
                    continue;
                if (!purchaseUnit.VendorLockCompanyId.HasValue && building.CompanyId != destinationBuilding.CompanyId)
                    continue;
                if (!context.InventoryByUnit.TryGetValue(unit.Id, out var inventories))
                    continue;

                foreach (var inventory in inventories)
                {
                    if (inventory.Quantity <= 0m)
                        continue;
                    if (resourceId.HasValue && inventory.ResourceTypeId != resourceId)
                        continue;
                    if (productId.HasValue && inventory.ProductTypeId != productId)
                        continue;
                    if (resourceId is null && productId is null)
                        continue;
                    if (inventory.Quality < minQuality)
                        continue;

                    var price = unit.MinPrice ?? GetBasePrice(context, inventory.ResourceTypeId, inventory.ProductTypeId);
                    var transitCostPerUnit = ComputeBuildingTransitCostPerUnit(building, destinationBuilding, itemWeightPerUnit);
                    var deliveredPricePerUnit = price + transitCostPerUnit;
                    if (price <= 0m || deliveredPricePerUnit > maxPrice)
                        continue;

                    matchingSupplies.Add((building, unit, inventory, price, transitCostPerUnit, deliveredPricePerUnit));
                }
            }
        }

        return matchingSupplies
            .OrderBy(supply => supply.DeliveredPricePerUnit)
            .ThenByDescending(supply => supply.Inventory.Quality)
            .ToList();
    }

    private static decimal GetBasePrice(TickContext context, Guid? resourceId, Guid? productId)
    {
        if (productId.HasValue && context.ProductTypesById.TryGetValue(productId.Value, out var productType))
            return productType.BasePrice;

        if (resourceId.HasValue && context.ResourceTypesById.TryGetValue(resourceId.Value, out var resourceType))
            return resourceType.BasePrice;

        return 0m;
    }

    private static decimal ComputeBuildingTransitCostPerUnit(
        Building sourceBuilding,
        Building destinationBuilding,
        decimal itemWeightPerUnit)
    {
        return GlobalExchangeCalculator.ComputeTransitCostPerUnit(
            sourceBuilding.Latitude,
            sourceBuilding.Longitude,
            destinationBuilding.Latitude,
            destinationBuilding.Longitude,
            itemWeightPerUnit);
    }
}
