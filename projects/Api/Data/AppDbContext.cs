using Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Api.Data;

/// <summary>
/// Entity Framework Core database context for the Capitalism game.
/// Manages all game entities including players, companies, buildings, resources, and products.
/// </summary>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    /// <summary>Registered game players.</summary>
    public DbSet<Player> Players => Set<Player>();

    /// <summary>Companies owned by players.</summary>
    public DbSet<Company> Companies => Set<Company>();

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

    /// <summary>Product brands owned by companies.</summary>
    public DbSet<Brand> Brands => Set<Brand>();

    /// <summary>Global game state (singleton row).</summary>
    public DbSet<GameState> GameStates => Set<GameState>();

    /// <summary>Exchange buy/sell orders.</summary>
    public DbSet<ExchangeOrder> ExchangeOrders => Set<ExchangeOrder>();

    /// <summary>Player-specific startup-pack offer lifecycle records.</summary>
    public DbSet<StartupPackOffer> StartupPackOffers => Set<StartupPackOffer>();

    /// <summary>Purchasable building lots within cities.</summary>
    public DbSet<BuildingLot> BuildingLots => Set<BuildingLot>();

    /// <inheritdoc/>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Player
        modelBuilder.Entity<Player>(e =>
        {
            e.HasKey(p => p.Id);
            e.HasIndex(p => p.Email).IsUnique();
            e.Property(p => p.Email).HasMaxLength(256);
            e.Property(p => p.DisplayName).HasMaxLength(100);
            e.Property(p => p.Role).HasMaxLength(20);
        });

        // StartupPackOffer
        modelBuilder.Entity<StartupPackOffer>(e =>
        {
            e.HasKey(offer => offer.Id);
            e.HasIndex(offer => offer.PlayerId).IsUnique();
            e.Property(offer => offer.OfferKey).HasMaxLength(50);
            e.Property(offer => offer.Status).HasMaxLength(20);
            e.Property(offer => offer.CompanyCashGrant).HasPrecision(18, 2);
            e.Property(offer => offer.ConcurrencyToken).IsConcurrencyToken();
            e.HasOne(offer => offer.Player)
                .WithOne()
                .HasForeignKey<StartupPackOffer>(offer => offer.PlayerId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Company>()
                .WithMany()
                .HasForeignKey(offer => offer.GrantedCompanyId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // Company
        modelBuilder.Entity<Company>(e =>
        {
            e.HasKey(c => c.Id);
            e.Property(c => c.Name).HasMaxLength(200);
            e.Property(c => c.Cash).HasPrecision(18, 2);
            e.HasOne(c => c.Player).WithMany(p => p.Companies).HasForeignKey(c => c.PlayerId);
        });

        // Building
        modelBuilder.Entity<Building>(e =>
        {
            e.HasKey(b => b.Id);
            e.Property(b => b.Type).HasMaxLength(30);
            e.Property(b => b.Name).HasMaxLength(200);
            e.Property(b => b.PowerConsumption).HasPrecision(18, 2);
            e.Property(b => b.AskingPrice).HasPrecision(18, 2);
            e.Property(b => b.PricePerSqm).HasPrecision(18, 2);
            e.Property(b => b.OccupancyPercent).HasPrecision(5, 2);
            e.Property(b => b.TotalAreaSqm).HasPrecision(18, 2);
            e.Property(b => b.PowerOutput).HasPrecision(18, 2);
            e.Property(b => b.InterestRate).HasPrecision(5, 2);
            e.HasOne(b => b.Company).WithMany(c => c.Buildings).HasForeignKey(b => b.CompanyId);
            e.HasOne(b => b.City).WithMany(c => c.Buildings).HasForeignKey(b => b.CityId);
            e.HasOne(b => b.PendingConfiguration)
                .WithOne(plan => plan.Building)
                .HasForeignKey<BuildingConfigurationPlan>(plan => plan.BuildingId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // BuildingUnit
        modelBuilder.Entity<BuildingUnit>(e =>
        {
            e.HasKey(u => u.Id);
            e.Property(u => u.UnitType).HasMaxLength(30);
            e.Property(u => u.PurchaseSource).HasMaxLength(20);
            e.Property(u => u.SaleVisibility).HasMaxLength(20);
            e.Property(u => u.BrandScope).HasMaxLength(20);
            e.Property(u => u.MinPrice).HasPrecision(18, 2);
            e.Property(u => u.MaxPrice).HasPrecision(18, 2);
            e.Property(u => u.Budget).HasPrecision(18, 2);
            e.Property(u => u.MinQuality).HasPrecision(5, 4);
            e.HasOne(u => u.Building).WithMany(b => b.Units).HasForeignKey(u => u.BuildingId);
        });

        // BuildingConfigurationPlan
        modelBuilder.Entity<BuildingConfigurationPlan>(e =>
        {
            e.HasKey(plan => plan.Id);
            e.HasOne(plan => plan.Building)
                .WithOne(building => building.PendingConfiguration)
                .HasForeignKey<BuildingConfigurationPlan>(plan => plan.BuildingId);
        });

        // BuildingConfigurationPlanUnit
        modelBuilder.Entity<BuildingConfigurationPlanUnit>(e =>
        {
            e.HasKey(unit => unit.Id);
            e.Property(unit => unit.UnitType).HasMaxLength(30);
            e.Property(unit => unit.PurchaseSource).HasMaxLength(20);
            e.Property(unit => unit.SaleVisibility).HasMaxLength(20);
            e.Property(unit => unit.BrandScope).HasMaxLength(20);
            e.Property(unit => unit.MinPrice).HasPrecision(18, 2);
            e.Property(unit => unit.MaxPrice).HasPrecision(18, 2);
            e.Property(unit => unit.Budget).HasPrecision(18, 2);
            e.Property(unit => unit.MinQuality).HasPrecision(5, 4);
            e.HasOne(unit => unit.BuildingConfigurationPlan)
                .WithMany(plan => plan.Units)
                .HasForeignKey(unit => unit.BuildingConfigurationPlanId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // BuildingConfigurationPlanRemoval
        modelBuilder.Entity<BuildingConfigurationPlanRemoval>(e =>
        {
            e.HasKey(removal => removal.Id);
            e.HasOne(removal => removal.BuildingConfigurationPlan)
                .WithMany(plan => plan.Removals)
                .HasForeignKey(removal => removal.BuildingConfigurationPlanId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // City
        modelBuilder.Entity<City>(e =>
        {
            e.HasKey(c => c.Id);
            e.Property(c => c.Name).HasMaxLength(200);
            e.Property(c => c.CountryCode).HasMaxLength(2);
            e.Property(c => c.AverageRentPerSqm).HasPrecision(18, 2);
        });

        // BuildingLot
        modelBuilder.Entity<BuildingLot>(e =>
        {
            e.HasKey(lot => lot.Id);
            e.Property(lot => lot.Name).HasMaxLength(200);
            e.Property(lot => lot.Description).HasMaxLength(500);
            e.Property(lot => lot.District).HasMaxLength(100);
            e.Property(lot => lot.Price).HasPrecision(18, 2);
            e.Property(lot => lot.SuitableTypes).HasMaxLength(200);
            e.Property(lot => lot.ConcurrencyToken).IsConcurrencyToken();
            e.HasOne(lot => lot.City).WithMany(c => c.Lots).HasForeignKey(lot => lot.CityId);
            e.HasOne(lot => lot.OwnerCompany).WithMany().HasForeignKey(lot => lot.OwnerCompanyId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(lot => lot.Building).WithMany().HasForeignKey(lot => lot.BuildingId).OnDelete(DeleteBehavior.SetNull);
        });

        // CityResource
        modelBuilder.Entity<CityResource>(e =>
        {
            e.HasKey(cr => cr.Id);
            e.Property(cr => cr.Abundance).HasPrecision(5, 4);
            e.HasOne(cr => cr.City).WithMany(c => c.Resources).HasForeignKey(cr => cr.CityId);
            e.HasOne(cr => cr.ResourceType).WithMany().HasForeignKey(cr => cr.ResourceTypeId);
        });

        // ResourceType
        modelBuilder.Entity<ResourceType>(e =>
        {
            e.HasKey(r => r.Id);
            e.HasIndex(r => r.Slug).IsUnique();
            e.Property(r => r.Name).HasMaxLength(100);
            e.Property(r => r.Slug).HasMaxLength(100);
            e.Property(r => r.Category).HasMaxLength(30);
            e.Property(r => r.BasePrice).HasPrecision(18, 2);
            e.Property(r => r.WeightPerUnit).HasPrecision(18, 4);
            e.Property(r => r.UnitName).HasMaxLength(50);
            e.Property(r => r.UnitSymbol).HasMaxLength(20);
            e.Property(r => r.ImageUrl).HasMaxLength(12000);
        });

        // ProductType
        modelBuilder.Entity<ProductType>(e =>
        {
            e.HasKey(p => p.Id);
            e.HasIndex(p => p.Slug).IsUnique();
            e.Property(p => p.Name).HasMaxLength(200);
            e.Property(p => p.Slug).HasMaxLength(200);
            e.Property(p => p.Industry).HasMaxLength(50);
            e.Property(p => p.BasePrice).HasPrecision(18, 2);
            e.Property(p => p.OutputQuantity).HasPrecision(18, 4);
            e.Property(p => p.EnergyConsumptionMwh).HasPrecision(18, 4);
            e.Property(p => p.UnitName).HasMaxLength(50);
            e.Property(p => p.UnitSymbol).HasMaxLength(20);
        });

        // ProductRecipe
        modelBuilder.Entity<ProductRecipe>(e =>
        {
            e.HasKey(r => r.Id);
            e.Property(r => r.Quantity).HasPrecision(18, 4);
            e.HasOne(r => r.ProductType).WithMany(p => p.Recipes).HasForeignKey(r => r.ProductTypeId);
            e.HasOne(r => r.ResourceType).WithMany().HasForeignKey(r => r.ResourceTypeId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(r => r.InputProductType).WithMany().HasForeignKey(r => r.InputProductTypeId).OnDelete(DeleteBehavior.Restrict);
        });

        // Inventory
        modelBuilder.Entity<Inventory>(e =>
        {
            e.HasKey(i => i.Id);
            e.Property(i => i.Quantity).HasPrecision(18, 4);
            e.Property(i => i.Quality).HasPrecision(5, 4);
            e.HasOne(i => i.Building).WithMany().HasForeignKey(i => i.BuildingId);
            e.HasOne(i => i.ResourceType).WithMany().HasForeignKey(i => i.ResourceTypeId);
            e.HasOne(i => i.ProductType).WithMany().HasForeignKey(i => i.ProductTypeId);
        });

        // Brand
        modelBuilder.Entity<Brand>(e =>
        {
            e.HasKey(b => b.Id);
            e.Property(b => b.Name).HasMaxLength(200);
            e.Property(b => b.Scope).HasMaxLength(20);
            e.Property(b => b.IndustryCategory).HasMaxLength(50);
            e.Property(b => b.Awareness).HasPrecision(5, 4);
            e.Property(b => b.Quality).HasPrecision(5, 4);
            e.HasOne(b => b.Company).WithMany().HasForeignKey(b => b.CompanyId);
        });

        // GameState (singleton)
        modelBuilder.Entity<GameState>(e =>
        {
            e.HasKey(g => g.Id);
            e.Property(g => g.TaxRate).HasPrecision(5, 2);
        });

        // ExchangeOrder
        modelBuilder.Entity<ExchangeOrder>(e =>
        {
            e.HasKey(o => o.Id);
            e.Property(o => o.Side).HasMaxLength(10);
            e.Property(o => o.PricePerUnit).HasPrecision(18, 2);
            e.Property(o => o.Quantity).HasPrecision(18, 4);
            e.Property(o => o.RemainingQuantity).HasPrecision(18, 4);
            e.Property(o => o.MinQuality).HasPrecision(5, 4);
            e.HasOne(o => o.ExchangeBuilding).WithMany().HasForeignKey(o => o.ExchangeBuildingId);
            e.HasOne(o => o.Company).WithMany().HasForeignKey(o => o.CompanyId);
        });
    }
}
