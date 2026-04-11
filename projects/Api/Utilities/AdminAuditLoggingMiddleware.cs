using System.Text.Json;
using Api.Data;
using Api.Data.Entities;
using Api.Security;
using Microsoft.EntityFrameworkCore;

namespace Api.Utilities;

public sealed class AdminAuditLoggingMiddleware(RequestDelegate next)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    public async Task InvokeAsync(HttpContext context, AppDbContext db)
    {
        if (!ShouldInspect(context))
        {
            await next(context);
            return;
        }

        context.Request.EnableBuffering();

        var requestBody = await JsonSerializer.DeserializeAsync<GraphQlHttpRequest>(context.Request.Body, JsonOptions)
            ?? new GraphQlHttpRequest();
        context.Request.Body.Position = 0;

        var query = requestBody.Query?.Trim() ?? string.Empty;
        if (!query.Contains("mutation", StringComparison.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }

        await next(context);

        db.ChangeTracker.Clear();

        var adminActorId = context.User.GetAuthenticatedActorUserId();
        var effectivePlayerId = context.User.GetRequiredUserId();
        var effectiveCompanyId = context.User.GetEffectiveCompanyId();

        var players = await db.Players
            .AsNoTracking()
            .Where(player => player.Id == adminActorId || player.Id == effectivePlayerId)
            .ToListAsync(context.RequestAborted);

        var adminActor = players.FirstOrDefault(player => player.Id == adminActorId);
        var effectivePlayer = players.FirstOrDefault(player => player.Id == effectivePlayerId);
        if (adminActor is null || effectivePlayer is null)
        {
            return;
        }

        string? effectiveCompanyName = null;
        if (effectiveCompanyId.HasValue)
        {
            effectiveCompanyName = await db.Companies
                .AsNoTracking()
                .Where(company => company.Id == effectiveCompanyId.Value)
                .Select(company => company.Name)
                .FirstOrDefaultAsync(context.RequestAborted);
        }

        db.AdminActionAuditLogs.Add(new AdminActionAuditLog
        {
            Id = Guid.NewGuid(),
            AdminActorPlayerId = adminActor.Id,
            AdminActorEmail = adminActor.Email,
            AdminActorDisplayName = adminActor.DisplayName,
            EffectivePlayerId = effectivePlayer.Id,
            EffectivePlayerEmail = effectivePlayer.Email,
            EffectivePlayerDisplayName = effectivePlayer.DisplayName,
            EffectiveAccountType = context.User.GetEffectiveAccountType() ?? AccountContextType.Person,
            EffectiveCompanyId = effectiveCompanyId,
            EffectiveCompanyName = effectiveCompanyName,
            GraphQlOperationName = requestBody.OperationName?.Trim() ?? string.Empty,
            MutationSummary = BuildMutationSummary(query),
            ResponseStatusCode = context.Response.StatusCode,
            RecordedAtUtc = DateTime.UtcNow,
        });

        await db.SaveChangesAsync(context.RequestAborted);
    }

    private static bool ShouldInspect(HttpContext context)
    {
        return HttpMethods.IsPost(context.Request.Method)
            && context.Request.Path.Equals("/graphql", StringComparison.OrdinalIgnoreCase)
            && context.User.Identity?.IsAuthenticated == true
            && context.User.IsImpersonating();
    }

    private static string BuildMutationSummary(string query)
    {
        var normalized = query.Replace('\n', ' ').Replace('\r', ' ').Trim();
        return normalized.Length <= 500 ? normalized : normalized[..500];
    }

    private sealed class GraphQlHttpRequest
    {
        public string? OperationName { get; init; }

        public string? Query { get; init; }
    }
}
