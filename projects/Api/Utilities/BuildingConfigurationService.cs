using Api.Data;
using Api.Data.Entities;
using Api.Types;
using HotChocolate;
using Microsoft.EntityFrameworkCore;

namespace Api.Utilities;

/// <summary>
/// Handles validation, queueing, modification, and activation of building configuration upgrades.
/// </summary>
public static class BuildingConfigurationService
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

        var existingPlan = await db.BuildingConfigurationPlans
            .Include(plan => plan.Units)
            .Include(plan => plan.Removals)
            .FirstOrDefaultAsync(plan => plan.BuildingId == building.Id);

        var currentUnits = building.Units.ToDictionary(unit => (unit.GridX, unit.GridY));
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
        var plans = await db.BuildingConfigurationPlans
            .Include(plan => plan.Units)
            .Include(plan => plan.Removals)
            .Include(plan => plan.Building)
            .ThenInclude(building => building.Units)
            .ToListAsync();

        foreach (var plan in plans)
        {
            var liveUnitsByPosition = plan.Building.Units.ToDictionary(unit => (unit.GridX, unit.GridY));

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

            foreach (var pendingUnit in plan.Units.Where(unit => unit.IsChanged && unit.AppliesAtTick <= currentTick).ToList())
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
            BuildingType.SalesShop => [UnitType.Purchase, UnitType.Marketing, UnitType.PublicSales],
            BuildingType.ResearchDevelopment => [UnitType.ProductQuality, UnitType.BrandQuality],
            _ => []
        };
    }

    private static BuildingConfigurationPlanUnit CreateNextUnitPlan(
        BuildingConfigurationUnitInput desiredUnit,
        BuildingUnit? activeUnit,
        BuildingConfigurationPlanUnit? existingTargetUnit,
        BuildingConfigurationPlanRemoval? existingRemoval,
        long currentTick,
        Guid planId)
    {
        if (existingTargetUnit is not null && AreEquivalent(existingTargetUnit, desiredUnit))
        {
            if (IsPending(existingTargetUnit, currentTick))
            {
                return CloneUnit(existingTargetUnit);
            }

            return CreateUnitSnapshot(planId, desiredUnit, activeUnit?.Level ?? existingTargetUnit.Level, currentTick, 0, false, false);
        }

        if (existingRemoval is not null
            && IsPending(existingRemoval, currentTick)
            && activeUnit is not null
            && AreEquivalent(activeUnit, desiredUnit))
        {
            return CreateUnitSnapshot(
                planId,
                desiredUnit,
                activeUnit.Level,
                currentTick,
                CalculateCancelTicks(existingRemoval.TicksRequired),
                true,
                true);
        }

        if (existingTargetUnit is not null
            && IsPending(existingTargetUnit, currentTick)
            && activeUnit is not null
            && AreEquivalent(activeUnit, desiredUnit))
        {
            return CreateUnitSnapshot(
                planId,
                desiredUnit,
                activeUnit.Level,
                currentTick,
                CalculateCancelTicks(existingTargetUnit.TicksRequired),
                true,
                true);
        }

        return CreateUnitSnapshot(
            planId,
            desiredUnit,
            activeUnit?.Level ?? 1,
            currentTick,
            CalculateTicksRequired(activeUnit, desiredUnit),
            false,
            !AreEquivalent(activeUnit, desiredUnit));
    }

    private static BuildingConfigurationPlanRemoval? CreateNextRemovalPlan(
        BuildingUnit? activeUnit,
        BuildingConfigurationPlanUnit? existingTargetUnit,
        BuildingConfigurationPlanRemoval? existingRemoval,
        long currentTick,
        Guid planId,
        int gridX,
        int gridY)
    {
        if (existingRemoval is not null && IsPending(existingRemoval, currentTick) && activeUnit is not null)
        {
            return CloneRemoval(existingRemoval);
        }

        if (existingTargetUnit is not null && IsPending(existingTargetUnit, currentTick))
        {
            return CreateRemoval(planId, gridX, gridY, currentTick, CalculateCancelTicks(existingTargetUnit.TicksRequired), true);
        }

        if (activeUnit is not null)
        {
            return CreateRemoval(planId, gridX, gridY, currentTick, UnitPlanChangeTicks, false);
        }

        return null;
    }

    private static BuildingConfigurationPlanUnit CreateUnitSnapshot(
        Guid planId,
        BuildingConfigurationUnitInput input,
        int level,
        long currentTick,
        int ticksRequired,
        bool isReverting,
        bool isChanged)
    {
        return new BuildingConfigurationPlanUnit
        {
            Id = Guid.NewGuid(),
            BuildingConfigurationPlanId = planId,
            UnitType = input.UnitType,
            GridX = input.GridX,
            GridY = input.GridY,
            Level = level,
            LinkUp = input.LinkUp,
            LinkDown = input.LinkDown,
            LinkLeft = input.LinkLeft,
            LinkRight = input.LinkRight,
            LinkUpLeft = input.LinkUpLeft,
            LinkUpRight = input.LinkUpRight,
            LinkDownLeft = input.LinkDownLeft,
            LinkDownRight = input.LinkDownRight,
            StartedAtTick = currentTick,
            AppliesAtTick = currentTick + ticksRequired,
            TicksRequired = ticksRequired,
            IsChanged = isChanged || ticksRequired > 0,
            IsReverting = isReverting,
            ResourceTypeId = input.ResourceTypeId,
            ProductTypeId = input.ProductTypeId,
            MinPrice = input.MinPrice,
            MaxPrice = input.MaxPrice,
            PurchaseSource = input.PurchaseSource,
            SaleVisibility = input.SaleVisibility,
            Budget = input.Budget,
            MediaHouseBuildingId = input.MediaHouseBuildingId,
            MinQuality = input.MinQuality,
            BrandScope = input.BrandScope,
            VendorLockCompanyId = input.VendorLockCompanyId,
        };
    }

    private static BuildingConfigurationPlanUnit CloneUnit(BuildingConfigurationPlanUnit unit)
    {
        return new BuildingConfigurationPlanUnit
        {
            Id = Guid.NewGuid(),
            BuildingConfigurationPlanId = unit.BuildingConfigurationPlanId,
            UnitType = unit.UnitType,
            GridX = unit.GridX,
            GridY = unit.GridY,
            Level = unit.Level,
            LinkUp = unit.LinkUp,
            LinkDown = unit.LinkDown,
            LinkLeft = unit.LinkLeft,
            LinkRight = unit.LinkRight,
            LinkUpLeft = unit.LinkUpLeft,
            LinkUpRight = unit.LinkUpRight,
            LinkDownLeft = unit.LinkDownLeft,
            LinkDownRight = unit.LinkDownRight,
            StartedAtTick = unit.StartedAtTick,
            AppliesAtTick = unit.AppliesAtTick,
            TicksRequired = unit.TicksRequired,
            IsChanged = unit.IsChanged,
            IsReverting = unit.IsReverting,
            ResourceTypeId = unit.ResourceTypeId,
            ProductTypeId = unit.ProductTypeId,
            MinPrice = unit.MinPrice,
            MaxPrice = unit.MaxPrice,
            PurchaseSource = unit.PurchaseSource,
            SaleVisibility = unit.SaleVisibility,
            Budget = unit.Budget,
            MediaHouseBuildingId = unit.MediaHouseBuildingId,
            MinQuality = unit.MinQuality,
            BrandScope = unit.BrandScope,
            VendorLockCompanyId = unit.VendorLockCompanyId,
        };
    }

    private static BuildingConfigurationPlanRemoval CreateRemoval(
        Guid planId,
        int gridX,
        int gridY,
        long currentTick,
        int ticksRequired,
        bool isReverting)
    {
        return new BuildingConfigurationPlanRemoval
        {
            Id = Guid.NewGuid(),
            BuildingConfigurationPlanId = planId,
            GridX = gridX,
            GridY = gridY,
            StartedAtTick = currentTick,
            AppliesAtTick = currentTick + ticksRequired,
            TicksRequired = ticksRequired,
            IsReverting = isReverting,
        };
    }

    private static BuildingConfigurationPlanRemoval CloneRemoval(BuildingConfigurationPlanRemoval removal)
    {
        return new BuildingConfigurationPlanRemoval
        {
            Id = Guid.NewGuid(),
            BuildingConfigurationPlanId = removal.BuildingConfigurationPlanId,
            GridX = removal.GridX,
            GridY = removal.GridY,
            StartedAtTick = removal.StartedAtTick,
            AppliesAtTick = removal.AppliesAtTick,
            TicksRequired = removal.TicksRequired,
            IsReverting = removal.IsReverting,
        };
    }

    private static int CalculateTicksRequired(BuildingUnit? currentUnit, BuildingConfigurationUnitInput input)
    {
        if (currentUnit is null)
        {
            return UnitPlanChangeTicks;
        }

        if (!string.Equals(currentUnit.UnitType, input.UnitType, StringComparison.Ordinal))
        {
            return UnitPlanChangeTicks;
        }

        if (currentUnit.LinkUp != input.LinkUp
            || currentUnit.LinkDown != input.LinkDown
            || currentUnit.LinkLeft != input.LinkLeft
            || currentUnit.LinkRight != input.LinkRight
            || currentUnit.LinkUpLeft != input.LinkUpLeft
            || currentUnit.LinkUpRight != input.LinkUpRight
            || currentUnit.LinkDownLeft != input.LinkDownLeft
            || currentUnit.LinkDownRight != input.LinkDownRight)
        {
            return LinkChangeTicks;
        }

        if (currentUnit.ResourceTypeId != input.ResourceTypeId
            || currentUnit.ProductTypeId != input.ProductTypeId
            || currentUnit.MinPrice != input.MinPrice
            || currentUnit.MaxPrice != input.MaxPrice
            || !string.Equals(NormalizeString(currentUnit.PurchaseSource), NormalizeString(input.PurchaseSource), StringComparison.Ordinal)
            || !string.Equals(NormalizeString(currentUnit.SaleVisibility), NormalizeString(input.SaleVisibility), StringComparison.Ordinal)
            || currentUnit.Budget != input.Budget
            || currentUnit.MediaHouseBuildingId != input.MediaHouseBuildingId
            || currentUnit.MinQuality != input.MinQuality
            || !string.Equals(NormalizeString(currentUnit.BrandScope), NormalizeString(input.BrandScope), StringComparison.Ordinal)
            || currentUnit.VendorLockCompanyId != input.VendorLockCompanyId)
        {
            return LinkChangeTicks;
        }

        return 0;
    }

    private static int CalculateCancelTicks(int originalTicks)
    {
        return Math.Max(1, (int)Math.Ceiling(originalTicks * CancelTicksMultiplier));
    }

    private static bool AreEquivalent(BuildingUnit? currentUnit, BuildingConfigurationUnitInput desiredUnit)
    {
        if (currentUnit is null)
        {
            return false;
        }

        return string.Equals(currentUnit.UnitType, desiredUnit.UnitType, StringComparison.Ordinal)
            && currentUnit.GridX == desiredUnit.GridX
            && currentUnit.GridY == desiredUnit.GridY
            && currentUnit.LinkUp == desiredUnit.LinkUp
            && currentUnit.LinkDown == desiredUnit.LinkDown
            && currentUnit.LinkLeft == desiredUnit.LinkLeft
            && currentUnit.LinkRight == desiredUnit.LinkRight
            && currentUnit.LinkUpLeft == desiredUnit.LinkUpLeft
            && currentUnit.LinkUpRight == desiredUnit.LinkUpRight
            && currentUnit.LinkDownLeft == desiredUnit.LinkDownLeft
            && currentUnit.LinkDownRight == desiredUnit.LinkDownRight
            && currentUnit.ResourceTypeId == desiredUnit.ResourceTypeId
            && currentUnit.ProductTypeId == desiredUnit.ProductTypeId
            && currentUnit.MinPrice == desiredUnit.MinPrice
            && currentUnit.MaxPrice == desiredUnit.MaxPrice
            && string.Equals(NormalizeString(currentUnit.PurchaseSource), NormalizeString(desiredUnit.PurchaseSource), StringComparison.Ordinal)
            && string.Equals(NormalizeString(currentUnit.SaleVisibility), NormalizeString(desiredUnit.SaleVisibility), StringComparison.Ordinal)
            && currentUnit.Budget == desiredUnit.Budget
            && currentUnit.MediaHouseBuildingId == desiredUnit.MediaHouseBuildingId
            && currentUnit.MinQuality == desiredUnit.MinQuality
            && string.Equals(NormalizeString(currentUnit.BrandScope), NormalizeString(desiredUnit.BrandScope), StringComparison.Ordinal)
            && currentUnit.VendorLockCompanyId == desiredUnit.VendorLockCompanyId;
    }

    private static bool AreEquivalent(BuildingConfigurationPlanUnit pendingUnit, BuildingConfigurationUnitInput desiredUnit)
    {
        return string.Equals(pendingUnit.UnitType, desiredUnit.UnitType, StringComparison.Ordinal)
            && pendingUnit.GridX == desiredUnit.GridX
            && pendingUnit.GridY == desiredUnit.GridY
            && pendingUnit.LinkUp == desiredUnit.LinkUp
            && pendingUnit.LinkDown == desiredUnit.LinkDown
            && pendingUnit.LinkLeft == desiredUnit.LinkLeft
            && pendingUnit.LinkRight == desiredUnit.LinkRight
            && pendingUnit.LinkUpLeft == desiredUnit.LinkUpLeft
            && pendingUnit.LinkUpRight == desiredUnit.LinkUpRight
            && pendingUnit.LinkDownLeft == desiredUnit.LinkDownLeft
            && pendingUnit.LinkDownRight == desiredUnit.LinkDownRight
            && pendingUnit.ResourceTypeId == desiredUnit.ResourceTypeId
            && pendingUnit.ProductTypeId == desiredUnit.ProductTypeId
            && pendingUnit.MinPrice == desiredUnit.MinPrice
            && pendingUnit.MaxPrice == desiredUnit.MaxPrice
            && string.Equals(NormalizeString(pendingUnit.PurchaseSource), NormalizeString(desiredUnit.PurchaseSource), StringComparison.Ordinal)
            && string.Equals(NormalizeString(pendingUnit.SaleVisibility), NormalizeString(desiredUnit.SaleVisibility), StringComparison.Ordinal)
            && pendingUnit.Budget == desiredUnit.Budget
            && pendingUnit.MediaHouseBuildingId == desiredUnit.MediaHouseBuildingId
            && pendingUnit.MinQuality == desiredUnit.MinQuality
            && string.Equals(NormalizeString(pendingUnit.BrandScope), NormalizeString(desiredUnit.BrandScope), StringComparison.Ordinal)
            && pendingUnit.VendorLockCompanyId == desiredUnit.VendorLockCompanyId;
    }

    private static bool IsPending(BuildingConfigurationPlanUnit unit, long currentTick)
    {
        return unit.IsChanged && unit.AppliesAtTick > currentTick;
    }

    private static bool IsPending(BuildingConfigurationPlanRemoval removal, long currentTick)
    {
        return removal.AppliesAtTick > currentTick;
    }

    private static void RefreshPlanSummary(BuildingConfigurationPlan plan, long currentTick)
    {
        var remainingTicks = plan.Units
            .Where(unit => unit.IsChanged)
            .Select(unit => Math.Max(unit.AppliesAtTick - currentTick, 0L))
            .Concat(plan.Removals.Select(removal => Math.Max(removal.AppliesAtTick - currentTick, 0L)))
            .DefaultIfEmpty(0L)
            .Max();

        plan.AppliesAtTick = currentTick + remainingTicks;
        plan.TotalTicksRequired = (int)remainingTicks;
    }

    private static void ValidateUnits(string buildingType, IReadOnlyCollection<BuildingConfigurationUnitInput> submittedUnits)
    {
        if (submittedUnits.Count > 16)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("A building configuration can contain at most 16 units.")
                    .SetCode("TOO_MANY_BUILDING_UNITS")
                    .Build());
        }

        var allowedUnitTypes = GetAllowedUnitTypes(buildingType);
        var duplicatePositions = submittedUnits
            .GroupBy(unit => (unit.GridX, unit.GridY))
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicatePositions is not null)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Each building grid position can contain only one unit.")
                    .SetCode("DUPLICATE_BUILDING_UNIT_POSITION")
                    .Build());
        }

        var unitPositions = new HashSet<(int, int)>(submittedUnits.Select(u => (u.GridX, u.GridY)));

        foreach (var unit in submittedUnits)
        {
            if (unit.GridX < 0 || unit.GridX > 3 || unit.GridY < 0 || unit.GridY > 3)
            {
                throw new GraphQLException(
                    ErrorBuilder.New()
                        .SetMessage("Building unit positions must stay within the 4x4 grid.")
                        .SetCode("INVALID_BUILDING_UNIT_POSITION")
                        .Build());
            }

            if (!allowedUnitTypes.Contains(unit.UnitType))
            {
                throw new GraphQLException(
                    ErrorBuilder.New()
                        .SetMessage($"Unit type {unit.UnitType} is not allowed for building type {buildingType}.")
                        .SetCode("INVALID_BUILDING_UNIT_TYPE")
                        .Build());
            }

            // Validate directional link flags: each active link must point to a cell
            // that is within the 4x4 grid boundary and occupied by another unit in the plan.
            ValidateDirectionalLinks(unit, unitPositions);

            if (unit.UnitType == UnitType.ProductQuality && !unit.ProductTypeId.HasValue)
            {
                throw new GraphQLException(
                    ErrorBuilder.New()
                        .SetMessage("Product Quality units must target a product type.")
                        .SetCode("PRODUCT_QUALITY_PRODUCT_REQUIRED")
                        .Build());
            }

            if (unit.UnitType == UnitType.BrandQuality)
            {
                if (string.IsNullOrWhiteSpace(unit.BrandScope))
                {
                    throw new GraphQLException(
                        ErrorBuilder.New()
                            .SetMessage("Brand Quality units must define a brand scope.")
                            .SetCode("BRAND_QUALITY_SCOPE_REQUIRED")
                            .Build());
                }

                if (unit.BrandScope is not BrandScope.Company and not BrandScope.Category and not BrandScope.Product)
                {
                    throw new GraphQLException(
                        ErrorBuilder.New()
                            .SetMessage($"Unsupported brand scope {unit.BrandScope}.")
                            .SetCode("INVALID_BRAND_SCOPE")
                            .Build());
                }

                if (unit.BrandScope is BrandScope.Category or BrandScope.Product && !unit.ProductTypeId.HasValue)
                {
                    throw new GraphQLException(
                        ErrorBuilder.New()
                            .SetMessage("Brand Quality units researching a category or product must target a product type.")
                            .SetCode("BRAND_QUALITY_PRODUCT_REQUIRED")
                            .Build());
                }
            }
        }
    }

    /// <summary>
    /// Validates that each active directional link flag on a unit points to a cell
    /// within the 4x4 grid boundary and occupied by another unit in the submitted plan.
    /// Links are directional: a flag set on unit A means A sends resources toward that
    /// neighbor. The neighbor does not need a reciprocal flag for the link to be valid.
    /// </summary>
    private static void ValidateDirectionalLinks(
        BuildingConfigurationUnitInput unit,
        HashSet<(int, int)> unitPositions)
    {
        void CheckLink(bool flagActive, int targetX, int targetY, string direction)
        {
            if (!flagActive) return;

            if (targetX < 0 || targetX > 3 || targetY < 0 || targetY > 3)
            {
                throw new GraphQLException(
                    ErrorBuilder.New()
                        .SetMessage(
                            $"Unit at ({unit.GridX}, {unit.GridY}) has a {direction} link that points outside the 4x4 grid boundary.")
                        .SetCode("LINK_OUT_OF_BOUNDS")
                        .Build());
            }

            if (!unitPositions.Contains((targetX, targetY)))
            {
                throw new GraphQLException(
                    ErrorBuilder.New()
                        .SetMessage(
                            $"Unit at ({unit.GridX}, {unit.GridY}) has a {direction} link pointing to an empty cell at ({targetX}, {targetY}). Add a unit there or remove the link.")
                        .SetCode("LINK_TARGET_MISSING")
                        .Build());
            }
        }

        CheckLink(unit.LinkRight,     unit.GridX + 1, unit.GridY,     "right");
        CheckLink(unit.LinkLeft,      unit.GridX - 1, unit.GridY,     "left");
        CheckLink(unit.LinkDown,      unit.GridX,     unit.GridY + 1, "down");
        CheckLink(unit.LinkUp,        unit.GridX,     unit.GridY - 1, "up");
        CheckLink(unit.LinkDownRight, unit.GridX + 1, unit.GridY + 1, "down-right diagonal");
        CheckLink(unit.LinkDownLeft,  unit.GridX - 1, unit.GridY + 1, "down-left diagonal");
        CheckLink(unit.LinkUpRight,   unit.GridX + 1, unit.GridY - 1, "up-right diagonal");
        CheckLink(unit.LinkUpLeft,    unit.GridX - 1, unit.GridY - 1, "up-left diagonal");
    }
}
