using System.ComponentModel.DataAnnotations;

namespace Api.Data.Entities;

/// <summary>
/// A player-authored in-game chat message shown in the shared server chat.
/// </summary>
public sealed class ChatMessage
{
    public Guid Id { get; set; }

    public Guid PlayerId { get; set; }
    public Player Player { get; set; } = null!;

    [Required, MaxLength(300)]
    public string Message { get; set; } = string.Empty;

    public DateTime SentAtUtc { get; set; } = DateTime.UtcNow;
}
