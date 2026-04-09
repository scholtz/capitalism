using Api.Data;
using Api.Data.Entities;
using Api.Engine;
using Api.Security;
using Api.Utilities;
using HotChocolate.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Api.Types;

/// <summary>
/// GraphQL query type for the Capitalism V game.
/// Provides read access to game data including players, cities, resources, products, and buildings.
/// </summary>
public sealed class Query
{
    /// <summary>Returns the currently authenticated player's profile.</summary>
    [Authorize]
    public async Task<Player?> GetMe(
        [Service] AppDbContext db,
        [Service] IHttpContextAccessor httpContextAccessor)
    {
        var userId = httpContextAccessor.HttpContext!.User.GetRequiredUserId();
        return await db.Players
            .Include(p => p.Companies)
            .FirstOrDefaultAsync(p => p.Id == userId);
    }

    /// <summary>Returns the authenticated player's personal account, portfolio, and dividend history.</summary>
    [Authorize]
    public async Task<PersonAccountResult?> GetPersonAccount(
        [Service] AppDbContext db,
        [Service] IHttpContextAccessor httpContextAccessor)
    {
        var userId = httpContextAccessor.HttpContext!.User.GetRequiredUserId();
        var player = await db.Players
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == userId);

        if (player is null)
        {
            return null;
        }

        var companies = await db.Companies
            .AsNoTracking()
            .OrderBy(company => company.Name)
            .ToListAsync();
        var buildings = await db.Buildings.AsNoTracking().ToListAsync();
        var lots = await db.BuildingLots
            .AsNoTracking()
            .Where(lot => lot.OwnerCompanyId.HasValue)
            .ToListAsync();
        var inventories = await db.Inventories
            .AsNoTracking()
            .Include(inventory => inventory.ResourceType)
            .Include(inventory => inventory.ProductType)
            .ToListAsync();
        var shareholdings = await db.Shareholdings.AsNoTracking().ToListAsync();
        var sharePriceByCompany = BuildQuotedSharePriceLookup(companies, buildings, lots, inventories, shareholdings);
        var companiesById = companies.ToDictionary(company => company.Id);

        var portfolio = shareholdings
            .Where(holding => holding.OwnerPlayerId == userId && holding.ShareCount > 0m)
            .Select(holding =>
            {
                var company = companiesById[holding.CompanyId];
                var sharePrice = sharePriceByCompany.GetValueOrDefault(holding.CompanyId);
                return new PortfolioHoldingResult
                {
                    CompanyId = company.Id,
                    CompanyName = company.Name,
                    ShareCount = holding.ShareCount,
                    OwnershipRatio = company.TotalSharesIssued > 0m
                        ? decimal.Round(holding.ShareCount / company.TotalSharesIssued, 4, MidpointRounding.AwayFromZero)
                        : 0m,
                    SharePrice = sharePrice,
                    MarketValue = decimal.Round(holding.ShareCount * sharePrice, 4, MidpointRounding.AwayFromZero),
                };
            })
            .OrderByDescending(holding => holding.MarketValue)
            .ThenBy(holding => holding.CompanyName)
            .ToList();

        var dividendPayments = await db.DividendPayments
            .AsNoTracking()
            .Where(payment => payment.RecipientPlayerId == userId)
            .OrderByDescending(payment => payment.RecordedAtTick)
            .ToListAsync();

