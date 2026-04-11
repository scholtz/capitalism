using Api.Data.Entities;
using Api.Utilities;

namespace Api.Engine.Phases;

/// <summary>
/// Fills PURCHASE units by matching against active exchange sell orders,
/// same-city B2B supply, and/or the city-level global exchange
/// (infinite counterparty).
/// Purchase source is controlled by <see cref="BuildingUnit.PurchaseSource"/>:
/// <list type="bullet">
///   <item><description>LOCAL  – player-placed exchange orders only.</description></item>
///   <item><description>EXCHANGE – global city exchange only (transit-cost-aware).</description></item>
///   <item><description>OPTIMAL (default) – best available price across both sources.</description></item>
/// </list>
/// Purchases respect the unit's MaxPrice (applied to the fully delivered price),
/// PurchaseCapacity, MinQuality, and optional vendor lock. Company cash is
/// debited for each fill.
/// </summary>
public sealed class PurchasingPhase : ITickPhase
{
    public string Name => "Purchasing";
    public int Order => 600;

    public Task ProcessAsync(TickContext context)
    {
        // Gather all purchase units across all buildings.
        foreach (var (buildingId, units) in context.UnitsByBuilding)
        {
            if (!context.BuildingsById.TryGetValue(buildingId, out var building))
                continue;
            if (!context.CompaniesById.TryGetValue(building.CompanyId, out var company))
                continue;

            foreach (var unit in units)
            {
                if (unit.UnitType != UnitType.Purchase) continue;
                ProcessPurchaseUnit(context, building, unit, company);
            }
        }

        return Task.CompletedTask;
    }

