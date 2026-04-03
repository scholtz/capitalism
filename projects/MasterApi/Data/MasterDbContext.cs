using MasterApi.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace MasterApi.Data;

public sealed class MasterDbContext(DbContextOptions<MasterDbContext> options) : DbContext(options)
{
    public DbSet<GameServerNode> GameServers => Set<GameServerNode>();

    public DbSet<PlayerAccount> PlayerAccounts => Set<PlayerAccount>();

    public DbSet<ProSubscription> ProSubscriptions => Set<ProSubscription>();

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
    }
}