        return new PersonAccountResult
        {
            PlayerId = player.Id,
            DisplayName = player.DisplayName,
            PersonalCash = player.PersonalCash,
            ActiveAccountType = player.ActiveAccountType,
            ActiveCompanyId = player.ActiveCompanyId,
            Shareholdings = portfolio,
            DividendPayments = dividendPayments
                .Select(payment => new DividendPaymentResult
                {
                    Id = payment.Id,
                    CompanyId = payment.CompanyId,
                    CompanyName = companiesById.GetValueOrDefault(payment.CompanyId)?.Name ?? string.Empty,
                    ShareCount = payment.ShareCount,
                    AmountPerShare = payment.AmountPerShare,
                    TotalAmount = payment.TotalAmount,
                    GameYear = payment.GameYear,
                    RecordedAtTick = payment.RecordedAtTick,
                    RecordedAtUtc = payment.RecordedAtUtc,
                    Description = payment.Description,
                })
                .ToList(),
        };
    }

    /// <summary>
    /// Returns quoted company share prices and public-float availability for the stock exchange.
    /// When authenticated, includes the current player's direct and controlled-company holdings.
    /// </summary>
    public async Task<List<StockExchangeListingResult>> GetStockExchangeListings(
        [Service] AppDbContext db,
        [Service] IHttpContextAccessor httpContextAccessor)
    {
        var companies = await db.Companies
            .AsNoTracking()
            .OrderBy(company => company.Name)
            .ToListAsync();
        var buildings = await db.Buildings.AsNoTracking().ToListAsync();
        var lots = await db.BuildingLots
            .AsNoTracking()
            .Where(lot => lot.OwnerCompanyId.HasValue)
            .ToListAsync();
        var inventories = await db.Inventories
            .AsNoTracking()
            .Include(inventory => inventory.ResourceType)
            .Include(inventory => inventory.ProductType)
            .ToListAsync();
        var shareholdings = await db.Shareholdings.AsNoTracking().ToListAsync();
        var sharePriceByCompany = BuildQuotedSharePriceLookup(companies, buildings, lots, inventories, shareholdings);

        Guid? userId = null;
        HashSet<Guid> controlledCompanyIds = [];
        if (httpContextAccessor.HttpContext?.User.Identity?.IsAuthenticated == true)
        {
            userId = httpContextAccessor.HttpContext.User.GetRequiredUserId();
            controlledCompanyIds = companies
                .Where(company => company.PlayerId == userId.Value)
                .Select(company => company.Id)
                .ToHashSet();
        }

        return companies
            .Select(company =>
            {
                var sharePrice = sharePriceByCompany.GetValueOrDefault(company.Id);
                var playerOwnedShares = userId.HasValue
                    ? shareholdings
                        .Where(holding => holding.CompanyId == company.Id && holding.OwnerPlayerId == userId.Value)
                        .Sum(holding => holding.ShareCount)
                    : 0m;
                var controlledCompanyOwnedShares = controlledCompanyIds.Count > 0
                    ? shareholdings
                        .Where(holding => holding.CompanyId == company.Id
                            && holding.OwnerCompanyId.HasValue
                            && controlledCompanyIds.Contains(holding.OwnerCompanyId.Value))
                        .Sum(holding => holding.ShareCount)
                    : 0m;
                var combinedRatio = company.TotalSharesIssued > 0m
                    ? decimal.Round((playerOwnedShares + controlledCompanyOwnedShares) / company.TotalSharesIssued, 4, MidpointRounding.AwayFromZero)
                    : 0m;

                return new StockExchangeListingResult
                {
                    CompanyId = company.Id,
                    CompanyName = company.Name,
                    TotalSharesIssued = company.TotalSharesIssued,
                    PublicFloatShares = SharePriceCalculator.ComputePublicFloat(company, shareholdings.Where(holding => holding.CompanyId == company.Id)),
                    SharePrice = sharePrice,
                    BidPrice = SharePriceCalculator.ComputeBidPrice(sharePrice),
                    AskPrice = SharePriceCalculator.ComputeAskPrice(sharePrice),
                    DividendPayoutRatio = company.DividendPayoutRatio,
                    PlayerOwnedShares = playerOwnedShares,
                    ControlledCompanyOwnedShares = controlledCompanyOwnedShares,
                    CombinedControlledOwnershipRatio = combinedRatio,
                    CanClaimControl = userId.HasValue && company.PlayerId != userId.Value && combinedRatio >= 0.5m,
                };
            })
            .OrderByDescending(listing => listing.SharePrice)
            .ThenBy(listing => listing.CompanyName)
            .ToList();
    }

    private static Dictionary<Guid, decimal> BuildQuotedSharePriceLookup(
        IReadOnlyCollection<Company> companies,
        IReadOnlyCollection<Building> buildings,
        IReadOnlyCollection<BuildingLot> lots,
        IReadOnlyCollection<Inventory> inventories,
        IReadOnlyCollection<Shareholding> shareholdings)
    {
        var baseEquityByCompany = SharePriceCalculator.ComputeBaseEquityByCompany(companies, buildings, lots, inventories);
        return SharePriceCalculator.ComputeQuotedSharePriceByCompany(companies, baseEquityByCompany, shareholdings);
    }

    /// <summary>Returns the current player's startup-pack offer if they are eligible.</summary>
    [Authorize]
    public async Task<StartupPackOffer?> GetStartupPackOffer(
        [Service] AppDbContext db,
        [Service] IHttpContextAccessor httpContextAccessor)
    {
        var userId = httpContextAccessor.HttpContext!.User.GetRequiredUserId();
        var player = await db.Players.FirstOrDefaultAsync(candidate => candidate.Id == userId);
        if (player is null)
        {
            return null;
        }

        return await StartupPackService.EnsureOfferForPlayerAsync(db, player, DateTime.UtcNow);
    }

    /// <summary>Lists all cities available on the game map.</summary>
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

    /// <summary>
    /// Gets the player ranking (leaderboard) sorted by total wealth.
    ///
    /// Wealth formula (interim, will evolve as the economy simulation matures):
    ///   TotalWealth = CashTotal + BuildingValue + InventoryValue
    ///   - CashTotal:      Sum of company.Cash across all owned companies.
    ///   - BuildingValue:  Sum of (BuildingBaseValue[type] × level) for each building.
    ///                     Base values reflect construction cost and are documented in
    ///                     <see cref="WealthCalculator.BuildingBaseValues"/>.
    ///   - InventoryValue: Sum of (inventory.Quantity × item.BasePrice) for all
    ///                     resource and product stock in company buildings.
    /// </summary>
    public async Task<List<PlayerRanking>> GetRankings([Service] AppDbContext db)
    {
        var players = await db.Players
            .Include(p => p.Companies)
            .ThenInclude(c => c.Buildings)
            .ThenInclude(b => b.Units)
            .Where(p => p.Role != PlayerRole.Admin)
            .ToListAsync();

        // Load all inventory for non-admin players' buildings with price lookups.
        var companyIds = players.SelectMany(p => p.Companies).Select(c => c.Id).ToList();
        var buildingIds = players.SelectMany(p => p.Companies)
            .SelectMany(c => c.Buildings)
            .Select(b => b.Id)
            .ToList();

        var inventories = await db.Inventories
            .Where(i => buildingIds.Contains(i.BuildingId))
            .Include(i => i.ResourceType)
            .Include(i => i.ProductType)
            .ToListAsync();

        var inventoryByBuilding = inventories
            .GroupBy(i => i.BuildingId)
            .ToDictionary(g => g.Key, g => g.ToList());

        return players
            .Select(p =>
            {
                var cashTotal = p.Companies.Sum(c => c.Cash);
                var buildingValue = p.Companies
                    .SelectMany(c => c.Buildings)
                    .Sum(b => WealthCalculator.GetBuildingValue(b));
                var inventoryValue = p.Companies
                    .SelectMany(c => c.Buildings)
                    .Sum(b => inventoryByBuilding.TryGetValue(b.Id, out var inv)
                        ? inv.Sum(i => i.Quantity * WealthCalculator.GetItemBasePrice(i))
                        : 0m);

                return new PlayerRanking
                {
                    PlayerId = p.Id,
                    DisplayName = p.DisplayName,
                    CashTotal = cashTotal,
                    BuildingValue = buildingValue,
                    InventoryValue = inventoryValue,
                    TotalWealth = cashTotal + buildingValue + inventoryValue,
                    CompanyCount = p.Companies.Count
                };
            })
            .OrderByDescending(r => r.TotalWealth)
            .ToList();
    }

    /// <summary>Returns per-company wealth rankings for the leaderboard.</summary>
    public async Task<List<CompanyRanking>> GetCompanyRankings([Service] AppDbContext db)
    {
        var companies = await db.Companies
            .Include(c => c.Buildings)
            .ThenInclude(b => b.Units)
            .Include(c => c.Player)
            .Where(c => c.Player != null && c.Player.Role != PlayerRole.Admin)
            .AsSplitQuery()
            .ToListAsync();

        var buildingIds = companies
            .SelectMany(c => c.Buildings)
            .Select(b => b.Id)
            .ToList();

        var inventories = await db.Inventories
            .Where(i => buildingIds.Contains(i.BuildingId))
            .Include(i => i.ResourceType)
            .Include(i => i.ProductType)
            .ToListAsync();

        var inventoryByBuilding = inventories
            .GroupBy(i => i.BuildingId)
            .ToDictionary(g => g.Key, g => g.ToList());

        return companies
            .Select(c =>
            {
                var buildingValue = c.Buildings
                    .Sum(b => WealthCalculator.GetBuildingValue(b));
                var inventoryValue = c.Buildings
                    .Sum(b => inventoryByBuilding.TryGetValue(b.Id, out var inv)
                        ? inv.Sum(i => i.Quantity * WealthCalculator.GetItemBasePrice(i))
                        : 0m);

                return new CompanyRanking
                {
                    CompanyId = c.Id,
                    CompanyName = c.Name,
                    PlayerId = c.PlayerId,
                    OwnerDisplayName = c.Player?.DisplayName ?? "Unknown",
                    Cash = c.Cash,
                    BuildingValue = buildingValue,
                    InventoryValue = inventoryValue,
                    TotalWealth = c.Cash + buildingValue + inventoryValue,
                    BuildingCount = c.Buildings.Count
                };
            })
            .OrderByDescending(r => r.TotalWealth)
            .ToList();
    }

    /// <summary>Gets the current player's companies with their buildings.</summary>
    [Authorize]
    public async Task<List<Company>> GetMyCompanies(
        [Service] AppDbContext db,
        [Service] IHttpContextAccessor httpContextAccessor)
    {
        var userId = httpContextAccessor.HttpContext!.User.GetRequiredUserId();

        var gameState = await db.GameStates.FirstOrDefaultAsync();
        if (gameState is not null)
        {
            await BuildingConfigurationService.ApplyDuePlansAsync(db, gameState.CurrentTick);
            await db.SaveChangesAsync();
        }

        return await db.Companies
            .Include(c => c.Buildings)
            .ThenInclude(b => b.Units)
            .Include(c => c.Buildings)
            .ThenInclude(b => b.PendingConfiguration)
            .ThenInclude(plan => plan!.Units)
            .Include(c => c.Buildings)
            .ThenInclude(b => b.PendingConfiguration)
            .ThenInclude(plan => plan!.Removals)
            .AsSplitQuery()
            .Where(c => c.PlayerId == userId)
            .OrderBy(c => c.Name)
            .ToListAsync();
    }

    /// <summary>
    /// Returns the brand research state for a company — all brands accumulated by R&amp;D and marketing
    /// so the frontend can show current product-quality and brand-awareness progress.
    /// Requires auth and company ownership.
    /// </summary>
    [Authorize]
    public async Task<List<ResearchBrandState>> GetCompanyBrands(
        Guid companyId,
        [Service] AppDbContext db,
        [Service] IHttpContextAccessor httpContextAccessor)
    {
        var userId = httpContextAccessor.HttpContext!.User.GetRequiredUserId();
        var company = await db.Companies
            .FirstOrDefaultAsync(c => c.Id == companyId && c.PlayerId == userId);
        if (company is null)
            return [];

        var brands = await db.Brands
            .Where(b => b.CompanyId == companyId)
            .ToListAsync();

        var productTypeIds = brands
            .Where(b => b.ProductTypeId.HasValue)
            .Select(b => b.ProductTypeId!.Value)
            .Distinct()
            .ToList();

        var productTypes = await db.ProductTypes
            .Where(pt => productTypeIds.Contains(pt.Id))
            .ToListAsync();

        return brands.Select(b =>
        {
            var pt = b.ProductTypeId.HasValue
                ? productTypes.FirstOrDefault(p => p.Id == b.ProductTypeId.Value)
                : null;
            return new ResearchBrandState
            {
                Id = b.Id,
                CompanyId = b.CompanyId,
                Name = b.Name,
                Scope = b.Scope,
                ProductTypeId = b.ProductTypeId,
                ProductName = pt?.Name,
                IndustryCategory = b.IndustryCategory,
                Awareness = b.Awareness,
                Quality = b.Quality,
                MarketingEfficiencyMultiplier = b.MarketingEfficiencyMultiplier,
            };
        }).ToList();
    }

    /// <summary>Returns owner-editable company settings including salary levels per city.</summary>
    [Authorize]
    public async Task<CompanySettingsResult?> GetCompanySettings(
        Guid companyId,
        [Service] AppDbContext db,
        [Service] IHttpContextAccessor httpContextAccessor)
    {
        var userId = httpContextAccessor.HttpContext!.User.GetRequiredUserId();
        var company = await db.Companies
            .Include(candidate => candidate.CitySalarySettings)
            .FirstOrDefaultAsync(candidate => candidate.Id == companyId && candidate.PlayerId == userId);

        if (company is null)
        {
            return null;
        }

        var cities = await db.Cities
            .OrderBy(city => city.Name)
            .ToListAsync();
        var allCompanies = await db.Companies
            .Include(candidate => candidate.Buildings)
            .ToListAsync();
        var allOwnedLots = await db.BuildingLots
            .Where(lot => lot.OwnerCompanyId.HasValue)
            .ToListAsync();
        var companyBuildingIds = allCompanies
            .SelectMany(candidate => candidate.Buildings)
            .Select(building => building.Id)
            .ToList();
        var allInventories = await db.Inventories
            .Where(inventory => companyBuildingIds.Contains(inventory.BuildingId))
            .Include(inventory => inventory.ResourceType)
            .Include(inventory => inventory.ProductType)
            .ToListAsync();

        var companyAssetValues = allCompanies.ToDictionary(
            candidate => candidate.Id,
            candidate => ComputeCompanyAssetValue(candidate, allOwnedLots, allInventories));
        var assetValue = companyAssetValues.GetValueOrDefault(company.Id);
        var currentTick = await db.GameStates.AsNoTracking().Select(state => state.CurrentTick).FirstOrDefaultAsync();
        var maxAssetValue = companyAssetValues.Values.DefaultIfEmpty(0m).Max();
        var overheadRate = CompanyEconomyCalculator.ComputeAdministrationOverheadRate(
            company,
            assetValue,
            maxAssetValue,
            currentTick);
        var (ageFactor, assetFactor) = CompanyEconomyCalculator.ComputeAdministrationOverheadDrivers(
            company,
            assetValue,
            maxAssetValue,
            currentTick);

        return new CompanySettingsResult
        {
            CompanyId = company.Id,
            CompanyName = company.Name,
            Cash = company.Cash,
            TotalSharesIssued = company.TotalSharesIssued,
            DividendPayoutRatio = company.DividendPayoutRatio,
            FoundedAtTick = company.FoundedAtTick,
            AdministrationOverheadRate = overheadRate,
            AgeFactor = ageFactor,
            AssetFactor = assetFactor,
            AssetValue = assetValue,
            CitySalarySettings = cities
                .Select(city =>
                {
                    var multiplier = CompanyEconomyCalculator.GetSalaryMultiplier(company.CitySalarySettings, city.Id);
                    return new CompanyCitySalarySettingResult
                    {
                        CityId = city.Id,
                        CityName = city.Name,
                        BaseSalaryPerManhour = city.BaseSalaryPerManhour,
                        SalaryMultiplier = multiplier,
                        EffectiveSalaryPerManhour = CompanyEconomyCalculator.GetEffectiveHourlyWage(city, multiplier),
                    };
                })
                .ToList(),
        };
    }

    /// <summary>Gets the current game state (tick, tax info).</summary>
    public async Task<GameState?> GetGameState([Service] AppDbContext db)
    {
        var gameState = await db.GameStates.FirstOrDefaultAsync();
        if (gameState is null)
        {
            return null;
        }

        await BuildingConfigurationService.ApplyDuePlansAsync(db, gameState.CurrentTick);
        await db.SaveChangesAsync();
        return gameState;
    }

    /// <summary>Gets available starter industries for onboarding.</summary>
    public StarterIndustriesPayload GetStarterIndustries()
    {
        return new StarterIndustriesPayload
        {
            Industries = Industry.StarterIndustries.ToList()
        };
    }

    /// <summary>Lists building lots for a city, including ownership and availability state.</summary>
    public async Task<List<BuildingLot>> GetCityLots(Guid cityId, [Service] AppDbContext db)
    {
        return await db.BuildingLots
            .Include(lot => lot.OwnerCompany)
            .Include(lot => lot.Building)
            .Include(lot => lot.ResourceType)
            .Where(lot => lot.CityId == cityId)
            .OrderBy(lot => lot.District)
            .ThenBy(lot => lot.Name)
            .ToListAsync();
    }

    /// <summary>Gets a single building lot by ID.</summary>
    public async Task<BuildingLot?> GetLot(Guid id, [Service] AppDbContext db)
    {
        return await db.BuildingLots
            .Include(lot => lot.OwnerCompany)
            .Include(lot => lot.Building)
            .Include(lot => lot.ResourceType)
            .FirstOrDefaultAsync(lot => lot.Id == id);
    }

    /// <summary>
    /// Returns product marketplace listings from active player SELL exchange orders.
    /// Covers finished goods, intermediate goods, and any product types on the exchange.
    /// This query is public and does not require authentication.
    /// </summary>
    public async Task<List<GlobalExchangeProductListing>> GetGlobalExchangeProductListings(
        Guid? productTypeId,
        [Service] AppDbContext db)
    {
        var ordersQuery = db.ExchangeOrders
            .Where(o => o.Side == "SELL" && o.IsActive && o.RemainingQuantity > 0m
                        && o.ProductTypeId.HasValue && !o.ResourceTypeId.HasValue)
            .Include(o => o.Company)
            .Include(o => o.ExchangeBuilding)
            .AsQueryable();

        if (productTypeId.HasValue)
            ordersQuery = ordersQuery.Where(o => o.ProductTypeId == productTypeId.Value);

        var orders = await ordersQuery
            .OrderBy(o => o.ProductTypeId)
            .ThenBy(o => o.PricePerUnit)
            .ToListAsync();

        var cityIds = orders.Select(o => o.ExchangeBuilding.CityId).Distinct().ToList();
        var cities = await db.Cities
            .Where(c => cityIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id);

        var productTypeIds = orders.Select(o => o.ProductTypeId!.Value).Distinct().ToList();
        var productTypes = await db.ProductTypes
            .Where(p => productTypeIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id);

        return orders
            .Select(o =>
            {
                cities.TryGetValue(o.ExchangeBuilding.CityId, out var city);
                productTypes.TryGetValue(o.ProductTypeId!.Value, out var product);
                return new GlobalExchangeProductListing
                {
                    OrderId = o.Id,
                    ProductTypeId = o.ProductTypeId!.Value,
                    ProductName = product?.Name ?? string.Empty,
                    ProductSlug = product?.Slug ?? string.Empty,
                    ProductIndustry = product?.Industry ?? string.Empty,
                    UnitSymbol = product?.UnitSymbol ?? string.Empty,
                    UnitName = product?.UnitName ?? string.Empty,
                    BasePrice = product?.BasePrice ?? 0m,
                    PricePerUnit = o.PricePerUnit,
                    RemainingQuantity = o.RemainingQuantity,
                    SellerCityId = city?.Id ?? Guid.Empty,
                    SellerCityName = city?.Name ?? string.Empty,
                    SellerCompanyId = o.CompanyId,
                    SellerCompanyName = o.Company.Name,
                    CreatedAtUtc = o.CreatedAtUtc,
                };
            })
            .OrderBy(l => l.ProductName)
            .ThenBy(l => l.PricePerUnit)
            .ToList();
    }

    /// <summary>
    /// Returns city-level global exchange offers for raw materials, including
    /// quality and estimated transit cost into the destination city.
    /// </summary>
    public async Task<List<GlobalExchangeOffer>> GetGlobalExchangeOffers(
        Guid destinationCityId,
        Guid? resourceTypeId,
        [Service] AppDbContext db)
    {
        var destinationCity = await db.Cities.FirstOrDefaultAsync(city => city.Id == destinationCityId);
        if (destinationCity is null)
        {
            return [];
        }

        var cities = await db.Cities
            .Include(city => city.Resources)
            .OrderBy(city => city.Name)
            .ToListAsync();

        var resourceQuery = db.ResourceTypes.AsQueryable();
        if (resourceTypeId.HasValue)
        {
            resourceQuery = resourceQuery.Where(resource => resource.Id == resourceTypeId.Value);
        }

        var resources = await resourceQuery
            .OrderBy(resource => resource.Name)
            .ToListAsync();

        return cities
            .SelectMany(city => resources.Select(resource =>
            {
                var abundance = city.Resources
                    .FirstOrDefault(entry => entry.ResourceTypeId == resource.Id)?.Abundance
                    ?? GlobalExchangeCalculator.DefaultMissingAbundance;
                var exchangePrice = GlobalExchangeCalculator.ComputeExchangePrice(city, resource, abundance);
                var transitCost = GlobalExchangeCalculator.ComputeTransitCostPerUnit(city, destinationCity, resource);

                return new GlobalExchangeOffer
                {
                    CityId = city.Id,
                    CityName = city.Name,
                    ResourceTypeId = resource.Id,
                    ResourceName = resource.Name,
                    ResourceSlug = resource.Slug,
                    UnitSymbol = resource.UnitSymbol,
                    LocalAbundance = decimal.Round(abundance, 4, MidpointRounding.AwayFromZero),
                    ExchangePricePerUnit = exchangePrice,
                    EstimatedQuality = GlobalExchangeCalculator.ComputeExchangeQuality(abundance),
                    TransitCostPerUnit = transitCost,
                    DeliveredPricePerUnit = exchangePrice + transitCost,
                    DistanceKm = decimal.Round(
                        (decimal)GlobalExchangeCalculator.ComputeDistanceKm(
                            city.Latitude,
                            city.Longitude,
                            destinationCity.Latitude,
                            destinationCity.Longitude),
                        1,
                        MidpointRounding.AwayFromZero)
                };
            }))
            .OrderBy(offer => offer.DeliveredPricePerUnit)
            .ThenByDescending(offer => offer.EstimatedQuality)
            .ThenBy(offer => offer.CityName)
            .ToList();
    }

    /// <summary>
    /// Returns per-unit inventory fill information for a building that belongs
    /// to the authenticated player.
    /// </summary>
    [Authorize]
    public async Task<List<BuildingUnitInventorySummary>> GetBuildingUnitInventorySummaries(
        Guid buildingId,
        [Service] AppDbContext db,
        [Service] IHttpContextAccessor httpContextAccessor)
    {
        var userId = httpContextAccessor.HttpContext!.User.GetRequiredUserId();

        var building = await db.Buildings
            .Include(candidate => candidate.Company)
            .Include(candidate => candidate.Units)
            .FirstOrDefaultAsync(candidate => candidate.Id == buildingId);

        if (building is null || building.Company.PlayerId != userId)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Building not found or you don't own it.")
                    .SetCode("BUILDING_NOT_FOUND")
                    .Build());
        }

        var inventories = await db.Inventories
            .Where(entry => entry.BuildingId == buildingId && entry.BuildingUnitId.HasValue)
            .ToListAsync();

        return building.Units
            .Select(unit =>
            {
                var capacity = GetUnitInventoryCapacity(unit);
                var unitInventories = inventories
                    .Where(entry => entry.BuildingUnitId == unit.Id)
                    .ToList();
                var quantity = unitInventories.Sum(entry => entry.Quantity);
                var averageQuality = quantity > 0m
                    ? decimal.Round(unitInventories.Sum(entry => entry.Quantity * entry.Quality) / quantity, 4, MidpointRounding.AwayFromZero)
                    : (decimal?)null;

                return new BuildingUnitInventorySummary
                {
                    BuildingUnitId = unit.Id,
                    Quantity = decimal.Round(quantity, 4, MidpointRounding.AwayFromZero),
                    Capacity = capacity,
                    FillPercent = capacity > 0m
                        ? decimal.Round(Math.Clamp(quantity / capacity, 0m, 1m), 4, MidpointRounding.AwayFromZero)
                        : 0m,
                    AverageQuality = averageQuality,
                    TotalSourcingCost = decimal.Round(unitInventories.Sum(entry => entry.SourcingCostTotal), 4, MidpointRounding.AwayFromZero),
                    SourcingCostPerUnit = quantity > 0m
                        ? decimal.Round(unitInventories.Sum(entry => entry.SourcingCostTotal) / quantity, 4, MidpointRounding.AwayFromZero)
                        : 0m
                };
            })
            .Where(summary => summary.Capacity > 0m || summary.Quantity > 0m)
            .ToList();
    }

    /// <summary>
    /// Returns detailed inventory entries for units in a building that belongs
    /// to the authenticated player.
    /// </summary>
    [Authorize]
    public async Task<List<BuildingUnitInventory>> BuildingUnitInventories(
        Guid buildingId,
        [Service] AppDbContext db,
        [Service] IHttpContextAccessor httpContextAccessor)
    {
        var userId = httpContextAccessor.HttpContext!.User.GetRequiredUserId();

        var building = await db.Buildings
            .Include(candidate => candidate.Company)
            .FirstOrDefaultAsync(candidate => candidate.Id == buildingId);

        if (building is null || building.Company.PlayerId != userId)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Building not found or you don't own it.")
                    .SetCode("BUILDING_NOT_FOUND")
                    .Build());
        }

        var inventories = await db.Inventories
            .Where(entry => entry.BuildingId == buildingId && entry.BuildingUnitId.HasValue)
            .Select(entry => new BuildingUnitInventory
            {
                Id = entry.Id,
                BuildingUnitId = entry.BuildingUnitId!.Value,
                ResourceTypeId = entry.ResourceTypeId,
                ProductTypeId = entry.ProductTypeId,
                Quantity = entry.Quantity,
                SourcingCostTotal = entry.SourcingCostTotal,
                SourcingCostPerUnit = entry.Quantity > 0m
                    ? decimal.Round(entry.SourcingCostTotal / entry.Quantity, 4, MidpointRounding.AwayFromZero)
                    : 0m,
                Quality = entry.Quality
            })
            .ToListAsync();

        return inventories;
    }

    /// <summary>
    /// Returns recent per-tick movement history for every tracked resource or
    /// product inside the building's units.
    /// </summary>
    [Authorize]
    public async Task<List<BuildingUnitResourceHistoryPoint>> GetBuildingUnitResourceHistories(
        Guid buildingId,
        int? limit,
        [Service] AppDbContext db,
        [Service] IHttpContextAccessor httpContextAccessor)
    {
        var userId = httpContextAccessor.HttpContext!.User.GetRequiredUserId();

        var building = await db.Buildings
            .Include(candidate => candidate.Company)
            .FirstOrDefaultAsync(candidate => candidate.Id == buildingId);

        if (building is null || building.Company.PlayerId != userId)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Building not found or you don't own it.")
                    .SetCode("BUILDING_NOT_FOUND")
                    .Build());
        }

        var historyQuery = db.BuildingUnitResourceHistories
            .Where(entry => entry.BuildingId == buildingId);

        var safeLimit = Math.Clamp(limit ?? 40, 1, 200);
        var maxTick = await historyQuery.MaxAsync(entry => (long?)entry.Tick);
        if (maxTick.HasValue)
        {
            var minTick = maxTick.Value - safeLimit + 1;
            historyQuery = historyQuery.Where(entry => entry.Tick >= minTick);
        }

        return await historyQuery
            .OrderBy(entry => entry.Tick)
            .ThenBy(entry => entry.BuildingUnitId)
            .ThenBy(entry => entry.ResourceTypeId)
            .ThenBy(entry => entry.ProductTypeId)
            .Select(entry => new BuildingUnitResourceHistoryPoint
            {
                BuildingUnitId = entry.BuildingUnitId,
                ResourceTypeId = entry.ResourceTypeId,
                ProductTypeId = entry.ProductTypeId,
                Tick = entry.Tick,
                InflowQuantity = entry.InflowQuantity,
                OutflowQuantity = entry.OutflowQuantity,
                ConsumedQuantity = entry.ConsumedQuantity,
                ProducedQuantity = entry.ProducedQuantity,
            })
            .ToListAsync();
    }

    /// <summary>
    /// Returns all pending scheduled actions for the authenticated player.
    /// Currently covers building configuration upgrades (layout changes) that have not yet applied.
    /// </summary>
    [Authorize]
    public async Task<List<ScheduledActionSummary>> GetMyPendingActions(
        [Service] AppDbContext db,
        [Service] IHttpContextAccessor httpContextAccessor)
    {
        var userId = httpContextAccessor.HttpContext!.User.GetRequiredUserId();

        var gameState = await db.GameStates.FirstOrDefaultAsync();
        var currentTick = gameState?.CurrentTick ?? 0L;

        var plans = await db.BuildingConfigurationPlans
            .Include(plan => plan.Building)
            .ThenInclude(building => building.Company)
            .Where(plan => plan.Building.Company.PlayerId == userId
                           && plan.AppliesAtTick > currentTick)
            .OrderBy(plan => plan.AppliesAtTick)
            .ToListAsync();

        return plans
            .Select(plan => new ScheduledActionSummary
            {
                Id = plan.Id,
                ActionType = ScheduledActionType.BuildingUpgrade,
                BuildingId = plan.BuildingId,
                BuildingName = plan.Building.Name,
                BuildingType = plan.Building.Type,
                SubmittedAtUtc = plan.SubmittedAtUtc,
                SubmittedAtTick = plan.SubmittedAtTick,
                AppliesAtTick = plan.AppliesAtTick,
                TicksRemaining = plan.AppliesAtTick - currentTick,
                TotalTicksRequired = plan.TotalTicksRequired,
            })
            .ToList();
    }

    /// <summary>
    /// Returns per-unit operational status for a building owned by the authenticated player.
    /// Each record describes whether a unit is actively processing, idle, or blocked and why.
    /// </summary>
    [Authorize]
    public async Task<List<BuildingUnitOperationalStatus>> GetBuildingUnitOperationalStatuses(
        Guid buildingId,
        [Service] AppDbContext db,
        [Service] IHttpContextAccessor httpContextAccessor)
    {
        var userId = httpContextAccessor.HttpContext!.User.GetRequiredUserId();

        var building = await db.Buildings
            .Include(b => b.Company)
            .FirstOrDefaultAsync(b => b.Id == buildingId);

        if (building is null || building.Company.PlayerId != userId)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Building not found or you don't own it.")
                    .SetCode("BUILDING_NOT_FOUND")
                    .Build());
        }

        var units = await db.BuildingUnits
            .Where(u => u.BuildingId == buildingId)
            .ToListAsync();

        var unitIds = units.Select(u => u.Id).ToList();

        // Load current inventory totals per unit.
        var inventoryByUnit = await db.Inventories
            .Where(i => i.BuildingUnitId.HasValue && unitIds.Contains(i.BuildingUnitId!.Value))
            .GroupBy(i => i.BuildingUnitId!.Value)
            .Select(g => new { UnitId = g.Key, Total = g.Sum(i => i.Quantity) })
            .ToDictionaryAsync(x => x.UnitId, x => x.Total);

        // Load recent history (last 5 ticks) to detect idle vs active units.
        var gameState = await db.GameStates.FirstOrDefaultAsync();
        var currentTick = gameState?.CurrentTick ?? 0L;
        var windowStart = Math.Max(0L, currentTick - 4L);

        var recentHistoryByUnit = await db.BuildingUnitResourceHistories
            .Where(h => h.BuildingId == buildingId && h.Tick >= windowStart)
            .GroupBy(h => h.BuildingUnitId)
            .Select(g => new
            {
                UnitId = g.Key,
                LastActiveTick = g.Max(h => h.Tick),
                TotalInflow = g.Sum(h => h.InflowQuantity),
                TotalConsumed = g.Sum(h => h.ConsumedQuantity),
                TotalProduced = g.Sum(h => h.ProducedQuantity),
                TotalOutflow = g.Sum(h => h.OutflowQuantity),
            })
            .ToDictionaryAsync(x => x.UnitId, x => x);

        // Load recent public sales for PUBLIC_SALES units.
        var recentSales = await db.PublicSalesRecords
            .Where(r => r.BuildingId == buildingId && r.Tick >= windowStart)
            .GroupBy(r => r.BuildingUnitId)
            .Select(g => new { UnitId = g.Key, TotalSold = g.Sum(r => r.QuantitySold) })
            .ToDictionaryAsync(x => x.UnitId, x => x.TotalSold);

        // Load city and salary settings to compute per-unit next-tick operating costs.
        var city = await db.Cities.FirstOrDefaultAsync(c => c.Id == building.CityId);
        var salarySetting = await db.CompanyCitySalarySettings
            .FirstOrDefaultAsync(s => s.CompanyId == building.CompanyId && s.CityId == building.CityId);
        var salaryMultiplier = CompanyEconomyCalculator.ClampSalaryMultiplier(
            salarySetting?.SalaryMultiplier ?? CompanyEconomyCalculator.DefaultSalaryMultiplier);
        var hourlyWage = city is not null
            ? CompanyEconomyCalculator.GetEffectiveHourlyWage(city, salaryMultiplier)
            : 0m;

        var result = new List<BuildingUnitOperationalStatus>();

        foreach (var unit in units)
        {
            inventoryByUnit.TryGetValue(unit.Id, out var inventoryTotal);
            recentHistoryByUnit.TryGetValue(unit.Id, out var history);
            var capacity = GetUnitInventoryCapacity(unit);
            var fillRatio = capacity > 0m ? inventoryTotal / capacity : 0m;

            var idleTicks = 0;
            if (history is not null && currentTick > 0)
            {
                idleTicks = (int)(currentTick - history.LastActiveTick);
            }
            else if (currentTick > 0)
            {
                idleTicks = (int)Math.Min(currentTick, 5L);
            }

            var status = DeriveUnitOperationalStatus(
                unit, inventoryTotal, capacity, fillRatio, history?.TotalInflow ?? 0m,
                history?.TotalConsumed ?? 0m, history?.TotalProduced ?? 0m,
                recentSales.GetValueOrDefault(unit.Id), idleTicks);

            // Compute per-unit next-tick operating costs from game constants.
            var laborHours = CompanyEconomyCalculator.GetBaseUnitLaborHours(unit.UnitType, unit.Level);
            var energyMwh = CompanyEconomyCalculator.GetBaseUnitEnergyMwh(unit.UnitType, unit.Level);
            status.NextTickLaborCost = laborHours > 0m && hourlyWage > 0m
                ? decimal.Round(laborHours * hourlyWage, 2, MidpointRounding.AwayFromZero)
                : (decimal?)null;
            status.NextTickEnergyCost = energyMwh > 0m
                ? decimal.Round(energyMwh * Engine.GameConstants.EnergyPricePerMwh, 2, MidpointRounding.AwayFromZero)
                : (decimal?)null;

            result.Add(status);
        }

        return result;
    }

    /// <summary>
    /// Returns per-unit operational status for a building owned by the authenticated player.
    /// Each record describes whether a unit is actively processing, idle, blocked due to a missing input, or not yet configured.
    /// </summary>
    [Authorize]
    public async Task<ProcurementPreview> ProcurementPreview(
        Guid buildingUnitId,
        [Service] AppDbContext db,
        [Service] IHttpContextAccessor httpContextAccessor)
    {
        var userId = httpContextAccessor.HttpContext!.User.GetRequiredUserId();

        var unit = await db.BuildingUnits
            .Include(u => u.Building)
            .ThenInclude(b => b.Company)
            .FirstOrDefaultAsync(u => u.Id == buildingUnitId);

        if (unit is null || unit.Building.Company.PlayerId != userId)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Building unit not found or you don't own it.")
                    .SetCode("BUILDING_UNIT_NOT_FOUND")
                    .Build());
        }

        return await ProcurementPreviewService.ComputeAsync(db, unit, unit.Building.Company);
    }

    private static BuildingUnitOperationalStatus DeriveUnitOperationalStatus(
        Data.Entities.BuildingUnit unit,
        decimal inventoryTotal,
        decimal capacity,
        decimal fillRatio,
        decimal recentInflow,
        decimal recentConsumed,
        decimal recentProduced,
        decimal recentSold,
        int idleTicks)
    {
        var s = new BuildingUnitOperationalStatus { BuildingUnitId = unit.Id, IdleTicks = idleTicks };

        switch (unit.UnitType)
        {
            case UnitType.Purchase:
                if (unit.ResourceTypeId is null && unit.ProductTypeId is null)
                {
                    s.Status = "UNCONFIGURED";
                    s.BlockedCode = "UNCONFIGURED";
                    s.BlockedReason = "This purchase unit is not configured. Set a resource or product type to enable buying.";
                }
                else if (fillRatio >= 0.98m)
                {
                    s.Status = "FULL";
                    s.BlockedCode = "OUTPUT_FULL";
                    s.BlockedReason = "Storage is full. The purchased stock cannot go anywhere until downstream units free up space.";
                }
                else if (recentInflow > 0m)
                {
                    s.Status = "ACTIVE";
                }
                else
                {
                    s.Status = "BLOCKED";
                    s.BlockedCode = "AWAITING_STOCK";
                    s.BlockedReason = "No stock was purchased recently. Check that the exchange has supply at or below your max price, and that the company has cash.";
                }
                break;

            case UnitType.Manufacturing:
                if (unit.ProductTypeId is null)
                {
                    s.Status = "UNCONFIGURED";
                    s.BlockedCode = "UNCONFIGURED";
                    s.BlockedReason = "This manufacturing unit is not configured. Set a product type to start production.";
                }
                else if (fillRatio >= 0.98m)
                {
                    s.Status = "FULL";
                    s.BlockedCode = "OUTPUT_FULL";
                    s.BlockedReason = "Manufacturing output is full. Make sure downstream storage or B2B sales units have capacity to receive finished goods.";
                }
                else if (recentProduced > 0m)
                {
                    s.Status = "ACTIVE";
                }
                else if (inventoryTotal <= 0m && recentConsumed <= 0m)
                {
                    s.Status = "BLOCKED";
                    s.BlockedCode = "NO_INPUTS";
                    s.BlockedReason = "No raw materials available. Ensure purchase units are linked to this manufacturing unit and are acquiring inputs from the exchange.";
                }
                else
                {
                    s.Status = "IDLE";
                }
                break;

            case UnitType.Storage:
                if (fillRatio >= 0.98m)
                {
                    s.Status = "FULL";
                    s.BlockedCode = "OUTPUT_FULL";
                    s.BlockedReason = "Storage is full. Downstream units must consume stock before more can be received.";
                }
                else if (recentInflow > 0m || recentProduced > 0m)
                {
                    s.Status = "ACTIVE";
                }
                else
                {
                    s.Status = "IDLE";
                }
                break;

            case UnitType.B2BSales:
                if (unit.ProductTypeId is null && unit.ResourceTypeId is null)
                {
                    s.Status = "UNCONFIGURED";
                    s.BlockedCode = "UNCONFIGURED";
                    s.BlockedReason = "This B2B sales unit is not configured with a product or resource type.";
                }
                else if (inventoryTotal <= 0m)
                {
                    s.Status = "BLOCKED";
                    s.BlockedCode = "NO_INVENTORY";
                    s.BlockedReason = "No inventory available for B2B sales. Upstream units need to fill this unit with stock.";
                }
                else if (recentInflow + recentProduced > 0m)
                {
                    s.Status = "ACTIVE";
                }
                else
                {
                    s.Status = "IDLE";
                }
                break;

            case UnitType.PublicSales:
                if (unit.ProductTypeId is null && unit.ResourceTypeId is null)
                {
                    s.Status = "UNCONFIGURED";
                    s.BlockedCode = "UNCONFIGURED";
                    s.BlockedReason = "This public sales unit is not configured with a product or resource type.";
                }
                else if (inventoryTotal <= 0m)
                {
                    s.Status = "BLOCKED";
                    s.BlockedCode = "NO_INVENTORY";
                    s.BlockedReason = "No stock available to sell. Ensure the purchase unit in the same shop is receiving products from your factory's B2B unit.";
                }
                else if (recentSold > 0m)
                {
                    s.Status = "ACTIVE";
                }
                else if (inventoryTotal > 0m)
                {
                    s.Status = "BLOCKED";
                    s.BlockedCode = "NO_DEMAND";
                    s.BlockedReason = "Products are available but nothing sold recently. Try lowering your price or check that the city has sufficient population demand.";
                }
                else
                {
                    s.Status = "IDLE";
                }
                break;

            default:
                s.Status = inventoryTotal > 0m || recentInflow > 0m ? "ACTIVE" : "IDLE";
                break;
        }

        return s;
    }

    /// <summary>
    /// Returns recent tick activity events for a building in human-readable form.
    /// Events are derived from BuildingUnitResourceHistory and PublicSalesRecords.
    /// </summary>
    [Authorize]
    public async Task<List<BuildingRecentActivityEvent>> GetBuildingRecentActivity(
        Guid buildingId,
        int? limit,
        [Service] AppDbContext db,
        [Service] IHttpContextAccessor httpContextAccessor)
    {
        var userId = httpContextAccessor.HttpContext!.User.GetRequiredUserId();

        var building = await db.Buildings
            .Include(b => b.Company)
            .FirstOrDefaultAsync(b => b.Id == buildingId);

        if (building is null || building.Company.PlayerId != userId)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Building not found or you don't own it.")
                    .SetCode("BUILDING_NOT_FOUND")
                    .Build());
        }

        var safeLimit = Math.Clamp(limit ?? 30, 1, 100);

        var gameState = await db.GameStates.FirstOrDefaultAsync();
        var currentTick = gameState?.CurrentTick ?? 0L;
        var windowStart = Math.Max(0L, currentTick - (safeLimit - 1));

        // Load resource/product names for lookups.
        var historyEntries = await db.BuildingUnitResourceHistories
            .Include(h => h.ResourceType)
            .Include(h => h.ProductType)
            .Where(h => h.BuildingId == buildingId && h.Tick >= windowStart)
            .OrderByDescending(h => h.Tick)
            .ToListAsync();

        var units = await db.BuildingUnits
            .Where(u => u.BuildingId == buildingId)
            .ToDictionaryAsync(u => u.Id, u => u);

        var salesRecords = await db.PublicSalesRecords
            .Include(r => r.ProductType)
            .Include(r => r.ResourceType)
            .Where(r => r.BuildingId == buildingId && r.Tick >= windowStart)
            .OrderByDescending(r => r.Tick)
            .ToListAsync();

        var events = new List<BuildingRecentActivityEvent>();

        foreach (var entry in historyEntries)
        {
            units.TryGetValue(entry.BuildingUnitId, out var unit);
            var itemName = entry.ResourceType?.Name ?? entry.ProductType?.Name ?? "item";

            if (entry.InflowQuantity > 0m && unit?.UnitType == UnitType.Purchase)
            {
                events.Add(new BuildingRecentActivityEvent
                {
                    Tick = entry.Tick,
                    BuildingUnitId = entry.BuildingUnitId,
                    EventType = "PURCHASED",
                    Description = $"Purchased {FormatQuantity(entry.InflowQuantity)} {itemName}",
                    Quantity = entry.InflowQuantity,
                    ResourceTypeId = entry.ResourceTypeId,
                    ProductTypeId = entry.ProductTypeId,
                });
            }
            else if (entry.ProducedQuantity > 0m && unit?.UnitType == UnitType.Manufacturing)
            {
                events.Add(new BuildingRecentActivityEvent
                {
                    Tick = entry.Tick,
                    BuildingUnitId = entry.BuildingUnitId,
                    EventType = "MANUFACTURED",
                    Description = $"Manufactured {FormatQuantity(entry.ProducedQuantity)} {itemName}",
                    Quantity = entry.ProducedQuantity,
                    ResourceTypeId = entry.ResourceTypeId,
                    ProductTypeId = entry.ProductTypeId,
                });
            }
            else if (entry.OutflowQuantity > 0m && unit?.UnitType == UnitType.Storage)
            {
                events.Add(new BuildingRecentActivityEvent
                {
                    Tick = entry.Tick,
                    BuildingUnitId = entry.BuildingUnitId,
                    EventType = "MOVED",
                    Description = $"Moved {FormatQuantity(entry.OutflowQuantity)} {itemName} to next unit",
                    Quantity = entry.OutflowQuantity,
                    ResourceTypeId = entry.ResourceTypeId,
                    ProductTypeId = entry.ProductTypeId,
                });
            }
        }

        foreach (var sale in salesRecords)
        {
            var itemName = sale.ProductType?.Name ?? sale.ResourceType?.Name ?? "item";
            events.Add(new BuildingRecentActivityEvent
            {
                Tick = sale.Tick,
                BuildingUnitId = sale.BuildingUnitId,
                EventType = "SOLD",
                Description = $"Sold {FormatQuantity(sale.QuantitySold)} {itemName} for ${sale.Revenue:0.00}",
                Quantity = sale.QuantitySold,
                Amount = sale.Revenue,
                ProductTypeId = sale.ProductTypeId,
                ResourceTypeId = sale.ResourceTypeId,
            });
        }

        return events
            .OrderByDescending(e => e.Tick)
            .Take(safeLimit)
            .ToList();
    }

    private static readonly HashSet<string> BuildingFinancialRevenueCategories =
    [
        LedgerCategory.Revenue,
        LedgerCategory.MediaHouseIncome,
        LedgerCategory.RentIncome,
    ];

    private static readonly HashSet<string> BuildingFinancialCostCategories =
    [
        LedgerCategory.PurchasingCost,
        LedgerCategory.LaborCost,
        LedgerCategory.EnergyCost,
        LedgerCategory.Marketing,
        LedgerCategory.DiscardedResources,
    ];

    /// <summary>
    /// Returns recent per-tick operational sales, costs, and profit for a building across all of its units.
    /// Uses building-scoped ledger entries and excludes one-time capital events such as property purchase,
    /// construction, and upgrades so the chart reflects operating performance.
    /// </summary>
    [Authorize]
    public async Task<BuildingFinancialTimeline> GetBuildingFinancialTimeline(
        Guid buildingId,
        int? limit,
        [Service] AppDbContext db,
        [Service] IHttpContextAccessor httpContextAccessor)
    {
        var userId = httpContextAccessor.HttpContext!.User.GetRequiredUserId();

        var building = await db.Buildings
            .AsNoTracking()
            .Include(candidate => candidate.Company)
            .FirstOrDefaultAsync(candidate => candidate.Id == buildingId);

        if (building is null || building.Company.PlayerId != userId)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Building not found or you don't own it.")
                    .SetCode("BUILDING_NOT_FOUND")
                    .Build());
        }

        var safeLimit = Math.Clamp(limit ?? 100, 1, 100);
        var currentTick = await db.GameStates
            .AsNoTracking()
            .Select(state => (long?)state.CurrentTick)
            .FirstOrDefaultAsync() ?? 0L;
        var windowStart = Math.Max(0L, currentTick - (safeLimit - 1L));

        var entries = await db.LedgerEntries
            .AsNoTracking()
            .Where(entry => entry.BuildingId == buildingId
                && entry.RecordedAtTick >= windowStart
                && entry.RecordedAtTick <= currentTick
                && (BuildingFinancialRevenueCategories.Contains(entry.Category)
                    || BuildingFinancialCostCategories.Contains(entry.Category)))
            .Select(entry => new
            {
                entry.RecordedAtTick,
                entry.Category,
                entry.Amount,
            })
            .OrderBy(entry => entry.RecordedAtTick)
            .ToListAsync();

        var entriesByTick = entries
            .GroupBy(entry => entry.RecordedAtTick)
            .ToDictionary(group => group.Key, group => group.ToList());

        var snapshots = new List<BuildingFinancialTickSnapshot>();
        for (var tick = windowStart; tick <= currentTick; tick++)
        {
            var tickEntries = entriesByTick.GetValueOrDefault(tick) ?? [];
            var sales = tickEntries
                .Where(entry => BuildingFinancialRevenueCategories.Contains(entry.Category))
                .Sum(entry => entry.Amount);
            var costs = tickEntries
                .Where(entry => BuildingFinancialCostCategories.Contains(entry.Category))
                .Sum(entry => Math.Abs(entry.Amount));

            snapshots.Add(new BuildingFinancialTickSnapshot
            {
                Tick = tick,
                Sales = sales,
                Costs = costs,
                Profit = sales - costs,
            });
        }

        return new BuildingFinancialTimeline
        {
            BuildingId = building.Id,
            BuildingName = building.Name,
            DataFromTick = windowStart,
            DataToTick = currentTick,
            TotalSales = snapshots.Sum(snapshot => snapshot.Sales),
            TotalCosts = snapshots.Sum(snapshot => snapshot.Costs),
            TotalProfit = snapshots.Sum(snapshot => snapshot.Profit),
            Timeline = snapshots,
        };
    }

    private static string FormatQuantity(decimal qty) =>
        qty == Math.Floor(qty) ? ((int)qty).ToString() : qty.ToString("0.####");

    private static decimal GetUnitInventoryCapacity(Data.Entities.BuildingUnit unit)
    {
        return unit.UnitType switch
        {
            UnitType.Mining or UnitType.Storage or UnitType.B2BSales
                or UnitType.Purchase or UnitType.Manufacturing
                or UnitType.Branding or UnitType.PublicSales
                => GameConstants.StorageCapacity(unit.Level),
            _ => 0m
        };
    }

    /// <summary>Returns a financial ledger summary for a company (requires auth — must own company).</summary>
    [Authorize]
    public async Task<CompanyLedgerSummary?> GetCompanyLedger(
        Guid companyId,
        int? gameYear,
        [Service] AppDbContext db,
        [Service] IHttpContextAccessor httpContextAccessor)
    {
        var userId = httpContextAccessor.HttpContext!.User.GetRequiredUserId();

        var company = await db.Companies
            .Include(c => c.Buildings)
            .FirstOrDefaultAsync(c => c.Id == companyId && c.PlayerId == userId);

        if (company is null) return null;

        var gameState = await db.GameStates
            .AsNoTracking()
            .FirstOrDefaultAsync();
        var currentTick = gameState?.CurrentTick ?? 0L;
        var currentGameYear = GameTime.GetGameYear(currentTick);
        var selectedGameYear = gameYear ?? currentGameYear;

        var allEntries = await db.LedgerEntries
            .Where(e => e.CompanyId == companyId)
            .ToListAsync();

        var entries = allEntries
            .Where(entry => IsTickInGameYear(entry.RecordedAtTick, selectedGameYear))
            .ToList();

        var totalRevenue = LedgerCalculator.GetTotalRevenue(entries);
        var totalPurchasingCosts = LedgerCalculator.GetTotalPurchasingCosts(entries);
        var totalLaborCosts = LedgerCalculator.GetTotalLaborCosts(entries);
        var totalEnergyCosts = LedgerCalculator.GetTotalEnergyCosts(entries);
        var totalMarketingCosts = LedgerCalculator.GetTotalMarketingCosts(entries);
        var totalTaxPaid = LedgerCalculator.GetTotalTaxPaid(entries);
        var totalOtherCosts = LedgerCalculator.GetTotalOtherCosts(entries);
        var totalPropertyPurchases = LedgerCalculator.GetTotalPropertyPurchases(entries);
        var taxableIncome = LedgerCalculator.ComputeTaxableIncome(entries);
        var estimatedIncomeTax = selectedGameYear == currentGameYear
            ? GameTime.ComputeEstimatedIncomeTax(taxableIncome, gameState?.TaxRate ?? 0m)
            : totalTaxPaid;

        var ownedLots = await db.BuildingLots
            .Where(lot => lot.OwnerCompanyId == companyId)
            .ToListAsync();

        var propertyValue = ownedLots.Sum(WealthCalculator.GetLandValue);
        var buildingValue = company.Buildings.Sum(b => WealthCalculator.GetBuildingValue(b));

        var buildingIds = company.Buildings.Select(b => b.Id).ToList();
        var inventories = await db.Inventories
            .Where(i => buildingIds.Contains(i.BuildingId))
            .Include(i => i.ResourceType)
            .Include(i => i.ProductType)
            .ToListAsync();
        var inventoryValue = inventories.Sum(i => i.Quantity * WealthCalculator.GetItemBasePrice(i));

        var buildingSummaries = entries
            .Where(e => e.BuildingId.HasValue)
            .GroupBy(e => e.BuildingId!.Value)
            .Select(g =>
            {
                var b = company.Buildings.FirstOrDefault(bld => bld.Id == g.Key);
                return new BuildingLedgerSummary
                {
                    BuildingId = g.Key,
                    BuildingName = b?.Name ?? string.Empty,
                    BuildingType = b?.Type ?? string.Empty,
                    Revenue = g.Where(e => e.Amount > 0).Sum(e => e.Amount),
                    Costs = Math.Abs(g.Where(e => e.Amount < 0).Sum(e => e.Amount)),
                };
            })
            .ToList();

        var history = allEntries
            .GroupBy(entry => GameTime.GetGameYear(entry.RecordedAtTick))
            .Select(group => BuildCompanyLedgerHistoryYear(group.Key, currentGameYear, gameState?.TaxRate ?? 0m, group.ToList()))
            .ToDictionary(item => item.GameYear);

        if (!history.ContainsKey(currentGameYear))
        {
            history[currentGameYear] = BuildCompanyLedgerHistoryYear(currentGameYear, currentGameYear, gameState?.TaxRate ?? 0m, []);
        }

        var incomeTaxDueAtTick = GameTime.GetIncomeTaxDueTickForGameYear(selectedGameYear);

        return new CompanyLedgerSummary
        {
            CompanyId = company.Id,
            CompanyName = company.Name,
            GameYear = selectedGameYear,
            IsCurrentGameYear = selectedGameYear == currentGameYear,
            CurrentCash = company.Cash,
            TotalRevenue = totalRevenue,
            TotalPurchasingCosts = totalPurchasingCosts,
            TotalLaborCosts = totalLaborCosts,
            TotalEnergyCosts = totalEnergyCosts,
            TotalMarketingCosts = totalMarketingCosts,
            TotalTaxPaid = totalTaxPaid,
            TotalOtherCosts = totalOtherCosts,
            TaxableIncome = taxableIncome,
            EstimatedIncomeTax = estimatedIncomeTax,
            TotalPropertyPurchases = totalPropertyPurchases,
            NetIncome = totalRevenue - totalPurchasingCosts - totalLaborCosts - totalEnergyCosts - totalMarketingCosts - totalTaxPaid - totalOtherCosts,
            PropertyValue = propertyValue,
            PropertyAppreciation = propertyValue - totalPropertyPurchases,
            BuildingValue = buildingValue,
            InventoryValue = inventoryValue,
            TotalAssets = company.Cash + propertyValue + buildingValue + inventoryValue,
            CashFromOperations = totalRevenue - totalPurchasingCosts - totalLaborCosts - totalEnergyCosts - totalMarketingCosts,
            CashFromInvestments = -totalPropertyPurchases,
            FirstRecordedTick = entries.Count > 0 ? entries.Min(e => e.RecordedAtTick) : 0,
            LastRecordedTick = entries.Count > 0 ? entries.Max(e => e.RecordedAtTick) : 0,
            BuildingSummaries = buildingSummaries,
            IncomeTaxDueAtTick = incomeTaxDueAtTick,
            IncomeTaxDueGameTimeUtc = GameTime.GetInGameTimeUtc(incomeTaxDueAtTick),
            IncomeTaxDueGameYear = GameTime.GetGameYear(incomeTaxDueAtTick),
            IsIncomeTaxSettled = selectedGameYear < currentGameYear,
            History = history.Values
                .OrderByDescending(item => item.GameYear)
                .ToList(),
        };
    }

    /// <summary>Returns drill-down entries for a specific ledger category.</summary>
    [Authorize]
    public async Task<List<LedgerEntryResult>> GetLedgerDrillDown(
        Guid companyId,
        string category,
        int? gameYear,
        [Service] AppDbContext db,
        [Service] IHttpContextAccessor httpContextAccessor)
    {
        var userId = httpContextAccessor.HttpContext!.User.GetRequiredUserId();

        var ownsCompany = await db.Companies
            .AnyAsync(c => c.Id == companyId && c.PlayerId == userId);

        if (!ownsCompany) return [];

        var entriesQuery = db.LedgerEntries
            .Where(e => e.CompanyId == companyId && e.Category == category);

        if (gameYear.HasValue)
        {
            var startTick = GameTime.GetStartTickForGameYear(gameYear.Value);
            var endTick = GameTime.GetEndTickForGameYear(gameYear.Value);
            entriesQuery = entriesQuery.Where(e => e.RecordedAtTick >= startTick && e.RecordedAtTick <= endTick);
        }

        var entries = await entriesQuery
            .OrderByDescending(e => e.RecordedAtTick)
            .Take(200)
            .Include(e => e.Building)
            .Include(e => e.ProductType)
            .Include(e => e.ResourceType)
            .ToListAsync();

        return entries.Select(e => new LedgerEntryResult
        {
            Id = e.Id,
            Category = e.Category,
            Description = e.Description,
            Amount = e.Amount,
            RecordedAtTick = e.RecordedAtTick,
            RecordedAtUtc = e.RecordedAtUtc,
            BuildingId = e.BuildingId,
            BuildingName = e.Building?.Name,
            BuildingUnitId = e.BuildingUnitId,
            ProductTypeId = e.ProductTypeId,
            ProductName = e.ProductType?.Name,
            ResourceTypeId = e.ResourceTypeId,
            ResourceName = e.ResourceType?.Name,
        }).ToList();
    }

    private static bool IsTickInGameYear(long tick, int gameYear)
    {
        var startTick = GameTime.GetStartTickForGameYear(gameYear);
        var endTick = GameTime.GetEndTickForGameYear(gameYear);
        return tick >= startTick && tick <= endTick;
    }

    private static CompanyLedgerHistoryYear BuildCompanyLedgerHistoryYear(
        int gameYear,
        int currentGameYear,
        decimal taxRate,
        List<LedgerEntry> entries)
    {
        var totalRevenue = LedgerCalculator.GetTotalRevenue(entries);
        var totalPurchasingCosts = LedgerCalculator.GetTotalPurchasingCosts(entries);
        var totalLaborCosts = LedgerCalculator.GetTotalLaborCosts(entries);
        var totalEnergyCosts = LedgerCalculator.GetTotalEnergyCosts(entries);
        var totalMarketingCosts = LedgerCalculator.GetTotalMarketingCosts(entries);
        var totalTaxPaid = LedgerCalculator.GetTotalTaxPaid(entries);
        var totalOtherCosts = LedgerCalculator.GetTotalOtherCosts(entries);
        var taxableIncome = LedgerCalculator.ComputeTaxableIncome(entries);
        var estimatedIncomeTax = gameYear == currentGameYear
            ? GameTime.ComputeEstimatedIncomeTax(taxableIncome, taxRate)
            : totalTaxPaid;

        return new CompanyLedgerHistoryYear
        {
            GameYear = gameYear,
            IsCurrentGameYear = gameYear == currentGameYear,
            TotalRevenue = totalRevenue,
            TotalLaborCosts = totalLaborCosts,
            TotalEnergyCosts = totalEnergyCosts,
            NetIncome = totalRevenue - totalPurchasingCosts - totalLaborCosts - totalEnergyCosts - totalMarketingCosts - totalTaxPaid - totalOtherCosts,
            TotalTaxPaid = totalTaxPaid,
            TaxableIncome = taxableIncome,
            EstimatedIncomeTax = estimatedIncomeTax,
            FirstRecordedTick = entries.Count > 0 ? entries.Min(entry => entry.RecordedAtTick) : 0,
            LastRecordedTick = entries.Count > 0 ? entries.Max(entry => entry.RecordedAtTick) : 0,
        };
    }

    private static decimal ComputeCompanyAssetValue(
        Company company,
        IReadOnlyCollection<BuildingLot> allOwnedLots,
        IReadOnlyCollection<Inventory> allInventories)
    {
        var buildingIds = company.Buildings.Select(building => building.Id).ToHashSet();
        var inventoryValue = allInventories
            .Where(inventory => buildingIds.Contains(inventory.BuildingId))
            .Sum(WealthCalculator.GetItemBasePrice);
        var lotValue = allOwnedLots
            .Where(lot => lot.OwnerCompanyId == company.Id)
            .Sum(WealthCalculator.GetLandValue);

        return company.Cash + company.Buildings.Sum(WealthCalculator.GetBuildingValue) + lotValue + allInventories
            .Where(inventory => buildingIds.Contains(inventory.BuildingId))
            .Sum(inventory => inventory.Quantity * WealthCalculator.GetItemBasePrice(inventory));
    }

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
        var demandDrivers = ComputeDemandDrivers(
            unit, productTypeForAnalytics, inventoryQuality, brandAwareness, populationIndex,
            marketShare, unmetDemandShare, city?.BaseSalaryPerManhour,
            recentCitySalary, city?.Population ?? 0);

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
        };
    }

    /// <summary>
    /// Returns product-level analytics for a MANUFACTURING unit.
    /// Shows labor/energy cost history, production quantity, and estimated economics
    /// (basePrice × produced) so players can evaluate manufacturing profitability.
    /// </summary>
    [Authorize]
    public async Task<UnitProductAnalytics?> GetUnitProductAnalytics(
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

        // Currently only supported for MANUFACTURING units
        if (unit.UnitType != UnitType.Manufacturing) return null;

        var productTypeId = unit.ProductTypeId;
        string? productName = null;
        decimal? basePrice = null;

        if (productTypeId.HasValue)
        {
            var product = await db.ProductTypes.FindAsync(productTypeId.Value);
            if (product is not null)
            {
                productName = product.Name;
                basePrice = product.BasePrice;
            }
        }

        // Load cost ledger entries for this unit (last 100 ticks, ordered descending then ascending)
        var ledgerEntries = await db.LedgerEntries
            .Where(e => e.BuildingUnitId == unitId
                && (e.Category == LedgerCategory.LaborCost || e.Category == LedgerCategory.EnergyCost))
            .OrderByDescending(e => e.RecordedAtTick)
            .Take(100)
            .ToListAsync();

        // Load resource history for production quantities for this unit
        var resourceHistory = await db.BuildingUnitResourceHistories
            .Where(h => h.BuildingUnitId == unitId && h.ProducedQuantity > 0)
            .OrderByDescending(h => h.Tick)
            .Take(100)
            .ToListAsync();

        // Merge all ticks from both sources
        var allTicks = ledgerEntries.Select(e => e.RecordedAtTick)
            .Union(resourceHistory.Select(h => h.Tick))
            .Distinct()
            .OrderBy(t => t)
            .ToList();

        var snapshots = allTicks.Select(tick =>
        {
            var labor = ledgerEntries
                .Where(e => e.RecordedAtTick == tick && e.Category == LedgerCategory.LaborCost)
                .Sum(e => -e.Amount);
            var energy = ledgerEntries
                .Where(e => e.RecordedAtTick == tick && e.Category == LedgerCategory.EnergyCost)
                .Sum(e => -e.Amount);
            var produced = resourceHistory
                .Where(h => h.Tick == tick)
                .Sum(h => h.ProducedQuantity);
            var totalCost = labor + energy;
            decimal? estRevenue = basePrice.HasValue ? Math.Round(produced * basePrice.Value, 2) : null;
            decimal? estProfit = estRevenue.HasValue ? Math.Round(estRevenue.Value - totalCost, 2) : null;

            return new UnitProductTickSnapshot
            {
                Tick = tick,
                LaborCost = Math.Round(labor, 2),
                EnergyCost = Math.Round(energy, 2),
                TotalCost = Math.Round(totalCost, 2),
                QuantityProduced = produced,
                EstimatedRevenue = estRevenue,
                EstimatedProfit = estProfit,
            };
        }).ToList();

        var totalCostSum = Math.Round(snapshots.Sum(s => s.TotalCost), 2);
        var totalProduced = snapshots.Sum(s => s.QuantityProduced);
        decimal? estRevSum = basePrice.HasValue ? Math.Round(totalProduced * basePrice.Value, 2) : null;
        decimal? estProfitSum = estRevSum.HasValue ? Math.Round(estRevSum.Value - totalCostSum, 2) : null;

        return new UnitProductAnalytics
        {
            BuildingUnitId = unit.Id,
            UnitType = unit.UnitType,
            ProductTypeId = productTypeId,
            ProductName = productName,
            DataFromTick = allTicks.Count > 0 ? allTicks.First() : 0,
            DataToTick = allTicks.Count > 0 ? allTicks.Last() : 0,
            TotalCost = totalCostSum,
            TotalQuantityProduced = totalProduced,
            EstimatedRevenue = estRevSum,
            EstimatedProfit = estProfitSum,
            Snapshots = snapshots,
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
        long cityPopulation = 0)
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

    /// <summary>
    /// Returns the city-level power balance: total supply from all power plants,
    /// total demand from all consuming buildings, the reserve margin, and a
    /// human-readable status string (BALANCED, CONSTRAINED, or CRITICAL).
    ///
    /// This query is public (no auth required) so players can assess a city
    /// before purchasing a lot.
    /// </summary>
    public async Task<CityPowerBalance> GetCityPowerBalance(Guid cityId, [Service] AppDbContext db)
    {
        var buildings = await db.Buildings
            .Where(b => b.CityId == cityId)
            .ToListAsync();

        var powerPlants = buildings.Where(b => b.Type == Data.Entities.BuildingType.PowerPlant).ToList();
        var consumers = buildings.Where(b => b.Type != Data.Entities.BuildingType.PowerPlant).ToList();

        var totalSupplyMw = powerPlants.Sum(plant =>
            plant.PowerOutput > 0m
                ? plant.PowerOutput!.Value
                : GameConstants.DefaultPowerOutputMw(plant.PowerPlantType));

        var totalDemandMw = consumers.Sum(building =>
            building.PowerConsumption > 0m
                ? building.PowerConsumption
                : GameConstants.PowerDemandMw(building.Type, building.Level));

        var reserveMw = totalSupplyMw - totalDemandMw;
        var reservePercent = totalDemandMw > 0m
            ? decimal.Round(reserveMw / totalDemandMw * 100m, 1, MidpointRounding.AwayFromZero)
            : 100m;

        string status;
        if (totalDemandMw == 0m || reserveMw >= 0m)
            status = "BALANCED";
        else if (totalSupplyMw >= totalDemandMw * 0.5m)
            status = "CONSTRAINED";
        else
            status = "CRITICAL";

        var powerPlantSummaries = powerPlants.Select(p => new PowerPlantSummary
        {
            BuildingId = p.Id,
            BuildingName = p.Name,
            PlantType = p.PowerPlantType ?? Data.Entities.PowerPlantType.Coal,
            OutputMw = p.PowerOutput > 0m
                ? p.PowerOutput!.Value
                : GameConstants.DefaultPowerOutputMw(p.PowerPlantType),
            PowerStatus = p.PowerStatus,
        }).ToList();

        return new CityPowerBalance
        {
            CityId = cityId,
            TotalSupplyMw = totalSupplyMw,
            TotalDemandMw = totalDemandMw,
            ReserveMw = reserveMw,
            ReservePercent = reservePercent,
            Status = status,
            PowerPlants = powerPlantSummaries,
            PowerPlantCount = powerPlants.Count,
            ConsumerBuildingCount = consumers.Count,
        };
    }

    /// <summary>
    /// Returns the authenticated player's first-sale mission status.
    /// The mission tracks the player's onboarding sales shop from initial configuration
    /// through to the moment a real public-sales record is created in the simulation.
    ///
    /// Phase values:
    ///   NO_SHOP          — onboarding is not yet complete (no shop building tracked).
    ///   CONFIGURE_SHOP   — shop exists but has readiness blockers preventing the first sale.
    ///   AWAITING_FIRST_SALE — shop is fully configured; waiting for the simulation to record a sale.
    ///   FIRST_SALE_RECORDED — a real PublicSalesRecord exists for this shop; mission complete.
    ///   ALREADY_COMPLETED   — the first-sale milestone was previously acknowledged and persisted.
    ///
    /// The <see cref="FirstSaleMissionStatus.Blockers"/> list explains WHY the shop is not ready
    /// (only relevant when phase is CONFIGURE_SHOP).
    /// </summary>
    [Authorize]
    public async Task<FirstSaleMissionStatus> GetFirstSaleMission(
        [Service] AppDbContext db,
        [Service] IHttpContextAccessor httpContextAccessor)
    {
        var userId = httpContextAccessor.HttpContext!.User.GetRequiredUserId();

        var player = await db.Players
            .Include(p => p.Companies)
            .FirstOrDefaultAsync(p => p.Id == userId);

        if (player is null)
            return new FirstSaleMissionStatus { Phase = FirstSaleMissionPhase.NoShop };

        // Already acknowledged by the player
        if (player.OnboardingFirstSaleCompletedAtUtc is not null)
            return new FirstSaleMissionStatus { Phase = FirstSaleMissionPhase.AlreadyCompleted };

        // No shop being tracked
        if (player.OnboardingShopBuildingId is null)
            return new FirstSaleMissionStatus { Phase = FirstSaleMissionPhase.NoShop };

        var shopBuilding = await db.Buildings
            .Include(b => b.Units)
            .FirstOrDefaultAsync(b => b.Id == player.OnboardingShopBuildingId);

        if (shopBuilding is null)
            return new FirstSaleMissionStatus { Phase = FirstSaleMissionPhase.NoShop };

        // Check if a real sale has already happened for this shop
        var firstSaleRecord = await db.PublicSalesRecords
            .Include(r => r.ProductType)
            .Where(r => r.BuildingId == shopBuilding.Id && r.QuantitySold > 0m)
            .OrderBy(r => r.Tick)
            .FirstOrDefaultAsync();

        if (firstSaleRecord is not null)
        {
            return new FirstSaleMissionStatus
            {
                Phase = FirstSaleMissionPhase.FirstSaleRecorded,
                ShopBuildingId = shopBuilding.Id,
                ShopName = shopBuilding.Name,
                FirstSaleRevenue = firstSaleRecord.Revenue,
                FirstSaleProductName = firstSaleRecord.ProductType?.Name,
                FirstSaleTick = firstSaleRecord.Tick,
                FirstSaleQuantity = firstSaleRecord.QuantitySold,
                FirstSalePricePerUnit = firstSaleRecord.PricePerUnit,
            };
        }

        // Shop exists — compute blockers
        var blockers = new List<string>();

        if (shopBuilding.IsUnderConstruction)
        {
            blockers.Add(FirstSaleMissionBlocker.BuildingUnderConstruction);
        }

        var publicSalesUnit = shopBuilding.Units
            .FirstOrDefault(u => string.Equals(u.UnitType, UnitType.PublicSales, StringComparison.Ordinal));

        if (publicSalesUnit is null)
        {
            blockers.Add(FirstSaleMissionBlocker.PublicSalesUnitMissing);
        }
        else
        {
            if (publicSalesUnit.MinPrice is null or <= 0m)
                blockers.Add(FirstSaleMissionBlocker.PriceNotSet);

            // Check inventory in the shop's public-sales unit
            var hasInventory = await db.Inventories
                .AnyAsync(inv => inv.BuildingUnitId == publicSalesUnit.Id && inv.Quantity > 0m);

            if (!hasInventory)
                blockers.Add(FirstSaleMissionBlocker.NoInventory);
        }

        var phase = blockers.Count == 0
            ? FirstSaleMissionPhase.AwaitingFirstSale
            : FirstSaleMissionPhase.ConfigureShop;

        return new FirstSaleMissionStatus
        {
            Phase = phase,
            ShopBuildingId = shopBuilding.Id,
            ShopName = shopBuilding.Name,
            Blockers = blockers,
        };
    }

    /// <summary>
    /// Lists all MEDIA_HOUSE buildings in a city. Public — no auth required.
    /// Includes channel type (NEWSPAPER, RADIO, TV), owner company name, and effectiveness multiplier.
    /// Players use this to discover where to route their marketing budget.
    /// </summary>
    public async Task<List<CityMediaHouseInfo>> GetCityMediaHouses(
        Guid cityId,
        [Service] AppDbContext db)
    {
        var mediaHouses = await db.Buildings
            .Where(b => b.CityId == cityId && b.Type == Data.Entities.BuildingType.MediaHouse)
            .Include(b => b.Company)
            .AsNoTracking()
            .ToListAsync();

        return mediaHouses.Select(b => new CityMediaHouseInfo
        {
            Id = b.Id,
            Name = b.Name,
            CityId = b.CityId,
            MediaType = b.MediaType,
            OwnerCompanyId = b.CompanyId,
            OwnerCompanyName = b.Company.Name,
            EffectivenessMultiplier = Data.Entities.MediaType.EffectivenessMultiplier(b.MediaType),
            PowerStatus = b.PowerStatus,
            IsUnderConstruction = b.IsUnderConstruction,
        }).ToList();
    }

    /// <summary>
    /// Returns brand awareness and quality metrics for products of a company. Requires authentication.
    /// Consumers: building-detail marketing unit configuration, analytics dashboards.
    /// Delegates to companyBrands — use that query for brand awareness reads.
    /// </summary>
    [Authorize]
    public async Task<List<ResearchBrandState>> GetCompanyMarketingStats(
        Guid companyId,
        [Service] AppDbContext db,
        [Service] IHttpContextAccessor httpContextAccessor)
    {
        var userId = httpContextAccessor.HttpContext!.User.GetRequiredUserId();
        var company = await db.Companies.FirstOrDefaultAsync(c => c.Id == companyId && c.PlayerId == userId);
        if (company is null) return [];

        var brands = await db.Brands.Where(b => b.CompanyId == companyId).ToListAsync();
        var productTypeIds = brands.Where(b => b.ProductTypeId.HasValue).Select(b => b.ProductTypeId!.Value).Distinct().ToList();
        var productTypes = await db.ProductTypes.Where(pt => productTypeIds.Contains(pt.Id)).ToListAsync();

        return brands.Select(b =>
        {
            var pt = b.ProductTypeId.HasValue ? productTypes.FirstOrDefault(p => p.Id == b.ProductTypeId.Value) : null;
            return new ResearchBrandState
            {
                Id = b.Id,
                CompanyId = b.CompanyId,
                Name = b.Name,
                Scope = b.Scope,
                ProductTypeId = b.ProductTypeId,
                ProductName = pt?.Name,
                IndustryCategory = b.IndustryCategory,
                Awareness = b.Awareness,
                Quality = b.Quality,
                MarketingEfficiencyMultiplier = b.MarketingEfficiencyMultiplier,
            };
        }).ToList();
    }

    // ── Bank Lending Marketplace ──────────────────────────────────────────────────

    /// <summary>
    /// Returns all active loan offers visible to borrowers.
    /// Excludes offers from the current player's own companies (self-lending guard).
    /// </summary>
    public async Task<List<LoanOfferSummary>> GetLoanOffers(
        [Service] AppDbContext db,
        [Service] IHttpContextAccessor httpContextAccessor)
    {
        var userId = httpContextAccessor.HttpContext?.User.GetUserId();

        // Get IDs of companies owned by current player (to exclude own offers).
        ISet<Guid> ownCompanyIds = new HashSet<Guid>();
        if (userId.HasValue)
        {
            ownCompanyIds = (await db.Companies
                .Where(c => c.PlayerId == userId.Value)
                .Select(c => c.Id)
                .ToListAsync()).ToHashSet();
        }

        var offers = await db.LoanOffers
            .Include(o => o.LenderCompany)
            .Include(o => o.BankBuilding)
            .ThenInclude(b => b.City)
            .Where(o => o.IsActive && o.TotalCapacity > o.UsedCapacity)
            .AsNoTracking()
            .ToListAsync();

        return offers
            .Where(o => !ownCompanyIds.Contains(o.LenderCompanyId))
            .Select(o => new LoanOfferSummary
            {
                Id = o.Id,
                BankBuildingId = o.BankBuildingId,
                BankBuildingName = o.BankBuilding.Name,
                CityId = o.BankBuilding.CityId,
                CityName = o.BankBuilding.City.Name,
                LenderCompanyId = o.LenderCompanyId,
                LenderCompanyName = o.LenderCompany.Name,
                AnnualInterestRatePercent = o.AnnualInterestRatePercent,
                MaxPrincipalPerLoan = o.MaxPrincipalPerLoan,
                TotalCapacity = o.TotalCapacity,
                UsedCapacity = o.UsedCapacity,
                RemainingCapacity = o.TotalCapacity - o.UsedCapacity,
                DurationTicks = o.DurationTicks,
                IsActive = o.IsActive,
                CreatedAtTick = o.CreatedAtTick,
                CreatedAtUtc = o.CreatedAtUtc,
            }).ToList();
    }

    /// <summary>
    /// Returns all loan offers published by the authenticated player's companies.
    /// Includes inactive offers and utilisation metrics.
    /// </summary>
    [Authorize]
    public async Task<List<LoanOfferSummary>> GetMyLoanOffers(
        [Service] AppDbContext db,
        [Service] IHttpContextAccessor httpContextAccessor)
    {
        var userId = httpContextAccessor.HttpContext!.User.GetRequiredUserId();

        var companyIds = await db.Companies
            .Where(c => c.PlayerId == userId)
            .Select(c => c.Id)
            .ToListAsync();

        var offers = await db.LoanOffers
            .Include(o => o.LenderCompany)
            .Include(o => o.BankBuilding)
            .ThenInclude(b => b.City)
            .Where(o => companyIds.Contains(o.LenderCompanyId))
            .AsNoTracking()
            .ToListAsync();

        return offers.Select(o => new LoanOfferSummary
        {
            Id = o.Id,
            BankBuildingId = o.BankBuildingId,
            BankBuildingName = o.BankBuilding.Name,
            CityId = o.BankBuilding.CityId,
            CityName = o.BankBuilding.City.Name,
            LenderCompanyId = o.LenderCompanyId,
            LenderCompanyName = o.LenderCompany.Name,
            AnnualInterestRatePercent = o.AnnualInterestRatePercent,
            MaxPrincipalPerLoan = o.MaxPrincipalPerLoan,
            TotalCapacity = o.TotalCapacity,
            UsedCapacity = o.UsedCapacity,
            RemainingCapacity = o.TotalCapacity - o.UsedCapacity,
            DurationTicks = o.DurationTicks,
            IsActive = o.IsActive,
            CreatedAtTick = o.CreatedAtTick,
            CreatedAtUtc = o.CreatedAtUtc,
        }).ToList();
    }

    /// <summary>
    /// Returns all active and historical loans for the authenticated player's companies (borrower view).
    /// </summary>
    [Authorize]
    public async Task<List<LoanSummary>> GetMyLoans(
        [Service] AppDbContext db,
        [Service] IHttpContextAccessor httpContextAccessor)
    {
        var userId = httpContextAccessor.HttpContext!.User.GetRequiredUserId();

        var companyIds = await db.Companies
            .Where(c => c.PlayerId == userId)
            .Select(c => c.Id)
            .ToListAsync();

        var loans = await db.Loans
            .Include(l => l.LenderCompany)
            .Include(l => l.BankBuilding)
            .Include(l => l.BorrowerCompany)
            .Where(l => companyIds.Contains(l.BorrowerCompanyId))
            .AsNoTracking()
            .ToListAsync();

        return loans.Select(MapToLoanSummary).ToList();
    }

    /// <summary>
    /// Returns all loans issued by a specific bank building (lender view).
    /// Only the bank's owning player can access this.
    /// </summary>
    [Authorize]
    public async Task<List<LoanSummary>> GetBankLoans(
        Guid bankBuildingId,
        [Service] AppDbContext db,
        [Service] IHttpContextAccessor httpContextAccessor)
    {
        var userId = httpContextAccessor.HttpContext!.User.GetRequiredUserId();

        var building = await db.Buildings
            .Include(b => b.Company)
            .FirstOrDefaultAsync(b => b.Id == bankBuildingId && b.Type == Data.Entities.BuildingType.Bank);

        if (building is null || building.Company.PlayerId != userId)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Bank building not found or you do not own it.")
                    .SetCode("BANK_NOT_FOUND")
                    .Build());
        }

        var loans = await db.Loans
            .Include(l => l.BorrowerCompany)
            .Include(l => l.LenderCompany)
            .Include(l => l.BankBuilding)
            .Where(l => l.BankBuildingId == bankBuildingId)
            .AsNoTracking()
            .ToListAsync();

        return loans.Select(MapToLoanSummary).ToList();
    }

    private static LoanSummary MapToLoanSummary(Loan l) => new()
    {
        Id = l.Id,
        LoanOfferId = l.LoanOfferId,
        BorrowerCompanyId = l.BorrowerCompanyId,
        BorrowerCompanyName = l.BorrowerCompany.Name,
        LenderCompanyId = l.LenderCompanyId,
        LenderCompanyName = l.LenderCompany.Name,
        BankBuildingId = l.BankBuildingId,
        BankBuildingName = l.BankBuilding.Name,
        OriginalPrincipal = l.OriginalPrincipal,
        RemainingPrincipal = l.RemainingPrincipal,
        AnnualInterestRatePercent = l.AnnualInterestRatePercent,
        DurationTicks = l.DurationTicks,
        StartTick = l.StartTick,
        DueTick = l.DueTick,
        NextPaymentTick = l.NextPaymentTick,
        PaymentAmount = l.PaymentAmount,
        PaymentsMade = l.PaymentsMade,
        TotalPayments = l.TotalPayments,
        Status = l.Status,
        MissedPayments = l.MissedPayments,
        AccumulatedPenalty = l.AccumulatedPenalty,
        AcceptedAtUtc = l.AcceptedAtUtc,
        ClosedAtUtc = l.ClosedAtUtc,
    };

    /// <summary>
    /// Evaluates what a PURCHASE unit would do on the next tick given its current
    /// configuration and live market state, without mutating any data.
    /// Returns expected source, delivered price, quality, and any block reasons.
    /// Requires authentication (the player must own the building).
    /// </summary>
    [Authorize]
    public async Task<ProcurementPreview?> GetProcurementPreview(
        Guid buildingUnitId,
        [Service] AppDbContext db,
        [Service] IHttpContextAccessor httpContextAccessor)
    {
        var userId = httpContextAccessor.HttpContext!.User.GetRequiredUserId();

        var unit = await db.BuildingUnits
            .Include(u => u.Building)
            .ThenInclude(b => b.Company)
            .FirstOrDefaultAsync(u => u.Id == buildingUnitId);

        if (unit is null || unit.Building.Company.PlayerId != userId)
            return null;

        if (unit.UnitType != Data.Entities.UnitType.Purchase)
            return null;

        var company = unit.Building.Company;
        return await ProcurementPreviewService.ComputeAsync(db, unit, company);
    }

    /// <summary>
    /// Returns the full ranked list of sourcing candidates for a PURCHASE unit,
    /// including global exchange offers from every city, player-placed exchange orders,
    /// and local B2B suppliers. Each candidate includes the full landed-cost breakdown
    /// (exchange price, transit cost, delivered price) and eligibility state so the
    /// player can compare options and understand why a cheaper-looking route may be
    /// blocked by a quality or price filter.
    ///
    /// Requires authentication. The authenticated player must own the building that
    /// contains the unit.
    /// </summary>
    [Authorize]
    public async Task<List<SourcingCandidate>> GetSourcingCandidates(
        Guid buildingUnitId,
        [Service] AppDbContext db,
        [Service] IHttpContextAccessor httpContextAccessor)
    {
        var userId = httpContextAccessor.HttpContext!.User.GetRequiredUserId();

        var unit = await db.BuildingUnits
            .Include(u => u.Building)
            .ThenInclude(b => b.Company)
            .FirstOrDefaultAsync(u => u.Id == buildingUnitId);

        if (unit is null || unit.Building.Company.PlayerId != userId)
            return [];

        if (unit.UnitType != Data.Entities.UnitType.Purchase)
            return [];

        return await SourcingComparisonService.GetCandidatesAsync(db, unit, unit.Building.Company);
    }

    /// <summary>
    /// Returns upgrade information for a building unit: cost, duration, and stat projections.
    /// Returns null if the unit is not found or the caller does not own it.
    /// </summary>
    [Authorize]
    public async Task<UnitUpgradeInfo?> GetUnitUpgradeInfo(
        Guid unitId,
        [Service] AppDbContext db,
        [Service] IHttpContextAccessor httpContextAccessor)
    {
        var userId = httpContextAccessor.HttpContext!.User.GetRequiredUserId();

        var unit = await db.BuildingUnits
            .Include(u => u.Building)
            .ThenInclude(b => b.Company)
            .FirstOrDefaultAsync(u => u.Id == unitId);

        if (unit is null || unit.Building.Company.PlayerId != userId)
            return null;

        var isUpgradable = Engine.GameConstants.IsUpgradableUnitType(unit.UnitType);
        var isMaxLevel = unit.Level >= Engine.GameConstants.MaxUnitLevel;
        var currentStat = Engine.GameConstants.GetUnitStat(unit.UnitType, unit.Level);

        return new UnitUpgradeInfo
        {
            UnitId = unit.Id,
            UnitType = unit.UnitType,
            CurrentLevel = unit.Level,
            NextLevel = isMaxLevel ? unit.Level : unit.Level + 1,
            IsMaxLevel = isMaxLevel,
            IsUpgradable = isUpgradable,
            UpgradeCost = isUpgradable && !isMaxLevel
                ? Engine.GameConstants.UnitUpgradeCost(unit.UnitType, unit.Level)
                : 0m,
            UpgradeTicks = isUpgradable && !isMaxLevel
                ? Engine.GameConstants.UnitUpgradeTicks(unit.Level)
                : 0,
            CurrentStat = currentStat,
            NextStat = isMaxLevel
                ? currentStat  // same as CurrentStat at max level
                : Engine.GameConstants.GetUnitStat(unit.UnitType, unit.Level + 1),
            StatLabel = Engine.GameConstants.GetUnitStatLabel(unit.UnitType),
        };
    }
}

