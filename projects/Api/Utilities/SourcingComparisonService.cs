using Api.Data;
using Api.Data.Entities;
using Api.Types;
using Microsoft.EntityFrameworkCore;

namespace Api.Utilities;

/// <summary>
/// Builds a ranked list of all sourcing candidates for a purchase unit.
/// Each candidate represents one possible supply route with its full landed-cost
/// breakdown so the player can compare options and understand why one route may
/// be cheaper after transport even if its sticker price is higher.
///
/// Evaluation order mirrors <see cref="Api.Engine.Phases.PurchasingPhase"/>:
///   1. Player-placed exchange sell orders (LOCAL or OPTIMAL mode)
///   2. Local same-city B2B suppliers        (LOCAL or OPTIMAL mode)
///   3. Global city exchange offers           (EXCHANGE or OPTIMAL mode, resources only)
///
/// All candidates are returned (eligible and ineligible) so the UI can explain
/// why a cheaper-looking option is blocked by a quality or price filter.
/// </summary>
public static class SourcingComparisonService
{
    /// <summary>
    /// Computes the full candidate list for the given purchase unit.
    /// Returns an empty list when the unit has no resource/product configured.
    /// </summary>
    public static async Task<List<SourcingCandidate>> GetCandidatesAsync(
        AppDbContext db,
        BuildingUnit unit,
        Company company)
    {
        var resourceId = unit.ResourceTypeId;
        var productId = unit.ProductTypeId;

        if (resourceId is null && productId is null)
            return [];

        var maxPrice = unit.MaxPrice ?? decimal.MaxValue;
        var minQuality = unit.MinQuality ?? 0m;
        var purchaseSource = unit.PurchaseSource ?? "OPTIMAL";

        var building = await db.Buildings
            .Include(b => b.City)
            .FirstOrDefaultAsync(b => b.Id == unit.BuildingId);

        if (building is null)
            return [];

        var candidates = new List<SourcingCandidate>();

        // ── Step 1: Player-placed exchange sell orders (LOCAL or OPTIMAL) ──────
        if (purchaseSource is "LOCAL" or "OPTIMAL")
        {
            var orderCandidates = await BuildPlayerExchangeOrderCandidatesAsync(
                db, unit, resourceId, productId, maxPrice, minQuality);
            candidates.AddRange(orderCandidates);
        }

        // ── Step 2: Local same-city B2B supply (LOCAL or OPTIMAL) ──────────────
        if (purchaseSource is "LOCAL" or "OPTIMAL")
        {
            var localCandidates = await BuildLocalB2BCandidatesAsync(
                db, unit, building, resourceId, productId, maxPrice, minQuality, company.Id);
            candidates.AddRange(localCandidates);
        }

        // ── Step 3: Global city exchange offers (EXCHANGE or OPTIMAL, resource only) ─
        if (purchaseSource is "EXCHANGE" or "OPTIMAL" && resourceId.HasValue)
        {
            var exchangeCandidates = await BuildGlobalExchangeCandidatesAsync(
                db, unit, building, resourceId.Value, maxPrice, minQuality,
                applyLockedCity: purchaseSource == "EXCHANGE");
            candidates.AddRange(exchangeCandidates);
        }

        // ── Rank and annotate ───────────────────────────────────────────────────
        // Sort: eligible first by delivered price, then ineligible by delivered price.
        var eligible = candidates
            .Where(c => c.IsEligible)
            .OrderBy(c => c.DeliveredPricePerUnit ?? decimal.MaxValue)
            .ThenByDescending(c => c.EstimatedQuality ?? 0m)
            .ToList();
        var ineligible = candidates
            .Where(c => !c.IsEligible)
            .OrderBy(c => c.DeliveredPricePerUnit ?? decimal.MaxValue)
            .ThenByDescending(c => c.EstimatedQuality ?? 0m)
            .ToList();

        var ranked = eligible.Concat(ineligible).ToList();
        for (var i = 0; i < ranked.Count; i++)
        {
            ranked[i].Rank = i + 1;
        }
        if (eligible.Count > 0)
            eligible[0].IsRecommended = true;

        return ranked;
    }

    // ── Player-placed exchange sell orders ────────────────────────────────────

