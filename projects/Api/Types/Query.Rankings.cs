using Api.Data;
using Api.Data.Entities;
using Api.Engine;
using Api.Security;
using Api.Utilities;
using HotChocolate.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Api.Types;

/// <summary>
/// Player/company rankings, company management, and game-state queries.
/// Methods: GetRankings, GetCompanyRankings, GetMyCompanies, GetCompanyBrands,
///          GetCompanySettings, GetGameState, GetStarterIndustries.
/// </summary>
public sealed partial class Query
{
    private const string ForbesRealTimeBillionairesUrl = "https://www.forbes.com/real-time-billionaires/";

    /// <summary>
    /// Gets the player ranking (leaderboard) sorted by total wealth.
    ///
    /// Wealth formula for players: Personal cash + value of owned shares.
    /// Wealth formula for companies: Cash + BuildingValue + InventoryValue (unchanged).
    /// </summary>
    public async Task<List<PlayerRanking>> GetRankings([Service] AppDbContext db)
    {
        var players = await db.Players
            .Where(p => p.Role != PlayerRole.Admin)
            .ToListAsync();

        // Load all companies, buildings, lots, inventories, and shareholdings for share price calculation
        var companies = await db.Companies.ToListAsync();
        var buildings = await db.Buildings.ToListAsync();
        var lots = await db.BuildingLots
            .Where(l => l.OwnerCompanyId.HasValue)
            .ToListAsync();
        var inventories = await db.Inventories
            .Include(i => i.ResourceType)
            .Include(i => i.ProductType)
            .ToListAsync();
        var shareholdings = await db.Shareholdings.ToListAsync();

        var sharePriceByCompany = BuildQuotedSharePriceLookup(companies, buildings, lots, inventories, shareholdings);

        return players
            .Select(p =>
            {
                var personalCash = p.PersonalCash;
                var sharesValue = shareholdings
                    .Where(sh => sh.OwnerPlayerId == p.Id && sh.ShareCount > 0m)
                    .Sum(sh => decimal.Round(
                        sh.ShareCount * sharePriceByCompany.GetValueOrDefault(sh.CompanyId),
                        4,
                        MidpointRounding.AwayFromZero));

                return new PlayerRanking
                {
                    PlayerId = p.Id,
                    DisplayName = p.PersonalAccountName ?? p.DisplayName,
                    PersonalAccountName = p.PersonalAccountName,
                    PersonalCash = personalCash,
                    SharesValue = sharesValue,
                    TotalWealth = decimal.Round(personalCash + sharesValue, 4, MidpointRounding.AwayFromZero),
                    CompanyCount = companies.Count(c => c.PlayerId == p.Id)
                };
            })
            .OrderByDescending(r => r.TotalWealth)
            .ToList();
    }

    /// <summary>Returns per-company wealth rankings for the leaderboard.</summary>
    public async Task<List<CompanyRanking>> GetCompanyRankings([Service] AppDbContext db)
    {
        var companies = await db.Companies
            .Include(c => c.Buildings)
            .ThenInclude(b => b.Units)
            .Include(c => c.Player)
            .Where(c => c.Player != null && c.Player.Role != PlayerRole.Admin)
            .AsSplitQuery()
            .ToListAsync();

        var buildingIds = companies
            .SelectMany(c => c.Buildings)
            .Select(b => b.Id)
            .ToList();

        var inventories = await db.Inventories
            .Where(i => buildingIds.Contains(i.BuildingId))
            .Include(i => i.ResourceType)
            .Include(i => i.ProductType)
            .ToListAsync();

        var inventoryByBuilding = inventories
            .GroupBy(i => i.BuildingId)
            .ToDictionary(g => g.Key, g => g.ToList());

        return companies
            .Select(c =>
            {
                var buildingValue = c.Buildings
                    .Sum(b => WealthCalculator.GetBuildingValue(b));
                var inventoryValue = c.Buildings
                    .Sum(b => inventoryByBuilding.TryGetValue(b.Id, out var inv)
                        ? inv.Sum(i => i.Quantity * WealthCalculator.GetItemBasePrice(i))
                        : 0m);

                return new CompanyRanking
                {
                    CompanyId = c.Id,
                    CompanyName = c.Name,
                    PlayerId = c.PlayerId,
                    OwnerDisplayName = c.Player?.PersonalAccountName ?? c.Player?.DisplayName ?? "Unknown",
                    OwnerPersonalAccountName = c.Player?.PersonalAccountName,
                    Cash = c.Cash,
                    BuildingValue = buildingValue,
                    InventoryValue = inventoryValue,
                    TotalWealth = c.Cash + buildingValue + inventoryValue,
                    BuildingCount = c.Buildings.Count
                };
            })
            .OrderByDescending(r => r.TotalWealth)
            .ToList();
    }

