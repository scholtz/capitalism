using Api.Data;
using Api.Data.Entities;
using Api.Engine;
using Api.Security;
using Api.Utilities;
using HotChocolate.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Api.Types;

public sealed partial class Query
{
    private static readonly TimeSpan LedgerHistoryCacheDuration = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan LedgerCurrentYearCacheDuration = TimeSpan.FromSeconds(10);

    /// <summary>Returns a financial ledger summary for a company (requires auth — must own company).</summary>
    [Authorize]
    public async Task<CompanyLedgerSummary?> GetCompanyLedger(
        Guid companyId,
        int? gameYear,
        [Service] AppDbContext db,
        [Service] IHttpContextAccessor httpContextAccessor,
        [Service] IMemoryCache cache)
    {
        var userId = httpContextAccessor.HttpContext!.User.GetRequiredUserId();

        var company = await db.Companies
            .AsNoTracking()
            .Include(c => c.Buildings)
            .FirstOrDefaultAsync(c => c.Id == companyId && c.PlayerId == userId);

        if (company is null) return null;

        var gameState = await db.GameStates
            .AsNoTracking()
            .FirstOrDefaultAsync();
        var currentTick = gameState?.CurrentTick ?? 0L;
        var currentGameYear = GameTime.GetGameYear(currentTick);
        var selectedGameYear = gameYear ?? currentGameYear;
        var isCurrentYear = selectedGameYear == currentGameYear;

        // Check cache — historical (non-current) years are immutable so can be cached longer.
        var cacheKey = $"ledger_{companyId}_{selectedGameYear}";
        var cacheDuration = isCurrentYear ? LedgerCurrentYearCacheDuration : LedgerHistoryCacheDuration;
        if (cache.TryGetValue(cacheKey, out CompanyLedgerSummary? cachedSummary) && cachedSummary is not null)
        {
            return cachedSummary;
        }

        // Fetch only entries for the selected year — uses the (CompanyId, RecordedAtTick) index.
        var startTick = GameTime.GetStartTickForGameYear(selectedGameYear);
        var endTick = GameTime.GetEndTickForGameYear(selectedGameYear);

        var entries = await db.LedgerEntries
            .AsNoTracking()
            .Where(e => e.CompanyId == companyId && e.RecordedAtTick >= startTick && e.RecordedAtTick <= endTick)
            .ToListAsync();

        // For history years we only need lightweight aggregates: tick + category + amount.
        // Load all entries but project only the columns needed for grouping.
        var historyProjection = await db.LedgerEntries
            .AsNoTracking()
            .Where(e => e.CompanyId == companyId)
            .Select(e => new { e.RecordedAtTick, e.Category, e.Amount })
            .ToListAsync();

        var totalRevenue = LedgerCalculator.GetTotalRevenue(entries);
        var totalPurchasingCosts = LedgerCalculator.GetTotalPurchasingCosts(entries);
        var totalShippingCosts = LedgerCalculator.GetTotalShippingCosts(entries);
        var totalLaborCosts = LedgerCalculator.GetTotalLaborCosts(entries);
        var totalEnergyCosts = LedgerCalculator.GetTotalEnergyCosts(entries);
        var totalMarketingCosts = LedgerCalculator.GetTotalMarketingCosts(entries);
        var totalTaxPaid = LedgerCalculator.GetTotalTaxPaid(entries);
        var totalOtherCosts = LedgerCalculator.GetTotalOtherCosts(entries);
        var totalPropertyPurchases = LedgerCalculator.GetTotalPropertyPurchases(entries);
        var totalStockPurchaseCashOut = LedgerCalculator.GetTotalStockPurchaseCashOut(entries);
        var totalStockSaleCashIn = LedgerCalculator.GetTotalStockSaleCashIn(entries);
        var taxableIncome = LedgerCalculator.ComputeTaxableIncome(entries);
        var estimatedIncomeTax = isCurrentYear
            ? GameTime.ComputeEstimatedIncomeTax(taxableIncome, gameState?.TaxRate ?? 0m)
            : totalTaxPaid;

        var ownedLots = await db.BuildingLots
            .AsNoTracking()
            .Where(lot => lot.OwnerCompanyId == companyId)
            .ToListAsync();

        var propertyValue = ownedLots.Sum(WealthCalculator.GetLandValue);
        var buildingValue = company.Buildings.Sum(b => WealthCalculator.GetBuildingValue(b));

        var buildingIds = company.Buildings.Select(b => b.Id).ToList();
        var inventories = await db.Inventories
            .AsNoTracking()
            .Where(i => buildingIds.Contains(i.BuildingId))
            .Include(i => i.ResourceType)
            .Include(i => i.ProductType)
            .ToListAsync();
        var inventoryValue = inventories.Sum(i => i.Quantity * WealthCalculator.GetItemBasePrice(i));

        var buildingSummaries = entries
            .Where(e => e.BuildingId.HasValue)
            .GroupBy(e => e.BuildingId!.Value)
            .Select(g =>
            {
                var b = company.Buildings.FirstOrDefault(bld => bld.Id == g.Key);
                return new BuildingLedgerSummary
                {
                    BuildingId = g.Key,
                    BuildingName = b?.Name ?? string.Empty,
                    BuildingType = b?.Type ?? string.Empty,
                    Revenue = g.Where(e => e.Amount > 0).Sum(e => e.Amount),
                    Costs = Math.Abs(g.Where(e => e.Amount < 0).Sum(e => e.Amount)),
                };
            })
            .ToList();

        // Build history from the lightweight projection — no in-memory entity hydration needed.
        var history = historyProjection
            .GroupBy(e => GameTime.GetGameYear(e.RecordedAtTick))
            .Select(group => BuildCompanyLedgerHistoryYearFromProjection(
                group.Key, currentGameYear, gameState?.TaxRate ?? 0m,
                group.Select(e => (e.RecordedAtTick, e.Category, e.Amount)).ToList()))
            .ToDictionary(item => item.GameYear);

        if (!history.ContainsKey(currentGameYear))
        {
            history[currentGameYear] = BuildCompanyLedgerHistoryYearFromProjection(currentGameYear, currentGameYear, gameState?.TaxRate ?? 0m, []);
        }

        var incomeTaxDueAtTick = GameTime.GetIncomeTaxDueTickForGameYear(selectedGameYear);

        var summary = new CompanyLedgerSummary
        {
            CompanyId = company.Id,
            CompanyName = company.Name,
            GameYear = selectedGameYear,
            IsCurrentGameYear = isCurrentYear,
            CurrentCash = company.Cash,
            TotalRevenue = totalRevenue,
            TotalPurchasingCosts = totalPurchasingCosts,
            TotalShippingCosts = totalShippingCosts,
            TotalLaborCosts = totalLaborCosts,
            TotalEnergyCosts = totalEnergyCosts,
            TotalMarketingCosts = totalMarketingCosts,
            TotalTaxPaid = totalTaxPaid,
            TotalOtherCosts = totalOtherCosts,
            TaxableIncome = taxableIncome,
            EstimatedIncomeTax = estimatedIncomeTax,
            TotalPropertyPurchases = totalPropertyPurchases,
            TotalStockPurchaseCashOut = totalStockPurchaseCashOut,
            TotalStockSaleCashIn = totalStockSaleCashIn,
            NetIncome = totalRevenue - totalPurchasingCosts - totalShippingCosts - totalLaborCosts - totalEnergyCosts - totalMarketingCosts - totalTaxPaid - totalOtherCosts,
            PropertyValue = propertyValue,
            PropertyAppreciation = propertyValue - totalPropertyPurchases,
            BuildingValue = buildingValue,
            InventoryValue = inventoryValue,
            TotalAssets = company.Cash + propertyValue + buildingValue + inventoryValue,
            CashFromOperations = totalRevenue - totalPurchasingCosts - totalShippingCosts - totalLaborCosts - totalEnergyCosts - totalMarketingCosts,
            CashFromInvestments = totalStockSaleCashIn - totalPropertyPurchases - totalStockPurchaseCashOut,
            FirstRecordedTick = entries.Count > 0 ? entries.Min(e => e.RecordedAtTick) : 0,
            LastRecordedTick = entries.Count > 0 ? entries.Max(e => e.RecordedAtTick) : 0,
            BuildingSummaries = buildingSummaries,
            IncomeTaxDueAtTick = incomeTaxDueAtTick,
            IncomeTaxDueGameTimeUtc = GameTime.GetInGameTimeUtc(incomeTaxDueAtTick),
            IncomeTaxDueGameYear = GameTime.GetGameYear(incomeTaxDueAtTick),
            IsIncomeTaxSettled = selectedGameYear < currentGameYear,
            History = history.Values
                .OrderByDescending(item => item.GameYear)
                .ToList(),
        };

        cache.Set(cacheKey, summary, cacheDuration);
        return summary;
    }