    private static async Task<List<SourcingCandidate>> BuildPlayerExchangeOrderCandidatesAsync(
        AppDbContext db,
        BuildingUnit unit,
        Guid? resourceId,
        Guid? productId,
        decimal maxPrice,
        decimal minQuality)
    {
        // minQuality not applied here; tick engine doesn't filter exchange orders by quality.
        _ = minQuality;

        var query = db.ExchangeOrders
            .Where(o => o.Side == "SELL" && o.IsActive && o.RemainingQuantity > 0m)
            .Include(o => o.Company)
            .AsQueryable();

        if (resourceId.HasValue)
            query = query.Where(o => o.ResourceTypeId == resourceId);
        if (productId.HasValue)
            query = query.Where(o => o.ProductTypeId == productId);
        if (unit.VendorLockCompanyId.HasValue)
            query = query.Where(o => o.CompanyId == unit.VendorLockCompanyId.Value);

        var orders = await query
            .OrderBy(o => o.PricePerUnit)
            .Take(5) // show up to 5 player orders to avoid overwhelming the UI
            .ToListAsync();

        return orders.Select(o =>
        {
            var priceOk = o.PricePerUnit <= maxPrice;
            return new SourcingCandidate
            {
                SourceType = unit.VendorLockCompanyId.HasValue
                    ? ProcurementSourceType.LockedVendor
                    : ProcurementSourceType.PlayerExchangeOrder,
                SourceVendorCompanyId = o.CompanyId,
                SourceVendorName = o.Company?.Name ?? "Exchange seller",
                DeliveredPricePerUnit = o.PricePerUnit,
                // Exchange orders have no quality field; 0.7 matches tick engine constant.
                EstimatedQuality = 0.7m,
                DistanceKm = 0,
                IsEligible = priceOk,
                BlockReason = priceOk ? null : ProcurementBlockReason.MaxPriceExceeded,
                BlockMessage = priceOk
                    ? null
                    : $"Order price (${o.PricePerUnit:F2}) exceeds your max price (${maxPrice:F2}).",
            };
        }).ToList();
    }

    // ── Local same-city B2B supply ─────────────────────────────────────────────

    private static async Task<List<SourcingCandidate>> BuildLocalB2BCandidatesAsync(
        AppDbContext db,
        BuildingUnit unit,
        Building building,
        Guid? resourceId,
        Guid? productId,
        decimal maxPrice,
        decimal minQuality,
        Guid buyerCompanyId)
    {
        var itemWeightPerUnit = await ComputeItemWeightPerUnitAsync(db, resourceId, productId);
        var query = db.BuildingUnits
            .Where(u => u.UnitType == UnitType.B2BSales
                     && u.Building.CityId == building.CityId
                     && u.BuildingId != building.Id)
            .Include(u => u.Building)
            .ThenInclude(b => b.Company)
            .AsQueryable();

        // Mirror PurchasingPhase.GetLocalB2BSupplies:
        // Without a vendor lock, the purchasing unit can only source from the
        // buyer's own company B2B units (internal inter-building transfer).
        // With a vendor lock, it is restricted to the locked company only.
        if (unit.VendorLockCompanyId.HasValue)
            query = query.Where(u => u.Building.CompanyId == unit.VendorLockCompanyId.Value);
        else
            query = query.Where(u => u.Building.CompanyId == buyerCompanyId);

        var salesUnits = await query.ToListAsync();

        var candidates = new List<SourcingCandidate>();
        foreach (var salesUnit in salesUnits)
        {
            var inventories = await db.Inventories
                .Where(inv => inv.BuildingUnitId == salesUnit.Id && inv.Quantity > 0m)
                .ToListAsync();

            foreach (var inv in inventories)
            {
                if (resourceId.HasValue && inv.ResourceTypeId != resourceId) continue;
                if (productId.HasValue && inv.ProductTypeId != productId) continue;
                if (resourceId is null && productId is null) continue;

                var price = salesUnit.MinPrice ?? 0m;
                if (price <= 0m) continue;
                var distanceKm = GlobalExchangeCalculator.ComputeDistanceKm(
                    salesUnit.Building.Latitude,
                    salesUnit.Building.Longitude,
                    building.Latitude,
                    building.Longitude);
                var transitCost = GlobalExchangeCalculator.ComputeTransitCostPerUnit(
                    salesUnit.Building.Latitude,
                    salesUnit.Building.Longitude,
                    building.Latitude,
                    building.Longitude,
                    itemWeightPerUnit);
                var deliveredPrice = price + transitCost;

                var qualityOk = inv.Quality >= minQuality;
                var priceOk = deliveredPrice <= maxPrice;
                var eligible = qualityOk && priceOk;

                string? blockReason = null;
                string? blockMessage = null;
                if (!priceOk)
                {
                    blockReason = ProcurementBlockReason.MaxPriceExceeded;
                    blockMessage = $"Delivered price (${deliveredPrice:F2}) exceeds your max price (${maxPrice:F2}).";
                }
                else if (!qualityOk)
                {
                    blockReason = ProcurementBlockReason.MinQualityFailed;
                    blockMessage = $"Quality ({inv.Quality:P0}) is below your minimum ({minQuality:P0}).";
                }

                candidates.Add(new SourcingCandidate
                {
                    SourceType = unit.VendorLockCompanyId.HasValue
                        ? ProcurementSourceType.LockedVendor
                        : ProcurementSourceType.LocalB2B,
                    SourceVendorCompanyId = salesUnit.Building.CompanyId,
                    SourceVendorName = salesUnit.Building.Company?.Name ?? "Local supplier",
                    ExchangePricePerUnit = price,
                    TransitCostPerUnit = transitCost,
                    DeliveredPricePerUnit = deliveredPrice,
                    EstimatedQuality = inv.Quality,
                    DistanceKm = distanceKm,
                    IsEligible = eligible,
                    BlockReason = blockReason,
                    BlockMessage = blockMessage,
                });
            }
        }

        return candidates;
    }

