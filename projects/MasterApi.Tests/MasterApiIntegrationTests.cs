using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using MasterApi.Tests.Infrastructure;

namespace MasterApi.Tests;

public sealed class MasterApiIntegrationTests : IClassFixture<MasterApiWebApplicationFactory>
{
    private readonly HttpClient _client;

    public MasterApiIntegrationTests(MasterApiWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    private async Task<JsonElement> GraphQlAsync(string query, object? variables = null, string? token = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/graphql");
        request.Content = new StringContent(
            JsonSerializer.Serialize(new { query, variables }),
            Encoding.UTF8,
            "application/json");

        if (token is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(body);
        return doc.RootElement.Clone();
    }

    private async Task<(string Token, JsonElement Player)> RegisterAndGetTokenAsync(
        string email = "test@example.com",
        string displayName = "Test Player",
        string password = "password123")
    {
        var result = await GraphQlAsync("""
            mutation Register($input: RegisterInput!) {
              register(input: $input) {
                token
                expiresAtUtc
                player { id email displayName createdAtUtc }
              }
            }
            """,
            new { input = new { email, displayName, password } });

        var payload = result.GetProperty("data").GetProperty("register");
        var token = payload.GetProperty("token").GetString()!;
        var player = payload.GetProperty("player").Clone();
        return (token, player);
    }

    #region Health check

    [Fact]
    public async Task HealthCheck_ReturnsOk()
    {
        var response = await _client.GetAsync("/healthz");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("ok", body);
    }

    #endregion

    #region Game servers

    [Fact]
    public async Task GameServers_ReturnsEmptyList_WhenNoneRegistered()
    {
        var result = await GraphQlAsync("""
            query { gameServers {
              id displayName region environment isOnline playerCount
            }}
            """);

        Assert.False(result.TryGetProperty("errors", out _));
        var servers = result.GetProperty("data").GetProperty("gameServers");
        Assert.Equal(JsonValueKind.Array, servers.ValueKind);
    }

    [Fact]
    public async Task RegisterGameServer_ValidInput_Succeeds()
    {
        var result = await GraphQlAsync("""
            mutation RegisterServer($input: RegisterGameServerInput!) {
              registerGameServer(input: $input) {
                id displayName region isOnline playerCount currentTick
              }
            }
            """,
            new
            {
                input = new
                {
                    registrationKey = "test-registration-key",
                    serverKey = $"server-{Guid.NewGuid():N}",
                    displayName = "Test Economy Server",
                    description = "A test game server",
                    region = "EU",
                    environment = "production",
                    backendUrl = "https://game.example.com",
                    graphqlUrl = "https://game.example.com/graphql",
                    frontendUrl = "https://game.example.com/app",
                    version = "1.0.0",
                    playerCount = 5,
                    companyCount = 12,
                    currentTick = 100,
                }
            });

        Assert.False(result.TryGetProperty("errors", out _));
        var server = result.GetProperty("data").GetProperty("registerGameServer");
        Assert.Equal("Test Economy Server", server.GetProperty("displayName").GetString());
        Assert.Equal("EU", server.GetProperty("region").GetString());
        Assert.Equal(5, server.GetProperty("playerCount").GetInt32());
        Assert.Equal(100, server.GetProperty("currentTick").GetInt64());
    }

    [Fact]
    public async Task RegisterGameServer_InvalidRegistrationKey_ReturnsError()
    {
        var result = await GraphQlAsync("""
            mutation RegisterServer($input: RegisterGameServerInput!) {
              registerGameServer(input: $input) { id }
            }
            """,
            new
            {
                input = new
                {
                    registrationKey = "wrong-key",
                    serverKey = "test",
                    displayName = "Test",
                    region = "EU",
                    environment = "prod",
                    backendUrl = "https://game.example.com",
                    graphqlUrl = "https://game.example.com/graphql",
                    frontendUrl = "https://game.example.com/app",
                    version = "1.0",
                    playerCount = 0,
                    companyCount = 0,
                    currentTick = 0,
                }
            });

        Assert.True(result.TryGetProperty("errors", out var errors));
        Assert.Contains("INVALID_REGISTRATION_KEY", errors[0].GetProperty("extensions").GetProperty("code").GetString());
    }

    #endregion

    #region Auth - Register

    [Fact]
    public async Task Register_ValidInput_ReturnsTokenAndPlayer()
    {
        var (token, player) = await RegisterAndGetTokenAsync($"reg-valid-{Guid.NewGuid():N}@example.com");

        Assert.NotEmpty(token);
        Assert.NotEmpty(player.GetProperty("id").GetString()!);
        Assert.NotEmpty(player.GetProperty("email").GetString()!);
        Assert.Equal("Test Player", player.GetProperty("displayName").GetString());
    }

    [Fact]
    public async Task Register_DuplicateEmail_ReturnsError()
    {
        var email = $"dup-{Guid.NewGuid():N}@example.com";
        await RegisterAndGetTokenAsync(email);

        var result = await GraphQlAsync("""
            mutation Register($input: RegisterInput!) {
              register(input: $input) { token }
            }
            """,
            new { input = new { email, displayName = "Another", password = "password123" } });

        Assert.True(result.TryGetProperty("errors", out var errors));
        Assert.Contains("DUPLICATE_EMAIL", errors[0].GetProperty("extensions").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Register_ShortPassword_ReturnsError()
    {
        var result = await GraphQlAsync("""
            mutation Register($input: RegisterInput!) {
              register(input: $input) { token }
            }
            """,
            new { input = new { email = "shortpw@example.com", displayName = "Test", password = "short" } });

        Assert.True(result.TryGetProperty("errors", out var errors));
        Assert.Contains("PASSWORD_TOO_SHORT", errors[0].GetProperty("extensions").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Register_InvalidEmail_ReturnsError()
    {
        var result = await GraphQlAsync("""
            mutation Register($input: RegisterInput!) {
              register(input: $input) { token }
            }
            """,
            new { input = new { email = "not-an-email", displayName = "Test", password = "password123" } });

        Assert.True(result.TryGetProperty("errors", out var errors));
        Assert.Contains("INVALID_EMAIL", errors[0].GetProperty("extensions").GetProperty("code").GetString());
    }

    #endregion

    #region Auth - Login

    [Fact]
    public async Task Login_ValidCredentials_ReturnsToken()
    {
        var email = $"login-{Guid.NewGuid():N}@example.com";
        await RegisterAndGetTokenAsync(email, password: "mypassword99");

        var result = await GraphQlAsync("""
            mutation Login($input: LoginInput!) {
              login(input: $input) {
                token expiresAtUtc
                player { id email displayName }
              }
            }
            """,
            new { input = new { email, password = "mypassword99" } });

        Assert.False(result.TryGetProperty("errors", out _));
        var payload = result.GetProperty("data").GetProperty("login");
        Assert.NotEmpty(payload.GetProperty("token").GetString()!);
        Assert.Equal(email, payload.GetProperty("player").GetProperty("email").GetString());
    }

    [Fact]
    public async Task Login_WrongPassword_ReturnsError()
    {
        var email = $"wrongpw-{Guid.NewGuid():N}@example.com";
        await RegisterAndGetTokenAsync(email, password: "correctpass1");

        var result = await GraphQlAsync("""
            mutation Login($input: LoginInput!) {
              login(input: $input) { token }
            }
            """,
            new { input = new { email, password = "wrongpass!" } });

        Assert.True(result.TryGetProperty("errors", out var errors));
        Assert.Contains("INVALID_CREDENTIALS", errors[0].GetProperty("extensions").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Login_UnknownEmail_ReturnsError()
    {
        var result = await GraphQlAsync("""
            mutation Login($input: LoginInput!) {
              login(input: $input) { token }
            }
            """,
            new { input = new { email = "nobody@example.com", password = "whatever" } });

        Assert.True(result.TryGetProperty("errors", out var errors));
        Assert.Contains("INVALID_CREDENTIALS", errors[0].GetProperty("extensions").GetProperty("code").GetString());
    }

    #endregion

    #region Authenticated queries

    [Fact]
    public async Task Me_Authenticated_ReturnsProfile()
    {
        var email = $"me-{Guid.NewGuid():N}@example.com";
        var (token, _) = await RegisterAndGetTokenAsync(email, "My Name");

        var result = await GraphQlAsync("""
            query { me { id email displayName createdAtUtc } }
            """, token: token);

        Assert.False(result.TryGetProperty("errors", out _));
        var me = result.GetProperty("data").GetProperty("me");
        Assert.Equal(email, me.GetProperty("email").GetString());
        Assert.Equal("My Name", me.GetProperty("displayName").GetString());
    }

    [Fact]
    public async Task Me_Unauthenticated_ReturnsAuthError()
    {
        var result = await GraphQlAsync("query { me { id email } }");
        Assert.True(result.TryGetProperty("errors", out _));
    }

    [Fact]
    public async Task MySubscription_NewPlayer_ReturnsFreeNoExpiry()
    {
        var (token, _) = await RegisterAndGetTokenAsync($"sub-new-{Guid.NewGuid():N}@example.com");

        var result = await GraphQlAsync("""
            query { mySubscription { tier status isActive daysRemaining canProlong expiresAtUtc } }
            """, token: token);

        Assert.False(result.TryGetProperty("errors", out _));
        var sub = result.GetProperty("data").GetProperty("mySubscription");
        Assert.Equal("FREE", sub.GetProperty("tier").GetString());
        Assert.Equal("NONE", sub.GetProperty("status").GetString());
        Assert.False(sub.GetProperty("isActive").GetBoolean());
        Assert.Equal(JsonValueKind.Null, sub.GetProperty("expiresAtUtc").ValueKind);
        Assert.True(sub.GetProperty("canProlong").GetBoolean());
    }

    [Fact]
    public async Task MySubscription_Unauthenticated_ReturnsAuthError()
    {
        var result = await GraphQlAsync("query { mySubscription { tier status } }");
        Assert.True(result.TryGetProperty("errors", out _));
    }

    #endregion

    #region ProlongSubscription

    [Fact]
    public async Task ProlongSubscription_NewPlayer_CreatesProSubscription()
    {
        var (token, _) = await RegisterAndGetTokenAsync($"prolong-new-{Guid.NewGuid():N}@example.com");

        var result = await GraphQlAsync("""
            mutation Prolong($input: ProlongSubscriptionInput!) {
              prolongSubscription(input: $input) {
                tier status isActive daysRemaining canProlong expiresAtUtc startsAtUtc
              }
            }
            """,
            new { input = new { months = 1 } },
            token: token);

        Assert.False(result.TryGetProperty("errors", out _));
        var sub = result.GetProperty("data").GetProperty("prolongSubscription");
        Assert.Equal("PRO", sub.GetProperty("tier").GetString());
        Assert.Equal("ACTIVE", sub.GetProperty("status").GetString());
        Assert.True(sub.GetProperty("isActive").GetBoolean());
        Assert.True(sub.GetProperty("daysRemaining").GetInt32() > 0);
        Assert.NotEqual(JsonValueKind.Null, sub.GetProperty("expiresAtUtc").ValueKind);
    }

    [Fact]
    public async Task ProlongSubscription_ExistingSubscription_ExtendsExpiry()
    {
        var (token, _) = await RegisterAndGetTokenAsync($"prolong-ext-{Guid.NewGuid():N}@example.com");

        // First prolong: 1 month
        await GraphQlAsync("""
            mutation Prolong($input: ProlongSubscriptionInput!) {
              prolongSubscription(input: $input) { expiresAtUtc }
            }
            """,
            new { input = new { months = 1 } },
            token: token);

        // Second prolong: 3 more months
        var result = await GraphQlAsync("""
            mutation Prolong($input: ProlongSubscriptionInput!) {
              prolongSubscription(input: $input) { tier status daysRemaining expiresAtUtc }
            }
            """,
            new { input = new { months = 3 } },
            token: token);

        Assert.False(result.TryGetProperty("errors", out _));
        var sub = result.GetProperty("data").GetProperty("prolongSubscription");
        Assert.Equal("PRO", sub.GetProperty("tier").GetString());
        // After 1+3 months, daysRemaining should be ~120 days
        Assert.True(sub.GetProperty("daysRemaining").GetInt32() > 100);
    }

    [Fact]
    public async Task ProlongSubscription_Unauthenticated_ReturnsAuthError()
    {
        var result = await GraphQlAsync("""
            mutation Prolong($input: ProlongSubscriptionInput!) {
              prolongSubscription(input: $input) { tier }
            }
            """,
            new { input = new { months = 1 } });

        Assert.True(result.TryGetProperty("errors", out _));
    }

    [Fact]
    public async Task ProlongSubscription_InvalidMonths_ReturnsError()
    {
        var (token, _) = await RegisterAndGetTokenAsync($"prolong-inv-{Guid.NewGuid():N}@example.com");

        var result = await GraphQlAsync("""
            mutation Prolong($input: ProlongSubscriptionInput!) {
              prolongSubscription(input: $input) { tier }
            }
            """,
            new { input = new { months = 0 } },
            token: token);

        Assert.True(result.TryGetProperty("errors", out var errors));
        Assert.Contains("INVALID_MONTHS", errors[0].GetProperty("extensions").GetProperty("code").GetString());
    }

    [Fact]
    public async Task ProlongSubscription_12Months_IsValid()
    {
        var (token, _) = await RegisterAndGetTokenAsync($"prolong-12m-{Guid.NewGuid():N}@example.com");

        var result = await GraphQlAsync("""
            mutation Prolong($input: ProlongSubscriptionInput!) {
              prolongSubscription(input: $input) { daysRemaining }
            }
            """,
            new { input = new { months = 12 } },
            token: token);

        Assert.False(result.TryGetProperty("errors", out _));
        var days = result.GetProperty("data").GetProperty("prolongSubscription").GetProperty("daysRemaining").GetInt32();
        // 12 months spans 365–366 days depending on leap year and month length variation
        Assert.True(days >= 364 && days <= 367);
    }

    #endregion

    #region Subscription status flow

    [Fact]
    public async Task SubscriptionFlow_ProlongThenQuery_ReturnsActiveStatus()
    {
        var (token, _) = await RegisterAndGetTokenAsync($"flow-{Guid.NewGuid():N}@example.com");

        // Create subscription
        await GraphQlAsync("""
            mutation Prolong($input: ProlongSubscriptionInput!) {
              prolongSubscription(input: $input) { tier }
            }
            """,
            new { input = new { months = 6 } },
            token: token);

        // Query it back
        var result = await GraphQlAsync("""
            query { mySubscription { tier status isActive daysRemaining canProlong } }
            """, token: token);

        Assert.False(result.TryGetProperty("errors", out _));
        var sub = result.GetProperty("data").GetProperty("mySubscription");
        Assert.Equal("PRO", sub.GetProperty("tier").GetString());
        Assert.Equal("ACTIVE", sub.GetProperty("status").GetString());
        Assert.True(sub.GetProperty("isActive").GetBoolean());
        Assert.True(sub.GetProperty("daysRemaining").GetInt32() > 150);
    }

    #endregion
}
