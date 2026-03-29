using Api.Data.Entities;
using Api.Utilities;

namespace Api.Engine.Phases;

/// <summary>
/// Fills PURCHASE units by matching against active exchange sell orders and/or
/// the city-level global exchange (infinite counterparty).
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

        var totalBought = 0m;
        var inventoryQuality = 0m;

        // Phase 1: Player-placed exchange orders (LOCAL or OPTIMAL source).
        if (purchaseSource is "LOCAL" or "OPTIMAL")
        {
            var matchingOrders = context.ActiveExchangeOrders
                .Where(o => o.Side == "SELL"
                            && o.IsActive
                            && o.RemainingQuantity > 0m
                            && o.PricePerUnit <= maxPrice
                            && (resourceId is null || o.ResourceTypeId == resourceId)
                            && (productId is null || o.ProductTypeId == productId))
                .OrderBy(o => o.PricePerUnit)
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
                var cost = fill * order.PricePerUnit;

                if (company.Cash < cost)
                {
                    fill = company.Cash / order.PricePerUnit;
                    fill = Math.Floor(fill * 10000m) / 10000m;
                    cost = fill * order.PricePerUnit;
                }

                if (fill <= 0m) break;

                order.RemainingQuantity -= fill;
                if (order.RemainingQuantity <= 0m) order.IsActive = false;

                company.Cash -= cost;

                context.Db.LedgerEntries.Add(new LedgerEntry
                {
                    Id = Guid.NewGuid(),
                    CompanyId = company.Id,
                    BuildingId = building.Id,
                    BuildingUnitId = unit.Id,
                    Category = LedgerCategory.PurchasingCost,
                    Description = resourceId.HasValue ? "Purchase: raw material" : "Purchase: product",
                    Amount = -cost,
                    RecordedAtTick = context.CurrentTick,
                    RecordedAtUtc = DateTime.UtcNow,
                    ResourceTypeId = resourceId,
                    ProductTypeId = productId,
                });

                // Credit selling company.
                if (context.CompaniesById.TryGetValue(order.CompanyId, out var seller))
                    seller.Cash += cost;

                totalBought += fill;
                // Quality is tracked per-source; for player orders use a default of 0.7 if not available.
                inventoryQuality = 0.7m;
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
                inventoryQuality = inventoryQuality > 0m
                    ? (inventoryQuality + globalQuality) / 2m  // blend with existing inventory quality
                    : globalQuality;
            }
        }

        if (totalBought > 0m)
        {
            var inv = context.GetOrCreateUnitInventory(
                building.Id, unit.Id, resourceId, productId);
            // Blend new inventory quality with existing.
            var existingQty = inv.Quantity;
            var newTotalQty = existingQty + totalBought;
            if (newTotalQty > 0m)
                inv.Quality = ((inv.Quality * existingQty) + (inventoryQuality * totalBought)) / newTotalQty;
            inv.Quantity += totalBought;
        }
    }

    /// <summary>
    /// Attempts to fill the purchase unit from the city-level global exchange
    /// (infinite counterparty). Picks the source city with the lowest delivered
    /// price (exchange price + transit cost) that satisfies MaxPrice and MinQuality.
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

        // Evaluate global exchange offers from every source city, sorted by delivered price.
        var bestOffer = context.CitiesById.Values
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

                return (sourceCity, deliveredPrice, quality);
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

        context.Db.LedgerEntries.Add(new LedgerEntry
        {
            Id = Guid.NewGuid(),
            CompanyId = company.Id,
            BuildingId = building.Id,
            BuildingUnitId = unit.Id,
            Category = LedgerCategory.PurchasingCost,
            Description = $"Global exchange purchase: {resource.Name} from {bestOffer.sourceCity.Name}",
            Amount = -totalCost,
            RecordedAtTick = context.CurrentTick,
            RecordedAtUtc = DateTime.UtcNow,
            ResourceTypeId = resourceId,
        });

        return (amountToBuy, bestOffer.quality, totalCost);
    }
}
