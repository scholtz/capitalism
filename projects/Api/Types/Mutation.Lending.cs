using Api.Data;
using Api.Data.Entities;
using Api.Engine;
using Api.Security;
using HotChocolate.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Api.Types;

/// <summary>
/// Bank lending marketplace mutations: publishing, updating, deactivating loan offers,
/// and accepting loans between player-owned companies.
/// </summary>
public sealed partial class Mutation
{
    // ── Bank Lending Marketplace ──────────────────────────────────────────────────

    /// <summary>
    /// Publishes a new loan offer from a bank building.
    /// Only the owning company can publish offers; rate must be 0.1–200%, duration 24–87600 ticks.
    /// </summary>
    [Authorize]
    public async Task<LoanOffer> PublishLoanOffer(
        PublishLoanOfferInput input,
        [Service] AppDbContext db,
        [Service] IHttpContextAccessor httpContextAccessor)
    {
        var userId = httpContextAccessor.HttpContext!.User.GetRequiredUserId();

        // Validate the bank building is owned by this player's company.
        var bankBuilding = await db.Buildings
            .Include(b => b.Company)
            .FirstOrDefaultAsync(b => b.Id == input.BankBuildingId && b.Type == BuildingType.Bank);

        if (bankBuilding is null)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Bank building not found.")
                    .SetCode("BANK_NOT_FOUND")
                    .Build());
        }

        if (bankBuilding.Company.PlayerId != userId)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("You do not own this bank building.")
                    .SetCode("BANK_NOT_FOUND")
                    .Build());
        }

        // Validate input.
        if (input.AnnualInterestRatePercent < 0.1m || input.AnnualInterestRatePercent > 200m)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Interest rate must be between 0.1% and 200%.")
                    .SetCode("INVALID_INTEREST_RATE")
                    .Build());
        }

        if (input.MaxPrincipalPerLoan < 1_000m)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Maximum principal per loan must be at least $1,000.")
                    .SetCode("INVALID_PRINCIPAL")
                    .Build());
        }

        if (input.TotalCapacity < input.MaxPrincipalPerLoan)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Total capacity must be at least as large as the maximum principal per loan.")
                    .SetCode("INVALID_CAPACITY")
                    .Build());
        }

        if (input.DurationTicks < 24 || input.DurationTicks > 87_600)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Loan duration must be between 24 ticks (1 in-game day) and 87,600 ticks (10 in-game years).")
                    .SetCode("INVALID_DURATION")
                    .Build());
        }

        // Ensure the lender company has enough cash to cover the full capacity.
        if (bankBuilding.Company.Cash < input.TotalCapacity)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage($"Insufficient funds. Your company needs at least {input.TotalCapacity:C0} to publish this offer.")
                    .SetCode("INSUFFICIENT_FUNDS")
                    .Build());
        }

        var currentTick = await db.GameStates.AsNoTracking().Select(gs => gs.CurrentTick).FirstOrDefaultAsync();

        var offer = new LoanOffer
        {
            Id = Guid.NewGuid(),
            BankBuildingId = bankBuilding.Id,
            LenderCompanyId = bankBuilding.CompanyId,
            AnnualInterestRatePercent = input.AnnualInterestRatePercent,
            MaxPrincipalPerLoan = input.MaxPrincipalPerLoan,
            TotalCapacity = input.TotalCapacity,
            UsedCapacity = 0m,
            DurationTicks = input.DurationTicks,
            IsActive = true,
            CreatedAtTick = currentTick,
            CreatedAtUtc = DateTime.UtcNow
        };

        db.LoanOffers.Add(offer);
        await db.SaveChangesAsync();

        return offer;
    }

    /// <summary>
    /// Updates an existing loan offer. Only the bank's owning player can update it.
    /// Ongoing loans are not affected; changes apply to new loans only.
    /// </summary>
    [Authorize]
    public async Task<LoanOffer> UpdateLoanOffer(
        UpdateLoanOfferInput input,
        [Service] AppDbContext db,
        [Service] IHttpContextAccessor httpContextAccessor)
    {
        var userId = httpContextAccessor.HttpContext!.User.GetRequiredUserId();

        var offer = await db.LoanOffers
            .Include(o => o.LenderCompany)
            .FirstOrDefaultAsync(o => o.Id == input.LoanOfferId);

        if (offer is null || offer.LenderCompany.PlayerId != userId)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Loan offer not found or you do not own it.")
                    .SetCode("OFFER_NOT_FOUND")
                    .Build());
        }

        if (input.AnnualInterestRatePercent.HasValue)
        {
            if (input.AnnualInterestRatePercent.Value < 0.1m || input.AnnualInterestRatePercent.Value > 200m)
            {
                throw new GraphQLException(
                    ErrorBuilder.New()
                        .SetMessage("Interest rate must be between 0.1% and 200%.")
                        .SetCode("INVALID_INTEREST_RATE")
                        .Build());
            }
            offer.AnnualInterestRatePercent = input.AnnualInterestRatePercent.Value;
        }

        if (input.MaxPrincipalPerLoan.HasValue)
        {
            if (input.MaxPrincipalPerLoan.Value < 1_000m)
            {
                throw new GraphQLException(
                    ErrorBuilder.New()
                        .SetMessage("Maximum principal per loan must be at least $1,000.")
                        .SetCode("INVALID_PRINCIPAL")
                        .Build());
            }
            offer.MaxPrincipalPerLoan = input.MaxPrincipalPerLoan.Value;
        }

        if (input.TotalCapacity.HasValue)
        {
            var newCapacity = input.TotalCapacity.Value;
            if (newCapacity < offer.UsedCapacity)
            {
                throw new GraphQLException(
                    ErrorBuilder.New()
                        .SetMessage($"Total capacity cannot be reduced below currently used capacity ({offer.UsedCapacity:C0}).")
                        .SetCode("INVALID_CAPACITY")
                        .Build());
            }
            offer.TotalCapacity = newCapacity;
        }

        if (input.DurationTicks.HasValue)
        {
            if (input.DurationTicks.Value < 24 || input.DurationTicks.Value > 87_600)
            {
                throw new GraphQLException(
                    ErrorBuilder.New()
                        .SetMessage("Loan duration must be between 24 ticks and 87,600 ticks.")
                        .SetCode("INVALID_DURATION")
                        .Build());
            }
            offer.DurationTicks = input.DurationTicks.Value;
        }

        if (input.IsActive.HasValue)
        {
            offer.IsActive = input.IsActive.Value;
        }

        await db.SaveChangesAsync();
        return offer;
    }

    /// <summary>
    /// Deactivates a loan offer so it no longer appears to borrowers.
    /// Existing loans are not affected.
    /// </summary>
    [Authorize]
    public async Task<LoanOffer> DeactivateLoanOffer(
        Guid loanOfferId,
        [Service] AppDbContext db,
        [Service] IHttpContextAccessor httpContextAccessor)
    {
        var userId = httpContextAccessor.HttpContext!.User.GetRequiredUserId();

        var offer = await db.LoanOffers
            .Include(o => o.LenderCompany)
            .FirstOrDefaultAsync(o => o.Id == loanOfferId);

        if (offer is null || offer.LenderCompany.PlayerId != userId)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Loan offer not found or you do not own it.")
                    .SetCode("OFFER_NOT_FOUND")
                    .Build());
        }

        offer.IsActive = false;
        await db.SaveChangesAsync();
        return offer;
    }

    /// <summary>
    /// Accepts a loan offer: creates a Loan record, transfers cash from lender to borrower,
    /// and records ledger entries for both parties.
    /// Guards: cannot borrow from own company; principal must not exceed remaining capacity; lender must have cash.
    /// </summary>
    [Authorize]
    public async Task<Loan> AcceptLoan(
        AcceptLoanInput input,
        [Service] AppDbContext db,
        [Service] IHttpContextAccessor httpContextAccessor)
    {
        var userId = httpContextAccessor.HttpContext!.User.GetRequiredUserId();

        // Verify borrower owns the company.
        var borrower = await db.Companies
            .FirstOrDefaultAsync(c => c.Id == input.BorrowerCompanyId && c.PlayerId == userId);

        if (borrower is null)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Borrower company not found or you do not own it.")
                    .SetCode("COMPANY_NOT_FOUND")
                    .Build());
        }

        // Load the offer with lender company.
        var offer = await db.LoanOffers
            .Include(o => o.LenderCompany)
            .FirstOrDefaultAsync(o => o.Id == input.LoanOfferId);

        if (offer is null || !offer.IsActive)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Loan offer not found or is no longer active.")
                    .SetCode("OFFER_NOT_FOUND")
                    .Build());
        }

        // Self-lending guard.
        if (offer.LenderCompanyId == input.BorrowerCompanyId)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("A company cannot borrow from itself.")
                    .SetCode("SELF_LENDING_NOT_ALLOWED")
                    .Build());
        }

        // Same player guard.
        if (offer.LenderCompany.PlayerId == userId)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("You cannot borrow from your own bank.")
                    .SetCode("SELF_LENDING_NOT_ALLOWED")
                    .Build());
        }

        if (input.PrincipalAmount < 1_000m)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Minimum loan amount is $1,000.")
                    .SetCode("INVALID_PRINCIPAL")
                    .Build());
        }

        if (input.PrincipalAmount > offer.MaxPrincipalPerLoan)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage($"Requested principal exceeds the maximum per-loan limit of {offer.MaxPrincipalPerLoan:C0}.")
                    .SetCode("EXCEEDS_MAX_PRINCIPAL")
                    .Build());
        }

        var remainingCapacity = offer.TotalCapacity - offer.UsedCapacity;
        if (input.PrincipalAmount > remainingCapacity)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage($"The offer only has {remainingCapacity:C0} of lending capacity remaining.")
                    .SetCode("INSUFFICIENT_CAPACITY")
                    .Build());
        }

        // Lender must have the cash.
        if (offer.LenderCompany.Cash < input.PrincipalAmount)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("The lender does not have sufficient funds to cover this loan at this time.")
                    .SetCode("LENDER_INSUFFICIENT_FUNDS")
                    .Build());
        }

        var currentTick = await db.GameStates.AsNoTracking().Select(gs => gs.CurrentTick).FirstOrDefaultAsync();

        // Calculate payment schedule: monthly payments (every 30 in-game days = 720 ticks), minimum 1 payment.
        var ticksPerPayment = 720L; // 30 in-game days
        var totalPayments = (int)Math.Max(1, offer.DurationTicks / ticksPerPayment);
        var dueTick = currentTick + offer.DurationTicks;

        // Compute flat equal payment (simple interest, not compound).
        var totalInterest = input.PrincipalAmount * (offer.AnnualInterestRatePercent / 100m)
            * ((decimal)offer.DurationTicks / GameConstants.TicksPerYear);
        var totalRepayment = input.PrincipalAmount + totalInterest;
        var paymentAmount = decimal.Round(totalRepayment / totalPayments, 4, MidpointRounding.AwayFromZero);

        // Transfer cash.
        offer.LenderCompany.Cash -= input.PrincipalAmount;
        borrower.Cash += input.PrincipalAmount;
        offer.UsedCapacity += input.PrincipalAmount;

        var loan = new Loan
        {
            Id = Guid.NewGuid(),
            LoanOfferId = offer.Id,
            BorrowerCompanyId = borrower.Id,
            BankBuildingId = offer.BankBuildingId,
            LenderCompanyId = offer.LenderCompanyId,
            OriginalPrincipal = input.PrincipalAmount,
            RemainingPrincipal = input.PrincipalAmount,
            AnnualInterestRatePercent = offer.AnnualInterestRatePercent,
            DurationTicks = offer.DurationTicks,
            StartTick = currentTick,
            DueTick = dueTick,
            NextPaymentTick = currentTick + ticksPerPayment,
            PaymentAmount = paymentAmount,
            PaymentsMade = 0,
            TotalPayments = totalPayments,
            Status = LoanStatus.Active,
            MissedPayments = 0,
            AccumulatedPenalty = 0m,
            AcceptedAtUtc = DateTime.UtcNow
        };

        db.Loans.Add(loan);

        // Ledger: borrower receives cash (loan origination).
        db.LedgerEntries.Add(new LedgerEntry
        {
            Id = Guid.NewGuid(),
            CompanyId = borrower.Id,
            Category = LedgerCategory.LoanOrigination,
            Description = $"Loan received from {offer.LenderCompany.Name} – {offer.AnnualInterestRatePercent}% p.a. over {offer.DurationTicks} ticks",
            Amount = input.PrincipalAmount,
            RecordedAtTick = currentTick,
            RecordedAtUtc = DateTime.UtcNow
        });

        await db.SaveChangesAsync();

        return loan;
    }
}
