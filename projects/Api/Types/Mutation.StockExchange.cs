using System.Globalization;
using System.Security.Claims;
using Api.Data;
using Api.Data.Entities;
using Api.Engine;
using Api.Security;
using Api.Utilities;
using HotChocolate.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Api.Types;

/// <summary>
/// Stock exchange mutations: buying and selling company shares
/// through the personal or company trading account.
/// </summary>
public sealed partial class Mutation
{
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
        var currentTick = await GetCurrentTickAsync(db);

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
            db.PersonTradeRecords.Add(new PersonTradeRecord
            {
                Id = Guid.NewGuid(),
                PlayerId = player.Id,
                CompanyId = targetCompany.Id,
                Direction = TradeDirection.Buy,
                ShareCount = shareCount,
                PricePerShare = askPrice,
                TotalValue = totalValue,
                RecordedAtTick = currentTick,
                RecordedAtUtc = DateTime.UtcNow,
            });
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
            AddCompanyLedgerEntry(
                db,
                account.Company,
                LedgerCategory.StockPurchase,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Bought {shareCount:0.####} shares in {targetCompany.Name} @ {askPrice:0.00}"),
                -totalValue,
                currentTick);

            if (account.Company.Id == targetCompany.Id)
            {
                targetCompany.TotalSharesIssued = Math.Max(0m, decimal.Round(targetCompany.TotalSharesIssued - shareCount, 4, MidpointRounding.AwayFromZero));
                await RecordSharePriceHistoryAsync(db, targetCompany.Id, askPrice, currentTick);
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

        await RecordSharePriceHistoryAsync(db, targetCompany.Id, askPrice, currentTick);
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
        var currentTick = await GetCurrentTickAsync(db);

        holding.ShareCount = decimal.Round(holding.ShareCount - shareCount, 4, MidpointRounding.AwayFromZero);
        if (holding.ShareCount <= 0m)
        {
            db.Shareholdings.Remove(holding);
            shareholdings.Remove(holding);
        }

        if (account.Company is null)
        {
            player.PersonalCash += totalValue;
            db.PersonTradeRecords.Add(new PersonTradeRecord
            {
                Id = Guid.NewGuid(),
                PlayerId = player.Id,
                CompanyId = targetCompany.Id,
                Direction = TradeDirection.Sell,
                ShareCount = shareCount,
                PricePerShare = bidPrice,
                TotalValue = totalValue,
                RecordedAtTick = currentTick,
                RecordedAtUtc = DateTime.UtcNow,
            });
        }
        else
        {
            account.Company.Cash += totalValue;
            AddCompanyLedgerEntry(
                db,
                account.Company,
                LedgerCategory.StockSale,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Sold {shareCount:0.####} shares in {targetCompany.Name} @ {bidPrice:0.00}"),
                totalValue,
                currentTick);
        }

        await RecordSharePriceHistoryAsync(db, targetCompany.Id, bidPrice, currentTick);
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

    private static async Task RecordSharePriceHistoryAsync(AppDbContext db, Guid companyId, decimal sharePrice, long currentTick)
    {
        var latestEntryForTick = await db.SharePriceHistoryEntries
            .Where(entry => entry.CompanyId == companyId && entry.RecordedAtTick == currentTick)
            .OrderByDescending(entry => entry.RecordedAtUtc)
            .FirstOrDefaultAsync();

        if (latestEntryForTick is null)
        {
            db.SharePriceHistoryEntries.Add(new SharePriceHistoryEntry
            {
                Id = Guid.NewGuid(),
                CompanyId = companyId,
                SharePrice = sharePrice,
                RecordedAtTick = currentTick,
                RecordedAtUtc = DateTime.UtcNow,
            });
            return;
        }

        latestEntryForTick.SharePrice = sharePrice;
        latestEntryForTick.RecordedAtUtc = DateTime.UtcNow;
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

    private sealed record ActiveTradingAccount(string AccountType, Company? Company, string AccountName);
}
