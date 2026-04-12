using Api.Data;
using Api.Data.Entities;
using Api.Types;
using HotChocolate;
using Microsoft.EntityFrameworkCore;

namespace Api.Utilities;

/// <summary>
/// Handles validation, queueing, modification, and activation of building configuration upgrades.
/// </summary>
public static partial class BuildingConfigurationService
{
    public const int LinkChangeTicks = 1;
    public const int UnitPlanChangeTicks = 3;
    private const decimal CancelTicksMultiplier = 0.1m;

    /// <summary>Normalizes null and empty strings to null for consistent comparison.</summary>
    private static string? NormalizeString(string? value) => string.IsNullOrEmpty(value) ? null : value;

    public static async Task<BuildingConfigurationPlan> StoreConfigurationAsync(
        AppDbContext db,
        Building building,
        IReadOnlyCollection<BuildingConfigurationUnitInput> submittedUnits,
        long currentTick)
    {
        ValidateUnits(building.Type, submittedUnits);
        await ValidateRecipeCompatibilityAsync(db, building.Type, submittedUnits);

        var existingPlan = await db.BuildingConfigurationPlans
            .Include(plan => plan.Units)
            .Include(plan => plan.Removals)
            .FirstOrDefaultAsync(plan => plan.BuildingId == building.Id);

        var currentUnits = building.Units.DistinctBy(unit => (unit.GridX, unit.GridY)).ToDictionary(unit => (unit.GridX, unit.GridY));
        var desiredUnits = submittedUnits
            .OrderBy(unit => unit.GridY)
            .ThenBy(unit => unit.GridX)
            .ToDictionary(unit => (unit.GridX, unit.GridY));
        var existingTargetUnits = existingPlan?.Units.ToDictionary(unit => (unit.GridX, unit.GridY))
            ?? new Dictionary<(int GridX, int GridY), BuildingConfigurationPlanUnit>();
        var existingRemovals = existingPlan?.Removals.ToDictionary(removal => (removal.GridX, removal.GridY))
            ?? new Dictionary<(int GridX, int GridY), BuildingConfigurationPlanRemoval>();
        var planId = Guid.NewGuid();

        var allPositions = currentUnits.Keys
            .Union(desiredUnits.Keys)
            .Union(existingTargetUnits.Keys)
            .Union(existingRemovals.Keys)
            .OrderBy(position => position.GridY)
            .ThenBy(position => position.GridX)
            .ToList();

        var nextUnits = new List<BuildingConfigurationPlanUnit>();
        var nextRemovals = new List<BuildingConfigurationPlanRemoval>();

        foreach (var position in allPositions)
        {
            var activeUnit = currentUnits.GetValueOrDefault(position);
            var desiredUnit = desiredUnits.GetValueOrDefault(position);
            var existingTargetUnit = existingTargetUnits.GetValueOrDefault(position);
            var existingRemoval = existingRemovals.GetValueOrDefault(position);

            if (desiredUnit is not null)
            {
                nextUnits.Add(CreateNextUnitPlan(
                    desiredUnit,
                    activeUnit,
                    existingTargetUnit,
                    existingRemoval,
                    currentTick,
                    planId));
                continue;
            }

            var removal = CreateNextRemovalPlan(
                activeUnit,
                existingTargetUnit,
                existingRemoval,
                currentTick,
                planId,
                position.GridX,
                position.GridY);

            if (removal is not null)
            {
                nextRemovals.Add(removal);
            }
        }

        var hasPendingChanges = nextUnits.Any(unit => unit.IsChanged) || nextRemovals.Any();
        if (!hasPendingChanges)
        {
            if (existingPlan is not null)
            {
                db.BuildingConfigurationPlans.Remove(existingPlan);
            }

            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("No building configuration changes were detected.")
                    .SetCode("NO_BUILDING_CONFIGURATION_CHANGES")
                    .Build());
        }

        if (existingPlan is not null)
        {
            db.BuildingConfigurationPlans.Remove(existingPlan);
            await db.SaveChangesAsync();
        }

        var plan = new BuildingConfigurationPlan
        {
            Id = planId,
            BuildingId = building.Id
        };
        db.BuildingConfigurationPlans.Add(plan);

        plan.SubmittedAtUtc = DateTime.UtcNow;
        plan.SubmittedAtTick = currentTick;
        plan.Units = nextUnits;
        plan.Removals = nextRemovals;
        RefreshPlanSummary(plan, currentTick);

