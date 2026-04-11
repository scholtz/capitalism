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

    public DateTime? StartupPackClaimedAtUtc { get; set; }

    public bool CanClaimStartupPack { get; set; }
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

public class MasterServerServiceInput
{
    public string RegistrationKey { get; set; } = string.Empty;

    public string ServerKey { get; set; } = string.Empty;
}

public sealed class GetGameAdministrationAccessInput : MasterServerServiceInput
{
    public string Email { get; set; } = string.Empty;
}

public sealed class GlobalGameAdminGrantInput : MasterServerServiceInput
{
    public string RequesterEmail { get; set; } = string.Empty;

    public string TargetEmail { get; set; } = string.Empty;
}

public sealed class GetGlobalGameAdminGrantsInput : MasterServerServiceInput
{
    public string RequesterEmail { get; set; } = string.Empty;
}

public sealed class GetGameNewsFeedInput : MasterServerServiceInput
{
    public string? PlayerEmail { get; set; }

    public int Limit { get; set; } = 50;

    public bool IncludeDrafts { get; set; }

    public string? RequesterEmail { get; set; }
}

public sealed class MarkGameNewsReadInput : MasterServerServiceInput
{
    public string PlayerEmail { get; set; } = string.Empty;

    public List<Guid> EntryIds { get; set; } = [];
}

public sealed class UpsertGameNewsEntryInput : MasterServerServiceInput
{
    public Guid? EntryId { get; set; }

    public string RequesterEmail { get; set; } = string.Empty;

    public string EntryType { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public string? TargetServerKey { get; set; }

    public List<GameNewsLocalizationInput> Localizations { get; set; } = [];
}

public sealed class GameNewsLocalizationInput
{
    public string Locale { get; set; } = "en";

    public string Title { get; set; } = string.Empty;

    public string Summary { get; set; } = string.Empty;

    public string HtmlContent { get; set; } = string.Empty;
}

public sealed class GameAdministrationAccessInfo
{
    public string Email { get; set; } = string.Empty;

    public bool IsRootAdministrator { get; set; }

    public bool HasGlobalAdminRole { get; set; }

    public bool CanAccessEveryGameDashboard { get; set; }
}

public sealed class GlobalGameAdminGrantInfo
{
    public Guid Id { get; set; }

    public string Email { get; set; } = string.Empty;

    public string GrantedByEmail { get; set; } = string.Empty;

    public DateTime GrantedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}

public sealed class GameNewsFeedResult
{
    public int UnreadCount { get; set; }

    public List<GameNewsEntryInfo> Items { get; set; } = [];
}

public sealed class GameNewsEntryInfo
{
    public Guid Id { get; set; }

    public string EntryType { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public string? TargetServerKey { get; set; }

    public string CreatedByEmail { get; set; } = string.Empty;

    public string UpdatedByEmail { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public DateTime? PublishedAtUtc { get; set; }

    public bool IsRead { get; set; }

    public List<GameNewsLocalizationInfo> Localizations { get; set; } = [];
}

public sealed class GameNewsLocalizationInfo
{
    public string Locale { get; set; } = "en";

    public string Title { get; set; } = string.Empty;

    public string Summary { get; set; } = string.Empty;

    public string HtmlContent { get; set; } = string.Empty;
}

public sealed class BuildingLayoutTemplateInfo
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string BuildingType { get; set; } = string.Empty;
    public string UnitsJson { get; set; } = "[]";
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

public sealed class SaveBuildingLayoutInput
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string BuildingType { get; set; } = string.Empty;
    public string UnitsJson { get; set; } = "[]";
    /// <summary>If provided, update the existing template instead of creating a new one.</summary>
    public Guid? ExistingId { get; set; }
}

public sealed class DeleteBuildingLayoutInput
{
    public Guid Id { get; set; }
}