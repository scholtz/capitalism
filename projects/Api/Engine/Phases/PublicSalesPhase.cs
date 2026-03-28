using Api.Data.Entities;

namespace Api.Engine.Phases;

/// <summary>
/// Sells products from PUBLIC_SALES units to the city population.
/// Demand is driven by city population, price competitiveness, product quality,
/// and brand awareness. Revenue is credited to the owning company.
/// Runs early so that sales consume inventory produced in prior ticks.
/// </summary>
public sealed class PublicSalesPhase : ITickPhase
{
    public string Name => "PublicSales";
    public int Order => 200;

    public Task ProcessAsync(TickContext context)
    {
        if (!context.BuildingsByType.TryGetValue(BuildingType.SalesShop, out var shops))
            return Task.CompletedTask;

        foreach (var building in shops)
        {
            if (!context.UnitsByBuilding.TryGetValue(building.Id, out var units))
                continue;
            if (!context.CitiesById.TryGetValue(building.CityId, out var city))
                continue;
            if (!context.CompaniesById.TryGetValue(building.CompanyId, out var company))
                continue;

            foreach (var unit in units)
            {
                if (unit.UnitType != UnitType.PublicSales) continue;
                ProcessSalesUnit(context, building, unit, city, company);
            }
        }

        return Task.CompletedTask;
    }

    private static void ProcessSalesUnit(
        TickContext context,
        Building building,
        BuildingUnit unit,
        City city,
        Company company)
    {
        if (!context.InventoryByUnit.TryGetValue(unit.Id, out var inventories))
            return;

        var salesCapacity = GameConstants.SalesCapacity(unit.Level);
        var totalSold = 0m;

        foreach (var inv in inventories)
        {
            if (inv.Quantity <= 0m) continue;
            if (totalSold >= salesCapacity) break;

            // Determine product type and price.
            Guid? productTypeId = inv.ProductTypeId ?? inv.ResourceTypeId;
            if (productTypeId is null) continue;

            decimal basePrice;
            string? industry = null;
            string? productName = null;
            if (inv.ProductTypeId.HasValue && context.ProductTypesById.TryGetValue(inv.ProductTypeId.Value, out var pt))
            {
                basePrice = pt.BasePrice;
                industry = pt.Industry;
                productName = pt.Name;
            }
            else if (inv.ResourceTypeId.HasValue && context.ResourceTypesById.TryGetValue(inv.ResourceTypeId.Value, out var rt))
            {
                basePrice = rt.BasePrice;
            }
            else
            {
                continue;
            }

            var price = unit.MinPrice ?? basePrice;
            if (price <= 0m) price = basePrice;

            // Demand formula.
            var baseDemand = city.Population * GameConstants.BaseDemandPerCapita;
            var priceRatio = basePrice > 0m ? price / basePrice : 1m;
            var priceMultiplier = Math.Max(0m, 2m - priceRatio); // demand drops as price exceeds base
            var qualityMultiplier = Math.Max(0.1m, inv.Quality);

            // Brand factor.
            var brand = context.FindBrand(building.CompanyId, inv.ProductTypeId, industry);
            var brandMultiplier = 0.3m + (brand?.Awareness ?? 0m) * 0.7m;

            var demand = baseDemand * priceMultiplier * qualityMultiplier * brandMultiplier;
            var remaining = salesCapacity - totalSold;
            var sold = Math.Min(demand, Math.Min(inv.Quantity, remaining));
            sold = Math.Max(0m, Math.Floor(sold * 10000m) / 10000m); // round down to 4 dp

            if (sold <= 0m) continue;

            // Record ledger entry for revenue
            context.Db.LedgerEntries.Add(new LedgerEntry
            {
                Id = Guid.NewGuid(),
                CompanyId = company.Id,
                BuildingId = building.Id,
                BuildingUnitId = unit.Id,
                Category = LedgerCategory.Revenue,
                Description = productName is not null ? $"Public sales: {productName}" : "Public sales",
                Amount = sold * price,
                RecordedAtTick = context.CurrentTick,
                RecordedAtUtc = DateTime.UtcNow,
                ProductTypeId = inv.ProductTypeId,
                ResourceTypeId = inv.ResourceTypeId,
            });

            // Record sales snapshot
            context.Db.PublicSalesRecords.Add(new PublicSalesRecord
            {
                Id = Guid.NewGuid(),
                BuildingUnitId = unit.Id,
                BuildingId = building.Id,
                CompanyId = company.Id,
                CityId = building.CityId,
                ProductTypeId = inv.ProductTypeId,
                ResourceTypeId = inv.ResourceTypeId,
                Tick = context.CurrentTick,
                RecordedAtUtc = DateTime.UtcNow,
                QuantitySold = sold,
                PricePerUnit = price,
                Revenue = sold * price,
                Demand = demand,
                SalesCapacity = salesCapacity,
            });

            inv.Quantity -= sold;
            company.Cash += sold * price;
            totalSold += sold;
        }
    }
}
