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

    /// <summary>
    /// Merges a target company (where the player holds ≥90% combined ownership) into a destination
    /// company that the player directly controls. All buildings, lots, cash, and owned shareholdings
    /// are transferred to the destination company. Taxes are settled for the target company at the
    /// time of merge. The target company is then permanently closed.
    /// </summary>
    [Authorize]
    public async Task<MergeCompanyResult> MergeCompany(
        MergeCompanyInput input,
        [Service] AppDbContext db,
        [Service] IHttpContextAccessor httpContextAccessor)
    {
        var userId = httpContextAccessor.HttpContext!.User.GetRequiredUserId();

        var companies = await db.Companies.ToListAsync();

        var targetCompany = companies.FirstOrDefault(c => c.Id == input.TargetCompanyId)
            ?? throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Target company not found.")
                    .SetCode("COMPANY_NOT_FOUND")
                    .Build());

        var destinationCompany = companies.FirstOrDefault(c => c.Id == input.DestinationCompanyId)
            ?? throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Destination company not found.")
                    .SetCode("DESTINATION_COMPANY_NOT_FOUND")
                    .Build());

        if (destinationCompany.PlayerId != userId)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("You must directly control the destination company.")
                    .SetCode("DESTINATION_NOT_CONTROLLED")
                    .Build());
        }

        if (targetCompany.Id == destinationCompany.Id)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Target and destination companies must be different.")
                    .SetCode("SAME_COMPANY")
                    .Build());
        }

        var shareholdings = await db.Shareholdings
            .Where(h => h.CompanyId == targetCompany.Id)
            .ToListAsync();

        var combinedRatio = ComputeControlledOwnershipRatio(userId, targetCompany, companies, shareholdings);
        if (combinedRatio < 0.9m)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage($"You need at least 90% combined ownership to merge this company. Current: {combinedRatio:P1}")
                    .SetCode("INSUFFICIENT_OWNERSHIP_FOR_MERGE")
                    .Build());
        }

        var gameState = await db.GameStates.FirstOrDefaultAsync();
        var currentTick = gameState?.CurrentTick ?? 0L;

        // Settle taxes for the target company at merge time
        var taxableEntries = await db.LedgerEntries
            .Where(e => e.CompanyId == targetCompany.Id)
            .ToListAsync();
        var taxableIncome = LedgerCalculator.ComputeTaxableIncome(taxableEntries);
        var taxRate = gameState?.TaxRate ?? 0m;
        if (taxableIncome > 0m && taxRate > 0m)
        {
            var taxAmount = decimal.Round(taxableIncome * taxRate, 2, MidpointRounding.AwayFromZero);
            targetCompany.Cash -= taxAmount;
            db.LedgerEntries.Add(new LedgerEntry
            {
                Id = Guid.NewGuid(),
                CompanyId = targetCompany.Id,
                Category = LedgerCategory.Tax,
                Description = $"Merger settlement tax",
                Amount = -taxAmount,
                RecordedAtTick = currentTick,
                RecordedAtUtc = DateTime.UtcNow,
            });
        }

        // Transfer buildings
        var buildings = await db.Buildings
            .Where(b => b.CompanyId == targetCompany.Id)
            .ToListAsync();
        foreach (var building in buildings)
        {
            building.CompanyId = destinationCompany.Id;
        }

        // Transfer building lots
        var lots = await db.BuildingLots
            .Where(l => l.OwnerCompanyId == targetCompany.Id)
            .ToListAsync();
        foreach (var lot in lots)
        {
            lot.OwnerCompanyId = destinationCompany.Id;
        }

        // Transfer shareholdings owned BY the target company
        var ownedShareholdings = await db.Shareholdings
            .Where(h => h.OwnerCompanyId == targetCompany.Id)
            .ToListAsync();
        foreach (var holding in ownedShareholdings)
        {
            // Merge with existing holding in destination if present
            var existingHolding = await db.Shareholdings.FirstOrDefaultAsync(h =>
                h.CompanyId == holding.CompanyId && h.OwnerCompanyId == destinationCompany.Id);
            if (existingHolding is not null)
            {
                existingHolding.ShareCount += holding.ShareCount;
                db.Shareholdings.Remove(holding);
            }
            else
            {
                holding.OwnerCompanyId = destinationCompany.Id;
            }
        }

        // Transfer target company's remaining cash to destination
        var cashTransferred = Math.Max(targetCompany.Cash, 0m);
        destinationCompany.Cash += cashTransferred;

        // Record the merger as a ledger entry on destination
        if (cashTransferred > 0m)
        {
            db.LedgerEntries.Add(new LedgerEntry
            {
                Id = Guid.NewGuid(),
                CompanyId = destinationCompany.Id,
                Category = LedgerCategory.Other,
                Description = $"Merger: cash received from {targetCompany.Name}",
                Amount = cashTransferred,
                RecordedAtTick = currentTick,
                RecordedAtUtc = DateTime.UtcNow,
            });
        }

        var buildingsTransferred = buildings.Count;
        var absorbedName = targetCompany.Name;

        // Remove all shareholdings IN the target company (shares become worthless)
        var targetShareholdings = await db.Shareholdings
            .Where(h => h.CompanyId == targetCompany.Id)
            .ToListAsync();
        db.Shareholdings.RemoveRange(targetShareholdings);

        // Remove salary settings and any pending player active-company references
        var salarySettings = await db.CompanyCitySalarySettings
            .Where(s => s.CompanyId == targetCompany.Id)
            .ToListAsync();
        db.CompanyCitySalarySettings.RemoveRange(salarySettings);

        // If any player is currently scoped to the target company, switch them back to person
        var affectedPlayers = await db.Players
            .Where(p => p.ActiveCompanyId == targetCompany.Id)
            .ToListAsync();
        foreach (var p in affectedPlayers)
        {
            p.ActiveAccountType = AccountContextType.Person;
            p.ActiveCompanyId = null;
        }

        db.Companies.Remove(targetCompany);

        await db.SaveChangesAsync();

        return new MergeCompanyResult
        {
            DestinationCompanyId = destinationCompany.Id,
            DestinationCompanyName = destinationCompany.Name,
            AbsorbedCompanyName = absorbedName,
            CashTransferred = cashTransferred,
            BuildingsTransferred = buildingsTransferred,
        };
    }
}
