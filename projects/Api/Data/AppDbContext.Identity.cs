using Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Api.Data;

public sealed partial class AppDbContext
{
    private static void ConfigureIdentityEntities(ModelBuilder modelBuilder)
    {
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

        modelBuilder.Entity<ChatMessage>(e =>
        {
            e.HasKey(message => message.Id);
            e.Property(message => message.Message).HasMaxLength(300);
            e.HasOne(message => message.Player)
                .WithMany()
                .HasForeignKey(message => message.PlayerId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(message => message.SentAtUtc);
        });

        modelBuilder.Entity<PersonTradeRecord>(e =>
        {
            e.HasKey(t => t.Id);
            e.Property(t => t.Direction).HasMaxLength(4);
            e.Property(t => t.ShareCount).HasPrecision(18, 4);
            e.Property(t => t.PricePerShare).HasPrecision(18, 4);
            e.Property(t => t.TotalValue).HasPrecision(18, 4);
            e.HasOne(t => t.Player).WithMany().HasForeignKey(t => t.PlayerId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(t => t.Company).WithMany().HasForeignKey(t => t.CompanyId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(t => t.PlayerId);
            e.HasIndex(t => t.RecordedAtTick);
        });

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
    }
}
