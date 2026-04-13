using Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Api.Data;

public sealed partial class AppDbContext
{
    private static void ConfigureBuildingEntities(ModelBuilder modelBuilder)
    {
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
            e.HasIndex(u => u.BuildingId);
        });

        modelBuilder.Entity<BuildingConfigurationPlan>(e =>
        {
            e.HasKey(plan => plan.Id);
            e.HasOne(plan => plan.Building)
                .WithOne(building => building.PendingConfiguration)
                .HasForeignKey<BuildingConfigurationPlan>(plan => plan.BuildingId);
        });

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

        modelBuilder.Entity<BuildingConfigurationPlanRemoval>(e =>
        {
            e.HasKey(removal => removal.Id);
            e.HasOne(removal => removal.BuildingConfigurationPlan)
                .WithMany(plan => plan.Removals)
                .HasForeignKey(removal => removal.BuildingConfigurationPlanId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<City>(e =>
        {
            e.HasKey(c => c.Id);
            e.Property(c => c.Name).HasMaxLength(200);
            e.Property(c => c.CountryCode).HasMaxLength(2);
            e.Property(c => c.AverageRentPerSqm).HasPrecision(18, 2);
            e.Property(c => c.BaseSalaryPerManhour).HasPrecision(18, 4);
        });

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

        modelBuilder.Entity<CityResource>(e =>
        {
            e.HasKey(cr => cr.Id);
            e.Property(cr => cr.Abundance).HasPrecision(5, 4);
            e.HasOne(cr => cr.City).WithMany(c => c.Resources).HasForeignKey(cr => cr.CityId);
            e.HasOne(cr => cr.ResourceType).WithMany().HasForeignKey(cr => cr.ResourceTypeId);
        });
    }
}
