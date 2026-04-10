using System.ComponentModel.DataAnnotations;

namespace Api.Data.Entities;

/// <summary>
/// A player-authored in-game chat message shown in the shared server-wide chat feed.
/// </summary>
/// <remarks>
/// Chat messages are append-only.  Players identified as
/// <see cref="Player.IsInvisibleInChat"/> are hidden from other players' feeds but
/// their messages are still visible to themselves.
/// </remarks>
public sealed class ChatMessage
{
    /// <summary>Primary key (GUID).</summary>
    public Guid Id { get; set; }

    /// <summary>The player who sent the message.</summary>
    public Guid PlayerId { get; set; }

    /// <summary>Navigation property to the author.</summary>
    public Player Player { get; set; } = null!;

    /// <summary>
    /// The text content of the message.  Maximum 300 characters.
    /// </summary>
    [Required, MaxLength(300)]
    public string Message { get; set; } = string.Empty;

    /// <summary>UTC timestamp when the message was recorded.</summary>
    public DateTime SentAtUtc { get; set; } = DateTime.UtcNow;
}