/// <summary>Payload for player ranking.</summary>
public sealed class PlayerRanking
{
    /// <summary>Player identifier.</summary>
    public Guid PlayerId { get; set; }

    /// <summary>Player display name.</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Total wealth = CashTotal + BuildingValue + InventoryValue.
    /// See <see cref="Query.GetRankings"/> for the full valuation formula.
    /// </summary>
    public decimal TotalWealth { get; set; }

    /// <summary>Sum of all company cash balances (liquid funds).</summary>
    public decimal CashTotal { get; set; }

    /// <summary>
    /// Estimated value of all owned buildings.
    /// Calculated as BuildingBaseValue[type] × level for each building.
    /// </summary>
    public decimal BuildingValue { get; set; }

    /// <summary>
    /// Estimated value of all inventory held in company buildings.
    /// Calculated as quantity × basePrice for each resource or product in stock.
    /// </summary>
    public decimal InventoryValue { get; set; }

    /// <summary>Number of companies owned.</summary>
    public int CompanyCount { get; set; }
}

/// <summary>Individual company ranking for the leaderboard.</summary>
public sealed class CompanyRanking
{
    /// <summary>Company identifier.</summary>
    public Guid CompanyId { get; set; }

    /// <summary>Company display name.</summary>
    public string CompanyName { get; set; } = string.Empty;

