namespace MasterApi.Data.Entities;

public sealed class GameServerNode
{
    public Guid Id { get; set; }

    public string ServerKey { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string Region { get; set; } = string.Empty;

    public string Environment { get; set; } = string.Empty;

    public string BackendUrl { get; set; } = string.Empty;

    public string GraphqlUrl { get; set; } = string.Empty;

    public string FrontendUrl { get; set; } = string.Empty;

    public string Version { get; set; } = string.Empty;

    public int PlayerCount { get; set; }

    public int CompanyCount { get; set; }

    public long CurrentTick { get; set; }

    public bool IsCompleted { get; set; }

    public string? WinnerDisplayName { get; set; }

    public decimal? WinnerWealth { get; set; }

    public DateTime RegisteredAtUtc { get; set; }

    public DateTime LastHeartbeatAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}
