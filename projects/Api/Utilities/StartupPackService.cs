using Api.Data;
using Api.Data.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Api.Utilities;

/// <summary>
/// Centralizes startup-pack eligibility, lifecycle, and entitlement-grant rules.
/// </summary>
public static class StartupPackService
{
    public const decimal PriceUsd = 20m;
    public const decimal CompanyCashGrant = 250_000m;
    public const int ProDurationDays = 90;
    public const int MaxClaimRetryAttempts = 3;
    public const int ClaimRetryBaseDelayMs = 25;

    private static readonly TimeSpan OfferAvailabilityWindow = TimeSpan.FromHours(72);
    private const int SqliteConstraintErrorCode = 19;

    /// <summary>Bumps the concurrency token so EF enforces optimistic concurrency on the next save.</summary>
    private static void BumpConcurrencyToken(StartupPackOffer offer)
    {
        offer.ConcurrencyToken = Guid.NewGuid();
    }

    /// <summary>
    /// Ensures an eligible player has a durable startup-pack record.
    /// Players who completed onboarding before this feature existed are automatically
    /// backfilled the first time the system checks their offer state and finds no record.
    /// </summary>
    public static async Task<StartupPackOffer?> EnsureOfferForPlayerAsync(
        AppDbContext db,
        Player player,
        DateTime nowUtc)
    {
        if (player.OnboardingCompletedAtUtc is null)
        {
            return null;
        }

        var offer = await db.StartupPackOffers.FirstOrDefaultAsync(candidate => candidate.PlayerId == player.Id);
        if (offer is null)
        {
            offer = new StartupPackOffer
            {
                Id = Guid.NewGuid(),
                PlayerId = player.Id,
                OfferKey = StartupPackOfferKeys.StartupPackV1,
                Status = StartupPackOfferStatus.Eligible,
                CreatedAtUtc = nowUtc,
                ExpiresAtUtc = nowUtc.Add(OfferAvailabilityWindow),
                CompanyCashGrant = CompanyCashGrant,
                ProDurationDays = ProDurationDays
            };

            db.StartupPackOffers.Add(offer);
            try
            {
                await db.SaveChangesAsync();
                return offer;
            }
            catch (DbUpdateException ex) when (ex.InnerException is SqliteException { SqliteErrorCode: SqliteConstraintErrorCode })
            {
                // The test and deployed environments both use SQLite right now, so a duplicate
                // PlayerId insert surfaces as SQLITE_CONSTRAINT and we can re-read the winner's row.
                db.Entry(offer).State = EntityState.Detached;
                return await db.StartupPackOffers.FirstAsync(candidate => candidate.PlayerId == player.Id);
            }
        }

        if (TryExpireOffer(offer, nowUtc))
        {
            await db.SaveChangesAsync();
        }

        return offer;
    }

    /// <summary>Updates the stored lifecycle state when a valid offer is displayed.</summary>
    public static bool MarkShown(StartupPackOffer offer, DateTime nowUtc)
    {
        if (offer.Status == StartupPackOfferStatus.Claimed)
        {
            return false;
        }

        var changed = TryExpireOffer(offer, nowUtc);
        if (changed)
        {
            return true;
        }
        if (offer.ShownAtUtc is null)
        {
            offer.ShownAtUtc = nowUtc;
            BumpConcurrencyToken(offer);
            changed = true;
        }

        if (offer.Status == StartupPackOfferStatus.Eligible)
        {
            offer.Status = StartupPackOfferStatus.Shown;
            BumpConcurrencyToken(offer);
            changed = true;
        }

        return changed;
    }

    /// <summary>Stores an explicit dismissal without blocking free progression.</summary>
    public static bool Dismiss(StartupPackOffer offer, DateTime nowUtc)
    {
        if (offer.Status == StartupPackOfferStatus.Claimed)
        {
            return false;
        }

        var changed = TryExpireOffer(offer, nowUtc);
        if (changed)
        {
            return true;
        }
        changed = MarkShown(offer, nowUtc) || changed;
        if (offer.Status != StartupPackOfferStatus.Dismissed)
        {
            offer.Status = StartupPackOfferStatus.Dismissed;
            BumpConcurrencyToken(offer);
            changed = true;
        }

        if (offer.DismissedAtUtc is null)
        {
            offer.DismissedAtUtc = nowUtc;
            BumpConcurrencyToken(offer);
            changed = true;
        }

        return changed;
    }

    /// <summary>Promotes an expired unclaimed offer to EXPIRED status.</summary>
    public static bool TryExpireOffer(StartupPackOffer offer, DateTime nowUtc)
    {
        if (offer.Status == StartupPackOfferStatus.Claimed || offer.Status == StartupPackOfferStatus.Expired)
        {
            return false;
        }

        if (offer.ExpiresAtUtc > nowUtc)
        {
            return false;
        }

        offer.Status = StartupPackOfferStatus.Expired;
        BumpConcurrencyToken(offer);
        return true;
    }

    /// <summary>
    /// Finalizes the offer as claimed. The caller must persist this in the same SaveChanges call
    /// as the company cash and player Pro updates so the economy/premium grant settles exactly once.
    /// </summary>
    public static void MarkClaimed(StartupPackOffer offer, Guid companyId, DateTime nowUtc)
    {
        offer.Status = StartupPackOfferStatus.Claimed;
        offer.ClaimedAtUtc = nowUtc;
        offer.GrantedCompanyId = companyId;
        BumpConcurrencyToken(offer);
    }
}
