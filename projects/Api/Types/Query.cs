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

/// <summary>
/// GraphQL query type for the Capitalism V game.
/// Provides read access to game data including players, cities, resources, products, and buildings.
/// Split across multiple partial files, one per domain:
/// <list type="bullet">
/// <item><see cref="Query"/> (this file) — auth, admin, news, stock exchange, world, resources, products</item>
/// <item><c>Query.Building.cs</c> — building inventory, operational status, analytics, ledger</item>
/// <item><c>Query.Chat.cs</c> — in-game chat feed</item>
/// <item><c>Query.Rankings.cs</c> — player/company rankings and game state</item>
/// <item><c>Query.Lending.cs</c> — bank loan offers and player loans</item>
/// </list>
/// </summary>
public sealed partial class Query
{
    private const int MaxRecentStockPriceHistoryPoints = 12;
    private const int DefaultChatMessageLimit = 50;
    private const int MaxChatMessageLimit = 100;

    /// <summary>Returns the currently authenticated player's profile.</summary>
    [Authorize]
    public async Task<Player?> GetMe(
        [Service] AppDbContext db,
        [Service] IHttpContextAccessor httpContextAccessor)
    {
        var principal = httpContextAccessor.HttpContext!.User;
        var userId = principal.GetRequiredUserId();
        var player = await db.Players
            .AsNoTracking()
            .Include(p => p.Companies)
            .FirstOrDefaultAsync(p => p.Id == userId);

        return ApplyImpersonationAccountContext(player, principal);
    }

    /// <summary>Returns the authenticated player's personal account, portfolio, and dividend history.</summary>
    [Authorize]
    public async Task<PersonAccountResult?> GetPersonAccount(
        [Service] AppDbContext db,
        [Service] IHttpContextAccessor httpContextAccessor)
    {
        var principal = httpContextAccessor.HttpContext!.User;
        var userId = principal.GetRequiredUserId();
        var player = await db.Players
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == userId);

        if (player is null)
        {
            return null;
        }

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
        var companiesById = companies.ToDictionary(company => company.Id);

        var portfolio = shareholdings
            .Where(holding => holding.OwnerPlayerId == userId && holding.ShareCount > 0m)
            .Select(holding =>
            {
                var company = companiesById[holding.CompanyId];
                var sharePrice = sharePriceByCompany.GetValueOrDefault(holding.CompanyId);
                return new PortfolioHoldingResult
                {
                    CompanyId = company.Id,
                    CompanyName = company.Name,
                    ShareCount = holding.ShareCount,
                    OwnershipRatio = company.TotalSharesIssued > 0m
                        ? decimal.Round(holding.ShareCount / company.TotalSharesIssued, 4, MidpointRounding.AwayFromZero)
                        : 0m,
                    SharePrice = sharePrice,
                    MarketValue = decimal.Round(holding.ShareCount * sharePrice, 4, MidpointRounding.AwayFromZero),
                };
            })
            .OrderByDescending(holding => holding.MarketValue)
            .ThenBy(holding => holding.CompanyName)
            .ToList();

        var dividendPayments = await db.DividendPayments
            .AsNoTracking()
            .Where(payment => payment.RecipientPlayerId == userId)
            .OrderByDescending(payment => payment.RecordedAtTick)
            .ToListAsync();

        var stockTrades = await db.PersonTradeRecords
            .AsNoTracking()
            .Where(trade => trade.PlayerId == userId)
            .OrderByDescending(trade => trade.RecordedAtTick)
            .ThenByDescending(trade => trade.RecordedAtUtc)
            .Take(100)
            .ToListAsync();

        var result = new PersonAccountResult
        {
            PlayerId = player.Id,
            DisplayName = player.DisplayName,
            PersonalCash = player.PersonalCash,
            ActiveAccountType = player.ActiveAccountType,
            ActiveCompanyId = player.ActiveCompanyId,
            Shareholdings = portfolio,
            DividendPayments = dividendPayments
                .Select(payment => new DividendPaymentResult
                {
                    Id = payment.Id,
                    CompanyId = payment.CompanyId,
                    CompanyName = companiesById.GetValueOrDefault(payment.CompanyId)?.Name ?? string.Empty,
                    ShareCount = payment.ShareCount,
                    AmountPerShare = payment.AmountPerShare,
                    TotalAmount = payment.TotalAmount,
                    GameYear = payment.GameYear,
                    RecordedAtTick = payment.RecordedAtTick,
                    RecordedAtUtc = payment.RecordedAtUtc,
                    Description = payment.Description,
                })
                .ToList(),
            StockTrades = stockTrades
                .Select(trade => new PersonTradeRecordResult
                {
                    Id = trade.Id,
                    CompanyId = trade.CompanyId,
                    CompanyName = companiesById.GetValueOrDefault(trade.CompanyId)?.Name ?? string.Empty,
                    Direction = trade.Direction,
                    ShareCount = trade.ShareCount,
                    PricePerShare = trade.PricePerShare,
                    TotalValue = trade.TotalValue,
                    RecordedAtTick = trade.RecordedAtTick,
                    RecordedAtUtc = trade.RecordedAtUtc,
                })
                .ToList(),
        };

