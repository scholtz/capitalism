using System.Net.Http.Json;
using System.Reflection;
using Api.Configuration;
using Api.Data;
using Api.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Api.Utilities;

public sealed class MasterServerRegistrationHostedService(
    IServiceScopeFactory scopeFactory,
    IHttpClientFactory httpClientFactory,
    IOptions<MasterServerRegistrationOptions> options,
    ILogger<MasterServerRegistrationHostedService> logger) : BackgroundService
{
    private const string RegisterGameServerMutation = """
        mutation RegisterGameServer($input: RegisterGameServerInput!) {
          registerGameServer(input: $input) {
            id
            serverKey
            isOnline
          }
        }
        """;

    private bool _hasLoggedSuccessfulRegistration;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.IsConfigured())
        {
            logger.LogInformation("Master server registration is disabled or incomplete.");
            return;
        }

        await RegisterOnceAsync(stoppingToken);

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(10, options.Value.HeartbeatIntervalSeconds)));

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await RegisterOnceAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task RegisterOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            var payload = await BuildPayloadAsync(cancellationToken);
            var client = httpClientFactory.CreateClient("master-server");

            using var response = await client.PostAsJsonAsync(
                options.Value.ApiUrl,
                new GraphQlRequest
                {
                    Query = RegisterGameServerMutation,
                    Variables = new GraphQlVariables { Input = payload },
                },
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Master server registration returned HTTP {StatusCode}.",
                    (int)response.StatusCode);
                return;
            }

            var graphQlResponse = await response.Content.ReadFromJsonAsync<GraphQlResponse>(cancellationToken: cancellationToken);

            if (graphQlResponse?.Errors is { Count: > 0 })
            {
                logger.LogWarning(
                    "Master server registration was rejected: {Message}",
                    graphQlResponse.Errors[0].Message);
                return;
            }

            if (!_hasLoggedSuccessfulRegistration)
            {
                logger.LogInformation("Registered game server '{ServerKey}' with the master server.", payload.ServerKey);
                _hasLoggedSuccessfulRegistration = true;
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to register game server with the master server.");
        }
    }

    private async Task<MasterServerRegistrationPayload> BuildPayloadAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var gameState = await db.GameStates
            .AsNoTracking()
            .Select(state => new
            {
                state.CurrentTick,
                state.IsEnded,
                state.WinnerDisplayName,
                state.WinnerWealth,
            })
            .FirstOrDefaultAsync(cancellationToken);

        var playerCount = await db.Players
            .AsNoTracking()
            .CountAsync(player => player.Role == PlayerRole.Player, cancellationToken);

        var companyCount = await db.Companies
            .AsNoTracking()
            .CountAsync(cancellationToken);

        var registrationOptions = options.Value;
        var backendUrl = registrationOptions.BackendUrl.TrimEnd('/');
        var graphqlUrl = string.IsNullOrWhiteSpace(registrationOptions.GraphqlUrl)
            ? $"{backendUrl}/graphql"
            : registrationOptions.GraphqlUrl.TrimEnd('/');

        return new MasterServerRegistrationPayload
        {
            RegistrationKey = registrationOptions.RegistrationKey,
            ServerKey = registrationOptions.ServerKey,
            DisplayName = registrationOptions.DisplayName,
            Description = registrationOptions.Description,
            Region = registrationOptions.Region,
            Environment = registrationOptions.Environment,
            BackendUrl = backendUrl,
            FrontendUrl = registrationOptions.FrontendUrl.TrimEnd('/'),
            GraphqlUrl = graphqlUrl,
            Version = ResolveVersion(),
            PlayerCount = playerCount,
            CompanyCount = companyCount,
            CurrentTick = gameState?.CurrentTick ?? 0L,
            IsCompleted = gameState?.IsEnded ?? false,
            WinnerDisplayName = gameState?.WinnerDisplayName,
            WinnerWealth = gameState?.WinnerWealth,
        };
    }

    private static string ResolveVersion()
    {
        return Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? Assembly.GetEntryAssembly()?.GetName().Version?.ToString()
            ?? "dev";
    }

    private sealed class GraphQlRequest
    {
        public string Query { get; init; } = string.Empty;

        public GraphQlVariables Variables { get; init; } = new();
    }

    private sealed class GraphQlVariables
    {
        public MasterServerRegistrationPayload Input { get; init; } = new();
    }

    private sealed class GraphQlResponse
    {
        public List<GraphQlError> Errors { get; init; } = [];
    }

    private sealed class GraphQlError
    {
        public string Message { get; init; } = string.Empty;
    }

    private sealed class MasterServerRegistrationPayload
    {
        public string RegistrationKey { get; init; } = string.Empty;

        public string ServerKey { get; init; } = string.Empty;

        public string DisplayName { get; init; } = string.Empty;

        public string Description { get; init; } = string.Empty;

        public string Region { get; init; } = string.Empty;

        public string Environment { get; init; } = string.Empty;

        public string BackendUrl { get; init; } = string.Empty;

        public string GraphqlUrl { get; init; } = string.Empty;

        public string FrontendUrl { get; init; } = string.Empty;

        public string Version { get; init; } = string.Empty;

        public int PlayerCount { get; init; }

        public int CompanyCount { get; init; }

        public long CurrentTick { get; init; }

        public bool IsCompleted { get; init; }

        public string? WinnerDisplayName { get; init; }

        public decimal? WinnerWealth { get; init; }
    }
}