    /// <summary>Gets the current player's companies with their buildings.</summary>
    [Authorize]
    public async Task<List<Company>> GetMyCompanies(
        [Service] AppDbContext db,
        [Service] IHttpContextAccessor httpContextAccessor)
    {
        var userId = httpContextAccessor.HttpContext!.User.GetRequiredUserId();

        var gameState = await db.GameStates.FirstOrDefaultAsync();
        if (gameState is not null)
        {
            await BuildingConfigurationService.ApplyDuePlansAsync(db, gameState.CurrentTick);
            await db.SaveChangesAsync();
        }

        return await db.Companies
            .Include(c => c.Buildings)
            .ThenInclude(b => b.Units)
            .Include(c => c.Buildings)
            .ThenInclude(b => b.PendingConfiguration)
            .ThenInclude(plan => plan!.Units)
            .Include(c => c.Buildings)
            .ThenInclude(b => b.PendingConfiguration)
            .ThenInclude(plan => plan!.Removals)
            .AsSplitQuery()
            .Where(c => c.PlayerId == userId)
            .OrderBy(c => c.Name)
            .ToListAsync();
    }

    /// <summary>
    /// Returns the brand research state for a company — all brands accumulated by R&amp;D and marketing
    /// so the frontend can show current product-quality and brand-awareness progress.
    /// Requires auth and company ownership.
    /// </summary>
    [Authorize]
    public async Task<List<ResearchBrandState>> GetCompanyBrands(
        Guid companyId,
        [Service] AppDbContext db,
        [Service] IHttpContextAccessor httpContextAccessor)
    {
        var userId = httpContextAccessor.HttpContext!.User.GetRequiredUserId();
        var company = await db.Companies
            .FirstOrDefaultAsync(c => c.Id == companyId && c.PlayerId == userId);
        if (company is null)
            return [];

        var brands = await db.Brands
            .Where(b => b.CompanyId == companyId)
            .ToListAsync();

        var productTypeIds = brands
            .Where(b => b.ProductTypeId.HasValue)
            .Select(b => b.ProductTypeId!.Value)
            .Distinct()
            .ToList();

        var productTypes = await db.ProductTypes
            .Where(pt => productTypeIds.Contains(pt.Id))
            .ToListAsync();

        return brands.Select(b =>
        {
            var pt = b.ProductTypeId.HasValue
                ? productTypes.FirstOrDefault(p => p.Id == b.ProductTypeId.Value)
                : null;
            return new ResearchBrandState
            {
                Id = b.Id,
                CompanyId = b.CompanyId,
                Name = b.Name,
                Scope = b.Scope,
                ProductTypeId = b.ProductTypeId,
                ProductName = pt?.Name,
                IndustryCategory = b.IndustryCategory,
                Awareness = b.Awareness,
                Quality = b.Quality,
                MarketingEfficiencyMultiplier = b.MarketingEfficiencyMultiplier,
            };
        }).ToList();
    }