    /// <summary>Owner player identifier.</summary>
    public Guid PlayerId { get; set; }

    /// <summary>Owner player display name.</summary>
    public string OwnerDisplayName { get; set; } = string.Empty;

    /// <summary>Total company wealth = Cash + BuildingValue + InventoryValue.</summary>
    public decimal TotalWealth { get; set; }

    /// <summary>Cash on hand for this company.</summary>
    public decimal Cash { get; set; }

    /// <summary>Estimated value of company buildings.</summary>
    public decimal BuildingValue { get; set; }

    /// <summary>Estimated value of inventory in company buildings.</summary>
    public decimal InventoryValue { get; set; }

    /// <summary>Number of buildings owned by this company.</summary>
    public int BuildingCount { get; set; }
}

/// <summary>Payload for starter industries.</summary>
public sealed class StarterIndustriesPayload
{
    /// <summary>Available starter industry values.</summary>
    public List<string> Industries { get; set; } = [];
}

/// <summary>Type values for scheduled actions visible to the player.</summary>
public static class ScheduledActionType
{
    /// <summary>A queued building configuration upgrade (layout/unit change).</summary>
    public const string BuildingUpgrade = "BUILDING_UPGRADE";
}

/// <summary>Summary of a single pending scheduled action for the player.</summary>
public sealed class ScheduledActionSummary
{
    /// <summary>Unique identifier (matches the underlying plan or entity).</summary>
    public Guid Id { get; set; }

