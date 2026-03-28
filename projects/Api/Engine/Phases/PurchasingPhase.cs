using Api.Data.Entities;

namespace Api.Engine.Phases;

/// <summary>
/// Fills PURCHASE units by matching against active exchange sell orders.
/// Purchases respect the unit's MaxPrice, PurchaseCapacity, MinQuality, and
/// optional vendor lock. Company cash is debited for each fill.
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

        // Find matching SELL orders on the exchange, cheapest first.
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

        var totalBought = 0m;
        foreach (var order in matchingOrders)
        {
            if (totalBought >= maxBuy) break;

            var want = maxBuy - totalBought;
            var available = order.RemainingQuantity;
            var fill = Math.Min(want, available);
            var cost = fill * order.PricePerUnit;

            if (company.Cash < cost)
            {
                // Buy what we can afford.
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
        }

        if (totalBought > 0m)
        {
            var inv = context.GetOrCreateUnitInventory(
                building.Id, unit.Id, resourceId, productId);
            inv.Quantity += totalBought;
        }
    }
}