        ApplyImpersonationAccountContext(result, principal);
        return result;
    }

    [Authorize]
    public async Task<GameAdminSessionInfo> GetGameAdminSession(
        [Service] AppDbContext db,
        [Service] IHttpContextAccessor httpContextAccessor,
        [Service] GameAdminAuthorizationService gameAdminAuthorizationService)
    {
        var principal = httpContextAccessor.HttpContext!.User;
        var accessContext = await gameAdminAuthorizationService.GetAccessContextAsync(db, principal, httpContextAccessor.HttpContext!.RequestAborted);
        var effectiveUserId = principal.GetRequiredUserId();

        var players = await db.Players
            .AsNoTracking()
            .Include(player => player.Companies)
            .Where(player => player.Id == accessContext.ActorPlayer.Id || player.Id == effectiveUserId)
            .ToListAsync(httpContextAccessor.HttpContext.RequestAborted);

        var adminActor = players.First(player => player.Id == accessContext.ActorPlayer.Id);
        var effectivePlayer = players.First(player => player.Id == effectiveUserId);
        var effectiveAccountType = principal.GetEffectiveAccountType() ?? effectivePlayer.ActiveAccountType;
        var effectiveCompanyId = principal.GetEffectiveCompanyId() ?? effectivePlayer.ActiveCompanyId;
        var effectiveCompanyName = principal.GetEffectiveCompanyName()
            ?? effectivePlayer.Companies.FirstOrDefault(company => company.Id == effectiveCompanyId)?.Name;

        return new GameAdminSessionInfo
        {
            IsLocalAdmin = accessContext.IsLocalAdmin,
            HasGlobalAdminRole = accessContext.HasGlobalAdminRole,
            IsRootAdministrator = accessContext.IsRootAdministrator,
            CanAccessAdminDashboard = accessContext.CanAccessAdminDashboard,
            IsImpersonating = accessContext.IsImpersonating,
            AdminActor = ToGameAdminPlayerSummary(adminActor),
            EffectivePlayer = ToGameAdminPlayerSummary(effectivePlayer),
            EffectiveAccountType = effectiveAccountType,
            EffectiveCompanyId = effectiveCompanyId,
            EffectiveCompanyName = effectiveCompanyName,
        };
    }

    [Authorize]
    public async Task<GameAdminDashboardResult> GetGameAdminDashboard(
        [Service] AppDbContext db,
        [Service] IHttpContextAccessor httpContextAccessor,
        [Service] GameAdminAuthorizationService gameAdminAuthorizationService,
        [Service] IMasterGameAdministrationService masterGameAdministrationService,
        [Service] IOptions<MasterServerRegistrationOptions> masterServerOptions)
    {
        var principal = httpContextAccessor.HttpContext!.User;
        var accessContext = await gameAdminAuthorizationService.RequireAdminDashboardAccessAsync(db, principal, httpContextAccessor.HttpContext!.RequestAborted);

        var players = await db.Players
            .AsNoTracking()
            .Include(player => player.Companies)
            .OrderBy(player => player.DisplayName)
            .ToListAsync(httpContextAccessor.HttpContext.RequestAborted);
        var companies = players.SelectMany(player => player.Companies).ToList();
        var currentTick = await db.GameStates
            .AsNoTracking()
            .Select(state => state.CurrentTick)
            .FirstOrDefaultAsync(httpContextAccessor.HttpContext.RequestAborted);
        var recentLedgerEntries = await db.LedgerEntries
            .AsNoTracking()
            .Where(entry => entry.RecordedAtTick >= Math.Max(0, currentTick - 100))
            .ToListAsync(httpContextAccessor.HttpContext.RequestAborted);
        var loans = await db.Loans
            .AsNoTracking()
            .ToListAsync(httpContextAccessor.HttpContext.RequestAborted);
        var shareholdings = await db.Shareholdings
            .AsNoTracking()
            .Where(holding => holding.OwnerPlayerId.HasValue || holding.OwnerCompanyId.HasValue)
            .ToListAsync(httpContextAccessor.HttpContext.RequestAborted);
        var auditLogs = await db.AdminActionAuditLogs
            .AsNoTracking()
            .OrderByDescending(log => log.RecordedAtUtc)
            .Take(25)
            .ToListAsync(httpContextAccessor.HttpContext.RequestAborted);

        var inflowSummaries = BuildInflowSummaries(recentLedgerEntries);
        var shippingCostSummaries = BuildShippingCostSummaries(recentLedgerEntries, companies);
        var globalAdminGrants = accessContext.IsRootAdministrator
            ? (await masterGameAdministrationService.GetGlobalGameAdminGrantsAsync(accessContext.ActorPlayer.Email, httpContextAccessor.HttpContext.RequestAborted)).ToList()
            : [];

        return new GameAdminDashboardResult
        {
            ServerKey = masterServerOptions.Value.ServerKey,
            TotalPersonalCash = players.Sum(player => player.PersonalCash),
            TotalCompanyCash = companies.Sum(company => company.Cash),
            MoneySupply = players.Sum(player => player.PersonalCash) + companies.Sum(company => company.Cash),
            ExternalMoneyInflowLast100Ticks = recentLedgerEntries
                .Where(entry => entry.Amount > 0m)
                .Where(entry => entry.Category is LedgerCategory.Revenue or LedgerCategory.MediaHouseIncome or LedgerCategory.RentIncome)
                .Sum(entry => entry.Amount),
            TotalShippingCostsLast100Ticks = Math.Abs(recentLedgerEntries
                .Where(entry => entry.Category == LedgerCategory.ShippingCost && entry.Amount < 0m)
                .Sum(entry => entry.Amount)),
            InflowSummaries = inflowSummaries,
            ShippingCostSummaries = shippingCostSummaries,
            MultiAccountAlerts = BuildMultiAccountAlerts(players, companies, loans, shareholdings),
            Players = players.Select(ToGameAdminPlayerSummary).ToList(),
            InvisiblePlayers = players.Where(player => player.IsInvisibleInChat).Select(ToGameAdminPlayerSummary).ToList(),
            GlobalGameAdminGrants = globalAdminGrants,
            RecentAuditLogs = auditLogs.Select(log => new GameAdminAuditLogRecord
            {
                Id = log.Id,
                AdminActorPlayerId = log.AdminActorPlayerId,
                AdminActorEmail = log.AdminActorEmail,
                AdminActorDisplayName = log.AdminActorDisplayName,
                EffectivePlayerId = log.EffectivePlayerId,
                EffectivePlayerEmail = log.EffectivePlayerEmail,
                EffectivePlayerDisplayName = log.EffectivePlayerDisplayName,
                EffectiveAccountType = log.EffectiveAccountType,
                EffectiveCompanyId = log.EffectiveCompanyId,
                EffectiveCompanyName = log.EffectiveCompanyName,
                GraphQlOperationName = log.GraphQlOperationName,
                MutationSummary = log.MutationSummary,
                ResponseStatusCode = log.ResponseStatusCode,
                RecordedAtUtc = log.RecordedAtUtc,
            }).ToList(),
        };
    }

    public async Task<GameNewsFeedResult> GetGameNewsFeed(
        bool includeDrafts,
        [Service] AppDbContext db,
        [Service] IHttpContextAccessor httpContextAccessor,
        [Service] IMasterGameAdministrationService masterGameAdministrationService,
        [Service] GameAdminAuthorizationService gameAdminAuthorizationService)
    {
        var principal = httpContextAccessor.HttpContext?.User;
        string? playerEmail = null;
        string? requesterEmail = null;

        if (principal?.Identity?.IsAuthenticated == true)
        {
            var effectiveUserId = principal.GetRequiredUserId();
            playerEmail = await db.Players
                .AsNoTracking()
                .Where(player => player.Id == effectiveUserId)
                .Select(player => player.Email)
                .FirstOrDefaultAsync(httpContextAccessor.HttpContext!.RequestAborted);
        }

        if (includeDrafts)
        {
            if (principal?.Identity?.IsAuthenticated != true)
            {
                throw new GraphQLException(
                    ErrorBuilder.New()
                        .SetMessage("Authentication is required to view draft news entries.")
                        .SetCode("NOT_AUTHENTICATED")
                        .Build());
            }

            var accessContext = await gameAdminAuthorizationService.RequireAdminDashboardAccessAsync(db, principal, httpContextAccessor.HttpContext!.RequestAborted);
            requesterEmail = accessContext.ActorPlayer.Email;
        }

        return await masterGameAdministrationService.GetGameNewsFeedAsync(
            playerEmail,
            includeDrafts,
            requesterEmail,
            httpContextAccessor.HttpContext?.RequestAborted ?? CancellationToken.None);
    }

    /// <summary>
    /// Returns quoted company share prices and public-float availability for the stock exchange.
    /// When authenticated, includes the current player's direct and controlled-company holdings.
    /// </summary>
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

    private static Player? ApplyImpersonationAccountContext(Player? player, ClaimsPrincipal principal)
    {
        if (player is null)
        {
            return null;
        }

        var effectiveAccountType = principal.GetEffectiveAccountType();
        if (string.IsNullOrWhiteSpace(effectiveAccountType))
        {
            return player;
        }

        player.ActiveAccountType = effectiveAccountType;
        player.ActiveCompanyId = principal.GetEffectiveCompanyId();
        return player;
    }

    private static void ApplyImpersonationAccountContext(PersonAccountResult result, ClaimsPrincipal principal)
    {
        var effectiveAccountType = principal.GetEffectiveAccountType();
        if (string.IsNullOrWhiteSpace(effectiveAccountType))
        {
            return;
        }

        result.ActiveAccountType = effectiveAccountType;
        result.ActiveCompanyId = principal.GetEffectiveCompanyId();
    }

    private static GameAdminPlayerSummary ToGameAdminPlayerSummary(Player player)
    {
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
            Companies = player.Companies
                .OrderBy(company => company.Name)
                .Select(company => new GameAdminCompanySummary
                {
                    Id = company.Id,
                    Name = company.Name,
                    Cash = company.Cash,
                })
                .ToList(),
        };
    }

    private static List<GameAdminMoneyInflowSummary> BuildInflowSummaries(IReadOnlyCollection<LedgerEntry> recentLedgerEntries)
    {
        var descriptions = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [LedgerCategory.Revenue] = "Revenue earned from selling goods into the public market.",
            [LedgerCategory.MediaHouseIncome] = "Advertising income flowing into media businesses.",
            [LedgerCategory.RentIncome] = "Lease income flowing from property tenants.",
            [LedgerCategory.LoanOrigination] = "New borrowed cash entering company treasuries.",
        };

        return recentLedgerEntries
            .Where(entry => entry.Amount > 0m && descriptions.ContainsKey(entry.Category))
            .GroupBy(entry => entry.Category)
            .Select(group => new GameAdminMoneyInflowSummary
            {
                Category = group.Key,
                Amount = group.Sum(entry => entry.Amount),
                Description = descriptions[group.Key],
            })
            .OrderByDescending(summary => summary.Amount)
            .ToList();
    }

    private static List<GameAdminShippingCostSummary> BuildShippingCostSummaries(
        IReadOnlyCollection<LedgerEntry> recentLedgerEntries,
        IReadOnlyCollection<Company> companies)
    {
        var companyNameById = companies.ToDictionary(company => company.Id, company => company.Name);

        return recentLedgerEntries
            .Where(entry => entry.Category == LedgerCategory.ShippingCost && entry.Amount < 0m)
            .GroupBy(entry => entry.CompanyId)
            .Select(group => new GameAdminShippingCostSummary
            {
                CompanyId = group.Key,
                CompanyName = companyNameById.GetValueOrDefault(group.Key, "Unknown company"),
                Amount = Math.Abs(group.Sum(entry => entry.Amount)),
                EntryCount = group.Count(),
            })
            .OrderByDescending(summary => summary.Amount)
            .ThenBy(summary => summary.CompanyName)
            .ToList();
    }

    private static List<GameAdminMultiAccountAlert> BuildMultiAccountAlerts(
        IReadOnlyCollection<Player> players,
        IReadOnlyCollection<Company> companies,
        IReadOnlyCollection<Loan> loans,
        IReadOnlyCollection<Shareholding> shareholdings)
    {
        var playersById = players.ToDictionary(player => player.Id);
        var companiesById = companies.ToDictionary(company => company.Id);
        var alerts = new List<GameAdminMultiAccountAlert>();

        foreach (var loan in loans.Where(loan => loan.RemainingPrincipal > 0m && loan.Status != LoanStatus.Repaid))
        {
            if (!companiesById.TryGetValue(loan.BorrowerCompanyId, out var borrowerCompany)
                || !companiesById.TryGetValue(loan.LenderCompanyId, out var lenderCompany)
                || borrowerCompany.PlayerId == lenderCompany.PlayerId
                || !playersById.TryGetValue(borrowerCompany.PlayerId, out var borrowerPlayer)
                || !playersById.TryGetValue(lenderCompany.PlayerId, out var lenderPlayer))
            {
                continue;
            }

            var confidenceScore = loan.AnnualInterestRatePercent <= 2m
                ? 0.95m
                : loan.AnnualInterestRatePercent <= 5m
                    ? 0.75m
                    : 0.5m;

            alerts.Add(new GameAdminMultiAccountAlert
            {
                Reason = "Cross-player loan exposure",
                ExposureAmount = loan.RemainingPrincipal,
                ConfidenceScore = confidenceScore,
                SupportingEntityType = "LOAN",
                SupportingEntityName = $"{lenderCompany.Name} → {borrowerCompany.Name}",
                PrimaryPlayer = ToGameAdminPlayerSummary(lenderPlayer),
                RelatedPlayer = ToGameAdminPlayerSummary(borrowerPlayer),
            });
        }

        foreach (var holding in shareholdings)
        {
            if (!companiesById.TryGetValue(holding.CompanyId, out var targetCompany) || targetCompany.TotalSharesIssued <= 0m)
            {
                continue;
            }

            Player? ownerPlayer = null;
            if (holding.OwnerPlayerId.HasValue)
            {
                playersById.TryGetValue(holding.OwnerPlayerId.Value, out ownerPlayer);
            }
            else if (holding.OwnerCompanyId.HasValue
                && companiesById.TryGetValue(holding.OwnerCompanyId.Value, out var ownerCompany)
                && playersById.TryGetValue(ownerCompany.PlayerId, out var ownerCompanyPlayer))
            {
                ownerPlayer = ownerCompanyPlayer;
            }

            if (ownerPlayer is null || ownerPlayer.Id == targetCompany.PlayerId || !playersById.TryGetValue(targetCompany.PlayerId, out var targetPlayer))
            {
                continue;
            }

            var ownershipRatio = holding.ShareCount / targetCompany.TotalSharesIssued;
            if (ownershipRatio < 0.2m)
            {
                continue;
            }

            alerts.Add(new GameAdminMultiAccountAlert
            {
                Reason = "Cross-player equity concentration",
                ExposureAmount = decimal.Round(ownershipRatio * 100m, 2, MidpointRounding.AwayFromZero),
                ConfidenceScore = Math.Clamp(0.55m + ownershipRatio, 0m, 0.99m),
                SupportingEntityType = "SHAREHOLDING",
                SupportingEntityName = $"{targetCompany.Name} ({decimal.Round(ownershipRatio * 100m, 1, MidpointRounding.AwayFromZero)}% stake)",
                PrimaryPlayer = ToGameAdminPlayerSummary(ownerPlayer),
                RelatedPlayer = ToGameAdminPlayerSummary(targetPlayer),
            });
        }

        return alerts
            .OrderByDescending(alert => alert.ConfidenceScore)
            .ThenByDescending(alert => alert.ExposureAmount)
            .Take(12)
            .ToList();
    }

    /// <summary>Lists all cities available on the game map.</summary>
    public async Task<List<City>> GetCities([Service] AppDbContext db)
    {
        return await db.Cities
            .Include(c => c.Resources)
            .ThenInclude(r => r.ResourceType)
            .OrderBy(c => c.Name)
            .ToListAsync();
    }

    /// <summary>Gets a specific city by ID.</summary>
    public async Task<City?> GetCity(Guid id, [Service] AppDbContext db)
    {
        var gameState = await db.GameStates.FirstOrDefaultAsync();
        if (gameState is not null)
        {
            await BuildingConfigurationService.ApplyDuePlansAsync(db, gameState.CurrentTick);
            await db.SaveChangesAsync();
        }

        return await db.Cities
            .Include(c => c.Resources)
            .ThenInclude(r => r.ResourceType)
            .Include(c => c.Buildings)
            .ThenInclude(b => b.PendingConfiguration)
            .ThenInclude(plan => plan!.Units)
            .Include(c => c.Buildings)
            .ThenInclude(b => b.PendingConfiguration)
            .ThenInclude(plan => plan!.Removals)
            .Include(c => c.Buildings)
            .ThenInclude(b => b.Units)
            .AsSplitQuery()
            .FirstOrDefaultAsync(c => c.Id == id);
    }

    /// <summary>Lists all raw material resource types in the game encyclopaedia.</summary>
    public async Task<List<ResourceType>> GetResourceTypes([Service] AppDbContext db)
    {
        return await db.ResourceTypes.OrderBy(r => r.Name).ToListAsync();
    }

    /// <summary>
    /// Returns a single resource type identified by its URL slug, together with all product types
    /// that use it as a direct ingredient. Designed for the encyclopedia resource detail view.
    /// Returns null when no resource with the given slug exists.
    /// </summary>
    public async Task<EncyclopediaResourceDetail?> GetEncyclopediaResource(
        string slug,
        [Service] AppDbContext db,
        [Service] IHttpContextAccessor httpContextAccessor)
    {
        var resource = await db.ResourceTypes
            .FirstOrDefaultAsync(r => r.Slug == slug);

        if (resource is null)
        {
            return null;
        }

        var hasActiveProSubscription = false;
        if (httpContextAccessor.HttpContext?.User.Identity?.IsAuthenticated == true)
        {
            var userId = httpContextAccessor.HttpContext.User.GetRequiredUserId();
            var subscriptionEndsAtUtc = await db.Players
                .Where(player => player.Id == userId)
                .Select(player => player.ProSubscriptionEndsAtUtc)
                .FirstOrDefaultAsync();

            hasActiveProSubscription = ProductAccessService.HasActiveProSubscription(subscriptionEndsAtUtc, DateTime.UtcNow);
        }

        var products = await db.ProductTypes
            .Include(p => p.Recipes)
            .ThenInclude(r => r.ResourceType)
            .Include(p => p.Recipes)
            .ThenInclude(r => r.InputProductType)
            .Where(p => p.Recipes.Any(r => r.ResourceTypeId == resource.Id))
            .OrderBy(p => p.Name)
            .ToListAsync();

        ProductAccessService.ApplyAccessMetadata(products, hasActiveProSubscription);

        return new EncyclopediaResourceDetail
        {
            Resource = resource,
            ProductsUsingResource = products,
        };
    }

    /// <summary>Lists all product types, optionally filtered by industry.</summary>
    public async Task<List<ProductType>> GetProductTypes(
        string? industry,
        [Service] AppDbContext db,
        [Service] IHttpContextAccessor httpContextAccessor)
    {
        var hasActiveProSubscription = false;
        if (httpContextAccessor.HttpContext?.User.Identity?.IsAuthenticated == true)
        {
            var userId = httpContextAccessor.HttpContext.User.GetRequiredUserId();
            var subscriptionEndsAtUtc = await db.Players
                .Where(player => player.Id == userId)
                .Select(player => player.ProSubscriptionEndsAtUtc)
                .FirstOrDefaultAsync();

            hasActiveProSubscription = ProductAccessService.HasActiveProSubscription(subscriptionEndsAtUtc, DateTime.UtcNow);
        }

        var query = db.ProductTypes
            .Include(p => p.Recipes)
            .ThenInclude(r => r.ResourceType)
            .Include(p => p.Recipes)
            .ThenInclude(r => r.InputProductType)
            .AsSplitQuery()
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(industry))
        {
            query = query.Where(p => p.Industry == industry);
        }

        var products = await query.OrderBy(p => p.Name).ToListAsync();
        ProductAccessService.ApplyAccessMetadata(products, hasActiveProSubscription);
        return products;
    }

    /// <summary>
    /// Returns ranked product candidates for a unit configuration picker.
    ///
    /// Ranking rules:
    /// <list type="bullet">
    ///   <item>
    ///     <b>PUBLIC_SALES</b> context: products already configured in MANUFACTURING or B2B_SALES
    ///     units within the same building are ranked as <c>connected</c> (score 100) first.
    ///   </item>
    ///   <item>
    ///     <b>STORAGE</b> context: products from MANUFACTURING units in the same building plus
    ///     products currently present in the building's inventory are ranked as <c>connected</c> (score 100).
    ///   </item>
    ///   <item>
    ///     <b>B2B_SALES</b> context: products configured in MANUFACTURING or STORAGE units within
    ///     the same building (including pending configuration) are ranked as <c>connected</c> (score 100).
    ///   </item>
    ///   <item>
    ///     <b>PRODUCT_QUALITY</b> or <b>BRAND_QUALITY</b> context: products used in MANUFACTURING
    ///     units across all buildings owned by the caller's companies are ranked as
    ///     <c>used_by_company</c> (score 50).
    ///   </item>
    ///   <item>All remaining unlocked products fall into the <c>catalog</c> tier (score 10).</item>
    /// </list>
    ///
    /// Within each tier products are sorted alphabetically by name.
    /// </summary>
    [Authorize]
    public async Task<List<RankedProductResult>> GetRankedProductTypes(
        Guid buildingId,
        string unitType,
        [Service] AppDbContext db,
        [Service] IHttpContextAccessor httpContextAccessor)
    {
        var userId = httpContextAccessor.HttpContext!.User.GetRequiredUserId();

        // Determine Pro subscription status for access metadata.
        var subscriptionEndsAtUtc = await db.Players
            .Where(p => p.Id == userId)
            .Select(p => p.ProSubscriptionEndsAtUtc)
            .FirstOrDefaultAsync();
        var hasActivePro = ProductAccessService.HasActiveProSubscription(subscriptionEndsAtUtc, DateTime.UtcNow);

        // Load all product types with recipes.
        var allProducts = await db.ProductTypes
            .Include(p => p.Recipes)
            .ThenInclude(r => r.ResourceType)
            .Include(p => p.Recipes)
            .ThenInclude(r => r.InputProductType)
            .AsSplitQuery()
            .OrderBy(p => p.Name)
            .ToListAsync();

        ProductAccessService.ApplyAccessMetadata(allProducts, hasActivePro);

        // Build a set of "promoted" product IDs and the reason.
        var promotedIds = new Dictionary<Guid, string>(capacity: 16);

        var normalizedUnitType = unitType.ToUpperInvariant();

        if (normalizedUnitType is "PUBLIC_SALES")
        {
            // Promote products already configured in sibling units of the same building.
            var connectedProductIds = await db.BuildingUnits
                .Where(u => u.BuildingId == buildingId
                    && (u.UnitType == "MANUFACTURING" || u.UnitType == "B2B_SALES")
                    && u.ProductTypeId.HasValue)
                .Select(u => u.ProductTypeId!.Value)
                .Distinct()
                .ToListAsync();

            // Also check the pending configuration (draft units).
            var pendingConnectedIds = await db.BuildingConfigurationPlanUnits
                .Where(u => u.BuildingConfigurationPlan.BuildingId == buildingId
                    && (u.UnitType == "MANUFACTURING" || u.UnitType == "B2B_SALES")
                    && u.ProductTypeId.HasValue)
                .Select(u => u.ProductTypeId!.Value)
                .Distinct()
                .ToListAsync();

            foreach (var id in connectedProductIds.Concat(pendingConnectedIds).Distinct())
                promotedIds.TryAdd(id, ProductRankingReason.Connected);
        }
        else if (normalizedUnitType is "STORAGE")
        {
            // Promote products from connected MANUFACTURING units in the same building.
            var mfgProductIds = await db.BuildingUnits
                .Where(u => u.BuildingId == buildingId
                    && u.UnitType == "MANUFACTURING"
                    && u.ProductTypeId.HasValue)
                .Select(u => u.ProductTypeId!.Value)
                .Distinct()
                .ToListAsync();

            // Also check the pending configuration for MANUFACTURING products.
            var pendingMfgIds = await db.BuildingConfigurationPlanUnits
                .Where(u => u.BuildingConfigurationPlan.BuildingId == buildingId
                    && u.UnitType == "MANUFACTURING"
                    && u.ProductTypeId.HasValue)
                .Select(u => u.ProductTypeId!.Value)
                .Distinct()
                .ToListAsync();

            // Promote products already present in the building's inventory.
            var stockProductIds = await db.Inventories
                .Where(i => i.BuildingId == buildingId && i.ProductTypeId.HasValue && i.Quantity > 0)
                .Select(i => i.ProductTypeId!.Value)
                .Distinct()
                .ToListAsync();

            foreach (var id in mfgProductIds.Concat(pendingMfgIds).Concat(stockProductIds).Distinct())
                promotedIds.TryAdd(id, ProductRankingReason.Connected);
        }
        else if (normalizedUnitType is "B2B_SALES")
        {
            // Promote products from MANUFACTURING or STORAGE units in the same building.
            var connectedProductIds = await db.BuildingUnits
                .Where(u => u.BuildingId == buildingId
                    && (u.UnitType == "MANUFACTURING" || u.UnitType == "STORAGE")
                    && u.ProductTypeId.HasValue)
                .Select(u => u.ProductTypeId!.Value)
                .Distinct()
                .ToListAsync();

            // Also check the pending configuration.
            var pendingConnectedIds = await db.BuildingConfigurationPlanUnits
                .Where(u => u.BuildingConfigurationPlan.BuildingId == buildingId
                    && (u.UnitType == "MANUFACTURING" || u.UnitType == "STORAGE")
                    && u.ProductTypeId.HasValue)
                .Select(u => u.ProductTypeId!.Value)
                .Distinct()
                .ToListAsync();

            foreach (var id in connectedProductIds.Concat(pendingConnectedIds).Distinct())
                promotedIds.TryAdd(id, ProductRankingReason.Connected);
        }
        else if (normalizedUnitType is "PRODUCT_QUALITY" or "BRAND_QUALITY")
        {
            // Promote products that this player's company manufactures in any building.
            var companyIds = await db.Companies
                .Where(c => c.PlayerId == userId)
                .Select(c => c.Id)
                .ToListAsync();

            var companyBuildingIds = await db.Buildings
                .Where(b => companyIds.Contains(b.CompanyId))
                .Select(b => b.Id)
                .ToListAsync();

            var usedProductIds = await db.BuildingUnits
                .Where(u => companyBuildingIds.Contains(u.BuildingId)
                    && u.UnitType == "MANUFACTURING"
                    && u.ProductTypeId.HasValue)
                .Select(u => u.ProductTypeId!.Value)
                .Distinct()
                .ToListAsync();

            foreach (var id in usedProductIds)
                promotedIds.TryAdd(id, ProductRankingReason.UsedByCompany);
        }

        // Build ranked results: promoted first (highest score), then catalog.
        return allProducts
            .Select(p =>
            {
                if (promotedIds.TryGetValue(p.Id, out var reason))
                {
                    var score = reason == ProductRankingReason.Connected ? 100 : 50;
                    return new RankedProductResult { ProductType = p, RankingReason = reason, RankingScore = score };
                }
                return new RankedProductResult { ProductType = p, RankingReason = ProductRankingReason.Catalog, RankingScore = 10 };
            })
            .OrderByDescending(r => r.RankingScore)
            .ThenBy(r => r.ProductType.Name)
            .ToList();
    }

}

