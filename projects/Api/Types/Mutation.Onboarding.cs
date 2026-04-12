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

public sealed partial class Mutation
{
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
}
