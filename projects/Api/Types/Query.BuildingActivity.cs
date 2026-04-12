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
}