    private static void ProcessPurchaseUnit(
        TickContext context,
        Building building,
        BuildingUnit unit,
        Company company)
    {
        var resourceId = unit.ResourceTypeId;
        var productId = unit.ProductTypeId;
        if (resourceId is null && productId is null) return;

        var capacity = GameConstants.PurchaseCapacity(unit.Level);
        var space = context.GetUnitFreeSpace(unit);
        var maxBuy = Math.Min(capacity, space);
        if (maxBuy <= 0m) return;

        var maxPrice = unit.MaxPrice ?? decimal.MaxValue;
        var minQuality = unit.MinQuality ?? 0m;
        var purchaseSource = unit.PurchaseSource ?? "OPTIMAL";
        var itemWeightPerUnit = GlobalExchangeCalculator.ComputeItemWeightPerUnit(
            resourceId,
            productId,
            context.ResourceTypesById,
            context.ProductTypesById,
            context.RecipesByProduct);

        var totalBought = 0m;
        var totalSourcingCost = 0m;
        var weightedQualityTotal = 0m;

        // Phase 1: Player-placed exchange orders (LOCAL or OPTIMAL source).
        if (purchaseSource is "LOCAL" or "OPTIMAL")
        {
            var matchingOrders = context.ActiveExchangeOrders
                .Where(o => o.Side == "SELL"
                            && o.IsActive
                            && o.RemainingQuantity > 0m
                            && (resourceId is null || o.ResourceTypeId == resourceId)
                            && (productId is null || o.ProductTypeId == productId))
                .Where(o =>
                {
                    if (!context.BuildingsById.TryGetValue(o.ExchangeBuildingId, out var exchangeBuilding))
                    {
                        return false;
                    }

                    var deliveredPricePerUnit = o.PricePerUnit + ComputeBuildingTransitCostPerUnit(exchangeBuilding, building, itemWeightPerUnit);
                    return deliveredPricePerUnit <= maxPrice;
                })
                .OrderBy(o =>
                {
                    var exchangeBuilding = context.BuildingsById[o.ExchangeBuildingId];
                    return o.PricePerUnit + ComputeBuildingTransitCostPerUnit(exchangeBuilding, building, itemWeightPerUnit);
                })
                .ToList();

            if (unit.VendorLockCompanyId.HasValue)
            {
                matchingOrders = matchingOrders
                    .Where(o => o.CompanyId == unit.VendorLockCompanyId.Value)
                    .ToList();
            }

            foreach (var order in matchingOrders)
            {
                if (totalBought >= maxBuy) break;

                var want = maxBuy - totalBought;
                var available = order.RemainingQuantity;
                var fill = Math.Min(want, available);
                if (!context.BuildingsById.TryGetValue(order.ExchangeBuildingId, out var exchangeBuilding))
                {
                    continue;
                }

                var transitCostPerUnit = ComputeBuildingTransitCostPerUnit(exchangeBuilding, building, itemWeightPerUnit);
                var goodsCost = fill * order.PricePerUnit;
                var shippingCost = fill * transitCostPerUnit;
                var totalDeliveredCost = goodsCost + shippingCost;

                if (company.Cash < totalDeliveredCost)
                {
                    var deliveredPricePerUnit = order.PricePerUnit + transitCostPerUnit;
                    fill = company.Cash / deliveredPricePerUnit;
                    fill = Math.Floor(fill * 10000m) / 10000m;
                    goodsCost = fill * order.PricePerUnit;
                    shippingCost = fill * transitCostPerUnit;
                    totalDeliveredCost = goodsCost + shippingCost;
                }

                if (fill <= 0m) break;

                order.RemainingQuantity -= fill;
                if (order.RemainingQuantity <= 0m) order.IsActive = false;

                company.Cash -= totalDeliveredCost;

                context.Db.LedgerEntries.Add(new LedgerEntry
                {
                    Id = Guid.NewGuid(),
                    CompanyId = company.Id,
                    BuildingId = building.Id,
                    BuildingUnitId = unit.Id,
                    Category = LedgerCategory.PurchasingCost,
                    Description = resourceId.HasValue ? "Purchase: raw material" : "Purchase: product",
                    Amount = -goodsCost,
                    RecordedAtTick = context.CurrentTick,
                    RecordedAtUtc = DateTime.UtcNow,
                    ResourceTypeId = resourceId,
                    ProductTypeId = productId,
                });

                if (shippingCost > 0m)
                {
                    context.Db.LedgerEntries.Add(new LedgerEntry
                    {
                        Id = Guid.NewGuid(),
                        CompanyId = company.Id,
                        BuildingId = building.Id,
                        BuildingUnitId = unit.Id,
                        Category = LedgerCategory.ShippingCost,
                        Description = resourceId.HasValue ? "Shipping: exchange order raw material" : "Shipping: exchange order product",
                        Amount = -shippingCost,
                        RecordedAtTick = context.CurrentTick,
                        RecordedAtUtc = DateTime.UtcNow,
                        ResourceTypeId = resourceId,
                        ProductTypeId = productId,
                    });
                }

                // Credit selling company.
                if (context.CompaniesById.TryGetValue(order.CompanyId, out var seller))
                    seller.Cash += goodsCost;

                totalBought += fill;
                totalSourcingCost += totalDeliveredCost;
                weightedQualityTotal += fill * 0.7m;
            }
        }

        if (purchaseSource is "LOCAL" or "OPTIMAL" && totalBought < maxBuy)
        {
            var localSupplies = GetLocalB2BSupplies(
                context,
                building,
                unit,
                resourceId,
                productId,
                itemWeightPerUnit,
                maxPrice,
                minQuality);

            foreach (var supply in localSupplies)
            {
                if (totalBought >= maxBuy) break;

                var wanted = maxBuy - totalBought;
                var eligibleTickStartQuantity = context.GetTickStartRemainingQuantity(supply.Inventory);
                if (eligibleTickStartQuantity <= 0m)
                    continue;

                var fill = Math.Min(wanted, eligibleTickStartQuantity);
                if (fill <= 0m) continue;

                var sellerIsSameCompany = supply.Building.CompanyId == company.Id;
                decimal goodsCost = fill * supply.PricePerUnit;
                decimal shippingCost = fill * supply.TransitCostPerUnit;
                decimal deliveredCost = goodsCost + shippingCost;
                if (company.Cash < shippingCost)
                {
                    fill = company.Cash / Math.Max(supply.TransitCostPerUnit, 0.0001m);
                    fill = Math.Floor(fill * 10000m) / 10000m;
                    goodsCost = fill * supply.PricePerUnit;
                    shippingCost = fill * supply.TransitCostPerUnit;
                    deliveredCost = goodsCost + shippingCost;
                }

                if (!sellerIsSameCompany)
                {
                    if (company.Cash < deliveredCost)
                    {
                        fill = company.Cash / supply.DeliveredPricePerUnit;
                        fill = Math.Floor(fill * 10000m) / 10000m;
                        goodsCost = fill * supply.PricePerUnit;
                        shippingCost = fill * supply.TransitCostPerUnit;
                        deliveredCost = goodsCost + shippingCost;
                    }
                }

                if (fill <= 0m) break;

                var withdrawn = context.WithdrawInventory(supply.Inventory, fill);
                if (withdrawn.Quantity <= 0m) continue;

                if (supply.Inventory.BuildingUnitId.HasValue)
                {
                    context.RecordUnitResourceHistory(
                        supply.Building.Id,
                        supply.Inventory.BuildingUnitId.Value,
                        supply.Inventory.ResourceTypeId,
                        supply.Inventory.ProductTypeId,
                        outflowQuantity: withdrawn.Quantity);
                }

                if (sellerIsSameCompany)
                {
                    // Internal transfer: record as sale for seller and purchase for buyer
                    context.Db.LedgerEntries.Add(new LedgerEntry
                    {
                        Id = Guid.NewGuid(),
                        CompanyId = supply.Building.CompanyId, // Seller
                        BuildingId = supply.Building.Id,
                        BuildingUnitId = supply.Unit.Id,
                        Category = LedgerCategory.Revenue,
                        Description = resourceId.HasValue ? "Internal sale: raw material" : "Internal sale: product",
                        Amount = goodsCost,
                        RecordedAtTick = context.CurrentTick,
                        RecordedAtUtc = DateTime.UtcNow,
                        ResourceTypeId = resourceId,
                        ProductTypeId = productId,
                    });

                    context.Db.LedgerEntries.Add(new LedgerEntry
                    {
                        Id = Guid.NewGuid(),
                        CompanyId = company.Id, // Buyer
                        BuildingId = building.Id,
                        BuildingUnitId = unit.Id,
                        Category = LedgerCategory.PurchasingCost,
                        Description = resourceId.HasValue ? "Internal purchase: raw material" : "Internal purchase: product",
                        Amount = -goodsCost,
                        RecordedAtTick = context.CurrentTick,
                        RecordedAtUtc = DateTime.UtcNow,
                        ResourceTypeId = resourceId,
                        ProductTypeId = productId,
                    });

                    if (shippingCost > 0m)
                    {
                        company.Cash -= shippingCost;
                        context.Db.LedgerEntries.Add(new LedgerEntry
                        {
                            Id = Guid.NewGuid(),
                            CompanyId = company.Id,
                            BuildingId = building.Id,
                            BuildingUnitId = unit.Id,
                            Category = LedgerCategory.ShippingCost,
                            Description = resourceId.HasValue ? "Shipping: internal raw material transfer" : "Shipping: internal product transfer",
                            Amount = -shippingCost,
                            RecordedAtTick = context.CurrentTick,
                            RecordedAtUtc = DateTime.UtcNow,
                            ResourceTypeId = resourceId,
                            ProductTypeId = productId,
                        });
                    }

                    totalSourcingCost += deliveredCost;
                }
                else
                {
                    goodsCost = withdrawn.Quantity * supply.PricePerUnit;
                    shippingCost = withdrawn.Quantity * supply.TransitCostPerUnit;
                    deliveredCost = goodsCost + shippingCost;
                    company.Cash -= deliveredCost;

                    if (context.CompaniesById.TryGetValue(supply.Building.CompanyId, out var seller))
                        seller.Cash += goodsCost;

                    context.Db.LedgerEntries.Add(new LedgerEntry
                    {
                        Id = Guid.NewGuid(),
                        CompanyId = company.Id,
                        BuildingId = building.Id,
                        BuildingUnitId = unit.Id,
                        Category = LedgerCategory.PurchasingCost,
                        Description = resourceId.HasValue ? "Purchase: local raw material" : "Purchase: local product",
                        Amount = -goodsCost,
                        RecordedAtTick = context.CurrentTick,
                        RecordedAtUtc = DateTime.UtcNow,
                        ResourceTypeId = resourceId,
                        ProductTypeId = productId,
                    });

                    if (shippingCost > 0m)
                    {
                        context.Db.LedgerEntries.Add(new LedgerEntry
                        {
                            Id = Guid.NewGuid(),
                            CompanyId = company.Id,
                            BuildingId = building.Id,
                            BuildingUnitId = unit.Id,
                            Category = LedgerCategory.ShippingCost,
                            Description = resourceId.HasValue ? "Shipping: local raw material" : "Shipping: local product",
                            Amount = -shippingCost,
                            RecordedAtTick = context.CurrentTick,
                            RecordedAtUtc = DateTime.UtcNow,
                            ResourceTypeId = resourceId,
                            ProductTypeId = productId,
                        });
                    }

                    totalSourcingCost += deliveredCost;
                }

                totalBought += withdrawn.Quantity;
                weightedQualityTotal += withdrawn.Quantity * supply.Inventory.Quality;
            }
        }

        // Phase 2: Global city exchange (infinite counterparty) for EXCHANGE or OPTIMAL source.
        // Only applies to raw resource purchases (not products) in the first version.
        if (purchaseSource is "EXCHANGE" or "OPTIMAL" && resourceId.HasValue && totalBought < maxBuy)
        {
            var remaining = maxBuy - totalBought;
            var (globalBought, globalQuality, globalCost) = BuyFromGlobalExchange(
                context, building, unit, company, resourceId.Value,
                remaining, maxPrice, minQuality);

            if (globalBought > 0m)
            {
                totalBought += globalBought;
                totalSourcingCost += globalCost;
                weightedQualityTotal += globalBought * globalQuality;
            }
        }

        if (totalBought > 0m)
        {
            var inv = context.GetOrCreateUnitInventory(
                building.Id, unit.Id, resourceId, productId);
            var inventoryQuality = weightedQualityTotal > 0m ? weightedQualityTotal / totalBought : (decimal?)null;
            context.AddInventory(inv, totalBought, totalSourcingCost, inventoryQuality);
            context.RecordUnitResourceHistory(
                building.Id,
                unit.Id,
                resourceId,
                productId,
                inflowQuantity: totalBought);
        }
    }

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

