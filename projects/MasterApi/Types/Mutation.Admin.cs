using System.Security.Claims;
using MasterApi.Configuration;
using MasterApi.Data;
using MasterApi.Data.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MasterApi.Types;

public sealed partial class Mutation
{
    public async Task<GlobalGameAdminGrantInfo> AssignGlobalGameAdmin(
        GlobalGameAdminGrantInput input,
        [Service] MasterDbContext db,
        [Service] IOptions<MasterServerOptions> masterServerOptions,
        [Service] IOptions<GameAdministrationOptions> gameAdministrationOptions)
    {
        Query.EnsureServiceAccess(input, masterServerOptions);

        var requesterEmail = Query.NormalizeEmail(input.RequesterEmail, "INVALID_REQUESTER_EMAIL");
        var targetEmail = Query.NormalizeEmail(input.TargetEmail, "INVALID_TARGET_EMAIL");
        var requesterAccess = await Query.BuildGameAdministrationAccessAsync(db, gameAdministrationOptions.Value, requesterEmail);
        if (!requesterAccess.IsRootAdministrator)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Only root administrators can assign the global game administrator role.")
                    .SetCode("ROOT_ADMIN_REQUIRED")
                    .Build());
        }

        var now = DateTime.UtcNow;
        var grant = await db.GlobalGameAdminGrants.FirstOrDefaultAsync(candidate => candidate.Email == targetEmail);
        if (grant is null)
        {
            grant = new GlobalGameAdminGrant
            {
                Id = Guid.NewGuid(),
                Email = targetEmail,
                GrantedByEmail = requesterEmail,
                GrantedAtUtc = now,
                UpdatedAtUtc = now,
            };
            db.GlobalGameAdminGrants.Add(grant);
        }
        else
        {
            grant.GrantedByEmail = requesterEmail;
            grant.UpdatedAtUtc = now;
        }

        await db.SaveChangesAsync();

        return new GlobalGameAdminGrantInfo
        {
            Id = grant.Id,
            Email = grant.Email,
            GrantedByEmail = grant.GrantedByEmail,
            GrantedAtUtc = grant.GrantedAtUtc,
            UpdatedAtUtc = grant.UpdatedAtUtc,
        };
    }

    public async Task<bool> RemoveGlobalGameAdmin(
        GlobalGameAdminGrantInput input,
        [Service] MasterDbContext db,
        [Service] IOptions<MasterServerOptions> masterServerOptions,
        [Service] IOptions<GameAdministrationOptions> gameAdministrationOptions)
    {
        Query.EnsureServiceAccess(input, masterServerOptions);

        var requesterEmail = Query.NormalizeEmail(input.RequesterEmail, "INVALID_REQUESTER_EMAIL");
        var targetEmail = Query.NormalizeEmail(input.TargetEmail, "INVALID_TARGET_EMAIL");
        var requesterAccess = await Query.BuildGameAdministrationAccessAsync(db, gameAdministrationOptions.Value, requesterEmail);
        if (!requesterAccess.IsRootAdministrator)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Only root administrators can remove the global game administrator role.")
                    .SetCode("ROOT_ADMIN_REQUIRED")
                    .Build());
        }

        var grant = await db.GlobalGameAdminGrants.FirstOrDefaultAsync(candidate => candidate.Email == targetEmail);
        if (grant is null)
        {
            return true;
        }

        db.GlobalGameAdminGrants.Remove(grant);
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<GameServerSummary> RegisterGameServer(
        RegisterGameServerInput input,
        [Service] MasterDbContext db,
        [Service] IOptions<MasterServerOptions> options)
    {
        var expectedKey = options.Value.RegistrationKey.Trim();
        if (string.IsNullOrWhiteSpace(expectedKey) || !string.Equals(expectedKey, input.RegistrationKey?.Trim(), StringComparison.Ordinal))
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Invalid game server registration key.")
                    .SetCode("INVALID_REGISTRATION_KEY")
                    .Build());
        }

        var serverKey = input.ServerKey.Trim();
        if (string.IsNullOrWhiteSpace(serverKey))
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Server key is required.")
                    .SetCode("SERVER_KEY_REQUIRED")
                    .Build());
        }

        var backendUrl = NormalizeRequiredUrl(input.BackendUrl, "BACKEND_URL_REQUIRED");
        var graphqlUrl = NormalizeRequiredUrl(input.GraphqlUrl, "GRAPHQL_URL_REQUIRED");
        var frontendUrl = NormalizeRequiredUrl(input.FrontendUrl, "FRONTEND_URL_REQUIRED");

        var now = DateTime.UtcNow;
        var server = await db.GameServers.FirstOrDefaultAsync(candidate => candidate.ServerKey == serverKey);

        if (server is null)
        {
            server = new GameServerNode
            {
                Id = Guid.NewGuid(),
                ServerKey = serverKey,
                RegisteredAtUtc = now,
            };

            db.GameServers.Add(server);
        }

        server.DisplayName = input.DisplayName.Trim();
        server.Description = input.Description?.Trim() ?? string.Empty;
        server.Region = input.Region.Trim();
        server.Environment = input.Environment.Trim();
        server.BackendUrl = backendUrl;
        server.GraphqlUrl = graphqlUrl;
        server.FrontendUrl = frontendUrl;
        server.Version = input.Version.Trim();
        server.PlayerCount = Math.Max(0, input.PlayerCount);
        server.CompanyCount = Math.Max(0, input.CompanyCount);
        server.CurrentTick = Math.Max(0, input.CurrentTick);
        server.LastHeartbeatAtUtc = now;
        server.UpdatedAtUtc = now;

        await db.SaveChangesAsync();

        return Query.ToSummary(server, now.AddSeconds(-Math.Max(5, options.Value.ActiveThresholdSeconds)));
    }
}
