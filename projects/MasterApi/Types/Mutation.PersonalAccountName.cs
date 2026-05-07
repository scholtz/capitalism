using System.Security.Claims;
using Microsoft.EntityFrameworkCore;

namespace MasterApi.Types;

public sealed partial class Mutation
{
    [HotChocolate.Authorization.Authorize]
    public async Task<MasterPlayerProfile> UpdatePersonalAccountName(
        UpdatePersonalAccountNameInput input,
        ClaimsPrincipal claimsPrincipal,
        [Service] Data.MasterDbContext db)
    {
        var player = await Query.GetCurrentUserAsync(claimsPrincipal, db)
            ?? throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Player not found.")
                    .SetCode("PLAYER_NOT_FOUND")
                    .Build());

        if (input.OnlyIfMissing && !string.IsNullOrWhiteSpace(player.PersonalAccountName))
        {
            return Query.ToProfile(player);
        }

        var normalized = NormalizePersonalAccountName(input.PersonalAccountName);
        var exists = await db.PlayerAccounts
            .AnyAsync(candidate =>
                candidate.Id != player.Id
                && candidate.PersonalAccountName != null
                && candidate.PersonalAccountName.ToLower() == normalized.ToLower());

        if (exists)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Personal account name is already taken.")
                    .SetCode("DUPLICATE_PERSONAL_ACCOUNT_NAME")
                    .Build());
        }

        player.PersonalAccountName = normalized;
        await db.SaveChangesAsync();
        return Query.ToProfile(player);
    }
}