/// <summary>Payload for player ranking.</summary>
public sealed class PlayerRanking
{
    /// <summary>Player identifier.</summary>
    public Guid PlayerId { get; set; }

    /// <summary>Player display name.</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Total wealth = PersonalCash + SharesValue.
    /// See <see cref="Query.GetRankings"/> for the full valuation formula.
    /// </summary>
    public decimal TotalWealth { get; set; }

    /// <summary>Cash held in the player's personal account.</summary>
    public decimal PersonalCash { get; set; }

    /// <summary>Market value of all shares held by the player's personal account.</summary>
    public decimal SharesValue { get; set; }

    /// <summary>Number of companies owned.</summary>
    public int CompanyCount { get; set; }
}

/// <summary>Individual company ranking for the leaderboard.</summary>
public sealed class CompanyRanking
{
    /// <summary>Company identifier.</summary>
    public Guid CompanyId { get; set; }

    /// <summary>Company display name.</summary>
    public string CompanyName { get; set; } = string.Empty;

    /// <summary>Owner player identifier.</summary>
    public Guid PlayerId { get; set; }

    /// <summary>Owner player display name.</summary>
    public string OwnerDisplayName { get; set; } = string.Empty;

    /// <summary>Total company wealth = Cash + BuildingValue + InventoryValue.</summary>
    public decimal TotalWealth { get; set; }

