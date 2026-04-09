using Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Api.Engine.Phases;

/// <summary>
/// Sells products from PUBLIC_SALES units to the city population.
/// Demand is driven by city population, price competitiveness, product quality,
/// and brand awareness. When multiple sellers compete in the same city for the
/// same product, total city demand is shared proportionally based on each
/// seller's competitiveness score (price index × quality × brand).
/// A public-sell index then limits each offer to 0-50% of its current stock
/// based on market saturation, competition, quality, brand, and lot location,
/// while a separate price index (0-1) can reduce sales all the way to zero for
/// extreme markups.
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

                foreach (var inv in inventories)
                {
                    if (inv.Quantity <= 0m) continue;

                    Guid? itemId = inv.ProductTypeId ?? inv.ResourceTypeId;
                    if (itemId is null) continue;

                    decimal basePrice;
                    string? industry = null;
                    string? productName = null;
                    var priceElasticity = PublicSalesPricingModel.DefaultPriceElasticity;
                    decimal? brandAwareness = null;
                    if (inv.ProductTypeId.HasValue && context.ProductTypesById.TryGetValue(inv.ProductTypeId.Value, out var pt))
                    {
                        basePrice = pt.BasePrice;
                        industry = pt.Industry;
                        productName = pt.Name;
                        priceElasticity = pt.PriceElasticity;
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
                    var priceIndex = PublicSalesPricingModel.ComputePriceIndex(basePrice, price, priceElasticity);
                    var qualityMultiplier = Math.Max(0.15m, inv.Quality);
                    var qualityDemandFactor = ComputeQualityDemandFactor(inv.Quality);

                    var brand = context.FindBrand(building.CompanyId, inv.ProductTypeId, industry);
                    brandAwareness = Math.Clamp(brand?.Awareness ?? 0m, 0m, 1m);
                    var brandFactor = ComputeBrandFactor(brandAwareness.Value);

                    // Competitiveness score determines market-share allocation.
                    // PopulationIndex represents foot traffic / location advantage.
                    var competitiveness = priceIndex * qualityMultiplier * brandFactor * populationIndex;
                    if (competitiveness <= 0m) continue;

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
                        PriceElasticity = priceElasticity,
                        PriceIndex = priceIndex,
                        QualityDemandFactor = qualityDemandFactor,
                        BrandAwareness = brandAwareness.Value,
                        BrandFactor = brandFactor,
                        Competitiveness = competitiveness,
                        CurrentStock = inv.Quantity,
                        MaxCanSell = inv.Quantity,
                    });
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
            // Salary purchasing power uses a blended signal:
            //   – static wage level (BaseSalaryPerManhour) — city baseline
            //   – dynamic recent spending (actual LaborCost ledger sum for past 10 ticks)
            // This directly implements the ROADMAP requirement:
            // "game currency collected by salaries in past 10 ticks".
            context.RecentSalaryByCity.TryGetValue(city.Id, out var recentSalary);
            var salaryFactor = PublicSalesPricingModel.ComputeBlendedSalaryFactor(
                city.BaseSalaryPerManhour, recentSalary, city.Population);
            var cityBaseDemand = city.Population * GameConstants.BaseDemandPerCapita * salaryFactor;
            if (cityBaseDemand <= 0m)
                continue;

            var totalCurrentStock = groupList.Sum(o => o.CurrentStock);
            var saturationFactor = ComputeSaturationFactor(cityBaseDemand, totalCurrentStock);
            var maxPopulationIndex = Math.Max(1m, groupList.Max(o => o.PopulationIndex));
            var weightedDemandAttractiveness = totalCurrentStock > 0m
                ? groupList.Sum(offer => offer.CurrentStock * ComputeDemandAttractiveness(offer, maxPopulationIndex)) / totalCurrentStock
                : 0m;
            var effectiveCityDemand = cityBaseDemand * ComputeMarketDemandFactor(saturationFactor, weightedDemandAttractiveness);
            if (effectiveCityDemand <= 0m)
                continue;

            // Total competitiveness of all sellers (used for market-share split).
            var totalCompetitiveness = groupList.Sum(o => o.Competitiveness);
            if (totalCompetitiveness <= 0m)
                continue;

            foreach (var offer in groupList)
            {
                // Market share: each seller's fraction of city demand based on competitiveness.
                // For a single seller, marketShare = 1.0 and demand = effectiveCityDemand.
                // For multiple sellers, the effective demand is proportionally split based on
                // each seller's competitiveness so stronger offers win a larger share.
                var marketShare = offer.Competitiveness / totalCompetitiveness;
                var demand = effectiveCityDemand * marketShare;

                // Enforce unit-level sales capacity.
                unitSoldTotals.TryGetValue(offer.Unit.Id, out var unitSoldSoFar);

                if (!unitCapacityCache.TryGetValue(offer.Unit.Id, out var salesCapacity))
                {
                    salesCapacity = GameConstants.SalesCapacity(offer.Unit.Level)
                        * TickContext.GetPowerEfficiency(offer.Building);
                    unitCapacityCache[offer.Unit.Id] = salesCapacity;
                }

                var remainingCapacity = Math.Max(0m, salesCapacity - unitSoldSoFar);
                if (remainingCapacity <= 0m)
                    continue;

                var locationFactor = ComputeLocationFactor(offer.PopulationIndex, maxPopulationIndex);
                var competitionFactor = ComputeCompetitionFactor(marketShare);
                var publicSellIndex = ComputePublicSellIndex(
                    saturationFactor,
                    offer.QualityDemandFactor,
                    offer.BrandFactor,
                    locationFactor,
                    competitionFactor);
                var stockTurnoverCap = offer.CurrentStock * 0.5m * publicSellIndex * offer.PriceIndex;
                if (stockTurnoverCap <= 0m)
                    continue;

                var sold = Math.Min(
                    demand,
                    Math.Min(stockTurnoverCap, Math.Min(offer.MaxCanSell, remainingCapacity)));
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

    private static decimal ComputeQualityDemandFactor(decimal quality) =>
        Math.Clamp(0.2m + Math.Min(1m, quality) * 0.8m, 0.2m, 1m);

    private static decimal ComputeBrandFactor(decimal awareness) =>
        Math.Clamp(0.2m + awareness * 0.8m, 0.2m, 1m);

    private static decimal ComputeSaturationFactor(decimal cityBaseDemand, decimal totalCurrentStock)
    {
        if (cityBaseDemand <= 0m)
            return 0m;

        if (totalCurrentStock <= 0m)
            return 1m;

        return Math.Clamp(cityBaseDemand / totalCurrentStock, 0.05m, 1m);
    }

    private static decimal ComputeLocationFactor(decimal populationIndex, decimal maxPopulationIndex)
    {
        if (maxPopulationIndex <= 0m)
            return 1m;

        return Math.Clamp(populationIndex / maxPopulationIndex, 0.25m, 1m);
    }

    private static decimal ComputeDemandAttractiveness(SalesOffer offer, decimal maxPopulationIndex)
    {
        var locationFactor = ComputeLocationFactor(offer.PopulationIndex, maxPopulationIndex);
        return Math.Clamp(
            offer.PriceIndex * 0.45m
            + offer.QualityDemandFactor * 0.25m
            + offer.BrandFactor * 0.20m
            + locationFactor * 0.10m,
            0m,
            1m);
    }

    private static decimal ComputeMarketDemandFactor(decimal saturationFactor, decimal weightedDemandAttractiveness)
    {
        var marketAbsorptionFactor = 0.25m + (0.75m * saturationFactor);
        return Math.Clamp(marketAbsorptionFactor * Math.Max(0m, weightedDemandAttractiveness), 0m, 1m);
    }

    private static decimal ComputeCompetitionFactor(decimal marketShare)
    {
        if (marketShare <= 0m)
            return 0.05m;

        return Math.Clamp((decimal)Math.Sqrt((double)marketShare), 0.05m, 1m);
    }

    private static decimal ComputePublicSellIndex(
        decimal saturationFactor,
        decimal qualityDemandFactor,
        decimal brandFactor,
        decimal locationFactor,
        decimal competitionFactor)
    {
        return Math.Clamp(
            saturationFactor * 0.45m
            + qualityDemandFactor * 0.15m
            + brandFactor * 0.10m
            + locationFactor * 0.10m
            + competitionFactor * 0.20m,
            0m,
            1m);
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
        public decimal PriceElasticity { get; init; }
        public decimal PriceIndex { get; init; }
        public decimal QualityDemandFactor { get; init; }
        public decimal BrandAwareness { get; init; }
        public decimal BrandFactor { get; init; }
        public decimal Competitiveness { get; init; }
        public decimal CurrentStock { get; init; }
        public decimal MaxCanSell { get; init; }
    }
}
