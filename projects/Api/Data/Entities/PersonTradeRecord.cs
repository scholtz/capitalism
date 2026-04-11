using System.ComponentModel.DataAnnotations;

namespace Api.Data.Entities;

/// <summary>Records a stock-exchange buy or sell executed from the player's personal account.</summary>
public sealed class PersonTradeRecord
{
    public Guid Id { get; set; }

    public Guid PlayerId { get; set; }
    public Player Player { get; set; } = null!;

    public Guid CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    [Required, MaxLength(4)]
    public string Direction { get; set; } = string.Empty; // "BUY" or "SELL"

    [Range(0, double.MaxValue)]
    public decimal ShareCount { get; set; }

    [Range(0, double.MaxValue)]
    public decimal PricePerShare { get; set; }

    [Range(0, double.MaxValue)]
    public decimal TotalValue { get; set; }

    public long RecordedAtTick { get; set; }

    public DateTime RecordedAtUtc { get; set; } = DateTime.UtcNow;
}

public static class TradeDirection
{
    public const string Buy = "BUY";
    public const string Sell = "SELL";
}