    /// <summary>Returns owner-editable company settings including salary levels per city.</summary>
    [Authorize]
    public async Task<CompanySettingsResult?> GetCompanySettings(
        Guid companyId,
        [Service] AppDbContext db,
        [Service] IHttpContextAccessor httpContextAccessor)
    {
        var userId = httpContextAccessor.HttpContext!.User.GetRequiredUserId();
        var company = await db.Companies
            .Include(candidate => candidate.CitySalarySettings)
            .FirstOrDefaultAsync(candidate => candidate.Id == companyId && candidate.PlayerId == userId);

        if (company is null)
        {
            return null;
        }

        var cities = await db.Cities
            .OrderBy(city => city.Name)
            .ToListAsync();
        var allCompanies = await db.Companies
            .Include(candidate => candidate.Buildings)
            .ToListAsync();
        var allOwnedLots = await db.BuildingLots
            .Where(lot => lot.OwnerCompanyId.HasValue)
            .ToListAsync();
        var companyBuildingIds = allCompanies
            .SelectMany(candidate => candidate.Buildings)
            .Select(building => building.Id)
            .ToList();
        var allInventories = await db.Inventories
            .Where(inventory => companyBuildingIds.Contains(inventory.BuildingId))
            .Include(inventory => inventory.ResourceType)
            .Include(inventory => inventory.ProductType)
            .ToListAsync();

        var companyAssetValues = allCompanies.ToDictionary(
            candidate => candidate.Id,
            candidate => ComputeCompanyAssetValue(candidate, allOwnedLots, allInventories));
        var assetValue = companyAssetValues.GetValueOrDefault(company.Id);
        var currentTick = await db.GameStates.AsNoTracking().Select(state => state.CurrentTick).FirstOrDefaultAsync();
        var maxAssetValue = companyAssetValues.Values.DefaultIfEmpty(0m).Max();
        var overheadRate = CompanyEconomyCalculator.ComputeAdministrationOverheadRate(
            company,
            assetValue,
            maxAssetValue,
            currentTick);
        var (ageFactor, assetFactor) = CompanyEconomyCalculator.ComputeAdministrationOverheadDrivers(
            company,
            assetValue,
            maxAssetValue,
            currentTick);

        return new CompanySettingsResult
        {
            CompanyId = company.Id,
            CompanyName = company.Name,
            Cash = company.Cash,
            TotalSharesIssued = company.TotalSharesIssued,
            DividendPayoutRatio = company.DividendPayoutRatio,
            FoundedAtTick = company.FoundedAtTick,
            AdministrationOverheadRate = overheadRate,
            AgeFactor = ageFactor,
            AssetFactor = assetFactor,
            AssetValue = assetValue,
            CitySalarySettings = cities
                .Select(city =>
                {
                    var multiplier = CompanyEconomyCalculator.GetSalaryMultiplier(company.CitySalarySettings, city.Id);
                    return new CompanyCitySalarySettingResult
                    {
                        CityId = city.Id,
                        CityName = city.Name,
                        BaseSalaryPerManhour = city.BaseSalaryPerManhour,
                        SalaryMultiplier = multiplier,
                        EffectiveSalaryPerManhour = CompanyEconomyCalculator.GetEffectiveHourlyWage(city, multiplier),
                    };
                })
                .ToList(),
        };
    }

    /// <summary>Gets the current game state (tick, tax info).</summary>
    public async Task<GameState?> GetGameState([Service] AppDbContext db, [Service] IMemoryCache cache)
    {
        // Use a very short cache to reduce DB reads when multiple panels request game state
        // simultaneously or in rapid succession (e.g. dashboard + home page on navigation).
        const string key = "gameState_singleton";
        if (cache.TryGetValue(key, out GameState? cached) && cached is not null)
        {
            return cached;
        }

        var gameState = await db.GameStates.FirstOrDefaultAsync();
        if (gameState is null)
        {
            return null;
        }

        await BuildingConfigurationService.ApplyDuePlansAsync(db, gameState.CurrentTick);
        await db.SaveChangesAsync();

        cache.Set(key, gameState, TimeSpan.FromSeconds(8));
        return gameState;
    }

    /// <summary>Gets available starter industries for onboarding.</summary>
    public StarterIndustriesPayload GetStarterIndustries()
    {
        return new StarterIndustriesPayload
        {
            Industries = Industry.StarterIndustries.ToList()
        };
    }

    /// <summary>Returns the top real-world wealth targets for the endgame win condition.</summary>
    public async Task<List<EndgameTargetPersonResult>> GetEndgameTargetLeaderboard([Service] AppDbContext db)
    {
        var targets = await EndgameService.GetTargetRichListAsync(db);
        return targets
            .Select(target => new EndgameTargetPersonResult
            {
                Name = target.Name,
                EstimatedUsdWealth = target.EstimatedUsdWealth,
                Source = target.Source,
                SourceDateUtc = target.SourceDateUtc,
            })
            .ToList();
    }

