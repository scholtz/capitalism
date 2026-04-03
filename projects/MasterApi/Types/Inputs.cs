namespace MasterApi.Types;

public sealed class RegisterInput
{
    public string Email { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;
}

public sealed class LoginInput
{
    public string Email { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;
}

public sealed class ProlongSubscriptionInput
{
    /// <summary>Number of months to extend the subscription (1–12).</summary>
    public int Months { get; set; } = 1;
}

public sealed class RegisterGameServerInput
{
    public string RegistrationKey { get; set; } = string.Empty;

    public string ServerKey { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string Region { get; set; } = string.Empty;

    public string Environment { get; set; } = string.Empty;

    public string BackendUrl { get; set; } = string.Empty;

    public string GraphqlUrl { get; set; } = string.Empty;

    public string FrontendUrl { get; set; } = string.Empty;

    public string Version { get; set; } = string.Empty;

    public int PlayerCount { get; set; }

    public int CompanyCount { get; set; }

    public long CurrentTick { get; set; }
}

public sealed class GameServerSummary
{
    public Guid Id { get; init; }

    public string ServerKey { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public string Region { get; init; } = string.Empty;

    public string Environment { get; init; } = string.Empty;

    public string BackendUrl { get; init; } = string.Empty;

    public string GraphqlUrl { get; init; } = string.Empty;

    public string FrontendUrl { get; init; } = string.Empty;

    public string Version { get; init; } = string.Empty;

    public int PlayerCount { get; init; }

    public int CompanyCount { get; init; }

    public long CurrentTick { get; init; }

    public DateTime RegisteredAtUtc { get; init; }

    public DateTime LastHeartbeatAtUtc { get; init; }

    public bool IsOnline { get; init; }
}

public sealed class MasterAuthPayload
{
    public string Token { get; set; } = string.Empty;

    public DateTime ExpiresAtUtc { get; set; }

    public MasterPlayerProfile Player { get; set; } = null!;
}

public sealed class MasterPlayerProfile
{
    public Guid Id { get; set; }

    public string Email { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }
}

public sealed class SubscriptionInfo
{
    public string Tier { get; set; } = "FREE";

    public string Status { get; set; } = "NONE";

    /// <summary>Null if the player has no active subscription.</summary>
    public DateTime? ExpiresAtUtc { get; set; }

    /// <summary>Null if the player has no active subscription.</summary>
    public DateTime? StartsAtUtc { get; set; }

    public bool IsActive { get; set; }

    /// <summary>Days remaining until expiry; null if no active subscription.</summary>
    public int? DaysRemaining { get; set; }

    /// <summary>Whether the player is eligible to prolong/renew.</summary>
    public bool CanProlong { get; set; }
}