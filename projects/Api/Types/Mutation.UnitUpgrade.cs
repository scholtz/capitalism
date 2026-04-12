using System.Globalization;
using Api.Data;
using Api.Data.Entities;
using Api.Security;
using Api.Utilities;
using HotChocolate.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Api.Types;

public sealed partial class Mutation
{
    /// <summary>
    /// Schedules a level upgrade for a building unit.
    /// Deducts the upgrade cost from the owning company's cash immediately
    /// and creates a queued building configuration plan that applies after the required ticks.
    /// </summary>
    [Authorize]
    public async Task<BuildingConfigurationPlan> ScheduleUnitUpgrade(
        ScheduleUnitUpgradeInput input,
        [Service] AppDbContext db,
        [Service] IHttpContextAccessor httpContextAccessor)
    {
        var userId = httpContextAccessor.HttpContext!.User.GetRequiredUserId();

        var gameState = await db.GameStates.FirstOrDefaultAsync()
            ?? throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Game state is not initialized.")
                    .SetCode("GAME_STATE_NOT_FOUND")
                    .Build());

        await BuildingConfigurationService.ApplyDuePlansAsync(db, gameState.CurrentTick);

        var unit = await db.BuildingUnits
            .Include(u => u.Building)
            .ThenInclude(b => b.Company)
            .Include(u => u.Building)
            .ThenInclude(b => b.Units)
            .Include(u => u.Building)
            .ThenInclude(b => b.PendingConfiguration)
            .ThenInclude(plan => plan!.Units)
            .Include(u => u.Building)
            .ThenInclude(b => b.PendingConfiguration)
            .ThenInclude(plan => plan!.Removals)
            .AsSplitQuery()
            .FirstOrDefaultAsync(u => u.Id == input.UnitId);

        if (unit is null || unit.Building.Company.PlayerId != userId)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Unit not found or you don't own it.")
                    .SetCode("UNIT_NOT_FOUND")
                    .Build());
        }

        if (!Engine.GameConstants.IsUpgradableUnitType(unit.UnitType))
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage($"Unit type {unit.UnitType} does not support level upgrades.")
                    .SetCode("UNIT_NOT_UPGRADABLE")
                    .Build());
        }

        if (unit.Level >= Engine.GameConstants.MaxUnitLevel)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage($"This unit is already at maximum level ({Engine.GameConstants.MaxUnitLevel}).")
                    .SetCode("MAX_LEVEL_REACHED")
                    .Build());
        }

        if (unit.Building.PendingConfiguration is not null)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("This building already has a pending configuration change. Wait for it to complete or cancel it before scheduling an upgrade.")
                    .SetCode("PENDING_CONFIGURATION_EXISTS")
                    .Build());
        }

        var upgradeCost = Engine.GameConstants.UnitUpgradeCost(unit.UnitType, unit.Level);
        var company = unit.Building.Company;

        if (company.Cash < upgradeCost)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage($"Insufficient funds. Upgrade costs ${upgradeCost.ToString("N0", CultureInfo.InvariantCulture)} but your company only has ${company.Cash.ToString("N0", CultureInfo.InvariantCulture)}.")
                    .SetCode("INSUFFICIENT_FUNDS")
                    .Build());
        }

        company.Cash -= upgradeCost;

        var upgradeTicks = Engine.GameConstants.UnitUpgradeTicks(unit.Level);
        var planId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        var plan = new BuildingConfigurationPlan
        {
            Id = planId,
            BuildingId = unit.BuildingId,
            SubmittedAtUtc = now,
            SubmittedAtTick = gameState.CurrentTick,
            AppliesAtTick = gameState.CurrentTick + upgradeTicks,
            TotalTicksRequired = upgradeTicks,
        };

        // Snapshot all active units; only the target unit gets a level bump and a timer.
        // DistinctBy is defensive deduplication per coding guidelines; AsSplitQuery() above prevents
        // Cartesian explosion, but we deduplicate by position as a safety net.
        var allActiveUnits = unit.Building.Units.DistinctBy(u => (u.GridX, u.GridY)).ToList();
        foreach (var activeUnit in allActiveUnits)
        {
            bool isTarget = activeUnit.Id == unit.Id;
            plan.Units.Add(new BuildingConfigurationPlanUnit
            {
                Id = Guid.NewGuid(),
                BuildingConfigurationPlanId = planId,
                UnitType = activeUnit.UnitType,
                GridX = activeUnit.GridX,
                GridY = activeUnit.GridY,
                Level = isTarget ? activeUnit.Level + 1 : activeUnit.Level,
                LinkUp = activeUnit.LinkUp,
                LinkDown = activeUnit.LinkDown,
                LinkLeft = activeUnit.LinkLeft,
                LinkRight = activeUnit.LinkRight,
                LinkUpLeft = activeUnit.LinkUpLeft,
                LinkUpRight = activeUnit.LinkUpRight,
                LinkDownLeft = activeUnit.LinkDownLeft,
                LinkDownRight = activeUnit.LinkDownRight,
                StartedAtTick = gameState.CurrentTick,
                AppliesAtTick = isTarget ? gameState.CurrentTick + upgradeTicks : gameState.CurrentTick,
                TicksRequired = isTarget ? upgradeTicks : 0,
                IsChanged = isTarget,
                ResourceTypeId = activeUnit.ResourceTypeId,
                ProductTypeId = activeUnit.ProductTypeId,
                MinPrice = activeUnit.MinPrice,
                MaxPrice = activeUnit.MaxPrice,
                PurchaseSource = activeUnit.PurchaseSource,
                SaleVisibility = activeUnit.SaleVisibility,
                Budget = activeUnit.Budget,
                MediaHouseBuildingId = activeUnit.MediaHouseBuildingId,
                MinQuality = activeUnit.MinQuality,
                BrandScope = activeUnit.BrandScope,
                VendorLockCompanyId = activeUnit.VendorLockCompanyId,
                LockedCityId = activeUnit.LockedCityId,
            });
        }

        db.BuildingConfigurationPlans.Add(plan);

        db.LedgerEntries.Add(new LedgerEntry
        {
            Id = Guid.NewGuid(),
            CompanyId = company.Id,
            BuildingId = unit.BuildingId,
            BuildingUnitId = unit.Id,
            Category = LedgerCategory.UnitUpgrade,
            Description = $"Unit upgrade: {unit.UnitType} Lv{unit.Level}→{unit.Level + 1} ({unit.Building.Name})",
            Amount = -upgradeCost,
            RecordedAtTick = gameState.CurrentTick,
            RecordedAtUtc = now,
        });

        await db.SaveChangesAsync();

        return await db.BuildingConfigurationPlans
            .Include(p => p.Units)
            .Include(p => p.Removals)
            .FirstAsync(p => p.Id == planId);
    }
}
