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

    [Authorize]
    public async Task<OperationsStatisticsResult> GetOperationsStatistics(
        [Service] AppDbContext db,
        [Service] IHttpContextAccessor httpContextAccessor,
        [Service] GameAdminAuthorizationService gameAdminAuthorizationService)
    {
        var principal = httpContextAccessor.HttpContext!.User;
        await gameAdminAuthorizationService.RequireAdminDashboardAccessAsync(db, principal, httpContextAccessor.HttpContext!.RequestAborted);

        var currentTick = await db.GameStates
            .AsNoTracking()
            .Select(state => state.CurrentTick)
            .FirstOrDefaultAsync(httpContextAccessor.HttpContext.RequestAborted);

        var recentLedgerEntries = await db.LedgerEntries
            .AsNoTracking()
            .Where(entry => entry.RecordedAtTick >= Math.Max(0, currentTick - 100))
            .ToListAsync(httpContextAccessor.HttpContext.RequestAborted);

        return new OperationsStatisticsResult
        {
            IncomeItems = BuildOperationsIncomeItems(recentLedgerEntries),
            ExpenseItems = BuildOperationsExpenseItems(recentLedgerEntries),
        };
    }

    [Authorize]
    public async Task<List<AdminProductAnalyticsRow>> GetAdminProductAnalytics(
        [Service] AppDbContext db,
        [Service] IHttpContextAccessor httpContextAccessor,
        [Service] GameAdminAuthorizationService gameAdminAuthorizationService)
    {
        var principal = httpContextAccessor.HttpContext!.User;
        await gameAdminAuthorizationService.RequireAdminDashboardAccessAsync(db, principal, httpContextAccessor.HttpContext!.RequestAborted);

        var currentTick = await db.GameStates
            .AsNoTracking()
            .Select(state => state.CurrentTick)
            .FirstOrDefaultAsync(httpContextAccessor.HttpContext.RequestAborted);
        var windowStart = Math.Max(0, currentTick - 100);

        var productTypes = await db.ProductTypes
            .AsNoTracking()
            .Select(product => new { product.Id, product.Name })
            .ToListAsync(httpContextAccessor.HttpContext.RequestAborted);

        var producedByProductId = await db.BuildingUnitResourceHistories
            .AsNoTracking()
            .Where(history => history.Tick >= windowStart && history.ProductTypeId.HasValue)
            .GroupBy(history => history.ProductTypeId!.Value)
            .Select(group => new { ProductTypeId = group.Key, Quantity = group.Sum(history => history.ProducedQuantity) })
            .ToDictionaryAsync(row => row.ProductTypeId, row => row.Quantity, httpContextAccessor.HttpContext.RequestAborted);

        var salesByProductId = await db.PublicSalesRecords
            .AsNoTracking()
            .Where(record => record.Tick >= windowStart && record.ProductTypeId.HasValue)
            .GroupBy(record => record.ProductTypeId!.Value)
            .Select(group => new
            {
                ProductTypeId = group.Key,
                UnitsSold = group.Sum(record => record.QuantitySold),
                MarketSize = group.Sum(record => record.Demand),
            })
            .ToListAsync(httpContextAccessor.HttpContext.RequestAborted);
        var soldByProductId = salesByProductId.ToDictionary(row => row.ProductTypeId, row => row.UnitsSold);
        var marketSizeByProductId = salesByProductId.ToDictionary(row => row.ProductTypeId, row => row.MarketSize);

        var costs = await db.LedgerEntries
            .AsNoTracking()
            .Where(entry => entry.RecordedAtTick >= windowStart && entry.ProductTypeId.HasValue)
            .Where(entry => entry.Amount < 0m)
            .Where(entry => entry.Category == LedgerCategory.PurchasingCost
                            || entry.Category == LedgerCategory.LaborCost
                            || entry.Category == LedgerCategory.EnergyCost)
            .GroupBy(entry => new { ProductTypeId = entry.ProductTypeId!.Value, entry.Category })
            .Select(group => new
            {
                group.Key.ProductTypeId,
                group.Key.Category,
                Amount = Math.Abs(group.Sum(entry => entry.Amount)),
            })
            .ToListAsync(httpContextAccessor.HttpContext.RequestAborted);

        var materialCostByProductId = costs
            .Where(cost => cost.Category == LedgerCategory.PurchasingCost)
            .GroupBy(cost => cost.ProductTypeId)
            .ToDictionary(group => group.Key, group => group.Sum(cost => cost.Amount));
        var laborCostByProductId = costs
            .Where(cost => cost.Category == LedgerCategory.LaborCost)
            .GroupBy(cost => cost.ProductTypeId)
            .ToDictionary(group => group.Key, group => group.Sum(cost => cost.Amount));
        var energyCostByProductId = costs
            .Where(cost => cost.Category == LedgerCategory.EnergyCost)
            .GroupBy(cost => cost.ProductTypeId)
            .ToDictionary(group => group.Key, group => group.Sum(cost => cost.Amount));

        var marketingSpendByProductId = await db.BuildingUnits
            .AsNoTracking()
            .Where(unit => unit.UnitType == UnitType.Marketing && unit.ProductTypeId.HasValue)
            .GroupBy(unit => unit.ProductTypeId!.Value)
            .Select(group => new
            {
                ProductTypeId = group.Key,
                Spend = group.Sum(unit => unit.Budget ?? 0m),
            })
            .ToDictionaryAsync(row => row.ProductTypeId, row => row.Spend, httpContextAccessor.HttpContext.RequestAborted);

        var researchQualityByProductId = await db.Brands
            .AsNoTracking()
            .Where(brand => brand.ProductTypeId.HasValue)
            .GroupBy(brand => brand.ProductTypeId!.Value)
            .Select(group => new
            {
                ProductTypeId = group.Key,
                Quality = group.Average(brand => brand.Quality),
            })
            .ToDictionaryAsync(row => row.ProductTypeId, row => decimal.Round(row.Quality, 4, MidpointRounding.AwayFromZero), httpContextAccessor.HttpContext.RequestAborted);

        return productTypes
            .Select(product =>
            {
                var unitsSold = soldByProductId.GetValueOrDefault(product.Id, 0m);
                var marketSize = marketSizeByProductId.GetValueOrDefault(product.Id, 0m);
                var saturation = marketSize > 0m
                    ? decimal.Round(Math.Clamp((unitsSold / marketSize) * 100m, 0m, 100m), 2, MidpointRounding.AwayFromZero)
                    : 0m;

                return new AdminProductAnalyticsRow
                {
                    ProductTypeId = product.Id,
                    ProductName = product.Name,
                    MaterialCost = materialCostByProductId.GetValueOrDefault(product.Id, 0m),
                    EnergyCost = energyCostByProductId.GetValueOrDefault(product.Id, 0m),
                    LaborCost = laborCostByProductId.GetValueOrDefault(product.Id, 0m),
                    UnitsProduced = producedByProductId.GetValueOrDefault(product.Id, 0m),
                    UnitsSold = unitsSold,
                    MarketSize = marketSize,
                    MarketSaturationPercent = saturation,
                    CurrentMarketingSpend = marketingSpendByProductId.GetValueOrDefault(product.Id, 0m),
                    ResearchQualityLevel = researchQualityByProductId.GetValueOrDefault(product.Id, 0m),
                };
            })
            .OrderBy(row => row.ProductName)
            .ToList();
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

    private static List<OperationsMoneyFlowItem> BuildOperationsIncomeItems(IReadOnlyCollection<LedgerEntry> recentLedgerEntries)
    {
        return BuildOperationsMoneyFlowItems(
            recentLedgerEntries,
            positive: true,
            [
                (LedgerCategory.Revenue, "Public sales", "Cash earned from goods sold into public markets."),
                (LedgerCategory.RentIncome, "Apartment rent", "Lease payments collected from city properties."),
                (LedgerCategory.StockSale, "IPO proceeds", "Cash raised by selling shares on the stock exchange."),
                (LedgerCategory.MediaHouseIncome, "AMM fees", "Media and market-maker style fee income."),
                (LedgerCategory.LoanOrigination, "Other player earnings", "Additional inflows such as financing and cross-player settlements."),
            ]);
    }

    private static List<OperationsMoneyFlowItem> BuildOperationsExpenseItems(IReadOnlyCollection<LedgerEntry> recentLedgerEntries)
    {
        return BuildOperationsMoneyFlowItems(
            recentLedgerEntries,
            positive: false,
            [
                (LedgerCategory.Tax, "Taxes", "Tax obligations paid by companies."),
                (LedgerCategory.StockPurchase, "FX fees", "Capital outflow for stock purchases and exchange exposure."),
                (LedgerCategory.LaborCost, "Labour costs", "Wages paid to keep business units staffed."),
                (LedgerCategory.EnergyCost, "Energy", "Electricity and utility expenses."),
                (LedgerCategory.UnitUpgrade, "Research costs", "R&D and upgrade spending on company operations."),
                (LedgerCategory.ShippingCost, "Stock exchange fees", "Logistics and market transfer fees."),
            ]);
    }

    private static List<OperationsMoneyFlowItem> BuildOperationsMoneyFlowItems(
        IReadOnlyCollection<LedgerEntry> recentLedgerEntries,
        bool positive,
        IReadOnlyCollection<(string Category, string Label, string Description)> definitions)
    {
        return definitions
            .Select(definition =>
            {
                var amount = recentLedgerEntries
                    .Where(entry => entry.Category == definition.Category)
                    .Where(entry => positive ? entry.Amount > 0m : entry.Amount < 0m)
                    .Sum(entry => Math.Abs(entry.Amount));

                return new OperationsMoneyFlowItem
                {
                    Category = definition.Label,
                    Amount = amount,
                    Description = definition.Description,
                };
            })
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
}