    /// <summary>Category of the scheduled action. See <see cref="ScheduledActionType"/>.</summary>
    public string ActionType { get; set; } = string.Empty;

    /// <summary>Building the action belongs to.</summary>
    public Guid BuildingId { get; set; }

    /// <summary>Human-readable building name for display in the UI.</summary>
    public string BuildingName { get; set; } = string.Empty;

    /// <summary>Building type string (e.g. FACTORY, SALES_SHOP).</summary>
    public string BuildingType { get; set; } = string.Empty;

    /// <summary>UTC timestamp when the action was submitted.</summary>
    public DateTime SubmittedAtUtc { get; set; }

    /// <summary>Game tick when the action was submitted.</summary>
    public long SubmittedAtTick { get; set; }

    /// <summary>Game tick when the action is scheduled to apply.</summary>
    public long AppliesAtTick { get; set; }

    /// <summary>Number of ticks remaining until the action applies.</summary>
    public long TicksRemaining { get; set; }

    /// <summary>Total ticks this action required from submission to application.</summary>
    public int TotalTicksRequired { get; set; }
}

/// <summary>Projected supply offer at a city's global exchange.</summary>
public sealed class GlobalExchangeOffer
{
    public Guid CityId { get; set; }
    public string CityName { get; set; } = string.Empty;
    public Guid ResourceTypeId { get; set; }
    public string ResourceName { get; set; } = string.Empty;
    public string ResourceSlug { get; set; } = string.Empty;
    public string UnitSymbol { get; set; } = string.Empty;
    public decimal LocalAbundance { get; set; }
    public decimal ExchangePricePerUnit { get; set; }
    public decimal EstimatedQuality { get; set; }
    public decimal TransitCostPerUnit { get; set; }
    public decimal DeliveredPricePerUnit { get; set; }
    public decimal DistanceKm { get; set; }
}

