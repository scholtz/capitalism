using Api.Data;
using Api.Data.Entities;
using Api.Engine;
using Api.Security;
using Api.Utilities;
using HotChocolate.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Api.Types;

public sealed partial class Query
{
    /// <summary>Returns analytics for a PUBLIC_SALES building unit.</summary>
    [Authorize]
    public async Task<PublicSalesAnalytics?> GetPublicSalesAnalytics(
        Guid unitId,
        [Service] AppDbContext db,
        [Service] IHttpContextAccessor httpContextAccessor)
    {
        var userId = httpContextAccessor.HttpContext!.User.GetRequiredUserId();

        var unit = await db.BuildingUnits
            .Include(u => u.Building)
            .ThenInclude(b => b.Company)
            .FirstOrDefaultAsync(u => u.Id == unitId);

        if (unit is null || unit.Building.Company.PlayerId != userId) return null;

        var building = unit.Building;
        var city = await db.Cities.FindAsync(building.CityId);

        // Load current tick for the salary window calculation, and compute
        // the total salary paid in this city over the past 10 ticks so the
        // SALARY demand driver can reflect dynamic economic activity.
        var gameStateForSalary = await db.GameStates.FirstOrDefaultAsync();
        decimal recentCitySalary = 0m;
        if (gameStateForSalary is not null && city is not null)
        {
            var salaryWindowStart = gameStateForSalary.CurrentTick - GameConstants.RecentSalaryWindowTicks;
            // Collect building IDs in this city to scope the ledger query.
            var cityBuildingIds = await db.Buildings
                .Where(b => b.CityId == city.Id)
                .Select(b => b.Id)
                .ToListAsync();
            if (cityBuildingIds.Count > 0)
            {
                recentCitySalary = await db.LedgerEntries
                    .Where(e => e.Category == LedgerCategory.LaborCost
                        && e.RecordedAtTick > salaryWindowStart
                        && e.BuildingId.HasValue
                        && cityBuildingIds.Contains(e.BuildingId!.Value))
                    .SumAsync(e => -e.Amount);  // amounts are negative; negate to get absolute total
            }
        }

        var records = await db.PublicSalesRecords
            .Where(r => r.BuildingUnitId == unitId)
            .OrderByDescending(r => r.Tick)
            .Take(100)
            .ToListAsync();

        var totalRevenue = records.Sum(r => r.Revenue);
        var totalQuantity = records.Sum(r => r.QuantitySold);
        var averagePrice = totalQuantity > 0 ? totalRevenue / totalQuantity : 0m;
        var dataFromTick = records.Count > 0 ? records.Min(r => r.Tick) : 0L;
        var dataToTick = records.Count > 0 ? records.Max(r => r.Tick) : 0L;

        var revenueHistory = records
            .OrderBy(r => r.Tick)
            .Select(r => new SalesTickSnapshot { Tick = r.Tick, Revenue = r.Revenue, QuantitySold = r.QuantitySold })
            .ToList();

        var priceHistory = records
            .OrderBy(r => r.Tick)
            .Select(r => new PriceTickSnapshot { Tick = r.Tick, PricePerUnit = r.PricePerUnit })
            .ToList();

        // Market share: look at the most recent tick's data for same product + city.
        // Also compute unmet demand (city demand not fulfilled by any seller).
        List<MarketShareEntry> marketShare = [];
        decimal? unmetDemandShare = null;
        var mostRecentTick = records.Count > 0 ? records.Max(r => r.Tick) : -1L;
        if (mostRecentTick >= 0)
        {
            var productTypeId = unit.ProductTypeId ?? records.FirstOrDefault()?.ProductTypeId;
            if (productTypeId.HasValue)
            {
                var cityRecords = await db.PublicSalesRecords
                    .Where(r => r.CityId == building.CityId
                                && r.ProductTypeId == productTypeId
                                && r.Tick == mostRecentTick)
                    .Include(r => r.Company)
                    .ToListAsync();

                var totalCitySales = cityRecords.Sum(r => r.QuantitySold);

                // Use the requesting unit's own Demand field as the reference for city demand.
                // Each unit's Demand is computed independently from the city population model,
                // so we cannot sum them across units (that would double-count). Instead we use
                // the requesting unit's most-recent recorded demand as the best-available proxy.
                var thisUnitRecord = cityRecords.FirstOrDefault(r => r.BuildingUnitId == unitId);
                var referenceUnitDemand = thisUnitRecord?.Demand ?? 0m;

                if (totalCitySales > 0)
                {
                    var perCompanyEntries = cityRecords
                        .GroupBy(r => r.CompanyId)
                        .Select(g => new MarketShareEntry
                        {
                            Label = g.First().Company.Name,
                            CompanyId = g.Key,
                            Share = g.Sum(r => r.QuantitySold) / totalCitySales,
                            IsUnmet = false,
                        })
                        .OrderByDescending(e => e.Share)
                        .ToList();

                    // Non-player / unmet market share: demand that went unfilled by all sellers.
                    // Use the requesting unit's reference demand (city population model output)
                    // as the denominator so shares are expressed as fractions of estimated city demand.
                    if (referenceUnitDemand > totalCitySales)
                    {
                        var unmetQty = referenceUnitDemand - totalCitySales;
                        var totalMarket = referenceUnitDemand;
                        // Renormalise seller shares relative to total market (not just sold volume)
                        foreach (var e in perCompanyEntries)
                            e.Share = e.Share * totalCitySales / totalMarket;
                        unmetDemandShare = unmetQty / totalMarket;
                        perCompanyEntries.Add(new MarketShareEntry
                        {
                            Label = "Unmet Demand",
                            CompanyId = null,
                            Share = unmetDemandShare.Value,
                            IsUnmet = true,
                        });
                    }
                    else
                    {
                        unmetDemandShare = 0m;
                    }

                    marketShare = perCompanyEntries;
                }
            }
        }

        // Demand signal: analyse recent ticks (last 5) for utilisation vs capacity
        var capacity = GameConstants.SalesCapacity(unit.Level);
        var recentRecords = records.OrderByDescending(r => r.Tick).Take(5).ToList();
        string demandSignal;
        string actionHint;
        decimal recentUtilization = 0m;

        // Named thresholds for demand signal classification
        const double SupplyConstrainedDemandOverSoldRatio = 1.4;
        const decimal SupplyConstrainedUtilizationThreshold = 0.85m;
        const decimal StrongDemandUtilizationThreshold = 0.7m;
        const decimal ModerateDemandUtilizationThreshold = 0.3m;

        if (recentRecords.Count == 0)
        {
            demandSignal = "NO_DATA";
            actionHint = "No sales recorded yet. Configure a product and price on this unit and let a few ticks pass to see demand insights.";
        }
        else
        {
            var avgQuantitySold = recentRecords.Average(r => (double)r.QuantitySold);
            var avgDemand = recentRecords.Average(r => (double)r.Demand);
            recentUtilization = capacity > 0m
                ? Math.Min(1m, (decimal)avgQuantitySold / capacity)
                : 0m;

            // Demand >> sold means inventory or capacity was the bottleneck
            var supplyConstrained = avgDemand > 0 && avgQuantitySold > 0
                && avgDemand >= avgQuantitySold * SupplyConstrainedDemandOverSoldRatio && (decimal)avgQuantitySold >= capacity * SupplyConstrainedUtilizationThreshold;

            if (supplyConstrained)
            {
                demandSignal = "SUPPLY_CONSTRAINED";
                actionHint = "Demand is outpacing your stock. Increase factory output or storage to capture more sales.";
            }
            else if (recentUtilization >= StrongDemandUtilizationThreshold)
            {
                demandSignal = "STRONG";
                actionHint = "Demand is strong. Consider testing a slightly higher price to improve your margin without losing customers.";
            }
            else if (recentUtilization >= ModerateDemandUtilizationThreshold)
            {
                demandSignal = "MODERATE";
                actionHint = "Sales are healthy. Keep monitoring stock levels and brand awareness to sustain performance.";
            }
            else
            {
                demandSignal = "WEAK";
                actionHint = "Sales are slow. Consider lowering your price, improving product quality, or investing in marketing to attract more customers.";
            }
        }

        // Elasticity index: product-level buyer sensitivity used by the public-sales
        // pricing model. More elastic products return a more negative value.
        decimal? elasticityIndex = null;
        var productTypeIdForElasticity = unit.ProductTypeId ?? records.FirstOrDefault()?.ProductTypeId;
        if (productTypeIdForElasticity.HasValue)
        {
            var productType = await db.ProductTypes.FindAsync(productTypeIdForElasticity.Value);
            if (productType is not null)
            {
                elasticityIndex = PublicSalesPricingModel.ComputeElasticityIndex(productType.PriceElasticity);
            }
        }

        // Supporting context: population index and current inventory quality / brand
        decimal? populationIndex = null;
        decimal? inventoryQuality = null;
        decimal? brandAwareness = null;

        var lot = await db.BuildingLots.FirstOrDefaultAsync(l => l.BuildingId == building.Id);
        if (lot is not null)
            populationIndex = lot.PopulationIndex;

        var inventory = await db.Inventories
            .Where(i => i.BuildingUnitId == unit.Id)
            .FirstOrDefaultAsync();
        if (inventory is not null)
            inventoryQuality = inventory.Quality;

        Data.Entities.ProductType? productTypeForAnalytics = null;
        if (productTypeIdForElasticity.HasValue)
        {
            productTypeForAnalytics = await db.ProductTypes.FindAsync(productTypeIdForElasticity.Value);
            if (productTypeForAnalytics is not null)
            {
                var brand = await db.Brands
                    .Where(b => b.CompanyId == building.CompanyId
                                && (b.Scope == "PRODUCT" && b.ProductTypeId == productTypeIdForElasticity.Value
                                    || b.Scope == "CATEGORY" && b.IndustryCategory == productTypeForAnalytics.Industry
                                    || b.Scope == "COMPANY"))
                    .OrderByDescending(b => b.Awareness)
                    .FirstOrDefaultAsync();
                if (brand is not null)
                    brandAwareness = brand.Awareness;
            }
        }

        // ── Profit history ──────────────────────────────────────────────────────
        // Gross profit per tick = revenue − (quantitySold × product base price).
        // This shows how much above cost the player earns; null when base price is unknown.
        decimal? totalProfit = null;
        List<ProfitTickSnapshot>? profitHistory = null;
        if (productTypeForAnalytics is not null)
        {
            var basePrice = productTypeForAnalytics.BasePrice;
            profitHistory = records
                .OrderBy(r => r.Tick)
                .Select(r =>
                {
                    var cost = r.QuantitySold * basePrice;
                    var profit = r.Revenue - cost;
                    // GrossMarginPct is null when revenue is zero (covers both zero-cost-zero-revenue
                    // and negative-cost-zero-revenue edge cases; caller should treat null as 'N/A').
                    return new ProfitTickSnapshot
                    {
                        Tick = r.Tick,
                        Profit = profit,
                        GrossMarginPct = r.Revenue > 0 ? Math.Round(profit / r.Revenue * 100m, 2) : null,
                    };
                })
                .ToList();
            totalProfit = profitHistory.Sum(p => p.Profit);
        }

        // ── Demand drivers ──────────────────────────────────────────────────────
        // Compute structured demand driver explanations from current unit state so
        // players can understand why sales are strong or weak.
        // Load the current market trend factor from the most-recent sales records
        // (stored there by PublicSalesPhase for analytics use).
        decimal? currentTrendFactor = null;
        if (records.Count > 0)
        {
            var latestTick = records.Max(r => r.Tick);
            var latestRecord = records.FirstOrDefault(r => r.Tick == latestTick);
            if (latestRecord is not null)
                currentTrendFactor = latestRecord.TrendFactor;
        }
        // Also try to fetch from MarketTrendState directly (most up-to-date).
        if (city is not null)
        {
            var resolvedItemIdForTrend = productTypeIdForElasticity
                ?? records.FirstOrDefault()?.ResourceTypeId;
            if (resolvedItemIdForTrend.HasValue)
            {
                var trendState = await db.MarketTrendStates
                    .FirstOrDefaultAsync(t => t.CityId == city.Id && t.ItemId == resolvedItemIdForTrend.Value);
                if (trendState is not null)
                    currentTrendFactor = trendState.TrendFactor;
            }
        }

        var demandDrivers = ComputeDemandDrivers(
            unit, productTypeForAnalytics, inventoryQuality, brandAwareness, populationIndex,
            marketShare, unmetDemandShare, city?.BaseSalaryPerManhour,
            recentCitySalary, city?.Population ?? 0, currentTrendFactor);

        // ── Trend direction ─────────────────────────────────────────────────────
        // Compare average revenue in the most-recent 5 ticks vs the prior 5 ticks to give
        // a simple UP / FLAT / DOWN direction signal. Helps the player see at a glance
        // whether the last few decisions improved or hurt performance.
        var trendDirection = ComputeTrendDirection(revenueHistory);

        // Resolve the product type ID used for the analytics response.  Priority:
        //   1. The product type loaded for analytics (based on unit or recent record)
        //   2. The product type ID on the unit itself
        //   3. The product type ID from the most-recent sales record
        var resolvedProductTypeId = productTypeForAnalytics?.Id
            ?? unit.ProductTypeId
            ?? records.FirstOrDefault()?.ProductTypeId;

        return new PublicSalesAnalytics
        {
            BuildingUnitId = unit.Id,
            BuildingId = building.Id,
            BuildingName = building.Name,
            CityName = city?.Name ?? string.Empty,
            ProductTypeId = resolvedProductTypeId,
            ProductName = productTypeForAnalytics?.Name,
            TotalRevenue = totalRevenue,
            TotalQuantitySold = totalQuantity,
            AveragePricePerUnit = averagePrice,
            CurrentSalesCapacity = capacity,
            DataFromTick = dataFromTick,
            DataToTick = dataToTick,
            RevenueHistory = revenueHistory,
            MarketShare = marketShare,
            PriceHistory = priceHistory,
            TrendDirection = trendDirection,
            DemandSignal = demandSignal,
            ActionHint = actionHint,
            RecentUtilization = recentUtilization,
            ElasticityIndex = elasticityIndex,
            UnmetDemandShare = unmetDemandShare,
            PopulationIndex = populationIndex,
            InventoryQuality = inventoryQuality,
            BrandAwareness = brandAwareness,
            TotalProfit = totalProfit,
            ProfitHistory = profitHistory,
            DemandDrivers = demandDrivers,
            TrendFactor = currentTrendFactor,
        };
    }

