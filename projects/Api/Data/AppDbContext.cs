using Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Api.Data;

/// <summary>
/// Entity Framework Core database context for the Capitalism game.
/// Manages all game entities including players, companies, buildings, resources, and products.
/// </summary>
public sealed partial class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    /// <summary>Registered game players.</summary>
    public DbSet<Player> Players => Set<Player>();

    /// <summary>Companies owned by players.</summary>
    public DbSet<Company> Companies => Set<Company>();

    /// <summary>Issued share positions held by players or companies.</summary>
    public DbSet<Shareholding> Shareholdings => Set<Shareholding>();

    /// <summary>Annual dividend settlement records.</summary>
    public DbSet<DividendPayment> DividendPayments => Set<DividendPayment>();

    /// <summary>Quoted share-price history recorded for the stock exchange.</summary>
    public DbSet<SharePriceHistoryEntry> SharePriceHistoryEntries => Set<SharePriceHistoryEntry>();

    /// <summary>Per-city salary settings selected by company owners.</summary>
    public DbSet<CompanyCitySalarySetting> CompanyCitySalarySettings => Set<CompanyCitySalarySetting>();

    /// <summary>Buildings placed on the game map.</summary>
    public DbSet<Building> Buildings => Set<Building>();

    /// <summary>Units within building 4x4 grids.</summary>
    public DbSet<BuildingUnit> BuildingUnits => Set<BuildingUnit>();

    /// <summary>Queued building configuration upgrades.</summary>
    public DbSet<BuildingConfigurationPlan> BuildingConfigurationPlans => Set<BuildingConfigurationPlan>();

    /// <summary>Queued unit snapshots for building configuration upgrades.</summary>
    public DbSet<BuildingConfigurationPlanUnit> BuildingConfigurationPlanUnits => Set<BuildingConfigurationPlanUnit>();

    /// <summary>Queued empty-slot transitions for building configuration upgrades.</summary>
    public DbSet<BuildingConfigurationPlanRemoval> BuildingConfigurationPlanRemovals => Set<BuildingConfigurationPlanRemoval>();

    /// <summary>Cities on the game map.</summary>
    public DbSet<City> Cities => Set<City>();

    /// <summary>Natural resources available near cities.</summary>
    public DbSet<CityResource> CityResources => Set<CityResource>();

    /// <summary>Raw material type definitions.</summary>
    public DbSet<ResourceType> ResourceTypes => Set<ResourceType>();

    /// <summary>Manufactured product type definitions.</summary>
    public DbSet<ProductType> ProductTypes => Set<ProductType>();

    /// <summary>Manufacturing recipes linking products to resources.</summary>
    public DbSet<ProductRecipe> ProductRecipes => Set<ProductRecipe>();

    /// <summary>Inventory stored in buildings.</summary>
    public DbSet<Inventory> Inventories => Set<Inventory>();

    /// <summary>Per-tick unit resource/product movement history.</summary>
    public DbSet<BuildingUnitResourceHistory> BuildingUnitResourceHistories => Set<BuildingUnitResourceHistory>();

    /// <summary>Product brands owned by companies.</summary>
    public DbSet<Brand> Brands => Set<Brand>();

    /// <summary>Global game state (singleton row).</summary>
    public DbSet<GameState> GameStates => Set<GameState>();

    /// <summary>Exchange buy/sell orders.</summary>
    public DbSet<ExchangeOrder> ExchangeOrders => Set<ExchangeOrder>();

    /// <summary>Purchasable building lots within cities.</summary>
    public DbSet<BuildingLot> BuildingLots => Set<BuildingLot>();

    /// <summary>Financial event records for company ledger.</summary>
    public DbSet<LedgerEntry> LedgerEntries => Set<LedgerEntry>();

    /// <summary>Per-tick public sales snapshots for analytics.</summary>
    public DbSet<PublicSalesRecord> PublicSalesRecords => Set<PublicSalesRecord>();

    /// <summary>Persisted market-trend state keyed by (city, item) pair.</summary>
    public DbSet<MarketTrendState> MarketTrendStates => Set<MarketTrendState>();

    /// <summary>Loan offers published by bank buildings.</summary>
    public DbSet<LoanOffer> LoanOffers => Set<LoanOffer>();

    /// <summary>Active and historical loans between companies.</summary>
    public DbSet<Loan> Loans => Set<Loan>();

    /// <summary>Audit trail for administrator actions performed while impersonating players.</summary>
    public DbSet<AdminActionAuditLog> AdminActionAuditLogs => Set<AdminActionAuditLog>();

    /// <summary>Shared in-game chat messages authored by players.</summary>
    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();

    /// <summary>Records stock-exchange buy/sell executions from the player's personal account.</summary>
    public DbSet<PersonTradeRecord> PersonTradeRecords => Set<PersonTradeRecord>();

    /// <inheritdoc/>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        ConfigureIdentityEntities(modelBuilder);
        ConfigureBuildingEntities(modelBuilder);
        ConfigureEconomyEntities(modelBuilder);
    }
}
