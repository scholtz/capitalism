using System.ComponentModel.DataAnnotations;

namespace Api.Data.Entities;

/// <summary>
/// A loan offer published by a bank building. Borrowers can browse active offers
/// and accept them to receive immediate cash in exchange for scheduled repayments.
/// </summary>
public sealed class LoanOffer
{
    public Guid Id { get; set; }

    /// <summary>The bank building that published this offer.</summary>
    public Guid BankBuildingId { get; set; }

    /// <summary>Navigation property to the bank building.</summary>
    public Building BankBuilding { get; set; } = null!;

    /// <summary>The company that owns the bank building.</summary>
    public Guid LenderCompanyId { get; set; }

    /// <summary>Navigation property to the lender company.</summary>
    public Company LenderCompany { get; set; } = null!;

    /// <summary>Annual interest rate as a percentage (e.g. 12.5 = 12.5%).</summary>
    public decimal AnnualInterestRatePercent { get; set; }

    /// <summary>Maximum principal that a single borrower can take from this offer.</summary>
    public decimal MaxPrincipalPerLoan { get; set; }

    /// <summary>Total capital the bank commits to this offer across all borrowers.</summary>
    public decimal TotalCapacity { get; set; }

    /// <summary>How much of TotalCapacity has already been lent out.</summary>
    public decimal UsedCapacity { get; set; }

    /// <summary>Repayment duration in ticks.</summary>
    public long DurationTicks { get; set; }

    /// <summary>Whether this offer is currently visible to borrowers.</summary>
    public bool IsActive { get; set; }

    /// <summary>Tick when this offer was published.</summary>
    public long CreatedAtTick { get; set; }

    /// <summary>UTC timestamp when this offer was published.</summary>
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Active loans issued against this offer.</summary>
    public ICollection<Loan> Loans { get; set; } = new List<Loan>();
}

/// <summary>
/// Status values for a loan offer.
/// </summary>
public static class LoanOfferStatus
{
    public const string Active = "ACTIVE";
    public const string Inactive = "INACTIVE";
    public const string Exhausted = "EXHAUSTED";
}
