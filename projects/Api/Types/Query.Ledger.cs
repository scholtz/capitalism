using Api.Data;
using Api.Data.Entities;
using Api.Engine;
using Api.Security;
using Api.Utilities;
using HotChocolate.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Api.Types;

public sealed partial class Query
{
    /// <summary>Returns a financial ledger summary for a company (requires auth — must own company).</summary>
    [Authorize]
    public async Task<CompanyLedgerSummary?> GetCompanyLedger(
        Guid companyId,
        int? gameYear,
        [Service] AppDbContext db,
        [Service] IHttpContextAccessor httpContextAccessor)
    {
        var userId = httpContextAccessor.HttpContext!.User.GetRequiredUserId();

        var company = await db.Companies
            .Include(c => c.Buildings)
            .FirstOrDefaultAsync(c => c.Id == companyId && c.PlayerId == userId);

        if (company is null) return null;

        var gameState = await db.GameStates
            .AsNoTracking()
            .FirstOrDefaultAsync();
        var currentTick = gameState?.CurrentTick ?? 0L;
        var currentGameYear = GameTime.GetGameYear(currentTick);
        var selectedGameYear = gameYear ?? currentGameYear;

        var allEntries = await db.LedgerEntries
            .Where(e => e.CompanyId == companyId)
            .ToListAsync();

        var entries = allEntries
            .Where(entry => IsTickInGameYear(entry.RecordedAtTick, selectedGameYear))
            .ToList();

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
        var estimatedIncomeTax = selectedGameYear == currentGameYear
            ? GameTime.ComputeEstimatedIncomeTax(taxableIncome, gameState?.TaxRate ?? 0m)
            : totalTaxPaid;

        var ownedLots = await db.BuildingLots
            .Where(lot => lot.OwnerCompanyId == companyId)
            .ToListAsync();

        var propertyValue = ownedLots.Sum(WealthCalculator.GetLandValue);
        var buildingValue = company.Buildings.Sum(b => WealthCalculator.GetBuildingValue(b));

        var buildingIds = company.Buildings.Select(b => b.Id).ToList();
        var inventories = await db.Inventories
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

        var history = allEntries
            .GroupBy(entry => GameTime.GetGameYear(entry.RecordedAtTick))
            .Select(group => BuildCompanyLedgerHistoryYear(group.Key, currentGameYear, gameState?.TaxRate ?? 0m, group.ToList()))
            .ToDictionary(item => item.GameYear);

        if (!history.ContainsKey(currentGameYear))
        {
            history[currentGameYear] = BuildCompanyLedgerHistoryYear(currentGameYear, currentGameYear, gameState?.TaxRate ?? 0m, []);
        }

        var incomeTaxDueAtTick = GameTime.GetIncomeTaxDueTickForGameYear(selectedGameYear);

        return new CompanyLedgerSummary
        {
            CompanyId = company.Id,
            CompanyName = company.Name,
            GameYear = selectedGameYear,
            IsCurrentGameYear = selectedGameYear == currentGameYear,
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

        var entriesQuery = db.LedgerEntries
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
        var totalRevenue = LedgerCalculator.GetTotalRevenue(entries);
        var totalPurchasingCosts = LedgerCalculator.GetTotalPurchasingCosts(entries);
        var totalShippingCosts = LedgerCalculator.GetTotalShippingCosts(entries);
        var totalLaborCosts = LedgerCalculator.GetTotalLaborCosts(entries);
        var totalEnergyCosts = LedgerCalculator.GetTotalEnergyCosts(entries);
        var totalMarketingCosts = LedgerCalculator.GetTotalMarketingCosts(entries);
        var totalTaxPaid = LedgerCalculator.GetTotalTaxPaid(entries);
        var totalOtherCosts = LedgerCalculator.GetTotalOtherCosts(entries);
        var taxableIncome = LedgerCalculator.ComputeTaxableIncome(entries);
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
            FirstRecordedTick = entries.Count > 0 ? entries.Min(entry => entry.RecordedAtTick) : 0,
            LastRecordedTick = entries.Count > 0 ? entries.Max(entry => entry.RecordedAtTick) : 0,
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
