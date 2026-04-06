using Api.Data;
using Api.Data.Entities;
using Api.Types;
using Microsoft.EntityFrameworkCore;

namespace Api.Utilities;

/// <summary>
/// Evaluates what a PURCHASE unit would do on the next tick given its current
/// configuration and live game state, without mutating any data. The same rules
/// mirror <see cref="Api.Engine.Phases.PurchasingPhase"/> so the preview is
/// trustworthy and consistent with actual simulation behavior.
/// </summary>
public static class ProcurementPreviewService
{
    /// <summary>
    /// Computes a procurement preview for the given purchase unit.
    /// Evaluation order mirrors <see cref="Api.Engine.Phases.PurchasingPhase"/> exactly:
    /// 1. Player-placed exchange sell orders (LOCAL or OPTIMAL)
    /// 2. Local same-city B2B supply (LOCAL or OPTIMAL)
    /// 3. Global city exchange / infinite counterparty (EXCHANGE or OPTIMAL)
    /// </summary>
    public static async Task<ProcurementPreview> ComputeAsync(
        AppDbContext db,
        BuildingUnit unit,
        Company company)
    {
        var resourceId = unit.ResourceTypeId;
        var productId = unit.ProductTypeId;

        if (resourceId is null && productId is null)
        {
            return new ProcurementPreview
            {
                SourceType = ProcurementSourceType.NoSource,
                CanExecute = false,
                BlockReason = ProcurementBlockReason.NotConfigured,
                BlockMessage = "The purchase unit has no resource or product configured.",
            };
        }

        var maxPrice = unit.MaxPrice ?? decimal.MaxValue;
        var minQuality = unit.MinQuality ?? 0m;
        var purchaseSource = unit.PurchaseSource ?? "OPTIMAL";

        var building = await db.Buildings
            .Include(b => b.Units)
            .FirstOrDefaultAsync(b => b.Id == unit.BuildingId);

        if (building is null)
        {
            return new ProcurementPreview
            {
                SourceType = ProcurementSourceType.NoSource,
                CanExecute = false,
                BlockReason = ProcurementBlockReason.NotConfigured,
                BlockMessage = "Building not found.",
            };
        }

        // Check cash before anything else.
        if (company.Cash <= 0m)
        {
            return new ProcurementPreview
            {
                SourceType = ProcurementSourceType.NoSource,
                CanExecute = false,
                BlockReason = ProcurementBlockReason.InsufficientCash,
                BlockMessage = "The company has no cash available for purchases.",
            };
        }

        // ── Step 1 (mirrors tick phase 1): Player-placed exchange sell orders ──
        // Applies for LOCAL and OPTIMAL modes. Returns immediately if a qualifying order is found.
        if (purchaseSource is "LOCAL" or "OPTIMAL")
        {
            var orderPreview = await EvaluatePlayerExchangeOrdersAsync(db, unit, building, resourceId, productId, maxPrice, minQuality);
            if (orderPreview.CanExecute)
                return orderPreview;
        }

        // ── Step 2 (mirrors tick phase 1.5): Local same-city B2B supply ──
        // Applies for LOCAL and OPTIMAL modes. Returns immediately if a qualifying supply is found.
        if (purchaseSource is "LOCAL" or "OPTIMAL")
        {
            var localPreview = await EvaluateLocalB2BAsync(db, unit, building, resourceId, productId, maxPrice, minQuality, company.Id);
            if (localPreview.CanExecute)
                return localPreview;

            // For LOCAL-only mode with no qualifying supply, return the blocked result.
            if (purchaseSource == "LOCAL")
                return localPreview;
        }

        // ── Step 3 (mirrors tick phase 2): Global city exchange ──
        // Applies for EXCHANGE and OPTIMAL modes (resource purchases only).
        // LockedCityId is only respected in EXCHANGE mode; OPTIMAL picks globally.
        if (purchaseSource is "EXCHANGE" or "OPTIMAL" && resourceId.HasValue)
        {
            var globalPreview = await EvaluateGlobalExchangeAsync(db, unit, building, resourceId.Value, maxPrice, minQuality, applyLockedCity: purchaseSource == "EXCHANGE");
            if (purchaseSource == "EXCHANGE")
                return globalPreview; // Always return for EXCHANGE – could be blocked.

            if (globalPreview.CanExecute)
                return globalPreview;
        }

        // EXCHANGE source but no resource (product-only) – not supported yet.
        if (purchaseSource == "EXCHANGE" && productId.HasValue)
        {
            return new ProcurementPreview
            {
                SourceType = ProcurementSourceType.NoSource,
                CanExecute = false,
                BlockReason = ProcurementBlockReason.NotConfigured,
                BlockMessage = "Exchange sourcing for products is not yet supported. Use LOCAL or OPTIMAL source.",
            };
        }

        // All sources exhausted (OPTIMAL with no qualifying offer or supply).
        return new ProcurementPreview
        {
            SourceType = ProcurementSourceType.NoSource,
            CanExecute = false,
            BlockReason = ProcurementBlockReason.NoStock,
            BlockMessage = resourceId.HasValue
                ? "No qualifying exchange order, local B2B supply, or global exchange offer found. Check max price, min quality, or wait for stock."
                : "No qualifying exchange order or local B2B supply found. Check max price, min quality, or wait for stock.",
        };
    }

