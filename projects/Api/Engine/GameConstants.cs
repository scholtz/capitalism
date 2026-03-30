using Api.Data.Entities;

namespace Api.Engine;

/// <summary>
/// Game balance constants for the tick-based economy simulation.
/// All capacity, rate, and demand values are tunable here.
/// </summary>
public static class GameConstants
{
    /// <summary>Maximum storage capacity (units) per unit level.</summary>
    public static decimal StorageCapacity(int level) => level switch
    {
        1 => 100m,
        2 => 250m,
        3 => 500m,
        4 => 1000m,
        _ => 100m * (decimal)Math.Pow(2, Math.Max(level - 1, 0))
    };

    /// <summary>Mining production per tick per unit level (multiplied by city abundance).</summary>
    public static decimal MiningRate(int level) => level switch
    {
        1 => 10m,
        2 => 25m,
        3 => 50m,
        4 => 100m,
        _ => 10m * (decimal)Math.Pow(2, Math.Max(level - 1, 0))
    };

    /// <summary>Manufacturing batches per tick per unit level.</summary>
    public static int ManufacturingBatches(int level) => level switch
    {
        1 => 1,
        2 => 2,
        3 => 4,
        4 => 8,
        _ => (int)Math.Pow(2, Math.Max(level - 1, 0))
    };

    /// <summary>Public sales capacity (units sold) per tick per unit level.</summary>
    public static decimal SalesCapacity(int level) => level switch
    {
        1 => 20m,
        2 => 50m,
        3 => 100m,
        4 => 200m,
        _ => 20m * (decimal)Math.Pow(2, Math.Max(level - 1, 0))
    };

    /// <summary>Purchase capacity (units bought) per tick per unit level.</summary>
    public static decimal PurchaseCapacity(int level) => level switch
    {
        1 => 50m,
        2 => 100m,
        3 => 200m,
        4 => 400m,
        _ => 50m * (decimal)Math.Pow(2, Math.Max(level - 1, 0))
    };

    /// <summary>Base demand per capita per product per tick.</summary>
    public const decimal BaseDemandPerCapita = 0.001m;

    /// <summary>Minimum number of purchasable lands maintained per building type and city.</summary>
    public const int MinimumAvailableLotsPerBuildingType = 10;

    /// <summary>Brand awareness increment per unit of marketing budget spent.</summary>
    public const decimal BrandAwarenessPerBudget = 0.0001m;

    /// <summary>R&D quality increment per tick per unit level.</summary>
    public static decimal ResearchQualityRate(int level) => level switch
    {
        1 => 0.001m,
        2 => 0.002m,
        3 => 0.004m,
        4 => 0.008m,
        _ => 0.001m * (decimal)Math.Pow(2, Math.Max(level - 1, 0))
    };

    /// <summary>Rate at which occupancy adjusts per tick toward equilibrium.</summary>
    public const decimal OccupancyAdjustmentRate = 0.5m;

    // ── Power grid constants ──────────────────────────────────────────────────

    /// <summary>
    /// Power demand in MW for a building of the given type and level.
    /// Power plants themselves consume no power (they produce it).
    /// </summary>
    public static decimal PowerDemandMw(string buildingType, int level) => buildingType switch
    {
        Data.Entities.BuildingType.Mine               => 2m * level,
        Data.Entities.BuildingType.Factory            => 5m * level,
        Data.Entities.BuildingType.SalesShop          => 1m * level,
        Data.Entities.BuildingType.ResearchDevelopment => 3m * level,
        Data.Entities.BuildingType.Apartment          => 0.5m * level,
        Data.Entities.BuildingType.Commercial         => 1m * level,
        Data.Entities.BuildingType.MediaHouse         => 2m * level,
        Data.Entities.BuildingType.Bank               => 1m * level,
        Data.Entities.BuildingType.Exchange           => 1m * level,
        Data.Entities.BuildingType.PowerPlant         => 0m,
        _                                             => 1m * level
    };

    /// <summary>
    /// Default power output in MW for a power plant by plant type.
    /// Used when a power plant building is created without an explicit output.
    /// </summary>
    public static decimal DefaultPowerOutputMw(string? powerPlantType) => powerPlantType switch
    {
        PowerPlantType.Coal    => 50m,
        PowerPlantType.Gas     => 40m,
        PowerPlantType.Solar   => 20m,
        PowerPlantType.Wind    => 25m,
        PowerPlantType.Nuclear => 200m,
        _                      => 30m
    };

    /// <summary>
    /// Fraction of capacity a CONSTRAINED building operates at.
    /// Applied to manufacturing batches, mining rate, and sales capacity.
    /// </summary>
    public const decimal ConstrainedEfficiencyFactor = 0.5m;
}
