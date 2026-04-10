using Api.Data;
using Api.Data.Entities;
using Api.Security;
using HotChocolate.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Api.Types;

/// <summary>
/// In-game chat queries.
/// Returns the shared server-wide chat feed, respecting invisible-player visibility rules.
/// </summary>
public sealed partial class Query
{
    /// <summary>
    /// Returns the latest shared in-game chat messages visible to the authenticated player.
    /// <para>
    /// Visibility rules:
    /// <list type="bullet">
    /// <item>Normal players see all messages from non-invisible players.</item>
    /// <item>Players marked as <see cref="Player.IsInvisibleInChat"/> are hidden from normal
    ///   players but still see their own messages.</item>
    /// <item>Administrators always see every message, including those from invisible players.</item>
    /// </list>
    /// </para>
    /// </summary>
    /// <param name="db">The game database context.</param>
    /// <param name="httpContextAccessor">Used to identify the currently authenticated player.</param>
    /// <param name="limit">
    /// Maximum number of messages to return. Clamped to [1, <c>MaxChatMessageLimit</c>].
    /// Defaults to <c>DefaultChatMessageLimit</c> when null.
    /// </param>
    [Authorize]
    public async Task<List<InGameChatMessage>> GetChatMessages(
        [Service] AppDbContext db,
        [Service] IHttpContextAccessor httpContextAccessor,
        int? limit)
    {
        var userId = httpContextAccessor.HttpContext!.User.GetRequiredUserId();
        var viewer = await db.Players
            .AsNoTracking()
            .FirstOrDefaultAsync(player => player.Id == userId);

        if (viewer is null)
        {
            return [];
        }

        var safeLimit = Math.Clamp(limit ?? DefaultChatMessageLimit, 1, MaxChatMessageLimit);
        var canSeeInvisible = viewer.Role == PlayerRole.Admin;

        var messages = await db.ChatMessages
            .AsNoTracking()
            .Include(message => message.Player)
            .Where(message => !message.Player.IsInvisibleInChat
                              || message.PlayerId == userId
                              || canSeeInvisible)
            .OrderByDescending(message => message.SentAtUtc)
            .Take(safeLimit)
            .OrderBy(message => message.SentAtUtc)
            .ToListAsync();

        return messages
            .Select(message => new InGameChatMessage
            {
                Id = message.Id,
                PlayerId = message.PlayerId,
                PlayerDisplayName = message.Player.DisplayName,
                Message = message.Message,
                SentAtUtc = message.SentAtUtc,
                IsOwnMessage = message.PlayerId == userId
            })
            .ToList();
    }
}
