using Api.Data.Entities;

namespace Api.Engine;

/// <summary>
/// Game balance constants for the tick-based economy simulation.
/// All capacity, rate, and demand values are tunable here.
/// </summary>
public static class GameConstants
{
    public const int GameStartYear = 2000;
    public const int TicksPerDay = 24;
    public const int DaysPerYear = 365;
    public const int TicksPerYear = TicksPerDay * DaysPerYear;

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

    /// <summary>
    /// Reference city salary used to normalise purchasing-power demand.
    /// Cities with a <see cref="Api.Data.Entities.City.BaseSalaryPerManhour"/> equal to this
    /// value produce a purchasing-power factor of 1.0 (no boost or penalty).
    /// Higher-wage cities attract proportionally more consumer spending.
    /// </summary>
    public const decimal ReferenceSalaryPerManhour = 20m;

    /// <summary>
    /// Number of past ticks included in the "recent salary" window used to
    /// compute dynamic purchasing-power demand (ROADMAP: "game currency
    /// collected by salaries in past 10 ticks").
    /// </summary>
    public const int RecentSalaryWindowTicks = 10;

    /// <summary>
    /// Expected fraction of a city's population that generates LaborCost
    /// ledger entries per tick through player-owned companies.  Used to
    /// normalise the dynamic salary spending signal into a [0.5, 2.0]
    /// purchasing-power factor.  0.001 = 0.1 % of population employed.
    /// </summary>
    public const decimal ExpectedSalaryParticipationRate = 0.001m;

    /// <summary>Standard cost of electricity used by unit operations and manufacturing.</summary>
    public const decimal EnergyPricePerMwh = 55m;

    /// <summary>Minimum number of purchasable lands maintained per building type and city.</summary>
    public const int MinimumAvailableLotsPerBuildingType = 10;

    /// <summary>Brand awareness increment per unit of marketing budget spent.</summary>
    public const decimal BrandAwarenessPerBudget = 0.0001m;

    /// <summary>
    /// Maximum marketing efficiency multiplier achievable through BRAND_QUALITY R&amp;D.
    /// At full research saturation the company's marketing budget is this many times more effective.
    /// </summary>
    public const decimal MaxMarketingEfficiencyMultiplier = 2m;

    /// <summary>R&amp;D efficiency multiplier increment per tick per unit level (for BRAND_QUALITY research).</summary>
    public static decimal ResearchEfficiencyRate(int level) => level switch
    {
        1 => 0.0005m,
        2 => 0.001m,
        3 => 0.002m,
        4 => 0.004m,
        _ => 0.0005m * (decimal)Math.Pow(2, Math.Max(level - 1, 0))
    };

    /// <summary>R&amp;D quality increment per tick per unit level (for PRODUCT_QUALITY research).</summary>
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

    // ── Unit upgrade constants ────────────────────────────────────────────────

    /// <summary>Maximum upgrade level for building units.</summary>
    public const int MaxUnitLevel = 4;

    /// <summary>
    /// Ticks required to upgrade a unit from <paramref name="currentLevel"/> to the next level.
    /// Level 1→2 = 10 ticks, 2→3 = 100 ticks, 3→4 = 1000 ticks (roadmap spec).
    /// </summary>
    public static int UnitUpgradeTicks(int currentLevel) => currentLevel switch
    {
        1 => 10,
        2 => 100,
        3 => 1000,
        _ => 10 * (int)Math.Pow(10, Math.Max(currentLevel - 1, 0))
    };

    /// <summary>
    /// Cash cost to upgrade a unit of the given type from <paramref name="currentLevel"/> to the next level.
    /// Costs scale with unit type strategic importance and level.
    /// </summary>
    public static decimal UnitUpgradeCost(string unitType, int currentLevel)
    {
        decimal baseCost = unitType switch
        {
            Data.Entities.UnitType.Mining         => 5_000m,
            Data.Entities.UnitType.Storage        => 2_000m,
            Data.Entities.UnitType.Manufacturing  => 8_000m,
            Data.Entities.UnitType.Purchase       => 3_000m,
            Data.Entities.UnitType.PublicSales    => 6_000m,
            Data.Entities.UnitType.B2BSales       => 4_000m,
            Data.Entities.UnitType.ProductQuality => 10_000m,
            Data.Entities.UnitType.BrandQuality   => 10_000m,
            Data.Entities.UnitType.Marketing      => 5_000m,
            Data.Entities.UnitType.Branding       => 5_000m,
            _                                     => 4_000m
        };
        // Each subsequent level costs 5× more: L1→L2 = base, L2→L3 = 5×base, L3→L4 = 25×base
        return baseCost * (decimal)Math.Pow(5, Math.Max(currentLevel - 1, 0));
    }

