using Api.Data.Entities;
using Api.Engine;
using Microsoft.EntityFrameworkCore;

namespace Api.Engine.Phases;

/// <summary>
/// Processes scheduled loan repayments each tick.
/// For each active loan whose NextPaymentTick has been reached:
///   - If the borrower has sufficient cash, deduct the instalment (principal + interest split),
///     add the cash to the lender, and write ledger entries for both sides.
///   - If the borrower cannot cover the payment, record a missed payment, accumulate a penalty,
///     and set the loan status to OVERDUE or DEFAULTED.
/// When the final payment is made, mark the loan REPAID and free the offer's capacity.
/// </summary>
public sealed class LoanRepaymentPhase : ITickPhase
{
    public string Name => "LoanRepayment";

    /// <summary>Runs after operating costs but before tax, so companies pay debts from real cash.</summary>
    public int Order => 950;

    /// <summary>Penalty rate applied to the remaining principal on each missed payment (5%).</summary>
    private const decimal MissedPaymentPenaltyRate = 0.05m;

    /// <summary>Number of missed payments before the loan is considered DEFAULTED.</summary>
    private const int DefaultedMissedPaymentThreshold = 3;

    public Task ProcessAsync(TickContext context)
    {
        // Load loans with NextPaymentTick <= currentTick that are not yet fully repaid.
        var dueLoans = context.Db.Loans
            .Include(l => l.LoanOffer)
            .Where(l => l.NextPaymentTick <= context.CurrentTick
                && (l.Status == LoanStatus.Active || l.Status == LoanStatus.Overdue))
            .ToList();

        foreach (var loan in dueLoans)
        {
            if (!context.CompaniesById.TryGetValue(loan.BorrowerCompanyId, out var borrower))
                continue;
            if (!context.CompaniesById.TryGetValue(loan.LenderCompanyId, out var lender))
                continue;

            // Determine how many payments are due (handles tick-skipping edge cases).
            var paymentsDue = ComputePaymentsDue(loan, context.CurrentTick);

            for (var paymentIndex = 0; paymentIndex < paymentsDue; paymentIndex++)
            {
                var paymentTick = loan.NextPaymentTick;
                var isLastPayment = loan.PaymentsMade + 1 >= loan.TotalPayments;

                // Compute principal vs interest split for this instalment.
                // Use flat/amortised split: split interest proportional to remaining principal.
                var interestPerPayment = ComputeInterestForPayment(loan);
                var principalPayment = loan.PaymentAmount - interestPerPayment;

                // For last payment, pay all remaining principal to avoid rounding drift.
                if (isLastPayment)
                {
                    principalPayment = loan.RemainingPrincipal;
                    // Recalculate total payment for the last instalment.
                    var lastPayment = principalPayment + interestPerPayment;
                    ProcessSinglePayment(context, loan, borrower, lender, lastPayment, principalPayment, interestPerPayment, paymentTick, isLastPayment);
                }
                else
                {
                    ProcessSinglePayment(context, loan, borrower, lender, loan.PaymentAmount, principalPayment, interestPerPayment, paymentTick, false);
                }

                // Stop processing further payments if the loan was closed or defaulted.
                if (loan.Status == LoanStatus.Repaid || loan.Status == LoanStatus.Defaulted)
                    break;
            }
        }

        return Task.CompletedTask;
    }

    private static int ComputePaymentsDue(Loan loan, long currentTick)
    {
        if (loan.TotalPayments <= 0) return 0;
        var ticksPerPayment = loan.DurationTicks / loan.TotalPayments;
        if (ticksPerPayment <= 0) return 1;

        var paymentsDue = 0;
        var checkTick = loan.NextPaymentTick;
        while (checkTick <= currentTick && loan.PaymentsMade + paymentsDue < loan.TotalPayments)
        {
            paymentsDue++;
            checkTick += ticksPerPayment;
        }

        return Math.Max(1, paymentsDue);
    }

    private static decimal ComputeInterestForPayment(Loan loan)
    {
        // Simple periodic interest: annualRate / ticksPerYear * remainingPrincipal
        // Using 365 days × 24 ticks/day = 8760 ticks per year.
        var ticksPerYear = GameConstants.TicksPerYear;
        var ticksPerPayment = loan.TotalPayments > 0 ? loan.DurationTicks / loan.TotalPayments : loan.DurationTicks;
        var periodicRate = (loan.AnnualInterestRatePercent / 100m) * ((decimal)ticksPerPayment / ticksPerYear);
        return decimal.Round(loan.RemainingPrincipal * periodicRate, 4, MidpointRounding.AwayFromZero);
    }

