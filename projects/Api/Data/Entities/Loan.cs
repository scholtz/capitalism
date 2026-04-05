using System.ComponentModel.DataAnnotations;

namespace Api.Data.Entities;

/// <summary>
/// An active loan: a borrower has accepted a loan offer and cash has been transferred.
/// The loan tracks the remaining principal and schedules repayments through the tick engine.
/// </summary>
public sealed class Loan
{
    public Guid Id { get; set; }

    /// <summary>The offer that this loan was accepted from.</summary>
    public Guid LoanOfferId { get; set; }

    /// <summary>Navigation property to the loan offer.</summary>
    public LoanOffer LoanOffer { get; set; } = null!;

    /// <summary>The company that borrowed the money.</summary>
    public Guid BorrowerCompanyId { get; set; }

    /// <summary>Navigation property to the borrower company.</summary>
    public Company BorrowerCompany { get; set; } = null!;

    /// <summary>The bank building that issued this loan (denormalised from offer for fast queries).</summary>
    public Guid BankBuildingId { get; set; }

    /// <summary>Navigation property to the bank building.</summary>
    public Building BankBuilding { get; set; } = null!;

    /// <summary>The lender company (denormalised from offer for fast queries).</summary>
    public Guid LenderCompanyId { get; set; }

    /// <summary>Navigation property to the lender company.</summary>
    public Company LenderCompany { get; set; } = null!;

    /// <summary>Original amount borrowed.</summary>
    public decimal OriginalPrincipal { get; set; }

    /// <summary>Remaining principal still owed.</summary>
    public decimal RemainingPrincipal { get; set; }

    /// <summary>Annual interest rate at time of acceptance (snapshot from offer).</summary>
    public decimal AnnualInterestRatePercent { get; set; }

    /// <summary>Total duration in ticks agreed at acceptance.</summary>
    public long DurationTicks { get; set; }

    /// <summary>Tick when the loan was accepted and cash transferred.</summary>
    public long StartTick { get; set; }

    /// <summary>Tick when the final repayment is due.</summary>
    public long DueTick { get; set; }

    /// <summary>Tick of the next scheduled repayment.</summary>
    public long NextPaymentTick { get; set; }

    /// <summary>Amount of each periodic repayment instalment.</summary>
    public decimal PaymentAmount { get; set; }

    /// <summary>Number of payments made so far.</summary>
    public int PaymentsMade { get; set; }

    /// <summary>Total number of payments scheduled.</summary>
    public int TotalPayments { get; set; }

    /// <summary>
    /// Loan status: ACTIVE, OVERDUE, DEFAULTED, REPAID.
    /// </summary>
    [MaxLength(20)]
    public string Status { get; set; } = LoanStatus.Active;

    /// <summary>Number of consecutive missed payments.</summary>
    public int MissedPayments { get; set; }

    /// <summary>Accumulated penalty amount for late payments.</summary>
    public decimal AccumulatedPenalty { get; set; }

    /// <summary>UTC timestamp when the loan was accepted.</summary>
    public DateTime AcceptedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>UTC timestamp when the loan was fully repaid or defaulted.</summary>
    public DateTime? ClosedAtUtc { get; set; }
}

/// <summary>Loan status values.</summary>
public static class LoanStatus
{
    /// <summary>Loan is active and payments are current.</summary>
    public const string Active = "ACTIVE";

    /// <summary>A payment has been missed but the loan is not yet defaulted.</summary>
    public const string Overdue = "OVERDUE";

    /// <summary>Three or more payments missed; the loan is in default.</summary>
    public const string Defaulted = "DEFAULTED";

    /// <summary>Loan has been fully repaid.</summary>
    public const string Repaid = "REPAID";
}
