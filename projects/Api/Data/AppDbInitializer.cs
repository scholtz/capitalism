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
public sealed class AppDbInitializer(
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

    private void SeedProducts()
    {
        var seeds = GetProductSeeds();
        var proOnlySlugs = DetermineInitialProOnlyProductSlugs(seeds);

        dbContext.ProductTypes.AddRange(seeds.Select(seed => new ProductType
        {
            Id = CreateDeterministicGuid($"product:{seed.Slug}"),
            Name = seed.Name,
            Slug = seed.Slug,
            Industry = seed.Industry,
            BasePrice = seed.BasePrice,
            PriceElasticity = seed.PriceElasticity,
            BaseCraftTicks = seed.BaseCraftTicks,
            OutputQuantity = seed.OutputQuantity,
            EnergyConsumptionMwh = seed.EnergyConsumptionMwh,
            BasicLaborHours = seed.BasicLaborHours,
            IsProOnly = proOnlySlugs.Contains(seed.Slug),
            UnitName = seed.UnitName,
            UnitSymbol = seed.UnitSymbol,
            Description = seed.Description
        }));
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

    private static IReadOnlyList<ProductSeed> GetProductSeeds() =>
    [
        .. GetFurnitureProducts(),
        .. GetFoodProducts(),
        .. GetHealthcareProducts(),
        .. GetElectronicsProducts(),
        .. GetConstructionProducts()
    ];

    private static HashSet<string> DetermineInitialProOnlyProductSlugs(IReadOnlyList<ProductSeed> seeds)
    {
        var proOnlySlugs = seeds
            .Where(seed => seed.Industry is Industry.Electronics or Industry.Construction)
            .Select(seed => seed.Slug)
            .ToHashSet(StringComparer.Ordinal);

        var changed = true;
        while (changed)
        {
            changed = false;

            foreach (var seed in seeds)
            {
                if (proOnlySlugs.Contains(seed.Slug))
                {
                    continue;
                }

                if (seed.Ingredients.Any(ingredient => ingredient.ProductSlug is not null && proOnlySlugs.Contains(ingredient.ProductSlug)))
                {
                    changed |= proOnlySlugs.Add(seed.Slug);
                }
            }
        }

        return proOnlySlugs;
    }

    private static IEnumerable<ProductSeed> GetFurnitureProducts()
    {
        yield return Product("Wood Planks", "wood-planks", Industry.Furniture, 18m, 1, "Cut and dried timber planks for furniture and interior fittings.", "Plank", "planks", 40m, 0.4m, ResourceIngredient("wood", 1m));
        yield return Product("Wooden Chair", "wooden-chair", Industry.Furniture, 45m, 2, "A basic wooden chair suited for starter furniture production.", "Chair", "chairs", 20m, 1m, ResourceIngredient("wood", 1m));
        yield return Product("Wooden Table", "wooden-table", Industry.Furniture, 120m, 3, "A classic dining table and one of the first scalable furniture products.", "Table", "tables", 10m, 1m, ResourceIngredient("wood", 1m));
        yield return Product("Wooden Bed", "wooden-bed", Industry.Furniture, 200m, 4, "A comfortable wooden bed frame for mass-market housing.", "Bed", "beds", 5m, 1m, ResourceIngredient("wood", 1m));

        foreach (var (name, slug, price, ticks, output, energy, planks, fasteners, description) in new[]
        {
            ("Wooden Stool", "wooden-stool", 32m, 2, 12m, 0.8m, 8m, 1m, "Simple low-cost wooden seating with fast throughput."),
            ("Bookshelf", "bookshelf", 95m, 3, 4m, 0.9m, 14m, 2m, "A mid-market bookshelf assembled from planks and brackets."),
            ("Nightstand", "nightstand", 70m, 3, 6m, 0.8m, 10m, 1m, "Compact bedside storage made from finished timber sections."),
            ("Office Desk", "office-desk", 160m, 4, 4m, 1.2m, 16m, 3m, "Commercial desk with cable management and sturdy frame."),
            ("Dining Bench", "dining-bench", 85m, 3, 4m, 0.9m, 12m, 2m, "Bench seating for residential dining sets."),
            ("Wardrobe", "wardrobe", 250m, 5, 2m, 1.5m, 22m, 4m, "Tall clothing cabinet for the consumer home market."),
            ("Dresser", "dresser", 210m, 5, 2m, 1.5m, 20m, 4m, "Bedroom dresser with multiple drawers and reinforced slides."),
            ("Coffee Table", "coffee-table", 90m, 3, 4m, 0.9m, 11m, 2m, "Low-profile living-room table produced in compact batches."),
            ("Bunk Bed", "bunk-bed", 340m, 6, 1m, 1.8m, 30m, 6m, "Space-saving stacked bed frame for residential and dormitory use."),
            ("Crib", "crib", 140m, 4, 2m, 1m, 14m, 2m, "Starter family furniture item with compact dimensions."),
            ("Patio Chair", "patio-chair", 60m, 3, 6m, 1m, 9m, 2m, "Outdoor chair with reinforced joints for weather exposure."),
            ("Patio Table", "patio-table", 150m, 4, 3m, 1.2m, 15m, 3m, "Outdoor table assembled from treated planks and metal hardware."),
            ("Door Frame", "door-frame", 110m, 3, 3m, 1m, 13m, 2m, "Interior frame for commercial and residential buildings."),
            ("Sofa Frame", "sofa-frame", 175m, 4, 2m, 1.3m, 18m, 3m, "Wooden support frame for upholstered living-room furniture."),
            ("TV Stand", "tv-stand", 135m, 4, 3m, 1.1m, 14m, 2m, "Cabinet-style stand for home media equipment.")
        })
        {
            yield return Product(name, slug, Industry.Furniture, price, ticks, description, "Piece", "pcs", output, energy, ProductIngredient("wood-planks", planks), ProductIngredient("iron-fasteners", fasteners));
        }

        yield return Product("Filing Cabinet", "filing-cabinet", Industry.Furniture, 180m, 4, "Office storage furniture reinforced with metal rails.", "Piece", "pcs", 3m, 1.2m, ProductIngredient("wood-planks", 10m), ProductIngredient("steel-panel", 2m), ProductIngredient("iron-fasteners", 2m));
        yield return Product("Window Frame", "window-frame", Industry.Furniture, 105m, 3, "Finished wood frame designed for glazing insertion.", "Piece", "pcs", 4m, 1m, ProductIngredient("wood-planks", 10m), ProductIngredient("glass-pane", 1m), ProductIngredient("iron-fasteners", 2m));
        yield return Product("Dining Set", "dining-set", Industry.Furniture, 480m, 6, "Bundled premium set combining table and chair production.", "Set", "sets", 1m, 2.2m, ProductIngredient("wooden-table", 1m), ProductIngredient("wooden-chair", 4m));
        yield return Product("Electronic Table", "electronic-table", Industry.Furniture, 520m, 6, "Smart office table with built-in electronics and connected features.", "Piece", "pcs", 1m, 2m, ResourceIngredient("wood", 1m), ProductIngredient("iron-fasteners", 10m), ProductIngredient("electronic-components", 10m));
    }

    private static IEnumerable<ProductSeed> GetFoodProducts()
    {
        yield return Product("Flour", "flour", Industry.FoodProcessing, 8m, 1, "Milled grain flour used in baking, noodle making, and packaged food.", "Bag", "bags", 10m, 0.4m, ResourceIngredient("grain", 1m));
        yield return Product("Bread", "bread", Industry.FoodProcessing, 3m, 1, "Basic bread loaf and one of the simplest onboarding products.", "Loaf", "loaves", 12m, 0.5m, ResourceIngredient("grain", 1m));

        foreach (var (name, slug, price, ticks, output, energy, flourQuantity, description) in new[]
        {
            ("Pasta", "pasta", 9m, 2, 16m, 0.7m, 2m, "Dry pasta manufactured from grain flour in large volumes."),
            ("Noodles", "noodles", 8m, 2, 18m, 0.6m, 2m, "Shelf-stable noodle packs for retail and wholesale markets."),
            ("Crackers", "crackers", 6m, 2, 20m, 0.6m, 1m, "Baked snack crackers produced from seasoned flour dough."),
            ("Pancake Mix", "pancake-mix", 11m, 2, 12m, 0.6m, 2m, "Convenience baking mix for pancakes and waffles."),
            ("Cake Mix", "cake-mix", 12m, 2, 12m, 0.7m, 2m, "Packaged baking mix for retail bakery goods."),
            ("Bakery Premix", "bakery-premix", 13m, 2, 8m, 0.7m, 3m, "Bulk flour blend for industrial bakery clients."),
            ("Biscuit Pack", "biscuit-pack", 9m, 2, 16m, 0.6m, 1m, "Retail biscuit pack with high throughput and stable demand."),
            ("Sandwich Bread", "sandwich-bread", 4m, 1, 14m, 0.5m, 1m, "Soft loaf product optimized for grocery chains."),
            ("Toast Bread", "toast-bread", 4m, 1, 14m, 0.5m, 1m, "Consistent sliced bread for breakfast and hospitality channels.")
        })
        {
            yield return Product(name, slug, Industry.FoodProcessing, price, ticks, description, "Pack", "packs", output, energy, ProductIngredient("flour", flourQuantity));
        }

        foreach (var (name, slug, price, ticks, output, energy, grainQuantity, description) in new[]
        {
            ("Cereal Flakes", "cereal-flakes", 7m, 2, 14m, 0.7m, 1m, "Packaged breakfast cereal made from processed grain."),
            ("Semolina", "semolina", 9m, 1, 10m, 0.4m, 1m, "Durum-style coarse flour used in premium food production."),
            ("Porridge Mix", "porridge-mix", 6m, 1, 14m, 0.3m, 1m, "Simple grain breakfast mix for mass-market distribution."),
            ("Grain Bars", "grain-bars", 10m, 2, 18m, 0.6m, 1m, "Packaged grain snack bars sold in multipacks."),
            ("Animal Feed", "animal-feed", 7m, 1, 10m, 0.3m, 1m, "Low-margin but reliable feed product for agribusiness buyers."),
            ("Bran Bags", "bran-bags", 4m, 1, 10m, 0.2m, 1m, "By-product bagged for livestock and industrial buyers.")
        })
        {
            yield return Product(name, slug, Industry.FoodProcessing, price, ticks, description, "Bag", "bags", output, energy, ResourceIngredient("grain", grainQuantity));
        }

        yield return Product("Breadcrumbs", "breadcrumbs", Industry.FoodProcessing, 5m, 1, "Crumb ingredient made from dried bread loaves.", "Bag", "bags", 10m, 0.3m, ProductIngredient("bread", 2m));
        yield return Product("Pasta Kit", "pasta-kit", Industry.FoodProcessing, 14m, 2, "Retail kit bundling pasta portions for home cooking.", "Kit", "kits", 12m, 0.7m, ProductIngredient("pasta", 2m));
        yield return Product("Snack Crackers", "snack-crackers", Industry.FoodProcessing, 7m, 2, "Premium cracker line with retail-ready packaging.", "Box", "boxes", 18m, 0.6m, ProductIngredient("crackers", 1m));
    }

    private static IEnumerable<ProductSeed> GetHealthcareProducts()
    {
        yield return Product("Bandages", "bandages", Industry.Healthcare, 15m, 1, "Basic wound-care bandages and a simple onboarding healthcare product.", "Pack", "packs", 20m, 0.3m, ResourceIngredient("cotton", 1m));
        yield return Product("Basic Medicine", "basic-medicine", Industry.Healthcare, 50m, 3, "Essential pharmaceutical product for starter healthcare chains.", "Bottle", "bottles", 8m, 1m, ResourceIngredient("chemical-minerals", 1m));

        foreach (var (name, slug, price, ticks, output, energy, description) in new[]
        {
            ("Antiseptic", "antiseptic", 24m, 2, 12m, 0.8m, "Disinfecting liquid used in wound treatment and surgical care."),
            ("Pain Relief Tablets", "pain-relief-tablets", 34m, 2, 14m, 0.9m, "Mass-market analgesic tablets packed for pharmacies."),
            ("Cough Syrup", "cough-syrup", 28m, 2, 10m, 0.8m, "Liquid cough relief product for pharmacies and clinics."),
            ("Vitamin Pack", "vitamin-pack", 26m, 2, 12m, 0.8m, "Supplement packs blended from mineral compounds."),
            ("Cold Pack", "cold-pack", 12m, 1, 16m, 0.4m, "Instant cold-compress product for sports and emergency use."),
            ("Saline Kit", "saline-kit", 18m, 2, 10m, 0.7m, "Sterile saline pack for hospitals and laboratories."),
            ("Healing Ointment", "healing-ointment", 30m, 2, 14m, 0.8m, "Topical wound treatment cream sold in tubes."),
            ("Allergy Tablets", "allergy-tablets", 27m, 2, 14m, 0.8m, "Seasonal allergy medication for pharmacy shelves."),
            ("Mineral Supplement", "mineral-supplement", 25m, 2, 10m, 0.8m, "Supplement tablets blended from purified mineral inputs.")
        })
        {
            yield return Product(name, slug, Industry.Healthcare, price, ticks, description, "Pack", "packs", output, energy, ResourceIngredient("chemical-minerals", 1m));
        }

        foreach (var (name, slug, price, ticks, output, energy, cottonQuantity, description) in new[]
        {
            ("Sterile Gauze", "sterile-gauze", 18m, 1, 16m, 0.4m, 1m, "Medical gauze made from refined cotton fibres."),
            ("Surgical Masks", "surgical-masks", 21m, 2, 24m, 0.6m, 1m, "Disposable masks sold to clinics and pharmacies."),
            ("Cotton Swabs", "cotton-swabs", 8m, 1, 30m, 0.3m, 0.5m, "Disposable cotton swabs for healthcare and cosmetic use."),
            ("Surgical Tape", "surgical-tape", 11m, 1, 20m, 0.3m, 0.5m, "Adhesive medical tape for wound dressing and equipment fastening."),
            ("Compression Wrap", "compression-wrap", 16m, 1, 14m, 0.4m, 0.8m, "Elastic cotton wrap used in sports and clinic recovery treatment.")
        })
        {
            yield return Product(name, slug, Industry.Healthcare, price, ticks, description, "Pack", "packs", output, energy, ResourceIngredient("cotton", cottonQuantity));
        }

        yield return Product("Medical Gloves", "medical-gloves", Industry.Healthcare, 22m, 2, "Protective gloves for healthcare and laboratory use.", "Box", "boxes", 20m, 0.6m, ResourceIngredient("chemical-minerals", 0.5m), ResourceIngredient("cotton", 0.2m));
        yield return Product("Disinfectant Wipes", "disinfectant-wipes", Industry.Healthcare, 19m, 2, "Pre-soaked wipes for cleaning and sanitation.", "Pack", "packs", 18m, 0.7m, ResourceIngredient("cotton", 0.5m), ProductIngredient("antiseptic", 1m));
        yield return Product("First Aid Kit", "first-aid-kit", Industry.Healthcare, 42m, 2, "Retail first-aid kit assembled from basic medical supplies.", "Kit", "kits", 8m, 0.6m, ProductIngredient("bandages", 2m), ProductIngredient("antiseptic", 1m), ProductIngredient("cotton-swabs", 1m));
        yield return Product("Wound Dressing Kit", "wound-dressing-kit", Industry.Healthcare, 38m, 2, "Advanced dressing bundle for hospitals and clinics.", "Kit", "kits", 8m, 0.7m, ProductIngredient("bandages", 2m), ProductIngredient("sterile-gauze", 2m), ProductIngredient("surgical-tape", 1m));
    }

    private static IEnumerable<ProductSeed> GetElectronicsProducts()
    {
        yield return Product("Silicon Wafer", "silicon-wafer", Industry.Electronics, 22m, 2, "Processed silicon wafer used as the basis for chips and sensors.", "Wafer", "wafers", 12m, 0.9m, ResourceIngredient("silicon", 1m));
        yield return Product("Glass Pane", "glass-pane", Industry.Electronics, 18m, 2, "Industrial glass pane made from processed silicon.", "Pane", "panes", 12m, 0.8m, ResourceIngredient("silicon", 1m));
        yield return Product("Gold Contact", "gold-contact", Industry.Electronics, 65m, 2, "Precision gold contact used in high-end electronic assemblies.", "Set", "sets", 10m, 0.8m, ResourceIngredient("gold", 0.2m));
        yield return Product("Electronic Components", "electronic-components", Industry.Electronics, 48m, 3, "Mixed component pack for smart devices, controls, and electronics furniture.", "Pack", "packs", 16m, 1.2m, ProductIngredient("silicon-wafer", 1m), ProductIngredient("gold-contact", 1m));
        yield return Product("Circuit Board", "circuit-board", Industry.Electronics, 55m, 3, "Populated board used in control panels and consumer devices.", "Board", "boards", 10m, 1.1m, ProductIngredient("silicon-wafer", 1m), ProductIngredient("gold-contact", 1m), ProductIngredient("electronic-components", 2m));
        yield return Product("Sensor Module", "sensor-module", Industry.Electronics, 72m, 4, "Compact sensor package for automation and smart-home products.", "Module", "modules", 8m, 1.4m, ProductIngredient("electronic-components", 3m), ProductIngredient("circuit-board", 1m));
        yield return Product("Battery Pack", "battery-pack", Industry.Electronics, 46m, 3, "Rechargeable battery pack using treated carbon and chemical inputs.", "Pack", "packs", 10m, 1.3m, ResourceIngredient("coal", 1m), ResourceIngredient("chemical-minerals", 1m));
        yield return Product("LED Lamp", "led-lamp", Industry.Electronics, 28m, 3, "Energy-efficient lighting unit for retail and construction buyers.", "Piece", "pcs", 12m, 1m, ProductIngredient("electronic-components", 2m), ProductIngredient("glass-pane", 1m));
        yield return Product("Power Adapter", "power-adapter", Industry.Electronics, 32m, 3, "External adapter unit for electronics and office equipment.", "Piece", "pcs", 10m, 1.1m, ProductIngredient("electronic-components", 2m), ProductIngredient("iron-fasteners", 1m));
        yield return Product("Radio Set", "radio-set", Industry.Electronics, 85m, 4, "Compact radio receiver for consumer retail distribution.", "Piece", "pcs", 6m, 1.5m, ProductIngredient("circuit-board", 1m), ProductIngredient("electronic-components", 3m), ProductIngredient("gold-contact", 1m));
        yield return Product("Desk Speaker", "desk-speaker", Industry.Electronics, 78m, 4, "Desktop audio speaker with compact amplifier internals.", "Piece", "pcs", 6m, 1.5m, ProductIngredient("circuit-board", 1m), ProductIngredient("electronic-components", 2m));
        yield return Product("Calculator", "calculator", Industry.Electronics, 26m, 3, "Simple consumer calculator with low input complexity.", "Piece", "pcs", 12m, 1m, ProductIngredient("electronic-components", 2m), ProductIngredient("glass-pane", 1m));
        yield return Product("Smart Home Hub", "smart-home-hub", Industry.Electronics, 140m, 5, "Connected automation hub with premium electronic internals.", "Piece", "pcs", 4m, 1.8m, ProductIngredient("circuit-board", 2m), ProductIngredient("electronic-components", 4m), ProductIngredient("signal-amplifier", 1m));
        yield return Product("Solar Cell", "solar-cell", Industry.Electronics, 58m, 4, "Photovoltaic cell used in energy and construction assemblies.", "Cell", "cells", 8m, 1.6m, ProductIngredient("silicon-wafer", 2m), ProductIngredient("gold-contact", 1m));
        yield return Product("Control Panel", "control-panel", Industry.Electronics, 95m, 4, "Industrial control panel for buildings and machinery.", "Panel", "panels", 5m, 1.7m, ProductIngredient("circuit-board", 2m), ProductIngredient("electronic-components", 3m), ProductIngredient("iron-fasteners", 2m));
        yield return Product("Industrial Relay", "industrial-relay", Industry.Electronics, 38m, 3, "Switching relay for control systems and automation.", "Piece", "pcs", 12m, 1m, ProductIngredient("gold-contact", 1m), ProductIngredient("electronic-components", 2m));
        yield return Product("Touch Display", "touch-display", Industry.Electronics, 115m, 5, "Glass-fronted display assembly for smart devices and kiosks.", "Display", "displays", 4m, 1.9m, ProductIngredient("glass-pane", 1m), ProductIngredient("circuit-board", 1m), ProductIngredient("gold-contact", 1m));
        yield return Product("Signal Amplifier", "signal-amplifier", Industry.Electronics, 68m, 4, "Amplifier module used in hubs, radios, and broadcast equipment.", "Module", "modules", 6m, 1.4m, ProductIngredient("circuit-board", 1m), ProductIngredient("electronic-components", 2m));
        yield return Product("Network Router", "network-router", Industry.Electronics, 88m, 4, "Consumer and small-office router with multi-component internals.", "Piece", "pcs", 6m, 1.6m, ProductIngredient("circuit-board", 1m), ProductIngredient("electronic-components", 3m), ProductIngredient("signal-amplifier", 1m));
        yield return Product("LED Bulb Pack", "led-bulb-pack", Industry.Electronics, 24m, 3, "Retail multi-pack of LED bulbs for home improvement channels.", "Pack", "packs", 10m, 1m, ProductIngredient("led-lamp", 2m));
        yield return Product("Meter Module", "meter-module", Industry.Electronics, 64m, 4, "Measurement module used in utilities and smart industrial systems.", "Module", "modules", 6m, 1.5m, ProductIngredient("sensor-module", 1m), ProductIngredient("circuit-board", 1m));
    }

    private static IEnumerable<ProductSeed> GetConstructionProducts()
    {
        yield return Product("Steel Ingot", "steel-ingot", Industry.Construction, 30m, 2, "Processed iron-and-coal metal stock used for structural production.", "Ingot", "ingots", 20m, 1.2m, ResourceIngredient("iron-ore", 1m), ResourceIngredient("coal", 1m));
        yield return Product("Steel Beam", "steel-beam", Industry.Construction, 70m, 3, "Structural steel beam for industrial and commercial buildings.", "Beam", "beams", 8m, 1.4m, ProductIngredient("steel-ingot", 2m));
        yield return Product("Iron Nails", "iron-nails", Industry.Construction, 12m, 1, "Standard nail box used in furniture and building assembly.", "Box", "boxes", 25m, 0.5m, ResourceIngredient("iron-ore", 0.5m));
        yield return Product("Iron Fasteners", "iron-fasteners", Industry.Construction, 18m, 2, "Precision fastener batch measured in kilograms for assembly lines.", "Kilogram", "kg", 20m, 0.7m, ResourceIngredient("iron-ore", 1m));
        yield return Product("Screws Box", "screws-box", Industry.Construction, 16m, 2, "Box of threaded screws for modular assembly and construction.", "Box", "boxes", 20m, 0.6m, ProductIngredient("iron-fasteners", 5m));
        yield return Product("Wood Panel", "wood-panel", Industry.Construction, 26m, 2, "Finished wooden panel for modular interiors and building shells.", "Panel", "panels", 12m, 0.7m, ProductIngredient("wood-planks", 3m));
        yield return Product("Insulation Roll", "insulation-roll", Industry.Construction, 22m, 2, "Insulation material made from processed cotton fibres.", "Roll", "rolls", 10m, 0.6m, ResourceIngredient("cotton", 1m));
        yield return Product("Glass Window", "glass-window", Industry.Construction, 42m, 3, "Finished glazed window for residential and commercial projects.", "Window", "windows", 6m, 1m, ProductIngredient("glass-pane", 2m), ProductIngredient("window-frame", 1m));
        yield return Product("Roofing Sheet", "roofing-sheet", Industry.Construction, 34m, 3, "Metal roofing section for warehouse and industrial builds.", "Sheet", "sheets", 10m, 1m, ProductIngredient("steel-ingot", 1m));
        yield return Product("Cable Duct", "cable-duct", Industry.Construction, 20m, 2, "Rigid duct section for power and data routing in buildings.", "Section", "sections", 12m, 0.7m, ProductIngredient("steel-ingot", 1m));
        yield return Product("Wall Panel", "wall-panel", Industry.Construction, 36m, 3, "Finished wall panel assembled from timber and insulation layers.", "Panel", "panels", 8m, 1m, ProductIngredient("wood-panel", 2m), ProductIngredient("insulation-roll", 1m));
        yield return Product("Support Column", "support-column", Industry.Construction, 65m, 4, "Load-bearing column for industrial and commercial structures.", "Column", "columns", 4m, 1.5m, ProductIngredient("steel-beam", 2m));
        yield return Product("Scaffold Kit", "scaffold-kit", Industry.Construction, 88m, 4, "Reusable scaffolding kit for construction contractors.", "Kit", "kits", 4m, 1.5m, ProductIngredient("steel-beam", 1m), ProductIngredient("iron-fasteners", 5m));
        yield return Product("Gate Frame", "gate-frame", Industry.Construction, 54m, 3, "Metal gate frame for warehouse and commercial property use.", "Frame", "frames", 6m, 1.1m, ProductIngredient("steel-ingot", 1m), ProductIngredient("iron-fasteners", 3m));
        yield return Product("Warehouse Rack", "warehouse-rack", Industry.Construction, 95m, 4, "Heavy-duty rack system for storage and logistics buildings.", "Rack", "racks", 4m, 1.4m, ProductIngredient("steel-beam", 1m), ProductIngredient("iron-fasteners", 4m));
        yield return Product("Solar Roof Tile", "solar-roof-tile", Industry.Construction, 145m, 5, "Integrated roofing tile with embedded photovoltaic cell.", "Tile", "tiles", 4m, 1.9m, ProductIngredient("roofing-sheet", 1m), ProductIngredient("solar-cell", 1m));
        yield return Product("Safety Railing", "safety-railing", Industry.Construction, 32m, 2, "Protective railing for industrial and commercial facilities.", "Section", "sections", 8m, 0.8m, ProductIngredient("steel-ingot", 1m), ProductIngredient("iron-fasteners", 2m));
        yield return Product("Steel Panel", "steel-panel", Industry.Construction, 40m, 3, "Flat steel panel used in cabinets, junction boxes, and doors.", "Panel", "panels", 8m, 1m, ProductIngredient("steel-ingot", 1m));
        yield return Product("Junction Box", "junction-box", Industry.Construction, 28m, 2, "Electrical junction housing used in construction projects.", "Box", "boxes", 10m, 0.8m, ProductIngredient("steel-panel", 1m), ProductIngredient("electronic-components", 1m));
        yield return Product("Ventilation Grille", "ventilation-grille", Industry.Construction, 24m, 2, "Steel ventilation grille for building airflow systems.", "Piece", "pcs", 12m, 0.7m, ProductIngredient("steel-panel", 1m));
        yield return Product("Steel Door", "steel-door", Industry.Construction, 80m, 4, "Durable steel security door for commercial and industrial use.", "Door", "doors", 4m, 1.5m, ProductIngredient("steel-panel", 2m), ProductIngredient("iron-fasteners", 4m));
        yield return Product("Pipe Section", "pipe-section", Industry.Construction, 30m, 2, "Standardized pipe section for ventilation and utility networks.", "Section", "sections", 10m, 0.9m, ProductIngredient("steel-ingot", 1m));
        yield return Product("Assembly Pallet", "assembly-pallet", Industry.Construction, 18m, 1, "Transport pallet for factories, warehouses, and supply chains.", "Pallet", "pallets", 12m, 0.4m, ProductIngredient("wood-planks", 3m));
    }

    private static ResourceSeed Resource(string name, string slug, string category, decimal basePrice, decimal weightPerUnit, string unitName, string unitSymbol, string description, string icon, string backgroundColor, string accentColor)
        => new(name, slug, category, basePrice, weightPerUnit, unitName, unitSymbol, description, icon, backgroundColor, accentColor);

    private static ProductSeed Product(string name, string slug, string industry, decimal basePrice, int baseCraftTicks, string description, string unitName, string unitSymbol, decimal outputQuantity, decimal energyConsumptionMwh, params RecipeSeed[] ingredients)
        => new(
            name,
            slug,
            industry,
            basePrice,
            baseCraftTicks,
            description,
            unitName,
            unitSymbol,
            outputQuantity,
            energyConsumptionMwh,
            ComputeBasicLaborHours(baseCraftTicks, energyConsumptionMwh, ingredients.Length),
            DeterminePriceElasticity(industry),
            ingredients);

    private static decimal DeterminePriceElasticity(string industry) => industry switch
    {
        Industry.FoodProcessing => 0.75m,
        Industry.Healthcare => 0.20m,
        Industry.Furniture => 0.35m,
        Industry.Electronics => 0.55m,
        Industry.Construction => 0.30m,
        _ => 0.35m,
    };

    private static decimal ComputeBasicLaborHours(int baseCraftTicks, decimal energyConsumptionMwh, int ingredientCount)
    {
        var labor = (baseCraftTicks * 0.55m) + (energyConsumptionMwh * 0.35m) + (ingredientCount * 0.15m);
        return decimal.Round(Math.Max(0.25m, labor), 4, MidpointRounding.AwayFromZero);
    }

    private static RecipeSeed ResourceIngredient(string resourceSlug, decimal quantity)
        => new(resourceSlug, null, quantity);

    private static RecipeSeed ProductIngredient(string productSlug, decimal quantity)
        => new(null, productSlug, quantity);

    private static string CreateEmojiImageDataUrl(string icon, string backgroundColor, string accentColor)
    {
        var safeBackgroundColor = NormalizeHexColor(backgroundColor);
        var safeAccentColor = NormalizeHexColor(accentColor);
        var svg = $$"""
        <svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 160 160'>
          <defs>
            <linearGradient id='g' x1='0' y1='0' x2='1' y2='1'>
              <stop offset='0%' stop-color='{{safeBackgroundColor}}'/>
              <stop offset='100%' stop-color='{{safeAccentColor}}'/>
            </linearGradient>
          </defs>
          <rect width='160' height='160' rx='28' fill='url(#g)'/>
          <circle cx='80' cy='80' r='48' fill='rgba(255,255,255,0.18)'/>
          <text x='80' y='96' text-anchor='middle' font-size='56'>{{icon}}</text>
        </svg>
        """;

        return $"data:image/svg+xml;utf8,{Uri.EscapeDataString(svg)}";
    }

    private static string NormalizeHexColor(string value)
    {
        if (value.Length == 7 && value[0] == '#' && value.Skip(1).All(Uri.IsHexDigit))
        {
            return value;
        }

        return "#4B5563";
    }

    private async Task SeedBuildingLotsAsync()
    {
        var bratislava = await dbContext.Cities.FirstAsync(c => c.Name == "Bratislava");
        var resources = await dbContext.ResourceTypes.ToDictionaryAsync(r => r.Slug);

        // Bratislava building lots across different districts.
        // Coordinates are spread around the city center (48.1486, 17.1077).
        //
        // BasePrice is the pure land anchor value (no resource premium).
        // LandService.RefreshLandState is called below to compute the dynamic PopulationIndex
        // and the final Price = ComputeAppraisedPrice(basePrice, populationIndex) + resourcePremium.
        // This means mine lots with raw-material deposits will always have Price > BasePrice.
        var lotsToSeed = new List<BuildingLot>
        {
            // ── Industrial Zone (eastern outskirts) ──
            // Low population index: these lots are near logistics hubs but away from residential areas.
            new BuildingLot
            {
                Id = CreateDeterministicGuid("lot:ba-industrial-1"),
                CityId = bratislava.Id,
                Name = "Industrial Plot A1",
                Description = "Large industrial plot near the eastern logistics corridor. Sits above an Iron Ore deposit (18,000t at 72% quality).",
                District = "Industrial Zone",
                Latitude = 48.1520, Longitude = 17.1250,
                PopulationIndex = 0.65m,
                BasePrice = 75_000m,
                Price = 75_000m,  // will be recomputed below
                SuitableTypes = "FACTORY,MINE",
                ResourceTypeId = resources.TryGetValue("iron-ore", out var ironOre) ? ironOre.Id : null,
                ResourceType = resources.TryGetValue("iron-ore", out var ironOreNav) ? ironOreNav : null,
                MaterialQuality = 0.72m,
                MaterialQuantity = 18_000m
            },
            new BuildingLot
            {
                Id = CreateDeterministicGuid("lot:ba-industrial-2"),
                CityId = bratislava.Id,
                Name = "Industrial Plot A2",
                Description = "Adjacent to major rail freight terminal. Sits above a Chemical Minerals deposit (12,000t at 55% quality).",
                District = "Industrial Zone",
                Latitude = 48.1540, Longitude = 17.1280,
                PopulationIndex = 0.60m,
                BasePrice = 65_000m,
                Price = 65_000m,  // will be recomputed below
                SuitableTypes = "FACTORY,MINE",
                ResourceTypeId = resources.TryGetValue("chemical-minerals", out var chem) ? chem.Id : null,
                ResourceType = resources.TryGetValue("chemical-minerals", out var chemNav) ? chemNav : null,
                MaterialQuality = 0.55m,
                MaterialQuantity = 12_000m
            },
            new BuildingLot
            {
                Id = CreateDeterministicGuid("lot:ba-industrial-3"),
                CityId = bratislava.Id,
                Name = "Factory Site B1",
                Description = "Modern industrial park with good power grid access. Suitable for energy-intensive production.",
                District = "Industrial Zone",
                Latitude = 48.1500, Longitude = 17.1300,
                PopulationIndex = 0.72m,
                BasePrice = 90_000m,
                Price = 90_000m,
                SuitableTypes = "FACTORY,POWER_PLANT"
            },
            // ── Commercial District (city center) ──
            // High population index: these lots are in the heart of the city with dense foot traffic.
            new BuildingLot
            {
                Id = CreateDeterministicGuid("lot:ba-commercial-1"),
                CityId = bratislava.Id,
                Name = "High Street Retail Space",
                Description = "Prime storefront on the main pedestrian avenue. High foot traffic and visibility.",
                District = "Commercial District",
                Latitude = 48.1450, Longitude = 17.1070,
                PopulationIndex = 1.85m,
                BasePrice = 120_000m,
                Price = 120_000m,
                SuitableTypes = "SALES_SHOP,COMMERCIAL"
            },
            new BuildingLot
            {
                Id = CreateDeterministicGuid("lot:ba-commercial-2"),
                CityId = bratislava.Id,
                Name = "Market Square Shop",
                Description = "Corner lot facing the historic market square. Excellent for retail with tourist exposure.",
                District = "Commercial District",
                Latitude = 48.1440, Longitude = 17.1090,
                PopulationIndex = 2.10m,
                BasePrice = 150_000m,
                Price = 150_000m,
                SuitableTypes = "SALES_SHOP,COMMERCIAL"
            },
            new BuildingLot
            {
                Id = CreateDeterministicGuid("lot:ba-commercial-3"),
                CityId = bratislava.Id,
                Name = "Shopping Boulevard Unit",
                Description = "Mid-range retail space on a busy commercial boulevard with steady local traffic.",
                District = "Commercial District",
                Latitude = 48.1460, Longitude = 17.1050,
                PopulationIndex = 1.60m,
                BasePrice = 100_000m,
                Price = 100_000m,
                SuitableTypes = "SALES_SHOP"
            },
            // ── Business Park (northern area) ──
            // Moderate-to-high population index: professional district with daytime footfall.
            new BuildingLot
            {
                Id = CreateDeterministicGuid("lot:ba-business-1"),
                CityId = bratislava.Id,
                Name = "Innovation Campus Office",
                Description = "Modern office complex in the technology business park. Perfect for R&D operations.",
                District = "Business Park",
                Latitude = 48.1560, Longitude = 17.1100,
                PopulationIndex = 1.20m,
                BasePrice = 130_000m,
                Price = 130_000m,
                SuitableTypes = "RESEARCH_DEVELOPMENT,BANK"
            },
            new BuildingLot
            {
                Id = CreateDeterministicGuid("lot:ba-business-2"),
                CityId = bratislava.Id,
                Name = "Financial Center Suite",
                Description = "Premium office space in the financial district. Ideal for banking and exchange operations.",
                District = "Business Park",
                Latitude = 48.1570, Longitude = 17.1060,
                PopulationIndex = 1.40m,
                BasePrice = 200_000m,
                Price = 200_000m,
                SuitableTypes = "BANK,EXCHANGE"
            },
            // ── Residential Quarter (western area) ──
            // Steady population index: consistent local demand from residents.
            new BuildingLot
            {
                Id = CreateDeterministicGuid("lot:ba-residential-1"),
                CityId = bratislava.Id,
                Name = "Riverside Apartment Block",
                Description = "Scenic residential plot overlooking the Danube. Strong rental demand from young professionals.",
                District = "Residential Quarter",
                Latitude = 48.1400, Longitude = 17.1000,
                PopulationIndex = 1.05m,
                BasePrice = 110_000m,
                Price = 110_000m,
                SuitableTypes = "APARTMENT"
            },
            new BuildingLot
            {
                Id = CreateDeterministicGuid("lot:ba-residential-2"),
                CityId = bratislava.Id,
                Name = "Suburban Housing Site",
                Description = "Affordable residential lot in a growing suburban neighborhood. Good long-term rental potential.",
                District = "Residential Quarter",
                Latitude = 48.1380, Longitude = 17.0950,
                PopulationIndex = 0.88m,
                BasePrice = 70_000m,
                Price = 70_000m,
                SuitableTypes = "APARTMENT"
            },
            // ── Media & Cultural District (south-central) ──
            // Moderate population index: near cultural venues with evening and weekend activity.
            new BuildingLot
            {
                Id = CreateDeterministicGuid("lot:ba-media-1"),
                CityId = bratislava.Id,
                Name = "Broadcast Tower Complex",
                Description = "Purpose-built media complex near the cultural center. Ideal for newspaper, radio, or TV operations.",
                District = "Media District",
                Latitude = 48.1420, Longitude = 17.1120,
                PopulationIndex = 1.25m,
                BasePrice = 140_000m,
                Price = 140_000m,
                SuitableTypes = "MEDIA_HOUSE"
            },
            // ── Energy Zone (south-eastern outskirts) ──
            // Low population index: far from residential areas; access to grid infrastructure.
            new BuildingLot
            {
                Id = CreateDeterministicGuid("lot:ba-energy-1"),
                CityId = bratislava.Id,
                Name = "Power Generation Site",
                Description = "Large plot with grid connection capacity for power generation. Zoned for energy infrastructure.",
                District = "Energy Zone",
                Latitude = 48.1350, Longitude = 17.1200,
                PopulationIndex = 0.52m,
                BasePrice = 160_000m,
                Price = 160_000m,
                SuitableTypes = "POWER_PLANT"
            },
            new BuildingLot
            {
                Id = CreateDeterministicGuid("lot:ba-energy-2"),
                CityId = bratislava.Id,
                Name = "Utility Substation Plot",
                Description = "Secondary energy plot suitable for smaller power plants or supplementary generation.",
                District = "Energy Zone",
                Latitude = 48.1360, Longitude = 17.1230,
                PopulationIndex = 0.55m,
                BasePrice = 100_000m,
                Price = 100_000m,
                SuitableTypes = "POWER_PLANT,FACTORY"
            }
        };

        dbContext.BuildingLots.AddRange(lotsToSeed);

        // Apply resource premium: Price = appraised land value + resource deposit premium.
        // This runs in the seeder so every fresh database starts with correct prices.
        // The tick engine recalculates prices on every tick using the same formula.
        foreach (var lot in lotsToSeed)
        {
            var resourcePremium = LandService.ComputeResourcePremium(
                lot.ResourceType, lot.MaterialQuality, lot.MaterialQuantity);
            if (resourcePremium > 0m)
            {
                var appraisedLandValue = LandService.ComputeAppraisedPrice(lot.BasePrice, lot.PopulationIndex);
                lot.Price = appraisedLandValue + resourcePremium;
            }
        }
    }

    private static Guid CreateDeterministicGuid(string key)
    {
        var hash = MD5.HashData(Encoding.UTF8.GetBytes(key));
        return new Guid(hash);
    }

    /// <summary>
    /// Applies the database schema in a way that is safe for three distinct startup scenarios:
    /// <list type="bullet">
    ///   <item><description>
    ///     <b>In-memory database (local development)</b> — EF migrations are not supported by
    ///     the in-memory provider.  <c>EnsureCreatedAsync</c> is used directly; migration steps
    ///     are skipped.
    ///   </description></item>
    ///   <item><description>
    ///     <b>Fresh relational database</b> — <c>EnsureCreatedAsync</c> creates the schema,
    ///     then a baseline entry is inserted into <c>__EFMigrationsHistory</c> for every
    ///     currently-defined migration so that <c>MigrateAsync</c> has nothing left to apply.
    ///   </description></item>
    ///   <item><description>
    ///     <b>Legacy relational database</b> (created by <c>EnsureCreatedAsync</c> before
    ///     migration support was introduced, no <c>__EFMigrationsHistory</c> table) —
    ///     same as fresh path: baseline entries are inserted for all existing migrations so that
    ///     the initial migration is not replayed against tables that already exist.
    ///   </description></item>
    ///   <item><description>
    ///     <b>Relational database already managed by migrations</b> — <c>EnsureCreatedAsync</c>
    ///     is a no-op, <c>EnsureMigrationsHistoryBaselineAsync</c> detects the existing history
    ///     table and returns immediately, and <c>MigrateAsync</c> applies only pending
    ///     migrations.
    ///   </description></item>
    /// </list>
    /// </summary>
    private async Task SafelyApplyMigrationsAsync()
    {
        // In-memory databases (development mode: UseInMemoryDatabase) do not support EF
        // migrations at all — the in-memory provider has no IMigrator service.  Fall back to
        // EnsureCreatedAsync which correctly builds the schema from the current model.
        if (!dbContext.Database.IsRelational())
        {
            await dbContext.Database.EnsureCreatedAsync();
            return;
        }

        // For relational databases (SQLite in production and test environments):
        //   a) EnsureCreatedAsync is idempotent and safe to call even when tables already exist.
        //      It creates the database file and schema when absent and returns false (no-op)
        //      when the database already exists — regardless of whether it was originally
        //      created by EnsureCreated or by a previous migration run.
        await dbContext.Database.EnsureCreatedAsync();

        //   b) If the database has no __EFMigrationsHistory table, every currently-defined
        //      migration is marked as already applied so that MigrateAsync (step c) does not
        //      attempt to re-create tables that already exist.
        var baselinedMigrationsHistory = await EnsureMigrationsHistoryBaselineAsync();

        if (baselinedMigrationsHistory)
        {
            return;
        }

        //   c) Apply any migrations that are not yet recorded in __EFMigrationsHistory.
        //      This is a no-op for brand-new or already up-to-date databases.
        try
        {
            await dbContext.Database.MigrateAsync();
        }
        catch (Exception exc)
        {
            Console.Error.WriteLine($"An error occurred while applying migrations: {exc.Message}");
        }
    }

    /// <summary>
    /// Creates <c>__EFMigrationsHistory</c> and inserts baseline rows for every currently-defined
    /// migration when the table does not exist.  This makes databases that were bootstrapped
    /// with <c>EnsureCreatedAsync</c> (before migration support was introduced) compatible with
    /// <c>MigrateAsync</c> without requiring a database drop-and-recreate.
    ///
    /// If <c>__EFMigrationsHistory</c> already exists the method returns immediately; it never
    /// removes or alters existing history rows.
    /// </summary>
    private async Task<bool> EnsureMigrationsHistoryBaselineAsync()
    {
        // The EF Core migration history table name matches the default used by
        // SqliteHistoryRepository (and other relational providers).
        const string historyTable = "__EFMigrationsHistory";

        // The ProductVersion column records which EF Core version managed each migration.
        // For baseline rows we record the current EF Core runtime version so reviewers can
        // see when the baseline was applied.
        var efProductVersion =
            (System.Reflection.CustomAttributeExtensions.GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>(
                typeof(Microsoft.EntityFrameworkCore.DbContext).Assembly))
            ?.InformationalVersion
            ?? "unknown";

        var connection = dbContext.Database.GetDbConnection();
        var wasOpen = connection.State == System.Data.ConnectionState.Open;

        if (!wasOpen)
            await connection.OpenAsync();

        try
        {
            // Check whether the migrations-history table already exists.
            await using var checkCmd = connection.CreateCommand();
            var provider = dbContext.Database.ProviderName ?? "Unknown";
            string checkQuery;
            if (provider.Contains("PostgreSQL"))
            {
                checkQuery = $"SELECT COUNT(1) FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace WHERE c.relkind = 'r' AND n.nspname = 'public' AND c.relname = '{historyTable}'";
            }
            else
            {
                checkQuery = $"SELECT COUNT(1) FROM sqlite_master WHERE type='table' AND name='{historyTable}'";
            }
            checkCmd.CommandText = checkQuery;
            var historyExists =
                Convert.ToInt64(await checkCmd.ExecuteScalarAsync() ?? 0L) > 0;

            if (historyExists)
                return false; // Already managed by migrations — nothing to do.

            // Create the history table using the SQLite-compatible schema that EF Core generates.
            await using var createCmd = connection.CreateCommand();
            createCmd.CommandText =
                $"""
                CREATE TABLE "{historyTable}" (
                    "MigrationId" TEXT NOT NULL,
                    "ProductVersion" TEXT NOT NULL,
                    CONSTRAINT "PK__{historyTable}" PRIMARY KEY ("MigrationId")
                )
                """;
            await createCmd.ExecuteNonQueryAsync();

            // Baseline: mark every currently-defined migration as already applied.
            // EnsureCreatedAsync (called just before this) already created the full schema
            // corresponding to all migrations up to HEAD, so no migration needs to be
            // replayed.  Future migrations added after this baseline will still be applied
            // correctly by MigrateAsync because they will NOT be in this initial history.
            foreach (var migrationId in dbContext.Database.GetMigrations())
            {
                // Use parameterized queries to prevent any injection risks even though
                // migration IDs come from the compiled assembly (not external input).
                await using var insertCmd = connection.CreateCommand();
                insertCmd.CommandText =
                    $"""
                    INSERT INTO "{historyTable}" ("MigrationId", "ProductVersion")
                    VALUES (@MigrationId, @ProductVersion)
                    """;
                var migParam = insertCmd.CreateParameter();
                migParam.ParameterName = "@MigrationId";
                migParam.Value = migrationId;
                insertCmd.Parameters.Add(migParam);

                var verParam = insertCmd.CreateParameter();
                verParam.ParameterName = "@ProductVersion";
                verParam.Value = efProductVersion;
                insertCmd.Parameters.Add(verParam);

                await insertCmd.ExecuteNonQueryAsync();
            }

            return true;
        }
        finally
        {
            if (!wasOpen)
                connection.Close();
        }
    }

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
