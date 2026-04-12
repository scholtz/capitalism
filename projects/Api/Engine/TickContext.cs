using Api.Data;
using Api.Data.Entities;

namespace Api.Engine;

/// <summary>
/// Holds all pre-loaded game data for a single tick, indexed for O(1) lookups.
/// Created by <see cref="TickProcessor"/> at the start of each tick.
/// </summary>
public sealed partial class TickContext
{
    private readonly Dictionary<(Guid BuildingUnitId, Guid? ResourceTypeId, Guid? ProductTypeId, long Tick), BuildingUnitResourceHistory> _unitResourceHistoryByKey = [];

    private static decimal RoundInventoryDecimal(decimal value) =>
        decimal.Round(value, 4, MidpointRounding.AwayFromZero);

    public required AppDbContext Db { get; init; }
    public required GameState GameState { get; init; }
    public long CurrentTick => GameState.CurrentTick;

    // ── Indexed game data ──

    public Dictionary<Guid, Building> BuildingsById { get; init; } = [];
    public Dictionary<string, List<Building>> BuildingsByType { get; init; } = [];
    public Dictionary<Guid, List<BuildingUnit>> UnitsByBuilding { get; init; } = [];
    public Dictionary<Guid, Dictionary<(int GridX, int GridY), BuildingUnit>> UnitsByBuildingPosition { get; init; } = [];
    public Dictionary<Guid, List<Inventory>> InventoryByUnit { get; init; } = [];
    public Dictionary<Guid, List<Inventory>> InventoryByBuilding { get; init; } = [];
    public Dictionary<Guid, Company> CompaniesById { get; init; } = [];
    public Dictionary<Guid, List<CompanyCitySalarySetting>> CitySalarySettingsByCompany { get; init; } = [];
    public Dictionary<Guid, City> CitiesById { get; init; } = [];
    public Dictionary<Guid, List<BuildingLot>> LotsByCompany { get; init; } = [];
    public Dictionary<Guid, List<CityResource>> ResourcesByCity { get; init; } = [];
    public Dictionary<Guid, ResourceType> ResourceTypesById { get; init; } = [];
    public Dictionary<Guid, ProductType> ProductTypesById { get; init; } = [];
    public Dictionary<Guid, List<ProductRecipe>> RecipesByProduct { get; init; } = [];
    public Dictionary<Guid, List<Brand>> BrandsByCompany { get; init; } = [];
    public List<ExchangeOrder> ActiveExchangeOrders { get; init; } = [];
    public Dictionary<Guid, decimal> TickStartRemainingQuantityByInventoryId { get; init; } = [];

    /// <summary>
    /// Total absolute LaborCost ledger amounts paid in each city over the past
    /// <see cref="GameConstants.RecentSalaryWindowTicks"/> ticks.
    /// Used by <see cref="Phases.PublicSalesPhase"/> to compute the dynamic
    /// salary purchasing-power factor (ROADMAP: "game currency collected by
    /// salaries in past 10 ticks").  Keyed by CityId.
    /// </summary>
    public Dictionary<Guid, decimal> RecentSalaryByCity { get; init; } = [];

    /// <summary>
    /// Persisted market-trend states keyed by <c>(CityId, ItemId)</c>.
    /// Pre-loaded by <see cref="TickProcessor"/> and mutated in-place by
    /// <see cref="Phases.PublicSalesPhase"/> so changes are saved in the same
    /// <c>SaveChangesAsync</c> call at the end of the tick.
    /// </summary>
    public Dictionary<(Guid CityId, Guid ItemId), MarketTrendState> TrendStatesByKey { get; init; } = [];

    public List<Inventory> NewInventory { get; } = [];
    public List<BuildingUnitResourceHistory> NewUnitResourceHistories { get; } = [];

    // ── Helpers ──

    /// <summary>
    /// Returns the operational efficiency factor for a building based on its PowerStatus.
    /// <list type="bullet">
    /// <item>POWERED → 1.0 (full capacity)</item>
    /// <item>CONSTRAINED → <see cref="GameConstants.ConstrainedEfficiencyFactor"/> (partial capacity)</item>
    /// <item>OFFLINE → 0.0 (completely stopped)</item>
    /// </list>
    /// </summary>
    public static decimal GetPowerEfficiency(Building building) => building.PowerStatus switch
    {
        Data.Entities.PowerStatus.Constrained => GameConstants.ConstrainedEfficiencyFactor,
        Data.Entities.PowerStatus.Offline     => 0m,
        _                                     => 1m
    };

}
