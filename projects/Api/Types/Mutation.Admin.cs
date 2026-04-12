using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Api.Data;
using Api.Data.Entities;
using Api.Engine;
using Api.Security;
using Api.Utilities;
using HotChocolate.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Api.Types;

public sealed partial class Mutation
{
    [Authorize]
    public async Task<GameAdminPlayerSummary> SetPlayerInvisibleInChat(
        SetPlayerInvisibleInChatInput input,
        [Service] AppDbContext db,
        [Service] IHttpContextAccessor httpContextAccessor,
        [Service] GameAdminAuthorizationService gameAdminAuthorizationService)
    {
        await gameAdminAuthorizationService.RequireAdminDashboardAccessAsync(db, httpContextAccessor.HttpContext!.User, httpContextAccessor.HttpContext.RequestAborted);
        var player = await db.Players
            .Include(candidate => candidate.Companies)
            .FirstOrDefaultAsync(candidate => candidate.Id == input.PlayerId, httpContextAccessor.HttpContext.RequestAborted)
            ?? throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Player not found.")
                    .SetCode("PLAYER_NOT_FOUND")
                    .Build());

        player.IsInvisibleInChat = input.IsInvisibleInChat;
        await db.SaveChangesAsync(httpContextAccessor.HttpContext.RequestAborted);

        return new GameAdminPlayerSummary
        {
            Id = player.Id,
            Email = player.Email,
            DisplayName = player.DisplayName,
            Role = player.Role,
            IsInvisibleInChat = player.IsInvisibleInChat,
            LastLoginAtUtc = player.LastLoginAtUtc,
            PersonalCash = player.PersonalCash,
            TotalCompanyCash = player.Companies.Sum(company => company.Cash),
            CompanyCount = player.Companies.Count,
            Companies = player.Companies.Select(company => new GameAdminCompanySummary
            {
                Id = company.Id,
                Name = company.Name,
                Cash = company.Cash,
            }).ToList(),
        };
    }

    [Authorize]
    public async Task<GameAdminPlayerSummary> SetLocalGameAdminRole(
        SetLocalGameAdminRoleInput input,
        [Service] AppDbContext db,
        [Service] IHttpContextAccessor httpContextAccessor,
        [Service] GameAdminAuthorizationService gameAdminAuthorizationService)
    {
        await gameAdminAuthorizationService.RequireRootAccessAsync(db, httpContextAccessor.HttpContext!.User, httpContextAccessor.HttpContext.RequestAborted);
        var player = await db.Players
            .Include(candidate => candidate.Companies)
            .FirstOrDefaultAsync(candidate => candidate.Id == input.PlayerId, httpContextAccessor.HttpContext.RequestAborted)
            ?? throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Player not found.")
                    .SetCode("PLAYER_NOT_FOUND")
                    .Build());

        player.Role = input.IsAdmin ? PlayerRole.Admin : PlayerRole.Player;
        await db.SaveChangesAsync(httpContextAccessor.HttpContext.RequestAborted);

        return new GameAdminPlayerSummary
        {
            Id = player.Id,
            Email = player.Email,
            DisplayName = player.DisplayName,
            Role = player.Role,
            IsInvisibleInChat = player.IsInvisibleInChat,
            LastLoginAtUtc = player.LastLoginAtUtc,
            PersonalCash = player.PersonalCash,
            TotalCompanyCash = player.Companies.Sum(company => company.Cash),
            CompanyCount = player.Companies.Count,
            Companies = player.Companies.Select(company => new GameAdminCompanySummary
            {
                Id = company.Id,
                Name = company.Name,
                Cash = company.Cash,
            }).ToList(),
        };
    }

    [Authorize]
    public async Task<GlobalGameAdminGrantSummary> AssignGlobalGameAdminRole(
        ManageGlobalGameAdminRoleInput input,
        [Service] AppDbContext db,
        [Service] IHttpContextAccessor httpContextAccessor,
        [Service] GameAdminAuthorizationService gameAdminAuthorizationService,
        [Service] IMasterGameAdministrationService masterGameAdministrationService)
    {
        var accessContext = await gameAdminAuthorizationService.RequireRootAccessAsync(db, httpContextAccessor.HttpContext!.User, httpContextAccessor.HttpContext.RequestAborted);
        return await masterGameAdministrationService.AssignGlobalGameAdminAsync(accessContext.ActorPlayer.Email, input.Email, httpContextAccessor.HttpContext.RequestAborted);
    }

    [Authorize]
    public async Task<bool> RemoveGlobalGameAdminRole(
        ManageGlobalGameAdminRoleInput input,
        [Service] AppDbContext db,
        [Service] IHttpContextAccessor httpContextAccessor,
        [Service] GameAdminAuthorizationService gameAdminAuthorizationService,
        [Service] IMasterGameAdministrationService masterGameAdministrationService)
    {
        var accessContext = await gameAdminAuthorizationService.RequireRootAccessAsync(db, httpContextAccessor.HttpContext!.User, httpContextAccessor.HttpContext.RequestAborted);
        await masterGameAdministrationService.RemoveGlobalGameAdminAsync(accessContext.ActorPlayer.Email, input.Email, httpContextAccessor.HttpContext.RequestAborted);
        return true;
    }

    [Authorize]
    public async Task<GameNewsEntryResult> UpsertGameNewsEntry(
        UpsertGameNewsEntryInput input,
        [Service] AppDbContext db,
        [Service] IHttpContextAccessor httpContextAccessor,
        [Service] GameAdminAuthorizationService gameAdminAuthorizationService,
        [Service] IMasterGameAdministrationService masterGameAdministrationService)
    {
        var accessContext = await gameAdminAuthorizationService.RequireAdminDashboardAccessAsync(db, httpContextAccessor.HttpContext!.User, httpContextAccessor.HttpContext.RequestAborted);
        return await masterGameAdministrationService.UpsertGameNewsEntryAsync(
            accessContext.ActorPlayer.Email,
            input.EntryId,
            input.EntryType,
            input.Status,
            input.Localizations,
            httpContextAccessor.HttpContext.RequestAborted);
    }

    [Authorize]
    public async Task<bool> MarkGameNewsRead(
        MarkGameNewsReadInput input,
        [Service] AppDbContext db,
        [Service] IHttpContextAccessor httpContextAccessor,
        [Service] IMasterGameAdministrationService masterGameAdministrationService)
    {
        var effectiveUserId = httpContextAccessor.HttpContext!.User.GetRequiredUserId();
        var playerEmail = await db.Players
            .AsNoTracking()
            .Where(player => player.Id == effectiveUserId)
            .Select(player => player.Email)
            .FirstOrDefaultAsync(httpContextAccessor.HttpContext.RequestAborted)
            ?? throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Player not found.")
                    .SetCode("PLAYER_NOT_FOUND")
                    .Build());

        await masterGameAdministrationService.MarkGameNewsReadAsync(playerEmail, input.EntryIds, httpContextAccessor.HttpContext.RequestAborted);
        return true;
    }
}