    private static void ProcessSinglePayment(
        TickContext context,
        Loan loan,
        Company borrower,
        Company lender,
        decimal totalPayment,
        decimal principalPayment,
        decimal interestPayment,
        long paymentTick,
        bool isLastPayment)
    {
        var ticksPerPayment = loan.TotalPayments > 0 ? loan.DurationTicks / loan.TotalPayments : loan.DurationTicks;

        if (borrower.Cash >= totalPayment)
        {
            // Successful payment.
            borrower.Cash -= totalPayment;
            lender.Cash += totalPayment;
            loan.RemainingPrincipal = Math.Max(0m, loan.RemainingPrincipal - principalPayment);
            loan.PaymentsMade++;
            loan.NextPaymentTick = paymentTick + ticksPerPayment;

            // If borrower was overdue, restore to active.
            if (loan.Status == LoanStatus.Overdue)
            {
                loan.Status = LoanStatus.Active;
                loan.MissedPayments = 0;
            }

            // Borrower ledger: principal repayment.
            context.Db.LedgerEntries.Add(new LedgerEntry
            {
                Id = Guid.NewGuid(),
                CompanyId = borrower.Id,
                Category = LedgerCategory.LoanRepaymentPrincipal,
                Description = $"Loan repayment (principal) – payment {loan.PaymentsMade}/{loan.TotalPayments}",
                Amount = -principalPayment,
                RecordedAtTick = context.CurrentTick,
                RecordedAtUtc = DateTime.UtcNow
            });

            // Borrower ledger: interest expense.
            if (interestPayment > 0m)
            {
                context.Db.LedgerEntries.Add(new LedgerEntry
                {
                    Id = Guid.NewGuid(),
                    CompanyId = borrower.Id,
                    Category = LedgerCategory.LoanInterestExpense,
                    Description = $"Loan interest expense – payment {loan.PaymentsMade}/{loan.TotalPayments}",
                    Amount = -interestPayment,
                    RecordedAtTick = context.CurrentTick,
                    RecordedAtUtc = DateTime.UtcNow
                });
            }

            // Lender ledger: interest income.
            if (interestPayment > 0m)
            {
                context.Db.LedgerEntries.Add(new LedgerEntry
                {
                    Id = Guid.NewGuid(),
                    CompanyId = lender.Id,
                    Category = LedgerCategory.LoanInterestIncome,
                    Description = $"Loan interest income from {borrower.Name} – payment {loan.PaymentsMade}/{loan.TotalPayments}",
                    Amount = interestPayment,
                    RecordedAtTick = context.CurrentTick,
                    RecordedAtUtc = DateTime.UtcNow
                });
            }

            // Check if fully repaid.
            if (isLastPayment || loan.PaymentsMade >= loan.TotalPayments || loan.RemainingPrincipal <= 0m)
            {
                loan.Status = LoanStatus.Repaid;
                loan.RemainingPrincipal = 0m;
                loan.ClosedAtUtc = DateTime.UtcNow;

                // Free the capacity on the offer.
                loan.LoanOffer.UsedCapacity = Math.Max(0m, loan.LoanOffer.UsedCapacity - loan.OriginalPrincipal);
            }
        }
        else
        {
            // Missed payment.
            loan.MissedPayments++;
            loan.NextPaymentTick = paymentTick + ticksPerPayment;

            // Apply penalty on remaining principal.
            var penalty = decimal.Round(loan.RemainingPrincipal * MissedPaymentPenaltyRate, 4, MidpointRounding.AwayFromZero);
            loan.AccumulatedPenalty += penalty;
            loan.RemainingPrincipal += penalty;

            // Charge borrower whatever cash they have as partial payment (optional, simple model: skip full payment).
            // For first pass: record missed payment only (no partial payment collection).

            // Record penalty in ledger for borrower.
            if (penalty > 0m)
            {
                context.Db.LedgerEntries.Add(new LedgerEntry
                {
                    Id = Guid.NewGuid(),
                    CompanyId = borrower.Id,
                    Category = LedgerCategory.LoanPenalty,
                    Description = $"Missed loan payment penalty (missed payment #{loan.MissedPayments})",
                    Amount = -penalty,
                    RecordedAtTick = context.CurrentTick,
                    RecordedAtUtc = DateTime.UtcNow
                });
            }

            // Update status.
            loan.Status = loan.MissedPayments >= DefaultedMissedPaymentThreshold
                ? LoanStatus.Defaulted
                : LoanStatus.Overdue;

            if (loan.Status == LoanStatus.Defaulted)
            {
                loan.ClosedAtUtc = DateTime.UtcNow;
                // Capacity remains locked (lender is owed money but capacity was consumed).
            }
        }
    }
}
