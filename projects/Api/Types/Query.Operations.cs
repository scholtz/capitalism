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
}