    /// <summary>Returns drill-down entries for a specific ledger category.</summary>
    [Authorize]
    public async Task<List<LedgerEntryResult>> GetLedgerDrillDown(
        Guid companyId,
        string category,
        int? gameYear,
        [Service] AppDbContext db,
        [Service] IHttpContextAccessor httpContextAccessor)
    {
        var userId = httpContextAccessor.HttpContext!.User.GetRequiredUserId();

        var ownsCompany = await db.Companies
            .AnyAsync(c => c.Id == companyId && c.PlayerId == userId);

        if (!ownsCompany) return [];

        // Uses the compound (CompanyId, Category, RecordedAtTick) index.
        var entriesQuery = db.LedgerEntries
            .AsNoTracking()
            .Where(e => e.CompanyId == companyId && e.Category == category);

        if (gameYear.HasValue)
        {
            var startTick = GameTime.GetStartTickForGameYear(gameYear.Value);
            var endTick = GameTime.GetEndTickForGameYear(gameYear.Value);
            entriesQuery = entriesQuery.Where(e => e.RecordedAtTick >= startTick && e.RecordedAtTick <= endTick);
        }

        var entries = await entriesQuery
            .OrderByDescending(e => e.RecordedAtTick)
            .Take(200)
            .Include(e => e.Building)
            .Include(e => e.ProductType)
            .Include(e => e.ResourceType)
            .ToListAsync();

        return entries.Select(e => new LedgerEntryResult
        {
            Id = e.Id,
            Category = e.Category,
            Description = e.Description,
            Amount = e.Amount,
            RecordedAtTick = e.RecordedAtTick,
            RecordedAtUtc = e.RecordedAtUtc,
            BuildingId = e.BuildingId,
            BuildingName = e.Building?.Name,
            BuildingUnitId = e.BuildingUnitId,
            ProductTypeId = e.ProductTypeId,
            ProductName = e.ProductType?.Name,
            ResourceTypeId = e.ResourceTypeId,
            ResourceName = e.ResourceType?.Name,
        }).ToList();
    }