/// <summary>
/// A product marketplace listing from a player-placed SELL exchange order.
/// Represents a specific offer to sell a manufactured or intermediate product.
/// </summary>
public sealed class GlobalExchangeProductListing
{
    /// <summary>The exchange order ID backing this listing.</summary>
    public Guid OrderId { get; set; }

    /// <summary>The product type being offered.</summary>
    public Guid ProductTypeId { get; set; }

    /// <summary>Human-readable product name.</summary>
    public string ProductName { get; set; } = string.Empty;

    /// <summary>URL-friendly product identifier.</summary>
    public string ProductSlug { get; set; } = string.Empty;

    /// <summary>Industry category: FURNITURE, FOOD_PROCESSING, HEALTHCARE, etc.</summary>
    public string ProductIndustry { get; set; } = string.Empty;

    /// <summary>Short display symbol for the produced unit (e.g. pcs).</summary>
    public string UnitSymbol { get; set; } = string.Empty;

    /// <summary>Display name for the produced unit (e.g. Piece, Crate).</summary>
    public string UnitName { get; set; } = string.Empty;

    /// <summary>Base market price per unit from the product catalogue.</summary>
    public decimal BasePrice { get; set; }

    /// <summary>Asking price per unit for this specific listing.</summary>
    public decimal PricePerUnit { get; set; }