    /// <summary>Cash on hand for this company.</summary>
    public decimal Cash { get; set; }

    /// <summary>Estimated value of company buildings.</summary>
    public decimal BuildingValue { get; set; }

    /// <summary>Estimated value of inventory in company buildings.</summary>
    public decimal InventoryValue { get; set; }

    /// <summary>Number of buildings owned by this company.</summary>
    public int BuildingCount { get; set; }
}

/// <summary>Payload for starter industries.</summary>
public sealed class StarterIndustriesPayload
{
    /// <summary>Available starter industry values.</summary>
    public List<string> Industries { get; set; } = [];
}

/// <summary>Type values for scheduled actions visible to the player.</summary>
public static class ScheduledActionType
{
    /// <summary>A queued building configuration upgrade (layout/unit change).</summary>
    public const string BuildingUpgrade = "BUILDING_UPGRADE";
}

/// <summary>Summary of a single pending scheduled action for the player.</summary>
public sealed class ScheduledActionSummary
{
    /// <summary>Unique identifier (matches the underlying plan or entity).</summary>
    public Guid Id { get; set; }

    /// <summary>Category of the scheduled action. See <see cref="ScheduledActionType"/>.</summary>
    public string ActionType { get; set; } = string.Empty;

    /// <summary>Building the action belongs to.</summary>
    public Guid BuildingId { get; set; }