    private static bool IsTickInGameYear(long tick, int gameYear)
    {
        var startTick = GameTime.GetStartTickForGameYear(gameYear);
        var endTick = GameTime.GetEndTickForGameYear(gameYear);
        return tick >= startTick && tick <= endTick;
    }

    private static CompanyLedgerHistoryYear BuildCompanyLedgerHistoryYear(
        int gameYear,
        int currentGameYear,
        decimal taxRate,
        List<LedgerEntry> entries)
    {
        var projections = entries.Select(e => (e.RecordedAtTick, e.Category, e.Amount)).ToList();
        return BuildCompanyLedgerHistoryYearFromProjection(gameYear, currentGameYear, taxRate, projections);
    }

    /// <summary>
    /// Builds a ledger history year summary from lightweight (RecordedAtTick, Category, Amount) projections,
    /// avoiding the need to hydrate full LedgerEntry entities for historical data.
    /// </summary>
    private static CompanyLedgerHistoryYear BuildCompanyLedgerHistoryYearFromProjection(
        int gameYear,
        int currentGameYear,
        decimal taxRate,
        List<(long RecordedAtTick, string Category, decimal Amount)> projections)
    {
        var totalRevenue = projections.Where(e => e.Category == LedgerCategory.Revenue && e.Amount > 0).Sum(e => e.Amount);
        var totalPurchasingCosts = Math.Abs(projections.Where(e => e.Category == LedgerCategory.PurchasingCost && e.Amount < 0).Sum(e => e.Amount));
        var totalShippingCosts = Math.Abs(projections.Where(e => e.Category == LedgerCategory.ShippingCost && e.Amount < 0).Sum(e => e.Amount));
        var totalLaborCosts = Math.Abs(projections.Where(e => e.Category == LedgerCategory.LaborCost && e.Amount < 0).Sum(e => e.Amount));
        var totalEnergyCosts = Math.Abs(projections.Where(e => e.Category == LedgerCategory.EnergyCost && e.Amount < 0).Sum(e => e.Amount));
        var totalMarketingCosts = Math.Abs(projections.Where(e => e.Category == LedgerCategory.Marketing && e.Amount < 0).Sum(e => e.Amount));
        var totalTaxPaid = Math.Abs(projections.Where(e => e.Category == LedgerCategory.Tax && e.Amount < 0).Sum(e => e.Amount));
        var totalOtherCosts = Math.Abs(projections.Where(e => e.Category == LedgerCategory.Other && e.Amount < 0).Sum(e => e.Amount));
        // Taxable income = revenue minus all deductible operating costs (excluding tax itself).
        var taxableIncome = Math.Max(totalRevenue - totalPurchasingCosts - totalShippingCosts - totalLaborCosts - totalEnergyCosts - totalMarketingCosts, 0m);
        var estimatedIncomeTax = gameYear == currentGameYear
            ? GameTime.ComputeEstimatedIncomeTax(taxableIncome, taxRate)
            : totalTaxPaid;

        return new CompanyLedgerHistoryYear
        {
            GameYear = gameYear,
            IsCurrentGameYear = gameYear == currentGameYear,
            TotalRevenue = totalRevenue,
            TotalLaborCosts = totalLaborCosts,
            TotalEnergyCosts = totalEnergyCosts,
            NetIncome = totalRevenue - totalPurchasingCosts - totalShippingCosts - totalLaborCosts - totalEnergyCosts - totalMarketingCosts - totalTaxPaid - totalOtherCosts,
            TotalTaxPaid = totalTaxPaid,
            TaxableIncome = taxableIncome,
            EstimatedIncomeTax = estimatedIncomeTax,
            FirstRecordedTick = projections.Count > 0 ? projections.Min(e => e.RecordedAtTick) : 0,
            LastRecordedTick = projections.Count > 0 ? projections.Max(e => e.RecordedAtTick) : 0,
        };
    }

    private static decimal ComputeCompanyAssetValue(
        Company company,
        IReadOnlyCollection<BuildingLot> allOwnedLots,
        IReadOnlyCollection<Inventory> allInventories)
    {
        var buildingIds = company.Buildings.Select(building => building.Id).ToHashSet();
        var inventoryValue = allInventories
            .Where(inventory => buildingIds.Contains(inventory.BuildingId))
            .Sum(WealthCalculator.GetItemBasePrice);
        var lotValue = allOwnedLots
            .Where(lot => lot.OwnerCompanyId == company.Id)
            .Sum(WealthCalculator.GetLandValue);

        return company.Cash + company.Buildings.Sum(WealthCalculator.GetBuildingValue) + lotValue + allInventories
            .Where(inventory => buildingIds.Contains(inventory.BuildingId))
            .Sum(inventory => inventory.Quantity * WealthCalculator.GetItemBasePrice(inventory));
    }
}
