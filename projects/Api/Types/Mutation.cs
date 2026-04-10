using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Api.Data;
using Api.Data.Entities;
using Api.Engine;
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
    private const decimal StarterFounderContribution = 50_000m;
    private const decimal DefaultDividendPayoutRatio = 0.2m;
    private const decimal DefaultCompanyShareCount = 10_000m;

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
            PersonalCash = 200_000m,
            ActiveAccountType = AccountContextType.Person,
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

    [Authorize]
    public async Task<AuthPayload> StartAdminImpersonation(
        StartAdminImpersonationInput input,
        [Service] AppDbContext db,
        [Service] IHttpContextAccessor httpContextAccessor,
        [Service] IOptions<JwtOptions> jwtOptions,
        [Service] GameAdminAuthorizationService gameAdminAuthorizationService)
    {
        var principal = httpContextAccessor.HttpContext!.User;
        var accessContext = await gameAdminAuthorizationService.RequireAdminDashboardAccessAsync(db, principal, httpContextAccessor.HttpContext.RequestAborted);
        var targetPlayer = await db.Players
            .AsNoTracking()
            .Include(player => player.Companies)
            .FirstOrDefaultAsync(player => player.Id == input.TargetPlayerId, httpContextAccessor.HttpContext.RequestAborted)
            ?? throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Player not found.")
                    .SetCode("PLAYER_NOT_FOUND")
                    .Build());

        var impersonationContext = ResolveImpersonationAccountContext(targetPlayer, input);
        targetPlayer.ActiveAccountType = impersonationContext.EffectiveAccountType;
        targetPlayer.ActiveCompanyId = impersonationContext.EffectiveCompanyId;

        var session = GenerateToken(accessContext.ActorPlayer, jwtOptions.Value, new AdminImpersonationTokenContext(
            targetPlayer,
            impersonationContext.EffectiveAccountType,
            impersonationContext.EffectiveCompanyId,
            impersonationContext.EffectiveCompanyName));

        return new AuthPayload
        {
            Token = session.Token,
            ExpiresAtUtc = session.ExpiresAtUtc,
            Player = targetPlayer,
        };
    }

    [Authorize]
    public async Task<AuthPayload> StopAdminImpersonation(
        [Service] AppDbContext db,
        [Service] IHttpContextAccessor httpContextAccessor,
        [Service] IOptions<JwtOptions> jwtOptions)
    {
        var actorUserId = httpContextAccessor.HttpContext!.User.GetAuthenticatedActorUserId();
        var actorPlayer = await db.Players
            .AsNoTracking()
            .Include(player => player.Companies)
            .FirstOrDefaultAsync(player => player.Id == actorUserId, httpContextAccessor.HttpContext.RequestAborted)
            ?? throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Player not found.")
                    .SetCode("PLAYER_NOT_FOUND")
                    .Build());

        var session = GenerateToken(actorPlayer, jwtOptions.Value);
        return new AuthPayload
        {
            Token = session.Token,
            ExpiresAtUtc = session.ExpiresAtUtc,
            Player = actorPlayer,
        };
    }

    [Authorize]
    public async Task<GameAdminPlayerSummary> SetPlayerInvisibleInChat(
        SetPlayerInvisibleInChatInput input,
        [Service] AppDbContext db,
        [Service] IHttpContextAccessor httpContextAccessor,
        [Service] GameAdminAuthorizationService gameAdminAuthorizationService)
    {
        await gameAdminAuthorizationService.RequireAdminDashboardAccessAsync(db, httpContextAccessor.HttpContext!.User, httpContextAccessor.HttpContext.RequestAborted);
        var player = await db.Players
            .Include(candidate => candidate.Companies)
            .FirstOrDefaultAsync(candidate => candidate.Id == input.PlayerId, httpContextAccessor.HttpContext.RequestAborted)
            ?? throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Player not found.")
                    .SetCode("PLAYER_NOT_FOUND")
                    .Build());

        player.IsInvisibleInChat = input.IsInvisibleInChat;
        await db.SaveChangesAsync(httpContextAccessor.HttpContext.RequestAborted);

        return new GameAdminPlayerSummary
        {
            Id = player.Id,
            Email = player.Email,
            DisplayName = player.DisplayName,
            Role = player.Role,
            IsInvisibleInChat = player.IsInvisibleInChat,
            LastLoginAtUtc = player.LastLoginAtUtc,
            PersonalCash = player.PersonalCash,
            TotalCompanyCash = player.Companies.Sum(company => company.Cash),
            CompanyCount = player.Companies.Count,
            Companies = player.Companies.Select(company => new GameAdminCompanySummary
            {
                Id = company.Id,
                Name = company.Name,
                Cash = company.Cash,
            }).ToList(),
        };
    }

    [Authorize]
    public async Task<GameAdminPlayerSummary> SetLocalGameAdminRole(
        SetLocalGameAdminRoleInput input,
        [Service] AppDbContext db,
        [Service] IHttpContextAccessor httpContextAccessor,
        [Service] GameAdminAuthorizationService gameAdminAuthorizationService)
    {
        await gameAdminAuthorizationService.RequireRootAccessAsync(db, httpContextAccessor.HttpContext!.User, httpContextAccessor.HttpContext.RequestAborted);
        var player = await db.Players
            .Include(candidate => candidate.Companies)
            .FirstOrDefaultAsync(candidate => candidate.Id == input.PlayerId, httpContextAccessor.HttpContext.RequestAborted)
            ?? throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Player not found.")
                    .SetCode("PLAYER_NOT_FOUND")
                    .Build());

        player.Role = input.IsAdmin ? PlayerRole.Admin : PlayerRole.Player;
        await db.SaveChangesAsync(httpContextAccessor.HttpContext.RequestAborted);

        return new GameAdminPlayerSummary
        {
            Id = player.Id,
            Email = player.Email,
            DisplayName = player.DisplayName,
            Role = player.Role,
            IsInvisibleInChat = player.IsInvisibleInChat,
            LastLoginAtUtc = player.LastLoginAtUtc,
            PersonalCash = player.PersonalCash,
            TotalCompanyCash = player.Companies.Sum(company => company.Cash),
            CompanyCount = player.Companies.Count,
            Companies = player.Companies.Select(company => new GameAdminCompanySummary
            {
                Id = company.Id,
                Name = company.Name,
                Cash = company.Cash,
            }).ToList(),
        };
    }

    [Authorize]
    public async Task<GlobalGameAdminGrantSummary> AssignGlobalGameAdminRole(
        ManageGlobalGameAdminRoleInput input,
        [Service] AppDbContext db,
        [Service] IHttpContextAccessor httpContextAccessor,
        [Service] GameAdminAuthorizationService gameAdminAuthorizationService,
        [Service] IMasterGameAdministrationService masterGameAdministrationService)
    {
        var accessContext = await gameAdminAuthorizationService.RequireRootAccessAsync(db, httpContextAccessor.HttpContext!.User, httpContextAccessor.HttpContext.RequestAborted);
        return await masterGameAdministrationService.AssignGlobalGameAdminAsync(accessContext.ActorPlayer.Email, input.Email, httpContextAccessor.HttpContext.RequestAborted);
    }

    [Authorize]
    public async Task<bool> RemoveGlobalGameAdminRole(
        ManageGlobalGameAdminRoleInput input,
        [Service] AppDbContext db,
        [Service] IHttpContextAccessor httpContextAccessor,
        [Service] GameAdminAuthorizationService gameAdminAuthorizationService,
        [Service] IMasterGameAdministrationService masterGameAdministrationService)
    {
        var accessContext = await gameAdminAuthorizationService.RequireRootAccessAsync(db, httpContextAccessor.HttpContext!.User, httpContextAccessor.HttpContext.RequestAborted);
        await masterGameAdministrationService.RemoveGlobalGameAdminAsync(accessContext.ActorPlayer.Email, input.Email, httpContextAccessor.HttpContext.RequestAborted);
        return true;
    }

    [Authorize]
    public async Task<GameNewsEntryResult> UpsertGameNewsEntry(
        UpsertGameNewsEntryInput input,
        [Service] AppDbContext db,
        [Service] IHttpContextAccessor httpContextAccessor,
        [Service] GameAdminAuthorizationService gameAdminAuthorizationService,
        [Service] IMasterGameAdministrationService masterGameAdministrationService)
    {
        var accessContext = await gameAdminAuthorizationService.RequireAdminDashboardAccessAsync(db, httpContextAccessor.HttpContext!.User, httpContextAccessor.HttpContext.RequestAborted);
        return await masterGameAdministrationService.UpsertGameNewsEntryAsync(
            accessContext.ActorPlayer.Email,
            input.EntryId,
            input.EntryType,
            input.Status,
            input.Localizations,
            httpContextAccessor.HttpContext.RequestAborted);
    }

    [Authorize]
    public async Task<bool> MarkGameNewsRead(
        MarkGameNewsReadInput input,
        [Service] AppDbContext db,
        [Service] IHttpContextAccessor httpContextAccessor,
        [Service] IMasterGameAdministrationService masterGameAdministrationService)
    {
        var effectiveUserId = httpContextAccessor.HttpContext!.User.GetRequiredUserId();
        var playerEmail = await db.Players
            .AsNoTracking()
            .Where(player => player.Id == effectiveUserId)
            .Select(player => player.Email)
            .FirstOrDefaultAsync(httpContextAccessor.HttpContext.RequestAborted)
            ?? throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Player not found.")
                    .SetCode("PLAYER_NOT_FOUND")
                    .Build());

        await masterGameAdministrationService.MarkGameNewsReadAsync(playerEmail, input.EntryIds, httpContextAccessor.HttpContext.RequestAborted);
        return true;
    }

    /// <summary>Creates a new company for the authenticated player.</summary>
    [Authorize]
    public async Task<Company> CreateCompany(
        CreateCompanyInput input,
        [Service] AppDbContext db,
        [Service] IHttpContextAccessor httpContextAccessor)
    {
        var userId = httpContextAccessor.HttpContext!.User.GetRequiredUserId();
        var currentTick = await db.GameStates
            .AsNoTracking()
            .Select(state => state.CurrentTick)
            .FirstOrDefaultAsync();

        var company = new Company
        {
            Id = Guid.NewGuid(),
            PlayerId = userId,
            Name = input.Name,
            Cash = 1_000_000m // Starting capital
            ,
            TotalSharesIssued = DefaultCompanyShareCount,
            DividendPayoutRatio = DefaultDividendPayoutRatio,
            FoundedAtUtc = DateTime.UtcNow,
            FoundedAtTick = currentTick
        };

        db.Companies.Add(company);
        db.Shareholdings.Add(new Shareholding
        {
            Id = Guid.NewGuid(),
            CompanyId = company.Id,
            OwnerPlayerId = userId,
            ShareCount = company.TotalSharesIssued,
        });

        var player = await db.Players.FirstOrDefaultAsync(candidate => candidate.Id == userId);
        if (player is not null)
        {
            player.ActiveAccountType = AccountContextType.Company;
            player.ActiveCompanyId = company.Id;
        }

        await db.SaveChangesAsync();

        return company;
    }

    /// <summary>Updates a company's display name and city salary settings.</summary>
    [Authorize]
    public async Task<Company> UpdateCompanySettings(
        UpdateCompanySettingsInput input,
        [Service] AppDbContext db,
        [Service] IHttpContextAccessor httpContextAccessor)
    {
        var userId = httpContextAccessor.HttpContext!.User.GetRequiredUserId();
        var company = await db.Companies
            .Include(candidate => candidate.CitySalarySettings)
            .FirstOrDefaultAsync(candidate => candidate.Id == input.CompanyId && candidate.PlayerId == userId)
            ?? throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Company not found or you don't own it.")
                    .SetCode("COMPANY_NOT_FOUND")
                    .Build());

        var trimmedName = input.Name.Trim();
        if (string.IsNullOrEmpty(trimmedName))
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Company name cannot be empty.")
                    .SetCode("INVALID_COMPANY_NAME")
                    .Build());
        }

        var validCityIds = await db.Cities
            .Select(city => city.Id)
            .ToListAsync();
        var validCityIdSet = validCityIds.ToHashSet();

        foreach (var salarySetting in input.CitySalarySettings)
        {
            if (!validCityIdSet.Contains(salarySetting.CityId))
            {
                throw new GraphQLException(
                    ErrorBuilder.New()
                        .SetMessage("City not found.")
                        .SetCode("CITY_NOT_FOUND")
                        .Build());
            }
        }

        company.Name = trimmedName;
        if (input.DividendPayoutRatio.HasValue)
        {
            company.DividendPayoutRatio = decimal.Round(
                Math.Clamp(input.DividendPayoutRatio.Value, 0m, 1m),
                4,
                MidpointRounding.AwayFromZero);
        }

        foreach (var salarySetting in input.CitySalarySettings
                     .GroupBy(setting => setting.CityId)
                     .Select(group => group.Last()))
        {
            var multiplier = CompanyEconomyCalculator.ClampSalaryMultiplier(salarySetting.SalaryMultiplier);
            var existing = company.CitySalarySettings
                .FirstOrDefault(setting => setting.CityId == salarySetting.CityId);

            if (existing is null)
            {
                var newSetting = new CompanyCitySalarySetting
                {
                    Id = Guid.NewGuid(),
                    CompanyId = company.Id,
                    CityId = salarySetting.CityId,
                    SalaryMultiplier = multiplier,
                };

                db.CompanyCitySalarySettings.Add(newSetting);
                company.CitySalarySettings.Add(newSetting);
            }
            else
            {
                existing.SalaryMultiplier = multiplier;
            }
        }

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

        // Validate media type when placing a media house.
        if (input.Type == BuildingType.MediaHouse)
        {
            if (string.IsNullOrWhiteSpace(input.MediaType) || !Data.Entities.MediaType.All.Contains(input.MediaType))
            {
                throw new GraphQLException(
                    ErrorBuilder.New()
                        .SetMessage("A media house requires a valid MediaType: NEWSPAPER, RADIO, or TV.")
                        .SetCode("INVALID_MEDIA_TYPE")
                        .Build());
            }
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

        var currentTick = await db.GameStates
            .AsNoTracking()
            .Select(state => state.CurrentTick)
            .FirstOrDefaultAsync();
        await LandService.EnsureMinimumAvailableLotsAsync(db, currentTick, [city.Id]);
        var lotId = await FindCompatibleAvailableLotIdAsync(db, city.Id, input.Type);

        var (_, building) = await PrepareLotPurchaseAsync(
            db,
            company,
            lotId,
            input.Type,
            input.Name,
            Engine.GameConstants.PowerDemandMw(input.Type, 1),
            DateTime.UtcNow,
            city.Id);

        // Apply media type for media houses.
        if (input.Type == BuildingType.MediaHouse && !string.IsNullOrWhiteSpace(input.MediaType))
            building.MediaType = input.MediaType;

        await LandService.EnsureMinimumAvailableLotsAsync(db, currentTick, [city.Id]);
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

        // Validate company name
        var trimmedCompanyName = input.CompanyName?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(trimmedCompanyName))
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Company name cannot be empty.")
                    .SetCode("INVALID_COMPANY_NAME")
                    .Build());
        }

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

        var currentTick = await db.GameStates
            .AsNoTracking()
            .Select(state => state.CurrentTick)
            .FirstOrDefaultAsync();
        await LandService.EnsureMinimumAvailableLotsAsync(db, currentTick, [city.Id]);

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
        if (player.PersonalCash < StarterFounderContribution)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("You do not have enough personal cash to fund the founder contribution.")
                    .SetCode("INSUFFICIENT_PERSONAL_FUNDS")
                    .Build());
        }

        var ipoSelection = ResolveStarterIpoSelection(null);

        var company = new Company
        {
            Id = Guid.NewGuid(),
            PlayerId = userId,
            Name = trimmedCompanyName,
            Cash = StarterFounderContribution + ipoSelection.RaiseTarget,
            TotalSharesIssued = DefaultCompanyShareCount,
            DividendPayoutRatio = DefaultDividendPayoutRatio,
            FoundedAtUtc = nowUtc,
            FoundedAtTick = currentTick
        };
        db.Companies.Add(company);
        player.PersonalCash -= StarterFounderContribution;
        player.ActiveAccountType = AccountContextType.Company;
        player.ActiveCompanyId = company.Id;
        db.Shareholdings.Add(new Shareholding
        {
            Id = Guid.NewGuid(),
            CompanyId = company.Id,
            OwnerPlayerId = userId,
            ShareCount = ipoSelection.FounderShareCount,
        });

        var factoryLotId = await FindCompatibleAvailableLotIdAsync(db, city.Id, BuildingType.Factory);
        var (_, factory) = await PrepareLotPurchaseAsync(
            db,
            company,
            factoryLotId,
            BuildingType.Factory,
            $"{trimmedCompanyName} Factory",
            Engine.GameConstants.PowerDemandMw(BuildingType.Factory, 1),
            nowUtc,
            city.Id);
        ConfigureStarterFactory(db, factory, product, starterResourceId.Value);

        var shopLotId = await FindCompatibleAvailableLotIdAsync(db, city.Id, BuildingType.SalesShop);
        var (_, shop) = await PrepareLotPurchaseAsync(
            db,
            company,
            shopLotId,
            BuildingType.SalesShop,
            $"{trimmedCompanyName} Shop",
            Engine.GameConstants.PowerDemandMw(BuildingType.SalesShop, 1),
            nowUtc,
            city.Id);
        AddStarterShop(db, company.Id, shop.Id, product);

        // Mark onboarding as completed for this player
        player.OnboardingCompletedAtUtc = nowUtc;
        player.OnboardingShopBuildingId = shop.Id;
        ClearOnboardingProgress(player);

        await LandService.EnsureMinimumAvailableLotsAsync(db, currentTick, [city.Id]);
        await db.SaveChangesAsync();

        return new OnboardingResult
        {
            Company = company,
            Factory = factory,
            SalesShop = shop,
            SelectedProduct = product
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

        var trimmedCompanyName = input.CompanyName?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(trimmedCompanyName))
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Company name cannot be empty.")
                    .SetCode("INVALID_COMPANY_NAME")
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

        if (player.PersonalCash < StarterFounderContribution)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("You do not have enough personal cash to fund the founder contribution.")
                    .SetCode("INSUFFICIENT_PERSONAL_FUNDS")
                    .Build());
        }

        var ipoSelection = ResolveStarterIpoSelection(input.IpoRaiseTarget);

        var company = new Company
        {
            Id = Guid.NewGuid(),
            PlayerId = userId,
            Name = trimmedCompanyName,
            Cash = StarterFounderContribution + ipoSelection.RaiseTarget,
            TotalSharesIssued = DefaultCompanyShareCount,
            DividendPayoutRatio = DefaultDividendPayoutRatio,
            FoundedAtUtc = nowUtc,
            FoundedAtTick = await db.GameStates.AsNoTracking().Select(state => state.CurrentTick).FirstOrDefaultAsync()
        };
        db.Companies.Add(company);
        player.PersonalCash -= StarterFounderContribution;
        player.ActiveAccountType = AccountContextType.Company;
        player.ActiveCompanyId = company.Id;
        db.Shareholdings.Add(new Shareholding
        {
            Id = Guid.NewGuid(),
            CompanyId = company.Id,
            OwnerPlayerId = userId,
            ShareCount = ipoSelection.FounderShareCount,
        });

        var (factoryLot, factory) = await PrepareLotPurchaseAsync(
            db,
            company,
            input.FactoryLotId,
            BuildingType.Factory,
            $"{trimmedCompanyName} Factory",
            Engine.GameConstants.PowerDemandMw(BuildingType.Factory, 1),
            nowUtc,
            input.CityId);

        AddStarterFactoryShell(db, factory.Id);

        player.OnboardingCurrentStep = OnboardingProgressStep.ShopSelection;
        player.OnboardingIndustry = input.Industry;
        player.OnboardingCityId = input.CityId;
        player.OnboardingCompanyId = company.Id;
        player.OnboardingFactoryLotId = factoryLot.Id;
        var currentTick = await db.GameStates
            .AsNoTracking()
            .Select(state => state.CurrentTick)
            .FirstOrDefaultAsync();

        try
        {
            await LandService.EnsureMinimumAvailableLotsAsync(db, currentTick, [input.CityId]);
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

        var onboardingCityId = player.OnboardingCityId!.Value;

        var (_, shop) = await PrepareLotPurchaseAsync(
            db,
            company,
            input.ShopLotId,
            BuildingType.SalesShop,
            $"{company.Name} Shop",
            Engine.GameConstants.PowerDemandMw(BuildingType.SalesShop, 1),
            nowUtc,
            onboardingCityId);

        ConfigureStarterFactory(db, factory, product, starterResourceId.Value);
        AddStarterShop(db, company.Id, shop.Id, product);

        player.OnboardingCompletedAtUtc = nowUtc;
        player.OnboardingShopBuildingId = shop.Id;
        player.ActiveAccountType = AccountContextType.Company;
        player.ActiveCompanyId = company.Id;
        ClearOnboardingProgress(player);
        var currentTick = await db.GameStates
            .AsNoTracking()
            .Select(state => state.CurrentTick)
            .FirstOrDefaultAsync();

        try
        {
            await LandService.EnsureMinimumAvailableLotsAsync(db, currentTick, [onboardingCityId]);
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

        return new OnboardingResult
        {
            Company = company,
            Factory = factory,
            SalesShop = shop,
            SelectedProduct = product
        };
    }

    /// <summary>Switches the authenticated player's acting account between PERSON and one controlled COMPANY.</summary>
    [Authorize]
    public async Task<AccountContextResult> SwitchAccountContext(
        SwitchAccountContextInput input,
        [Service] AppDbContext db,
        [Service] IHttpContextAccessor httpContextAccessor)
    {
        var userId = httpContextAccessor.HttpContext!.User.GetRequiredUserId();
        var player = await db.Players.FirstOrDefaultAsync(candidate => candidate.Id == userId)
            ?? throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Player not found.")
                    .SetCode("PLAYER_NOT_FOUND")
                    .Build());

        if (string.Equals(input.AccountType, AccountContextType.Person, StringComparison.OrdinalIgnoreCase))
        {
            player.ActiveAccountType = AccountContextType.Person;
            player.ActiveCompanyId = null;
            await db.SaveChangesAsync();

            return new AccountContextResult
            {
                ActiveAccountType = AccountContextType.Person,
                ActiveCompanyId = null,
                ActiveAccountName = player.DisplayName,
            };
        }

        if (!string.Equals(input.AccountType, AccountContextType.Company, StringComparison.OrdinalIgnoreCase) || input.CompanyId is null)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("A valid company account selection is required.")
                    .SetCode("INVALID_ACCOUNT_CONTEXT")
                    .Build());
        }

        var companies = await db.Companies.ToListAsync();
        var targetCompany = companies.FirstOrDefault(company => company.Id == input.CompanyId.Value)
            ?? throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Company not found.")
                    .SetCode("COMPANY_NOT_FOUND")
                    .Build());

        if (targetCompany.PlayerId != userId)
        {
            var shareholdings = await db.Shareholdings
                .Where(holding => holding.CompanyId == targetCompany.Id)
                .ToListAsync();
            var controlledOwnershipRatio = ComputeControlledOwnershipRatio(userId, targetCompany, companies, shareholdings);

            if (controlledOwnershipRatio < 0.5m)
            {
                throw new GraphQLException(
                    ErrorBuilder.New()
                        .SetMessage("You need at least 50% combined ownership through your person account and controlled companies to switch into this company.")
                        .SetCode("COMPANY_CONTROL_REQUIRED")
                        .Build());
            }

            targetCompany.PlayerId = userId;
        }

        player.ActiveAccountType = AccountContextType.Company;
        player.ActiveCompanyId = targetCompany.Id;
        await db.SaveChangesAsync();

        return new AccountContextResult
        {
            ActiveAccountType = AccountContextType.Company,
            ActiveCompanyId = targetCompany.Id,
            ActiveAccountName = targetCompany.Name,
        };
    }

    /// <summary>Purchases shares from public investors using either the personal account or the selected company account.</summary>
    [Authorize]
    public async Task<ShareTradeResult> BuyShares(
        BuySharesInput input,
        [Service] AppDbContext db,
        [Service] IHttpContextAccessor httpContextAccessor)
    {
        if (input.ShareCount <= 0m)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Share count must be greater than zero.")
                    .SetCode("INVALID_SHARE_COUNT")
                    .Build());
        }

        var userId = httpContextAccessor.HttpContext!.User.GetRequiredUserId();
        var player = await db.Players.FirstOrDefaultAsync(candidate => candidate.Id == userId)
            ?? throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Player not found.")
                    .SetCode("PLAYER_NOT_FOUND")
                    .Build());

    var account = !string.IsNullOrEmpty(input.TradeAccountType)
        ? await ResolveRequestedTradingAccountAsync(db, player, input.TradeAccountType, input.TradeAccountCompanyId)
        : await ResolveActiveTradingAccountAsync(db, player, httpContextAccessor.HttpContext!.User);
        var (companies, shareholdings, sharePrices) = await LoadSharePricingSnapshotAsync(db);
        var targetCompany = companies.FirstOrDefault(company => company.Id == input.CompanyId)
            ?? throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Company not found.")
                    .SetCode("COMPANY_NOT_FOUND")
                    .Build());

        var shareCount = decimal.Round(input.ShareCount, 4, MidpointRounding.AwayFromZero);
        var publicFloatShares = SharePriceCalculator.ComputePublicFloat(targetCompany, shareholdings.Where(holding => holding.CompanyId == targetCompany.Id));
        if (publicFloatShares < shareCount)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Not enough public-float shares are available at the moment.")
                    .SetCode("INSUFFICIENT_PUBLIC_FLOAT")
                    .Build());
        }

        var sharePrice = sharePrices.GetValueOrDefault(targetCompany.Id);
        var askPrice = SharePriceCalculator.ComputeAskPrice(sharePrice);
        var totalValue = decimal.Round(askPrice * shareCount, 4, MidpointRounding.AwayFromZero);

        if (account.Company is null)
        {
            if (player.PersonalCash < totalValue)
            {
                throw new GraphQLException(
                    ErrorBuilder.New()
                        .SetMessage("Insufficient personal cash for this share purchase.")
                        .SetCode("INSUFFICIENT_PERSONAL_FUNDS")
                        .Build());
            }

            player.PersonalCash -= totalValue;
        }
        else
        {
            if (account.Company.Cash < totalValue)
            {
                throw new GraphQLException(
                    ErrorBuilder.New()
                        .SetMessage("The selected company does not have enough cash for this share purchase.")
                        .SetCode("INSUFFICIENT_COMPANY_FUNDS")
                        .Build());
            }

            account.Company.Cash -= totalValue;

            if (account.Company.Id == targetCompany.Id)
            {
                targetCompany.TotalSharesIssued = Math.Max(0m, decimal.Round(targetCompany.TotalSharesIssued - shareCount, 4, MidpointRounding.AwayFromZero));
                await db.SaveChangesAsync();

                return new ShareTradeResult
                {
                    CompanyId = targetCompany.Id,
                    CompanyName = targetCompany.Name,
                    AccountType = AccountContextType.Company,
                    AccountCompanyId = account.Company.Id,
                    AccountName = account.AccountName,
                    ShareCount = shareCount,
                    PricePerShare = askPrice,
                    TotalValue = totalValue,
                    OwnedShareCount = 0m,
                    PublicFloatShares = SharePriceCalculator.ComputePublicFloat(targetCompany, shareholdings.Where(holding => holding.CompanyId == targetCompany.Id)),
                    PersonalCash = player.PersonalCash,
                    CompanyCash = account.Company.Cash,
                };
            }
        }

        var holding = GetOrCreateShareholding(
            db,
            shareholdings,
            targetCompany.Id,
            account.Company is null ? player.Id : null,
            account.Company?.Id);
        holding.ShareCount = decimal.Round(holding.ShareCount + shareCount, 4, MidpointRounding.AwayFromZero);

        await db.SaveChangesAsync();

        return new ShareTradeResult
        {
            CompanyId = targetCompany.Id,
            CompanyName = targetCompany.Name,
            AccountType = account.AccountType,
            AccountCompanyId = account.Company?.Id,
            AccountName = account.AccountName,
            ShareCount = shareCount,
            PricePerShare = askPrice,
            TotalValue = totalValue,
            OwnedShareCount = holding.ShareCount,
            PublicFloatShares = SharePriceCalculator.ComputePublicFloat(targetCompany, shareholdings.Where(item => item.CompanyId == targetCompany.Id)),
            PersonalCash = player.PersonalCash,
            CompanyCash = account.Company?.Cash,
        };
    }

    /// <summary>Sells shares back to the public exchange using either the personal account or the selected company account.</summary>
    [Authorize]
    public async Task<ShareTradeResult> SellShares(
        SellSharesInput input,
        [Service] AppDbContext db,
        [Service] IHttpContextAccessor httpContextAccessor)
    {
        if (input.ShareCount <= 0m)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Share count must be greater than zero.")
                    .SetCode("INVALID_SHARE_COUNT")
                    .Build());
        }

        var userId = httpContextAccessor.HttpContext!.User.GetRequiredUserId();
        var player = await db.Players.FirstOrDefaultAsync(candidate => candidate.Id == userId)
            ?? throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Player not found.")
                    .SetCode("PLAYER_NOT_FOUND")
                    .Build());

    var account = !string.IsNullOrEmpty(input.TradeAccountType)
        ? await ResolveRequestedTradingAccountAsync(db, player, input.TradeAccountType, input.TradeAccountCompanyId)
        : await ResolveActiveTradingAccountAsync(db, player, httpContextAccessor.HttpContext!.User);
        var (companies, shareholdings, sharePrices) = await LoadSharePricingSnapshotAsync(db);
        var targetCompany = companies.FirstOrDefault(company => company.Id == input.CompanyId)
            ?? throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Company not found.")
                    .SetCode("COMPANY_NOT_FOUND")
                    .Build());

        var shareCount = decimal.Round(input.ShareCount, 4, MidpointRounding.AwayFromZero);
        var holding = shareholdings.FirstOrDefault(candidate =>
            candidate.CompanyId == targetCompany.Id
            && candidate.OwnerPlayerId == (account.Company is null ? player.Id : null)
            && candidate.OwnerCompanyId == account.Company?.Id);

        if (holding is null || holding.ShareCount < shareCount)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("You do not hold enough shares to complete this sale.")
                    .SetCode("INSUFFICIENT_SHARES")
                    .Build());
        }

        var sharePrice = sharePrices.GetValueOrDefault(targetCompany.Id);
        var bidPrice = SharePriceCalculator.ComputeBidPrice(sharePrice);
        var totalValue = decimal.Round(bidPrice * shareCount, 4, MidpointRounding.AwayFromZero);

        holding.ShareCount = decimal.Round(holding.ShareCount - shareCount, 4, MidpointRounding.AwayFromZero);
        if (holding.ShareCount <= 0m)
        {
            db.Shareholdings.Remove(holding);
            shareholdings.Remove(holding);
        }

        if (account.Company is null)
        {
            player.PersonalCash += totalValue;
        }
        else
        {
            account.Company.Cash += totalValue;
        }

        await db.SaveChangesAsync();

        return new ShareTradeResult
        {
            CompanyId = targetCompany.Id,
            CompanyName = targetCompany.Name,
            AccountType = account.AccountType,
            AccountCompanyId = account.Company?.Id,
            AccountName = account.AccountName,
            ShareCount = shareCount,
            PricePerShare = bidPrice,
            TotalValue = totalValue,
            OwnedShareCount = holding.ShareCount > 0m ? holding.ShareCount : 0m,
            PublicFloatShares = SharePriceCalculator.ComputePublicFloat(targetCompany, shareholdings.Where(item => item.CompanyId == targetCompany.Id)),
            PersonalCash = player.PersonalCash,
            CompanyCash = account.Company?.Cash,
        };
    }

    private static StarterIpoSelection ResolveStarterIpoSelection(decimal? raiseTarget)
    {
        var normalizedRaiseTarget = raiseTarget ?? 400_000m;
        return normalizedRaiseTarget switch
        {
            400_000m => new StarterIpoSelection(400_000m, 0.5m),
            600_000m => new StarterIpoSelection(600_000m, 0.3333m),
            800_000m => new StarterIpoSelection(800_000m, 0.25m),
            _ => throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Supported IPO raise targets are 400000, 600000, or 800000.")
                    .SetCode("INVALID_IPO_RAISE_TARGET")
                    .Build())
        };
    }

    private static async Task<(List<Company> Companies, List<Shareholding> Shareholdings, Dictionary<Guid, decimal> SharePrices)> LoadSharePricingSnapshotAsync(AppDbContext db)
    {
        var companies = await db.Companies.ToListAsync();
        var buildings = await db.Buildings.ToListAsync();
        var lots = await db.BuildingLots.Where(lot => lot.OwnerCompanyId.HasValue).ToListAsync();
        var inventories = await db.Inventories
            .Include(inventory => inventory.ResourceType)
            .Include(inventory => inventory.ProductType)
            .ToListAsync();
        var shareholdings = await db.Shareholdings.ToListAsync();

        var baseEquityByCompany = SharePriceCalculator.ComputeBaseEquityByCompany(companies, buildings, lots, inventories);
        var sharePrices = SharePriceCalculator.ComputeQuotedSharePriceByCompany(companies, baseEquityByCompany, shareholdings);
        return (companies, shareholdings, sharePrices);
    }

    private static async Task<ActiveTradingAccount> ResolveRequestedTradingAccountAsync(
        AppDbContext db,
        Player player,
        string accountType,
        Guid? companyId)
    {
        if (string.Equals(accountType, AccountContextType.Company, StringComparison.Ordinal) && companyId.HasValue)
        {
            var company = await db.Companies.FirstOrDefaultAsync(candidate =>
                candidate.Id == companyId.Value && candidate.PlayerId == player.Id);
            if (company is not null)
            {
                return new ActiveTradingAccount(AccountContextType.Company, company, company.Name);
            }
        }

        return new ActiveTradingAccount(AccountContextType.Person, null, player.DisplayName);
    }

    private static async Task<ActiveTradingAccount> ResolveActiveTradingAccountAsync(AppDbContext db, Player player, ClaimsPrincipal principal)
    {
        var effectiveAccountType = principal.GetEffectiveAccountType() ?? player.ActiveAccountType;
        var effectiveCompanyId = principal.GetEffectiveCompanyId() ?? player.ActiveCompanyId;

        if (string.Equals(effectiveAccountType, AccountContextType.Company, StringComparison.Ordinal)
            && effectiveCompanyId.HasValue)
        {
            var company = await db.Companies.FirstOrDefaultAsync(candidate =>
                candidate.Id == effectiveCompanyId.Value && candidate.PlayerId == player.Id);
            if (company is not null)
            {
                return new ActiveTradingAccount(AccountContextType.Company, company, company.Name);
            }
        }

        if (!principal.IsImpersonating())
        {
            player.ActiveAccountType = AccountContextType.Person;
            player.ActiveCompanyId = null;
        }

        return new ActiveTradingAccount(AccountContextType.Person, null, player.DisplayName);
    }

    private static ImpersonationAccountContext ResolveImpersonationAccountContext(
        Player targetPlayer,
        StartAdminImpersonationInput input)
    {
        if (string.Equals(input.AccountType, AccountContextType.Person, StringComparison.OrdinalIgnoreCase))
        {
            return new ImpersonationAccountContext(AccountContextType.Person, null, null);
        }

        if (!string.Equals(input.AccountType, AccountContextType.Company, StringComparison.OrdinalIgnoreCase) || input.CompanyId is null)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("A valid person or company account must be selected for impersonation.")
                    .SetCode("INVALID_IMPERSONATION_ACCOUNT")
                    .Build());
        }

        var targetCompany = targetPlayer.Companies.FirstOrDefault(company => company.Id == input.CompanyId.Value)
            ?? throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("The selected company does not belong to the target player.")
                    .SetCode("IMPERSONATION_COMPANY_NOT_FOUND")
                    .Build());

        return new ImpersonationAccountContext(AccountContextType.Company, targetCompany.Id, targetCompany.Name);
    }

    private static Shareholding GetOrCreateShareholding(
        AppDbContext db,
        List<Shareholding> shareholdings,
        Guid companyId,
        Guid? ownerPlayerId,
        Guid? ownerCompanyId)
    {
        var existing = shareholdings.FirstOrDefault(holding =>
            holding.CompanyId == companyId
            && holding.OwnerPlayerId == ownerPlayerId
            && holding.OwnerCompanyId == ownerCompanyId);

        if (existing is not null)
        {
            return existing;
        }

        var holding = new Shareholding
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            OwnerPlayerId = ownerPlayerId,
            OwnerCompanyId = ownerCompanyId,
            ShareCount = 0m,
        };
        db.Shareholdings.Add(holding);
        shareholdings.Add(holding);
        return holding;
    }

    private static decimal ComputeControlledOwnershipRatio(
        Guid playerId,
        Company targetCompany,
        IEnumerable<Company> companies,
        IEnumerable<Shareholding> shareholdings)
    {
        if (targetCompany.TotalSharesIssued <= 0m)
        {
            return 0m;
        }

        var controlledCompanyIds = companies
            .Where(company => company.PlayerId == playerId)
            .Select(company => company.Id)
            .ToHashSet();

        var controlledShares = shareholdings
            .Where(holding => holding.CompanyId == targetCompany.Id
                && (holding.OwnerPlayerId == playerId
                    || (holding.OwnerCompanyId.HasValue && controlledCompanyIds.Contains(holding.OwnerCompanyId.Value))))
            .Sum(holding => holding.ShareCount);

        return decimal.Round(controlledShares / targetCompany.TotalSharesIssued, 4, MidpointRounding.AwayFromZero);
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

    /// <summary>
    /// Returns a human-readable display name for a building type constant.
    /// Used when auto-generating building names.
    /// </summary>
    private static string BuildingTypeDisplayName(string buildingType) => buildingType switch
    {
        BuildingType.Mine => "Mine",
        BuildingType.Factory => "Factory",
        BuildingType.SalesShop => "Sales Shop",
        BuildingType.ResearchDevelopment => "R&D Lab",
        BuildingType.Apartment => "Apartment",
        BuildingType.Commercial => "Office",
        BuildingType.MediaHouse => "Media House",
        BuildingType.Bank => "Bank",
        BuildingType.Exchange => "Exchange",
        BuildingType.PowerPlant => "Power Plant",
        _ => "Building"
    };

    private sealed record StarterIpoSelection(decimal RaiseTarget, decimal FounderOwnershipRatio)
    {
        public decimal FounderShareCount => decimal.Round(DefaultCompanyShareCount * FounderOwnershipRatio, 4, MidpointRounding.AwayFromZero);
    }

    private sealed record ActiveTradingAccount(string AccountType, Company? Company, string AccountName);

    private static void AddStarterFactoryShell(AppDbContext db, Guid buildingId)
    {
        db.BuildingUnits.AddRange(
            new BuildingUnit { Id = Guid.NewGuid(), BuildingId = buildingId, UnitType = UnitType.Purchase, GridX = 0, GridY = 0, Level = 1, LinkRight = true, PurchaseSource = "OPTIMAL" },
            new BuildingUnit { Id = Guid.NewGuid(), BuildingId = buildingId, UnitType = UnitType.Manufacturing, GridX = 1, GridY = 0, Level = 1, LinkRight = true },
            new BuildingUnit { Id = Guid.NewGuid(), BuildingId = buildingId, UnitType = UnitType.Storage, GridX = 2, GridY = 0, Level = 1, LinkRight = true },
            new BuildingUnit { Id = Guid.NewGuid(), BuildingId = buildingId, UnitType = UnitType.B2BSales, GridX = 3, GridY = 0, Level = 1, SaleVisibility = "COMPANY" }
        );
    }

    private static void ConfigureStarterFactory(AppDbContext db, Building factory, ProductType product, Guid starterResourceId)
    {
        db.BuildingUnits.RemoveRange(factory.Units);
        db.BuildingUnits.AddRange(
            // MaxPrice is intentionally left null so the starter factory can always purchase
            // raw materials from the global exchange regardless of the product's base price.
            // Example: Bread (base price 3 coins) requires Grain whose exchange price is ~6 coins
            // in Bratislava — capping MaxPrice to product.BasePrice would permanently block the
            // purchase unit from buying any input material for that industry.
            new BuildingUnit { Id = Guid.NewGuid(), BuildingId = factory.Id, UnitType = UnitType.Purchase, GridX = 0, GridY = 0, Level = 1, LinkRight = true, ResourceTypeId = starterResourceId, PurchaseSource = "OPTIMAL" },
            new BuildingUnit { Id = Guid.NewGuid(), BuildingId = factory.Id, UnitType = UnitType.Manufacturing, GridX = 1, GridY = 0, Level = 1, LinkRight = true, ProductTypeId = product.Id },
            new BuildingUnit { Id = Guid.NewGuid(), BuildingId = factory.Id, UnitType = UnitType.Storage, GridX = 2, GridY = 0, Level = 1, LinkRight = true },
            new BuildingUnit { Id = Guid.NewGuid(), BuildingId = factory.Id, UnitType = UnitType.B2BSales, GridX = 3, GridY = 0, Level = 1, ProductTypeId = product.Id, MinPrice = product.BasePrice, SaleVisibility = "COMPANY" }
        );
    }

    private static void AddStarterShop(AppDbContext db, Guid companyId, Guid buildingId, ProductType product)
    {
        db.BuildingUnits.AddRange(
            new BuildingUnit { Id = Guid.NewGuid(), BuildingId = buildingId, UnitType = UnitType.Purchase, GridX = 0, GridY = 0, Level = 1, LinkRight = true, ProductTypeId = product.Id, PurchaseSource = "LOCAL", MaxPrice = product.BasePrice * 1.1m, VendorLockCompanyId = companyId },
            new BuildingUnit { Id = Guid.NewGuid(), BuildingId = buildingId, UnitType = UnitType.PublicSales, GridX = 1, GridY = 0, Level = 1, ProductTypeId = product.Id, MinPrice = product.BasePrice * 1.5m }
        );
    }

    private static async Task<(BuildingLot Lot, Building Building)> PrepareLotPurchaseAsync(
        AppDbContext db,
        Company company,
        Guid lotId,
        string buildingType,
        string? buildingName,
        decimal powerConsumption,
        DateTime builtAtUtc,
        Guid? expectedCityId = null,
        string? powerPlantType = null,
        bool applyConstructionDelay = false)
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

        var currentTick = (await db.GameStates.AsNoTracking().FirstOrDefaultAsync())?.CurrentTick ?? 0;
        var constructionCost = applyConstructionDelay ? Engine.GameConstants.ConstructionCost(buildingType) : 0m;
        var totalCost = lot.Price + constructionCost;

        if (company.Cash < totalCost)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage($"Insufficient funds. This lot costs ${lot.Price.ToString("N0", CultureInfo.InvariantCulture)} and construction costs ${constructionCost.ToString("N0", CultureInfo.InvariantCulture)}, total ${totalCost.ToString("N0", CultureInfo.InvariantCulture)}, but you only have ${company.Cash.ToString("N0", CultureInfo.InvariantCulture)}.")
                    .SetCode("INSUFFICIENT_FUNDS")
                    .Build());
        }

        company.Cash -= totalCost;

        var constructionTicks = applyConstructionDelay ? Engine.GameConstants.ConstructionTicks(buildingType) : 0;

        // Auto-generate a natural building name when not provided.
        if (string.IsNullOrWhiteSpace(buildingName))
        {
            var existingCount = await db.Buildings
                .CountAsync(b => b.CompanyId == company.Id && b.Type == buildingType);
            var typeLabel = BuildingTypeDisplayName(buildingType);
            buildingName = $"{typeLabel} #{existingCount + 1}";
        }

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
            PowerPlantType = buildingType == BuildingType.PowerPlant ? (powerPlantType ?? Data.Entities.PowerPlantType.Coal) : null,
            PowerOutput = buildingType == BuildingType.PowerPlant
                ? Engine.GameConstants.DefaultPowerOutputMw(powerPlantType ?? Data.Entities.PowerPlantType.Coal)
                : null,
            BuiltAtUtc = builtAtUtc,
            IsUnderConstruction = applyConstructionDelay,
            ConstructionCompletesAtTick = applyConstructionDelay ? currentTick + constructionTicks : null,
            ConstructionCost = constructionCost,
        };

        db.Buildings.Add(building);
        lot.OwnerCompanyId = company.Id;
        lot.BuildingId = building.Id;
        lot.ConcurrencyToken = Guid.NewGuid();

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

        if (applyConstructionDelay && constructionCost > 0m)
        {
            db.LedgerEntries.Add(new LedgerEntry
            {
                Id = Guid.NewGuid(),
                CompanyId = company.Id,
                BuildingId = building.Id,
                Category = LedgerCategory.ConstructionCost,
                Description = $"Construction order: {buildingName} ({buildingType})",
                Amount = -constructionCost,
                RecordedAtTick = currentTick,
                RecordedAtUtc = builtAtUtc,
            });
        }

        return (lot, building);
    }

    private static async Task<Guid> FindCompatibleAvailableLotIdAsync(
        AppDbContext db,
        Guid cityId,
        string buildingType)
    {
        var lots = await db.BuildingLots
            .Where(lot => lot.CityId == cityId && lot.OwnerCompanyId == null)
            .ToListAsync();

        var lotId = lots
            .OrderBy(lot => lot.Price)
            .FirstOrDefault(lot => lot.SuitableTypes
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Contains(buildingType, StringComparer.OrdinalIgnoreCase))?
            .Id;

        if (lotId is not Guid matchingLotId || matchingLotId == Guid.Empty)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("No suitable land is currently available for this building type in the selected city.")
                    .SetCode("NO_SUITABLE_LOT_AVAILABLE")
                    .Build());
        }

        return matchingLotId;
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

    /// <summary>
    /// Validates that products assigned to STORAGE and B2B_SALES units are topologically
    /// reachable within the submitted configuration plan.
    ///
    /// Rules:
    /// <list type="bullet">
    ///   <item>STORAGE: productTypeId must match a MANUFACTURING unit in the submitted plan,
    ///   or be currently present in the building's inventory stock.</item>
    ///   <item>B2B_SALES: productTypeId must match a MANUFACTURING or STORAGE unit in the
    ///   submitted plan.</item>
    /// </list>
    /// </summary>
    private static async Task ValidateProductTopologyAsync(
        AppDbContext db,
        Guid buildingId,
        IReadOnlyCollection<BuildingConfigurationUnitInput> submittedUnits)
    {
        // Gather product IDs configured on MANUFACTURING units in this plan.
        var mfgProductIds = submittedUnits
            .Where(u => u.UnitType == "MANUFACTURING" && u.ProductTypeId.HasValue)
            .Select(u => u.ProductTypeId!.Value)
            .ToHashSet();

        // Gather product IDs configured on STORAGE units in this plan.
        var storageProductIds = submittedUnits
            .Where(u => u.UnitType == "STORAGE" && u.ProductTypeId.HasValue)
            .Select(u => u.ProductTypeId!.Value)
            .ToHashSet();

        // Validate STORAGE units.
        var storageUnitsWithProduct = submittedUnits
            .Where(u => u.UnitType == "STORAGE" && u.ProductTypeId.HasValue)
            .ToList();

        if (storageUnitsWithProduct.Count > 0)
        {
            // Allowed products for STORAGE = MFG products in plan + current inventory.
            var inventoryProductIds = await db.Inventories
                .Where(i => i.BuildingId == buildingId && i.ProductTypeId.HasValue && i.Quantity > 0)
                .Select(i => i.ProductTypeId!.Value)
                .Distinct()
                .ToHashSetAsync();

            foreach (var unit in storageUnitsWithProduct)
            {
                var pid = unit.ProductTypeId!.Value;
                if (!mfgProductIds.Contains(pid) && !inventoryProductIds.Contains(pid))
                {
                    throw new GraphQLException(
                        ErrorBuilder.New()
                            .SetMessage(
                                "A STORAGE unit's product must match a MANUFACTURING unit in this configuration or be present in the building's current inventory.")
                            .SetCode("STORAGE_PRODUCT_NOT_REACHABLE")
                            .Build());
                }
            }
        }

        // Validate B2B_SALES units.
        var b2bUnitsWithProduct = submittedUnits
            .Where(u => u.UnitType == "B2B_SALES" && u.ProductTypeId.HasValue)
            .ToList();

        if (b2bUnitsWithProduct.Count > 0)
        {
            // Allowed products for B2B_SALES = MFG products + STORAGE products in plan.
            var allowedForB2B = mfgProductIds.Union(storageProductIds).ToHashSet();

            foreach (var unit in b2bUnitsWithProduct)
            {
                var pid = unit.ProductTypeId!.Value;
                if (!allowedForB2B.Contains(pid))
                {
                    throw new GraphQLException(
                        ErrorBuilder.New()
                            .SetMessage(
                                "A B2B_SALES unit's product must match a MANUFACTURING or STORAGE unit in this configuration.")
                            .SetCode("B2B_PRODUCT_NOT_REACHABLE")
                            .Build());
                }
            }
        }
    }

    /// <summary>
    /// Validates that any MediaHouseBuildingId on MARKETING units references an actual
    /// MEDIA_HOUSE building in the same city as the shop being configured.
    /// </summary>
    private static async Task ValidateMediaHouseReferencesAsync(
        AppDbContext db,
        Building building,
        IReadOnlyCollection<BuildingConfigurationUnitInput> submittedUnits)
    {
        var mediaHouseIds = submittedUnits
            .Where(u => u.UnitType == UnitType.Marketing && u.MediaHouseBuildingId.HasValue)
            .Select(u => u.MediaHouseBuildingId!.Value)
            .Distinct()
            .ToList();

        if (mediaHouseIds.Count == 0) return;

        foreach (var mediaHouseId in mediaHouseIds)
        {
            var mediaHouse = await db.Buildings
                .AsNoTracking()
                .FirstOrDefaultAsync(b => b.Id == mediaHouseId);

            if (mediaHouse is null)
            {
                throw new GraphQLException(
                    ErrorBuilder.New()
                        .SetMessage($"Media house building {mediaHouseId} not found.")
                        .SetCode("MEDIA_HOUSE_NOT_FOUND")
                        .Build());
            }

            if (mediaHouse.Type != BuildingType.MediaHouse)
            {
                throw new GraphQLException(
                    ErrorBuilder.New()
                        .SetMessage($"Building {mediaHouseId} is not a media house.")
                        .SetCode("BUILDING_NOT_MEDIA_HOUSE")
                        .Build());
            }

            if (mediaHouse.CityId != building.CityId)
            {
                throw new GraphQLException(
                    ErrorBuilder.New()
                        .SetMessage("The selected media house must be in the same city as the marketing building.")
                        .SetCode("MEDIA_HOUSE_WRONG_CITY")
                        .Build());
            }
        }
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
            .AsSplitQuery()
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
        await ValidateMediaHouseReferencesAsync(db, building, input.Units);
        await ValidateProductTopologyAsync(db, building.Id, input.Units);

        var plan = await BuildingConfigurationService.StoreConfigurationAsync(db, building, input.Units, gameState.CurrentTick);
        await db.SaveChangesAsync();

        return await db.BuildingConfigurationPlans
            .Include(candidate => candidate.Units)
            .Include(candidate => candidate.Removals)
            .FirstAsync(candidate => candidate.Id == plan.Id);
    }

    /// <summary>Cancels a queued building configuration plan, reverting in-progress unit additions using roadmap-aligned rollback timing (10% of the original wait).</summary>
    [Authorize]
    public async Task<BuildingConfigurationPlan> CancelBuildingConfiguration(
        CancelBuildingConfigurationInput input,
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
            .AsSplitQuery()
            .FirstOrDefaultAsync(candidate => candidate.Id == input.BuildingId);

        if (building is null || building.Company.PlayerId != userId)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Building not found or you don't own it.")
                    .SetCode("BUILDING_NOT_FOUND")
                    .Build());
        }

        if (building.PendingConfiguration is null)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("This building does not have a pending configuration plan to cancel.")
                    .SetCode("NO_PENDING_CONFIGURATION")
                    .Build());
        }

        // Cancel by submitting the current active layout, which causes the service to schedule
        // rollback of any in-progress unit additions with 10% of the original wait time.
        var activeUnitInputs = building.Units
            .Select(unit => new BuildingConfigurationUnitInput
            {
                UnitType = unit.UnitType,
                GridX = unit.GridX,
                GridY = unit.GridY,
                LinkUp = unit.LinkUp,
                LinkDown = unit.LinkDown,
                LinkLeft = unit.LinkLeft,
                LinkRight = unit.LinkRight,
                LinkUpLeft = unit.LinkUpLeft,
                LinkUpRight = unit.LinkUpRight,
                LinkDownLeft = unit.LinkDownLeft,
                LinkDownRight = unit.LinkDownRight,
                ResourceTypeId = unit.ResourceTypeId,
                ProductTypeId = unit.ProductTypeId,
                MinPrice = unit.MinPrice,
                MaxPrice = unit.MaxPrice,
                PurchaseSource = unit.PurchaseSource,
                SaleVisibility = unit.SaleVisibility,
                Budget = unit.Budget,
                MediaHouseBuildingId = unit.MediaHouseBuildingId,
                MinQuality = unit.MinQuality,
                BrandScope = unit.BrandScope,
                VendorLockCompanyId = unit.VendorLockCompanyId,
                LockedCityId = unit.LockedCityId,
            })
            .ToList();

        var plan = await BuildingConfigurationService.StoreConfigurationAsync(db, building, activeUnitInputs, gameState.CurrentTick);
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

    /// <summary>
    /// Schedules a new rent per m² for an apartment or commercial building.
    /// The change is stored as pending and activates after one in-game day (24 ticks).
    /// </summary>
    [Authorize]
    public async Task<Building> SetRentPerSqm(
        SetRentPerSqmInput input,
        [Service] AppDbContext db,
        [Service] IHttpContextAccessor httpContextAccessor)
    {
        var userId = httpContextAccessor.HttpContext!.User.GetRequiredUserId();

        var building = await db.Buildings
            .Include(b => b.Company)
            .FirstOrDefaultAsync(b => b.Id == input.BuildingId);

        if (building is null || building.Company.PlayerId != userId)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Building not found or you don't own it.")
                    .SetCode("BUILDING_NOT_FOUND")
                    .Build());
        }

        if (building.Type != BuildingType.Apartment && building.Type != BuildingType.Commercial)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Only apartment and commercial buildings support rent pricing.")
                    .SetCode("INVALID_BUILDING_TYPE")
                    .Build());
        }

        if (input.RentPerSqm < 0m)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Rent per m² must be a non-negative value.")
                    .SetCode("INVALID_RENT")
                    .Build());
        }

        var gameState = await db.GameStates.FirstOrDefaultAsync()
            ?? throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Game state not found.")
                    .SetCode("GAME_STATE_NOT_FOUND")
                    .Build());

        // Schedule the rent change – takes effect after one in-game day (24 ticks).
        building.PendingPricePerSqm = input.RentPerSqm;
        building.PendingPriceActivationTick = gameState.CurrentTick + Engine.GameConstants.TicksPerDay;

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
            Engine.GameConstants.PowerDemandMw(input.BuildingType, 1),
            DateTime.UtcNow,
            powerPlantType: input.PowerPlantType,
            applyConstructionDelay: true);

        // Validate and apply media house channel type.
        if (input.BuildingType == BuildingType.MediaHouse)
        {
            if (string.IsNullOrEmpty(input.MediaType) || !Data.Entities.MediaType.All.Contains(input.MediaType))
            {
                throw new GraphQLException(
                    ErrorBuilder.New()
                        .SetMessage($"A valid mediaType (NEWSPAPER, RADIO, TV) is required for media house buildings. Received: '{input.MediaType}'.")
                        .SetCode("INVALID_MEDIA_TYPE")
                        .Build());
            }
            building.MediaType = input.MediaType;
        }

        try
        {
            var currentTick = await db.GameStates
                .AsNoTracking()
                .Select(state => state.CurrentTick)
                .FirstOrDefaultAsync();
            await LandService.EnsureMinimumAvailableLotsAsync(db, currentTick, [lot.CityId]);
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

        // Check backend-authoritative condition: a real public sale must have occurred in the simulation
        var hasRealSale = await db.PublicSalesRecords
            .AnyAsync(r => r.BuildingId == shopBuilding.Id && r.QuantitySold > 0m);

        if (!hasRealSale)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Your shop has not made its first real sale yet. Wait for the simulation to process the next tick and try again after your shop has sold at least one item.")
                    .SetCode("FIRST_SALE_NOT_RECORDED")
                    .Build());
        }

        player.OnboardingFirstSaleCompletedAtUtc = DateTime.UtcNow;
        player.OnboardingShopBuildingId = null;
        await db.SaveChangesAsync();

        return player;
    }

    // ── Bank Lending Marketplace ──────────────────────────────────────────────────

    /// <summary>
    /// Publishes a new loan offer from a bank building.
    /// Only the owning company can publish offers; rate must be 0.1–200%, duration 24–87600 ticks.
    /// </summary>
    [Authorize]
    public async Task<LoanOffer> PublishLoanOffer(
        PublishLoanOfferInput input,
        [Service] AppDbContext db,
        [Service] IHttpContextAccessor httpContextAccessor)
    {
        var userId = httpContextAccessor.HttpContext!.User.GetRequiredUserId();

        // Validate the bank building is owned by this player's company.
        var bankBuilding = await db.Buildings
            .Include(b => b.Company)
            .FirstOrDefaultAsync(b => b.Id == input.BankBuildingId && b.Type == BuildingType.Bank);

        if (bankBuilding is null)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Bank building not found.")
                    .SetCode("BANK_NOT_FOUND")
                    .Build());
        }

        if (bankBuilding.Company.PlayerId != userId)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("You do not own this bank building.")
                    .SetCode("BANK_NOT_FOUND")
                    .Build());
        }

        // Validate input.
        if (input.AnnualInterestRatePercent < 0.1m || input.AnnualInterestRatePercent > 200m)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Interest rate must be between 0.1% and 200%.")
                    .SetCode("INVALID_INTEREST_RATE")
                    .Build());
        }

        if (input.MaxPrincipalPerLoan < 1_000m)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Maximum principal per loan must be at least $1,000.")
                    .SetCode("INVALID_PRINCIPAL")
                    .Build());
        }

        if (input.TotalCapacity < input.MaxPrincipalPerLoan)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Total capacity must be at least as large as the maximum principal per loan.")
                    .SetCode("INVALID_CAPACITY")
                    .Build());
        }

        if (input.DurationTicks < 24 || input.DurationTicks > 87_600)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Loan duration must be between 24 ticks (1 in-game day) and 87,600 ticks (10 in-game years).")
                    .SetCode("INVALID_DURATION")
                    .Build());
        }

        // Ensure the lender company has enough cash to cover the full capacity.
        if (bankBuilding.Company.Cash < input.TotalCapacity)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage($"Insufficient funds. Your company needs at least {input.TotalCapacity:C0} to publish this offer.")
                    .SetCode("INSUFFICIENT_FUNDS")
                    .Build());
        }

        var currentTick = await db.GameStates.AsNoTracking().Select(gs => gs.CurrentTick).FirstOrDefaultAsync();

        var offer = new LoanOffer
        {
            Id = Guid.NewGuid(),
            BankBuildingId = bankBuilding.Id,
            LenderCompanyId = bankBuilding.CompanyId,
            AnnualInterestRatePercent = input.AnnualInterestRatePercent,
            MaxPrincipalPerLoan = input.MaxPrincipalPerLoan,
            TotalCapacity = input.TotalCapacity,
            UsedCapacity = 0m,
            DurationTicks = input.DurationTicks,
            IsActive = true,
            CreatedAtTick = currentTick,
            CreatedAtUtc = DateTime.UtcNow
        };

        db.LoanOffers.Add(offer);
        await db.SaveChangesAsync();

        return offer;
    }

    /// <summary>
    /// Updates an existing loan offer. Only the bank's owning player can update it.
    /// Ongoing loans are not affected; changes apply to new loans only.
    /// </summary>
    [Authorize]
    public async Task<LoanOffer> UpdateLoanOffer(
        UpdateLoanOfferInput input,
        [Service] AppDbContext db,
        [Service] IHttpContextAccessor httpContextAccessor)
    {
        var userId = httpContextAccessor.HttpContext!.User.GetRequiredUserId();

        var offer = await db.LoanOffers
            .Include(o => o.LenderCompany)
            .FirstOrDefaultAsync(o => o.Id == input.LoanOfferId);

        if (offer is null || offer.LenderCompany.PlayerId != userId)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Loan offer not found or you do not own it.")
                    .SetCode("OFFER_NOT_FOUND")
                    .Build());
        }

        if (input.AnnualInterestRatePercent.HasValue)
        {
            if (input.AnnualInterestRatePercent.Value < 0.1m || input.AnnualInterestRatePercent.Value > 200m)
            {
                throw new GraphQLException(
                    ErrorBuilder.New()
                        .SetMessage("Interest rate must be between 0.1% and 200%.")
                        .SetCode("INVALID_INTEREST_RATE")
                        .Build());
            }
            offer.AnnualInterestRatePercent = input.AnnualInterestRatePercent.Value;
        }

        if (input.MaxPrincipalPerLoan.HasValue)
        {
            if (input.MaxPrincipalPerLoan.Value < 1_000m)
            {
                throw new GraphQLException(
                    ErrorBuilder.New()
                        .SetMessage("Maximum principal per loan must be at least $1,000.")
                        .SetCode("INVALID_PRINCIPAL")
                        .Build());
            }
            offer.MaxPrincipalPerLoan = input.MaxPrincipalPerLoan.Value;
        }

        if (input.TotalCapacity.HasValue)
        {
            var newCapacity = input.TotalCapacity.Value;
            if (newCapacity < offer.UsedCapacity)
            {
                throw new GraphQLException(
                    ErrorBuilder.New()
                        .SetMessage($"Total capacity cannot be reduced below currently used capacity ({offer.UsedCapacity:C0}).")
                        .SetCode("INVALID_CAPACITY")
                        .Build());
            }
            offer.TotalCapacity = newCapacity;
        }

        if (input.DurationTicks.HasValue)
        {
            if (input.DurationTicks.Value < 24 || input.DurationTicks.Value > 87_600)
            {
                throw new GraphQLException(
                    ErrorBuilder.New()
                        .SetMessage("Loan duration must be between 24 ticks and 87,600 ticks.")
                        .SetCode("INVALID_DURATION")
                        .Build());
            }
            offer.DurationTicks = input.DurationTicks.Value;
        }

        if (input.IsActive.HasValue)
        {
            offer.IsActive = input.IsActive.Value;
        }

        await db.SaveChangesAsync();
        return offer;
    }

    /// <summary>
    /// Deactivates a loan offer so it no longer appears to borrowers.
    /// Existing loans are not affected.
    /// </summary>
    [Authorize]
    public async Task<LoanOffer> DeactivateLoanOffer(
        Guid loanOfferId,
        [Service] AppDbContext db,
        [Service] IHttpContextAccessor httpContextAccessor)
    {
        var userId = httpContextAccessor.HttpContext!.User.GetRequiredUserId();

        var offer = await db.LoanOffers
            .Include(o => o.LenderCompany)
            .FirstOrDefaultAsync(o => o.Id == loanOfferId);

        if (offer is null || offer.LenderCompany.PlayerId != userId)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Loan offer not found or you do not own it.")
                    .SetCode("OFFER_NOT_FOUND")
                    .Build());
        }

        offer.IsActive = false;
        await db.SaveChangesAsync();
        return offer;
    }

    /// <summary>
    /// Accepts a loan offer: creates a Loan record, transfers cash from lender to borrower,
    /// and records ledger entries for both parties.
    /// Guards: cannot borrow from own company; principal must not exceed remaining capacity; lender must have cash.
    /// </summary>
    [Authorize]
    public async Task<Loan> AcceptLoan(
        AcceptLoanInput input,
        [Service] AppDbContext db,
        [Service] IHttpContextAccessor httpContextAccessor)
    {
        var userId = httpContextAccessor.HttpContext!.User.GetRequiredUserId();

        // Verify borrower owns the company.
        var borrower = await db.Companies
            .FirstOrDefaultAsync(c => c.Id == input.BorrowerCompanyId && c.PlayerId == userId);

        if (borrower is null)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Borrower company not found or you do not own it.")
                    .SetCode("COMPANY_NOT_FOUND")
                    .Build());
        }

        // Load the offer with lender company.
        var offer = await db.LoanOffers
            .Include(o => o.LenderCompany)
            .FirstOrDefaultAsync(o => o.Id == input.LoanOfferId);

        if (offer is null || !offer.IsActive)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Loan offer not found or is no longer active.")
                    .SetCode("OFFER_NOT_FOUND")
                    .Build());
        }

        // Self-lending guard.
        if (offer.LenderCompanyId == input.BorrowerCompanyId)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("A company cannot borrow from itself.")
                    .SetCode("SELF_LENDING_NOT_ALLOWED")
                    .Build());
        }

        // Same player guard.
        if (offer.LenderCompany.PlayerId == userId)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("You cannot borrow from your own bank.")
                    .SetCode("SELF_LENDING_NOT_ALLOWED")
                    .Build());
        }

        if (input.PrincipalAmount < 1_000m)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Minimum loan amount is $1,000.")
                    .SetCode("INVALID_PRINCIPAL")
                    .Build());
        }

        if (input.PrincipalAmount > offer.MaxPrincipalPerLoan)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage($"Requested principal exceeds the maximum per-loan limit of {offer.MaxPrincipalPerLoan:C0}.")
                    .SetCode("EXCEEDS_MAX_PRINCIPAL")
                    .Build());
        }

        var remainingCapacity = offer.TotalCapacity - offer.UsedCapacity;
        if (input.PrincipalAmount > remainingCapacity)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage($"The offer only has {remainingCapacity:C0} of lending capacity remaining.")
                    .SetCode("INSUFFICIENT_CAPACITY")
                    .Build());
        }

        // Lender must have the cash.
        if (offer.LenderCompany.Cash < input.PrincipalAmount)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("The lender does not have sufficient funds to cover this loan at this time.")
                    .SetCode("LENDER_INSUFFICIENT_FUNDS")
                    .Build());
        }

        var currentTick = await db.GameStates.AsNoTracking().Select(gs => gs.CurrentTick).FirstOrDefaultAsync();

        // Calculate payment schedule: monthly payments (every 30 in-game days = 720 ticks), minimum 1 payment.
        var ticksPerPayment = 720L; // 30 in-game days
        var totalPayments = (int)Math.Max(1, offer.DurationTicks / ticksPerPayment);
        var dueTick = currentTick + offer.DurationTicks;

        // Compute flat equal payment (simple interest, not compound).
        var totalInterest = input.PrincipalAmount * (offer.AnnualInterestRatePercent / 100m)
            * ((decimal)offer.DurationTicks / GameConstants.TicksPerYear);
        var totalRepayment = input.PrincipalAmount + totalInterest;
        var paymentAmount = decimal.Round(totalRepayment / totalPayments, 4, MidpointRounding.AwayFromZero);

        // Transfer cash.
        offer.LenderCompany.Cash -= input.PrincipalAmount;
        borrower.Cash += input.PrincipalAmount;
        offer.UsedCapacity += input.PrincipalAmount;

        var loan = new Loan
        {
            Id = Guid.NewGuid(),
            LoanOfferId = offer.Id,
            BorrowerCompanyId = borrower.Id,
            BankBuildingId = offer.BankBuildingId,
            LenderCompanyId = offer.LenderCompanyId,
            OriginalPrincipal = input.PrincipalAmount,
            RemainingPrincipal = input.PrincipalAmount,
            AnnualInterestRatePercent = offer.AnnualInterestRatePercent,
            DurationTicks = offer.DurationTicks,
            StartTick = currentTick,
            DueTick = dueTick,
            NextPaymentTick = currentTick + ticksPerPayment,
            PaymentAmount = paymentAmount,
            PaymentsMade = 0,
            TotalPayments = totalPayments,
            Status = LoanStatus.Active,
            MissedPayments = 0,
            AccumulatedPenalty = 0m,
            AcceptedAtUtc = DateTime.UtcNow
        };

        db.Loans.Add(loan);

        // Ledger: borrower receives cash (loan origination).
        db.LedgerEntries.Add(new LedgerEntry
        {
            Id = Guid.NewGuid(),
            CompanyId = borrower.Id,
            Category = LedgerCategory.LoanOrigination,
            Description = $"Loan received from {offer.LenderCompany.Name} – {offer.AnnualInterestRatePercent}% p.a. over {offer.DurationTicks} ticks",
            Amount = input.PrincipalAmount,
            RecordedAtTick = currentTick,
            RecordedAtUtc = DateTime.UtcNow
        });

        await db.SaveChangesAsync();

        return loan;
    }

    /// <summary>
    /// Instantly updates the minimum sale price on a PUBLIC_SALES building unit.
    /// Unlike StoreBuildingConfiguration, this takes effect immediately (next tick)
    /// without requiring a queued upgrade, because price is just a runtime parameter.
    /// </summary>
    [Authorize]
    public async Task<BuildingUnit> UpdatePublicSalesPrice(
        UpdatePublicSalesPriceInput input,
        [Service] AppDbContext db,
        [Service] IHttpContextAccessor httpContextAccessor)
    {
        var userId = httpContextAccessor.HttpContext!.User.GetRequiredUserId();

        var unit = await db.BuildingUnits
            .Include(u => u.Building)
            .ThenInclude(b => b.Company)
            .FirstOrDefaultAsync(u => u.Id == input.UnitId);

        if (unit is null || unit.Building.Company.PlayerId != userId)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Unit not found or you don't own it.")
                    .SetCode("UNIT_NOT_FOUND")
                    .Build());
        }

        if (unit.UnitType != UnitType.PublicSales)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Only PUBLIC_SALES units support instant price updates.")
                    .SetCode("INVALID_UNIT_TYPE")
                    .Build());
        }

        if (input.NewMinPrice <= 0m)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Minimum sale price must be greater than zero.")
                    .SetCode("INVALID_PRICE")
                    .Build());
        }

        unit.MinPrice = input.NewMinPrice;
        await db.SaveChangesAsync();

        return unit;
    }

    /// <summary>
    /// Discards all inventory stored in a storage-capable building unit.
    /// A ledger entry with category DISCARDED_RESOURCES is recorded for each
    /// distinct item flushed, so the loss is visible in the company ledger.
    /// Returns a summary of what was discarded.
    /// </summary>
    [Authorize]
    public async Task<FlushStorageResult> FlushStorage(
        FlushStorageInput input,
        [Service] AppDbContext db,
        [Service] IHttpContextAccessor httpContextAccessor)
    {
        var userId = httpContextAccessor.HttpContext!.User.GetRequiredUserId();

        var unit = await db.BuildingUnits
            .Include(u => u.Building)
            .ThenInclude(b => b.Company)
            .FirstOrDefaultAsync(u => u.Id == input.BuildingUnitId);

        if (unit is null || unit.Building.Company.PlayerId != userId)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Unit not found or you don't own it.")
                    .SetCode("UNIT_NOT_FOUND")
                    .Build());
        }

        // Only allow flushing units that can physically hold inventory.
        var flushableTypes = new HashSet<string>
        {
            UnitType.Storage,
            UnitType.Mining,
            UnitType.Manufacturing,
        };

        if (!flushableTypes.Contains(unit.UnitType))
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Only STORAGE, MINING and MANUFACTURING units can be flushed.")
                    .SetCode("INVALID_UNIT_TYPE")
                    .Build());
        }

        var currentTick = await db.GameStates
            .AsNoTracking()
            .Select(state => state.CurrentTick)
            .FirstAsync();

        var inventory = await db.Inventories
            .Where(i => i.BuildingUnitId == unit.Id && i.Quantity > 0m)
            .ToListAsync();

        if (inventory.Count == 0)
        {
            return new FlushStorageResult
            {
                DiscardedItemCount = 0,
                TotalDiscardedValue = 0m,
                DiscardedEntries = [],
            };
        }

        var nowUtc = DateTime.UtcNow;
        var discardedEntries = new List<FlushStorageEntry>();

        // Pre-load resource and product names in a single query each to avoid N+1.
        var resourceTypeIds = inventory.Where(i => i.ResourceTypeId.HasValue).Select(i => i.ResourceTypeId!.Value).ToHashSet();
        var productTypeIds = inventory.Where(i => i.ProductTypeId.HasValue).Select(i => i.ProductTypeId!.Value).ToHashSet();

        var resourceNames = resourceTypeIds.Count > 0
            ? await db.ResourceTypes.AsNoTracking()
                .Where(r => resourceTypeIds.Contains(r.Id))
                .ToDictionaryAsync(r => r.Id, r => r.Name)
            : new Dictionary<Guid, string>();

        var productNames = productTypeIds.Count > 0
            ? await db.ProductTypes.AsNoTracking()
                .Where(p => productTypeIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, p => p.Name)
            : new Dictionary<Guid, string>();

        foreach (var item in inventory)
        {
            var itemName = item.ResourceTypeId.HasValue
                ? (resourceNames.TryGetValue(item.ResourceTypeId.Value, out var rn) ? rn : "Resource")
                : item.ProductTypeId.HasValue
                    ? (productNames.TryGetValue(item.ProductTypeId.Value, out var pn) ? pn : "Product")
                    : "Item";

            db.LedgerEntries.Add(new LedgerEntry
            {
                Id = Guid.NewGuid(),
                CompanyId = unit.Building.CompanyId,
                BuildingId = unit.BuildingId,
                BuildingUnitId = unit.Id,
                Category = LedgerCategory.DiscardedResources,
                Description = $"Flushed {item.Quantity:F2} × {itemName} from storage",
                Amount = -item.SourcingCostTotal,
                RecordedAtTick = currentTick,
                RecordedAtUtc = nowUtc,
                ResourceTypeId = item.ResourceTypeId,
                ProductTypeId = item.ProductTypeId,
            });

            discardedEntries.Add(new FlushStorageEntry
            {
                ItemName = itemName,
                Quantity = item.Quantity,
                SourcingCostLost = item.SourcingCostTotal,
                ResourceTypeId = item.ResourceTypeId,
                ProductTypeId = item.ProductTypeId,
            });
        }

        db.Inventories.RemoveRange(inventory);
        await db.SaveChangesAsync();

        return new FlushStorageResult
        {
            DiscardedItemCount = discardedEntries.Count,
            TotalDiscardedValue = discardedEntries.Sum(e => e.SourcingCostLost),
            DiscardedEntries = discardedEntries,
        };
    }

    /// <summary>
    /// Schedules a level upgrade for a building unit.
    /// Deducts the upgrade cost from the owning company's cash immediately
    /// and creates a queued building configuration plan that applies after the required ticks.
    /// </summary>
    [Authorize]
    public async Task<BuildingConfigurationPlan> ScheduleUnitUpgrade(
        ScheduleUnitUpgradeInput input,
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

        var unit = await db.BuildingUnits
            .Include(u => u.Building)
            .ThenInclude(b => b.Company)
            .Include(u => u.Building)
            .ThenInclude(b => b.Units)
            .Include(u => u.Building)
            .ThenInclude(b => b.PendingConfiguration)
            .ThenInclude(plan => plan!.Units)
            .Include(u => u.Building)
            .ThenInclude(b => b.PendingConfiguration)
            .ThenInclude(plan => plan!.Removals)
            .AsSplitQuery()
            .FirstOrDefaultAsync(u => u.Id == input.UnitId);

        if (unit is null || unit.Building.Company.PlayerId != userId)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Unit not found or you don't own it.")
                    .SetCode("UNIT_NOT_FOUND")
                    .Build());
        }

        if (!Engine.GameConstants.IsUpgradableUnitType(unit.UnitType))
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage($"Unit type {unit.UnitType} does not support level upgrades.")
                    .SetCode("UNIT_NOT_UPGRADABLE")
                    .Build());
        }

        if (unit.Level >= Engine.GameConstants.MaxUnitLevel)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage($"This unit is already at maximum level ({Engine.GameConstants.MaxUnitLevel}).")
                    .SetCode("MAX_LEVEL_REACHED")
                    .Build());
        }

        if (unit.Building.PendingConfiguration is not null)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("This building already has a pending configuration change. Wait for it to complete or cancel it before scheduling an upgrade.")
                    .SetCode("PENDING_CONFIGURATION_EXISTS")
                    .Build());
        }

        var upgradeCost = Engine.GameConstants.UnitUpgradeCost(unit.UnitType, unit.Level);
        var company = unit.Building.Company;

        if (company.Cash < upgradeCost)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage($"Insufficient funds. Upgrade costs ${upgradeCost.ToString("N0", CultureInfo.InvariantCulture)} but your company only has ${company.Cash.ToString("N0", CultureInfo.InvariantCulture)}.")
                    .SetCode("INSUFFICIENT_FUNDS")
                    .Build());
        }

        company.Cash -= upgradeCost;

        var upgradeTicks = Engine.GameConstants.UnitUpgradeTicks(unit.Level);
        var planId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        var plan = new BuildingConfigurationPlan
        {
            Id = planId,
            BuildingId = unit.BuildingId,
            SubmittedAtUtc = now,
            SubmittedAtTick = gameState.CurrentTick,
            AppliesAtTick = gameState.CurrentTick + upgradeTicks,
            TotalTicksRequired = upgradeTicks,
        };

        // Snapshot all active units; only the target unit gets a level bump and a timer.
        // DistinctBy is defensive deduplication per coding guidelines; AsSplitQuery() above prevents
        // Cartesian explosion, but we deduplicate by position as a safety net.
        var allActiveUnits = unit.Building.Units.DistinctBy(u => (u.GridX, u.GridY)).ToList();
        foreach (var activeUnit in allActiveUnits)
        {
            bool isTarget = activeUnit.Id == unit.Id;
            plan.Units.Add(new BuildingConfigurationPlanUnit
            {
                Id = Guid.NewGuid(),
                BuildingConfigurationPlanId = planId,
                UnitType = activeUnit.UnitType,
                GridX = activeUnit.GridX,
                GridY = activeUnit.GridY,
                Level = isTarget ? activeUnit.Level + 1 : activeUnit.Level,
                LinkUp = activeUnit.LinkUp,
                LinkDown = activeUnit.LinkDown,
                LinkLeft = activeUnit.LinkLeft,
                LinkRight = activeUnit.LinkRight,
                LinkUpLeft = activeUnit.LinkUpLeft,
                LinkUpRight = activeUnit.LinkUpRight,
                LinkDownLeft = activeUnit.LinkDownLeft,
                LinkDownRight = activeUnit.LinkDownRight,
                StartedAtTick = gameState.CurrentTick,
                AppliesAtTick = isTarget ? gameState.CurrentTick + upgradeTicks : gameState.CurrentTick,
                TicksRequired = isTarget ? upgradeTicks : 0,
                IsChanged = isTarget,
                ResourceTypeId = activeUnit.ResourceTypeId,
                ProductTypeId = activeUnit.ProductTypeId,
                MinPrice = activeUnit.MinPrice,
                MaxPrice = activeUnit.MaxPrice,
                PurchaseSource = activeUnit.PurchaseSource,
                SaleVisibility = activeUnit.SaleVisibility,
                Budget = activeUnit.Budget,
                MediaHouseBuildingId = activeUnit.MediaHouseBuildingId,
                MinQuality = activeUnit.MinQuality,
                BrandScope = activeUnit.BrandScope,
                VendorLockCompanyId = activeUnit.VendorLockCompanyId,
                LockedCityId = activeUnit.LockedCityId,
            });
        }

        db.BuildingConfigurationPlans.Add(plan);

        db.LedgerEntries.Add(new LedgerEntry
        {
            Id = Guid.NewGuid(),
            CompanyId = company.Id,
            BuildingId = unit.BuildingId,
            BuildingUnitId = unit.Id,
            Category = LedgerCategory.UnitUpgrade,
            Description = $"Unit upgrade: {unit.UnitType} Lv{unit.Level}→{unit.Level + 1} ({unit.Building.Name})",
            Amount = -upgradeCost,
            RecordedAtTick = gameState.CurrentTick,
            RecordedAtUtc = now,
        });

        await db.SaveChangesAsync();

        return await db.BuildingConfigurationPlans
            .Include(p => p.Units)
            .Include(p => p.Removals)
            .FirstAsync(p => p.Id == planId);
    }

    private static AuthenticatedSession GenerateToken(
        Player player,
        JwtOptions options,
        AdminImpersonationTokenContext? impersonation = null)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.SigningKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var expires = DateTime.UtcNow.AddMinutes(options.ExpiresMinutes);

        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, player.Id.ToString()),
            new Claim(ClaimTypes.Email, player.Email),
            new Claim(ClaimTypes.Name, player.DisplayName),
            new Claim(ClaimTypes.Role, player.Role)
        };

        if (impersonation is not null)
        {
            claims.Add(new Claim(ClaimsPrincipalExtensions.EffectivePlayerIdClaimType, impersonation.EffectivePlayer.Id.ToString()));
            claims.Add(new Claim(ClaimsPrincipalExtensions.EffectivePlayerEmailClaimType, impersonation.EffectivePlayer.Email));
            claims.Add(new Claim(ClaimsPrincipalExtensions.EffectivePlayerNameClaimType, impersonation.EffectivePlayer.DisplayName));
            claims.Add(new Claim(ClaimsPrincipalExtensions.EffectiveAccountTypeClaimType, impersonation.EffectiveAccountType));

            if (impersonation.EffectiveCompanyId.HasValue)
            {
                claims.Add(new Claim(ClaimsPrincipalExtensions.EffectiveCompanyIdClaimType, impersonation.EffectiveCompanyId.Value.ToString()));
            }

            if (!string.IsNullOrWhiteSpace(impersonation.EffectiveCompanyName))
            {
                claims.Add(new Claim(ClaimsPrincipalExtensions.EffectiveCompanyNameClaimType, impersonation.EffectiveCompanyName));
            }
        }

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

    private sealed record ImpersonationAccountContext(
        string EffectiveAccountType,
        Guid? EffectiveCompanyId,
        string? EffectiveCompanyName);

    private sealed record AdminImpersonationTokenContext(
        Player EffectivePlayer,
        string EffectiveAccountType,
        Guid? EffectiveCompanyId,
        string? EffectiveCompanyName);
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

/// <summary>Summary of a flush-storage operation.</summary>
public sealed class FlushStorageResult
{
    /// <summary>Number of distinct inventory lines discarded.</summary>
    public int DiscardedItemCount { get; set; }

    /// <summary>Total sourcing-cost value of all discarded items.</summary>
    public decimal TotalDiscardedValue { get; set; }

    /// <summary>Per-item breakdown of what was discarded.</summary>
    public List<FlushStorageEntry> DiscardedEntries { get; set; } = [];
}

/// <summary>A single item line in a flush-storage result.</summary>
public sealed class FlushStorageEntry
{
    public string ItemName { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal SourcingCostLost { get; set; }
    public Guid? ResourceTypeId { get; set; }
    public Guid? ProductTypeId { get; set; }
}
