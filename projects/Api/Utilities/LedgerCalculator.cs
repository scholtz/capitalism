using Api.Data.Entities;

namespace Api.Utilities;

/// <summary>
/// Centralizes yearly ledger and income-tax calculations.
/// </summary>
public static class LedgerCalculator
{
    private static readonly HashSet<string> DeductibleCategories =
    [
        LedgerCategory.PurchasingCost,
        LedgerCategory.LaborCost,
        LedgerCategory.EnergyCost,
        LedgerCategory.Marketing,
        LedgerCategory.ShippingCost,
        LedgerCategory.Other,
        LedgerCategory.UnitUpgrade,
    ];

    public static decimal GetTotalRevenue(IEnumerable<LedgerEntry> entries)
    {
        return entries
            .Where(entry => entry.Category == LedgerCategory.Revenue)
            .Sum(entry => entry.Amount);
    }

    public static decimal GetTotalPurchasingCosts(IEnumerable<LedgerEntry> entries)
    {
        return Math.Abs(entries
            .Where(entry => entry.Category == LedgerCategory.PurchasingCost)
            .Sum(entry => entry.Amount));
    }

    public static decimal GetTotalMarketingCosts(IEnumerable<LedgerEntry> entries)
    {
        return Math.Abs(entries
            .Where(entry => entry.Category == LedgerCategory.Marketing)
            .Sum(entry => entry.Amount));
    }

    public static decimal GetTotalLaborCosts(IEnumerable<LedgerEntry> entries)
    {
        return Math.Abs(entries
            .Where(entry => entry.Category == LedgerCategory.LaborCost)
            .Sum(entry => entry.Amount));
    }

    public static decimal GetTotalEnergyCosts(IEnumerable<LedgerEntry> entries)
    {
        return Math.Abs(entries
            .Where(entry => entry.Category == LedgerCategory.EnergyCost)
            .Sum(entry => entry.Amount));
    }

    public static decimal GetTotalShippingCosts(IEnumerable<LedgerEntry> entries)
    {
        return Math.Abs(entries
            .Where(entry => entry.Category == LedgerCategory.ShippingCost)
            .Sum(entry => entry.Amount));
    }

    public static decimal GetTotalTaxPaid(IEnumerable<LedgerEntry> entries)
    {
        return Math.Abs(entries
            .Where(entry => entry.Category == LedgerCategory.Tax)
            .Sum(entry => entry.Amount));
    }

    public static decimal GetTotalOtherCosts(IEnumerable<LedgerEntry> entries)
    {
        return Math.Abs(entries
            .Where(entry => entry.Category == LedgerCategory.Other && entry.Amount < 0)
            .Sum(entry => entry.Amount));
    }

    public static decimal GetTotalPropertyPurchases(IEnumerable<LedgerEntry> entries)
    {
        return Math.Abs(entries
            .Where(entry => entry.Category == LedgerCategory.PropertyPurchase)
            .Sum(entry => entry.Amount));
    }

    public static decimal GetTotalStockPurchaseCashOut(IEnumerable<LedgerEntry> entries)
    {
        return Math.Abs(entries
            .Where(entry => entry.Category == LedgerCategory.StockPurchase && entry.Amount < 0m)
            .Sum(entry => entry.Amount));
    }

    public static decimal GetTotalStockSaleCashIn(IEnumerable<LedgerEntry> entries)
    {
        return entries
            .Where(entry => entry.Category == LedgerCategory.StockSale && entry.Amount > 0m)
            .Sum(entry => entry.Amount);
    }

    public static decimal ComputeTaxableIncome(IEnumerable<LedgerEntry> entries)
    {
        var ledgerEntries = entries.ToList();
        var revenue = GetTotalRevenue(ledgerEntries);
        var deductibleCosts = Math.Abs(ledgerEntries
            .Where(entry => DeductibleCategories.Contains(entry.Category) && entry.Amount < 0m)
            .Sum(entry => entry.Amount));

        return Math.Max(revenue - deductibleCosts, 0m);
    }
}
