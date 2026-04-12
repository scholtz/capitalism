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
}
