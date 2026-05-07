using System.ComponentModel.DataAnnotations.Schema;
using Api.Engine;
using Api.Utilities;

namespace Api.Data.Entities;

/// <summary>
/// Tracks the current game tick and global state.
/// Only one row should exist in this table.
/// </summary>
public sealed class GameState
{
    /// <summary>Singleton row identifier (always 1).</summary>
    public int Id { get; set; } = 1;

    /// <summary>Current game tick number. Incremented by the game engine each cycle.</summary>
    public long CurrentTick { get; set; }

    /// <summary>UTC timestamp of the last tick processing.</summary>
    public DateTime LastTickAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>UTC timestamp when this game shard was started.</summary>
    public DateTime StartedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Interval in seconds between ticks.</summary>
    public int TickIntervalSeconds { get; set; } = 60;

    /// <summary>Ticks between tax calculation cycles.</summary>
    public int TaxCycleTicks { get; set; } = GameConstants.TicksPerYear;

    /// <summary>Global tax rate percentage (0-100).</summary>
    public decimal TaxRate { get; set; } = 15m;

    /// <summary>True when the endgame win condition was reached and the shard has ended.</summary>
    public bool IsEnded { get; set; }

    /// <summary>UTC timestamp when the shard was marked completed.</summary>
    public DateTime? EndedAtUtc { get; set; }

    /// <summary>Winning player's identifier.</summary>
    public Guid? WinnerPlayerId { get; set; }

    /// <summary>Winning player's display name at game end.</summary>
    public string? WinnerDisplayName { get; set; }

    /// <summary>Winner final personal-account wealth at game end.</summary>
    public decimal? WinnerWealth { get; set; }

    /// <summary>Name of the real-world target that was surpassed.</summary>
    public string? WinningTargetName { get; set; }

    /// <summary>Estimated net worth of the surpassed real-world target.</summary>
    public decimal? WinningTargetWealth { get; set; }

    [NotMapped]
    public int CurrentGameYear => GameTime.GetGameYear(CurrentTick);

    [NotMapped]
    public DateTime CurrentGameTimeUtc => GameTime.GetInGameTimeUtc(CurrentTick);

    [NotMapped]
    public int TicksPerDay => GameConstants.TicksPerDay;

    [NotMapped]
    public int TicksPerYear => GameConstants.TicksPerYear;

    [NotMapped]
    public long NextTaxTick => GameTime.GetNextTaxTick(CurrentTick, TaxCycleTicks);

    [NotMapped]
    public DateTime NextTaxGameTimeUtc => GameTime.GetInGameTimeUtc(NextTaxTick);

    [NotMapped]
    public int NextTaxGameYear => GameTime.GetGameYear(NextTaxTick);
}
