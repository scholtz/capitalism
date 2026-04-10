using Api.Data;
using Api.Data.Entities;
using Api.Security;
using HotChocolate.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Api.Types;

/// <summary>
/// In-game chat mutations.
/// Provides a server-wide shared chat feed authenticated players can post to.
/// </summary>
public sealed partial class Mutation
{
    /// <summary>
    /// Sends a shared in-game chat message authored by the authenticated player.
    /// </summary>
    /// <remarks>
    /// Players marked as <see cref="Player.IsInvisibleInChat"/> remain hidden from
    /// regular players' chat feeds, but their messages are still stored and visible
    /// to themselves and administrators.
    /// </remarks>
    /// <param name="input">The message payload; content is trimmed before storage.</param>
    /// <param name="db">The game database context.</param>
    /// <param name="httpContextAccessor">Used to identify the sender.</param>
    /// <returns>The persisted chat message including the sender's display name.</returns>
    /// <exception cref="GraphQLException">
    /// Thrown with code <c>PLAYER_NOT_FOUND</c> if the caller's player record does not exist,
    /// or <c>CHAT_MESSAGE_EMPTY</c> if the trimmed message is blank.
    /// </exception>
    [Authorize]
    public async Task<InGameChatMessage> SendChatMessage(
        SendChatMessageInput input,
        [Service] AppDbContext db,
        [Service] IHttpContextAccessor httpContextAccessor)
    {
        var userId = httpContextAccessor.HttpContext!.User.GetRequiredUserId();
        var player = await db.Players.FirstOrDefaultAsync(candidate => candidate.Id == userId);
        if (player is null)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Player not found.")
                    .SetCode("PLAYER_NOT_FOUND")
                    .Build());
        }

        var message = input.Message.Trim();
        if (string.IsNullOrWhiteSpace(message))
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Chat message cannot be empty.")
                    .SetCode("CHAT_MESSAGE_EMPTY")
                    .Build());
        }

        var chatMessage = new ChatMessage
        {
            Id = Guid.NewGuid(),
            PlayerId = player.Id,
            Message = message,
            SentAtUtc = DateTime.UtcNow
        };

        db.ChatMessages.Add(chatMessage);
        await db.SaveChangesAsync();

        return new InGameChatMessage
        {
            Id = chatMessage.Id,
            PlayerId = player.Id,
            PlayerDisplayName = player.DisplayName,
            Message = chatMessage.Message,
            SentAtUtc = chatMessage.SentAtUtc,
            IsOwnMessage = true
        };
    }
}