        return plan;
    }

    public static async Task ApplyDuePlansAsync(AppDbContext db, long currentTick)
    {
        // AsSplitQuery prevents EF Core Cartesian explosion when loading plan.Units,
        // plan.Removals, and plan.Building.Units simultaneously.  Without it, the
        // Building.Units navigation property can contain duplicate entries, causing
        // liveUnitsByPosition to appear empty (or duplicate keys) and leading to
        // spurious new BuildingUnit records being inserted for positions that already exist.
        var plans = await db.BuildingConfigurationPlans
            .Include(plan => plan.Units)
            .Include(plan => plan.Removals)
            .Include(plan => plan.Building)
            .ThenInclude(building => building.Company)
            .Include(plan => plan.Building)
            .ThenInclude(building => building.Units)
            .AsSplitQuery()
            .ToListAsync();

        foreach (var plan in plans)
        {
            var liveUnitsByPosition = plan.Building.Units.DistinctBy(unit => (unit.GridX, unit.GridY)).ToDictionary(unit => (unit.GridX, unit.GridY));

            foreach (var removal in plan.Removals.Where(removal => removal.AppliesAtTick <= currentTick).ToList())
            {
                if (liveUnitsByPosition.TryGetValue((removal.GridX, removal.GridY), out var liveUnit))
                {
                    db.BuildingUnits.Remove(liveUnit);
                    plan.Building.Units.Remove(liveUnit);
                    liveUnitsByPosition.Remove((removal.GridX, removal.GridY));
                }

                plan.Removals.Remove(removal);
                db.BuildingConfigurationPlanRemovals.Remove(removal);
            }

            var dueUnits = plan.Units
                .Where(unit => unit.IsChanged && unit.AppliesAtTick <= currentTick)
                .ToList();

            var costfulDueUnits = dueUnits
                .Select(unit => new
                {
                    Unit = unit,
                    Cost = BuildingConfigurationEconomics.CalculateActivationCost(
                        liveUnitsByPosition.GetValueOrDefault((unit.GridX, unit.GridY)),
                        unit)
                })
                .Where(entry => entry.Cost > 0m)
                .ToList();

            var totalActivationCost = costfulDueUnits.Sum(entry => entry.Cost);

            if (totalActivationCost > 0m && plan.Building.Company.Cash < totalActivationCost)
            {
                foreach (var entry in costfulDueUnits)
                {
                    entry.Unit.AppliesAtTick = currentTick + 1;
                    entry.Unit.TicksRequired = Math.Max(entry.Unit.TicksRequired, 1);
                }

                dueUnits = dueUnits.Except(costfulDueUnits.Select(entry => entry.Unit)).ToList();
            }
            else if (totalActivationCost > 0m)
            {
                plan.Building.Company.Cash -= totalActivationCost;
            }

            foreach (var pendingUnit in dueUnits)
            {
                if (!liveUnitsByPosition.TryGetValue((pendingUnit.GridX, pendingUnit.GridY), out var liveUnit))
                {
                    liveUnit = new BuildingUnit
                    {
                        Id = Guid.NewGuid(),
                        BuildingId = plan.BuildingId
                    };

                    db.BuildingUnits.Add(liveUnit);
                    liveUnitsByPosition[(pendingUnit.GridX, pendingUnit.GridY)] = liveUnit;
                }

                liveUnit.UnitType = pendingUnit.UnitType;
                liveUnit.GridX = pendingUnit.GridX;
                liveUnit.GridY = pendingUnit.GridY;
                liveUnit.Level = pendingUnit.Level;
                liveUnit.LinkUp = pendingUnit.LinkUp;
                liveUnit.LinkDown = pendingUnit.LinkDown;
                liveUnit.LinkLeft = pendingUnit.LinkLeft;
                liveUnit.LinkRight = pendingUnit.LinkRight;
                liveUnit.LinkUpLeft = pendingUnit.LinkUpLeft;
                liveUnit.LinkUpRight = pendingUnit.LinkUpRight;
                liveUnit.LinkDownLeft = pendingUnit.LinkDownLeft;
                liveUnit.LinkDownRight = pendingUnit.LinkDownRight;
                liveUnit.ResourceTypeId = pendingUnit.ResourceTypeId;
                liveUnit.ProductTypeId = pendingUnit.ProductTypeId;
                liveUnit.MinPrice = pendingUnit.MinPrice;
                liveUnit.MaxPrice = pendingUnit.MaxPrice;
                liveUnit.PurchaseSource = pendingUnit.PurchaseSource;
                liveUnit.SaleVisibility = pendingUnit.SaleVisibility;
                liveUnit.Budget = pendingUnit.Budget;
                liveUnit.MediaHouseBuildingId = pendingUnit.MediaHouseBuildingId;
                liveUnit.MinQuality = pendingUnit.MinQuality;
                liveUnit.BrandScope = pendingUnit.BrandScope;
                liveUnit.VendorLockCompanyId = pendingUnit.VendorLockCompanyId;
                // LockedCityId only applies to EXCHANGE mode; clear it for any other mode.
                liveUnit.LockedCityId = (pendingUnit.PurchaseSource == "EXCHANGE") ? pendingUnit.LockedCityId : null;

                pendingUnit.StartedAtTick = currentTick;
                pendingUnit.AppliesAtTick = currentTick;
                pendingUnit.TicksRequired = 0;
                pendingUnit.IsChanged = false;
                pendingUnit.IsReverting = false;
            }

            if (!plan.Units.Any(unit => unit.IsChanged) && plan.Removals.Count == 0)
            {
                db.BuildingConfigurationPlanUnits.RemoveRange(plan.Units);
                db.BuildingConfigurationPlans.Remove(plan);
                continue;
            }

            RefreshPlanSummary(plan, currentTick);
        }
    }

    public static IReadOnlyList<string> GetAllowedUnitTypes(string buildingType)
    {
        return buildingType switch
        {
            BuildingType.Mine => [UnitType.Mining, UnitType.Storage, UnitType.B2BSales],
            BuildingType.Factory => [UnitType.Purchase, UnitType.Manufacturing, UnitType.Branding, UnitType.Storage, UnitType.B2BSales],
            BuildingType.SalesShop => [UnitType.Purchase, UnitType.Marketing, UnitType.Storage, UnitType.PublicSales],
            BuildingType.ResearchDevelopment => [UnitType.ProductQuality, UnitType.BrandQuality],
            _ => []
        };
    }

}
