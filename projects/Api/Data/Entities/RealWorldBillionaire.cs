namespace Api.Data.Entities;

/// <summary>
/// Real-world wealth benchmark rows used by the endgame win condition.
/// </summary>
public sealed class RealWorldBillionaire
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public decimal EstimatedNetWorthUsd { get; set; }

    public int Rank { get; set; }

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
