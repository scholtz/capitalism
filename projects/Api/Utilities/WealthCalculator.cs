using Api.Data.Entities;

namespace Api.Utilities;

/// <summary>
/// Provides deterministic wealth valuation helpers for the leaderboard ranking system.
///
/// Valuation strategy (interim — will evolve as the economy simulation matures):
///   Building values are fixed costs per type scaled by level, reflecting
///   construction investment rather than market fluctuation.  This produces
///   stable, auditable rankings today and can be replaced by a market-price
///   feed once the exchange has sufficient liquidity data.
///
///   Inventory values use each item's catalogue base price multiplied by
///   quantity in stock.  Quality and brand premiums are not yet factored in;
///   when those systems mature a quality multiplier can be layered on top
///   without breaking the overall formula.
/// </summary>
public static class WealthCalculator
{
    /// <summary>
    /// Fixed construction-cost base value per building type (in game currency).
    /// Multiply by building.Level to get total building value.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, decimal> BuildingBaseValues =
        new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
        {
            ["MINE"]                = 250_000m,
            ["FACTORY"]             = 200_000m,
            ["SALES_SHOP"]          = 150_000m,
            ["RESEARCH_DEVELOPMENT"]= 300_000m,
            ["APARTMENT"]           = 400_000m,
            ["COMMERCIAL"]          = 350_000m,
            ["MEDIA_HOUSE"]         = 500_000m,
            ["BANK"]                = 600_000m,
            ["EXCHANGE"]            = 450_000m,
            ["POWER_PLANT"]         = 350_000m,
        };

    /// <summary>
    /// Returns the estimated asset value for a single building.
    /// Returns 0 for unknown building types rather than throwing.
    /// </summary>
    public static decimal GetBuildingValue(Building building)
    {
        if (BuildingBaseValues.TryGetValue(building.Type, out var baseValue))
        {
            return baseValue * building.Level;
        }

        return 0m;
    }

    /// <summary>
    /// Returns the base price of an inventory item.
    /// Uses the resource type's base price for raw materials, or the product type's
    /// base price for manufactured goods.  Returns 0 if neither is populated.
    /// </summary>
    public static decimal GetItemBasePrice(Inventory inventory)
    {
        return inventory.ResourceType?.BasePrice
            ?? inventory.ProductType?.BasePrice
            ?? 0m;
    }
}