    /// <summary>
    /// Attempts to fill the purchase unit from the city-level global exchange
    /// (infinite counterparty). Picks the source city with the lowest delivered
    /// <summary>
    /// Attempts to fill the purchase unit from the city-level global exchange
    /// (infinite counterparty). Picks the source city with the lowest delivered
    /// price (exchange price + transit cost) that satisfies MaxPrice and MinQuality.
    /// When <paramref name="unit"/> has a <see cref="BuildingUnit.LockedCityId"/> set,
    /// only that specific city is considered as a source.
    /// Returns the amount bought, quality, and total cost.
    /// </summary>
    private static (decimal amountBought, decimal quality, decimal cost) BuyFromGlobalExchange(
        TickContext context,
        Building building,
        BuildingUnit unit,
        Company company,
        Guid resourceId,
        decimal maxAmountToBuy,
        decimal maxPrice,
        decimal minQuality)
    {
        if (!context.ResourceTypesById.TryGetValue(resourceId, out var resource)) return (0m, 0m, 0m);
        if (!context.CitiesById.TryGetValue(building.CityId, out var destinationCity)) return (0m, 0m, 0m);

        // When LockedCityId is set AND purchase source is EXCHANGE, restrict sourcing to only that city.
        // For OPTIMAL mode, LockedCityId must be ignored so the engine can find the best price globally.
        var purchaseSource = unit.PurchaseSource ?? "OPTIMAL";
        var candidateCities = unit.LockedCityId.HasValue && purchaseSource == "EXCHANGE"
            ? context.CitiesById.Values.Where(c => c.Id == unit.LockedCityId.Value)
            : context.CitiesById.Values;

        // Evaluate global exchange offers from candidate source cities, sorted by delivered price.
        var bestOffer = candidateCities
            .Select(sourceCity =>
            {
                var abundance = context.ResourcesByCity
                    .GetValueOrDefault(sourceCity.Id)
                    ?.FirstOrDefault(cr => cr.ResourceTypeId == resourceId)
                    ?.Abundance ?? GlobalExchangeCalculator.DefaultMissingAbundance;

                var exchangePrice = GlobalExchangeCalculator.ComputeExchangePrice(sourceCity, resource, abundance);
                var transitCost = GlobalExchangeCalculator.ComputeTransitCostPerUnit(sourceCity, destinationCity, resource);
                var deliveredPrice = exchangePrice + transitCost;
                var quality = GlobalExchangeCalculator.ComputeExchangeQuality(abundance);

                return (sourceCity, exchangePrice, transitCost, deliveredPrice, quality);
            })
            .Where(o => o.deliveredPrice <= maxPrice && o.quality >= minQuality)
            .OrderBy(o => o.deliveredPrice)
            .ThenByDescending(o => o.quality)
            .FirstOrDefault();

        if (bestOffer == default) return (0m, 0m, 0m);

        // Global exchange is an infinite counterparty – buy the full amount requested.
        var amountToBuy = maxAmountToBuy;
        var totalCost = amountToBuy * bestOffer.deliveredPrice;

        if (company.Cash < totalCost)
        {
            amountToBuy = company.Cash / bestOffer.deliveredPrice;
            amountToBuy = Math.Floor(amountToBuy * 10000m) / 10000m;
            totalCost = amountToBuy * bestOffer.deliveredPrice;
        }

        if (amountToBuy <= 0m) return (0m, 0m, 0m);

        company.Cash -= totalCost;
        var purchasingCost = amountToBuy * bestOffer.exchangePrice;
        var shippingCost = amountToBuy * bestOffer.transitCost;

        context.Db.LedgerEntries.Add(new LedgerEntry
        {
            Id = Guid.NewGuid(),
            CompanyId = company.Id,
            BuildingId = building.Id,
            BuildingUnitId = unit.Id,
            Category = LedgerCategory.PurchasingCost,
            Description = $"Global exchange purchase: {resource.Name} from {bestOffer.sourceCity.Name}",
            Amount = -purchasingCost,
            RecordedAtTick = context.CurrentTick,
            RecordedAtUtc = DateTime.UtcNow,
            ResourceTypeId = resourceId,
        });

        if (shippingCost > 0m)
        {
            context.Db.LedgerEntries.Add(new LedgerEntry
            {
                Id = Guid.NewGuid(),
                CompanyId = company.Id,
                BuildingId = building.Id,
                BuildingUnitId = unit.Id,
                Category = LedgerCategory.ShippingCost,
                Description = $"Shipping: {resource.Name} from {bestOffer.sourceCity.Name}",
                Amount = -shippingCost,
                RecordedAtTick = context.CurrentTick,
                RecordedAtUtc = DateTime.UtcNow,
                ResourceTypeId = resourceId,
            });
        }

        return (amountToBuy, bestOffer.quality, totalCost);
    }
}