    /// <summary>Human-readable building name for display in the UI.</summary>
    public string BuildingName { get; set; } = string.Empty;

    /// <summary>Building type string (e.g. FACTORY, SALES_SHOP).</summary>
    public string BuildingType { get; set; } = string.Empty;

    /// <summary>UTC timestamp when the action was submitted.</summary>
    public DateTime SubmittedAtUtc { get; set; }

    /// <summary>Game tick when the action was submitted.</summary>
    public long SubmittedAtTick { get; set; }

    /// <summary>Game tick when the action is scheduled to apply.</summary>
    public long AppliesAtTick { get; set; }

    /// <summary>Number of ticks remaining until the action applies.</summary>
    public long TicksRemaining { get; set; }

    /// <summary>Total ticks this action required from submission to application.</summary>
    public int TotalTicksRequired { get; set; }
}

/// <summary>Projected supply offer at a city's global exchange.</summary>
public sealed class GlobalExchangeOffer
{
    public Guid CityId { get; set; }
    public string CityName { get; set; } = string.Empty;
    public Guid ResourceTypeId { get; set; }
    public string ResourceName { get; set; } = string.Empty;
    public string ResourceSlug { get; set; } = string.Empty;
    public string UnitSymbol { get; set; } = string.Empty;
    public decimal LocalAbundance { get; set; }
    public decimal ExchangePricePerUnit { get; set; }
    public decimal EstimatedQuality { get; set; }
    public decimal TransitCostPerUnit { get; set; }
    public decimal DeliveredPricePerUnit { get; set; }
    public decimal DistanceKm { get; set; }
}

