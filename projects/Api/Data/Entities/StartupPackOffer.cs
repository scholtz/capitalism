using System.ComponentModel.DataAnnotations;

namespace Api.Data.Entities;

/// <summary>
/// Durable lifecycle state for the one-time post-onboarding startup pack offer.
/// </summary>
public sealed class StartupPackOffer
{
    /// <summary>Unique identifier for this player-specific offer record.</summary>
    public Guid Id { get; set; }

    /// <summary>Player who can claim the offer.</summary>
    public Guid PlayerId { get; set; }

    /// <summary>Linked player account.</summary>
    public Player Player { get; set; } = null!;

    /// <summary>Stable key for the startup pack definition.</summary>
    [Required, MaxLength(50)]
    public string OfferKey { get; set; } = StartupPackOfferKeys.StartupPackV1;

    /// <summary>Current lifecycle status for the offer.</summary>
    [Required, MaxLength(20)]
    public string Status { get; set; } = StartupPackOfferStatus.Eligible;

    /// <summary>When the offer record was created for the player.</summary>
    public DateTime CreatedAtUtc { get; set; }

    /// <summary>When the offer stops being claimable.</summary>
    public DateTime ExpiresAtUtc { get; set; }

    /// <summary>When the offer was first presented to the player.</summary>
    public DateTime? ShownAtUtc { get; set; }

    /// <summary>When the player explicitly dismissed the offer.</summary>
    public DateTime? DismissedAtUtc { get; set; }

    /// <summary>When the offer was successfully claimed.</summary>
    public DateTime? ClaimedAtUtc { get; set; }

    /// <summary>Expansion capital granted to the selected company when claimed.</summary>
    public decimal CompanyCashGrant { get; set; }

    /// <summary>Pro subscription duration granted by this offer in days.</summary>
    public int ProDurationDays { get; set; }

    /// <summary>Company that received the cash grant, if claimed.</summary>
    public Guid? GrantedCompanyId { get; set; }

    /// <summary>
    /// Application-managed concurrency token that makes claim settlement atomic.
    /// Only one request may persist a transition into the claimed state.
    /// </summary>
    public Guid ConcurrencyToken { get; set; } = Guid.NewGuid();
}

/// <summary>Stable identifiers for startup-pack definitions.</summary>
public static class StartupPackOfferKeys
{
    public const string StartupPackV1 = "STARTUP_PACK_V1";
}

/// <summary>Lifecycle states for the startup-pack offer.</summary>
public static class StartupPackOfferStatus
{
    public const string Eligible = "ELIGIBLE";
    public const string Shown = "SHOWN";
    public const string Dismissed = "DISMISSED";
    public const string Claimed = "CLAIMED";
    public const string Expired = "EXPIRED";
}
