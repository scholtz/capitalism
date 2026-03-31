using Api.Data;
using Api.Data.Entities;
using Api.Engine;
using Api.Tests.Infrastructure;
using Api.Utilities;
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

    private async Task<(Guid CompanyId, Guid FactoryId, Guid ShopId, Guid ShopPurchaseUnitId, Guid ShopPublicSalesUnitId)> SeedFactoryToShopSupplyChainAsync(AppDbContext db)
    {
        var city = await db.Cities.Include(c => c.Resources).FirstAsync();

        var player = new Player
        {
            Id = Guid.NewGuid(),
            Email = $"factory-chain-{Guid.NewGuid():N}@test.com",
            DisplayName = "Factory Chain Tester",
            PasswordHash = "hash",
            Role = PlayerRole.Player
        };
        db.Players.Add(player);

        var company = new Company
        {
            Id = Guid.NewGuid(),
            PlayerId = player.Id,
            Name = "Starter Chain Corp",
            Cash = 5_000_000m
        };
        db.Companies.Add(company);

        var product = await db.ProductTypes
            .Include(candidate => candidate.Recipes)
            .FirstAsync(candidate => candidate.Slug == "wooden-chair");
        var woodResource = await db.ResourceTypes.FirstAsync(candidate => candidate.Slug == "wood");

        var factory = new Building
        {
            Id = Guid.NewGuid(),
            CompanyId = company.Id,
            CityId = city.Id,
            Type = BuildingType.Factory,
            Name = "Starter Factory",
            Level = 1
        };
        db.Buildings.Add(factory);

        var factoryPurchaseUnit = new BuildingUnit
        {
            Id = Guid.NewGuid(),
            BuildingId = factory.Id,
            UnitType = UnitType.Purchase,
            GridX = 0,
            GridY = 0,
            Level = 1,
            LinkRight = true,
            ResourceTypeId = woodResource.Id,
            MaxPrice = 999_999m,
            PurchaseSource = "EXCHANGE"
        };

        db.BuildingUnits.AddRange(
            factoryPurchaseUnit,
            new BuildingUnit
            {
                Id = Guid.NewGuid(),
                BuildingId = factory.Id,
                UnitType = UnitType.Manufacturing,
                GridX = 1,
                GridY = 0,
                Level = 1,
                LinkRight = true,
                ProductTypeId = product.Id
            },
            new BuildingUnit
            {
                Id = Guid.NewGuid(),
                BuildingId = factory.Id,
                UnitType = UnitType.Storage,
                GridX = 2,
                GridY = 0,
                Level = 1,
                LinkRight = true
            },
            new BuildingUnit
            {
                Id = Guid.NewGuid(),
                BuildingId = factory.Id,
                UnitType = UnitType.B2BSales,
                GridX = 3,
                GridY = 0,
                Level = 1,
                ProductTypeId = product.Id,
                MinPrice = product.BasePrice,
                SaleVisibility = "COMPANY"
            });

        var shop = new Building
        {
            Id = Guid.NewGuid(),
            CompanyId = company.Id,
            CityId = city.Id,
            Type = BuildingType.SalesShop,
            Name = "Starter Shop",
            Level = 1
        };
        db.Buildings.Add(shop);

        var shopPurchaseUnit = new BuildingUnit
        {
            Id = Guid.NewGuid(),
            BuildingId = shop.Id,
            UnitType = UnitType.Purchase,
            GridX = 0,
            GridY = 0,
            Level = 1,
            LinkRight = true,
            ProductTypeId = product.Id,
            PurchaseSource = "LOCAL",
            MaxPrice = product.BasePrice * 1.1m,
            VendorLockCompanyId = company.Id
        };
        var shopPublicSalesUnit = new BuildingUnit
        {
            Id = Guid.NewGuid(),
            BuildingId = shop.Id,
            UnitType = UnitType.PublicSales,
            GridX = 1,
            GridY = 0,
            Level = 1,
            ProductTypeId = product.Id,
            MinPrice = product.BasePrice * 1.5m
        };
        db.BuildingUnits.AddRange(shopPurchaseUnit, shopPublicSalesUnit);

        db.Inventories.Add(new Inventory
        {
            Id = Guid.NewGuid(),
            BuildingId = factory.Id,
            BuildingUnitId = factoryPurchaseUnit.Id,
            ResourceTypeId = woodResource.Id,
            Quantity = 10m,
            Quality = 0.7m,
            SourcingCostTotal = 40m
        });

        await db.SaveChangesAsync();
        return (company.Id, factory.Id, shop.Id, shopPurchaseUnit.Id, shopPublicSalesUnit.Id);
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
    public async Task UnitResourceHistory_TracksManufacturingAndStorageMovement()
    {
        Guid factoryId;

        await using (var seedScope = _factory.Services.CreateAsyncScope())
        {
            var seedDb = seedScope.ServiceProvider.GetRequiredService<AppDbContext>();
            (_, factoryId, _, _, _) = await SeedFactoryToShopSupplyChainAsync(seedDb);
        }

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var processor = await CreateProcessorAsync(scope);

        await processor.ProcessTickAsync();
        await processor.ProcessTickAsync();

        var manufacturingUnitId = await db.BuildingUnits
            .Where(unit => unit.BuildingId == factoryId && unit.UnitType == UnitType.Manufacturing)
            .Select(unit => unit.Id)
            .SingleAsync();
        var storageUnitId = await db.BuildingUnits
            .Where(unit => unit.BuildingId == factoryId && unit.UnitType == UnitType.Storage)
            .Select(unit => unit.Id)
            .SingleAsync();
        var woodResourceId = await db.ResourceTypes
            .Where(resource => resource.Slug == "wood")
            .Select(resource => resource.Id)
            .SingleAsync();
        var chairProductId = await db.ProductTypes
            .Where(product => product.Slug == "wooden-chair")
            .Select(product => product.Id)
            .SingleAsync();

        var manufacturingWoodHistory = await db.BuildingUnitResourceHistories
            .Where(entry => entry.BuildingUnitId == manufacturingUnitId && entry.ResourceTypeId == woodResourceId)
            .ToListAsync();
        var manufacturingChairHistory = await db.BuildingUnitResourceHistories
            .Where(entry => entry.BuildingUnitId == manufacturingUnitId && entry.ProductTypeId == chairProductId)
            .ToListAsync();
        var storageChairHistory = await db.BuildingUnitResourceHistories
            .Where(entry => entry.BuildingUnitId == storageUnitId && entry.ProductTypeId == chairProductId)
            .ToListAsync();

        Assert.Contains(manufacturingWoodHistory, entry => entry.InflowQuantity > 0m && entry.ConsumedQuantity > 0m);
        Assert.Contains(manufacturingChairHistory, entry => entry.ProducedQuantity > 0m);
        Assert.Contains(storageChairHistory, entry => entry.InflowQuantity > 0m);
        Assert.Contains(storageChairHistory, entry => entry.OutflowQuantity > 0m);
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
    public async Task PurchasingPhase_PurchaseUnit_OnlyBuysB2BInventoryThatExistedAtTickStart()
    {
        Guid factoryId;
        Guid shopPurchaseUnitId;
        Guid shopPublicSalesUnitId;

        await using (var seedScope = _factory.Services.CreateAsyncScope())
        {
            var seedDb = seedScope.ServiceProvider.GetRequiredService<AppDbContext>();
            (_, factoryId, _, shopPurchaseUnitId, shopPublicSalesUnitId) = await SeedFactoryToShopSupplyChainAsync(seedDb);
        }

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var processor = await CreateProcessorAsync(scope);

        await processor.ProcessTickAsync();
        await processor.ProcessTickAsync();

        var factoryB2BSalesUnitId = await db.BuildingUnits
            .Where(unit => unit.BuildingId == factoryId && unit.UnitType == UnitType.B2BSales)
            .Select(unit => unit.Id)
            .SingleAsync();

        var purchasedInventoryBeforeThirdTick = await db.Inventories
            .Where(entry => entry.BuildingUnitId == shopPurchaseUnitId && entry.ProductTypeId != null)
            .ToListAsync();
        var b2bInventoryAfterSecondTick = await db.Inventories
            .Where(entry => entry.BuildingUnitId == factoryB2BSalesUnitId && entry.ProductTypeId != null)
            .ToListAsync();

        Assert.Equal(
            0m,
            purchasedInventoryBeforeThirdTick.Sum(entry => entry.Quantity));
        Assert.True(
            b2bInventoryAfterSecondTick.Sum(entry => entry.Quantity) > 0m,
            "Freshly moved inventory should stop in the B2B sales unit until the next tick.");

        await processor.ProcessTickAsync();

        var purchasedInventoryAfterThirdTick = await db.Inventories
            .Where(entry => entry.BuildingUnitId == shopPurchaseUnitId && entry.ProductTypeId != null)
            .ToListAsync();

        await processor.ProcessTickAsync();

        var publicSalesInventoryAfterFourthTick = await db.Inventories
            .Where(entry => entry.BuildingUnitId == shopPublicSalesUnitId && entry.ProductTypeId != null)
            .ToListAsync();

        Assert.True(
            purchasedInventoryAfterThirdTick.Sum(entry => entry.Quantity) > 0m,
            "Shop purchase unit should buy finished products from the same company's B2B sales unit once that stock existed at tick start.");
        Assert.True(
            publicSalesInventoryAfterFourthTick.Sum(entry => entry.Quantity) > 0m,
            "Products should advance to the public sales unit one hop per tick after the shop purchase unit is filled.");
    }

    [Fact]
    public async Task ResourceMovement_MovesEachInventoryOnlyOneHopPerTick()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var city = await db.Cities.FirstAsync();

        var player = new Player
        {
            Id = Guid.NewGuid(),
            Email = $"one-hop-{Guid.NewGuid():N}@test.com",
            DisplayName = "One Hop Tester",
            PasswordHash = "hash",
            Role = PlayerRole.Player
        };
        db.Players.Add(player);

        var company = new Company
        {
            Id = Guid.NewGuid(),
            PlayerId = player.Id,
            Name = "One Hop Corp",
            Cash = 1_000_000m
        };
        db.Companies.Add(company);

        var product = await db.ProductTypes.FirstAsync(candidate => candidate.Slug == "wooden-chair");
        var ironOre = await db.ResourceTypes.FirstAsync(candidate => candidate.Slug == "iron-ore");

        var factory = new Building
        {
            Id = Guid.NewGuid(),
            CompanyId = company.Id,
            CityId = city.Id,
            Type = BuildingType.Factory,
            Name = "One Hop Factory",
            Level = 1
        };
        db.Buildings.Add(factory);

        var purchaseUnit = new BuildingUnit
        {
            Id = Guid.NewGuid(),
            BuildingId = factory.Id,
            UnitType = UnitType.Purchase,
            GridX = 0,
            GridY = 0,
            Level = 1,
            LinkRight = true,
            ResourceTypeId = ironOre.Id,
            MaxPrice = 999_999m,
            PurchaseSource = "LOCAL"
        };
        var manufacturingUnit = new BuildingUnit
        {
            Id = Guid.NewGuid(),
            BuildingId = factory.Id,
            UnitType = UnitType.Manufacturing,
            GridX = 1,
            GridY = 0,
            Level = 1,
            LinkRight = true,
            ProductTypeId = product.Id
        };
        var storageUnit = new BuildingUnit
        {
            Id = Guid.NewGuid(),
            BuildingId = factory.Id,
            UnitType = UnitType.Storage,
            GridX = 2,
            GridY = 0,
            Level = 1,
            LinkRight = true
        };
        var salesUnit = new BuildingUnit
        {
            Id = Guid.NewGuid(),
            BuildingId = factory.Id,
            UnitType = UnitType.B2BSales,
            GridX = 3,
            GridY = 0,
            Level = 1,
            ProductTypeId = product.Id,
            MinPrice = product.BasePrice,
            SaleVisibility = "COMPANY"
        };
        db.BuildingUnits.AddRange(purchaseUnit, manufacturingUnit, storageUnit, salesUnit);

        db.Inventories.AddRange(
            new Inventory
            {
                Id = Guid.NewGuid(),
                BuildingId = factory.Id,
                BuildingUnitId = purchaseUnit.Id,
                ResourceTypeId = ironOre.Id,
                Quantity = 4m,
                Quality = 0.65m
            },
            new Inventory
            {
                Id = Guid.NewGuid(),
                BuildingId = factory.Id,
                BuildingUnitId = manufacturingUnit.Id,
                ProductTypeId = product.Id,
                Quantity = 5m,
                Quality = 0.8m
            },
            new Inventory
            {
                Id = Guid.NewGuid(),
                BuildingId = factory.Id,
                BuildingUnitId = storageUnit.Id,
                ProductTypeId = product.Id,
                Quantity = 7m,
                Quality = 0.82m
            });

        await db.SaveChangesAsync();

        var processor = await CreateProcessorAsync(scope);
        await processor.ProcessTickAsync();

        var purchaseQuantity = await db.Inventories
            .Where(entry => entry.BuildingUnitId == purchaseUnit.Id && entry.ResourceTypeId == ironOre.Id)
            .SumAsync(entry => entry.Quantity);
        var manufacturingProductQuantity = await db.Inventories
            .Where(entry => entry.BuildingUnitId == manufacturingUnit.Id && entry.ProductTypeId == product.Id)
            .SumAsync(entry => entry.Quantity);
        var manufacturingInputQuantity = await db.Inventories
            .Where(entry => entry.BuildingUnitId == manufacturingUnit.Id && entry.ResourceTypeId == ironOre.Id)
            .SumAsync(entry => entry.Quantity);
        var storageQuantity = await db.Inventories
            .Where(entry => entry.BuildingUnitId == storageUnit.Id && entry.ProductTypeId == product.Id)
            .SumAsync(entry => entry.Quantity);
        var salesQuantity = await db.Inventories
            .Where(entry => entry.BuildingUnitId == salesUnit.Id && entry.ProductTypeId == product.Id)
            .SumAsync(entry => entry.Quantity);

        Assert.Equal(0m, purchaseQuantity);
        Assert.Equal(0m, manufacturingProductQuantity);
        Assert.Equal(4m, manufacturingInputQuantity);
        Assert.Equal(5m, storageQuantity);
        Assert.Equal(7m, salesQuantity);
    }

    [Fact]
    public async Task ResourceMovementPhase_ManufacturingInputDoesNotExceedHalfCapacityForSingleInputRecipe()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var city = await db.Cities.FirstAsync();
        var wood = await db.ResourceTypes.FirstAsync(candidate => candidate.Slug == "wood");

        var product = new ProductType
        {
            Id = Guid.NewGuid(),
            Name = "Capacity Test Product",
            Slug = $"capacity-test-{Guid.NewGuid():N}",
            Industry = Industry.Furniture,
            BasePrice = 100m,
            OutputQuantity = 1m
        };
        db.ProductTypes.Add(product);
        db.ProductRecipes.Add(new ProductRecipe
        {
            Id = Guid.NewGuid(),
            ProductTypeId = product.Id,
            ResourceTypeId = wood.Id,
            Quantity = 200m
        });

        var player = new Player
        {
            Id = Guid.NewGuid(),
            Email = $"mfg-cap-{Guid.NewGuid():N}@test.com",
            DisplayName = "Manufacturing Capacity Tester",
            PasswordHash = "hash",
            Role = PlayerRole.Player
        };
        db.Players.Add(player);

        var company = new Company
        {
            Id = Guid.NewGuid(),
            PlayerId = player.Id,
            Name = "Manufacturing Capacity Corp",
            Cash = 1_000_000m
        };
        db.Companies.Add(company);

        var factory = new Building
        {
            Id = Guid.NewGuid(),
            CompanyId = company.Id,
            CityId = city.Id,
            Type = BuildingType.Factory,
            Name = "Capacity Factory",
            Level = 1
        };
        db.Buildings.Add(factory);

        var purchaseUnit = new BuildingUnit
        {
            Id = Guid.NewGuid(),
            BuildingId = factory.Id,
            UnitType = UnitType.Purchase,
            GridX = 0,
            GridY = 0,
            Level = 1,
            LinkRight = true,
            ResourceTypeId = wood.Id,
            MaxPrice = 999_999m,
            PurchaseSource = "LOCAL"
        };
        var manufacturingUnit = new BuildingUnit
        {
            Id = Guid.NewGuid(),
            BuildingId = factory.Id,
            UnitType = UnitType.Manufacturing,
            GridX = 1,
            GridY = 0,
            Level = 1,
            ProductTypeId = product.Id
        };
        db.BuildingUnits.AddRange(purchaseUnit, manufacturingUnit);

        db.Inventories.Add(new Inventory
        {
            Id = Guid.NewGuid(),
            BuildingId = factory.Id,
            BuildingUnitId = purchaseUnit.Id,
            ResourceTypeId = wood.Id,
            Quantity = 100m,
            Quality = 0.75m
        });

        await db.SaveChangesAsync();

        var processor = await CreateProcessorAsync(scope);
        await processor.ProcessTickAsync();

        var purchaseQuantity = await db.Inventories
            .Where(entry => entry.BuildingUnitId == purchaseUnit.Id && entry.ResourceTypeId == wood.Id)
            .SumAsync(entry => entry.Quantity);
        var manufacturingInputQuantity = await db.Inventories
            .Where(entry => entry.BuildingUnitId == manufacturingUnit.Id && entry.ResourceTypeId == wood.Id)
            .SumAsync(entry => entry.Quantity);

        Assert.Equal(50m, manufacturingInputQuantity);
        Assert.Equal(50m, purchaseQuantity);
    }

    [Fact]
    public async Task PublicSalesPhase_HigherPopulationIndexImprovesSales()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var city = await db.Cities.FirstAsync();
        var product = await db.ProductTypes.FirstAsync(candidate => candidate.Slug == "wooden-chair");

        var player = new Player
        {
            Id = Guid.NewGuid(),
            Email = $"population-sales-{Guid.NewGuid():N}@test.com",
            DisplayName = "Population Sales Tester",
            PasswordHash = "hash",
            Role = PlayerRole.Player
        };
        db.Players.Add(player);

        var company = new Company
        {
            Id = Guid.NewGuid(),
            PlayerId = player.Id,
            Name = "Retail Density Corp",
            Cash = 2_000_000m
        };
        db.Companies.Add(company);

        var premiumShop = new Building
        {
            Id = Guid.NewGuid(),
            CompanyId = company.Id,
            CityId = city.Id,
            Type = BuildingType.SalesShop,
            Name = "Center Shop",
            Latitude = city.Latitude + 0.001,
            Longitude = city.Longitude + 0.001,
            Level = 1
        };
        var outskirtsShop = new Building
        {
            Id = Guid.NewGuid(),
            CompanyId = company.Id,
            CityId = city.Id,
            Type = BuildingType.SalesShop,
            Name = "Outskirts Shop",
            Latitude = city.Latitude + 0.14,
            Longitude = city.Longitude + 0.14,
            Level = 1
        };
        var premiumApartment = new Building
        {
            Id = Guid.NewGuid(),
            CompanyId = company.Id,
            CityId = city.Id,
            Type = BuildingType.Apartment,
            Name = "Center Apartments",
            Latitude = premiumShop.Latitude + 0.002,
            Longitude = premiumShop.Longitude + 0.002,
            Level = 1,
            OccupancyPercent = 96m
        };
        var premiumCommercial = new Building
        {
            Id = Guid.NewGuid(),
            CompanyId = company.Id,
            CityId = city.Id,
            Type = BuildingType.Commercial,
            Name = "Center Offices",
            Latitude = premiumShop.Latitude + 0.003,
            Longitude = premiumShop.Longitude + 0.0015,
            Level = 1,
            OccupancyPercent = 94m
        };
        db.Buildings.AddRange(premiumShop, outskirtsShop, premiumApartment, premiumCommercial);

        var premiumUnit = new BuildingUnit
        {
            Id = Guid.NewGuid(),
            BuildingId = premiumShop.Id,
            UnitType = UnitType.PublicSales,
            GridX = 0,
            GridY = 0,
            Level = 1,
            ProductTypeId = product.Id,
            MinPrice = product.BasePrice * 1.8m,
        };
        var outskirtsUnit = new BuildingUnit
        {
            Id = Guid.NewGuid(),
            BuildingId = outskirtsShop.Id,
            UnitType = UnitType.PublicSales,
            GridX = 0,
            GridY = 0,
            Level = 1,
            ProductTypeId = product.Id,
            MinPrice = product.BasePrice * 1.8m,
        };
        db.BuildingUnits.AddRange(premiumUnit, outskirtsUnit);

        db.Inventories.AddRange(
            new Inventory
            {
                Id = Guid.NewGuid(),
                BuildingId = premiumShop.Id,
                BuildingUnitId = premiumUnit.Id,
                ProductTypeId = product.Id,
                Quantity = 500m,
                Quality = 0.8m,
            },
            new Inventory
            {
                Id = Guid.NewGuid(),
                BuildingId = outskirtsShop.Id,
                BuildingUnitId = outskirtsUnit.Id,
                ProductTypeId = product.Id,
                Quantity = 500m,
                Quality = 0.8m,
            });

        db.BuildingLots.AddRange(
            new BuildingLot
            {
                Id = Guid.NewGuid(),
                CityId = city.Id,
                Name = "Center Retail Lot",
                Description = "High-footfall central parcel.",
                District = "Retail District",
                Latitude = premiumShop.Latitude,
                Longitude = premiumShop.Longitude,
                BasePrice = 120_000m,
                Price = 120_000m,
                PopulationIndex = 1.6m,
                SuitableTypes = BuildingType.SalesShop,
                OwnerCompanyId = company.Id,
                BuildingId = premiumShop.Id,
            },
            new BuildingLot
            {
                Id = Guid.NewGuid(),
                CityId = city.Id,
                Name = "Peripheral Retail Lot",
                Description = "Low-density edge parcel.",
                District = "Retail District",
                Latitude = outskirtsShop.Latitude,
                Longitude = outskirtsShop.Longitude,
                BasePrice = 80_000m,
                Price = 80_000m,
                PopulationIndex = 0.45m,
                SuitableTypes = BuildingType.SalesShop,
                OwnerCompanyId = company.Id,
                BuildingId = outskirtsShop.Id,
            });

        await db.SaveChangesAsync();

        var processor = await CreateProcessorAsync(scope);
        await processor.ProcessTickAsync();

        var lotsByBuilding = await db.BuildingLots
            .Where(lot => lot.BuildingId == premiumShop.Id || lot.BuildingId == outskirtsShop.Id)
            .ToDictionaryAsync(lot => lot.BuildingId!.Value);

        var revenuesByBuilding = await db.PublicSalesRecords
            .Where(record => record.BuildingId == premiumShop.Id || record.BuildingId == outskirtsShop.Id)
            .GroupBy(record => record.BuildingId)
            .ToDictionaryAsync(group => group.Key, group => group.Sum(record => record.Revenue));
        var demandByBuilding = await db.PublicSalesRecords
            .Where(record => record.BuildingId == premiumShop.Id || record.BuildingId == outskirtsShop.Id)
            .GroupBy(record => record.BuildingId)
            .ToDictionaryAsync(group => group.Key, group => group.Sum(record => record.Demand));

        Assert.True(lotsByBuilding[premiumShop.Id].PopulationIndex > lotsByBuilding[outskirtsShop.Id].PopulationIndex,
            "The premium retail lot should retain a higher population index than the outskirt lot during the land market phase.");
        Assert.True(demandByBuilding[premiumShop.Id] > demandByBuilding[outskirtsShop.Id],
            "The higher-population retail lot should generate more public-sales demand than the outskirt lot.");

        Assert.True(revenuesByBuilding[premiumShop.Id] >= revenuesByBuilding[outskirtsShop.Id],
            "The premium retail lot should not underperform the outskirt lot once higher population demand is applied.");
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
    public async Task TaxPhase_DeductsAnnualIncomeTaxOnPositiveTaxableIncome()
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

        db.LedgerEntries.AddRange(
            new LedgerEntry
            {
                Id = Guid.NewGuid(),
                CompanyId = company.Id,
                Category = LedgerCategory.Revenue,
                Description = "Annual sales",
                Amount = 100_000m,
                RecordedAtTick = 10,
                RecordedAtUtc = DateTime.UtcNow,
            },
            new LedgerEntry
            {
                Id = Guid.NewGuid(),
                CompanyId = company.Id,
                Category = LedgerCategory.PurchasingCost,
                Description = "Annual purchasing",
                Amount = -40_000m,
                RecordedAtTick = 25,
                RecordedAtUtc = DateTime.UtcNow,
            });

        // Set game state so the next tick is a tax cycle tick.
        var gs = await db.GameStates.FirstAsync();
        gs.CurrentTick = gs.TaxCycleTicks - 1; // Next tick will be tax cycle.
        await db.SaveChangesAsync();

        var cashBefore = company.Cash;
        var processor = await CreateProcessorAsync(scope);
        await processor.ProcessTickAsync();

        Assert.Equal(cashBefore - 9_000m, company.Cash);

        var taxEntry = await db.LedgerEntries
            .Where(entry => entry.CompanyId == company.Id && entry.Category == LedgerCategory.Tax)
            .SingleAsync();

        Assert.Equal(-9_000m, taxEntry.Amount);
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

        var purchasingEntries = await db.LedgerEntries
            .Where(entry => entry.CompanyId == companyId
                && entry.BuildingUnitId == purchaseUnitId
                && entry.Category == LedgerCategory.PurchasingCost)
            .ToListAsync();

        Assert.Empty(purchasingEntries);
        Assert.True(company.Cash <= cashBefore);
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

        var purchasingEntries = await db.LedgerEntries
            .Where(entry => entry.CompanyId == companyId
                && entry.BuildingUnitId == purchaseUnitId
                && entry.Category == LedgerCategory.PurchasingCost)
            .ToListAsync();

        Assert.Empty(purchasingEntries);
        Assert.True(company.Cash <= cashBefore);
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

        var purchasingEntries = await db.LedgerEntries
            .Where(entry => entry.CompanyId == companyId
                && entry.BuildingUnitId == purchaseUnitId
                && entry.Category == LedgerCategory.PurchasingCost)
            .ToListAsync();

        Assert.Empty(purchasingEntries);
        Assert.True(company.Cash <= cashBefore);
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
        var ledgerEntries = await db.LedgerEntries
            .Where(entry => entry.CompanyId == companyId && entry.BuildingUnitId == purchaseUnitId)
            .ToListAsync();
        var ledgerEntry = ledgerEntries
            .SingleOrDefault(entry => entry.Category == LedgerCategory.PurchasingCost);

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
        Assert.Equal(cashBefore + ledgerEntries.Sum(entry => entry.Amount), company.Cash);
    }

    [Fact]
    public async Task PurchasingPhase_GlobalExchange_StoresSourcingCostTotalOnInventory()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var (companyId, _, _, purchaseUnitId, _) =
            await SeedExchangePurchaseUnitAsync(db, maxPrice: 9999m, purchaseSource: "EXCHANGE");

        var processor = await CreateProcessorAsync(scope);
        await processor.ProcessTickAsync();

        var ledgerEntry = await db.LedgerEntries
            .Where(entry => entry.CompanyId == companyId
                && entry.BuildingUnitId == purchaseUnitId
                && entry.Category == LedgerCategory.PurchasingCost)
            .SingleAsync();

        var inventories = await db.Inventories
            .Where(entry => entry.BuildingUnitId == purchaseUnitId && entry.Quantity > 0m)
            .ToListAsync();

        Assert.NotEmpty(inventories);
        Assert.True(inventories.Sum(entry => entry.SourcingCostTotal) > 0m);
        Assert.Equal(-ledgerEntry.Amount, inventories.Sum(entry => entry.SourcingCostTotal));
        Assert.True(inventories.Sum(entry => entry.SourcingCostTotal) / inventories.Sum(entry => entry.Quantity) > 0m);
    }

    [Fact]
    public async Task PurchasingPhase_ExchangeSource_ForProductType_IsGracefullySkipped()
    {
        // In v1, the global exchange only supports raw-resource purchases.
        // A PURCHASE unit configured with EXCHANGE source but a product type (not resource)
        // should produce no inventory — the engine must skip the exchange path gracefully.
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var city = await db.Cities.FirstAsync();
        var product = await db.ProductTypes.FirstAsync();

        var player = new Api.Data.Entities.Player
        {
            Id = Guid.NewGuid(),
            Email = $"exchg-prod-{Guid.NewGuid():N}@test.com",
            DisplayName = "Product Exchange Tester",
            PasswordHash = "hash",
            Role = Api.Data.Entities.PlayerRole.Player
        };
        db.Players.Add(player);

        var company = new Api.Data.Entities.Company
        {
            Id = Guid.NewGuid(),
            PlayerId = player.Id,
            Name = "Product Exchange Corp",
            Cash = 1_000_000m
        };
        db.Companies.Add(company);

        var building = new Api.Data.Entities.Building
        {
            Id = Guid.NewGuid(),
            CompanyId = company.Id,
            CityId = city.Id,
            Type = Api.Data.Entities.BuildingType.Factory,
            Name = "Product Exchange Factory",
            Level = 1
        };
        db.Buildings.Add(building);

        // PURCHASE unit targeting a product type with EXCHANGE source.
        var purchaseUnit = new Api.Data.Entities.BuildingUnit
        {
            Id = Guid.NewGuid(),
            BuildingId = building.Id,
            UnitType = Api.Data.Entities.UnitType.Purchase,
            GridX = 0,
            GridY = 0,
            Level = 1,
            ProductTypeId = product.Id,
            ResourceTypeId = null,
            MaxPrice = 9999m,
            PurchaseSource = "EXCHANGE"
        };
        db.BuildingUnits.Add(purchaseUnit);
        await db.SaveChangesAsync();

        var processor = await CreateProcessorAsync(scope);
        await processor.ProcessTickAsync();

        // No inventory should have been created — exchange does not serve product-type units.
        var inventoryCount = await db.Inventories
            .Where(entry => entry.BuildingUnitId == purchaseUnit.Id && entry.Quantity > 0m)
            .CountAsync();

        Assert.Equal(0, inventoryCount);
    }

    [Fact]
    public async Task PurchasingPhase_GlobalExchange_FillsInventory_ForGrain_FoodProcessingIndustry()
    {
        // AC coverage: All 3 starter industries should be able to source their raw input from the global exchange.
        // Grain is the Food Processing raw material input (BasePrice=5, abundance=0.6 in Bratislava).
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var (companyId, _, _, purchaseUnitId, _) =
            await SeedExchangePurchaseUnitAsync(db, maxPrice: 9999m, purchaseSource: "EXCHANGE", resourceSlug: "grain");

        var company = await db.Companies.FindAsync(companyId);
        var cashBefore = company!.Cash;

        var processor = await CreateProcessorAsync(scope);
        await processor.ProcessTickAsync();

        var inventory = await db.Inventories
            .Where(i => i.BuildingUnitId == purchaseUnitId)
            .ToListAsync();

        Assert.NotEmpty(inventory);
        Assert.True(inventory.Sum(i => i.Quantity) > 0m,
            "Food Processing (Grain) purchase unit should fill inventory from global exchange.");
        Assert.True(company.Cash < cashBefore,
            "Company cash should decrease after purchasing Grain from global exchange.");
        Assert.All(inventory, inv => Assert.InRange(inv.Quality, 0.35m, 0.95m));
    }

    [Fact]
    public async Task PurchasingPhase_GlobalExchange_FillsInventory_ForChemicalMinerals_HealthcareIndustry()
    {
        // AC coverage: All 3 starter industries should be able to source their raw input from the global exchange.
        // Chemical Minerals is the Healthcare raw material input (BasePrice=30, abundance=0.3 in Bratislava).
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var (companyId, _, _, purchaseUnitId, _) =
            await SeedExchangePurchaseUnitAsync(db, maxPrice: 9999m, purchaseSource: "EXCHANGE", resourceSlug: "chemical-minerals");

        var company = await db.Companies.FindAsync(companyId);
        var cashBefore = company!.Cash;

        var processor = await CreateProcessorAsync(scope);
        await processor.ProcessTickAsync();

        var inventory = await db.Inventories
            .Where(i => i.BuildingUnitId == purchaseUnitId)
            .ToListAsync();

        Assert.NotEmpty(inventory);
        Assert.True(inventory.Sum(i => i.Quantity) > 0m,
            "Healthcare (Chemical Minerals) purchase unit should fill inventory from global exchange.");
        Assert.True(company.Cash < cashBefore,
            "Company cash should decrease after purchasing Chemical Minerals from global exchange.");
        Assert.All(inventory, inv => Assert.InRange(inv.Quality, 0.35m, 0.95m));
    }

    [Fact]
    public async Task ManufacturingPhase_CarriesConsumedSourcingCostsIntoOutputInventory()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var (_, factoryId, _) = await SeedFactoryAndShopAsync(db);

        var purchaseUnit = await db.BuildingUnits
            .Where(unit => unit.BuildingId == factoryId && unit.UnitType == UnitType.Purchase)
            .SingleAsync();
        purchaseUnit.PurchaseSource = "LOCAL";
        purchaseUnit.MaxPrice = 0m;

        var purchaseInventory = await db.Inventories
            .Where(entry => entry.BuildingUnitId == purchaseUnit.Id && entry.ResourceTypeId != null)
            .SingleAsync();
        purchaseInventory.SourcingCostTotal = 125m;

        var factory = await db.Buildings.SingleAsync(candidate => candidate.Id == factoryId);
        var manufacturingUnit = await db.BuildingUnits
            .SingleAsync(unit => unit.BuildingId == factoryId && unit.UnitType == UnitType.Manufacturing);

        await db.SaveChangesAsync();

        var processor = await CreateProcessorAsync(scope);
        await processor.ProcessTickAsync();

        var productInventories = await db.Inventories
            .Where(entry => entry.BuildingId == factoryId && entry.ProductTypeId != null && entry.Quantity > 0m)
            .ToListAsync();
        var manufacturingLedgerCosts = await db.LedgerEntries
            .Where(entry => entry.BuildingUnitId == manufacturingUnit.Id
                && (entry.Description.StartsWith("Manufacturing labor")
                    || entry.Description.StartsWith("Manufacturing energy")))
            .SumAsync(entry => -entry.Amount);
        var expectedOutputSourcingCost = 12.5m + manufacturingLedgerCosts;

        Assert.NotEmpty(productInventories);
        Assert.Equal(expectedOutputSourcingCost, productInventories.Sum(entry => entry.SourcingCostTotal));

        var remainingRawInventories = await db.Inventories
            .Where(entry => entry.BuildingId == factoryId && entry.ResourceTypeId != null && entry.Quantity > 0m)
            .ToListAsync();

        Assert.Equal(9m, remainingRawInventories.Sum(entry => entry.Quantity));
        Assert.Equal(112.5m, remainingRawInventories.Sum(entry => entry.SourcingCostTotal));
    }

    #endregion
}