    /// <summary>
    /// Evaluates player-placed exchange sell orders (mirrors tick phase 1 – LOCAL/OPTIMAL path).
    /// Returns the best qualifying sell order at or below maxPrice.
    /// NOTE: The tick engine does NOT filter player exchange orders by MinQuality (exchange orders
    /// do not carry quality metadata). This method mirrors that behavior exactly – MinQuality is
    /// intentionally not applied here. The reported quality (0.7) is the same estimate used by
    /// the tick engine's weighted-quality accumulation for exchange purchases.
    /// VendorLockCompanyId IS applied, matching tick engine behavior (see PurchasingPhase Phase 1).
    /// </summary>
    private static async Task<ProcurementPreview> EvaluatePlayerExchangeOrdersAsync(
        AppDbContext db,
        BuildingUnit unit,
        Building building,
        Guid? resourceId,
        Guid? productId,
        decimal maxPrice,
        decimal minQuality)
    {
        // minQuality parameter is intentionally unused here – tick engine does not filter
        // exchange sell orders by quality because ExchangeOrder has no quality field.
        _ = minQuality;

        var query = db.ExchangeOrders
            .Where(o => o.Side == "SELL" && o.IsActive && o.RemainingQuantity > 0m && o.PricePerUnit <= maxPrice)
            .AsQueryable();

        if (resourceId.HasValue)
            query = query.Where(o => o.ResourceTypeId == resourceId);
        if (productId.HasValue)
            query = query.Where(o => o.ProductTypeId == productId);
        // Apply vendor lock if set – mirrors tick engine PurchasingPhase phase 1 behavior.
        if (unit.VendorLockCompanyId.HasValue)
            query = query.Where(o => o.CompanyId == unit.VendorLockCompanyId.Value);

        var bestOrder = await query
            .OrderBy(o => o.PricePerUnit)
            .Include(o => o.Company)
            .FirstOrDefaultAsync();

        if (bestOrder is null)
        {
            return new ProcurementPreview
            {
                SourceType = ProcurementSourceType.PlayerExchangeOrder,
                CanExecute = false,
                BlockReason = ProcurementBlockReason.NoStock,
                BlockMessage = "No qualifying player exchange sell orders found.",
            };
        }

        return new ProcurementPreview
        {
            SourceType = ProcurementSourceType.PlayerExchangeOrder,
            SourceVendorCompanyId = bestOrder.CompanyId,
            SourceVendorName = bestOrder.Company?.Name ?? "Exchange seller",
            DeliveredPricePerUnit = bestOrder.PricePerUnit,
            // Exchange orders have no quality field; 0.7m matches the tick engine's
            // weightedQualityTotal accumulation constant for exchange purchases.
            EstimatedQuality = 0.7m,
            CanExecute = true,
        };
    }

