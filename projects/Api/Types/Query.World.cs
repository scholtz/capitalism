using System.Security.Claims;
using Api.Configuration;
using Api.Data;
using Api.Data.Entities;
using Api.Engine;
using Api.Security;
using Api.Utilities;
using HotChocolate.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Api.Types;

public sealed partial class Query
{
    public async Task<List<City>> GetCities([Service] AppDbContext db)
    {
        return await db.Cities
            .Include(c => c.Resources)
            .ThenInclude(r => r.ResourceType)
            .OrderBy(c => c.Name)
            .ToListAsync();
    }

    /// <summary>Gets a specific city by ID.</summary>
    public async Task<City?> GetCity(Guid id, [Service] AppDbContext db)
    {
        var gameState = await db.GameStates.FirstOrDefaultAsync();
        if (gameState is not null)
        {
            await BuildingConfigurationService.ApplyDuePlansAsync(db, gameState.CurrentTick);
            await db.SaveChangesAsync();
        }

        return await db.Cities
            .Include(c => c.Resources)
            .ThenInclude(r => r.ResourceType)
            .Include(c => c.Buildings)
            .ThenInclude(b => b.PendingConfiguration)
            .ThenInclude(plan => plan!.Units)
            .Include(c => c.Buildings)
            .ThenInclude(b => b.PendingConfiguration)
            .ThenInclude(plan => plan!.Removals)
            .Include(c => c.Buildings)
            .ThenInclude(b => b.Units)
            .AsSplitQuery()
            .FirstOrDefaultAsync(c => c.Id == id);
    }

    /// <summary>Lists all raw material resource types in the game encyclopaedia.</summary>
    public async Task<List<ResourceType>> GetResourceTypes([Service] AppDbContext db)
    {
        return await db.ResourceTypes.OrderBy(r => r.Name).ToListAsync();
    }

    /// <summary>
    /// Returns a single resource type identified by its URL slug, together with all product types
    /// that use it as a direct ingredient. Designed for the encyclopedia resource detail view.
    /// Returns null when no resource with the given slug exists.
    /// </summary>
    public async Task<EncyclopediaResourceDetail?> GetEncyclopediaResource(
        string slug,
        [Service] AppDbContext db,
        [Service] IHttpContextAccessor httpContextAccessor)
    {
        var resource = await db.ResourceTypes
            .FirstOrDefaultAsync(r => r.Slug == slug);

        if (resource is null)
        {
            return null;
        }

        var hasActiveProSubscription = false;
        if (httpContextAccessor.HttpContext?.User.Identity?.IsAuthenticated == true)
        {
            var userId = httpContextAccessor.HttpContext.User.GetRequiredUserId();
            var subscriptionEndsAtUtc = await db.Players
                .Where(player => player.Id == userId)
                .Select(player => player.ProSubscriptionEndsAtUtc)
                .FirstOrDefaultAsync();

            hasActiveProSubscription = ProductAccessService.HasActiveProSubscription(subscriptionEndsAtUtc, DateTime.UtcNow);
        }

        var products = await db.ProductTypes
            .Include(p => p.Recipes)
            .ThenInclude(r => r.ResourceType)
            .Include(p => p.Recipes)
            .ThenInclude(r => r.InputProductType)
            .Where(p => p.Recipes.Any(r => r.ResourceTypeId == resource.Id))
            .OrderBy(p => p.Name)
            .ToListAsync();

        ProductAccessService.ApplyAccessMetadata(products, hasActiveProSubscription);

        return new EncyclopediaResourceDetail
        {
            Resource = resource,
            ProductsUsingResource = products,
        };
    }

    /// <summary>Lists all product types, optionally filtered by industry.</summary>
    public async Task<List<ProductType>> GetProductTypes(
        string? industry,
        [Service] AppDbContext db,
        [Service] IHttpContextAccessor httpContextAccessor)
    {
        var hasActiveProSubscription = false;
        if (httpContextAccessor.HttpContext?.User.Identity?.IsAuthenticated == true)
        {
            var userId = httpContextAccessor.HttpContext.User.GetRequiredUserId();
            var subscriptionEndsAtUtc = await db.Players
                .Where(player => player.Id == userId)
                .Select(player => player.ProSubscriptionEndsAtUtc)
                .FirstOrDefaultAsync();

            hasActiveProSubscription = ProductAccessService.HasActiveProSubscription(subscriptionEndsAtUtc, DateTime.UtcNow);
        }

        var query = db.ProductTypes
            .Include(p => p.Recipes)
            .ThenInclude(r => r.ResourceType)
            .Include(p => p.Recipes)
            .ThenInclude(r => r.InputProductType)
            .AsSplitQuery()
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(industry))
        {
            query = query.Where(p => p.Industry == industry);
        }

