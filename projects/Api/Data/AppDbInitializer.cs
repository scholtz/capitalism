using Api.Configuration;
using Api.Data.Entities;
using Api.Engine;
using Api.Utilities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;

namespace Api.Data;

/// <summary>
/// Seeds the database with initial game data: admin player, cities, resources, products, and recipes.
/// Called at application startup.
/// </summary>
public sealed partial class AppDbInitializer(
    AppDbContext dbContext,
    IOptions<SeedDataOptions> seedOptions)
{
    /// <summary>
    /// Ensures the schema is up to date and seeds initial data if missing.
    ///
    /// Startup sequence (relational databases):
    /// 1. <c>EnsureCreatedAsync</c> — creates the database and all tables if they do not exist
    ///    yet.  For databases that already exist (whether bootstrapped by a previous
    ///    <c>EnsureCreated</c> call or by an earlier migration run) this is a no-op.
    /// 2. <c>EnsureMigrationsHistoryBaselineAsync</c> — if <c>__EFMigrationsHistory</c> is
    ///    absent (legacy database that was bootstrapped by <c>EnsureCreatedAsync</c> before
    ///    migration support was introduced) this method creates the history table and marks every
    ///    currently-defined migration as already applied.  This prevents <c>MigrateAsync</c>
    ///    from attempting to re-create tables that already exist.
    /// 3. <c>MigrateAsync</c> — applies only the migrations that are not yet in the history
    ///    table.  For a brand-new or already up-to-date database this is a no-op.
    ///
    /// For in-memory databases (used in local development) migrations are not supported by
    /// the provider; <c>EnsureCreatedAsync</c> is used directly and steps 2–3 are skipped.
    /// </summary>
    public async Task InitializeAsync()
    {
        await SafelyApplyMigrationsAsync();

        if (!await dbContext.Players.AnyAsync(p => p.Email == seedOptions.Value.AdminEmail))
        {
            var hasher = new PasswordHasher<Player>();
            var admin = new Player
            {
                Id = Guid.NewGuid(),
                Email = seedOptions.Value.AdminEmail,
                DisplayName = seedOptions.Value.AdminDisplayName,
                Role = PlayerRole.Admin,
                PersonalCash = 200_000m,
                ActiveAccountType = AccountContextType.Person,
                CreatedAtUtc = DateTime.UtcNow
            };
            admin.PasswordHash = hasher.HashPassword(admin, seedOptions.Value.AdminPassword);
            dbContext.Players.Add(admin);
        }

        if (!await dbContext.GameStates.AnyAsync())
        {
            dbContext.GameStates.Add(new GameState { Id = 1, CurrentTick = 0, TickIntervalSeconds = seedOptions.Value.TickIntervalSeconds });
        }
        else
        {
            var gameState = await dbContext.GameStates.FirstAsync();
            if (gameState.TickIntervalSeconds <= 0)
            {
                gameState.TickIntervalSeconds = seedOptions.Value.TickIntervalSeconds;
            }

            if (gameState.TaxCycleTicks != GameConstants.TicksPerYear)
            {
                gameState.TaxCycleTicks = GameConstants.TicksPerYear;
            }
        }

        if (!await dbContext.ResourceTypes.AnyAsync())
        {
            SeedResources();
        }

        if (!await dbContext.Cities.AnyAsync())
        {
            SeedCities();
        }

        if (!await dbContext.ProductTypes.AnyAsync())
        {
            SeedProducts();
        }

        await dbContext.SaveChangesAsync();

        if (!await dbContext.CityResources.AnyAsync())
        {
            await SeedCityResourcesAsync();
            await dbContext.SaveChangesAsync();
        }

        if (!await dbContext.ProductRecipes.AnyAsync())
        {
            await SeedRecipesAsync();
            await dbContext.SaveChangesAsync();
        }

        if (!await dbContext.BuildingLots.AnyAsync())
        {
            await SeedBuildingLotsAsync();
            await dbContext.SaveChangesAsync();
        }

        var currentTick = await dbContext.GameStates
            .AsNoTracking()
            .Select(state => state.CurrentTick)
            .FirstOrDefaultAsync();
        await LandService.EnsureMinimumAvailableLotsAsync(dbContext, currentTick);
        await dbContext.SaveChangesAsync();
    }

    private void SeedResources()
    {
        dbContext.ResourceTypes.AddRange(GetResourceSeeds().Select(seed => new ResourceType
        {
            Id = CreateDeterministicGuid($"resource:{seed.Slug}"),
            Name = seed.Name,
            Slug = seed.Slug,
            Category = seed.Category,
            BasePrice = seed.BasePrice,
            WeightPerUnit = seed.WeightPerUnit,
            UnitName = seed.UnitName,
            UnitSymbol = seed.UnitSymbol,
            Description = seed.Description,
            ImageUrl = CreateEmojiImageDataUrl(seed.Icon, seed.BackgroundColor, seed.AccentColor)
        }));
    }

    private void SeedCities()
    {
        dbContext.Cities.AddRange(
            new City { Id = CreateDeterministicGuid("city:bratislava"), Name = "Bratislava", CountryCode = "SK", Latitude = 48.1486, Longitude = 17.1077, Population = 475_000, AverageRentPerSqm = 14m, BaseSalaryPerManhour = 18m },
            new City { Id = CreateDeterministicGuid("city:prague"), Name = "Prague", CountryCode = "CZ", Latitude = 50.0755, Longitude = 14.4378, Population = 1_350_000, AverageRentPerSqm = 18m, BaseSalaryPerManhour = 22m },
            new City { Id = CreateDeterministicGuid("city:vienna"), Name = "Vienna", CountryCode = "AT", Latitude = 48.2082, Longitude = 16.3738, Population = 1_900_000, AverageRentPerSqm = 22m, BaseSalaryPerManhour = 28m });
    }


    private async Task SeedCityResourcesAsync()
    {
        var cities = await dbContext.Cities.ToListAsync();
        var resources = await dbContext.ResourceTypes.ToDictionaryAsync(r => r.Slug);

        foreach (var city in cities)
        {
            dbContext.CityResources.Add(new CityResource { Id = Guid.NewGuid(), CityId = city.Id, ResourceTypeId = resources["wood"].Id, Abundance = 0.7m });
            dbContext.CityResources.Add(new CityResource { Id = Guid.NewGuid(), CityId = city.Id, ResourceTypeId = resources["grain"].Id, Abundance = 0.6m });
        }

        var bratislava = cities.First(c => c.Name == "Bratislava");
        dbContext.CityResources.Add(new CityResource { Id = Guid.NewGuid(), CityId = bratislava.Id, ResourceTypeId = resources["iron-ore"].Id, Abundance = 0.4m });
        dbContext.CityResources.Add(new CityResource { Id = Guid.NewGuid(), CityId = bratislava.Id, ResourceTypeId = resources["chemical-minerals"].Id, Abundance = 0.3m });

        var prague = cities.First(c => c.Name == "Prague");
        dbContext.CityResources.Add(new CityResource { Id = Guid.NewGuid(), CityId = prague.Id, ResourceTypeId = resources["coal"].Id, Abundance = 0.6m });
        dbContext.CityResources.Add(new CityResource { Id = Guid.NewGuid(), CityId = prague.Id, ResourceTypeId = resources["silicon"].Id, Abundance = 0.3m });

        var vienna = cities.First(c => c.Name == "Vienna");
        dbContext.CityResources.Add(new CityResource { Id = Guid.NewGuid(), CityId = vienna.Id, ResourceTypeId = resources["cotton"].Id, Abundance = 0.5m });
        dbContext.CityResources.Add(new CityResource { Id = Guid.NewGuid(), CityId = vienna.Id, ResourceTypeId = resources["gold"].Id, Abundance = 0.1m });
    }

    private async Task SeedRecipesAsync()
    {
        var resources = await dbContext.ResourceTypes.ToDictionaryAsync(r => r.Slug);
        var products = await dbContext.ProductTypes.ToDictionaryAsync(p => p.Slug);

        foreach (var seed in GetProductSeeds())
        {
            foreach (var ingredient in seed.Ingredients)
            {
                dbContext.ProductRecipes.Add(new ProductRecipe
                {
                    Id = Guid.NewGuid(),
                    ProductTypeId = products[seed.Slug].Id,
                    ResourceTypeId = ingredient.ResourceSlug is not null ? resources[ingredient.ResourceSlug].Id : null,
                    InputProductTypeId = ingredient.ProductSlug is not null ? products[ingredient.ProductSlug].Id : null,
                    Quantity = ingredient.Quantity
                });
            }
        }
    }

    private static IReadOnlyList<ResourceSeed> GetResourceSeeds() =>
    [
        Resource("Wood", "wood", "ORGANIC", 10m, 5m, "Ton", "t", "Harvested timber used in furniture, packaging, and construction.", "🪵", "#8B5A2B", "#D4A373"),
        Resource("Iron Ore", "iron-ore", "MINERAL", 25m, 10m, "Ton", "t", "Raw iron ore used to smelt metal components, fasteners, and structural goods.", "⛏️", "#6B7280", "#9CA3AF"),
        Resource("Coal", "coal", "MINERAL", 8m, 8m, "Ton", "t", "Industrial fuel used in heat-intensive processing, metallurgy, and battery chemistry.", "🪨", "#1F2937", "#4B5563"),
        Resource("Gold", "gold", "MINERAL", 500m, 0.1m, "Kilogram", "kg", "Precious conductive metal used in premium electronics and contact surfaces.", "🥇", "#B45309", "#F59E0B"),
        Resource("Chemical Minerals", "chemical-minerals", "MINERAL", 30m, 3m, "Ton", "t", "Industrial mineral feedstock used in chemicals, coatings, polymers, and medicine.", "🧪", "#7C3AED", "#A78BFA"),
        Resource("Cotton", "cotton", "ORGANIC", 15m, 1m, "Ton", "t", "Soft natural fibre used in healthcare textiles, insulation, and consumer fabric goods.", "🧵", "#E5E7EB", "#94A3B8"),
        Resource("Grain", "grain", "ORGANIC", 5m, 2m, "Ton", "t", "Agricultural staple milled into flour and processed into packaged foods.", "🌾", "#CA8A04", "#FCD34D"),
        Resource("Silicon", "silicon", "MINERAL", 40m, 2m, "Kilogram", "kg", "High-purity mineral used for wafers, glass, and electronics manufacturing.", "💠", "#0EA5E9", "#67E8F9")
    ];


    private sealed record ResourceSeed(
        string Name,
        string Slug,
        string Category,
        decimal BasePrice,
        decimal WeightPerUnit,
        string UnitName,
        string UnitSymbol,
        string Description,
        string Icon,
        string BackgroundColor,
        string AccentColor);

    private sealed record ProductSeed(
        string Name,
        string Slug,
        string Industry,
        decimal BasePrice,
        int BaseCraftTicks,
        string Description,
        string UnitName,
        string UnitSymbol,
        decimal OutputQuantity,
        decimal EnergyConsumptionMwh,
        decimal BasicLaborHours,
        decimal PriceElasticity,
        IReadOnlyList<RecipeSeed> Ingredients);

    private sealed record RecipeSeed(string? ResourceSlug, string? ProductSlug, decimal Quantity);
}
