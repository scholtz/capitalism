using System.Security.Claims;
using Api.Configuration;
using Api.Data;
using Api.Data.Entities;
using Api.Engine;
using Api.Security;
using Api.Utilities;
using HotChocolate.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Api.Types;

public sealed partial class Query
{
    public async Task<GameNewsFeedResult> GetGameNewsFeed(
        bool includeDrafts,
        [Service] AppDbContext db,
        [Service] IHttpContextAccessor httpContextAccessor,
        [Service] IMasterGameAdministrationService masterGameAdministrationService,
        [Service] GameAdminAuthorizationService gameAdminAuthorizationService,
        [Service] ILogger<Query> logger)
    {
        var principal = httpContextAccessor.HttpContext?.User;
        string? playerEmail = null;
        string? requesterEmail = null;

        if (principal?.Identity?.IsAuthenticated == true)
        {
            var effectiveUserId = principal.GetRequiredUserId();
            playerEmail = await db.Players
                .AsNoTracking()
                .Where(player => player.Id == effectiveUserId)
                .Select(player => player.Email)
                .FirstOrDefaultAsync(httpContextAccessor.HttpContext!.RequestAborted);
        }

        if (includeDrafts)
        {
            if (principal?.Identity?.IsAuthenticated != true)
            {
                throw new GraphQLException(
                    ErrorBuilder.New()
                        .SetMessage("Authentication is required to view draft news entries.")
                        .SetCode("NOT_AUTHENTICATED")
                        .Build());
            }

            var accessContext = await gameAdminAuthorizationService.RequireAdminDashboardAccessAsync(db, principal, httpContextAccessor.HttpContext!.RequestAborted);
            requesterEmail = accessContext.ActorPlayer.Email;
        }

        try
        {
            return await masterGameAdministrationService.GetGameNewsFeedAsync(
                playerEmail,
                includeDrafts,
                requesterEmail,
                httpContextAccessor.HttpContext?.RequestAborted ?? CancellationToken.None);
        }
        catch (Exception ex) when (ex is not GraphQLException and not OperationCanceledException)
        {
            // When the master API is temporarily unavailable, return an empty feed rather than
            // propagating the error so the frontend can show its empty state gracefully.
            // OperationCanceledException is intentionally re-thrown so that request cancellation
            // (browser tab closed, client timeout, navigation away) propagates correctly.
            logger.LogWarning(ex, "Failed to fetch news feed from master API; returning empty feed.");
            return new GameNewsFeedResult();
        }
    }

}
