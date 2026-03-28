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
            .FirstOrDefaultAsync(c => c.Id == id);
    }

    /// <summary>Lists all raw material resource types in the game encyclopaedia.</summary>
    public async Task<List<ResourceType>> GetResourceTypes([Service] AppDbContext db)
    {
        return await db.ResourceTypes.OrderBy(r => r.Name).ToListAsync();
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
            .Where(c => c.PlayerId == userId)
            .OrderBy(c => c.Name)
            .ToListAsync();
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
            .FirstOrDefaultAsync(lot => lot.Id == id);
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
                    AverageQuality = averageQuality
                };
            })
            .Where(summary => summary.Capacity > 0m || summary.Quantity > 0m)
            .ToList();
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

    private static decimal GetUnitInventoryCapacity(BuildingUnit unit)
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
        [Service] AppDbContext db,
        [Service] IHttpContextAccessor httpContextAccessor)
    {
        var userId = httpContextAccessor.HttpContext!.User.GetRequiredUserId();

        var company = await db.Companies
            .Include(c => c.Buildings)
            .FirstOrDefaultAsync(c => c.Id == companyId && c.PlayerId == userId);

        if (company is null) return null;

        var entries = await db.LedgerEntries
            .Where(e => e.CompanyId == companyId)
            .ToListAsync();

        var totalRevenue = entries.Where(e => e.Category == LedgerCategory.Revenue).Sum(e => e.Amount);
        var totalPurchasingCosts = Math.Abs(entries.Where(e => e.Category == LedgerCategory.PurchasingCost).Sum(e => e.Amount));
        var totalMarketingCosts = Math.Abs(entries.Where(e => e.Category == LedgerCategory.Marketing).Sum(e => e.Amount));
        var totalTaxPaid = Math.Abs(entries.Where(e => e.Category == LedgerCategory.Tax).Sum(e => e.Amount));
        var totalOtherCosts = Math.Abs(entries.Where(e => e.Category == LedgerCategory.Other && e.Amount < 0).Sum(e => e.Amount));
        var totalPropertyPurchases = Math.Abs(entries.Where(e => e.Category == LedgerCategory.PropertyPurchase).Sum(e => e.Amount));

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

        return new CompanyLedgerSummary
        {
            CompanyId = company.Id,
            CompanyName = company.Name,
            CurrentCash = company.Cash,
            TotalRevenue = totalRevenue,
            TotalPurchasingCosts = totalPurchasingCosts,
            TotalMarketingCosts = totalMarketingCosts,
            TotalTaxPaid = totalTaxPaid,
            TotalOtherCosts = totalOtherCosts,
            TotalPropertyPurchases = totalPropertyPurchases,
            NetIncome = totalRevenue - totalPurchasingCosts - totalMarketingCosts - totalTaxPaid - totalOtherCosts,
            BuildingValue = buildingValue,
            InventoryValue = inventoryValue,
            TotalAssets = company.Cash + buildingValue + inventoryValue,
            CashFromOperations = totalRevenue - totalPurchasingCosts - totalMarketingCosts,
            CashFromInvestments = -totalPropertyPurchases,
            FirstRecordedTick = entries.Count > 0 ? entries.Min(e => e.RecordedAtTick) : 0,
            LastRecordedTick = entries.Count > 0 ? entries.Max(e => e.RecordedAtTick) : 0,
            BuildingSummaries = buildingSummaries,
        };
    }

    /// <summary>Returns drill-down entries for a specific ledger category.</summary>
    [Authorize]
    public async Task<List<LedgerEntryResult>> GetLedgerDrillDown(
        Guid companyId,
        string category,
        [Service] AppDbContext db,
        [Service] IHttpContextAccessor httpContextAccessor)
    {
        var userId = httpContextAccessor.HttpContext!.User.GetRequiredUserId();

        var ownsCompany = await db.Companies
            .AnyAsync(c => c.Id == companyId && c.PlayerId == userId);

        if (!ownsCompany) return [];

        var entries = await db.LedgerEntries
            .Where(e => e.CompanyId == companyId && e.Category == category)
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

        // Market share: look at the most recent tick's data for same product + city
        List<MarketShareEntry> marketShare = [];
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
                if (totalCitySales > 0)
                {
                    marketShare = cityRecords
                        .GroupBy(r => r.CompanyId)
                        .Select(g => new MarketShareEntry
                        {
                            Label = g.First().Company.Name,
                            CompanyId = g.Key,
                            Share = g.Sum(r => r.QuantitySold) / totalCitySales,
                        })
                        .OrderByDescending(e => e.Share)
                        .ToList();
                }
            }
        }

        return new PublicSalesAnalytics
        {
            BuildingUnitId = unit.Id,
            BuildingId = building.Id,
            BuildingName = building.Name,
            CityName = city?.Name ?? string.Empty,
            TotalRevenue = totalRevenue,
            TotalQuantitySold = totalQuantity,
            AveragePricePerUnit = averagePrice,
            CurrentSalesCapacity = GameConstants.SalesCapacity(unit.Level),
            DataFromTick = dataFromTick,
            DataToTick = dataToTick,
            RevenueHistory = revenueHistory,
            MarketShare = marketShare,
            PriceHistory = priceHistory,
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

/// <summary>Inventory fill information for a single building unit.</summary>
public sealed class BuildingUnitInventorySummary
{
    public Guid BuildingUnitId { get; set; }
    public decimal Quantity { get; set; }
    public decimal Capacity { get; set; }
    public decimal FillPercent { get; set; }
    public decimal? AverageQuality { get; set; }
}
