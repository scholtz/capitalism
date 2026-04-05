using Api.Data;
using Api.Data.Entities;
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

        // For EXCHANGE or OPTIMAL with a resource, evaluate global exchange offers.
        if (purchaseSource is "EXCHANGE" or "OPTIMAL" && resourceId.HasValue)
        {
            var preview = await EvaluateGlobalExchangeAsync(db, unit, building, resourceId.Value, maxPrice, minQuality);
            if (purchaseSource == "EXCHANGE" || (purchaseSource == "OPTIMAL" && preview.CanExecute))
            {
                // If EXCHANGE-only and locked city is set but unavailable, return blocked.
                if (purchaseSource == "EXCHANGE" && !preview.CanExecute)
                {
                    return preview;
                }

                if (preview.CanExecute)
                    return preview;
            }
        }

        // For LOCAL or OPTIMAL (fallback), evaluate local B2B supplies.
        if (purchaseSource is "LOCAL" or "OPTIMAL")
        {
            var localPreview = await EvaluateLocalB2BAsync(db, unit, building, resourceId, productId, maxPrice, minQuality, company.Id);
            if (localPreview.CanExecute)
                return localPreview;

            // If both paths fail for OPTIMAL, return a combined block message.
            if (purchaseSource == "OPTIMAL")
            {
                return new ProcurementPreview
                {
                    SourceType = ProcurementSourceType.NoSource,
                    CanExecute = false,
                    BlockReason = ProcurementBlockReason.NoStock,
                    BlockMessage = resourceId.HasValue
                        ? "No qualifying exchange offer or local B2B supply found. Check max price, min quality, or wait for stock."
                        : "No qualifying local B2B supply found. Check max price, min quality, or wait for stock.",
                };
            }

            return localPreview;
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

        return new ProcurementPreview
        {
            SourceType = ProcurementSourceType.NoSource,
            CanExecute = false,
            BlockReason = ProcurementBlockReason.NoStock,
            BlockMessage = "No qualifying source found for the current configuration.",
        };
    }

    private static async Task<ProcurementPreview> EvaluateGlobalExchangeAsync(
        AppDbContext db,
        BuildingUnit unit,
        Building building,
        Guid resourceId,
        decimal maxPrice,
        decimal minQuality)
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

        // Apply LockedCityId filter if set.
        var candidateCities = unit.LockedCityId.HasValue
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

        var lockedCityMissing = unit.LockedCityId.HasValue && !allCities.Any(c => c.Id == unit.LockedCityId.Value);
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
                BlockReason = unit.LockedCityId.HasValue ? ProcurementBlockReason.LockedSourceUnavailable : ProcurementBlockReason.NoStock,
                BlockMessage = unit.LockedCityId.HasValue
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
            BlockMessage = "No qualifying B2B supply found in this city. Check that you have a B2B Sales unit in another building with stock and a qualifying price.",
        };
    }
}

/// <summary>Result of evaluating a purchase unit's next-tick behavior without executing it.</summary>
public sealed class ProcurementPreview
{
    /// <summary>The type of source that would be used.</summary>
    public string SourceType { get; set; } = ProcurementSourceType.NoSource;

    /// <summary>Source city ID (for global exchange sources).</summary>
    public Guid? SourceCityId { get; set; }

    /// <summary>Human-readable source city name.</summary>
    public string? SourceCityName { get; set; }

    /// <summary>Vendor company ID (for local or vendor-locked sources).</summary>
    public Guid? SourceVendorCompanyId { get; set; }

    /// <summary>Human-readable vendor name.</summary>
    public string? SourceVendorName { get; set; }

    /// <summary>Exchange price per unit at the source city (before transit).</summary>
    public decimal? ExchangePricePerUnit { get; set; }

    /// <summary>Transit cost per unit from source city to destination.</summary>
    public decimal? TransitCostPerUnit { get; set; }

    /// <summary>Total delivered price per unit (exchange price + transit cost).</summary>
    public decimal? DeliveredPricePerUnit { get; set; }

    /// <summary>Estimated quality of the goods that would be purchased (0–1).</summary>
    public decimal? EstimatedQuality { get; set; }

    /// <summary>Whether the purchase would execute on the next tick.</summary>
    public bool CanExecute { get; set; }

    /// <summary>Machine-readable block reason code.</summary>
    public string? BlockReason { get; set; }

    /// <summary>Human-readable explanation of why the purchase is blocked.</summary>
    public string? BlockMessage { get; set; }
}

/// <summary>Well-known source type strings for <see cref="ProcurementPreview"/>.</summary>
public static class ProcurementSourceType
{
    public const string GlobalExchange = "GLOBAL_EXCHANGE";
    public const string LocalB2B = "LOCAL_B2B";
    public const string LockedVendor = "LOCKED_VENDOR";
    public const string NoSource = "NO_SOURCE";
}

/// <summary>Well-known block reason codes for <see cref="ProcurementPreview"/>.</summary>
public static class ProcurementBlockReason
{
    public const string NotConfigured = "NOT_CONFIGURED";
    public const string MaxPriceExceeded = "MAX_PRICE_EXCEEDED";
    public const string MinQualityFailed = "MIN_QUALITY_FAILED";
    public const string NoStock = "NO_STOCK";
    public const string LockedSourceUnavailable = "LOCKED_SOURCE_UNAVAILABLE";
    public const string InsufficientCash = "INSUFFICIENT_CASH";
}
