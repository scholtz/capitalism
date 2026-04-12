using Api.Data.Entities;
using Api.Types;

namespace Api.Utilities;

public static partial class BuildingConfigurationService
{
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
            LockedCityId = (input.PurchaseSource == "EXCHANGE") ? input.LockedCityId : null,
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
            LockedCityId = unit.LockedCityId,
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
            || currentUnit.VendorLockCompanyId != input.VendorLockCompanyId
            || currentUnit.LockedCityId != input.LockedCityId)
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
            && currentUnit.VendorLockCompanyId == desiredUnit.VendorLockCompanyId
            && currentUnit.LockedCityId == desiredUnit.LockedCityId;
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
            && pendingUnit.VendorLockCompanyId == desiredUnit.VendorLockCompanyId
            && pendingUnit.LockedCityId == desiredUnit.LockedCityId;
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
}