/// <summary>
/// A product marketplace listing from a player-placed SELL exchange order.
/// Represents a specific offer to sell a manufactured or intermediate product.
/// </summary>
public sealed class GlobalExchangeProductListing
{
    /// <summary>The exchange order ID backing this listing.</summary>
    public Guid OrderId { get; set; }

    /// <summary>The product type being offered.</summary>
    public Guid ProductTypeId { get; set; }

    /// <summary>Human-readable product name.</summary>
    public string ProductName { get; set; } = string.Empty;

    /// <summary>URL-friendly product identifier.</summary>
    public string ProductSlug { get; set; } = string.Empty;

    /// <summary>Industry category: FURNITURE, FOOD_PROCESSING, HEALTHCARE, etc.</summary>
    public string ProductIndustry { get; set; } = string.Empty;

    /// <summary>Short display symbol for the produced unit (e.g. pcs).</summary>
    public string UnitSymbol { get; set; } = string.Empty;

    /// <summary>Display name for the produced unit (e.g. Piece, Crate).</summary>
    public string UnitName { get; set; } = string.Empty;

    /// <summary>Base market price per unit from the product catalogue.</summary>
    public decimal BasePrice { get; set; }

    /// <summary>Asking price per unit for this specific listing.</summary>
    public decimal PricePerUnit { get; set; }

