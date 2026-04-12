using System.Security.Claims;
using Api.Configuration;
using Api.Data;
using Api.Data.Entities;
using Api.Engine;
using Api.Security;
using Api.Utilities;
using HotChocolate.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Api.Types;

public sealed partial class Query
{
    public async Task<List<StockExchangeListingResult>> GetStockExchangeListings(
        [Service] AppDbContext db,
        [Service] IHttpContextAccessor httpContextAccessor)
    {
        var companies = await db.Companies
            .AsNoTracking()
            .OrderBy(company => company.Name)
            .ToListAsync();
        var buildings = await db.Buildings.AsNoTracking().ToListAsync();
        var lots = await db.BuildingLots
            .AsNoTracking()
            .Where(lot => lot.OwnerCompanyId.HasValue)
            .ToListAsync();
        var inventories = await db.Inventories
            .AsNoTracking()
            .Include(inventory => inventory.ResourceType)
            .Include(inventory => inventory.ProductType)
            .ToListAsync();
        var shareholdings = await db.Shareholdings.AsNoTracking().ToListAsync();
        var sharePriceByCompany = BuildQuotedSharePriceLookup(companies, buildings, lots, inventories, shareholdings);

        Guid? userId = null;
        HashSet<Guid> controlledCompanyIds = [];
        if (httpContextAccessor.HttpContext?.User.Identity?.IsAuthenticated == true)
        {
            userId = httpContextAccessor.HttpContext.User.GetRequiredUserId();
            controlledCompanyIds = companies
                .Where(company => company.PlayerId == userId.Value)
                .Select(company => company.Id)
                .ToHashSet();
        }

        return companies
            .Select(company =>
            {
                var sharePrice = sharePriceByCompany.GetValueOrDefault(company.Id);
                var playerOwnedShares = userId.HasValue
                    ? shareholdings
                        .Where(holding => holding.CompanyId == company.Id && holding.OwnerPlayerId == userId.Value)
                        .Sum(holding => holding.ShareCount)
                    : 0m;
                var controlledCompanyOwnedShares = controlledCompanyIds.Count > 0
                    ? shareholdings
                        .Where(holding => holding.CompanyId == company.Id
                            && holding.OwnerCompanyId.HasValue
                            && controlledCompanyIds.Contains(holding.OwnerCompanyId.Value))
                        .Sum(holding => holding.ShareCount)
                    : 0m;
                var combinedRatio = company.TotalSharesIssued > 0m
                    ? decimal.Round((playerOwnedShares + controlledCompanyOwnedShares) / company.TotalSharesIssued, 4, MidpointRounding.AwayFromZero)
                    : 0m;

                return new StockExchangeListingResult
                {
                    CompanyId = company.Id,
                    CompanyName = company.Name,
                    TotalSharesIssued = company.TotalSharesIssued,
                    PublicFloatShares = SharePriceCalculator.ComputePublicFloat(company, shareholdings.Where(holding => holding.CompanyId == company.Id)),
                    SharePrice = sharePrice,
                    MarketValue = decimal.Round(company.TotalSharesIssued * sharePrice, 2, MidpointRounding.AwayFromZero),
                    BidPrice = SharePriceCalculator.ComputeBidPrice(sharePrice),
                    AskPrice = SharePriceCalculator.ComputeAskPrice(sharePrice),
                    DividendPayoutRatio = company.DividendPayoutRatio,
                    PlayerOwnedShares = playerOwnedShares,
                    ControlledCompanyOwnedShares = controlledCompanyOwnedShares,
                    CombinedControlledOwnershipRatio = combinedRatio,
                    CanClaimControl = userId.HasValue && company.PlayerId != userId.Value && combinedRatio >= 0.5m,
                    CanMerge = userId.HasValue && company.PlayerId != userId.Value && combinedRatio >= 0.9m,
                };
            })
            .OrderByDescending(listing => listing.SharePrice)
            .ThenBy(listing => listing.CompanyName)
            .ToList();
    }

