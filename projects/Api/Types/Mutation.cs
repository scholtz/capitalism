using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Api.Data;
using Api.Data.Entities;
using Api.Security;
using Api.Utilities;
using HotChocolate.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Api.Types;

/// <summary>
/// GraphQL mutation type for the Capitalism V game.
/// Handles authentication, company management, building placement, and onboarding.
/// </summary>
public sealed class Mutation
{
    private static readonly IReadOnlyDictionary<string, string> StarterOnboardingProductByIndustry = new Dictionary<string, string>
    {
        [Industry.Furniture] = "wooden-chair",
        [Industry.FoodProcessing] = "bread",
        [Industry.Healthcare] = "basic-medicine"
    };

    /// <summary>Registers a new player account and returns an auth token.</summary>
    public async Task<AuthPayload> Register(
        RegisterInput input,
        [Service] AppDbContext db,
        [Service] IOptions<JwtOptions> jwtOptions)
    {
        if (await db.Players.AnyAsync(p => p.Email == input.Email))
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("A player with this email already exists.")
                    .SetCode("DUPLICATE_EMAIL")
                    .Build());
        }

        var player = new Player
        {
            Id = Guid.NewGuid(),
            Email = input.Email,
            DisplayName = input.DisplayName,
            Role = PlayerRole.Player,
            CreatedAtUtc = DateTime.UtcNow
        };

        var hasher = new PasswordHasher<Player>();
        player.PasswordHash = hasher.HashPassword(player, input.Password);

        db.Players.Add(player);
        await db.SaveChangesAsync();

        var session = GenerateToken(player, jwtOptions.Value);
        return new AuthPayload
        {
            Token = session.Token,
            ExpiresAtUtc = session.ExpiresAtUtc,
            Player = player
        };
    }

    /// <summary>Authenticates a player and returns an auth token.</summary>
    public async Task<AuthPayload> Login(
        LoginInput input,
        [Service] AppDbContext db,
        [Service] IOptions<JwtOptions> jwtOptions)
    {
        var player = await db.Players.FirstOrDefaultAsync(p => p.Email == input.Email);
        if (player is null)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Invalid email or password.")
                    .SetCode("INVALID_CREDENTIALS")
                    .Build());
        }

        var hasher = new PasswordHasher<Player>();
        var result = hasher.VerifyHashedPassword(player, player.PasswordHash, input.Password);

        if (result == PasswordVerificationResult.Failed)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Invalid email or password.")
                    .SetCode("INVALID_CREDENTIALS")
                    .Build());
        }

        player.LastLoginAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();

        var session = GenerateToken(player, jwtOptions.Value);
        return new AuthPayload
        {
            Token = session.Token,
            ExpiresAtUtc = session.ExpiresAtUtc,
            Player = player
        };
    }

    /// <summary>Creates a new company for the authenticated player.</summary>
    [Authorize]
    public async Task<Company> CreateCompany(
        CreateCompanyInput input,
        [Service] AppDbContext db,
        [Service] IHttpContextAccessor httpContextAccessor)
    {
        var userId = httpContextAccessor.HttpContext!.User.GetRequiredUserId();

        var company = new Company
        {
            Id = Guid.NewGuid(),
            PlayerId = userId,
            Name = input.Name,
            Cash = 1_000_000m // Starting capital
            , FoundedAtUtc = DateTime.UtcNow
        };

        db.Companies.Add(company);
        await db.SaveChangesAsync();

        return company;
    }

    /// <summary>Places a new building on the game map for a company.</summary>
    [Authorize]
    public async Task<Building> PlaceBuilding(
        PlaceBuildingInput input,
        [Service] AppDbContext db,
        [Service] IHttpContextAccessor httpContextAccessor)
    {
        var userId = httpContextAccessor.HttpContext!.User.GetRequiredUserId();

        var company = await db.Companies.FirstOrDefaultAsync(
            c => c.Id == input.CompanyId && c.PlayerId == userId);

        if (company is null)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Company not found or you don't own it.")
                    .SetCode("COMPANY_NOT_FOUND")
                    .Build());
        }

        if (!BuildingType.All.Contains(input.Type))
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage($"Invalid building type: {input.Type}")
                    .SetCode("INVALID_BUILDING_TYPE")
                    .Build());
        }

        var city = await db.Cities.FindAsync(input.CityId);
        if (city is null)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("City not found.")
                    .SetCode("CITY_NOT_FOUND")
                    .Build());
        }

        var building = new Building
        {
            Id = Guid.NewGuid(),
            CompanyId = company.Id,
            CityId = city.Id,
            Type = input.Type,
            Name = input.Name,
            Latitude = city.Latitude,
            Longitude = city.Longitude,
            Level = 1,
            PowerConsumption = 1m,
            BuiltAtUtc = DateTime.UtcNow
        };

        db.Buildings.Add(building);
        await db.SaveChangesAsync();

        return building;
    }

    /// <summary>
    /// Completes the onboarding process: creates a company, a factory, and a sales shop
    /// with default unit configurations for the chosen industry.
    /// </summary>
    [Authorize]
    public async Task<OnboardingResult> CompleteOnboarding(
        OnboardingInput input,
        [Service] AppDbContext db,
        [Service] IHttpContextAccessor httpContextAccessor)
    {
        var userId = httpContextAccessor.HttpContext!.User.GetRequiredUserId();
        var nowUtc = DateTime.UtcNow;
        var player = await db.Players.FindAsync(userId)
            ?? throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Player not found.")
                    .SetCode("PLAYER_NOT_FOUND")
                    .Build());
        var hasActiveProSubscription = ProductAccessService.HasActiveProSubscription(player, nowUtc);

        // Validate industry
        if (!Industry.StarterIndustries.Contains(input.Industry))
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage($"Invalid starter industry: {input.Industry}")
                    .SetCode("INVALID_INDUSTRY")
                    .Build());
        }

        // Validate city
        var city = await db.Cities.FindAsync(input.CityId);
        if (city is null)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("City not found.")
                    .SetCode("CITY_NOT_FOUND")
                    .Build());
        }

        // Validate product
        var product = await db.ProductTypes
            .Include(candidate => candidate.Recipes)
            .ThenInclude(recipe => recipe.ResourceType)
            .Include(candidate => candidate.Recipes)
            .ThenInclude(recipe => recipe.InputProductType)
            .FirstOrDefaultAsync(candidate => candidate.Id == input.ProductTypeId);
        if (product is null || product.Industry != input.Industry || !IsStarterOnboardingProduct(product))
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Product not found, doesn't belong to selected industry, or is not available for onboarding.")
                    .SetCode("INVALID_PRODUCT")
                    .Build());
        }

        if (!ProductAccessService.IsUnlockedForPlayer(product, hasActiveProSubscription))
        {
            throw ProductAccessService.CreateProAccessException(product.Name);
        }

        var starterResourceId = product.Recipes
            .Where(recipe => recipe.InputProductTypeId is null && recipe.ResourceTypeId is not null)
            .Select(recipe => recipe.ResourceTypeId)
            .FirstOrDefault();
        if (starterResourceId is null)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("The selected onboarding product is missing a starter raw-material recipe.")
                    .SetCode("INVALID_PRODUCT")
                    .Build());
        }

        // Create company
        var company = new Company
        {
            Id = Guid.NewGuid(),
            PlayerId = userId,
            Name = input.CompanyName,
            Cash = 500_000m,
            FoundedAtUtc = nowUtc
        };
        db.Companies.Add(company);

        // Create factory with default layout
        var factory = new Building
        {
            Id = Guid.NewGuid(),
            CompanyId = company.Id,
            CityId = city.Id,
            Type = BuildingType.Factory,
            Name = $"{input.CompanyName} Factory",
            Latitude = city.Latitude + 0.001,
            Longitude = city.Longitude + 0.001,
            Level = 1,
            PowerConsumption = 2m,
            BuiltAtUtc = nowUtc
        };
        db.Buildings.Add(factory);

        // Add default factory units: Purchase, Manufacturing, Storage, B2B Sales
        db.BuildingUnits.AddRange(
            new BuildingUnit { Id = Guid.NewGuid(), BuildingId = factory.Id, UnitType = UnitType.Purchase, GridX = 0, GridY = 0, Level = 1, LinkRight = true, ResourceTypeId = starterResourceId, PurchaseSource = "LOCAL", MaxPrice = product.BasePrice },
            new BuildingUnit { Id = Guid.NewGuid(), BuildingId = factory.Id, UnitType = UnitType.Manufacturing, GridX = 1, GridY = 0, Level = 1, LinkRight = true, ProductTypeId = product.Id },
            new BuildingUnit { Id = Guid.NewGuid(), BuildingId = factory.Id, UnitType = UnitType.Storage, GridX = 2, GridY = 0, Level = 1, LinkRight = true },
            new BuildingUnit { Id = Guid.NewGuid(), BuildingId = factory.Id, UnitType = UnitType.B2BSales, GridX = 3, GridY = 0, Level = 1, ProductTypeId = product.Id, MinPrice = product.BasePrice }
        );

        // Create sales shop
        var shop = new Building
        {
            Id = Guid.NewGuid(),
            CompanyId = company.Id,
            CityId = city.Id,
            Type = BuildingType.SalesShop,
            Name = $"{input.CompanyName} Shop",
            Latitude = city.Latitude + 0.002,
            Longitude = city.Longitude + 0.002,
            Level = 1,
            PowerConsumption = 1m,
            BuiltAtUtc = nowUtc
        };
        db.Buildings.Add(shop);

        // Add default shop units: Purchase, Public Sales
        db.BuildingUnits.AddRange(
            new BuildingUnit { Id = Guid.NewGuid(), BuildingId = shop.Id, UnitType = UnitType.Purchase, GridX = 0, GridY = 0, Level = 1, LinkRight = true, ProductTypeId = product.Id, PurchaseSource = "LOCAL", MaxPrice = product.BasePrice },
            new BuildingUnit { Id = Guid.NewGuid(), BuildingId = shop.Id, UnitType = UnitType.PublicSales, GridX = 1, GridY = 0, Level = 1, ProductTypeId = product.Id, MinPrice = product.BasePrice }
        );

        // Mark onboarding as completed for this player
        player.OnboardingCompletedAtUtc = nowUtc;
        player.OnboardingShopBuildingId = shop.Id;
        ClearOnboardingProgress(player);

        await db.SaveChangesAsync();
        var startupPackOffer = await StartupPackService.EnsureOfferForPlayerAsync(db, player, nowUtc);

        return new OnboardingResult
        {
            Company = company,
            Factory = factory,
            SalesShop = shop,
            SelectedProduct = product,
            StartupPackOffer = startupPackOffer
        };
    }

    /// <summary>
    /// Starts the lot-based onboarding journey by creating the first company and purchasing its factory lot.
    /// The player can later resume at the sales-shop step using the stored onboarding progress metadata.
    /// </summary>
    [Authorize]
    public async Task<OnboardingStartResult> StartOnboardingCompany(
        StartOnboardingCompanyInput input,
        [Service] AppDbContext db,
        [Service] IHttpContextAccessor httpContextAccessor)
    {
        var userId = httpContextAccessor.HttpContext!.User.GetRequiredUserId();
        var nowUtc = DateTime.UtcNow;
        var player = await db.Players.FindAsync(userId)
            ?? throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Player not found.")
                    .SetCode("PLAYER_NOT_FOUND")
                    .Build());

        if (player.OnboardingCompletedAtUtc is not null)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("You have already completed onboarding.")
                    .SetCode("ONBOARDING_ALREADY_COMPLETED")
                    .Build());
        }

        if (player.OnboardingCurrentStep is not null || player.OnboardingCompanyId is not null)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Onboarding is already in progress.")
                    .SetCode("ONBOARDING_ALREADY_IN_PROGRESS")
                    .Build());
        }

        if (await db.Companies.AnyAsync(company => company.PlayerId == userId))
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("You already have a company. Resume or finish onboarding instead.")
                    .SetCode("ONBOARDING_ALREADY_IN_PROGRESS")
                    .Build());
        }

        if (!Industry.StarterIndustries.Contains(input.Industry))
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage($"Invalid starter industry: {input.Industry}")
                    .SetCode("INVALID_INDUSTRY")
                    .Build());
        }

        var city = await db.Cities.FindAsync(input.CityId);
        if (city is null)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("City not found.")
                    .SetCode("CITY_NOT_FOUND")
                    .Build());
        }

        var company = new Company
        {
            Id = Guid.NewGuid(),
            PlayerId = userId,
            Name = input.CompanyName,
            Cash = 500_000m,
            FoundedAtUtc = nowUtc
        };
        db.Companies.Add(company);

        var (factoryLot, factory) = await PrepareLotPurchaseAsync(
            db,
            company,
            input.FactoryLotId,
            BuildingType.Factory,
            $"{input.CompanyName} Factory",
            2m,
            nowUtc,
            input.CityId);

        AddStarterFactoryShell(db, factory.Id);

        player.OnboardingCurrentStep = OnboardingProgressStep.ShopSelection;
        player.OnboardingIndustry = input.Industry;
        player.OnboardingCityId = input.CityId;
        player.OnboardingCompanyId = company.Id;
        player.OnboardingFactoryLotId = factoryLot.Id;

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("This lot has already been purchased.")
                    .SetCode("LOT_ALREADY_OWNED")
                    .Build());
        }

        return new OnboardingStartResult
        {
            Company = company,
            Factory = factory,
            FactoryLot = factoryLot,
            NextStep = OnboardingProgressStep.ShopSelection
        };
    }

    /// <summary>
    /// Finishes the lot-based onboarding journey by selecting the starter product, purchasing the first sales shop,
    /// configuring both buildings, and marking onboarding complete.
    /// </summary>
    [Authorize]
    public async Task<OnboardingResult> FinishOnboarding(
        FinishOnboardingInput input,
        [Service] AppDbContext db,
        [Service] IHttpContextAccessor httpContextAccessor)
    {
        var userId = httpContextAccessor.HttpContext!.User.GetRequiredUserId();
        var nowUtc = DateTime.UtcNow;
        var player = await db.Players.FindAsync(userId)
            ?? throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Player not found.")
                    .SetCode("PLAYER_NOT_FOUND")
                    .Build());

        if (!string.Equals(player.OnboardingCurrentStep, OnboardingProgressStep.ShopSelection, StringComparison.Ordinal)
            || player.OnboardingCompanyId is null
            || player.OnboardingIndustry is null
            || player.OnboardingCityId is null
            || player.OnboardingFactoryLotId is null)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("No onboarding progress was found to resume.")
                    .SetCode("ONBOARDING_NOT_IN_PROGRESS")
                    .Build());
        }

        var company = await db.Companies.FirstOrDefaultAsync(
            candidate => candidate.Id == player.OnboardingCompanyId && candidate.PlayerId == userId)
            ?? throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Company not found or you don't own it.")
                    .SetCode("COMPANY_NOT_FOUND")
                    .Build());

        var factoryLot = await db.BuildingLots.FirstOrDefaultAsync(lot => lot.Id == player.OnboardingFactoryLotId)
            ?? throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Factory lot not found.")
                    .SetCode("LOT_NOT_FOUND")
                    .Build());

        var factory = await db.Buildings
            .Include(building => building.Units)
            .FirstOrDefaultAsync(building => building.Id == factoryLot.BuildingId && building.CompanyId == company.Id)
            ?? throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Factory not found.")
                    .SetCode("BUILDING_NOT_FOUND")
                    .Build());

        var hasActiveProSubscription = ProductAccessService.HasActiveProSubscription(player, nowUtc);
        var product = await db.ProductTypes
            .Include(candidate => candidate.Recipes)
            .ThenInclude(recipe => recipe.ResourceType)
            .Include(candidate => candidate.Recipes)
            .ThenInclude(recipe => recipe.InputProductType)
            .FirstOrDefaultAsync(candidate => candidate.Id == input.ProductTypeId);

        if (product is null || product.Industry != player.OnboardingIndustry || !IsStarterOnboardingProduct(product))
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Product not found, doesn't belong to selected industry, or is not available for onboarding.")
                    .SetCode("INVALID_PRODUCT")
                    .Build());
        }

        if (!ProductAccessService.IsUnlockedForPlayer(product, hasActiveProSubscription))
        {
            throw ProductAccessService.CreateProAccessException(product.Name);
        }

        var starterResourceId = product.Recipes
            .Where(recipe => recipe.InputProductTypeId is null && recipe.ResourceTypeId is not null)
            .Select(recipe => recipe.ResourceTypeId)
            .FirstOrDefault();
        if (starterResourceId is null)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("The selected onboarding product is missing a starter raw-material recipe.")
                    .SetCode("INVALID_PRODUCT")
                    .Build());
        }

        var (_, shop) = await PrepareLotPurchaseAsync(
            db,
            company,
            input.ShopLotId,
            BuildingType.SalesShop,
            $"{company.Name} Shop",
            1m,
            nowUtc,
            player.OnboardingCityId!.Value);

        ConfigureStarterFactory(db, factory, product, starterResourceId.Value);
        AddStarterShop(db, shop.Id, product);

        player.OnboardingCompletedAtUtc = nowUtc;
        player.OnboardingShopBuildingId = shop.Id;
        ClearOnboardingProgress(player);

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("This lot has already been purchased.")
                    .SetCode("LOT_ALREADY_OWNED")
                    .Build());
        }

        var startupPackOffer = await StartupPackService.EnsureOfferForPlayerAsync(db, player, nowUtc);

        return new OnboardingResult
        {
            Company = company,
            Factory = factory,
            SalesShop = shop,
            SelectedProduct = product,
            StartupPackOffer = startupPackOffer
        };
    }

    private static bool IsStarterOnboardingProduct(ProductType product)
    {
        return StarterOnboardingProductByIndustry.TryGetValue(product.Industry, out var starterSlug)
            && string.Equals(product.Slug, starterSlug, StringComparison.Ordinal);
    }

    private static void ClearOnboardingProgress(Player player)
    {
        player.OnboardingCurrentStep = null;
        player.OnboardingIndustry = null;
        player.OnboardingCityId = null;
        player.OnboardingCompanyId = null;
        player.OnboardingFactoryLotId = null;
    }

    private static void AddStarterFactoryShell(AppDbContext db, Guid buildingId)
    {
        db.BuildingUnits.AddRange(
            new BuildingUnit { Id = Guid.NewGuid(), BuildingId = buildingId, UnitType = UnitType.Purchase, GridX = 0, GridY = 0, Level = 1, LinkRight = true, PurchaseSource = "LOCAL" },
            new BuildingUnit { Id = Guid.NewGuid(), BuildingId = buildingId, UnitType = UnitType.Manufacturing, GridX = 1, GridY = 0, Level = 1, LinkRight = true },
            new BuildingUnit { Id = Guid.NewGuid(), BuildingId = buildingId, UnitType = UnitType.Storage, GridX = 2, GridY = 0, Level = 1, LinkRight = true },
            new BuildingUnit { Id = Guid.NewGuid(), BuildingId = buildingId, UnitType = UnitType.B2BSales, GridX = 3, GridY = 0, Level = 1 }
        );
    }

    private static void ConfigureStarterFactory(AppDbContext db, Building factory, ProductType product, Guid starterResourceId)
    {
        db.BuildingUnits.RemoveRange(factory.Units);
        db.BuildingUnits.AddRange(
            new BuildingUnit { Id = Guid.NewGuid(), BuildingId = factory.Id, UnitType = UnitType.Purchase, GridX = 0, GridY = 0, Level = 1, LinkRight = true, ResourceTypeId = starterResourceId, PurchaseSource = "LOCAL", MaxPrice = product.BasePrice },
            new BuildingUnit { Id = Guid.NewGuid(), BuildingId = factory.Id, UnitType = UnitType.Manufacturing, GridX = 1, GridY = 0, Level = 1, LinkRight = true, ProductTypeId = product.Id },
            new BuildingUnit { Id = Guid.NewGuid(), BuildingId = factory.Id, UnitType = UnitType.Storage, GridX = 2, GridY = 0, Level = 1, LinkRight = true },
            new BuildingUnit { Id = Guid.NewGuid(), BuildingId = factory.Id, UnitType = UnitType.B2BSales, GridX = 3, GridY = 0, Level = 1, ProductTypeId = product.Id, MinPrice = product.BasePrice }
        );
    }

    private static void AddStarterShop(AppDbContext db, Guid buildingId, ProductType product)
    {
        db.BuildingUnits.AddRange(
            new BuildingUnit { Id = Guid.NewGuid(), BuildingId = buildingId, UnitType = UnitType.Purchase, GridX = 0, GridY = 0, Level = 1, LinkRight = true, ProductTypeId = product.Id, PurchaseSource = "LOCAL", MaxPrice = product.BasePrice },
            new BuildingUnit { Id = Guid.NewGuid(), BuildingId = buildingId, UnitType = UnitType.PublicSales, GridX = 1, GridY = 0, Level = 1, ProductTypeId = product.Id, MinPrice = product.BasePrice }
        );
    }

    private static async Task<(BuildingLot Lot, Building Building)> PrepareLotPurchaseAsync(
        AppDbContext db,
        Company company,
        Guid lotId,
        string buildingType,
        string buildingName,
        decimal powerConsumption,
        DateTime builtAtUtc,
        Guid? expectedCityId = null)
    {
        var lot = await db.BuildingLots
            .Include(candidate => candidate.City)
            .FirstOrDefaultAsync(candidate => candidate.Id == lotId);

        if (lot is null)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Building lot not found.")
                    .SetCode("LOT_NOT_FOUND")
                    .Build());
        }

        if (lot.OwnerCompanyId is not null)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("This lot has already been purchased.")
                    .SetCode("LOT_ALREADY_OWNED")
                    .Build());
        }

        if (expectedCityId.HasValue && lot.CityId != expectedCityId.Value)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("This lot does not belong to the selected onboarding city.")
                    .SetCode("LOT_CITY_MISMATCH")
                    .Build());
        }

        if (!BuildingType.All.Contains(buildingType))
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage($"Invalid building type: {buildingType}")
                    .SetCode("INVALID_BUILDING_TYPE")
                    .Build());
        }

        var suitableTypes = lot.SuitableTypes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (!suitableTypes.Contains(buildingType))
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage($"Building type {buildingType} is not suitable for this lot. Suitable types: {lot.SuitableTypes}")
                    .SetCode("UNSUITABLE_BUILDING_TYPE")
                    .Build());
        }

        if (company.Cash < lot.Price)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage($"Insufficient funds. This lot costs ${lot.Price:N0} but you only have ${company.Cash:N0}.")
                    .SetCode("INSUFFICIENT_FUNDS")
                    .Build());
        }

        company.Cash -= lot.Price;

        var building = new Building
        {
            Id = Guid.NewGuid(),
            CompanyId = company.Id,
            CityId = lot.CityId,
            Type = buildingType,
            Name = buildingName,
            Latitude = lot.Latitude,
            Longitude = lot.Longitude,
            Level = 1,
            PowerConsumption = powerConsumption,
            BuiltAtUtc = builtAtUtc
        };

        db.Buildings.Add(building);
        lot.OwnerCompanyId = company.Id;
        lot.BuildingId = building.Id;
        lot.ConcurrencyToken = Guid.NewGuid();

        var currentTick = (await db.GameStates.AsNoTracking().FirstOrDefaultAsync())?.CurrentTick ?? 0;
        db.LedgerEntries.Add(new LedgerEntry
        {
            Id = Guid.NewGuid(),
            CompanyId = company.Id,
            BuildingId = building.Id,
            Category = LedgerCategory.PropertyPurchase,
            Description = $"Purchased lot: {lot.Name}",
            Amount = -lot.Price,
            RecordedAtTick = currentTick,
            RecordedAtUtc = builtAtUtc,
        });

        return (lot, building);
    }

    private static async Task EnsureSubmittedProductsAreAccessibleAsync(
        AppDbContext db,
        Building building,
        IReadOnlyCollection<BuildingConfigurationUnitInput> submittedUnits,
        bool hasActiveProSubscription)
    {
        var submittedProductIds = submittedUnits
            .Where(unit => unit.ProductTypeId is not null)
            .Select(unit => unit.ProductTypeId!.Value)
            .Distinct()
            .ToList();

        if (submittedProductIds.Count == 0)
        {
            return;
        }

        var productsById = await db.ProductTypes
            .Where(product => submittedProductIds.Contains(product.Id))
            .ToDictionaryAsync(product => product.Id);

        foreach (var unit in submittedUnits.Where(candidate => candidate.ProductTypeId is not null))
        {
            var productId = unit.ProductTypeId!.Value;
            if (!productsById.TryGetValue(productId, out var product))
            {
                throw new GraphQLException(
                    ErrorBuilder.New()
                        .SetMessage("Product not found.")
                        .SetCode("INVALID_PRODUCT")
                        .Build());
            }

            if (!product.IsProOnly
                || hasActiveProSubscription
                || IsRetainingExistingProProduct(building, unit.UnitType, unit.GridX, unit.GridY, productId))
            {
                continue;
            }

            throw ProductAccessService.CreateProAccessException(product.Name);
        }
    }

    private static bool IsRetainingExistingProProduct(Building building, string unitType, int gridX, int gridY, Guid productTypeId)
    {
        return building.Units.Any(unit =>
                   unit.UnitType == unitType
                   && unit.GridX == gridX
                   && unit.GridY == gridY
                   && unit.ProductTypeId == productTypeId)
               || (building.PendingConfiguration?.Units.Any(unit =>
                   unit.UnitType == unitType
                    && unit.GridX == gridX
                    && unit.GridY == gridY
                    && unit.ProductTypeId == productTypeId) ?? false);
    }

    /// <summary>Queues a building configuration update that becomes active after the required ticks have passed.</summary>
    [Authorize]
    public async Task<BuildingConfigurationPlan> StoreBuildingConfiguration(
        StoreBuildingConfigurationInput input,
        [Service] AppDbContext db,
        [Service] IHttpContextAccessor httpContextAccessor)
    {
        var userId = httpContextAccessor.HttpContext!.User.GetRequiredUserId();

        var gameState = await db.GameStates.FirstOrDefaultAsync()
            ?? throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Game state is not initialized.")
                    .SetCode("GAME_STATE_NOT_FOUND")
                    .Build());

        await BuildingConfigurationService.ApplyDuePlansAsync(db, gameState.CurrentTick);

        var building = await db.Buildings
            .Include(candidate => candidate.Company)
            .Include(candidate => candidate.Units)
            .Include(candidate => candidate.PendingConfiguration)
            .ThenInclude(plan => plan!.Units)
            .Include(candidate => candidate.PendingConfiguration)
            .ThenInclude(plan => plan!.Removals)
            .FirstOrDefaultAsync(candidate => candidate.Id == input.BuildingId);

        if (building is null || building.Company.PlayerId != userId)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Building not found or you don't own it.")
                    .SetCode("BUILDING_NOT_FOUND")
                    .Build());
        }

        if (!BuildingConfigurationService.GetAllowedUnitTypes(building.Type).Any())
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("This building type does not support editable unit configurations.")
                    .SetCode("BUILDING_CONFIGURATION_NOT_SUPPORTED")
                    .Build());
        }

        var subscriptionEndsAtUtc = await db.Players
            .Where(player => player.Id == userId)
            .Select(player => player.ProSubscriptionEndsAtUtc)
            .FirstOrDefaultAsync();
        var hasActiveProSubscription = ProductAccessService.HasActiveProSubscription(subscriptionEndsAtUtc, DateTime.UtcNow);
        await EnsureSubmittedProductsAreAccessibleAsync(db, building, input.Units, hasActiveProSubscription);

        var plan = await BuildingConfigurationService.StoreConfigurationAsync(db, building, input.Units, gameState.CurrentTick);
        await db.SaveChangesAsync();

        return await db.BuildingConfigurationPlans
            .Include(candidate => candidate.Units)
            .Include(candidate => candidate.Removals)
            .FirstAsync(candidate => candidate.Id == plan.Id);
    }

    /// <summary>Marks the player's startup-pack offer as shown when the UI presents it.</summary>
    [Authorize]
    public async Task<StartupPackOffer?> MarkStartupPackOfferShown(
        [Service] AppDbContext db,
        [Service] IHttpContextAccessor httpContextAccessor)
    {
        var userId = httpContextAccessor.HttpContext!.User.GetRequiredUserId();
        var player = await db.Players.FirstOrDefaultAsync(candidate => candidate.Id == userId);
        if (player is null)
        {
            return null;
        }

        var offer = await StartupPackService.EnsureOfferForPlayerAsync(db, player, DateTime.UtcNow);
        if (offer is null)
        {
            return null;
        }

        if (StartupPackService.MarkShown(offer, DateTime.UtcNow))
        {
            await db.SaveChangesAsync();
        }

        return offer;
    }

    /// <summary>Stores a player dismissal while keeping the offer revisit-able until expiry.</summary>
    [Authorize]
    public async Task<StartupPackOffer?> DismissStartupPackOffer(
        [Service] AppDbContext db,
        [Service] IHttpContextAccessor httpContextAccessor)
    {
        var userId = httpContextAccessor.HttpContext!.User.GetRequiredUserId();
        var player = await db.Players.FirstOrDefaultAsync(candidate => candidate.Id == userId);
        if (player is null)
        {
            return null;
        }

        var offer = await StartupPackService.EnsureOfferForPlayerAsync(db, player, DateTime.UtcNow);
        if (offer is null)
        {
            return null;
        }

        if (StartupPackService.Dismiss(offer, DateTime.UtcNow))
        {
            await db.SaveChangesAsync();
        }

        return offer;
    }

    /// <summary>Claims the startup pack and grants both Pro time and expansion capital exactly once.</summary>
    [Authorize]
    public async Task<StartupPackClaimResult> ClaimStartupPack(
        ClaimStartupPackInput input,
        [Service] AppDbContext db,
        [Service] IHttpContextAccessor httpContextAccessor)
    {
        var userId = httpContextAccessor.HttpContext!.User.GetRequiredUserId();
        for (var attempt = 0; attempt < StartupPackService.MaxClaimRetryAttempts; attempt++)
        {
            try
            {
                var nowUtc = DateTime.UtcNow;
                var player = await db.Players
                    .Include(candidate => candidate.Companies)
                    .FirstOrDefaultAsync(candidate => candidate.Id == userId)
                    ?? throw new GraphQLException(
                        ErrorBuilder.New()
                            .SetMessage("Player not found.")
                            .SetCode("PLAYER_NOT_FOUND")
                            .Build());

                var offer = await StartupPackService.EnsureOfferForPlayerAsync(db, player, nowUtc)
                    ?? throw new GraphQLException(
                        ErrorBuilder.New()
                            .SetMessage("Startup pack is not available for this player.")
                            .SetCode("STARTUP_PACK_NOT_AVAILABLE")
                            .Build());

                if (StartupPackService.TryExpireOffer(offer, nowUtc))
                {
                    await db.SaveChangesAsync();
                }

                if (offer.Status == StartupPackOfferStatus.Expired)
                {
                    throw new GraphQLException(
                        ErrorBuilder.New()
                            .SetMessage("Startup pack offer has expired.")
                            .SetCode("STARTUP_PACK_EXPIRED")
                            .Build());
                }

                if (offer.Status == StartupPackOfferStatus.Claimed && offer.GrantedCompanyId is null)
                {
                    throw new GraphQLException(
                        ErrorBuilder.New()
                            .SetMessage("Startup pack claim state is incomplete.")
                            .SetCode("STARTUP_PACK_CLAIM_INTEGRITY")
                            .Build());
                }

                Company? company;
                if (offer.Status == StartupPackOfferStatus.Claimed && offer.GrantedCompanyId is not null)
                {
                    company = player.Companies.FirstOrDefault(candidate => candidate.Id == offer.GrantedCompanyId);
                }
                else
                {
                    company = player.Companies.FirstOrDefault(candidate => candidate.Id == input.CompanyId);
                }

                if (company is null)
                {
                    throw new GraphQLException(
                        ErrorBuilder.New()
                            .SetMessage("Company not found or you don't own it.")
                            .SetCode("COMPANY_NOT_FOUND")
                            .Build());
                }

                if (offer.Status != StartupPackOfferStatus.Claimed)
                {
                    StartupPackService.MarkShown(offer, nowUtc);
                    company.Cash += offer.CompanyCashGrant;
                    // If the player already has active Pro time from another source, extend from that
                    // future end-date instead of overwriting it or restarting from "now".
                    var subscriptionStart = player.ProSubscriptionEndsAtUtc is { } endsAt && endsAt > offer.CreatedAtUtc
                        ? endsAt
                        : offer.CreatedAtUtc;
                    player.ProSubscriptionEndsAtUtc = subscriptionStart.AddDays(offer.ProDurationDays);
                    StartupPackService.MarkClaimed(offer, company.Id, nowUtc);

                    // Bump concurrency tokens for atomic settlement
                    player.ConcurrencyToken = Guid.NewGuid();

                    // The startup-pack offer carries the concurrency token for the atomic settlement.
                    // If another request claims first, this SaveChanges rolls back the entire grant
                    // and the retry path below re-reads the already-claimed durable state.
                    await db.SaveChangesAsync();
                }

                var companySnapshot = await db.Companies
                    .AsNoTracking()
                    .FirstAsync(candidate =>
                        candidate.Id == offer.GrantedCompanyId!.Value
                        && candidate.PlayerId == player.Id);
                var playerSnapshot = await db.Players
                    .AsNoTracking()
                    .FirstAsync(candidate => candidate.Id == player.Id);

                return new StartupPackClaimResult
                {
                    Offer = offer,
                    Company = companySnapshot,
                    ProSubscriptionEndsAtUtc = playerSnapshot.ProSubscriptionEndsAtUtc ?? nowUtc
                };
            }
            catch (DbUpdateConcurrencyException) when (attempt < StartupPackService.MaxClaimRetryAttempts - 1)
            {
                db.ChangeTracker.Clear();
                await Task.Delay(StartupPackService.ClaimRetryBaseDelayMs * (attempt + 1));
            }
        }

        throw new GraphQLException(
            ErrorBuilder.New()
                .SetMessage("Startup pack claim could not be completed safely. Please try again.")
                .SetCode("STARTUP_PACK_CLAIM_RETRY")
                .Build());
    }

    /// <summary>Sets or clears the for-sale status and asking price of a building.</summary>
    [Authorize]
    public async Task<Building> SetBuildingForSale(
        SetBuildingForSaleInput input,
        [Service] AppDbContext db,
        [Service] IHttpContextAccessor httpContextAccessor)
    {
        var userId = httpContextAccessor.HttpContext!.User.GetRequiredUserId();

        var building = await db.Buildings
            .Include(b => b.Company)
            .Include(b => b.Units)
            .FirstOrDefaultAsync(b => b.Id == input.BuildingId);

        if (building is null || building.Company.PlayerId != userId)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Building not found or you don't own it.")
                    .SetCode("BUILDING_NOT_FOUND")
                    .Build());
        }

        building.IsForSale = input.IsForSale;
        building.AskingPrice = input.IsForSale ? input.AskingPrice : null;

        await db.SaveChangesAsync();
        return building;
    }

    /// <summary>Purchases a building lot and places a building on it.</summary>
    [Authorize]
    public async Task<PurchaseLotResult> PurchaseLot(
        PurchaseLotInput input,
        [Service] AppDbContext db,
        [Service] IHttpContextAccessor httpContextAccessor)
    {
        var userId = httpContextAccessor.HttpContext!.User.GetRequiredUserId();

        var company = await db.Companies.FirstOrDefaultAsync(
            c => c.Id == input.CompanyId && c.PlayerId == userId);

        if (company is null)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Company not found or you don't own it.")
                    .SetCode("COMPANY_NOT_FOUND")
                    .Build());
        }

        var (lot, building) = await PrepareLotPurchaseAsync(
            db,
            company,
            input.LotId,
            input.BuildingType,
            input.BuildingName,
            1m,
            DateTime.UtcNow);

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("This lot has already been purchased.")
                    .SetCode("LOT_ALREADY_OWNED")
                    .Build());
        }

        return new PurchaseLotResult
        {
            Lot = lot,
            Building = building,
            Company = company
        };
    }

    /// <summary>
    /// Marks the first-sale onboarding milestone as completed for the current player.
    /// Validates backend-authoritative conditions: the player must have a sales shop
    /// created during onboarding with at least one configured PUBLIC_SALES unit.
    /// Idempotent once the milestone has been granted.
    /// </summary>
    [Authorize]
    public async Task<Player> CompleteFirstSaleMilestone(
        [Service] AppDbContext db,
        [Service] IHttpContextAccessor httpContextAccessor)
    {
        var userId = httpContextAccessor.HttpContext!.User.GetRequiredUserId();
        var player = await db.Players
            .Include(p => p.Companies)
            .FirstOrDefaultAsync(p => p.Id == userId)
            ?? throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Player not found.")
                    .SetCode("PLAYER_NOT_FOUND")
                    .Build());

        // Idempotent: already completed
        if (player.OnboardingFirstSaleCompletedAtUtc is not null)
        {
            return player;
        }

        if (player.OnboardingShopBuildingId is null)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("No sales shop was found for this onboarding milestone. Please complete the onboarding setup first.")
                    .SetCode("SHOP_NOT_FOUND")
                    .Build());
        }

        // Verify the shop belongs to this player and has a configured public-sales unit
        var shopBuilding = await db.Buildings
            .Include(b => b.Units)
            .FirstOrDefaultAsync(b => b.Id == player.OnboardingShopBuildingId);

        if (shopBuilding is null)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Sales shop building not found.")
                    .SetCode("SHOP_NOT_FOUND")
                    .Build());
        }

        // Verify ownership via the company chain
        var ownsShop = await db.Companies
            .AnyAsync(c => c.Id == shopBuilding.CompanyId && c.PlayerId == userId);

        if (!ownsShop)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("You do not own this sales shop.")
                    .SetCode("SHOP_NOT_FOUND")
                    .Build());
        }

        // Check backend-authoritative condition: shop must have a PUBLIC_SALES unit with a price set
        var hasSalesUnit = shopBuilding.Units.Any(u =>
            string.Equals(u.UnitType, UnitType.PublicSales, StringComparison.Ordinal)
            && u.MinPrice > 0);

        if (!hasSalesUnit)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Your sales shop is not yet configured. Please set up a public sales unit with a selling price and return here to complete the milestone.")
                    .SetCode("SHOP_NOT_CONFIGURED")
                    .Build());
        }

        player.OnboardingFirstSaleCompletedAtUtc = DateTime.UtcNow;
        player.OnboardingShopBuildingId = null;
        await db.SaveChangesAsync();

        return player;
    }

    private static AuthenticatedSession GenerateToken(Player player, JwtOptions options)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.SigningKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var expires = DateTime.UtcNow.AddMinutes(options.ExpiresMinutes);

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, player.Id.ToString()),
            new Claim(ClaimTypes.Email, player.Email),
            new Claim(ClaimTypes.Name, player.DisplayName),
            new Claim(ClaimTypes.Role, player.Role)
        };

        var token = new JwtSecurityToken(
            issuer: options.Issuer,
            audience: options.Audience,
            claims: claims,
            expires: expires,
            signingCredentials: credentials);

        return new AuthenticatedSession(
            new JwtSecurityTokenHandler().WriteToken(token),
            expires);
    }
}

