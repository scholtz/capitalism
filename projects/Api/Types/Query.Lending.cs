using Api.Data;
using Api.Data.Entities;
using Api.Engine;
using Api.Security;
using Api.Utilities;
using HotChocolate.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Api.Types;

/// <summary>
/// Bank lending marketplace queries: loan offers, player loans, bank loans,
/// procurement preview, sourcing candidates, and unit upgrade info.
/// </summary>
public sealed partial class Query
{
    // ── Bank Lending Marketplace ──────────────────────────────────────────────────

    /// <summary>
    /// Returns all active loan offers visible to borrowers.
    /// Excludes offers from the current player's own companies (self-lending guard).
    /// </summary>
    public async Task<List<LoanOfferSummary>> GetLoanOffers(
        [Service] AppDbContext db,
        [Service] IHttpContextAccessor httpContextAccessor)
    {
        var userId = httpContextAccessor.HttpContext?.User.GetUserId();

        // Get IDs of companies owned by current player (to exclude own offers).
        ISet<Guid> ownCompanyIds = new HashSet<Guid>();
        if (userId.HasValue)
        {
            ownCompanyIds = (await db.Companies
                .Where(c => c.PlayerId == userId.Value)
                .Select(c => c.Id)
                .ToListAsync()).ToHashSet();
        }

        var offers = await db.LoanOffers
            .Include(o => o.LenderCompany)
            .Include(o => o.BankBuilding)
            .ThenInclude(b => b.City)
            .Where(o => o.IsActive && o.TotalCapacity > o.UsedCapacity)
            .AsNoTracking()
            .ToListAsync();

        return offers
            .Where(o => !ownCompanyIds.Contains(o.LenderCompanyId))
            .Select(o => new LoanOfferSummary
            {
                Id = o.Id,
                BankBuildingId = o.BankBuildingId,
                BankBuildingName = o.BankBuilding.Name,
                CityId = o.BankBuilding.CityId,
                CityName = o.BankBuilding.City.Name,
                LenderCompanyId = o.LenderCompanyId,
                LenderCompanyName = o.LenderCompany.Name,
                AnnualInterestRatePercent = o.AnnualInterestRatePercent,
                MaxPrincipalPerLoan = o.MaxPrincipalPerLoan,
                TotalCapacity = o.TotalCapacity,
                UsedCapacity = o.UsedCapacity,
                RemainingCapacity = o.TotalCapacity - o.UsedCapacity,
                DurationTicks = o.DurationTicks,
                IsActive = o.IsActive,
                CreatedAtTick = o.CreatedAtTick,
                CreatedAtUtc = o.CreatedAtUtc,
            }).ToList();
    }

    /// <summary>
    /// Returns all loan offers published by the authenticated player's companies.
    /// Includes inactive offers and utilisation metrics.
    /// </summary>
    [Authorize]
    public async Task<List<LoanOfferSummary>> GetMyLoanOffers(
        [Service] AppDbContext db,
        [Service] IHttpContextAccessor httpContextAccessor)
    {
        var userId = httpContextAccessor.HttpContext!.User.GetRequiredUserId();

        var companyIds = await db.Companies
            .Where(c => c.PlayerId == userId)
            .Select(c => c.Id)
            .ToListAsync();

        var offers = await db.LoanOffers
            .Include(o => o.LenderCompany)
            .Include(o => o.BankBuilding)
            .ThenInclude(b => b.City)
            .Where(o => companyIds.Contains(o.LenderCompanyId))
            .AsNoTracking()
            .ToListAsync();

        return offers.Select(o => new LoanOfferSummary
        {
            Id = o.Id,
            BankBuildingId = o.BankBuildingId,
            BankBuildingName = o.BankBuilding.Name,
            CityId = o.BankBuilding.CityId,
            CityName = o.BankBuilding.City.Name,
            LenderCompanyId = o.LenderCompanyId,
            LenderCompanyName = o.LenderCompany.Name,
            AnnualInterestRatePercent = o.AnnualInterestRatePercent,
            MaxPrincipalPerLoan = o.MaxPrincipalPerLoan,
            TotalCapacity = o.TotalCapacity,
            UsedCapacity = o.UsedCapacity,
            RemainingCapacity = o.TotalCapacity - o.UsedCapacity,
            DurationTicks = o.DurationTicks,
            IsActive = o.IsActive,
            CreatedAtTick = o.CreatedAtTick,
            CreatedAtUtc = o.CreatedAtUtc,
        }).ToList();
    }

