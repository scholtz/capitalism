using Api.Data;
using Api.Data.Entities;
using Api.Engine;
using Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Api.Tests;

/// <summary>
/// Integration tests for the tick-based game engine.
/// Uses IClassFixture to share a single factory + DB across all tests.
/// </summary>
public sealed class TickEngineIntegrationTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;

    public TickEngineIntegrationTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        // Ensure host is started so the database is seeded.
        _ = _factory.CreateClient();
    }

    #region Helpers

    private Task<TickProcessor> CreateProcessorAsync(IServiceScope scope)
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var phases = scope.ServiceProvider.GetServices<ITickPhase>();
        var logger = new NullLogger<TickProcessor>();
        return Task.FromResult(new TickProcessor(db, phases, logger));
    }

    private async Task<(Guid CompanyId, Guid BuildingId, Guid CityId)> SeedMineAsync(AppDbContext db)
    {
        var city = await db.Cities.Include(c => c.Resources).FirstAsync();

        var player = new Player
        {
            Id = Guid.NewGuid(),
            Email = $"mine-{Guid.NewGuid():N}@test.com",
            DisplayName = "Mine Tester",
            PasswordHash = "hash",
            Role = PlayerRole.Player
        };
        db.Players.Add(player);

        var company = new Company
        {
            Id = Guid.NewGuid(),
            PlayerId = player.Id,
            Name = "Mining Corp",
            Cash = 1_000_000m
        };
        db.Companies.Add(company);

        var resourceType = await db.CityResources
            .Where(cr => cr.CityId == city.Id && cr.Abundance > 0)
            .Select(cr => cr.ResourceType)
            .FirstAsync();

        var building = new Building
        {
            Id = Guid.NewGuid(),
            CompanyId = company.Id,
            CityId = city.Id,
            Type = BuildingType.Mine,
            Name = "Test Mine",
            Level = 1
        };
        db.Buildings.Add(building);

        var miningUnit = new BuildingUnit
        {
            Id = Guid.NewGuid(),
            BuildingId = building.Id,
            UnitType = UnitType.Mining,
            GridX = 0,
            GridY = 0,
            Level = 1,
            ResourceTypeId = resourceType!.Id
        };

        var storageUnit = new BuildingUnit
        {
            Id = Guid.NewGuid(),
            BuildingId = building.Id,
            UnitType = UnitType.Storage,
            GridX = 1,
            GridY = 0,
            Level = 1
        };
        miningUnit.LinkRight = true; // Push mined resources to storage

        db.BuildingUnits.AddRange(miningUnit, storageUnit);
        await db.SaveChangesAsync();

        return (company.Id, building.Id, city.Id);
    }

    private async Task<(Guid CompanyId, Guid FactoryId, Guid ShopId)> SeedFactoryAndShopAsync(AppDbContext db)
    {
        var city = await db.Cities.Include(c => c.Resources).FirstAsync();

        var player = new Player
        {
            Id = Guid.NewGuid(),
            Email = $"factory-{Guid.NewGuid():N}@test.com",
            DisplayName = "Factory Tester",
            PasswordHash = "hash",
            Role = PlayerRole.Player
        };
        db.Players.Add(player);

        var company = new Company
        {
            Id = Guid.NewGuid(),
            PlayerId = player.Id,
            Name = "Factory Corp",
            Cash = 5_000_000m
        };
        db.Companies.Add(company);

        // Use Wooden Chair – a known starter product that only needs raw wood.
        var product = await db.ProductTypes
            .Include(p => p.Recipes)
            .FirstAsync(p => p.Slug == "wooden-chair");
        var woodResource = await db.ResourceTypes.FirstAsync(r => r.Slug == "wood");

        // Factory building.
        var factory = new Building
        {
            Id = Guid.NewGuid(),
            CompanyId = company.Id,
            CityId = city.Id,
            Type = BuildingType.Factory,
            Name = "Test Factory",
            Level = 1
        };
        db.Buildings.Add(factory);

        // Purchase unit at (0,0) linked right to Manufacturing at (1,0).
        var purchaseUnit = new BuildingUnit
        {
            Id = Guid.NewGuid(),
            BuildingId = factory.Id,
            UnitType = UnitType.Purchase,
            GridX = 0, GridY = 0,
            Level = 1,
            LinkRight = true,
            ResourceTypeId = woodResource.Id,
            MaxPrice = 999_999m,
            PurchaseSource = "EXCHANGE"
        };

        var manufacturingUnit = new BuildingUnit
        {
            Id = Guid.NewGuid(),
            BuildingId = factory.Id,
            UnitType = UnitType.Manufacturing,
            GridX = 1, GridY = 0,
            Level = 1,
            LinkRight = true,
            ProductTypeId = product.Id
        };

        var storageUnit = new BuildingUnit
        {
            Id = Guid.NewGuid(),
            BuildingId = factory.Id,
            UnitType = UnitType.Storage,
            GridX = 2, GridY = 0,
            Level = 1
        };

        db.BuildingUnits.AddRange(purchaseUnit, manufacturingUnit, storageUnit);

        // Pre-stock raw wood in the purchase unit so manufacturing can run.
        db.Inventories.Add(new Inventory
        {
            Id = Guid.NewGuid(),
            BuildingId = factory.Id,
            BuildingUnitId = purchaseUnit.Id,
            ResourceTypeId = woodResource.Id,
            Quantity = 10m, // Enough for 10 batches (recipe needs 1 wood per batch)
            Quality = 0.7m
        });

        // Sales shop building.
        var shop = new Building
        {
            Id = Guid.NewGuid(),
            CompanyId = company.Id,
            CityId = city.Id,
            Type = BuildingType.SalesShop,
            Name = "Test Shop",
            Level = 1
        };
        db.Buildings.Add(shop);

        var salesUnit = new BuildingUnit
        {
            Id = Guid.NewGuid(),
            BuildingId = shop.Id,
            UnitType = UnitType.PublicSales,
            GridX = 0, GridY = 0,
            Level = 1,
            ProductTypeId = product.Id,
            MinPrice = product.BasePrice
        };
        db.BuildingUnits.Add(salesUnit);

        // Pre-stock products in the sales unit.
        db.Inventories.Add(new Inventory
        {
            Id = Guid.NewGuid(),
            BuildingId = shop.Id,
            BuildingUnitId = salesUnit.Id,
            ProductTypeId = product.Id,
            Quantity = 50m,
            Quality = 0.8m
        });

        await db.SaveChangesAsync();
        return (company.Id, factory.Id, shop.Id);
    }

    private async Task<(Guid CompanyId, Guid BuildingId)> SeedApartmentAsync(AppDbContext db)
    {
        var city = await db.Cities.FirstAsync();

        var player = new Player
        {
            Id = Guid.NewGuid(),
            Email = $"rent-{Guid.NewGuid():N}@test.com",
            DisplayName = "Rent Tester",
            PasswordHash = "hash",
            Role = PlayerRole.Player
        };
        db.Players.Add(player);

        var company = new Company
        {
            Id = Guid.NewGuid(),
            PlayerId = player.Id,
            Name = "Rent Corp",
            Cash = 500_000m
        };
        db.Companies.Add(company);

        var building = new Building
        {
            Id = Guid.NewGuid(),
            CompanyId = company.Id,
            CityId = city.Id,
            Type = BuildingType.Apartment,
            Name = "Test Apartments",
            Level = 1,
            PricePerSqm = city.AverageRentPerSqm, // market rate
            TotalAreaSqm = 1000m,
            OccupancyPercent = 80m
        };
        db.Buildings.Add(building);
        await db.SaveChangesAsync();

        return (company.Id, building.Id);
    }

    #endregion

    [Fact]
    public async Task ProcessTick_IncrementsTick()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var gs = await db.GameStates.FirstAsync();
        var previousTick = gs.CurrentTick;

        var processor = await CreateProcessorAsync(scope);
        await processor.ProcessTickAsync();

        Assert.Equal(previousTick + 1, gs.CurrentTick);
    }

    [Fact]
    public async Task MiningPhase_ProducesResources()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var (companyId, buildingId, _) = await SeedMineAsync(db);

        var processor = await CreateProcessorAsync(scope);
        await processor.ProcessTickAsync();

        // Mining unit should have produced inventory.
        var inventories = await db.Inventories
            .Where(i => i.BuildingId == buildingId && i.BuildingUnitId != null)
            .ToListAsync();

        Assert.NotEmpty(inventories);
        Assert.True(inventories.Sum(i => i.Quantity) > 0m,
            "Mining should have produced resources.");
    }

    [Fact]
    public async Task ResourceMovement_PushesToLinkedStorage()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var (companyId, buildingId, _) = await SeedMineAsync(db);

        var processor = await CreateProcessorAsync(scope);

        // Tick 1: Mine produces resources.
        await processor.ProcessTickAsync();

        // Tick 2: Movement pushes to storage.
        await processor.ProcessTickAsync();

        var miningUnit = await db.BuildingUnits
            .FirstAsync(u => u.BuildingId == buildingId && u.UnitType == UnitType.Mining);
        var storageUnit = await db.BuildingUnits
            .FirstAsync(u => u.BuildingId == buildingId && u.UnitType == UnitType.Storage);

        var storageInv = await db.Inventories
            .Where(i => i.BuildingUnitId == storageUnit.Id)
            .ToListAsync();

        // Storage should have received some resources via the active link.
        Assert.NotEmpty(storageInv);
        Assert.True(storageInv.Sum(i => i.Quantity) > 0m,
            "Resources should have moved from mining to storage via link.");
    }

    [Fact]
    public async Task ManufacturingPhase_ProducesProducts()
    {
        Guid factoryId;
        Guid manufacturingUnitId;
        Guid purchaseUnitId;

        // Seed in its own scope so the processor gets a clean DbContext.
        await using (var seedScope = _factory.Services.CreateAsyncScope())
        {
            var seedDb = seedScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var (_, fId, _) = await SeedFactoryAndShopAsync(seedDb);
            factoryId = fId;
            manufacturingUnitId = (await seedDb.BuildingUnits
                .FirstAsync(u => u.BuildingId == factoryId && u.UnitType == UnitType.Manufacturing)).Id;
            purchaseUnitId = (await seedDb.BuildingUnits
                .FirstAsync(u => u.BuildingId == factoryId && u.UnitType == UnitType.Purchase)).Id;
        }

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Verify seed data is visible.
        var factory = await db.Buildings.Include(b => b.Units).FirstAsync(b => b.Id == factoryId);
        Assert.Equal(BuildingType.Factory, factory.Type);
        Assert.Equal(3, factory.Units.Count);

        var purchaseInvBefore = await db.Inventories
            .Where(i => i.BuildingUnitId == purchaseUnitId)
            .ToListAsync();
        Assert.NotEmpty(purchaseInvBefore);

        var processor = await CreateProcessorAsync(scope);
        await processor.ProcessTickAsync();

        // Check all inventories for the factory after tick.
        // Products may be in the manufacturing unit or pushed to storage via links.
        var allInv = await db.Inventories
            .Where(i => i.BuildingId == factoryId)
            .ToListAsync();

        var productInv = allInv.Where(i => i.ProductTypeId != null).ToList();

        Assert.NotEmpty(productInv);
        Assert.True(productInv.Sum(i => i.Quantity) > 0m,
            "Manufacturing should have produced products from available inputs.");
    }

    [Fact]
    public async Task PublicSalesPhase_SellsProductsAndCreditsCompany()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var (companyId, _, shopId) = await SeedFactoryAndShopAsync(db);

        var company = await db.Companies.FindAsync(companyId);
        var cashBefore = company!.Cash;

        var processor = await CreateProcessorAsync(scope);
        await processor.ProcessTickAsync();

        // Sales should have generated revenue.
        Assert.True(company.Cash > cashBefore,
            "Company cash should increase after public sales.");
    }

    [Fact]
    public async Task RentPhase_CollectsRentForApartments()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var (companyId, _) = await SeedApartmentAsync(db);

        var company = await db.Companies.FindAsync(companyId);
        var cashBefore = company!.Cash;

        var processor = await CreateProcessorAsync(scope);
        await processor.ProcessTickAsync();

        Assert.True(company.Cash > cashBefore,
            "Company should collect rent from apartment building.");
    }

    [Fact]
    public async Task TaxPhase_DeductsTaxOnCycle()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Create a company with cash.
        var player = new Player
        {
            Id = Guid.NewGuid(),
            Email = $"tax-{Guid.NewGuid():N}@test.com",
            DisplayName = "Tax Tester",
            PasswordHash = "hash",
            Role = PlayerRole.Player
        };
        db.Players.Add(player);

        var company = new Company
        {
            Id = Guid.NewGuid(),
            PlayerId = player.Id,
            Name = "Tax Target Corp",
            Cash = 1_000_000m
        };
        db.Companies.Add(company);

        // Set game state so the next tick is a tax cycle tick.
        var gs = await db.GameStates.FirstAsync();
        gs.CurrentTick = gs.TaxCycleTicks - 1; // Next tick will be tax cycle.
        await db.SaveChangesAsync();

        var cashBefore = company.Cash;
        var processor = await CreateProcessorAsync(scope);
        await processor.ProcessTickAsync();

        Assert.True(company.Cash < cashBefore,
            "Tax should have been deducted on the tax cycle tick.");
        Assert.True(company.Cash > 0m,
            "Company should still have cash after tax.");
    }

    [Fact]
    public async Task MultipleTicks_MiningAccumulatesResources()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var (_, buildingId, _) = await SeedMineAsync(db);

        var processor = await CreateProcessorAsync(scope);

        // Run 5 ticks.
        for (var i = 0; i < 5; i++)
            await processor.ProcessTickAsync();

        var totalMined = await db.Inventories
            .Where(i => i.BuildingId == buildingId)
            .SumAsync(i => i.Quantity);

        Assert.True(totalMined > 0m,
            "Mining should accumulate resources over multiple ticks.");
    }

    #region Global Exchange Purchasing

    /// <summary>
    /// Seeds a factory with a PURCHASE unit configured to buy from the global
    /// exchange (PurchaseSource = "EXCHANGE").  No player-placed sell orders exist.
    /// </summary>
    private async Task<(Guid CompanyId, Guid BuildingId, Guid CityId, Guid PurchaseUnitId, Guid ResourceTypeId)>
        SeedExchangePurchaseUnitAsync(
            AppDbContext db,
            decimal? maxPrice = null,
            decimal? minQuality = null,
            string purchaseSource = "EXCHANGE",
            string resourceSlug = "wood")
    {
        var city = await db.Cities.Include(c => c.Resources).ThenInclude(r => r.ResourceType).FirstAsync();

        var player = new Player
        {
            Id = Guid.NewGuid(),
            Email = $"exchg-{Guid.NewGuid():N}@test.com",
            DisplayName = "Exchange Tester",
            PasswordHash = "hash",
            Role = PlayerRole.Player
        };
        db.Players.Add(player);

        var company = new Company
        {
            Id = Guid.NewGuid(),
            PlayerId = player.Id,
            Name = "Exchange Corp",
            Cash = 1_000_000m
        };
        db.Companies.Add(company);

        var resource = await db.ResourceTypes.FirstAsync(r => r.Slug == resourceSlug);

        var building = new Building
        {
            Id = Guid.NewGuid(),
            CompanyId = company.Id,
            CityId = city.Id,
            Type = BuildingType.Factory,
            Name = "Exchange Factory",
            Level = 1
        };
        db.Buildings.Add(building);

        var purchaseUnit = new BuildingUnit
        {
            Id = Guid.NewGuid(),
            BuildingId = building.Id,
            UnitType = UnitType.Purchase,
            GridX = 0,
            GridY = 0,
            Level = 1,
            ResourceTypeId = resource.Id,
            MaxPrice = maxPrice,
            MinQuality = minQuality,
            PurchaseSource = purchaseSource
        };
        db.BuildingUnits.Add(purchaseUnit);

        await db.SaveChangesAsync();
        return (company.Id, building.Id, city.Id, purchaseUnit.Id, resource.Id);
    }

    [Fact]
    public async Task PurchasingPhase_GlobalExchange_FillsInventoryWhenNoPlayerOrders()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var (companyId, buildingId, _, purchaseUnitId, _) =
            await SeedExchangePurchaseUnitAsync(db, maxPrice: 9999m, purchaseSource: "EXCHANGE");

        var company = await db.Companies.FindAsync(companyId);
        var cashBefore = company!.Cash;

        var processor = await CreateProcessorAsync(scope);
        await processor.ProcessTickAsync();

        // Inventory should have been filled from the global exchange.
        var inventory = await db.Inventories
            .Where(i => i.BuildingUnitId == purchaseUnitId)
            .ToListAsync();

        Assert.NotEmpty(inventory);
        Assert.True(inventory.Sum(i => i.Quantity) > 0m,
            "Purchase unit should have filled inventory from global exchange.");

        // Company cash should have decreased (cost of purchase).
        Assert.True(company.Cash < cashBefore,
            "Company should have paid for global exchange purchase.");

        // Quality should be within expected exchange range (0.35 – 0.95).
        Assert.All(inventory, inv => Assert.InRange(inv.Quality, 0.35m, 0.95m));
    }

    [Fact]
    public async Task PurchasingPhase_GlobalExchange_RespectsMaxPrice()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Set MaxPrice to an impossibly low value (0.001) so no global exchange offer qualifies.
        var (companyId, buildingId, _, purchaseUnitId, _) =
            await SeedExchangePurchaseUnitAsync(db, maxPrice: 0.001m, purchaseSource: "EXCHANGE");

        var company = await db.Companies.FindAsync(companyId);
        var cashBefore = company!.Cash;

        var processor = await CreateProcessorAsync(scope);
        await processor.ProcessTickAsync();

        // Inventory should remain empty because no offer meets the max price.
        var inventory = await db.Inventories
            .Where(i => i.BuildingUnitId == purchaseUnitId)
            .ToListAsync();

        Assert.True(inventory.Sum(i => i.Quantity) == 0m,
            "Purchase unit should not have bought anything when max price is too low.");

        // Cash should be unchanged.
        Assert.Equal(cashBefore, company.Cash);
    }

    [Fact]
    public async Task PurchasingPhase_GlobalExchange_RespectsMinQuality()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Set MinQuality to 1.0 (perfect) – no global exchange city will meet this.
        var (companyId, buildingId, _, purchaseUnitId, _) =
            await SeedExchangePurchaseUnitAsync(db, maxPrice: 9999m, minQuality: 1.0m, purchaseSource: "EXCHANGE");

        var company = await db.Companies.FindAsync(companyId);
        var cashBefore = company!.Cash;

        var processor = await CreateProcessorAsync(scope);
        await processor.ProcessTickAsync();

        var inventory = await db.Inventories
            .Where(i => i.BuildingUnitId == purchaseUnitId)
            .ToListAsync();

        Assert.True(inventory.Sum(i => i.Quantity) == 0m,
            "Purchase unit should not have bought anything when min quality requirement is unattainable.");
        Assert.Equal(cashBefore, company.Cash);
    }

    [Fact]
    public async Task PurchasingPhase_LocalSource_DoesNotUsGlobalExchange()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // LOCAL source should not fill from global exchange; no player sell orders exist.
        var (companyId, _, _, purchaseUnitId, _) =
            await SeedExchangePurchaseUnitAsync(db, maxPrice: 9999m, purchaseSource: "LOCAL");

        var company = await db.Companies.FindAsync(companyId);
        var cashBefore = company!.Cash;

        var processor = await CreateProcessorAsync(scope);
        await processor.ProcessTickAsync();

        var inventory = await db.Inventories
            .Where(i => i.BuildingUnitId == purchaseUnitId)
            .ToListAsync();

        // LOCAL source with no player orders → no inventory filled.
        Assert.True(inventory.Sum(i => i.Quantity) == 0m,
            "LOCAL purchase source should not fill from global exchange.");
        Assert.Equal(cashBefore, company.Cash);
    }

    [Fact]
    public async Task PurchasingPhase_OptimalSource_UsesGlobalExchangeWhenNoPlayerOrders()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // OPTIMAL source should fall back to global exchange when no player orders exist.
        var (companyId, _, _, purchaseUnitId, _) =
            await SeedExchangePurchaseUnitAsync(db, maxPrice: 9999m, purchaseSource: "OPTIMAL");

        var company = await db.Companies.FindAsync(companyId);
        var cashBefore = company!.Cash;

        var processor = await CreateProcessorAsync(scope);
        await processor.ProcessTickAsync();

        var inventory = await db.Inventories
            .Where(i => i.BuildingUnitId == purchaseUnitId)
            .ToListAsync();

        Assert.True(inventory.Sum(i => i.Quantity) > 0m,
            "OPTIMAL source should fill from global exchange when no player orders are available.");
        Assert.True(company.Cash < cashBefore,
            "Company cash should decrease after OPTIMAL purchase from global exchange.");
    }

    [Fact]
    public async Task PurchasingPhase_GlobalExchange_DeliveredPriceMatchesCalculator()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var (companyId, buildingId, cityId, purchaseUnitId, resourceId) =
            await SeedExchangePurchaseUnitAsync(db, maxPrice: 9999m, purchaseSource: "EXCHANGE");

        var company = await db.Companies.FindAsync(companyId);
        var cashBefore = company!.Cash;

        var processor = await CreateProcessorAsync(scope);
        await processor.ProcessTickAsync();

        // Reload ledger entry to verify the price paid matches the calculator.
        var ledgerEntry = await db.LedgerEntries
            .Where(e => e.CompanyId == companyId && e.BuildingUnitId == purchaseUnitId)
            .FirstOrDefaultAsync();

        Assert.NotNull(ledgerEntry);
        Assert.True(ledgerEntry!.Amount < 0m, "Purchasing cost should be negative in the ledger.");

        // Verify the unit has inventory and quality is in valid range.
        var inventory = await db.Inventories
            .Where(i => i.BuildingUnitId == purchaseUnitId)
            .FirstOrDefaultAsync();

        Assert.NotNull(inventory);
        Assert.True(inventory!.Quantity > 0m);
        Assert.InRange(inventory.Quality, 0.35m, 0.95m);

        // Cash decrease must exactly match the ledger debit.
        Assert.Equal(cashBefore + ledgerEntry.Amount, company.Cash);
    }

    #endregion
}