    /// <summary>Returns true when the given unit type supports level upgrades.</summary>
    public static bool IsUpgradableUnitType(string unitType) => unitType switch
    {
        Data.Entities.UnitType.Mining         => true,
        Data.Entities.UnitType.Storage        => true,
        Data.Entities.UnitType.Manufacturing  => true,
        Data.Entities.UnitType.Purchase       => true,
        Data.Entities.UnitType.PublicSales    => true,
        Data.Entities.UnitType.B2BSales       => true,
        Data.Entities.UnitType.ProductQuality => true,
        Data.Entities.UnitType.BrandQuality   => true,
        _                                     => false
    };

    /// <summary>Returns the primary stat value for a unit type at the given level (for UI display).</summary>
    public static decimal GetUnitStat(string unitType, int level) => unitType switch
    {
        Data.Entities.UnitType.Mining         => MiningRate(level),
        Data.Entities.UnitType.Storage        => StorageCapacity(level),
        Data.Entities.UnitType.Manufacturing  => ManufacturingBatches(level),
        Data.Entities.UnitType.Purchase       => PurchaseCapacity(level),
        Data.Entities.UnitType.PublicSales    => SalesCapacity(level),
        Data.Entities.UnitType.B2BSales       => SalesCapacity(level),
        Data.Entities.UnitType.ProductQuality => ResearchQualityRate(level) * 1000m,
        Data.Entities.UnitType.BrandQuality   => ResearchEfficiencyRate(level) * 1000m,
        _                                     => (decimal)level
    };

    /// <summary>Returns a human-readable label for the primary stat of the given unit type.</summary>
    public static string GetUnitStatLabel(string unitType) => unitType switch
    {
        Data.Entities.UnitType.Mining         => "units/tick",
        Data.Entities.UnitType.Storage        => "capacity",
        Data.Entities.UnitType.Manufacturing  => "batches/tick",
        Data.Entities.UnitType.Purchase       => "units/tick",
        Data.Entities.UnitType.PublicSales    => "units/tick",
        Data.Entities.UnitType.B2BSales       => "units/tick",
        Data.Entities.UnitType.ProductQuality => "quality‰/tick",
        Data.Entities.UnitType.BrandQuality   => "efficiency‰/tick",
        _                                     => "level"
    };

    // ── Construction constants ────────────────────────────────────────────────

    /// <summary>
    /// Number of ticks required to construct a building of the given type.
    /// One tick = one hour; 24 ticks = one in-game day.
    /// </summary>
    public static int ConstructionTicks(string buildingType) => buildingType switch
    {
        Data.Entities.BuildingType.Mine               => 24,   // 1 day
        Data.Entities.BuildingType.Factory            => 48,   // 2 days
        Data.Entities.BuildingType.SalesShop          => 24,   // 1 day
        Data.Entities.BuildingType.ResearchDevelopment => 72,  // 3 days
        Data.Entities.BuildingType.Apartment          => 96,   // 4 days
        Data.Entities.BuildingType.Commercial         => 48,   // 2 days
        Data.Entities.BuildingType.MediaHouse         => 48,   // 2 days
        Data.Entities.BuildingType.Bank               => 72,   // 3 days
        Data.Entities.BuildingType.Exchange           => 96,   // 4 days
        Data.Entities.BuildingType.PowerPlant         => 120,  // 5 days
        _                                             => 24
    };

    /// <summary>
    /// Construction cost (in game currency) for a building of the given type.
    /// This is charged in addition to the land purchase price.
    /// </summary>
    public static decimal ConstructionCost(string buildingType) => buildingType switch
    {
        Data.Entities.BuildingType.Mine               => 5_000m,
        Data.Entities.BuildingType.Factory            => 15_000m,
        Data.Entities.BuildingType.SalesShop          => 8_000m,
        Data.Entities.BuildingType.ResearchDevelopment => 25_000m,
        Data.Entities.BuildingType.Apartment          => 40_000m,
        Data.Entities.BuildingType.Commercial         => 20_000m,
        Data.Entities.BuildingType.MediaHouse         => 30_000m,
        Data.Entities.BuildingType.Bank               => 50_000m,
        Data.Entities.BuildingType.Exchange           => 60_000m,
        Data.Entities.BuildingType.PowerPlant         => 80_000m,
        _                                             => 10_000m
    };
}
