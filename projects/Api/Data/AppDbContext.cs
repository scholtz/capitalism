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
            e.Property(p => p.PersonalCash).HasPrecision(18, 2);
            e.Property(p => p.ActiveAccountType).HasMaxLength(20);
            e.Property(p => p.OnboardingCurrentStep).HasMaxLength(40);
            e.Property(p => p.OnboardingIndustry).HasMaxLength(50);
            e.Property(p => p.ConcurrencyToken).IsConcurrencyToken();
        });

        // Company
        modelBuilder.Entity<Company>(e =>
        {
            e.HasKey(c => c.Id);
            e.Property(c => c.Name).HasMaxLength(200);
            e.Property(c => c.Cash).HasPrecision(18, 2);
            e.Property(c => c.TotalSharesIssued).HasPrecision(18, 4);
            e.Property(c => c.DividendPayoutRatio).HasPrecision(8, 4);
            e.HasOne(c => c.Player).WithMany(p => p.Companies).HasForeignKey(c => c.PlayerId);
            e.HasMany(c => c.CitySalarySettings)
                .WithOne(setting => setting.Company)
                .HasForeignKey(setting => setting.CompanyId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasMany(c => c.Shareholdings)
                .WithOne(holding => holding.Company)
                .HasForeignKey(holding => holding.CompanyId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasMany(c => c.DividendPayments)
                .WithOne(payment => payment.Company)
                .HasForeignKey(payment => payment.CompanyId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Shareholding
        modelBuilder.Entity<Shareholding>(e =>
        {
            e.HasKey(holding => holding.Id);
            e.Property(holding => holding.ShareCount).HasPrecision(18, 4);
            e.HasOne(holding => holding.OwnerPlayer)
                .WithMany(player => player.Shareholdings)
                .HasForeignKey(holding => holding.OwnerPlayerId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(holding => holding.OwnerCompany)
                .WithMany()
                .HasForeignKey(holding => holding.OwnerCompanyId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(holding => new { holding.CompanyId, holding.OwnerPlayerId });
            e.HasIndex(holding => new { holding.CompanyId, holding.OwnerCompanyId });
        });

        // DividendPayment
        modelBuilder.Entity<DividendPayment>(e =>
        {
            e.HasKey(payment => payment.Id);
            e.Property(payment => payment.ShareCount).HasPrecision(18, 4);
            e.Property(payment => payment.AmountPerShare).HasPrecision(18, 4);
            e.Property(payment => payment.TotalAmount).HasPrecision(18, 4);
            e.Property(payment => payment.Description).HasMaxLength(200);
            e.HasOne(payment => payment.RecipientPlayer)
                .WithMany(player => player.DividendPayments)
                .HasForeignKey(payment => payment.RecipientPlayerId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(payment => payment.RecipientCompany)
                .WithMany()
                .HasForeignKey(payment => payment.RecipientCompanyId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(payment => new { payment.CompanyId, payment.GameYear });
            e.HasIndex(payment => new { payment.RecipientPlayerId, payment.RecordedAtTick });
        });

        // SharePriceHistoryEntry
        modelBuilder.Entity<SharePriceHistoryEntry>(e =>
        {
            e.HasKey(entry => entry.Id);
            e.Property(entry => entry.SharePrice).HasPrecision(18, 4);
            e.HasOne(entry => entry.Company)
                .WithMany()
                .HasForeignKey(entry => entry.CompanyId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(entry => new { entry.CompanyId, entry.RecordedAtTick, entry.RecordedAtUtc });
        });

        // CompanyCitySalarySetting
        modelBuilder.Entity<CompanyCitySalarySetting>(e =>
        {
            e.HasKey(setting => setting.Id);
            e.HasIndex(setting => new { setting.CompanyId, setting.CityId }).IsUnique();
            e.Property(setting => setting.SalaryMultiplier).HasPrecision(8, 4);
            e.HasOne(setting => setting.City)
                .WithMany()
                .HasForeignKey(setting => setting.CityId)
                .OnDelete(DeleteBehavior.Cascade);
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
            e.Property(b => b.PowerStatus).HasMaxLength(20);
            e.Property(b => b.InterestRate).HasPrecision(5, 2);
            e.Property(b => b.ConstructionCost).HasPrecision(18, 2);
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
            e.Property(c => c.BaseSalaryPerManhour).HasPrecision(18, 4);
        });

        // BuildingLot
        modelBuilder.Entity<BuildingLot>(e =>
        {
            e.HasKey(lot => lot.Id);
            e.Property(lot => lot.Name).HasMaxLength(200);
            e.Property(lot => lot.Description).HasMaxLength(500);
            e.Property(lot => lot.District).HasMaxLength(100);
            e.Property(lot => lot.PopulationIndex).HasPrecision(9, 4);
            e.Property(lot => lot.BasePrice).HasPrecision(18, 2);
            e.Property(lot => lot.Price).HasPrecision(18, 2);
            e.Property(lot => lot.SuitableTypes).HasMaxLength(200);
            e.Property(lot => lot.MaterialQuality).HasPrecision(5, 4);
            e.Property(lot => lot.MaterialQuantity).HasPrecision(18, 2);
            e.Property(lot => lot.ConcurrencyToken).IsConcurrencyToken();
            e.HasOne(lot => lot.City).WithMany(c => c.Lots).HasForeignKey(lot => lot.CityId);
            e.HasOne(lot => lot.OwnerCompany).WithMany().HasForeignKey(lot => lot.OwnerCompanyId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(lot => lot.Building).WithMany().HasForeignKey(lot => lot.BuildingId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(lot => lot.ResourceType).WithMany().HasForeignKey(lot => lot.ResourceTypeId).OnDelete(DeleteBehavior.SetNull);
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
            e.Property(p => p.PriceElasticity).HasPrecision(5, 4);
            e.Property(p => p.OutputQuantity).HasPrecision(18, 4);
            e.Property(p => p.EnergyConsumptionMwh).HasPrecision(18, 4);
            e.Property(p => p.BasicLaborHours).HasPrecision(18, 4);
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
            e.Property(i => i.SourcingCostTotal).HasPrecision(18, 4);
            e.Property(i => i.Quality).HasPrecision(5, 4);
            e.HasOne(i => i.Building).WithMany().HasForeignKey(i => i.BuildingId);
            e.HasOne(i => i.BuildingUnit).WithMany().HasForeignKey(i => i.BuildingUnitId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(i => i.ResourceType).WithMany().HasForeignKey(i => i.ResourceTypeId);
            e.HasOne(i => i.ProductType).WithMany().HasForeignKey(i => i.ProductTypeId);
        });

        // BuildingUnitResourceHistory
        modelBuilder.Entity<BuildingUnitResourceHistory>(e =>
        {
            e.HasKey(history => history.Id);
            e.Property(history => history.InflowQuantity).HasPrecision(18, 4);
            e.Property(history => history.OutflowQuantity).HasPrecision(18, 4);
            e.Property(history => history.ConsumedQuantity).HasPrecision(18, 4);
            e.Property(history => history.ProducedQuantity).HasPrecision(18, 4);
            e.HasOne(history => history.Building)
                .WithMany()
                .HasForeignKey(history => history.BuildingId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(history => history.BuildingUnit)
                .WithMany()
                .HasForeignKey(history => history.BuildingUnitId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(history => history.ResourceType)
                .WithMany()
                .HasForeignKey(history => history.ResourceTypeId)
                .OnDelete(DeleteBehavior.SetNull);
            e.HasOne(history => history.ProductType)
                .WithMany()
                .HasForeignKey(history => history.ProductTypeId)
                .OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(history => new { history.BuildingId, history.Tick });
            e.HasIndex(history => new { history.BuildingUnitId, history.Tick, history.ResourceTypeId, history.ProductTypeId })
                .IsUnique();
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
            e.Property(b => b.MarketingEfficiencyMultiplier).HasPrecision(7, 4).HasDefaultValue(1m);
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

        // LedgerEntry
        modelBuilder.Entity<LedgerEntry>(e =>
        {
            e.HasKey(l => l.Id);
            e.Property(l => l.Category).HasMaxLength(40);
            e.Property(l => l.Description).HasMaxLength(500);
            e.Property(l => l.Amount).HasPrecision(18, 4);
            e.HasOne(l => l.Company).WithMany().HasForeignKey(l => l.CompanyId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(l => l.Building).WithMany().HasForeignKey(l => l.BuildingId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(l => l.BuildingUnit).WithMany().HasForeignKey(l => l.BuildingUnitId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(l => l.ProductType).WithMany().HasForeignKey(l => l.ProductTypeId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(l => l.ResourceType).WithMany().HasForeignKey(l => l.ResourceTypeId).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(l => new { l.CompanyId, l.RecordedAtTick });
        });

        // PublicSalesRecord
        modelBuilder.Entity<PublicSalesRecord>(e =>
        {
            e.HasKey(r => r.Id);
            e.Property(r => r.QuantitySold).HasPrecision(18, 4);
            e.Property(r => r.PricePerUnit).HasPrecision(18, 4);
            e.Property(r => r.Revenue).HasPrecision(18, 4);
            e.Property(r => r.Demand).HasPrecision(18, 4);
            e.Property(r => r.SalesCapacity).HasPrecision(18, 4);
            e.Property(r => r.TrendFactor).HasPrecision(8, 4);
            e.HasOne(r => r.BuildingUnit).WithMany().HasForeignKey(r => r.BuildingUnitId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(r => r.Building).WithMany().HasForeignKey(r => r.BuildingId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(r => r.Company).WithMany().HasForeignKey(r => r.CompanyId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(r => r.City).WithMany().HasForeignKey(r => r.CityId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(r => r.ProductType).WithMany().HasForeignKey(r => r.ProductTypeId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(r => r.ResourceType).WithMany().HasForeignKey(r => r.ResourceTypeId).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(r => new { r.BuildingUnitId, r.Tick });
            e.HasIndex(r => new { r.CompanyId, r.Tick });
        });

        // MarketTrendState
        modelBuilder.Entity<MarketTrendState>(e =>
        {
            e.HasKey(t => t.Id);
            e.Property(t => t.TrendFactor).HasPrecision(8, 4);
            // Unique constraint: one trend state per (city, item) pair.
            e.HasIndex(t => new { t.CityId, t.ItemId }).IsUnique();
        });

        // LoanOffer
        modelBuilder.Entity<LoanOffer>(e =>
        {
            e.HasKey(o => o.Id);
            e.Property(o => o.AnnualInterestRatePercent).HasPrecision(8, 4);
            e.Property(o => o.MaxPrincipalPerLoan).HasPrecision(18, 2);
            e.Property(o => o.TotalCapacity).HasPrecision(18, 2);
            e.Property(o => o.UsedCapacity).HasPrecision(18, 2);
            e.HasOne(o => o.BankBuilding).WithMany().HasForeignKey(o => o.BankBuildingId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(o => o.LenderCompany).WithMany().HasForeignKey(o => o.LenderCompanyId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(o => new { o.LenderCompanyId, o.IsActive });
        });

        // Loan
        modelBuilder.Entity<Loan>(e =>
        {
            e.HasKey(l => l.Id);
            e.Property(l => l.OriginalPrincipal).HasPrecision(18, 2);
            e.Property(l => l.RemainingPrincipal).HasPrecision(18, 4);
            e.Property(l => l.AnnualInterestRatePercent).HasPrecision(8, 4);
            e.Property(l => l.PaymentAmount).HasPrecision(18, 4);
            e.Property(l => l.AccumulatedPenalty).HasPrecision(18, 4);
            e.Property(l => l.Status).HasMaxLength(20);
            e.HasOne(l => l.LoanOffer).WithMany(o => o.Loans).HasForeignKey(l => l.LoanOfferId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(l => l.BorrowerCompany).WithMany().HasForeignKey(l => l.BorrowerCompanyId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(l => l.BankBuilding).WithMany().HasForeignKey(l => l.BankBuildingId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(l => l.LenderCompany).WithMany().HasForeignKey(l => l.LenderCompanyId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(l => new { l.BorrowerCompanyId, l.Status });
            e.HasIndex(l => new { l.LenderCompanyId, l.Status });
            e.HasIndex(l => l.NextPaymentTick);
        });

        // AdminActionAuditLog
        modelBuilder.Entity<AdminActionAuditLog>(e =>
        {
            e.HasKey(log => log.Id);
            e.Property(log => log.AdminActorEmail).HasMaxLength(256);
            e.Property(log => log.AdminActorDisplayName).HasMaxLength(100);
            e.Property(log => log.EffectivePlayerEmail).HasMaxLength(256);
            e.Property(log => log.EffectivePlayerDisplayName).HasMaxLength(100);
            e.Property(log => log.EffectiveAccountType).HasMaxLength(20);
            e.Property(log => log.EffectiveCompanyName).HasMaxLength(200);
            e.Property(log => log.GraphQlOperationName).HasMaxLength(160);
            e.Property(log => log.MutationSummary).HasMaxLength(500);
            e.HasIndex(log => log.RecordedAtUtc);
            e.HasIndex(log => log.AdminActorPlayerId);
            e.HasIndex(log => log.EffectivePlayerId);
        });
    }
}
