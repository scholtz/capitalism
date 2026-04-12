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
    /// <summary>Saves (creates or updates) a reusable building layout template.</summary>
    [HotChocolate.Authorization.Authorize]
    public async Task<BuildingLayoutTemplateInfo> SaveBuildingLayout(
        SaveBuildingLayoutInput input,
        ClaimsPrincipal claimsPrincipal,
        [Service] MasterDbContext db)
    {
        var player = await Query.GetCurrentUserAsync(claimsPrincipal, db)
            ?? throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Player not found.")
                    .SetCode("PLAYER_NOT_FOUND")
                    .Build());
        var userId = player.Id;

        if (string.IsNullOrWhiteSpace(input.Name))
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Layout name is required.")
                    .SetCode("LAYOUT_NAME_REQUIRED")
                    .Build());
        }

        if (string.IsNullOrWhiteSpace(input.BuildingType))
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Building type is required.")
                    .SetCode("BUILDING_TYPE_REQUIRED")
                    .Build());
        }

        var unitsJson = input.UnitsJson ?? "[]";
        if (unitsJson.Length > 32_768)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("UnitsJson payload exceeds the 32 KB limit.")
                    .SetCode("UNITS_JSON_TOO_LARGE")
                    .Build());
        }

        try
        {
            System.Text.Json.JsonDocument.Parse(unitsJson);
        }
        catch (System.Text.Json.JsonException)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("UnitsJson must be valid JSON.")
                    .SetCode("UNITS_JSON_INVALID")
                    .Build());
        }

        var now = DateTime.UtcNow;

        if (input.ExistingId.HasValue)
        {
            var existing = await db.BuildingLayoutTemplates
                .FirstOrDefaultAsync(l => l.Id == input.ExistingId.Value && l.PlayerAccountId == userId);

            if (existing is null)
            {
                throw new GraphQLException(
                    ErrorBuilder.New()
                        .SetMessage("Layout template not found or you do not own it.")
                        .SetCode("LAYOUT_NOT_FOUND")
                        .Build());
            }

            existing.Name = input.Name.Trim();
            existing.Description = string.IsNullOrWhiteSpace(input.Description) ? null : input.Description.Trim();
            existing.BuildingType = input.BuildingType.Trim().ToUpperInvariant();
            existing.UnitsJson = unitsJson;
            existing.UpdatedAtUtc = now;

            await db.SaveChangesAsync();

            return new BuildingLayoutTemplateInfo
            {
                Id = existing.Id,
                Name = existing.Name,
                Description = existing.Description,
                BuildingType = existing.BuildingType,
                UnitsJson = existing.UnitsJson,
                CreatedAtUtc = existing.CreatedAtUtc,
                UpdatedAtUtc = existing.UpdatedAtUtc,
            };
        }
        else
        {
            var layout = new Data.Entities.BuildingLayoutTemplate
            {
                Id = Guid.NewGuid(),
                PlayerAccountId = userId,
                Name = input.Name.Trim(),
                Description = string.IsNullOrWhiteSpace(input.Description) ? null : input.Description.Trim(),
                BuildingType = input.BuildingType.Trim().ToUpperInvariant(),
                UnitsJson = unitsJson,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
            };

            db.BuildingLayoutTemplates.Add(layout);
            await db.SaveChangesAsync();

            return new BuildingLayoutTemplateInfo
            {
                Id = layout.Id,
                Name = layout.Name,
                Description = layout.Description,
                BuildingType = layout.BuildingType,
                UnitsJson = layout.UnitsJson,
                CreatedAtUtc = layout.CreatedAtUtc,
                UpdatedAtUtc = layout.UpdatedAtUtc,
            };
        }
    }

    /// <summary>Deletes a reusable building layout template owned by the current user.</summary>
    [HotChocolate.Authorization.Authorize]
    public async Task<bool> DeleteBuildingLayout(
        DeleteBuildingLayoutInput input,
        ClaimsPrincipal claimsPrincipal,
        [Service] MasterDbContext db)
    {
        var player = await Query.GetCurrentUserAsync(claimsPrincipal, db)
            ?? throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Player not found.")
                    .SetCode("PLAYER_NOT_FOUND")
                    .Build());
        var userId = player.Id;

        var layout = await db.BuildingLayoutTemplates
            .FirstOrDefaultAsync(l => l.Id == input.Id && l.PlayerAccountId == userId);

        if (layout is null)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Layout template not found or you do not own it.")
                    .SetCode("LAYOUT_NOT_FOUND")
                    .Build());
        }

        db.BuildingLayoutTemplates.Remove(layout);
        await db.SaveChangesAsync();
        return true;
    }
}
