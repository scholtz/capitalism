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
            Cash = 1_000_000m, // Starting capital
            FoundedAtUtc = DateTime.UtcNow
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
            FoundedAtUtc = DateTime.UtcNow
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
            BuiltAtUtc = DateTime.UtcNow
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
            BuiltAtUtc = DateTime.UtcNow
        };
        db.Buildings.Add(shop);

        // Add default shop units: Purchase, Public Sales
        db.BuildingUnits.AddRange(
            new BuildingUnit { Id = Guid.NewGuid(), BuildingId = shop.Id, UnitType = UnitType.Purchase, GridX = 0, GridY = 0, Level = 1, LinkRight = true, ProductTypeId = product.Id, PurchaseSource = "LOCAL", MaxPrice = product.BasePrice },
            new BuildingUnit { Id = Guid.NewGuid(), BuildingId = shop.Id, UnitType = UnitType.PublicSales, GridX = 1, GridY = 0, Level = 1, ProductTypeId = product.Id, MinPrice = product.BasePrice }
        );

        // Mark onboarding as completed for this player
        var player = await db.Players.FindAsync(userId);
        if (player is not null)
        {
            player.OnboardingCompletedAtUtc = DateTime.UtcNow;
        }

        await db.SaveChangesAsync();

        return new OnboardingResult
        {
            Company = company,
            Factory = factory,
            SalesShop = shop,
            SelectedProduct = product
        };
    }

    private static bool IsStarterOnboardingProduct(ProductType product)
    {
        return StarterOnboardingProductByIndustry.TryGetValue(product.Industry, out var starterSlug)
            && string.Equals(product.Slug, starterSlug, StringComparison.Ordinal);
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

        var plan = await BuildingConfigurationService.StoreConfigurationAsync(db, building, input.Units, gameState.CurrentTick);
        await db.SaveChangesAsync();

        return await db.BuildingConfigurationPlans
            .Include(candidate => candidate.Units)
            .Include(candidate => candidate.Removals)
            .FirstAsync(candidate => candidate.Id == plan.Id);
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
}