    /// <summary>Remaining quantity available in this order.</summary>
    public decimal RemainingQuantity { get; set; }

    /// <summary>City where the selling exchange building is located.</summary>
    public Guid SellerCityId { get; set; }

    /// <summary>Name of the seller's city.</summary>
    public string SellerCityName { get; set; } = string.Empty;

    /// <summary>Company that placed this sell order.</summary>
    public Guid SellerCompanyId { get; set; }

    /// <summary>Name of the selling company.</summary>
    public string SellerCompanyName { get; set; } = string.Empty;

    /// <summary>When this order was created.</summary>
    public DateTime CreatedAtUtc { get; set; }
}

/// <summary>
/// A single line in the shared in-game chat feed.
/// </summary>
public sealed class InGameChatMessage
{
    public Guid Id { get; set; }
    public Guid PlayerId { get; set; }
    public string PlayerDisplayName { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public DateTime SentAtUtc { get; set; }
    public bool IsOwnMessage { get; set; }
}

/// <summary>Inventory fill information for a single building unit.</summary>
public sealed class BuildingUnitInventorySummary
{
    public Guid BuildingUnitId { get; set; }
    public decimal Quantity { get; set; }
    public decimal Capacity { get; set; }
    public decimal FillPercent { get; set; }
    public decimal? AverageQuality { get; set; }
    public decimal TotalSourcingCost { get; set; }
    public decimal SourcingCostPerUnit { get; set; }
}

/// <summary>
/// City-level power balance snapshot.
/// Computed on demand from current building data.
/// </summary>
public sealed class CityPowerBalance
{
    /// <summary>The city this balance applies to.</summary>
    public Guid CityId { get; set; }

    /// <summary>Total power output in MW from all power plants in the city.</summary>
    public decimal TotalSupplyMw { get; set; }

    /// <summary>Total power demand in MW from all consuming buildings in the city.</summary>
    public decimal TotalDemandMw { get; set; }

    /// <summary>Reserve capacity in MW (supply minus demand; negative means shortage).</summary>
    public decimal ReserveMw { get; set; }

    /// <summary>Reserve as a percentage of demand (negative means shortage).</summary>
    public decimal ReservePercent { get; set; }

    /// <summary>
    /// Overall power status for the city:
    /// BALANCED = supply &gt;= demand,
    /// CONSTRAINED = supply &lt; demand but &gt;= 50%,
    /// CRITICAL = supply &lt; 50% of demand.
    /// </summary>
    public string Status { get; set; } = "BALANCED";

    /// <summary>Summary of each power plant in the city.</summary>
    public List<PowerPlantSummary> PowerPlants { get; set; } = [];

    /// <summary>Number of power plants in the city.</summary>
    public int PowerPlantCount { get; set; }

    /// <summary>Number of consuming buildings in the city.</summary>
    public int ConsumerBuildingCount { get; set; }
}

/// <summary>Summary of a single power plant building for the city power balance view.</summary>
public sealed class PowerPlantSummary
{
    /// <summary>Building identifier.</summary>
    public Guid BuildingId { get; set; }

    /// <summary>Building display name.</summary>
    public string BuildingName { get; set; } = string.Empty;

    /// <summary>Plant type: COAL, GAS, SOLAR, WIND, or NUCLEAR.</summary>
    public string PlantType { get; set; } = string.Empty;

    /// <summary>Current power output in MW.</summary>
    public decimal OutputMw { get; set; }

    /// <summary>Power supply status of this plant (always POWERED).</summary>
    public string PowerStatus { get; set; } = Data.Entities.PowerStatus.Powered;
}

/// <summary>
/// Snapshot of a company brand accumulated by R&amp;D research and marketing spend.
/// Exposed by the companyBrands query so the frontend can render research progress.
/// </summary>
public sealed class ResearchBrandState
{
    /// <summary>Brand identifier.</summary>
    public Guid Id { get; set; }

    /// <summary>Company that owns this brand.</summary>
    public Guid CompanyId { get; set; }

    /// <summary>Display name of the brand (product name or company name).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Scope: PRODUCT, CATEGORY, or COMPANY.</summary>
    public string Scope { get; set; } = BrandScope.Product;

    /// <summary>The specific product type this brand applies to (if scope is PRODUCT or CATEGORY).</summary>
    public Guid? ProductTypeId { get; set; }

    /// <summary>Human-readable product name for this brand (null for company-wide brands).</summary>
    public string? ProductName { get; set; }

    /// <summary>Industry category for CATEGORY-scoped brands.</summary>
    public string? IndustryCategory { get; set; }

    /// <summary>
    /// Brand awareness level (0.0–1.0). Driven by marketing unit spend.
    /// Higher awareness translates to more sales driven by brand recognition.
    /// </summary>
    public decimal Awareness { get; set; }

    /// <summary>
    /// Brand quality level (0.0–1.0). Driven by PRODUCT_QUALITY R&amp;D.
    /// Higher quality improves manufactured product output quality.
    /// </summary>
    public decimal Quality { get; set; }

    /// <summary>
    /// Marketing efficiency multiplier (≥ 1.0). Driven by BRAND_QUALITY R&amp;D.
    /// A value of 1.5 means each unit of marketing budget generates 50% more brand awareness than baseline.
    /// This is NOT a direct brand gain — it only amplifies the effect of marketing spend.
    /// </summary>
    public decimal MarketingEfficiencyMultiplier { get; set; } = 1m;
}

/// <summary>Phase values for the first-sale onboarding mission.</summary>
public static class FirstSaleMissionPhase
{
    /// <summary>Onboarding is not complete or no shop building is being tracked.</summary>
    public const string NoShop = "NO_SHOP";

    /// <summary>Shop exists but has at least one configuration blocker preventing the first sale.</summary>
    public const string ConfigureShop = "CONFIGURE_SHOP";

    /// <summary>Shop is fully configured; waiting for the next simulation tick to record a sale.</summary>
    public const string AwaitingFirstSale = "AWAITING_FIRST_SALE";

    /// <summary>A real PublicSalesRecord with QuantitySold &gt; 0 exists for the onboarding shop.</summary>
    public const string FirstSaleRecorded = "FIRST_SALE_RECORDED";

