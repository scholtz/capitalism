using Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Api.Engine.Phases;

/// <summary>
/// Sells products from PUBLIC_SALES units to the city population.
/// Demand is driven by city population, price competitiveness, product quality,
/// and brand awareness. When multiple sellers compete in the same city for the
/// same product, total city demand is shared proportionally based on each
/// seller's competitiveness score (price attractiveness × quality × brand).
/// Revenue is credited to the owning company.
/// Runs early so that sales consume inventory produced in prior ticks.
/// </summary>
public sealed class PublicSalesPhase : ITickPhase
{
    public string Name => "PublicSales";
    public int Order => 200;

    /// <summary>
    /// Collects all potential sales offers across all shops in a city, then
    /// distributes city-level demand proportionally among competing sellers.
    /// </summary>
    public async Task ProcessAsync(TickContext context)
    {
        if (!context.BuildingsByType.TryGetValue(BuildingType.SalesShop, out var shops))
            return;

        var shopIds = shops.Select(shop => shop.Id).ToList();
        var lotsByBuildingId = await context.Db.BuildingLots
            .Where(lot => lot.BuildingId.HasValue && shopIds.Contains(lot.BuildingId.Value))
            .ToDictionaryAsync(lot => lot.BuildingId!.Value);

        // ── Phase 1: Gather every seller's offer for each (city, product) pair ──
        var offers = new List<SalesOffer>();

        foreach (var building in shops)
        {
            if (!context.UnitsByBuilding.TryGetValue(building.Id, out var units))
                continue;
            if (!context.CitiesById.TryGetValue(building.CityId, out var city))
                continue;
            if (!context.CompaniesById.TryGetValue(building.CompanyId, out var company))
                continue;

            var efficiency = TickContext.GetPowerEfficiency(building);
            if (efficiency <= 0m) continue;

            foreach (var unit in units)
            {
                if (unit.UnitType != UnitType.PublicSales) continue;
                if (!context.InventoryByUnit.TryGetValue(unit.Id, out var inventories))
                    continue;

                lotsByBuildingId.TryGetValue(building.Id, out var lot);
                var salesCapacity = GameConstants.SalesCapacity(unit.Level) * efficiency;
                var usedCapacity = 0m;

                foreach (var inv in inventories)
                {
                    if (inv.Quantity <= 0m) continue;
                    if (usedCapacity >= salesCapacity) break;

                    Guid? itemId = inv.ProductTypeId ?? inv.ResourceTypeId;
                    if (itemId is null) continue;

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

                    var populationIndex = lot?.PopulationIndex > 0m ? lot.PopulationIndex : 1m;
                    var priceRatio = basePrice > 0m ? price / basePrice : 1m;
                    var priceMultiplier = Math.Max(0m, 2m - priceRatio);
                    var qualityMultiplier = Math.Max(0.1m, inv.Quality);

                    var brand = context.FindBrand(building.CompanyId, inv.ProductTypeId, industry);
                    var brandMultiplier = 0.3m + (brand?.Awareness ?? 0m) * 0.7m;

                    // Competitiveness score determines market-share allocation.
                    // PopulationIndex represents foot traffic / location advantage.
                    var competitiveness = priceMultiplier * qualityMultiplier * brandMultiplier * populationIndex;
                    if (competitiveness <= 0m) continue;

                    var availableCapacity = salesCapacity - usedCapacity;
                    var maxCanSell = Math.Min(inv.Quantity, availableCapacity);

                    offers.Add(new SalesOffer
                    {
                        CityId = building.CityId,
                        ItemId = itemId.Value,
                        Building = building,
                        Unit = unit,
                        City = city,
                        Company = company,
                        Lot = lot,
                        Inventory = inv,
                        BasePrice = basePrice,
                        Price = price,
                        Industry = industry,
                        ProductName = productName,
                        PopulationIndex = populationIndex,
                        Competitiveness = competitiveness,
                        MaxCanSell = maxCanSell,
                    });

                    // Reserve capacity for this inventory slot.
                    usedCapacity += maxCanSell;
                }
            }
        }

        // ── Phase 2: Distribute demand per (city, product) among competing sellers ──
        var grouped = offers.GroupBy(o => (o.CityId, o.ItemId));

        // Track actual sales per unit to enforce sales capacity across products.
        var unitSoldTotals = new Dictionary<Guid, decimal>();

        // Pre-compute unit sales capacity to avoid redundant recalculations.
        var unitCapacityCache = new Dictionary<Guid, decimal>();

        foreach (var group in grouped)
        {
            var groupList = group.ToList();
            var firstOffer = groupList[0];
            var city = firstOffer.City;

            // City-level base demand for this product (population-driven, no location bias).
            var cityBaseDemand = city.Population * GameConstants.BaseDemandPerCapita;

            // Total competitiveness of all sellers (used for market-share split).
            var totalCompetitiveness = groupList.Sum(o => o.Competitiveness);

            foreach (var offer in groupList)
            {
                // Market share: each seller's fraction of city demand based on competitiveness.
                // For a single seller, marketShare = 1.0 and demand = cityBaseDemand × competitiveness.
                // For multiple sellers, demand is proportionally split so total ≤ cityBaseDemand × avgCompetitiveness.
                var marketShare = offer.Competitiveness / totalCompetitiveness;
                var demand = cityBaseDemand * marketShare;

                // Enforce unit-level sales capacity.
                unitSoldTotals.TryGetValue(offer.Unit.Id, out var unitSoldSoFar);

                if (!unitCapacityCache.TryGetValue(offer.Unit.Id, out var salesCapacity))
                {
                    salesCapacity = GameConstants.SalesCapacity(offer.Unit.Level)
                        * TickContext.GetPowerEfficiency(offer.Building);
                    unitCapacityCache[offer.Unit.Id] = salesCapacity;
                }

                var remainingCapacity = Math.Max(0m, salesCapacity - unitSoldSoFar);

                var sold = Math.Min(demand, Math.Min(offer.MaxCanSell, remainingCapacity));
                sold = Math.Max(0m, Math.Floor(sold * 10000m) / 10000m);
                if (sold <= 0m) continue;

                // Record ledger entry.
                context.Db.LedgerEntries.Add(new LedgerEntry
                {
                    Id = Guid.NewGuid(),
                    CompanyId = offer.Company.Id,
                    BuildingId = offer.Building.Id,
                    BuildingUnitId = offer.Unit.Id,
                    Category = LedgerCategory.Revenue,
                    Description = offer.ProductName is not null ? $"Public sales: {offer.ProductName}" : "Public sales",
                    Amount = sold * offer.Price,
                    RecordedAtTick = context.CurrentTick,
                    RecordedAtUtc = DateTime.UtcNow,
                    ProductTypeId = offer.Inventory.ProductTypeId,
                    ResourceTypeId = offer.Inventory.ResourceTypeId,
                });

                // Record sales snapshot.
                context.Db.PublicSalesRecords.Add(new PublicSalesRecord
                {
                    Id = Guid.NewGuid(),
                    BuildingUnitId = offer.Unit.Id,
                    BuildingId = offer.Building.Id,
                    CompanyId = offer.Company.Id,
                    CityId = offer.Building.CityId,
                    ProductTypeId = offer.Inventory.ProductTypeId,
                    ResourceTypeId = offer.Inventory.ResourceTypeId,
                    Tick = context.CurrentTick,
                    RecordedAtUtc = DateTime.UtcNow,
                    QuantitySold = sold,
                    PricePerUnit = offer.Price,
                    Revenue = sold * offer.Price,
                    Demand = demand,
                    SalesCapacity = salesCapacity,
                });

                context.RecordUnitResourceHistory(
                    offer.Building.Id,
                    offer.Unit.Id,
                    offer.Inventory.ResourceTypeId,
                    offer.Inventory.ProductTypeId,
                    outflowQuantity: sold);
                context.WithdrawInventory(offer.Inventory, sold);
                offer.Company.Cash += sold * offer.Price;
                unitSoldTotals[offer.Unit.Id] = unitSoldSoFar + sold;
            }
        }
    }

    /// <summary>
    /// Intermediate structure holding a single seller's offer for one product in one city.
    /// </summary>
    private sealed class SalesOffer
    {
        public Guid CityId { get; init; }
        public Guid ItemId { get; init; }
        public Building Building { get; init; } = null!;
        public BuildingUnit Unit { get; init; } = null!;
        public City City { get; init; } = null!;
        public Company Company { get; init; } = null!;
        public BuildingLot? Lot { get; init; }
        public Inventory Inventory { get; init; } = null!;
        public decimal BasePrice { get; init; }
        public decimal Price { get; init; }
        public string? Industry { get; init; }
        public string? ProductName { get; init; }
        public decimal PopulationIndex { get; init; }
        public decimal Competitiveness { get; init; }
        public decimal MaxCanSell { get; init; }
    }
}
