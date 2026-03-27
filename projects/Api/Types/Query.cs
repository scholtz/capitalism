using Api.Data;
using Api.Data.Entities;
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
