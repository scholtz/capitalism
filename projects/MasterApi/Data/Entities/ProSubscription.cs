namespace MasterApi.Data.Entities;

public enum SubscriptionTier
{
    Free,
    Pro,
}

public enum SubscriptionStatus
{
    Active,
    Expired,
    Cancelled,
}

public sealed class ProSubscription
{
    public Guid Id { get; set; }

    public Guid PlayerAccountId { get; set; }

    public PlayerAccount PlayerAccount { get; set; } = null!;

    public SubscriptionTier Tier { get; set; } = SubscriptionTier.Pro;

    public SubscriptionStatus Status { get; set; } = SubscriptionStatus.Active;

    public DateTime StartsAtUtc { get; set; }

    public DateTime ExpiresAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}