    /// <summary>Remaining quantity available in this order.</summary>
    public decimal RemainingQuantity { get; set; }

    /// <summary>City where the selling exchange building is located.</summary>
    public Guid SellerCityId { get; set; }

    /// <summary>Name of the seller's city.</summary>
    public string SellerCityName { get; set; } = string.Empty;

    /// <summary>Company that placed this sell order.</summary>
    public Guid SellerCompanyId { get; set; }

    /// <summary>Name of the selling company.</summary>
    public string SellerCompanyName { get; set; } = string.Empty;

    /// <summary>When this order was created.</summary>
    public DateTime CreatedAtUtc { get; set; }
}

/// <summary>Inventory fill information for a single building unit.</summary>
public sealed class BuildingUnitInventorySummary
{
    public Guid BuildingUnitId { get; set; }
    public decimal Quantity { get; set; }
    public decimal Capacity { get; set; }
    public decimal FillPercent { get; set; }
    public decimal? AverageQuality { get; set; }
    public decimal TotalSourcingCost { get; set; }
    public decimal SourcingCostPerUnit { get; set; }
}

/// <summary>
/// City-level power balance snapshot.
/// Computed on demand from current building data.
/// </summary>
public sealed class CityPowerBalance
{
    /// <summary>The city this balance applies to.</summary>
    public Guid CityId { get; set; }