/// <summary>Auth response payload.</summary>
public sealed class AuthPayload
{
    /// <summary>JWT bearer token.</summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>Token expiration time in UTC.</summary>
    public DateTime ExpiresAtUtc { get; set; }

    /// <summary>Authenticated player profile.</summary>
    public Player Player { get; set; } = null!;
}

/// <summary>Onboarding completion result.</summary>
public sealed class OnboardingResult
{
    /// <summary>The newly created company.</summary>
    public Company Company { get; set; } = null!;

    /// <summary>The factory building with default units.</summary>
    public Building Factory { get; set; } = null!;

    /// <summary>The sales shop building with default units.</summary>
    public Building SalesShop { get; set; } = null!;

    /// <summary>The product selected for manufacturing.</summary>
    public ProductType SelectedProduct { get; set; } = null!;

    /// <summary>The player's startup-pack offer, if one is available after onboarding.</summary>
    public StartupPackOffer? StartupPackOffer { get; set; }
}

/// <summary>Result of purchasing the first factory during lot-based onboarding.</summary>
public sealed class OnboardingStartResult
{
    /// <summary>The newly created company.</summary>
    public Company Company { get; set; } = null!;

    /// <summary>The first factory placed on the selected lot.</summary>
    public Building Factory { get; set; } = null!;

    /// <summary>The lot that now hosts the player's first factory.</summary>
    public BuildingLot FactoryLot { get; set; } = null!;

    /// <summary>The next onboarding step the client should guide the player to.</summary>
    public string NextStep { get; set; } = string.Empty;
}

/// <summary>Claim result payload for the startup-pack offer.</summary>
public sealed class StartupPackClaimResult
{
    /// <summary>Updated lifecycle state for the startup-pack offer.</summary>
    public StartupPackOffer Offer { get; set; } = null!;

    /// <summary>Company that received the startup capital grant.</summary>
    public Company Company { get; set; } = null!;

    /// <summary>UTC timestamp until which the player now has Pro access.</summary>
    public DateTime ProSubscriptionEndsAtUtc { get; set; }
}

/// <summary>Result of purchasing a building lot.</summary>
public sealed class PurchaseLotResult
{
    /// <summary>The purchased lot with updated ownership.</summary>
    public BuildingLot Lot { get; set; } = null!;

    /// <summary>The building placed on the lot.</summary>
    public Building Building { get; set; } = null!;

    /// <summary>The company with updated cash balance.</summary>
    public Company Company { get; set; } = null!;
}