        var products = await query.OrderBy(p => p.Name).ToListAsync();
        ProductAccessService.ApplyAccessMetadata(products, hasActiveProSubscription);
        return products;
    }

    /// <summary>
    /// Returns ranked product candidates for a unit configuration picker.
    ///
    /// Ranking rules:
    /// <list type="bullet">
    ///   <item>
    ///     <b>PUBLIC_SALES</b> context: products already configured in MANUFACTURING or B2B_SALES
    ///     units within the same building are ranked as <c>connected</c> (score 100) first.
    ///   </item>
    ///   <item>
    ///     <b>STORAGE</b> context: products from MANUFACTURING units in the same building plus
    ///     products currently present in the building's inventory are ranked as <c>connected</c> (score 100).
    ///   </item>
    ///   <item>
    ///     <b>B2B_SALES</b> context: products configured in MANUFACTURING or STORAGE units within
    ///     the same building (including pending configuration) are ranked as <c>connected</c> (score 100).
    ///   </item>
    ///   <item>
    ///     <b>PRODUCT_QUALITY</b> or <b>BRAND_QUALITY</b> context: products used in MANUFACTURING
    ///     units across all buildings owned by the caller's companies are ranked as
    ///     <c>used_by_company</c> (score 50).
    ///   </item>
    ///   <item>All remaining unlocked products fall into the <c>catalog</c> tier (score 10).</item>
    /// </list>
    ///
    /// Within each tier products are sorted alphabetically by name.
    /// </summary>
    [Authorize]
    public async Task<List<RankedProductResult>> GetRankedProductTypes(
        Guid buildingId,
        string unitType,
        [Service] AppDbContext db,
        [Service] IHttpContextAccessor httpContextAccessor)
    {
        var userId = httpContextAccessor.HttpContext!.User.GetRequiredUserId();

        // Determine Pro subscription status for access metadata.
        var subscriptionEndsAtUtc = await db.Players
            .Where(p => p.Id == userId)
            .Select(p => p.ProSubscriptionEndsAtUtc)
            .FirstOrDefaultAsync();
        var hasActivePro = ProductAccessService.HasActiveProSubscription(subscriptionEndsAtUtc, DateTime.UtcNow);

        // Load all product types with recipes.
        var allProducts = await db.ProductTypes
            .Include(p => p.Recipes)
            .ThenInclude(r => r.ResourceType)
            .Include(p => p.Recipes)
            .ThenInclude(r => r.InputProductType)
            .AsSplitQuery()
            .OrderBy(p => p.Name)
            .ToListAsync();

        ProductAccessService.ApplyAccessMetadata(allProducts, hasActivePro);

        // Build a set of "promoted" product IDs and the reason.
        var promotedIds = new Dictionary<Guid, string>(capacity: 16);

        var normalizedUnitType = unitType.ToUpperInvariant();

        if (normalizedUnitType is "PUBLIC_SALES")
        {
            // Promote products already configured in sibling units of the same building.
            var connectedProductIds = await db.BuildingUnits
                .Where(u => u.BuildingId == buildingId
                    && (u.UnitType == "MANUFACTURING" || u.UnitType == "B2B_SALES")
                    && u.ProductTypeId.HasValue)
                .Select(u => u.ProductTypeId!.Value)
                .Distinct()
                .ToListAsync();

            // Also check the pending configuration (draft units).
            var pendingConnectedIds = await db.BuildingConfigurationPlanUnits
                .Where(u => u.BuildingConfigurationPlan.BuildingId == buildingId
                    && (u.UnitType == "MANUFACTURING" || u.UnitType == "B2B_SALES")
                    && u.ProductTypeId.HasValue)
                .Select(u => u.ProductTypeId!.Value)
                .Distinct()
                .ToListAsync();

            foreach (var id in connectedProductIds.Concat(pendingConnectedIds).Distinct())
                promotedIds.TryAdd(id, ProductRankingReason.Connected);
        }
        else if (normalizedUnitType is "STORAGE")
        {
            // Promote products from connected MANUFACTURING units in the same building.
            var mfgProductIds = await db.BuildingUnits
                .Where(u => u.BuildingId == buildingId
                    && u.UnitType == "MANUFACTURING"
                    && u.ProductTypeId.HasValue)
                .Select(u => u.ProductTypeId!.Value)
                .Distinct()
                .ToListAsync();

            // Also check the pending configuration for MANUFACTURING products.
            var pendingMfgIds = await db.BuildingConfigurationPlanUnits
                .Where(u => u.BuildingConfigurationPlan.BuildingId == buildingId
                    && u.UnitType == "MANUFACTURING"
                    && u.ProductTypeId.HasValue)
                .Select(u => u.ProductTypeId!.Value)
                .Distinct()
                .ToListAsync();

            // Promote products already present in the building's inventory.
            var stockProductIds = await db.Inventories
                .Where(i => i.BuildingId == buildingId && i.ProductTypeId.HasValue && i.Quantity > 0)
                .Select(i => i.ProductTypeId!.Value)
                .Distinct()
                .ToListAsync();

            foreach (var id in mfgProductIds.Concat(pendingMfgIds).Concat(stockProductIds).Distinct())
                promotedIds.TryAdd(id, ProductRankingReason.Connected);
        }
        else if (normalizedUnitType is "B2B_SALES")
        {
            // Promote products from MANUFACTURING or STORAGE units in the same building.
            var connectedProductIds = await db.BuildingUnits
                .Where(u => u.BuildingId == buildingId
                    && (u.UnitType == "MANUFACTURING" || u.UnitType == "STORAGE")
                    && u.ProductTypeId.HasValue)
                .Select(u => u.ProductTypeId!.Value)
                .Distinct()
                .ToListAsync();

            // Also check the pending configuration.
            var pendingConnectedIds = await db.BuildingConfigurationPlanUnits
                .Where(u => u.BuildingConfigurationPlan.BuildingId == buildingId
                    && (u.UnitType == "MANUFACTURING" || u.UnitType == "STORAGE")
                    && u.ProductTypeId.HasValue)
                .Select(u => u.ProductTypeId!.Value)
                .Distinct()
                .ToListAsync();

            foreach (var id in connectedProductIds.Concat(pendingConnectedIds).Distinct())
                promotedIds.TryAdd(id, ProductRankingReason.Connected);
        }
        else if (normalizedUnitType is "PRODUCT_QUALITY" or "BRAND_QUALITY")
        {
            // Promote products that this player's company manufactures in any building.
            var companyIds = await db.Companies
                .Where(c => c.PlayerId == userId)
                .Select(c => c.Id)
                .ToListAsync();

            var companyBuildingIds = await db.Buildings
                .Where(b => companyIds.Contains(b.CompanyId))
                .Select(b => b.Id)
                .ToListAsync();

            var usedProductIds = await db.BuildingUnits
                .Where(u => companyBuildingIds.Contains(u.BuildingId)
                    && u.UnitType == "MANUFACTURING"
                    && u.ProductTypeId.HasValue)
                .Select(u => u.ProductTypeId!.Value)
                .Distinct()
                .ToListAsync();

            foreach (var id in usedProductIds)
                promotedIds.TryAdd(id, ProductRankingReason.UsedByCompany);
        }

        // Build ranked results: promoted first (highest score), then catalog.
        return allProducts
            .Select(p =>
            {
                if (promotedIds.TryGetValue(p.Id, out var reason))
                {
                    var score = reason == ProductRankingReason.Connected ? 100 : 50;
                    return new RankedProductResult { ProductType = p, RankingReason = reason, RankingScore = score };
                }
                return new RankedProductResult { ProductType = p, RankingReason = ProductRankingReason.Catalog, RankingScore = 10 };
            })
            .OrderByDescending(r => r.RankingScore)
            .ThenBy(r => r.ProductType.Name)
            .ToList();
    }
}
