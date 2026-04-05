using System.ComponentModel.DataAnnotations;
namespace Api.Data.Entities;

/// <summary>Records a single financial event for a company.</summary>
public sealed class LedgerEntry
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Company Company { get; set; } = null!;
    public Guid? BuildingId { get; set; }
    public Building? Building { get; set; }
    public Guid? BuildingUnitId { get; set; }
    public BuildingUnit? BuildingUnit { get; set; }

    /// <summary>Category: REVENUE, PURCHASING_COST, PROPERTY_PURCHASE, UNIT_UPGRADE, MARKETING, TAX, OTHER</summary>
    [Required, MaxLength(40)]
    public string Category { get; set; } = string.Empty;

    [MaxLength(500)]
    public string Description { get; set; } = string.Empty;

    /// <summary>Positive = income, negative = expense.</summary>
    public decimal Amount { get; set; }

    public long RecordedAtTick { get; set; }
    public DateTime RecordedAtUtc { get; set; } = DateTime.UtcNow;

    public Guid? ProductTypeId { get; set; }
    public ProductType? ProductType { get; set; }
    public Guid? ResourceTypeId { get; set; }
    public ResourceType? ResourceType { get; set; }
}

public static class LedgerCategory
{
    public const string Revenue = "REVENUE";
    public const string PurchasingCost = "PURCHASING_COST";
    public const string LaborCost = "LABOR_COST";
    public const string EnergyCost = "ENERGY_COST";
    public const string PropertyPurchase = "PROPERTY_PURCHASE";
    public const string ConstructionCost = "CONSTRUCTION_COST";
    public const string UnitUpgrade = "UNIT_UPGRADE";
    public const string Marketing = "MARKETING";
    public const string MediaHouseIncome = "MEDIA_HOUSE_INCOME";
    public const string Tax = "TAX";
    public const string RentIncome = "RENT_INCOME";
    public const string Other = "OTHER";
    public const string LoanOrigination = "LOAN_ORIGINATION";
    public const string LoanRepaymentPrincipal = "LOAN_REPAYMENT_PRINCIPAL";
    public const string LoanInterestExpense = "LOAN_INTEREST_EXPENSE";
    public const string LoanInterestIncome = "LOAN_INTEREST_INCOME";
    public const string LoanPenalty = "LOAN_PENALTY";
}
