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
    /// <summary>
    /// Computes structured demand driver entries for a PUBLIC_SALES unit from its current state.
    /// Each entry describes one economic factor and whether it helps or hurts demand.
    /// </summary>
    private static List<DemandDriverEntry> ComputeDemandDrivers(
        Data.Entities.BuildingUnit unit,
        Data.Entities.ProductType? productType,
        decimal? inventoryQuality,
        decimal? brandAwareness,
        decimal? populationIndex,
        List<MarketShareEntry> marketShare,
        decimal? unmetDemandShare,
        decimal? baseSalaryPerManhour = null,
        decimal recentCitySalary = 0m,
        long cityPopulation = 0,
        decimal? trendFactor = null)
    {
        var drivers = new List<DemandDriverEntry>();

        // PRICE driver – based on price vs base price ratio.
        if (productType is not null)
        {
            var price = unit.MinPrice ?? productType.BasePrice;
            if (price <= 0m) price = productType.BasePrice;
            var priceIndex = productType.BasePrice > 0m
                ? PublicSalesPricingModel.ComputePriceIndex(productType.BasePrice, price, productType.PriceElasticity)
                : 1m;
            // priceIndex 1.0 = neutral; <1.0 = price drag; >1.0 = price boost (discounting).
            var priceScore = Math.Clamp(priceIndex, 0m, 1m);
            string priceImpact;
            string priceDesc;
            if (priceIndex >= 0.9m)
            {
                priceImpact = "POSITIVE";
                priceDesc = price < productType.BasePrice
                    ? "Priced below market baseline — attracts price-sensitive buyers."
                    : "Price is competitive versus the market baseline.";
            }
            else if (priceIndex >= 0.6m)
            {
                priceImpact = "NEUTRAL";
                priceDesc = "Price is moderately above the market baseline — some buyers may look elsewhere.";
            }
            else
            {
                priceImpact = "NEGATIVE";
                priceDesc = "Price is significantly above the market baseline — reducing demand noticeably.";
            }
            drivers.Add(new DemandDriverEntry { Factor = "PRICE", Impact = priceImpact, Score = priceScore, Description = priceDesc });
        }

        // QUALITY driver – inventory quality directly scales buyer demand.
        if (inventoryQuality.HasValue)
        {
            var q = Math.Clamp(inventoryQuality.Value, 0m, 1m);
            string qImpact;
            string qDesc;
            if (q >= 0.7m)
            {
                qImpact = "POSITIVE";
                qDesc = $"High product quality ({q * 100m:F0}%) increases buyer willingness to purchase.";
            }
            else if (q >= 0.4m)
            {
                qImpact = "NEUTRAL";
                qDesc = $"Average product quality ({q * 100m:F0}%) — improving quality would lift demand.";
            }
            else
            {
                qImpact = "NEGATIVE";
                qDesc = $"Low product quality ({q * 100m:F0}%) is reducing demand. Improve your supply chain.";
            }
            drivers.Add(new DemandDriverEntry { Factor = "QUALITY", Impact = qImpact, Score = q, Description = qDesc });
        }

        // BRAND driver – brand awareness multiplies demand.
        {
            var awareness = brandAwareness ?? 0m;
            var brandScore = Math.Clamp(awareness, 0m, 1m);
            string brandImpact;
            string brandDesc;
            if (awareness >= 0.5m)
            {
                brandImpact = "POSITIVE";
                brandDesc = $"Strong brand awareness ({awareness * 100m:F0}%) is boosting demand.";
            }
            else if (awareness >= 0.15m)
            {
                brandImpact = "NEUTRAL";
                brandDesc = $"Moderate brand awareness ({awareness * 100m:F0}%). Invest in marketing to improve reach.";
            }
            else
            {
                brandImpact = "NEGATIVE";
                brandDesc = brandAwareness.HasValue
                    ? $"Low brand awareness ({awareness * 100m:F0}%). Customers don't know your product yet."
                    : "No brand set. Creating a brand would increase visibility and demand.";
            }
            drivers.Add(new DemandDriverEntry { Factor = "BRAND", Impact = brandImpact, Score = brandScore, Description = brandDesc });
        }

        // LOCATION driver – lot population index (foot traffic) scales demand.
        if (populationIndex.HasValue)
        {
            var pi = populationIndex.Value;
            var locScore = Math.Clamp(pi / 2m, 0m, 1m); // normalise: 2.0× = full score
            string locImpact;
            string locDesc;
            if (pi >= 1.3m)
            {
                locImpact = "POSITIVE";
                locDesc = $"High-traffic location (×{pi:F2}) — more foot traffic means more potential buyers.";
            }
            else if (pi >= 0.8m)
            {
                locImpact = "NEUTRAL";
                locDesc = $"Average location traffic (×{pi:F2}). A busier lot would increase reach.";
            }
            else
            {
                locImpact = "NEGATIVE";
                locDesc = $"Low-traffic location (×{pi:F2}) is limiting your customer reach.";
            }
            drivers.Add(new DemandDriverEntry { Factor = "LOCATION", Impact = locImpact, Score = locScore, Description = locDesc });
        }

        // SALARY driver – city purchasing power based on average wages and recent
        // salary activity (ROADMAP: "game currency collected by salaries in past 10 ticks").
        // Higher-wage cities with more active companies generate more consumer demand.
        // This is informational: players cannot change their city's salary,
        // but it explains why the same product sells better in Vienna than Bratislava,
        // and why more economic activity in a city boosts consumer spending.
        if (baseSalaryPerManhour.HasValue && baseSalaryPerManhour.Value > 0m)
        {
            var salaryFactor = PublicSalesPricingModel.ComputeBlendedSalaryFactor(
                baseSalaryPerManhour.Value, recentCitySalary, cityPopulation);
            var salaryScore = Math.Clamp((salaryFactor - 0.5m) / 1.5m, 0m, 1m); // map [0.5,2.0] → [0,1]
            var hasRecentData = recentCitySalary > 0m;
            string salaryImpact;
            string salaryDesc;
            if (salaryFactor >= 1.2m)
            {
                salaryImpact = "POSITIVE";
                salaryDesc = hasRecentData
                    ? $"Residents here have strong purchasing power (×{salaryFactor:F2}) — above-average wages and active company payroll in this city are boosting consumer demand."
                    : $"Residents here have above-average purchasing power (salary ×{salaryFactor:F2}), boosting baseline demand.";
            }
            else if (salaryFactor >= 0.85m)
            {
                salaryImpact = "NEUTRAL";
                salaryDesc = hasRecentData
                    ? $"City purchasing power is near average (×{salaryFactor:F2}), combining the local wage level with recent salary spending in this city."
                    : $"City salary is near the market average (×{salaryFactor:F2}). Demand is not materially affected by purchasing power.";
            }
            else
            {
                salaryImpact = "NEGATIVE";
                salaryDesc = hasRecentData
                    ? $"Purchasing power is below average (×{salaryFactor:F2}) — low wages or limited company payroll activity in this city are constraining consumer spending."
                    : $"Residents here have below-average purchasing power (salary ×{salaryFactor:F2}), which limits baseline demand for your product.";
            }
            drivers.Add(new DemandDriverEntry { Factor = "SALARY", Impact = salaryImpact, Score = salaryScore, Description = salaryDesc });
        }

        // SATURATION driver — shows whether the city market is under-supplied (scarcity) or over-supplied.
        // Scarcity is good for sellers (demand exceeds supply); saturation limits how much more you can sell.
        if (unmetDemandShare.HasValue)
        {
            var unmet = Math.Clamp(unmetDemandShare.Value, 0m, 1m);
            string satImpact;
            string satDesc;
            decimal satScore;

            if (unmet >= 0.30m)
            {
                // Supply-constrained market: meaningful unmet demand → seller-favourable.
                satImpact = "POSITIVE";
                satScore = unmet;
                satDesc = $"Demand exceeds supply — {(unmet * 100m):F0}% of city demand is unmet. Increasing your stock would capture more sales.";
            }
            else if (unmet >= 0.05m)
            {
                // Healthy balance: small residual unmet demand.
                satImpact = "NEUTRAL";
                satScore = 0.5m;
                satDesc = $"Supply and demand are roughly balanced ({(unmet * 100m):F0}% unmet demand). Monitor stock levels to stay competitive.";
            }
            else
            {
                // Saturated: virtually all demand is already met by sellers in this city.
                satImpact = "NEGATIVE";
                satScore = 0.3m;
                satDesc = "The market is saturated — supply meets or exceeds demand. Competing on price or quality is more important than increasing volume.";
            }

            drivers.Add(new DemandDriverEntry { Factor = "SATURATION", Impact = satImpact, Score = satScore, Description = satDesc });
        }

        // COMPETITION driver — shows how many rival sellers exist and what share the player holds.
        // A monopoly or dominant share is positive; a crowded market with a small share is negative.
        {
            var activeSellers = marketShare.Where(e => !e.IsUnmet).ToList();
            if (activeSellers.Count > 0)
            {
                var sellerCount = activeSellers.Count;
                var ownShare = activeSellers
                    .FirstOrDefault(e => e.CompanyId == unit.Building.CompanyId)?.Share ?? 0m;
                var compScore = Math.Clamp(ownShare, 0m, 1m);

                string compImpact;
                string compDesc;

                if (sellerCount <= 1)
                {
                    compImpact = "POSITIVE";
                    compDesc = "You are the only seller of this product in this city — no direct competition.";
                }
                else if (ownShare >= 0.50m)
                {
                    var rivals = sellerCount - 1;
                    compImpact = "POSITIVE";
                    compDesc = $"You hold a dominant share ({ownShare * 100m:F0}%) against {rivals} competitor{(rivals > 1 ? "s" : "")}. Your offer is winning on price, quality, or brand.";
                }
                else if (ownShare >= 0.25m)
                {
                    var rivals = sellerCount - 1;
                    compImpact = "NEUTRAL";
                    compDesc = $"Competing with {rivals} other seller{(rivals > 1 ? "s" : "")} — you hold {ownShare * 100m:F0}% of the market. Improve quality or price to gain share.";
                }
                else
                {
                    var rivals = sellerCount - 1;
                    compImpact = "NEGATIVE";
                    compDesc = $"Strong competition: {rivals} rival{(rivals > 1 ? "s" : "")} hold most of this market and your {ownShare * 100m:F0}% share is low. Improve price competitiveness, quality, or brand to win more demand.";
                }

                drivers.Add(new DemandDriverEntry { Factor = "COMPETITION", Impact = compImpact, Score = compScore, Description = compDesc });
            }
        }

        // TREND driver – based on the persisted market-trend factor for this city/product.
        if (trendFactor.HasValue)
        {
            var tf = Math.Clamp(trendFactor.Value, GameConstants.TrendMin, GameConstants.TrendMax);
            // Normalise to [0,1] for the score field.
            var trendScore = Math.Clamp((tf - GameConstants.TrendMin)
                / (GameConstants.TrendMax - GameConstants.TrendMin), 0m, 1m);
            string trendImpact;
            string trendDesc;
            if (tf > 1.10m)
            {
                trendImpact = "POSITIVE";
                trendDesc = $"Market trend is rising (+{((tf - 1m) * 100m):F0}%) — consumer demand is growing for this product. Keep stock well-supplied to capitalise.";
            }
            else if (tf >= 0.90m)
            {
                trendImpact = "NEUTRAL";
                trendDesc = tf >= 1.0m
                    ? "Market trend is slightly positive — demand is near the baseline."
                    : "Market trend is slightly below baseline — demand is softening but still healthy.";
            }
            else
            {
                trendImpact = "NEGATIVE";
                trendDesc = $"Market trend is falling ({((tf - 1m) * 100m):F0}%) — consumer demand for this product is suppressed. Consider lowering price, improving quality, or investing in marketing to reverse the trend.";
            }
            drivers.Add(new DemandDriverEntry { Factor = "TREND", Impact = trendImpact, Score = trendScore, Description = trendDesc });
        }

        // Order: most impactful first — NEGATIVE first, then NEUTRAL, then POSITIVE.
        static int ImpactSortPriority(string impact) => impact switch
        {
            "NEGATIVE" => 0,
            "NEUTRAL"  => 1,
            _          => 2,   // "POSITIVE" and any unknown values
        };
        return [.. drivers.OrderBy(d => ImpactSortPriority(d.Impact))
                          .ThenByDescending(d => d.Score)];
    }
}