    /// <summary>Total power output in MW from all power plants in the city.</summary>
    public decimal TotalSupplyMw { get; set; }

    /// <summary>Total power demand in MW from all consuming buildings in the city.</summary>
    public decimal TotalDemandMw { get; set; }

    /// <summary>Reserve capacity in MW (supply minus demand; negative means shortage).</summary>
    public decimal ReserveMw { get; set; }

    /// <summary>Reserve as a percentage of demand (negative means shortage).</summary>
    public decimal ReservePercent { get; set; }

    /// <summary>
    /// Overall power status for the city:
    /// BALANCED = supply &gt;= demand,
    /// CONSTRAINED = supply &lt; demand but &gt;= 50%,
    /// CRITICAL = supply &lt; 50% of demand.
    /// </summary>
    public string Status { get; set; } = "BALANCED";

    /// <summary>Summary of each power plant in the city.</summary>
    public List<PowerPlantSummary> PowerPlants { get; set; } = [];

    /// <summary>Number of power plants in the city.</summary>
    public int PowerPlantCount { get; set; }

    /// <summary>Number of consuming buildings in the city.</summary>
    public int ConsumerBuildingCount { get; set; }
}

/// <summary>Summary of a single power plant building for the city power balance view.</summary>
public sealed class PowerPlantSummary
{
    /// <summary>Building identifier.</summary>
    public Guid BuildingId { get; set; }

    /// <summary>Building display name.</summary>
    public string BuildingName { get; set; } = string.Empty;

    /// <summary>Plant type: COAL, GAS, SOLAR, WIND, or NUCLEAR.</summary>
    public string PlantType { get; set; } = string.Empty;

    /// <summary>Current power output in MW.</summary>
    public decimal OutputMw { get; set; }

    /// <summary>Power supply status of this plant (always POWERED).</summary>
    public string PowerStatus { get; set; } = Data.Entities.PowerStatus.Powered;
}

/// <summary>
/// Snapshot of a company brand accumulated by R&amp;D research and marketing spend.
/// Exposed by the companyBrands query so the frontend can render research progress.
/// </summary>
public sealed class ResearchBrandState
{
    /// <summary>Brand identifier.</summary>
    public Guid Id { get; set; }

    /// <summary>Company that owns this brand.</summary>
    public Guid CompanyId { get; set; }

    /// <summary>Display name of the brand (product name or company name).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Scope: PRODUCT, CATEGORY, or COMPANY.</summary>
    public string Scope { get; set; } = BrandScope.Product;

    /// <summary>The specific product type this brand applies to (if scope is PRODUCT or CATEGORY).</summary>
    public Guid? ProductTypeId { get; set; }

    /// <summary>Human-readable product name for this brand (null for company-wide brands).</summary>
    public string? ProductName { get; set; }

    /// <summary>Industry category for CATEGORY-scoped brands.</summary>
    public string? IndustryCategory { get; set; }

    /// <summary>
    /// Brand awareness level (0.0–1.0). Driven by marketing unit spend.
    /// Higher awareness translates to more sales driven by brand recognition.
    /// </summary>
    public decimal Awareness { get; set; }

    /// <summary>
    /// Brand quality level (0.0–1.0). Driven by PRODUCT_QUALITY R&amp;D.
    /// Higher quality improves manufactured product output quality.
    /// </summary>
    public decimal Quality { get; set; }

    /// <summary>
    /// Marketing efficiency multiplier (≥ 1.0). Driven by BRAND_QUALITY R&amp;D.
    /// A value of 1.5 means each unit of marketing budget generates 50% more brand awareness than baseline.
    /// This is NOT a direct brand gain — it only amplifies the effect of marketing spend.
    /// </summary>
    public decimal MarketingEfficiencyMultiplier { get; set; } = 1m;
}

/// <summary>Phase values for the first-sale onboarding mission.</summary>
public static class FirstSaleMissionPhase
{
    /// <summary>Onboarding is not complete or no shop building is being tracked.</summary>
    public const string NoShop = "NO_SHOP";

    /// <summary>Shop exists but has at least one configuration blocker preventing the first sale.</summary>
    public const string ConfigureShop = "CONFIGURE_SHOP";

    /// <summary>Shop is fully configured; waiting for the next simulation tick to record a sale.</summary>
    public const string AwaitingFirstSale = "AWAITING_FIRST_SALE";

    /// <summary>A real PublicSalesRecord with QuantitySold &gt; 0 exists for the onboarding shop.</summary>
    public const string FirstSaleRecorded = "FIRST_SALE_RECORDED";

    /// <summary>The player has already acknowledged the first-sale milestone (OnboardingFirstSaleCompletedAtUtc is set).</summary>
    public const string AlreadyCompleted = "ALREADY_COMPLETED";
}

/// <summary>Blocker codes returned when the first-sale mission phase is CONFIGURE_SHOP.</summary>
public static class FirstSaleMissionBlocker
{
    /// <summary>The sales shop building is still under construction and cannot operate yet.</summary>
    public const string BuildingUnderConstruction = "BUILDING_UNDER_CONSTRUCTION";

    /// <summary>No PUBLIC_SALES unit is present in the shop building.</summary>
    public const string PublicSalesUnitMissing = "PUBLIC_SALES_UNIT_MISSING";

    /// <summary>The PUBLIC_SALES unit does not have a selling price set (MinPrice is null or zero).</summary>
    public const string PriceNotSet = "PRICE_NOT_SET";

    /// <summary>The PUBLIC_SALES unit has no inventory to sell yet (factory has not produced anything).</summary>
    public const string NoInventory = "NO_INVENTORY";
}

/// <summary>
/// Mission-status view model for the post-onboarding first-sale mission.
/// Returned by the <c>firstSaleMission</c> query.
/// </summary>
public sealed class FirstSaleMissionStatus
{
    /// <summary>
    /// Current phase of the first-sale mission.
    /// One of: NO_SHOP, CONFIGURE_SHOP, AWAITING_FIRST_SALE, FIRST_SALE_RECORDED, ALREADY_COMPLETED.
    /// </summary>
    public string Phase { get; set; } = FirstSaleMissionPhase.NoShop;

    /// <summary>The onboarding sales shop building ID being tracked (null when phase is NO_SHOP).</summary>
    public Guid? ShopBuildingId { get; set; }

    /// <summary>Display name of the onboarding sales shop (null when phase is NO_SHOP).</summary>
    public string? ShopName { get; set; }

    /// <summary>
    /// List of blocker codes explaining why the shop is not yet ready.
    /// Only populated when phase is CONFIGURE_SHOP.
    /// See <see cref="FirstSaleMissionBlocker"/> for possible values.
    /// </summary>
    public List<string> Blockers { get; set; } = [];

    /// <summary>Revenue from the first recorded sale (null until phase is FIRST_SALE_RECORDED).</summary>
    public decimal? FirstSaleRevenue { get; set; }

    /// <summary>Name of the product sold in the first sale (null until phase is FIRST_SALE_RECORDED).</summary>
    public string? FirstSaleProductName { get; set; }

    /// <summary>Game tick at which the first sale occurred (null until phase is FIRST_SALE_RECORDED).</summary>
    public long? FirstSaleTick { get; set; }

    /// <summary>Quantity sold in the first sale (null until phase is FIRST_SALE_RECORDED).</summary>
    public decimal? FirstSaleQuantity { get; set; }

    /// <summary>Price per unit in the first sale (null until phase is FIRST_SALE_RECORDED).</summary>
    public decimal? FirstSalePricePerUnit { get; set; }
}

/// <summary>
/// Read model for a media house building in a city.
/// Returned by the <c>cityMediaHouses</c> query.
/// </summary>
public sealed class CityMediaHouseInfo
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid CityId { get; set; }

    /// <summary>Channel type: NEWSPAPER, RADIO, TV. Null if not configured.</summary>
    public string? MediaType { get; set; }

    public Guid OwnerCompanyId { get; set; }
    public string OwnerCompanyName { get; set; } = string.Empty;

    /// <summary>
    /// Awareness multiplier applied when this media house is selected as the campaign channel.
    /// 1.0 = Newspaper, 1.5 = Radio, 2.0 = TV.
    /// </summary>
    public decimal EffectivenessMultiplier { get; set; }

    /// <summary>POWERED, CONSTRAINED, or OFFLINE.</summary>
    public string PowerStatus { get; set; } = Data.Entities.PowerStatus.Powered;

    public bool IsUnderConstruction { get; set; }
}

/// <summary>Read model for a loan offer visible to borrowers or bank owners.</summary>
public sealed class LoanOfferSummary
{
    public Guid Id { get; set; }
    public Guid BankBuildingId { get; set; }
    public string BankBuildingName { get; set; } = string.Empty;
    public Guid CityId { get; set; }
    public string CityName { get; set; } = string.Empty;
    public Guid LenderCompanyId { get; set; }
    public string LenderCompanyName { get; set; } = string.Empty;
    public decimal AnnualInterestRatePercent { get; set; }
    public decimal MaxPrincipalPerLoan { get; set; }
    public decimal TotalCapacity { get; set; }
    public decimal UsedCapacity { get; set; }
    public decimal RemainingCapacity { get; set; }
    public long DurationTicks { get; set; }
    public bool IsActive { get; set; }
    public long CreatedAtTick { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

/// <summary>Read model for an active or historical loan (borrower or lender view).</summary>
public sealed class LoanSummary
{
    public Guid Id { get; set; }
    public Guid LoanOfferId { get; set; }
    public Guid BorrowerCompanyId { get; set; }
    public string BorrowerCompanyName { get; set; } = string.Empty;
    public Guid LenderCompanyId { get; set; }
    public string LenderCompanyName { get; set; } = string.Empty;
    public Guid BankBuildingId { get; set; }
    public string BankBuildingName { get; set; } = string.Empty;
    public decimal OriginalPrincipal { get; set; }
    public decimal RemainingPrincipal { get; set; }
    public decimal AnnualInterestRatePercent { get; set; }
    public long DurationTicks { get; set; }
    public long StartTick { get; set; }
    public long DueTick { get; set; }
    public long NextPaymentTick { get; set; }
    public decimal PaymentAmount { get; set; }
    public int PaymentsMade { get; set; }
    public int TotalPayments { get; set; }
    public string Status { get; set; } = string.Empty;
    public int MissedPayments { get; set; }
    public decimal AccumulatedPenalty { get; set; }
    public DateTime AcceptedAtUtc { get; set; }
    public DateTime? ClosedAtUtc { get; set; }
}

/// <summary>Upgrade info for a single building unit: cost, timing, and stat projections.</summary>
public sealed class UnitUpgradeInfo
{
    public Guid UnitId { get; set; }
    public string UnitType { get; set; } = string.Empty;
    public int CurrentLevel { get; set; }
    public int NextLevel { get; set; }
    public bool IsMaxLevel { get; set; }
    public bool IsUpgradable { get; set; }
    public decimal UpgradeCost { get; set; }
    public int UpgradeTicks { get; set; }
    public decimal CurrentStat { get; set; }
    public decimal NextStat { get; set; }
    public string StatLabel { get; set; } = string.Empty;
}
