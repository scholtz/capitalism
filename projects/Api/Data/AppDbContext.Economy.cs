using Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Api.Data;

public sealed partial class AppDbContext
{
    private static void ConfigureEconomyEntities(ModelBuilder modelBuilder)
    {
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

        modelBuilder.Entity<ProductRecipe>(e =>
        {
            e.HasKey(r => r.Id);
            e.Property(r => r.Quantity).HasPrecision(18, 4);
            e.HasOne(r => r.ProductType).WithMany(p => p.Recipes).HasForeignKey(r => r.ProductTypeId);
            e.HasOne(r => r.ResourceType).WithMany().HasForeignKey(r => r.ResourceTypeId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(r => r.InputProductType).WithMany().HasForeignKey(r => r.InputProductTypeId).OnDelete(DeleteBehavior.Restrict);
        });

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

        modelBuilder.Entity<GameState>(e =>
        {
            e.HasKey(g => g.Id);
            e.Property(g => g.TaxRate).HasPrecision(5, 2);
        });

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

        modelBuilder.Entity<MarketTrendState>(e =>
        {
            e.HasKey(t => t.Id);
            e.Property(t => t.TrendFactor).HasPrecision(8, 4);
            e.HasIndex(t => new { t.CityId, t.ItemId }).IsUnique();
        });

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