    /// <summary>Returns the current game-over status for endgame-aware UI surfaces.</summary>
    public async Task<GameStatusResult?> GetGameStatus([Service] AppDbContext db)
    {
        var gameState = await db.GameStates
            .AsNoTracking()
            .Select(state => new { state.IsEnded, state.EndedAtUtc, state.WinnerDisplayName })
            .FirstOrDefaultAsync();
        if (gameState is null)
        {
            return null;
        }

        return new GameStatusResult
        {
            IsGameOver = gameState.IsEnded,
            GameOverAt = gameState.EndedAtUtc,
            WinnerName = gameState.WinnerDisplayName,
            TopRealWorldWealth = await EndgameService.GetWinThresholdWealthAsync(db),
        };
    }

    /// <summary>Alias of endgameTargetLeaderboard for compatibility with rich-list naming in product docs.</summary>
    public Task<List<EndgameTargetPersonResult>> GetRichList([Service] AppDbContext db) => GetEndgameTargetLeaderboard(db);

    /// <summary>Canonical endgame benchmark query used by docs and frontend progress views.</summary>
    public Task<List<EndgameTargetPersonResult>> GetRealWorldBenchmarks([Service] AppDbContext db) => GetEndgameTargetLeaderboard(db);

    /// <summary>Compatibility alias matching issue naming for top real-world billionaire targets.</summary>
    public async Task<List<TopRealWorldBillionaireResult>> GetTopRealWorldBillionaires([Service] AppDbContext db)
    {
        var targets = await EndgameService.GetTargetRichListAsync(db);
        return targets
            .Select(target => new TopRealWorldBillionaireResult
            {
                Name = target.Name,
                WealthUsd = target.EstimatedUsdWealth,
                SourceUrl = target.Source.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                    ? target.Source
                    : ForbesRealTimeBillionairesUrl,
                SourceDateUtc = target.SourceDateUtc,
            })
            .ToList();
    }

    /// <summary>Canonical game-end payload containing winner details and final top-10 personal ranking.</summary>
    public async Task<GameEndStateResult?> GetGameEndState([Service] AppDbContext db)
    {
        var gameState = await db.GameStates
            .AsNoTracking()
            .Select(state => new
            {
                state.IsEnded,
                state.EndedAtUtc,
                state.WinnerDisplayName,
                state.WinnerWealth,
            })
            .FirstOrDefaultAsync();
        if (gameState is null)
        {
            return null;
        }

        var result = new GameEndStateResult
        {
            IsEnded = gameState.IsEnded,
            EndedAt = gameState.EndedAtUtc,
            Winner = gameState.WinnerDisplayName is null
                ? null
                : new GameEndWinnerResult
                {
                    Name = gameState.WinnerDisplayName,
                    NetWorth = gameState.WinnerWealth ?? 0m,
                },
        };

        if (!gameState.IsEnded)
        {
            return result;
        }

        var ranking = await EndgameService.ComputePlayerWealthRankingAsync(db);
        result.TopRanking = ranking
            .Take(10)
            .Select((entry, index) => new GameEndTopRankingEntry
            {
                Rank = index + 1,
                Name = entry.DisplayName,
                NetWorth = entry.TotalWealth,
            })
            .ToList();

        return result;
    }

    /// <summary>Compatibility alias matching issue naming for full endgame status payload.</summary>
    public async Task<EndgameStatusResult?> GetEndgameStatus([Service] AppDbContext db)
    {
        var gameEndState = await GetGameEndState(db);
        if (gameEndState is null)
        {
            return null;
        }

        return new EndgameStatusResult
        {
            IsEnded = gameEndState.IsEnded,
            EndedAtUtc = gameEndState.EndedAt,
            Winner = gameEndState.Winner is null
                ? null
                : new EndgameStatusWinnerResult
                {
                    Name = gameEndState.Winner.Name,
                    Wealth = gameEndState.Winner.NetWorth,
                },
            FinalRankings = gameEndState.TopRanking
                .Select(entry => new EndgameStatusRankingEntry
                {
                    Rank = entry.Rank,
                    Name = entry.Name,
                    Wealth = entry.NetWorth,
                })
                .ToList(),
        };
    }
}