    /// <summary>The player has already acknowledged the first-sale milestone (OnboardingFirstSaleCompletedAtUtc is set).</summary>
    public const string AlreadyCompleted = "ALREADY_COMPLETED";
}

/// <summary>Blocker codes returned when the first-sale mission phase is CONFIGURE_SHOP.</summary>
public static class FirstSaleMissionBlocker
{
    /// <summary>The sales shop building is still under construction and cannot operate yet.</summary>
    public const string BuildingUnderConstruction = "BUILDING_UNDER_CONSTRUCTION";

    /// <summary>No PUBLIC_SALES unit is present in the shop building.</summary>
    public const string PublicSalesUnitMissing = "PUBLIC_SALES_UNIT_MISSING";

    /// <summary>The PUBLIC_SALES unit does not have a selling price set (MinPrice is null or zero).</summary>
    public const string PriceNotSet = "PRICE_NOT_SET";

    /// <summary>The PUBLIC_SALES unit has no inventory to sell yet (factory has not produced anything).</summary>
    public const string NoInventory = "NO_INVENTORY";
}

/// <summary>
/// Mission-status view model for the post-onboarding first-sale mission.
/// Returned by the <c>firstSaleMission</c> query.
/// </summary>
public sealed class FirstSaleMissionStatus
{
    /// <summary>
    /// Current phase of the first-sale mission.
    /// One of: NO_SHOP, CONFIGURE_SHOP, AWAITING_FIRST_SALE, FIRST_SALE_RECORDED, ALREADY_COMPLETED.
    /// </summary>
    public string Phase { get; set; } = FirstSaleMissionPhase.NoShop;

    /// <summary>The onboarding sales shop building ID being tracked (null when phase is NO_SHOP).</summary>
    public Guid? ShopBuildingId { get; set; }

    /// <summary>Display name of the onboarding sales shop (null when phase is NO_SHOP).</summary>
    public string? ShopName { get; set; }

    /// <summary>
    /// List of blocker codes explaining why the shop is not yet ready.
    /// Only populated when phase is CONFIGURE_SHOP.
    /// See <see cref="FirstSaleMissionBlocker"/> for possible values.
    /// </summary>
    public List<string> Blockers { get; set; } = [];

    /// <summary>Revenue from the first recorded sale (null until phase is FIRST_SALE_RECORDED).</summary>
    public decimal? FirstSaleRevenue { get; set; }

    /// <summary>Name of the product sold in the first sale (null until phase is FIRST_SALE_RECORDED).</summary>
    public string? FirstSaleProductName { get; set; }

    /// <summary>Game tick at which the first sale occurred (null until phase is FIRST_SALE_RECORDED).</summary>
    public long? FirstSaleTick { get; set; }

    /// <summary>Quantity sold in the first sale (null until phase is FIRST_SALE_RECORDED).</summary>
    public decimal? FirstSaleQuantity { get; set; }

    /// <summary>Price per unit in the first sale (null until phase is FIRST_SALE_RECORDED).</summary>
    public decimal? FirstSalePricePerUnit { get; set; }
}

/// <summary>
/// Read model for a media house building in a city.
/// Returned by the <c>cityMediaHouses</c> query.
/// </summary>
public sealed class CityMediaHouseInfo
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid CityId { get; set; }

    /// <summary>Channel type: NEWSPAPER, RADIO, TV. Null if not configured.</summary>
    public string? MediaType { get; set; }

    public Guid OwnerCompanyId { get; set; }
    public string OwnerCompanyName { get; set; } = string.Empty;

    /// <summary>
    /// Awareness multiplier applied when this media house is selected as the campaign channel.
    /// 1.0 = Newspaper, 1.5 = Radio, 2.0 = TV.
    /// </summary>
    public decimal EffectivenessMultiplier { get; set; }

    /// <summary>POWERED, CONSTRAINED, or OFFLINE.</summary>
    public string PowerStatus { get; set; } = Data.Entities.PowerStatus.Powered;

    public bool IsUnderConstruction { get; set; }
}

/// <summary>Read model for a loan offer visible to borrowers or bank owners.</summary>
public sealed class LoanOfferSummary
{
    public Guid Id { get; set; }
    public Guid BankBuildingId { get; set; }
    public string BankBuildingName { get; set; } = string.Empty;
    public Guid CityId { get; set; }
    public string CityName { get; set; } = string.Empty;
    public Guid LenderCompanyId { get; set; }
    public string LenderCompanyName { get; set; } = string.Empty;
    public decimal AnnualInterestRatePercent { get; set; }
    public decimal MaxPrincipalPerLoan { get; set; }
    public decimal TotalCapacity { get; set; }
    public decimal UsedCapacity { get; set; }
    public decimal RemainingCapacity { get; set; }
    public long DurationTicks { get; set; }
    public bool IsActive { get; set; }
    public long CreatedAtTick { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

/// <summary>Read model for an active or historical loan (borrower or lender view).</summary>
public sealed class LoanSummary
{
    public Guid Id { get; set; }
    public Guid LoanOfferId { get; set; }
    public Guid BorrowerCompanyId { get; set; }
    public string BorrowerCompanyName { get; set; } = string.Empty;
    public Guid LenderCompanyId { get; set; }
    public string LenderCompanyName { get; set; } = string.Empty;
    public Guid BankBuildingId { get; set; }
    public string BankBuildingName { get; set; } = string.Empty;
    public decimal OriginalPrincipal { get; set; }
    public decimal RemainingPrincipal { get; set; }
    public decimal AnnualInterestRatePercent { get; set; }
    public long DurationTicks { get; set; }
    public long StartTick { get; set; }
    public long DueTick { get; set; }
    public long NextPaymentTick { get; set; }
    public decimal PaymentAmount { get; set; }
    public int PaymentsMade { get; set; }
    public int TotalPayments { get; set; }
    public string Status { get; set; } = string.Empty;
    public int MissedPayments { get; set; }
    public decimal AccumulatedPenalty { get; set; }
    public DateTime AcceptedAtUtc { get; set; }
    public DateTime? ClosedAtUtc { get; set; }
}

/// <summary>Upgrade info for a single building unit: cost, timing, and stat projections.</summary>
public sealed class UnitUpgradeInfo
{
    public Guid UnitId { get; set; }
    public string UnitType { get; set; } = string.Empty;
    public int CurrentLevel { get; set; }
    public int NextLevel { get; set; }
    public bool IsMaxLevel { get; set; }
    public bool IsUpgradable { get; set; }
    public decimal UpgradeCost { get; set; }
    public int UpgradeTicks { get; set; }
    public decimal CurrentStat { get; set; }
    public decimal NextStat { get; set; }
    public string StatLabel { get; set; } = string.Empty;
}