    private static async Task<ProcurementPreview> EvaluateGlobalExchangeAsync(
        AppDbContext db,
        BuildingUnit unit,
        Building building,
        Guid resourceId,
        decimal maxPrice,
        decimal minQuality,
        bool applyLockedCity = false)
    {
        var resource = await db.ResourceTypes.FindAsync(resourceId);
        if (resource is null)
        {
            return new ProcurementPreview
            {
                SourceType = ProcurementSourceType.NoSource,
                CanExecute = false,
                BlockReason = ProcurementBlockReason.NotConfigured,
                BlockMessage = "The configured resource type was not found.",
            };
        }

        var destinationCity = await db.Cities.FindAsync(building.CityId);
        if (destinationCity is null)
        {
            return new ProcurementPreview
            {
                SourceType = ProcurementSourceType.NoSource,
                CanExecute = false,
                BlockReason = ProcurementBlockReason.NotConfigured,
                BlockMessage = "The building's city was not found.",
            };
        }

        var cityResources = await db.CityResources
            .Where(cr => cr.ResourceTypeId == resourceId)
            .Include(cr => cr.City)
            .ToListAsync();

        var allCities = await db.Cities.ToListAsync();

        // Apply LockedCityId filter only when applyLockedCity=true (EXCHANGE mode only).
        // OPTIMAL mode must not be constrained by LockedCityId so it can pick the globally cheapest source.
        var candidateCities = applyLockedCity && unit.LockedCityId.HasValue
            ? allCities.Where(c => c.Id == unit.LockedCityId.Value).ToList()
            : allCities;

        var offers = candidateCities
            .Select(sourceCity =>
            {
                var abundance = cityResources
                    .FirstOrDefault(cr => cr.CityId == sourceCity.Id)
                    ?.Abundance ?? GlobalExchangeCalculator.DefaultMissingAbundance;

                var exchangePrice = GlobalExchangeCalculator.ComputeExchangePrice(sourceCity, resource, abundance);
                var transitCost = GlobalExchangeCalculator.ComputeTransitCostPerUnit(sourceCity, destinationCity, resource);
                var deliveredPrice = exchangePrice + transitCost;
                var quality = GlobalExchangeCalculator.ComputeExchangeQuality(abundance);

                return new
                {
                    SourceCity = sourceCity,
                    ExchangePrice = exchangePrice,
                    TransitCost = transitCost,
                    DeliveredPrice = deliveredPrice,
                    Quality = quality,
                    MaxPriceOk = deliveredPrice <= maxPrice,
                    MinQualityOk = quality >= minQuality,
                };
            })
            .ToList();

        var lockedCityMissing = applyLockedCity && unit.LockedCityId.HasValue && !allCities.Any(c => c.Id == unit.LockedCityId.Value);
        if (lockedCityMissing)
        {
            return new ProcurementPreview
            {
                SourceType = ProcurementSourceType.GlobalExchange,
                CanExecute = false,
                BlockReason = ProcurementBlockReason.LockedSourceUnavailable,
                BlockMessage = "The locked source city no longer exists or is unavailable.",
            };
        }

        var bestOffer = offers
            .Where(o => o.MaxPriceOk && o.MinQualityOk)
            .OrderBy(o => o.DeliveredPrice)
            .ThenByDescending(o => o.Quality)
            .FirstOrDefault();

        if (bestOffer is not null)
        {
            return new ProcurementPreview
            {
                SourceType = ProcurementSourceType.GlobalExchange,
                SourceCityId = bestOffer.SourceCity.Id,
                SourceCityName = bestOffer.SourceCity.Name,
                ExchangePricePerUnit = bestOffer.ExchangePrice,
                TransitCostPerUnit = bestOffer.TransitCost,
                DeliveredPricePerUnit = bestOffer.DeliveredPrice,
                EstimatedQuality = bestOffer.Quality,
                CanExecute = true,
            };
        }

        // No qualifying offer – determine the most instructive block reason.
        var anyOffer = offers.FirstOrDefault();
        if (anyOffer is null)
        {
            return new ProcurementPreview
            {
                SourceType = ProcurementSourceType.GlobalExchange,
                CanExecute = false,
                BlockReason = (applyLockedCity && unit.LockedCityId.HasValue) ? ProcurementBlockReason.LockedSourceUnavailable : ProcurementBlockReason.NoStock,
                BlockMessage = (applyLockedCity && unit.LockedCityId.HasValue)
                    ? "The locked source city has no exchange offer for this resource."
                    : "No exchange offers available for this resource.",
            };
        }

        // Pick the blocked offer with the best delivered price for diagnostic display.
        var blockedOffer = offers.OrderBy(o => o.DeliveredPrice).First();
        var maxPriceBlock = !blockedOffer.MaxPriceOk;
        var minQualityBlock = !blockedOffer.MinQualityOk;

        return new ProcurementPreview
        {
            SourceType = ProcurementSourceType.GlobalExchange,
            SourceCityId = blockedOffer.SourceCity.Id,
            SourceCityName = blockedOffer.SourceCity.Name,
            ExchangePricePerUnit = blockedOffer.ExchangePrice,
            TransitCostPerUnit = blockedOffer.TransitCost,
            DeliveredPricePerUnit = blockedOffer.DeliveredPrice,
            EstimatedQuality = blockedOffer.Quality,
            CanExecute = false,
            BlockReason = maxPriceBlock ? ProcurementBlockReason.MaxPriceExceeded : ProcurementBlockReason.MinQualityFailed,
            BlockMessage = maxPriceBlock
                ? $"The best available delivered price (${blockedOffer.DeliveredPrice:F2}) exceeds your max price (${maxPrice:F2}). Raise the max price or choose a different source city."
                : $"The best available quality ({blockedOffer.Quality:P0}) is below your minimum ({minQuality:P0}). Lower the min quality threshold or choose a higher-abundance source.",
        };
    }