    /// <summary>
    /// Returns all active and historical loans for the authenticated player's companies (borrower view).
    /// </summary>
    [Authorize]
    public async Task<List<LoanSummary>> GetMyLoans(
        [Service] AppDbContext db,
        [Service] IHttpContextAccessor httpContextAccessor)
    {
        var userId = httpContextAccessor.HttpContext!.User.GetRequiredUserId();

        var companyIds = await db.Companies
            .Where(c => c.PlayerId == userId)
            .Select(c => c.Id)
            .ToListAsync();

        var loans = await db.Loans
            .Include(l => l.LenderCompany)
            .Include(l => l.BankBuilding)
            .Include(l => l.BorrowerCompany)
            .Where(l => companyIds.Contains(l.BorrowerCompanyId))
            .AsNoTracking()
            .ToListAsync();

        return loans.Select(MapToLoanSummary).ToList();
    }

    /// <summary>
    /// Returns all loans issued by a specific bank building (lender view).
    /// Only the bank's owning player can access this.
    /// </summary>
    [Authorize]
    public async Task<List<LoanSummary>> GetBankLoans(
        Guid bankBuildingId,
        [Service] AppDbContext db,
        [Service] IHttpContextAccessor httpContextAccessor)
    {
        var userId = httpContextAccessor.HttpContext!.User.GetRequiredUserId();

        var building = await db.Buildings
            .Include(b => b.Company)
            .FirstOrDefaultAsync(b => b.Id == bankBuildingId && b.Type == Data.Entities.BuildingType.Bank);

        if (building is null || building.Company.PlayerId != userId)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Bank building not found or you do not own it.")
                    .SetCode("BANK_NOT_FOUND")
                    .Build());
        }

        var loans = await db.Loans
            .Include(l => l.BorrowerCompany)
            .Include(l => l.LenderCompany)
            .Include(l => l.BankBuilding)
            .Where(l => l.BankBuildingId == bankBuildingId)
            .AsNoTracking()
            .ToListAsync();

        return loans.Select(MapToLoanSummary).ToList();
    }

    private static LoanSummary MapToLoanSummary(Loan l) => new()
    {
        Id = l.Id,
        LoanOfferId = l.LoanOfferId,
        BorrowerCompanyId = l.BorrowerCompanyId,
        BorrowerCompanyName = l.BorrowerCompany.Name,
        LenderCompanyId = l.LenderCompanyId,
        LenderCompanyName = l.LenderCompany.Name,
        BankBuildingId = l.BankBuildingId,
        BankBuildingName = l.BankBuilding.Name,
        OriginalPrincipal = l.OriginalPrincipal,
        RemainingPrincipal = l.RemainingPrincipal,
        AnnualInterestRatePercent = l.AnnualInterestRatePercent,
        DurationTicks = l.DurationTicks,
        StartTick = l.StartTick,
        DueTick = l.DueTick,
        NextPaymentTick = l.NextPaymentTick,
        PaymentAmount = l.PaymentAmount,
        PaymentsMade = l.PaymentsMade,
        TotalPayments = l.TotalPayments,
        Status = l.Status,
        MissedPayments = l.MissedPayments,
        AccumulatedPenalty = l.AccumulatedPenalty,
        AcceptedAtUtc = l.AcceptedAtUtc,
        ClosedAtUtc = l.ClosedAtUtc,
    };

    /// <summary>
    /// Evaluates what a PURCHASE unit would do on the next tick given its current
    /// configuration and live market state, without mutating any data.
    /// Returns expected source, delivered price, quality, and any block reasons.
    /// Requires authentication (the player must own the building).
    /// </summary>
    [Authorize]
    public async Task<ProcurementPreview?> GetProcurementPreview(
        Guid buildingUnitId,
        [Service] AppDbContext db,
        [Service] IHttpContextAccessor httpContextAccessor)
    {
        var userId = httpContextAccessor.HttpContext!.User.GetRequiredUserId();

        var unit = await db.BuildingUnits
            .Include(u => u.Building)
            .ThenInclude(b => b.Company)
            .FirstOrDefaultAsync(u => u.Id == buildingUnitId);

        if (unit is null || unit.Building.Company.PlayerId != userId)
            return null;

        if (unit.UnitType != Data.Entities.UnitType.Purchase)
            return null;

        var company = unit.Building.Company;
        return await ProcurementPreviewService.ComputeAsync(db, unit, company);
    }

    /// <summary>
    /// Returns the full ranked list of sourcing candidates for a PURCHASE unit,
    /// including global exchange offers from every city, player-placed exchange orders,
    /// and local B2B suppliers. Each candidate includes the full landed-cost breakdown
    /// (exchange price, transit cost, delivered price) and eligibility state so the
    /// player can compare options and understand why a cheaper-looking route may be
    /// blocked by a quality or price filter.
    ///
    /// Requires authentication. The authenticated player must own the building that
    /// contains the unit.
    /// </summary>
    [Authorize]
    public async Task<List<SourcingCandidate>> GetSourcingCandidates(
        Guid buildingUnitId,
        [Service] AppDbContext db,
        [Service] IHttpContextAccessor httpContextAccessor)
    {
        var userId = httpContextAccessor.HttpContext!.User.GetRequiredUserId();

        var unit = await db.BuildingUnits
            .Include(u => u.Building)
            .ThenInclude(b => b.Company)
            .FirstOrDefaultAsync(u => u.Id == buildingUnitId);

        if (unit is null || unit.Building.Company.PlayerId != userId)
            return [];

        if (unit.UnitType != Data.Entities.UnitType.Purchase)
            return [];

        return await SourcingComparisonService.GetCandidatesAsync(db, unit, unit.Building.Company);
    }

    /// <summary>
    /// Returns upgrade information for a building unit: cost, duration, and stat projections.
    /// Returns null if the unit is not found or the caller does not own it.
    /// </summary>
    [Authorize]
    public async Task<UnitUpgradeInfo?> GetUnitUpgradeInfo(
        Guid unitId,
        [Service] AppDbContext db,
        [Service] IHttpContextAccessor httpContextAccessor)
    {
        var userId = httpContextAccessor.HttpContext!.User.GetRequiredUserId();

        var unit = await db.BuildingUnits
            .Include(u => u.Building)
            .ThenInclude(b => b.Company)
            .FirstOrDefaultAsync(u => u.Id == unitId);

        if (unit is null || unit.Building.Company.PlayerId != userId)
            return null;

        var isUpgradable = Engine.GameConstants.IsUpgradableUnitType(unit.UnitType);
        var isMaxLevel = unit.Level >= Engine.GameConstants.MaxUnitLevel;
        var currentStat = Engine.GameConstants.GetUnitStat(unit.UnitType, unit.Level);

        return new UnitUpgradeInfo
        {
            UnitId = unit.Id,
            UnitType = unit.UnitType,
            CurrentLevel = unit.Level,
            NextLevel = isMaxLevel ? unit.Level : unit.Level + 1,
            IsMaxLevel = isMaxLevel,
            IsUpgradable = isUpgradable,
            UpgradeCost = isUpgradable && !isMaxLevel
                ? Engine.GameConstants.UnitUpgradeCost(unit.UnitType, unit.Level)
                : 0m,
            UpgradeTicks = isUpgradable && !isMaxLevel
                ? Engine.GameConstants.UnitUpgradeTicks(unit.Level)
                : 0,
            CurrentStat = currentStat,
            NextStat = isMaxLevel
                ? currentStat  // same as CurrentStat at max level
                : Engine.GameConstants.GetUnitStat(unit.UnitType, unit.Level + 1),
            StatLabel = Engine.GameConstants.GetUnitStatLabel(unit.UnitType),
        };
    }
}
