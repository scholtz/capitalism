using Api.Data.Entities;

namespace Api.Types;

public sealed class GameNewsFeedResult
{
    public int UnreadCount { get; set; }

    public List<GameNewsEntryResult> Items { get; set; } = [];
}

public sealed class GameNewsEntryResult
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

    public List<GameNewsLocalizationResult> Localizations { get; set; } = [];
}

public sealed class GameNewsLocalizationResult
{
    public string Locale { get; set; } = "en";

    public string Title { get; set; } = string.Empty;

    public string Summary { get; set; } = string.Empty;

    public string HtmlContent { get; set; } = string.Empty;
}

public sealed class GameAdminSessionInfo
{
    public bool IsLocalAdmin { get; set; }

    public bool HasGlobalAdminRole { get; set; }

    public bool IsRootAdministrator { get; set; }

    public bool CanAccessAdminDashboard { get; set; }

    public bool IsImpersonating { get; set; }

    public GameAdminPlayerSummary AdminActor { get; set; } = null!;

    public GameAdminPlayerSummary? EffectivePlayer { get; set; }

    public string EffectiveAccountType { get; set; } = AccountContextType.Person;

    public Guid? EffectiveCompanyId { get; set; }

    public string? EffectiveCompanyName { get; set; }
}

public sealed class GameAdminDashboardResult
{
    public string ServerKey { get; set; } = string.Empty;

    public decimal TotalPersonalCash { get; set; }

    public decimal TotalCompanyCash { get; set; }

    public decimal MoneySupply { get; set; }

    public decimal ExternalMoneyInflowLast100Ticks { get; set; }

    public decimal TotalShippingCostsLast100Ticks { get; set; }

    public List<GameAdminMoneyInflowSummary> InflowSummaries { get; set; } = [];

    public List<GameAdminShippingCostSummary> ShippingCostSummaries { get; set; } = [];

    public List<GameAdminMultiAccountAlert> MultiAccountAlerts { get; set; } = [];

    public List<GameAdminPlayerSummary> Players { get; set; } = [];

    public List<GameAdminPlayerSummary> InvisiblePlayers { get; set; } = [];

    public List<GlobalGameAdminGrantSummary> GlobalGameAdminGrants { get; set; } = [];

    public List<GameAdminAuditLogRecord> RecentAuditLogs { get; set; } = [];
}

public sealed class GameAdminShippingCostSummary
{
    public Guid CompanyId { get; set; }

    public string CompanyName { get; set; } = string.Empty;

    public decimal Amount { get; set; }

    public int EntryCount { get; set; }
}

public sealed class GameAdminMoneyInflowSummary
{
    public string Category { get; set; } = string.Empty;

    public decimal Amount { get; set; }

    public string Description { get; set; } = string.Empty;
}

public sealed class GameAdminMultiAccountAlert
{
    public string Reason { get; set; } = string.Empty;

    public decimal ExposureAmount { get; set; }

    public decimal ConfidenceScore { get; set; }

    public string SupportingEntityType { get; set; } = string.Empty;

    public string SupportingEntityName { get; set; } = string.Empty;

    public GameAdminPlayerSummary PrimaryPlayer { get; set; } = null!;

    public GameAdminPlayerSummary RelatedPlayer { get; set; } = null!;
}

public sealed class GameAdminPlayerSummary
{
    public Guid Id { get; set; }

    public string Email { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string Role { get; set; } = PlayerRole.Player;

    public bool IsInvisibleInChat { get; set; }

    public DateTime? LastLoginAtUtc { get; set; }

    public decimal PersonalCash { get; set; }

    public decimal TotalCompanyCash { get; set; }

    public int CompanyCount { get; set; }

    public List<GameAdminCompanySummary> Companies { get; set; } = [];
}

public sealed class GameAdminCompanySummary
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public decimal Cash { get; set; }
}

public sealed class GlobalGameAdminGrantSummary
{
    public Guid Id { get; set; }

    public string Email { get; set; } = string.Empty;

    public string GrantedByEmail { get; set; } = string.Empty;

    public DateTime GrantedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}

public sealed class GameAdminAuditLogRecord
{
    public Guid Id { get; set; }

    public Guid AdminActorPlayerId { get; set; }

    public string AdminActorEmail { get; set; } = string.Empty;

    public string AdminActorDisplayName { get; set; } = string.Empty;

    public Guid EffectivePlayerId { get; set; }

    public string EffectivePlayerEmail { get; set; } = string.Empty;

    public string EffectivePlayerDisplayName { get; set; } = string.Empty;

    public string EffectiveAccountType { get; set; } = AccountContextType.Person;

    public Guid? EffectiveCompanyId { get; set; }

    public string? EffectiveCompanyName { get; set; }

    public string GraphQlOperationName { get; set; } = string.Empty;

    public string MutationSummary { get; set; } = string.Empty;

    public int ResponseStatusCode { get; set; }

    public DateTime RecordedAtUtc { get; set; }
}
