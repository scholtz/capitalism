using System.ComponentModel.DataAnnotations;

namespace Api.Data.Entities;

/// <summary>Stores quoted share prices over time for stock-exchange history displays.</summary>
public sealed class SharePriceHistoryEntry
{
    public Guid Id { get; set; }

    public Guid CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    [Range(0, double.MaxValue)]
    public decimal SharePrice { get; set; }

    public long RecordedAtTick { get; set; }

    public DateTime RecordedAtUtc { get; set; } = DateTime.UtcNow;
}
