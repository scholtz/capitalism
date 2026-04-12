using Api.Data;
using Api.Data.Entities;
using Api.Types;
using HotChocolate;
using Microsoft.EntityFrameworkCore;

namespace Api.Utilities;

public static partial class BuildingConfigurationService
{
    /// <summary>
    /// Validates that every Manufacturing unit whose product type is specified does not
    /// conflict with any Purchase unit that has an explicitly configured resource.
    /// Specifically, if a Purchase unit supplies a resource R and a linked Manufacturing
    /// unit targets a product whose recipe does NOT include R, that is rejected with
    /// <c>RECIPE_INPUT_MISMATCH</c>. Purchase units with no resource configured are
    /// excluded from this check — an unconfigured purchase is incomplete but not invalid.
    /// </summary>
    private static async Task ValidateRecipeCompatibilityAsync(
        AppDbContext db,
        string buildingType,
        IReadOnlyCollection<BuildingConfigurationUnitInput> submittedUnits)
    {
        if (buildingType != BuildingType.Factory)
        {
            return;
        }

        var manufacturingUnits = submittedUnits
            .Where(u => u.UnitType == UnitType.Manufacturing && u.ProductTypeId.HasValue)
            .ToList();

        if (manufacturingUnits.Count == 0)
        {
            return;
        }

        var configuredPurchaseResourceIds = submittedUnits
            .Where(u => u.UnitType == UnitType.Purchase && u.ResourceTypeId.HasValue)
            .Select(u => u.ResourceTypeId!.Value)
            .ToHashSet();

        var configuredPurchaseProductIds = submittedUnits
            .Where(u => u.UnitType == UnitType.Purchase && u.ProductTypeId.HasValue)
            .Select(u => u.ProductTypeId!.Value)
            .ToHashSet();

        if (configuredPurchaseResourceIds.Count == 0 && configuredPurchaseProductIds.Count == 0)
        {
            return;
        }

        foreach (var mfgUnit in manufacturingUnits)
        {
            var productId = mfgUnit.ProductTypeId!.Value;

            var recipes = await db.ProductRecipes
                .Where(r => r.ProductTypeId == productId)
                .ToListAsync();

            if (recipes.Count == 0)
            {
                continue;
            }

            var anyPurchaseSuppliesRecipe = recipes.Any(recipe =>
                (recipe.ResourceTypeId.HasValue && configuredPurchaseResourceIds.Contains(recipe.ResourceTypeId.Value))
                || (recipe.InputProductTypeId.HasValue && configuredPurchaseProductIds.Contains(recipe.InputProductTypeId.Value)));

            if (!anyPurchaseSuppliesRecipe)
            {
                var product = await db.ProductTypes.FindAsync(productId);
                throw new GraphQLException(
                    ErrorBuilder.New()
                        .SetMessage(
                            $"The Manufacturing unit's product '{product?.Name ?? productId.ToString()}' requires an input that no configured Purchase unit in this plan supplies. " +
                            "Update the Purchase unit to supply a resource or product required by this product's recipe.")
                        .SetCode("RECIPE_INPUT_MISMATCH")
                        .Build());
            }
        }
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

        var unitByPosition = submittedUnits.ToDictionary(u => (u.GridX, u.GridY));

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

            ValidateDirectionalLinks(unit, unitByPosition);
            ValidateContradictoryLinks(unit, unitByPosition);

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

            if ((unit.UnitType == UnitType.PublicSales || unit.UnitType == UnitType.B2BSales) && unit.MinPrice.HasValue && unit.MinPrice <= 0)
            {
                throw new GraphQLException(
                    ErrorBuilder.New()
                        .SetMessage("Minimum price must be greater than zero.")
                        .SetCode("INVALID_MIN_PRICE")
                        .Build());
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
        Dictionary<(int, int), BuildingConfigurationUnitInput> unitByPosition)
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

            if (!unitByPosition.ContainsKey((targetX, targetY)))
            {
                throw new GraphQLException(
                    ErrorBuilder.New()
                        .SetMessage(
                            $"Unit at ({unit.GridX}, {unit.GridY}) has a {direction} link pointing to an empty cell at ({targetX}, {targetY}). Add a unit there or remove the link.")
                        .SetCode("LINK_TARGET_MISSING")
                        .Build());
            }
        }

        CheckLink(unit.LinkRight, unit.GridX + 1, unit.GridY, "right");
        CheckLink(unit.LinkLeft, unit.GridX - 1, unit.GridY, "left");
        CheckLink(unit.LinkDown, unit.GridX, unit.GridY + 1, "down");
        CheckLink(unit.LinkUp, unit.GridX, unit.GridY - 1, "up");
        CheckLink(unit.LinkDownRight, unit.GridX + 1, unit.GridY + 1, "down-right diagonal");
        CheckLink(unit.LinkDownLeft, unit.GridX - 1, unit.GridY + 1, "down-left diagonal");
        CheckLink(unit.LinkUpRight, unit.GridX + 1, unit.GridY - 1, "up-right diagonal");
        CheckLink(unit.LinkUpLeft, unit.GridX - 1, unit.GridY - 1, "up-left diagonal");
    }

    /// <summary>
    /// Validates that no pair of units forms a contradictory bidirectional link.
    /// Per product rules, a link between two units can only flow in one direction.
    /// </summary>
    private static void ValidateContradictoryLinks(
        BuildingConfigurationUnitInput unit,
        Dictionary<(int, int), BuildingConfigurationUnitInput> unitByPosition)
    {
        void CheckPair(bool outgoingFlag, int neighborX, int neighborY, Func<BuildingConfigurationUnitInput, bool> incomingFlag, string axis)
        {
            if (!outgoingFlag) return;
            if (!unitByPosition.TryGetValue((neighborX, neighborY), out var neighbor)) return;
            if (!incomingFlag(neighbor)) return;

            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage(
                        $"Contradictory bidirectional {axis} link between units at ({unit.GridX}, {unit.GridY}) and ({neighborX}, {neighborY}). A link can only flow in one direction between the same pair of units.")
                    .SetCode("CONTRADICTORY_LINK")
                    .Build());
        }

        CheckPair(unit.LinkRight, unit.GridX + 1, unit.GridY, n => n.LinkLeft, "horizontal");
        CheckPair(unit.LinkDown, unit.GridX, unit.GridY + 1, n => n.LinkUp, "vertical");
        CheckPair(unit.LinkDownRight, unit.GridX + 1, unit.GridY + 1, n => n.LinkUpLeft, "diagonal (↘/↖)");
        CheckPair(unit.LinkDownLeft, unit.GridX - 1, unit.GridY + 1, n => n.LinkUpRight, "diagonal (↙/↗)");
    }
}