    /// <summary>Returns recent quoted share-price history for a single company.</summary>
    public async Task<List<StockExchangePriceHistoryPointResult>> GetStockExchangePriceHistory(
        Guid companyId,
        [Service] AppDbContext db)
    {
        var companies = await db.Companies
            .AsNoTracking()
            .OrderBy(company => company.Name)
            .ToListAsync();
        var targetCompany = companies.FirstOrDefault(company => company.Id == companyId);
        if (targetCompany is null)
        {
            return [];
        }

        var buildings = await db.Buildings.AsNoTracking().ToListAsync();
        var lots = await db.BuildingLots
            .AsNoTracking()
            .Where(lot => lot.OwnerCompanyId.HasValue)
            .ToListAsync();
        var inventories = await db.Inventories
            .AsNoTracking()
            .Include(inventory => inventory.ResourceType)
            .Include(inventory => inventory.ProductType)
            .ToListAsync();
        var shareholdings = await db.Shareholdings.AsNoTracking().ToListAsync();
        var sharePriceByCompany = BuildQuotedSharePriceLookup(companies, buildings, lots, inventories, shareholdings);
        var currentTick = await db.GameStates
            .AsNoTracking()
            .Select(gameState => (long?)gameState.CurrentTick)
            .FirstOrDefaultAsync() ?? 0L;

        var priceHistory = await db.SharePriceHistoryEntries
            .AsNoTracking()
            .Where(entry => entry.CompanyId == companyId)
            .OrderByDescending(entry => entry.RecordedAtTick)
            .ThenByDescending(entry => entry.RecordedAtUtc)
            .Take(100)
            .ToListAsync();

        var groupedHistory = priceHistory
            .GroupBy(entry => entry.RecordedAtTick)
            .Select(group => group.OrderByDescending(entry => entry.RecordedAtUtc).First())
            .OrderBy(entry => entry.RecordedAtTick)
            .Select(entry => new StockExchangePriceHistoryPointResult
            {
                CompanyId = entry.CompanyId,
                Tick = entry.RecordedAtTick,
                Price = entry.SharePrice,
                RecordedAtUtc = entry.RecordedAtUtc,
            })
            .ToList();

        var currentPrice = sharePriceByCompany.GetValueOrDefault(companyId);
        if (currentPrice > 0m && (groupedHistory.Count == 0 || groupedHistory[^1].Tick != currentTick))
        {
            groupedHistory.Add(new StockExchangePriceHistoryPointResult
            {
                CompanyId = companyId,
                Tick = currentTick,
                Price = currentPrice,
                RecordedAtUtc = DateTime.UtcNow,
            });
        }

        return groupedHistory
            .OrderByDescending(point => point.Tick)
            .Take(MaxRecentStockPriceHistoryPoints)
            .OrderBy(point => point.Tick)
            .ToList();
    }

    /// <summary>Returns the ownership breakdown (shareholders list) for a single company.</summary>
    public async Task<CompanyOwnershipResult?> GetCompanyShareholders(
        Guid companyId,
        [Service] AppDbContext db)
    {
        var company = await db.Companies
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == companyId);

        if (company is null)
        {
            return null;
        }

        var shareholdings = await db.Shareholdings
            .AsNoTracking()
            .Where(holding => holding.CompanyId == companyId && holding.ShareCount > 0m)
            .Include(holding => holding.OwnerPlayer)
            .Include(holding => holding.OwnerCompany)
            .ToListAsync();

        var shareholders = shareholdings
            .Select(holding =>
            {
                var holderName = holding.OwnerPlayer?.DisplayName
                    ?? holding.OwnerCompany?.Name
                    ?? "Unknown";
                var holderType = holding.OwnerPlayerId.HasValue ? "PERSON" : "COMPANY";
                var ownershipRatio = company.TotalSharesIssued > 0m
                    ? decimal.Round(holding.ShareCount / company.TotalSharesIssued, 4, MidpointRounding.AwayFromZero)
                    : 0m;

                return new CompanyShareholderResult
                {
                    HolderName = holderName,
                    HolderType = holderType,
                    HolderPlayerId = holding.OwnerPlayerId,
                    HolderCompanyId = holding.OwnerCompanyId,
                    ShareCount = holding.ShareCount,
                    OwnershipRatio = ownershipRatio,
                };
            })
            .OrderByDescending(shareholder => shareholder.OwnershipRatio)
            .ThenBy(shareholder => shareholder.HolderName)
            .ToList();

        var namedSharesTotal = shareholdings.Sum(holding => holding.ShareCount);
        var publicFloat = company.TotalSharesIssued > namedSharesTotal
            ? company.TotalSharesIssued - namedSharesTotal
            : 0m;

        return new CompanyOwnershipResult
        {
            CompanyId = company.Id,
            CompanyName = company.Name,
            TotalSharesIssued = company.TotalSharesIssued,
            PublicFloatShares = publicFloat,
            ShareholderCount = shareholders.Count,
            Shareholders = shareholders,
        };
    }

    private static Dictionary<Guid, decimal> BuildQuotedSharePriceLookup(
        IReadOnlyCollection<Company> companies,
        IReadOnlyCollection<Building> buildings,
        IReadOnlyCollection<BuildingLot> lots,
        IReadOnlyCollection<Inventory> inventories,
        IReadOnlyCollection<Shareholding> shareholdings)
    {
        var baseEquityByCompany = SharePriceCalculator.ComputeBaseEquityByCompany(companies, buildings, lots, inventories);
        return SharePriceCalculator.ComputeQuotedSharePriceByCompany(companies, baseEquityByCompany, shareholdings);
    }
}