    /// <summary>
    /// Computes a revenue trend direction by comparing average revenue in the most-recent
    /// 5 ticks vs the prior 5 ticks. Returns UP, FLAT, or DOWN. Returns NO_DATA when there
    /// are fewer than 2 ticks of history (not enough to compute a meaningful comparison).
    /// A 5 % threshold separates FLAT from directional movement.
    /// </summary>
    private static string ComputeTrendDirection(List<SalesTickSnapshot> revenueHistory)
    {
        // Require two full equal windows (5 ticks each = 10 ticks minimum) so that the
        // comparison is always fair.  Histories shorter than 10 ticks cannot produce a
        // trustworthy directional verdict and are classified as NO_DATA.  This prevents
        // the early-game period (6-9 ticks) from showing a misleading UP/DOWN/FLAT badge
        // to players who are actively learning price elasticity and supply dynamics.
        if (revenueHistory.Count < 10) return "NO_DATA";

        var recent = revenueHistory.TakeLast(5).ToList();
        var prior  = revenueHistory.SkipLast(5).TakeLast(5).ToList();

        var recentAvg = recent.Average(s => s.Revenue);
        var priorAvg  = prior.Average(s => s.Revenue);

        if (priorAvg == 0)
            return recentAvg > 0 ? "UP" : "FLAT";

        var change = (recentAvg - priorAvg) / priorAvg;
        return change > GameConstants.FlatTrendThresholdPct ? "UP"
             : change < -GameConstants.FlatTrendThresholdPct ? "DOWN"
             : "FLAT";
    }
}