    private static async Task<ProcurementPreview> EvaluateLocalB2BAsync(
        AppDbContext db,
        BuildingUnit unit,
        Building building,
        Guid? resourceId,
        Guid? productId,
        decimal maxPrice,
        decimal minQuality,
        Guid buyerCompanyId)
    {
        // Find same-city B2B supplies matching this purchase unit's configured item.
        var query = db.BuildingUnits
            .Where(u => u.UnitType == UnitType.B2BSales && u.Building.CityId == building.CityId && u.BuildingId != building.Id)
            .Include(u => u.Building)
            .AsQueryable();

        if (unit.VendorLockCompanyId.HasValue)
        {
            query = query.Where(u => u.Building.CompanyId == unit.VendorLockCompanyId.Value);
        }
        else
        {
            query = query.Where(u => u.Building.CompanyId == buyerCompanyId);
        }

        var salesUnits = await query.ToListAsync();

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
                if (inv.Quality < minQuality) continue;

                var price = salesUnit.MinPrice ?? 0m;
                if (price <= 0m || price > maxPrice) continue;

                string sourceName;
                if (unit.VendorLockCompanyId.HasValue)
                {
                    var vendorCompany = await db.Companies.FindAsync(salesUnit.Building.CompanyId);
                    sourceName = vendorCompany?.Name ?? "Unknown vendor";
                }
                else
                {
                    sourceName = "Your own inventory";
                }

                return new ProcurementPreview
                {
                    SourceType = unit.VendorLockCompanyId.HasValue ? ProcurementSourceType.LockedVendor : ProcurementSourceType.LocalB2B,
                    SourceVendorCompanyId = salesUnit.Building.CompanyId,
                    SourceVendorName = sourceName,
                    DeliveredPricePerUnit = price,
                    EstimatedQuality = inv.Quality,
                    CanExecute = true,
                };
            }
        }

        // No qualifying local supply.
        if (unit.VendorLockCompanyId.HasValue)
        {
            var vendorCompany = await db.Companies.FindAsync(unit.VendorLockCompanyId.Value);
            return new ProcurementPreview
            {
                SourceType = ProcurementSourceType.LockedVendor,
                CanExecute = false,
                BlockReason = ProcurementBlockReason.LockedSourceUnavailable,
                BlockMessage = $"The locked vendor ({vendorCompany?.Name ?? "Unknown"}) has no qualifying stock in this city. Check their prices and available inventory.",
            };
        }

        return new ProcurementPreview
        {
            SourceType = ProcurementSourceType.LocalB2B,
            CanExecute = false,
            BlockReason = ProcurementBlockReason.NoStock,
            BlockMessage = "No qualifying same-city B2B supply was found. Configure a B2B Sales unit with matching stock in another building, or select a vendor/company that already has inventory available at a qualifying price and quality.",
        };
    }
}
