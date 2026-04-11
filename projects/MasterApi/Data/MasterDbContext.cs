using MasterApi.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace MasterApi.Data;

public sealed class MasterDbContext(DbContextOptions<MasterDbContext> options) : DbContext(options)
{
    public DbSet<GameServerNode> GameServers => Set<GameServerNode>();

    public DbSet<PlayerAccount> PlayerAccounts => Set<PlayerAccount>();

    public DbSet<ProSubscription> ProSubscriptions => Set<ProSubscription>();

    public DbSet<GlobalGameAdminGrant> GlobalGameAdminGrants => Set<GlobalGameAdminGrant>();

    public DbSet<GameNewsEntry> GameNewsEntries => Set<GameNewsEntry>();

    public DbSet<GameNewsEntryLocalization> GameNewsEntryLocalizations => Set<GameNewsEntryLocalization>();

    public DbSet<GameNewsReadReceipt> GameNewsReadReceipts => Set<GameNewsReadReceipt>();

    public DbSet<BuildingLayoutTemplate> BuildingLayoutTemplates => Set<BuildingLayoutTemplate>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var gameServer = modelBuilder.Entity<GameServerNode>();

        gameServer.HasKey(server => server.Id);
        gameServer.HasIndex(server => server.ServerKey).IsUnique();

        gameServer.Property(server => server.ServerKey).HasMaxLength(120);
        gameServer.Property(server => server.DisplayName).HasMaxLength(160);
        gameServer.Property(server => server.Description).HasMaxLength(320);
        gameServer.Property(server => server.Region).HasMaxLength(80);
        gameServer.Property(server => server.Environment).HasMaxLength(80);
        gameServer.Property(server => server.BackendUrl).HasMaxLength(240);
        gameServer.Property(server => server.GraphqlUrl).HasMaxLength(240);
        gameServer.Property(server => server.FrontendUrl).HasMaxLength(240);
        gameServer.Property(server => server.Version).HasMaxLength(80);

        var player = modelBuilder.Entity<PlayerAccount>();
        player.HasKey(p => p.Id);
        player.HasIndex(p => p.Email).IsUnique();
        player.Property(p => p.Email).HasMaxLength(200);
        player.Property(p => p.DisplayName).HasMaxLength(120);
        player.Property(p => p.PasswordHash).HasMaxLength(512);

        var sub = modelBuilder.Entity<ProSubscription>();
        sub.HasKey(s => s.Id);
        sub.HasOne(s => s.PlayerAccount)
           .WithMany(p => p.Subscriptions)
           .HasForeignKey(s => s.PlayerAccountId);

        var globalAdminGrant = modelBuilder.Entity<GlobalGameAdminGrant>();
        globalAdminGrant.HasKey(grant => grant.Id);
        globalAdminGrant.HasIndex(grant => grant.Email).IsUnique();
        globalAdminGrant.Property(grant => grant.Email).HasMaxLength(200);
        globalAdminGrant.Property(grant => grant.GrantedByEmail).HasMaxLength(200);

        var newsEntry = modelBuilder.Entity<GameNewsEntry>();
        newsEntry.HasKey(entry => entry.Id);
        newsEntry.HasIndex(entry => new { entry.TargetServerKey, entry.PublishedAtUtc });
        newsEntry.Property(entry => entry.EntryType).HasMaxLength(20);
        newsEntry.Property(entry => entry.Status).HasMaxLength(20);
        newsEntry.Property(entry => entry.TargetServerKey).HasMaxLength(120);
        newsEntry.Property(entry => entry.CreatedByEmail).HasMaxLength(200);
        newsEntry.Property(entry => entry.UpdatedByEmail).HasMaxLength(200);
        newsEntry.HasMany(entry => entry.Localizations)
            .WithOne(localization => localization.GameNewsEntry)
            .HasForeignKey(localization => localization.GameNewsEntryId)
            .OnDelete(DeleteBehavior.Cascade);
        newsEntry.HasMany(entry => entry.ReadReceipts)
            .WithOne(receipt => receipt.GameNewsEntry)
            .HasForeignKey(receipt => receipt.GameNewsEntryId)
            .OnDelete(DeleteBehavior.Cascade);

        var localization = modelBuilder.Entity<GameNewsEntryLocalization>();
        localization.HasKey(entry => entry.Id);
        localization.HasIndex(entry => new { entry.GameNewsEntryId, entry.Locale }).IsUnique();
        localization.Property(entry => entry.Locale).HasMaxLength(10);
        localization.Property(entry => entry.Title).HasMaxLength(220);
        localization.Property(entry => entry.Summary).HasMaxLength(1000);

        var readReceipt = modelBuilder.Entity<GameNewsReadReceipt>();
        readReceipt.HasKey(receipt => receipt.Id);
        readReceipt.HasIndex(receipt => new { receipt.GameNewsEntryId, receipt.PlayerEmail, receipt.ServerKey }).IsUnique();
        readReceipt.Property(receipt => receipt.PlayerEmail).HasMaxLength(200);
        readReceipt.Property(receipt => receipt.ServerKey).HasMaxLength(120);

        var layout = modelBuilder.Entity<BuildingLayoutTemplate>();
        layout.HasKey(l => l.Id);
        layout.HasIndex(l => new { l.PlayerAccountId, l.BuildingType });
        layout.HasOne(l => l.PlayerAccount)
            .WithMany()
            .HasForeignKey(l => l.PlayerAccountId)
            .OnDelete(DeleteBehavior.Cascade);
        layout.Property(l => l.Name).HasMaxLength(120);
        layout.Property(l => l.Description).HasMaxLength(500);
        layout.Property(l => l.BuildingType).HasMaxLength(60);
    }
}