using System.Security.Claims;
using Microsoft.EntityFrameworkCore;

namespace MasterApi.Types;

public sealed partial class Mutation
{
    [HotChocolate.Authorization.Authorize]
    public async Task<MasterPlayerProfile> SetPersonalAccountName(
        string name,
        ClaimsPrincipal claimsPrincipal,
        [Service] Data.MasterDbContext db)
        => await SavePersonalAccountNameAsync(name, onlyIfMissing: false, claimsPrincipal, db);

    [HotChocolate.Authorization.Authorize]
    public async Task<MasterPlayerProfile> UpdatePersonalAccountName(
        UpdatePersonalAccountNameInput input,
        ClaimsPrincipal claimsPrincipal,
        [Service] Data.MasterDbContext db)
        => await SavePersonalAccountNameAsync(input.PersonalAccountName, input.OnlyIfMissing, claimsPrincipal, db);

    private static async Task<MasterPlayerProfile> SavePersonalAccountNameAsync(
        string personalAccountName,
        bool onlyIfMissing,
        ClaimsPrincipal claimsPrincipal,
        Data.MasterDbContext db)
    {
        var player = await Query.GetCurrentUserAsync(claimsPrincipal, db)
            ?? throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Player not found.")
                    .SetCode("PLAYER_NOT_FOUND")
                    .Build());

        if (onlyIfMissing && !string.IsNullOrWhiteSpace(player.PersonalAccountName))
        {
            return Query.ToProfile(player);
        }

        var normalized = NormalizePersonalAccountName(personalAccountName);
        player.PersonalAccountName = normalized;
        await db.SaveChangesAsync();
        return Query.ToProfile(player);
    }
}
