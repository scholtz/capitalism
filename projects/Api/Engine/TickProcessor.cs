using System.Diagnostics;
using Api.Data;
using Api.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Api.Engine;

/// <summary>
/// Orchestrates a single game tick: loads all data, runs each <see cref="ITickPhase"/>
/// in order, then persists changes in a single SaveChanges call.
/// Designed to handle 20 000 buildings / 500 000 units in under one second.
/// </summary>
public sealed class TickProcessor(
    AppDbContext db,
    IEnumerable<ITickPhase> phases,
    ILogger<TickProcessor> logger)
{
    /// <summary>
    /// Advances the game by one tick and returns the configured interval in seconds
    /// so the hosted service knows how long to wait before the next tick.
    /// </summary>
    public async Task<int> ProcessTickAsync(CancellationToken ct = default)
    {
        var gameState = await db.GameStates.FirstOrDefaultAsync(ct);
        if (gameState is null)
        {
            logger.LogWarning("No game state found; skipping tick.");
            return 10;
        }

        gameState.CurrentTick++;
        gameState.LastTickAtUtc = DateTime.UtcNow;

        var context = await BuildContextAsync(gameState, ct);
        var sw = Stopwatch.StartNew();

        foreach (var phase in phases.OrderBy(p => p.Order))
        {
            try
            {
                await phase.ProcessAsync(context);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Tick {Tick} – phase {Phase} failed.", gameState.CurrentTick, phase.Name);
            }
        }

        // Persist new inventory rows created during this tick (skip zero-quantity leftovers).
        var newToAdd = context.NewInventory.Where(i => i.Quantity > 0m).ToList();
        if (newToAdd.Count > 0)
        {
            db.Inventories.AddRange(newToAdd);
        }

        var newHistory = context.NewUnitResourceHistories
            .Where(entry => entry.InflowQuantity > 0m
                || entry.OutflowQuantity > 0m
                || entry.ConsumedQuantity > 0m
                || entry.ProducedQuantity > 0m)
            .ToList();
        if (newHistory.Count > 0)
        {
            db.BuildingUnitResourceHistories.AddRange(newHistory);
        }

        // Remove depleted tracked inventory (quantity ≤ 0) to keep the table lean.
        // NewInventory items are untracked so they can't be passed to RemoveRange.
        var depleted = context.InventoryByBuilding.Values
            .SelectMany(list => list)
            .Where(i => i.Quantity <= 0m && !context.NewInventory.Contains(i))
            .ToList();
        if (depleted.Count > 0)
        {
            db.Inventories.RemoveRange(depleted);
        }

        await db.SaveChangesAsync(ct);

        sw.Stop();
        logger.LogInformation(
            "Tick {Tick} completed in {ElapsedMs}ms  (buildings={Buildings}, phases={Phases})",
            gameState.CurrentTick,
            sw.ElapsedMilliseconds,
            context.BuildingsById.Count,
            phases.Count());

        return gameState.TickIntervalSeconds;
    }

    private async Task<TickContext> BuildContextAsync(GameState gameState, CancellationToken ct)
    {
        // Bulk-load all data needed by every phase.
        // EF Core change-tracking is required for entities we modify (Company.Cash,
        // Inventory.Quantity, Brand, Building, ExchangeOrder, GameState).

        // AsSplitQuery prevents EF Core from generating a single SQL query with a Cartesian
        // product across three collection navigation properties (Units, PendingConfiguration.Units,
        // PendingConfiguration.Removals).  Without it, the same BuildingUnit row can appear
        // multiple times in the result set, and the subsequent ToDictionary call for
        // UnitsByBuildingPosition throws "An item with the same key has already been added."
        var buildings = await db.Buildings
            .Include(b => b.Units)
            .Include(b => b.PendingConfiguration)!.ThenInclude(p => p!.Units)
            .Include(b => b.PendingConfiguration)!.ThenInclude(p => p!.Removals)
            .AsSplitQuery()
            .ToListAsync(ct);

        var companies = await db.Companies.ToListAsync(ct);
        var companySalarySettings = await db.CompanyCitySalarySettings
            .Include(setting => setting.City)
            .ToListAsync(ct);
        var cities = await db.Cities.ToListAsync(ct);
        var lots = await db.BuildingLots
            .Where(lot => lot.OwnerCompanyId.HasValue)
            .ToListAsync(ct);
        var cityResources = await db.CityResources.ToListAsync(ct);
        var resourceTypes = await db.ResourceTypes.ToListAsync(ct);
        var productTypes = await db.ProductTypes.Include(p => p.Recipes).ToListAsync(ct);
        var brands = await db.Brands.ToListAsync(ct);
        var inventories = await db.Inventories.ToListAsync(ct);
        var exchangeOrders = await db.ExchangeOrders.Where(o => o.IsActive).ToListAsync(ct);

        // Build a city-keyed map of total absolute salary paid in the past
        // RecentSalaryWindowTicks ticks.  This implements the ROADMAP requirement
        // "the game currency collected by salaries in past 10 ticks".
        // Uses a single JOIN-free query: load only Id→CityId for buildings that
        // have LaborCost entries in the window, then group in memory.
        var salaryWindowStart = gameState.CurrentTick - GameConstants.RecentSalaryWindowTicks;
        var buildingCityLookup = buildings.ToDictionary(b => b.Id, b => b.CityId);
        var recentLaborEntries = await db.LedgerEntries
            .Where(e => e.Category == LedgerCategory.LaborCost
                && e.RecordedAtTick > salaryWindowStart
                && e.BuildingId.HasValue)
            .Select(e => new { e.BuildingId, e.Amount })
            .ToListAsync(ct);

        var recentSalaryByCity = recentLaborEntries
            .Where(e => buildingCityLookup.ContainsKey(e.BuildingId!.Value))
            .GroupBy(e => buildingCityLookup[e.BuildingId!.Value])
            .ToDictionary(g => g.Key, g => g.Sum(e => Math.Abs(e.Amount)));

        return new TickContext
        {
            Db = db,
            GameState = gameState,
            BuildingsById = buildings.ToDictionary(b => b.Id),
            BuildingsByType = buildings
                .GroupBy(b => b.Type)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase),
            UnitsByBuilding = buildings.ToDictionary(b => b.Id, b => b.Units.ToList()),
            UnitsByBuildingPosition = buildings.ToDictionary(
                b => b.Id,
                b => b.Units.DistinctBy(u => (u.GridX, u.GridY)).ToDictionary(u => (u.GridX, u.GridY))),
            InventoryByUnit = inventories
                .Where(i => i.BuildingUnitId.HasValue)
                .GroupBy(i => i.BuildingUnitId!.Value)
                .ToDictionary(g => g.Key, g => g.ToList()),
            InventoryByBuilding = inventories
                .GroupBy(i => i.BuildingId)
                .ToDictionary(g => g.Key, g => g.ToList()),
            CompaniesById = companies.ToDictionary(c => c.Id),
            CitySalarySettingsByCompany = companySalarySettings
                .GroupBy(setting => setting.CompanyId)
                .ToDictionary(g => g.Key, g => g.ToList()),
            CitiesById = cities.ToDictionary(c => c.Id),
            LotsByCompany = lots
                .GroupBy(lot => lot.OwnerCompanyId!.Value)
                .ToDictionary(g => g.Key, g => g.ToList()),
            ResourcesByCity = cityResources
                .GroupBy(cr => cr.CityId)
                .ToDictionary(g => g.Key, g => g.ToList()),
            ResourceTypesById = resourceTypes.ToDictionary(r => r.Id),
            ProductTypesById = productTypes.ToDictionary(p => p.Id),
            RecipesByProduct = productTypes.ToDictionary(p => p.Id, p => p.Recipes.ToList()),
            BrandsByCompany = brands
                .GroupBy(b => b.CompanyId)
                .ToDictionary(g => g.Key, g => g.ToList()),
            ActiveExchangeOrders = exchangeOrders,
            TickStartRemainingQuantityByInventoryId = inventories.ToDictionary(i => i.Id, i => i.Quantity),
            RecentSalaryByCity = recentSalaryByCity,
        };
    }
}