    private static async Task<decimal> ComputeItemWeightPerUnitAsync(
        AppDbContext db,
        Guid? resourceId,
        Guid? productId)
    {
        var resources = await db.ResourceTypes.AsNoTracking().ToListAsync();
        var products = await db.ProductTypes
            .AsNoTracking()
            .Include(product => product.Recipes)
            .ToListAsync();

        var resourceTypesById = resources.ToDictionary(resource => resource.Id);
        var productTypesById = products.ToDictionary(product => product.Id);
        var recipesByProduct = products.ToDictionary(product => product.Id, product => product.Recipes.ToList());

        return GlobalExchangeCalculator.ComputeItemWeightPerUnit(
            resourceId,
            productId,
            resourceTypesById,
            productTypesById,
            recipesByProduct);
    }

    // ── Global city exchange offers ───────────────────────────────────────────

    private static async Task<List<SourcingCandidate>> BuildGlobalExchangeCandidatesAsync(
        AppDbContext db,
        BuildingUnit unit,
        Building building,
        Guid resourceId,
        decimal maxPrice,
        decimal minQuality,
        bool applyLockedCity)
    {
        var resource = await db.ResourceTypes.FindAsync(resourceId);
        if (resource is null) return [];

        var destinationCity = building.City
            ?? await db.Cities.FindAsync(building.CityId);
        if (destinationCity is null) return [];

        var cityResources = await db.CityResources
            .Where(cr => cr.ResourceTypeId == resourceId)
            .Include(cr => cr.City)
            .ToListAsync();

        var allCities = await db.Cities.ToListAsync();

        var candidateCities = applyLockedCity && unit.LockedCityId.HasValue
            ? allCities.Where(c => c.Id == unit.LockedCityId.Value).ToList()
            : allCities;

        return candidateCities.Select(sourceCity =>
        {
            var abundance = cityResources
                .FirstOrDefault(cr => cr.CityId == sourceCity.Id)
                ?.Abundance ?? GlobalExchangeCalculator.DefaultMissingAbundance;

            var exchangePrice = GlobalExchangeCalculator.ComputeExchangePrice(sourceCity, resource, abundance);
            var transitCost = GlobalExchangeCalculator.ComputeTransitCostPerUnit(sourceCity, destinationCity, resource);
            var deliveredPrice = exchangePrice + transitCost;
            var quality = GlobalExchangeCalculator.ComputeExchangeQuality(abundance);
            var (qualityMin, qualityMax) = GlobalExchangeCalculator.ComputeExchangeQualityBand(abundance);
            var distanceKm = GlobalExchangeCalculator.ComputeDistanceKm(
                sourceCity.Latitude, sourceCity.Longitude,
                destinationCity.Latitude, destinationCity.Longitude);

            var priceOk = deliveredPrice <= maxPrice;
            var qualityOk = quality >= minQuality;
            var eligible = priceOk && qualityOk;

            string? blockReason = null;
            string? blockMessage = null;
            if (!priceOk)
            {
                blockReason = ProcurementBlockReason.MaxPriceExceeded;
                blockMessage = $"Landed cost (${deliveredPrice:F2}) exceeds your max price (${maxPrice:F2}).";
            }
            else if (!qualityOk)
            {
                blockReason = ProcurementBlockReason.MinQualityFailed;
                blockMessage = $"Quality ({quality:P0}) is below your minimum ({minQuality:P0}).";
            }

            return new SourcingCandidate
            {
                SourceType = ProcurementSourceType.GlobalExchange,
                SourceCityId = sourceCity.Id,
                SourceCityName = sourceCity.Name,
                ExchangePricePerUnit = exchangePrice,
                TransitCostPerUnit = transitCost,
                DeliveredPricePerUnit = deliveredPrice,
                EstimatedQuality = quality,
                QualityMin = qualityMin,
                QualityMax = qualityMax,
                DistanceKm = distanceKm,
                IsEligible = eligible,
                BlockReason = blockReason,
                BlockMessage = blockMessage,
            };
        }).ToList();
    }
}
