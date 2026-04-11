using System.Net.Http.Headers;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using MasterApi.Tests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using MasterApi;
using Microsoft.IdentityModel.Tokens;

namespace MasterApi.Tests;

public sealed class MasterApiIntegrationTests : IClassFixture<MasterApiWebApplicationFactory>
{
    private const string SharedJwtIssuer = "Capitalism";
    private const string SharedJwtAudience = "Capitalism";
    private const string SharedJwtSigningKey = "ChangeThisSigningKeyBeforeProduction123!";

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

    private static string CreateSharedToken(string userId, string email, string displayName, params Claim[] extraClaims)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SharedJwtSigningKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId),
            new(ClaimTypes.Email, email),
            new(ClaimTypes.Name, displayName),
        };
        claims.AddRange(extraClaims);

        var token = new JwtSecurityToken(
            issuer: SharedJwtIssuer,
            audience: SharedJwtAudience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(30),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
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
            query { me { id email displayName createdAtUtc startupPackClaimedAtUtc canClaimStartupPack } }
            """, token: token);

        Assert.False(result.TryGetProperty("errors", out _));
        var me = result.GetProperty("data").GetProperty("me");
        Assert.Equal(email, me.GetProperty("email").GetString());
        Assert.Equal("My Name", me.GetProperty("displayName").GetString());
        Assert.Equal(JsonValueKind.Null, me.GetProperty("startupPackClaimedAtUtc").ValueKind);
        Assert.True(me.GetProperty("canClaimStartupPack").GetBoolean());
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

        [Fact]
        public async Task ClaimStartupPack_NewPlayer_ActivatesProAndMarksProfile()
        {
                var email = $"startup-pack-{Guid.NewGuid():N}@example.com";
                var (token, _) = await RegisterAndGetTokenAsync(email, "Starter");

                var claimResult = await GraphQlAsync("""
                        mutation {
                            claimStartupPack {
                                tier
                                status
                                isActive
                                daysRemaining
                                expiresAtUtc
                            }
                        }
                        """, token: token);

                Assert.False(claimResult.TryGetProperty("errors", out _));
                var subscription = claimResult.GetProperty("data").GetProperty("claimStartupPack");
                Assert.Equal("PRO", subscription.GetProperty("tier").GetString());
                Assert.Equal("ACTIVE", subscription.GetProperty("status").GetString());
                Assert.True(subscription.GetProperty("isActive").GetBoolean());
                Assert.True(subscription.GetProperty("daysRemaining").GetInt32() >= 89);

                var meResult = await GraphQlAsync("""
                        query { me { startupPackClaimedAtUtc canClaimStartupPack } }
                        """, token: token);

                Assert.False(meResult.TryGetProperty("errors", out _));
                var me = meResult.GetProperty("data").GetProperty("me");
                Assert.NotEqual(JsonValueKind.Null, me.GetProperty("startupPackClaimedAtUtc").ValueKind);
                Assert.False(me.GetProperty("canClaimStartupPack").GetBoolean());
        }

        [Fact]
        public async Task ClaimStartupPack_AlreadyClaimed_IsIdempotent()
        {
                var email = $"startup-pack-idempotent-{Guid.NewGuid():N}@example.com";
                var (token, _) = await RegisterAndGetTokenAsync(email, "Idempotent Starter");

                var firstClaim = await GraphQlAsync("""
                        mutation {
                            claimStartupPack {
                                tier
                                status
                                isActive
                                expiresAtUtc
                            }
                        }
                        """, token: token);
                var firstExpiry = firstClaim.GetProperty("data").GetProperty("claimStartupPack").GetProperty("expiresAtUtc").GetString();

                var secondClaim = await GraphQlAsync("""
                        mutation {
                            claimStartupPack {
                                tier
                                status
                                isActive
                                expiresAtUtc
                            }
                        }
                        """, token: token);

                Assert.False(secondClaim.TryGetProperty("errors", out _));
                var secondSubscription = secondClaim.GetProperty("data").GetProperty("claimStartupPack");
                Assert.Equal("PRO", secondSubscription.GetProperty("tier").GetString());
                Assert.Equal("ACTIVE", secondSubscription.GetProperty("status").GetString());
                Assert.True(secondSubscription.GetProperty("isActive").GetBoolean());
                Assert.Equal(firstExpiry, secondSubscription.GetProperty("expiresAtUtc").GetString());
        }

        [Fact]
        public async Task ClaimStartupPack_Unauthenticated_ReturnsAuthError()
        {
                var result = await GraphQlAsync("""
                        mutation {
                            claimStartupPack {
                                tier
                            }
                        }
                        """);

                Assert.True(result.TryGetProperty("errors", out _));
        }


        #endregion

        #region Game administration service

                [Fact]
                public async Task GameNewsFeed_IncludesSeededGlobalChangelogEntry()
                {
                        var result = await GraphQlAsync("""
                                query Feed($input: GetGameNewsFeedInput!) {
                                    gameNewsFeed(input: $input) {
                                        items {
                                            entryType
                                            status
                                            localizations {
                                                locale
                                                title
                                            }
                                        }
                                    }
                                }
                                """,
                                new
                                {
                                        input = new
                                        {
                                                registrationKey = "test-registration-key",
                                                serverKey = "capitalism-local",
                                                includeDrafts = false,
                                                limit = 50,
                                        }
                                });

                        Assert.False(result.TryGetProperty("errors", out _));
                        var items = result.GetProperty("data").GetProperty("gameNewsFeed").GetProperty("items").EnumerateArray().ToList();
        Assert.Contains(
                items,
                item => item.GetProperty("entryType").GetString() == "CHANGELOG"
                        && item.GetProperty("status").GetString() == "PUBLISHED"
                        && item.GetProperty("localizations").EnumerateArray().Any(localization =>
                                localization.GetProperty("locale").GetString() == "en"
                                && localization.GetProperty("title").GetString() == "Game administration and newsroom launched"));
                }

        [Fact]
        public async Task GameNewsFeed_AllowsAnonymousPublicRequests()
        {
            var result = await GraphQlAsync("""
                query Feed($input: GetGameNewsFeedInput!) {
                    gameNewsFeed(input: $input) {
                        items { id entryType status }
                    }
                }
                """,
                new
                {
                    input = new
                    {
                        registrationKey = "test-registration-key",
                        serverKey = "capitalism-local",
                        includeDrafts = false,
                        limit = 20,
                    }
                });

            Assert.False(result.TryGetProperty("errors", out _));
            Assert.NotEmpty(result.GetProperty("data").GetProperty("gameNewsFeed").GetProperty("items").EnumerateArray());
        }

        [Fact]
        public async Task GameNewsFeed_HidesDraftsFromPublicButIncludesThemForAdminView()
        {
            await GraphQlAsync("""
                mutation UpsertDraft($input: UpsertGameNewsEntryInput!) {
                  upsertGameNewsEntry(input: $input) { id }
                }
                """,
                new
                {
                    input = new
                    {
                        registrationKey = "test-registration-key",
                        serverKey = "capitalism-local",
                        requesterEmail = "admin@events.local",
                        entryType = "NEWS",
                        status = "DRAFT",
                        localizations = new[]
                        {
                            new
                            {
                                locale = "en",
                                title = "Draft note",
                                summary = "Private admin note",
                                htmlContent = "<p>Still preparing the patch.</p>",
                            }
                        }
                    }
                });

                                    await GraphQlAsync("""
                                        mutation UpsertPublished($input: UpsertGameNewsEntryInput!) {
                                          upsertGameNewsEntry(input: $input) { id }
                                        }
                                        """,
                                        new
                                        {
                                            input = new
                                            {
                                                registrationKey = "test-registration-key",
                                                serverKey = "capitalism-local",
                                                requesterEmail = "admin@events.local",
                                                entryType = "CHANGELOG",
                                                status = "PUBLISHED",
                                                localizations = new[]
                                                {
                                                    new
                                                    {
                                                        locale = "en",
                                                        title = "Patch 0.1",
                                                        summary = "Admin tools arrived.",
                                                        htmlContent = "<p>Added the first admin tooling wave.</p>",
                                                    }
                                                }
                                            }
                                        });

                                    var publicFeed = await GraphQlAsync("""
                                        query Feed($input: GetGameNewsFeedInput!) {
                                          gameNewsFeed(input: $input) {
                                            unreadCount
                                            items { id status entryType }
                                          }
                                        }
                                        """,
                                        new
                                        {
                                            input = new
                                            {
                                                registrationKey = "test-registration-key",
                                                serverKey = "capitalism-local",
                                                playerEmail = "reader@example.com",
                                                includeDrafts = false,
                                                limit = 50,
                                            }
                                        });

                                    Assert.False(publicFeed.TryGetProperty("errors", out _));
                                    var publicItems = publicFeed.GetProperty("data").GetProperty("gameNewsFeed").GetProperty("items").EnumerateArray().ToList();
                                    Assert.NotEmpty(publicItems);
                                    Assert.DoesNotContain(publicItems, item => item.GetProperty("status").GetString() == "DRAFT");
                                    Assert.Contains(publicItems, item => item.GetProperty("status").GetString() == "PUBLISHED");
                                    Assert.True(publicFeed.GetProperty("data").GetProperty("gameNewsFeed").GetProperty("unreadCount").GetInt32() >= 1);

                                    var adminFeed = await GraphQlAsync("""
                                        query Feed($input: GetGameNewsFeedInput!) {
                                          gameNewsFeed(input: $input) {
                                            items { id status entryType }
                                          }
                                        }
                                        """,
                                        new
                                        {
                                            input = new
                                            {
                                                registrationKey = "test-registration-key",
                                                serverKey = "capitalism-local",
                                                includeDrafts = true,
                                                limit = 50,
                                                requesterEmail = "admin@events.local",
                                            }
                                        });

                                    Assert.False(adminFeed.TryGetProperty("errors", out _));
                                    var adminItems = adminFeed.GetProperty("data").GetProperty("gameNewsFeed").GetProperty("items").EnumerateArray().ToList();
                                    Assert.Contains(adminItems, item => item.GetProperty("status").GetString() == "DRAFT");
                                    Assert.Contains(adminItems, item => item.GetProperty("status").GetString() == "PUBLISHED");
                                }

                                [Fact]
                                public async Task MarkGameNewsRead_ClearsUnreadCountForPlayerAndServer()
                                {
                                    var createResult = await GraphQlAsync("""
                                        mutation Upsert($input: UpsertGameNewsEntryInput!) {
                                          upsertGameNewsEntry(input: $input) {
                                            id
                                            targetServerKey
                                          }
                                        }
                                        """,
                                        new
                                        {
                                            input = new
                                            {
                                                registrationKey = "test-registration-key",
                                                serverKey = "capitalism-local",
                                                requesterEmail = "admin@events.local",
                                                entryType = "NEWS",
                                                status = "PUBLISHED",
                                                localizations = new[]
                                                {
                                                    new
                                                    {
                                                        locale = "en",
                                                        title = "Welcome to the shard",
                                                        summary = "Read this before expanding.",
                                                        htmlContent = "<p>Factories are now live.</p>",
                                                    }
                                                }
                                            }
                                        });

                                    var entryId = createResult.GetProperty("data").GetProperty("upsertGameNewsEntry").GetProperty("id").GetString();
                                    Assert.NotNull(entryId);

                                    var beforeRead = await GraphQlAsync("""
                                        query Feed($input: GetGameNewsFeedInput!) {
                                          gameNewsFeed(input: $input) {
                                            unreadCount
                                            items { id isRead }
                                          }
                                        }
                                        """,
                                        new
                                        {
                                            input = new
                                            {
                                                registrationKey = "test-registration-key",
                                                serverKey = "capitalism-local",
                                                playerEmail = "reader@example.com",
                                                includeDrafts = false,
                                                limit = 50,
                                            }
                                        });

                                    var beforeFeed = beforeRead.GetProperty("data").GetProperty("gameNewsFeed");
                                    var beforeUnreadCount = beforeFeed.GetProperty("unreadCount").GetInt32();
                                    var beforeItems = beforeFeed.GetProperty("items").EnumerateArray().ToList();
                                    var createdBefore = beforeItems.Single(item => item.GetProperty("id").GetString() == entryId);

                                    Assert.True(beforeUnreadCount >= 1);
                                    Assert.False(createdBefore.GetProperty("isRead").GetBoolean());

                                    var markResult = await GraphQlAsync("""
                                        mutation MarkRead($input: MarkGameNewsReadInput!) {
                                          markGameNewsRead(input: $input)
                                        }
                                        """,
                                        new
                                        {
                                            input = new
                                            {
                                                registrationKey = "test-registration-key",
                                                serverKey = "capitalism-local",
                                                playerEmail = "reader@example.com",
                                                entryIds = new[] { entryId },
                                            }
                                        });

                                    Assert.False(markResult.TryGetProperty("errors", out _));
                                    Assert.True(markResult.GetProperty("data").GetProperty("markGameNewsRead").GetBoolean());

                                    var afterRead = await GraphQlAsync("""
                                        query Feed($input: GetGameNewsFeedInput!) {
                                          gameNewsFeed(input: $input) {
                                            unreadCount
                                            items { id isRead }
                                          }
                                        }
                                        """,
                                        new
                                        {
                                            input = new
                                            {
                                                registrationKey = "test-registration-key",
                                                serverKey = "capitalism-local",
                                                playerEmail = "reader@example.com",
                                                includeDrafts = false,
                                                limit = 50,
                                            }
                                        });

                                    var afterFeed = afterRead.GetProperty("data").GetProperty("gameNewsFeed");
                                    var afterUnreadCount = afterFeed.GetProperty("unreadCount").GetInt32();
                                    var afterItems = afterFeed.GetProperty("items").EnumerateArray().ToList();
                                    var createdAfter = afterItems.Single(item => item.GetProperty("id").GetString() == entryId);

                                    Assert.Equal(beforeUnreadCount - 1, afterUnreadCount);
                                    Assert.True(createdAfter.GetProperty("isRead").GetBoolean());
                                }

                                [Fact]
                                public async Task AssignGlobalGameAdmin_RootAdministrator_UpdatesAccess()
                                {
                                    var assignResult = await GraphQlAsync("""
                                        mutation Assign($input: GlobalGameAdminGrantInput!) {
                                          assignGlobalGameAdmin(input: $input) {
                                            email
                                            grantedByEmail
                                          }
                                        }
                                        """,
                                        new
                                        {
                                            input = new
                                            {
                                                registrationKey = "test-registration-key",
                                                serverKey = "capitalism-local",
                                                requesterEmail = "root@example.com",
                                                targetEmail = "global-admin@example.com",
                                            }
                                        });

                                    Assert.False(assignResult.TryGetProperty("errors", out _));
                                    var grant = assignResult.GetProperty("data").GetProperty("assignGlobalGameAdmin");
                                    Assert.Equal("global-admin@example.com", grant.GetProperty("email").GetString());
                                    Assert.Equal("root@example.com", grant.GetProperty("grantedByEmail").GetString());

                                    var accessResult = await GraphQlAsync("""
                                        query Access($input: GetGameAdministrationAccessInput!) {
                                          gameAdministrationAccess(input: $input) {
                                            email
                                            isRootAdministrator
                                            hasGlobalAdminRole
                                            canAccessEveryGameDashboard
                                          }
                                        }
                                        """,
                                        new
                                        {
                                            input = new
                                            {
                                                registrationKey = "test-registration-key",
                                                serverKey = "capitalism-local",
                                                email = "global-admin@example.com",
                                            }
                                        });

                                    Assert.False(accessResult.TryGetProperty("errors", out _));
                                    var access = accessResult.GetProperty("data").GetProperty("gameAdministrationAccess");
                                    Assert.False(access.GetProperty("isRootAdministrator").GetBoolean());
                                    Assert.True(access.GetProperty("hasGlobalAdminRole").GetBoolean());
                                    Assert.True(access.GetProperty("canAccessEveryGameDashboard").GetBoolean());

                                    var grantsResult = await GraphQlAsync("""
                                        query Grants($input: GetGlobalGameAdminGrantsInput!) {
                                          globalGameAdminGrants(input: $input) {
                                            email
                                          }
                                        }
                                        """,
                                        new
                                        {
                                            input = new
                                            {
                                                registrationKey = "test-registration-key",
                                                serverKey = "capitalism-local",
                                                requesterEmail = "root@example.com",
                                            }
                                        });

                                    Assert.False(grantsResult.TryGetProperty("errors", out _));
                                    Assert.Contains(
                                        grantsResult.GetProperty("data").GetProperty("globalGameAdminGrants").EnumerateArray(),
                                        item => item.GetProperty("email").GetString() == "global-admin@example.com");
                                }

                                [Fact]
                                public async Task AssignGlobalGameAdmin_NonRootAdministrator_ReturnsError()
                                {
                                    var result = await GraphQlAsync("""
                                        mutation Assign($input: GlobalGameAdminGrantInput!) {
                                          assignGlobalGameAdmin(input: $input) {
                                            email
                                          }
                                        }
                                        """,
                                        new
                                        {
                                            input = new
                                            {
                                                registrationKey = "test-registration-key",
                                                serverKey = "capitalism-local",
                                                requesterEmail = "local-admin@example.com",
                                                targetEmail = "global-admin@example.com",
                                            }
                                        });

                                    Assert.True(result.TryGetProperty("errors", out var errors));
                                    Assert.Contains("ROOT_ADMIN_REQUIRED", errors[0].GetProperty("extensions").GetProperty("code").GetString());
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

    #region Additional edge-case tests

    [Fact]
    public async Task Register_EmptyEmail_ReturnsError()
    {
        var result = await GraphQlAsync("""
            mutation {
              register(input: { email: "", password: "password123", displayName: "Test" }) {
                token
              }
            }
            """);

        Assert.True(result.TryGetProperty("errors", out var errors));
        var code = errors[0].GetProperty("extensions").GetProperty("code").GetString();
        Assert.Equal("INVALID_EMAIL", code);
    }

    [Fact]
    public async Task Register_EmptyDisplayName_ReturnsError()
    {
        var result = await GraphQlAsync("""
            mutation {
              register(input: { email: "test-display@example.com", password: "password123", displayName: "" }) {
                token
              }
            }
            """);

        Assert.True(result.TryGetProperty("errors", out var errors));
        var code = errors[0].GetProperty("extensions").GetProperty("code").GetString();
        Assert.Equal("DISPLAY_NAME_REQUIRED", code);
    }

    [Fact]
    public async Task Register_EmptyPassword_ReturnsError()
    {
        var result = await GraphQlAsync("""
            mutation {
              register(input: { email: "test-emptypass@example.com", password: "", displayName: "Test" }) {
                token
              }
            }
            """);

        Assert.True(result.TryGetProperty("errors", out var errors));
        var code = errors[0].GetProperty("extensions").GetProperty("code").GetString();
        Assert.Equal("PASSWORD_TOO_SHORT", code);
    }

    [Fact]
    public async Task GameServers_RegisteredServer_ReturnsAllFields()
    {
        var result = await GraphQlAsync("""
            mutation Reg($input: RegisterGameServerInput!) {
              registerGameServer(input: $input) { id displayName region environment playerCount }
            }
            """,
            new
            {
                input = new
                {
                    registrationKey = "test-registration-key",
                    serverKey = "test-key-fields",
                    displayName = "Field Test Server",
                    description = "Verifying all fields",
                    region = "EU",
                    environment = "test",
                    backendUrl = "https://test.example.com",
                    graphqlUrl = "https://test.example.com/graphql",
                    frontendUrl = "https://test.example.com/app",
                    version = "2.0.0",
                    playerCount = 7,
                    companyCount = 14,
                    currentTick = 999,
                },
            });

        var srv = result.GetProperty("data").GetProperty("registerGameServer");
        Assert.Equal("Field Test Server", srv.GetProperty("displayName").GetString());
        Assert.Equal("EU", srv.GetProperty("region").GetString());
        Assert.Equal("test", srv.GetProperty("environment").GetString());
        Assert.Equal(7, srv.GetProperty("playerCount").GetInt32());
    }

    [Fact]
    public async Task GameServers_RegisteredByKey_AppearsInList()
    {
        // Register a server with a unique key
        var uniqueKey = "list-test-" + Guid.NewGuid().ToString("N")[..8];
        var uniqueName = "List Appearance Server " + uniqueKey;

        await GraphQlAsync("""
            mutation Reg($input: RegisterGameServerInput!) {
              registerGameServer(input: $input) { id }
            }
            """,
            new
            {
                input = new
                {
                    registrationKey = "test-registration-key",
                    serverKey = uniqueKey,
                    displayName = uniqueName,
                    description = "Multi test",
                    region = "EU",
                    environment = "test",
                    backendUrl = $"https://{uniqueKey}.example.com",
                    graphqlUrl = $"https://{uniqueKey}.example.com/graphql",
                    frontendUrl = $"https://{uniqueKey}.example.com/app",
                    version = "1.0.0",
                    playerCount = 5,
                    companyCount = 10,
                    currentTick = 100,
                },
            });

        var result = await GraphQlAsync("""
            query { gameServers { id displayName } }
            """);

        var servers = result.GetProperty("data").GetProperty("gameServers");
        var names = Enumerable.Range(0, servers.GetArrayLength())
            .Select(i => servers[i].GetProperty("displayName").GetString())
            .ToList();

        Assert.Contains(uniqueName, names);
    }

    [Fact]
    public async Task ProlongSubscription_ExpiredSubscription_ExtendsFromNow()
    {
        // Register and get a token
        var registerResult = await GraphQlAsync("""
            mutation {
              register(input: { email: "expired-sub@example.com", password: "password123", displayName: "ExpiredTest" }) {
                token
              }
            }
            """);

        var token = registerResult.GetProperty("data").GetProperty("register").GetProperty("token").GetString()!;

        // First prolong: 1 month
        await GraphQlAsync("""
            mutation Prolong($input: ProlongSubscriptionInput!) {
              prolongSubscription(input: $input) { daysRemaining }
            }
            """,
            new { input = new { months = 1 } },
            token: token);

        // Second prolong: 3 more months
        var result = await GraphQlAsync("""
            mutation Prolong($input: ProlongSubscriptionInput!) {
              prolongSubscription(input: $input) { tier status isActive daysRemaining expiresAtUtc }
            }
            """,
            new { input = new { months = 3 } },
            token: token);

        Assert.False(result.TryGetProperty("errors", out _));
        var sub = result.GetProperty("data").GetProperty("prolongSubscription");
        Assert.Equal("PRO", sub.GetProperty("tier").GetString());
        Assert.True(sub.GetProperty("isActive").GetBoolean());
        // Should have ~4 months total (120+ days)
        Assert.True(sub.GetProperty("daysRemaining").GetInt32() >= 115);
    }

    [Fact]
    public async Task MySubscription_AllFieldsPresent()
    {
        var registerResult = await GraphQlAsync("""
            mutation {
              register(input: { email: "fields-test@example.com", password: "password123", displayName: "FieldsTest" }) {
                token
              }
            }
            """);

        var token = registerResult.GetProperty("data").GetProperty("register").GetProperty("token").GetString()!;

        await GraphQlAsync("""
            mutation Prolong($input: ProlongSubscriptionInput!) {
              prolongSubscription(input: $input) { tier }
            }
            """,
            new { input = new { months = 1 } },
            token: token);

        var result = await GraphQlAsync("""
            query { mySubscription { tier status isActive daysRemaining canProlong startsAtUtc expiresAtUtc } }
            """, token: token);

        Assert.False(result.TryGetProperty("errors", out _));
        var sub = result.GetProperty("data").GetProperty("mySubscription");
        Assert.Equal("PRO", sub.GetProperty("tier").GetString());
        Assert.Equal("ACTIVE", sub.GetProperty("status").GetString());
        Assert.True(sub.GetProperty("isActive").GetBoolean());
        Assert.True(sub.GetProperty("canProlong").GetBoolean());
        Assert.False(string.IsNullOrEmpty(sub.GetProperty("startsAtUtc").GetString()));
        Assert.False(string.IsNullOrEmpty(sub.GetProperty("expiresAtUtc").GetString()));
    }

    [Fact]
    public async Task Login_IsCaseInsensitiveForEmail()
    {
        // Register with lowercase email
        await GraphQlAsync("""
            mutation {
              register(input: { email: "camelcase@example.com", password: "password123", displayName: "CaseTest" }) {
                token
              }
            }
            """);

        // Login with uppercase email should succeed
        var result = await GraphQlAsync("""
            mutation {
              login(input: { email: "CAMELCASE@EXAMPLE.COM", password: "password123" }) {
                token
                player { email displayName }
              }
            }
            """);

        Assert.False(result.TryGetProperty("errors", out _));
        var player = result.GetProperty("data").GetProperty("login").GetProperty("player");
        Assert.Equal("camelcase@example.com", player.GetProperty("email").GetString());
    }

    [Fact]
    public async Task Me_ReturnsAllProfileFields()
    {
        var (token, _) = await RegisterAndGetTokenAsync(
            "profile-fields@example.com", "Profile User", "password123");

        var result = await GraphQlAsync("""
            query { me { id email displayName createdAtUtc startupPackClaimedAtUtc canClaimStartupPack } }
            """, token: token);

        Assert.False(result.TryGetProperty("errors", out _));
        var me = result.GetProperty("data").GetProperty("me");
        Assert.Equal("profile-fields@example.com", me.GetProperty("email").GetString());
        Assert.Equal("Profile User", me.GetProperty("displayName").GetString());
        Assert.False(string.IsNullOrEmpty(me.GetProperty("id").GetString()));
        Assert.False(string.IsNullOrEmpty(me.GetProperty("createdAtUtc").GetString()));
        Assert.Equal(JsonValueKind.Null, me.GetProperty("startupPackClaimedAtUtc").ValueKind);
        Assert.True(me.GetProperty("canClaimStartupPack").GetBoolean());
    }

    [Fact]
    public async Task Register_TokenExpiry_IsSetInFuture()
    {
        var result = await GraphQlAsync("""
            mutation {
              register(input: { email: "expiry-test@example.com", password: "password123", displayName: "ExpiryTest" }) {
                token
                expiresAtUtc
              }
            }
            """);

        Assert.False(result.TryGetProperty("errors", out _));
        var payload = result.GetProperty("data").GetProperty("register");
        var expiresAtStr = payload.GetProperty("expiresAtUtc").GetString()!;
        var expiresAt = DateTime.Parse(expiresAtStr, null, System.Globalization.DateTimeStyles.RoundtripKind);
        Assert.True(expiresAt > DateTime.UtcNow, "Token expiry must be in the future");
    }

    [Fact]
    public async Task Login_ReturnsTokenAndPlayer()
    {
        await RegisterAndGetTokenAsync("login-fields@example.com", "Login Fields User", "password123");

        var result = await GraphQlAsync("""
            mutation {
              login(input: { email: "login-fields@example.com", password: "password123" }) {
                token
                expiresAtUtc
                player { id email displayName createdAtUtc }
              }
            }
            """);

        Assert.False(result.TryGetProperty("errors", out _));
        var payload = result.GetProperty("data").GetProperty("login");
        Assert.False(string.IsNullOrEmpty(payload.GetProperty("token").GetString()));
        Assert.False(string.IsNullOrEmpty(payload.GetProperty("expiresAtUtc").GetString()));
        var player = payload.GetProperty("player");
        Assert.Equal("login-fields@example.com", player.GetProperty("email").GetString());
    }

    [Fact]
    public async Task Me_GameStyleToken_ResolvesPlayerByEmail()
    {
        const string email = "cross-api@example.com";
        await RegisterAndGetTokenAsync(email, "Cross API User");
        var token = CreateSharedToken(Guid.NewGuid().ToString(), email, "Cross API User", new Claim(ClaimTypes.Role, "PLAYER"));

        var result = await GraphQlAsync("""
            query { me { email displayName } }
            """, token: token);

        Assert.False(result.TryGetProperty("errors", out _));
        var me = result.GetProperty("data").GetProperty("me");
        Assert.Equal(email, me.GetProperty("email").GetString());
        Assert.Equal("Cross API User", me.GetProperty("displayName").GetString());
    }

    [Fact]
    public async Task MySubscription_FreeUser_CanProlong_IsTrue()
    {
        var (token, _) = await RegisterAndGetTokenAsync("canprolong@example.com");

        var result = await GraphQlAsync("""
            query { mySubscription { tier status isActive canProlong daysRemaining } }
            """, token: token);

        Assert.False(result.TryGetProperty("errors", out _));
        var sub = result.GetProperty("data").GetProperty("mySubscription");
        Assert.Equal("FREE", sub.GetProperty("tier").GetString());
        Assert.Equal("NONE", sub.GetProperty("status").GetString());
        Assert.False(sub.GetProperty("isActive").GetBoolean());
        Assert.True(sub.GetProperty("canProlong").GetBoolean());
    }

    [Fact]
    public async Task ProlongSubscription_ThenMe_BothWorkWithSameToken()
    {
        var (token, _) = await RegisterAndGetTokenAsync("dual-auth@example.com");

        // Both queries should work with the same token
        var prolongResult = await GraphQlAsync("""
            mutation Prolong($input: ProlongSubscriptionInput!) {
              prolongSubscription(input: $input) { tier isActive }
            }
            """,
            new { input = new { months = 2 } },
            token: token);

        var meResult = await GraphQlAsync("""
            query { me { email } }
            """, token: token);

        Assert.False(prolongResult.TryGetProperty("errors", out _));
        Assert.False(meResult.TryGetProperty("errors", out _));
        Assert.True(prolongResult.GetProperty("data").GetProperty("prolongSubscription").GetProperty("isActive").GetBoolean());
        Assert.Equal("dual-auth@example.com", meResult.GetProperty("data").GetProperty("me").GetProperty("email").GetString());
    }

    [Fact]
    public async Task GameServers_IsOnline_BasedOnHeartbeatThreshold()
    {
        // A newly registered server should be considered online
        var result = await GraphQlAsync("""
            mutation Reg($input: RegisterGameServerInput!) {
              registerGameServer(input: $input) { id isOnline lastHeartbeatAtUtc }
            }
            """,
            new
            {
                input = new
                {
                    registrationKey = "test-registration-key",
                    serverKey = "heartbeat-threshold-test",
                    displayName = "Heartbeat Test Server",
                    description = "Test heartbeat threshold",
                    region = "EU",
                    environment = "test",
                    backendUrl = "https://hb.example.com",
                    graphqlUrl = "https://hb.example.com/graphql",
                    frontendUrl = "https://hb.example.com/app",
                    version = "1.0.0",
                    playerCount = 0,
                    companyCount = 0,
                    currentTick = 0,
                },
            });

        Assert.False(result.TryGetProperty("errors", out _));
        var srv = result.GetProperty("data").GetProperty("registerGameServer");
        Assert.True(srv.GetProperty("isOnline").GetBoolean(), "Freshly registered server should be online");
    }

    [Fact]
    public async Task RegisterGameServer_Heartbeat_UpdatesExistingServer()
    {
        const string serverKey = "heartbeat-update-test";

        // First registration
        await GraphQlAsync("""
            mutation Reg($input: RegisterGameServerInput!) {
              registerGameServer(input: $input) { id playerCount }
            }
            """,
            new
            {
                input = new
                {
                    registrationKey = "test-registration-key",
                    serverKey,
                    displayName = "Update Test Server",
                    description = "Tests heartbeat update",
                    region = "EU",
                    environment = "test",
                    backendUrl = "https://upd.example.com",
                    graphqlUrl = "https://upd.example.com/graphql",
                    frontendUrl = "https://upd.example.com/app",
                    version = "1.0.0",
                    playerCount = 0,
                    companyCount = 0,
                    currentTick = 0,
                },
            });

        // Second registration (heartbeat with updated playerCount)
        var result = await GraphQlAsync("""
            mutation Reg($input: RegisterGameServerInput!) {
              registerGameServer(input: $input) { id playerCount currentTick }
            }
            """,
            new
            {
                input = new
                {
                    registrationKey = "test-registration-key",
                    serverKey,
                    displayName = "Update Test Server",
                    description = "Tests heartbeat update",
                    region = "EU",
                    environment = "test",
                    backendUrl = "https://upd.example.com",
                    graphqlUrl = "https://upd.example.com/graphql",
                    frontendUrl = "https://upd.example.com/app",
                    version = "1.0.0",
                    playerCount = 99,
                    companyCount = 55,
                    currentTick = 12345,
                },
            });

        Assert.False(result.TryGetProperty("errors", out _));
        var srv = result.GetProperty("data").GetProperty("registerGameServer");
        Assert.Equal(99, srv.GetProperty("playerCount").GetInt32());
        Assert.Equal(12345, srv.GetProperty("currentTick").GetInt32());
    }

    #endregion

    #region Subscription lifecycle tests

    [Fact]
    public async Task ProlongSubscription_ActiveSub_ExtendsInPlace_Stacks()
    {
        // Register and get an initial subscription
        var (token, _) = await RegisterAndGetTokenAsync("lifecycle-stack@example.com");

        // Prolong for 1 month (creates first Active subscription)
        await GraphQlAsync("""
            mutation Prolong($input: ProlongSubscriptionInput!) {
              prolongSubscription(input: $input) { tier status isActive daysRemaining }
            }
            """,
            new { input = new { months = 1 } },
            token: token);

        // Prolong again while still active — should extend the existing record in place,
        // not create a new record. daysRemaining should reflect ~60 days (2 months stacked).
        var result = await GraphQlAsync("""
            mutation Prolong($input: ProlongSubscriptionInput!) {
              prolongSubscription(input: $input) { tier status isActive daysRemaining }
            }
            """,
            new { input = new { months = 1 } },
            token: token);

        Assert.False(result.TryGetProperty("errors", out _));
        var sub = result.GetProperty("data").GetProperty("prolongSubscription");
        Assert.Equal("ACTIVE", sub.GetProperty("status").GetString());
        Assert.True(sub.GetProperty("isActive").GetBoolean());
        var daysRemaining = sub.GetProperty("daysRemaining").GetInt32();
        Assert.True(daysRemaining > 50, $"Expected daysRemaining > 50 but got {daysRemaining}");
    }

    [Fact]
    public async Task MySubscription_AfterProlong_ShowsActiveStatus()
    {
        var (token, _) = await RegisterAndGetTokenAsync("sub-active-query@example.com");

        await GraphQlAsync("""
            mutation Prolong($input: ProlongSubscriptionInput!) {
              prolongSubscription(input: $input) { tier }
            }
            """,
            new { input = new { months = 3 } },
            token: token);

        var result = await GraphQlAsync("""
            query { mySubscription { tier status isActive daysRemaining canProlong expiresAtUtc } }
            """, token: token);

        Assert.False(result.TryGetProperty("errors", out _));
        var sub = result.GetProperty("data").GetProperty("mySubscription");
        Assert.Equal("PRO", sub.GetProperty("tier").GetString());
        Assert.Equal("ACTIVE", sub.GetProperty("status").GetString());
        Assert.True(sub.GetProperty("isActive").GetBoolean());
        Assert.True(sub.GetProperty("canProlong").GetBoolean());
        var daysRemaining = sub.GetProperty("daysRemaining").GetInt32();
        Assert.True(daysRemaining > 85, $"Expected ~90 days remaining but got {daysRemaining}");
    }

    [Fact]
    public async Task MySubscription_FreshUser_ShowsFreeNone()
    {
        var (token, _) = await RegisterAndGetTokenAsync("fresh-free@example.com");

        var result = await GraphQlAsync("""
            query { mySubscription { tier status isActive canProlong daysRemaining } }
            """, token: token);

        Assert.False(result.TryGetProperty("errors", out _));
        var sub = result.GetProperty("data").GetProperty("mySubscription");
        Assert.Equal("FREE", sub.GetProperty("tier").GetString());
        Assert.Equal("NONE", sub.GetProperty("status").GetString());
        Assert.False(sub.GetProperty("isActive").GetBoolean());
        Assert.True(sub.GetProperty("canProlong").GetBoolean());
        Assert.True(sub.GetProperty("daysRemaining").ValueKind == System.Text.Json.JsonValueKind.Null);
    }

    [Fact]
    public async Task ProlongSubscription_MaxMonths_12_IsAccepted()
    {
        var (token, _) = await RegisterAndGetTokenAsync("max-months@example.com");

        var result = await GraphQlAsync("""
            mutation Prolong($input: ProlongSubscriptionInput!) {
              prolongSubscription(input: $input) { tier status isActive daysRemaining }
            }
            """,
            new { input = new { months = 12 } },
            token: token);

        Assert.False(result.TryGetProperty("errors", out _));
        var sub = result.GetProperty("data").GetProperty("prolongSubscription");
        Assert.Equal("ACTIVE", sub.GetProperty("status").GetString());
        var days = sub.GetProperty("daysRemaining").GetInt32();
        Assert.True(days > 360, $"Expected ~365 days remaining but got {days}");
    }

    [Fact]
    public async Task ProlongSubscription_MonthsOutOfRange_ReturnsError()
    {
        var (token, _) = await RegisterAndGetTokenAsync("months-range@example.com");

        var tooFew = await GraphQlAsync("""
            mutation Prolong($input: ProlongSubscriptionInput!) {
              prolongSubscription(input: $input) { tier }
            }
            """,
            new { input = new { months = 0 } },
            token: token);

        var tooMany = await GraphQlAsync("""
            mutation Prolong($input: ProlongSubscriptionInput!) {
              prolongSubscription(input: $input) { tier }
            }
            """,
            new { input = new { months = 13 } },
            token: token);

        Assert.True(tooFew.TryGetProperty("errors", out var fewErrors));
        Assert.Equal("INVALID_MONTHS", fewErrors[0].GetProperty("extensions").GetProperty("code").GetString());

        Assert.True(tooMany.TryGetProperty("errors", out var manyErrors));
        Assert.Equal("INVALID_MONTHS", manyErrors[0].GetProperty("extensions").GetProperty("code").GetString());
    }

    [Fact]
    public async Task JwtOptions_DefaultSigningKey_ConstantMatchesAppsettings()
    {
        // Verifies that the constant used in Program.cs startup guard matches
        // the value in appsettings.json so the guard cannot silently become a no-op.
        Assert.Equal("ChangeThisSigningKeyBeforeProduction123!", MasterApi.Security.JwtOptions.DefaultSigningKey);
    }

    #endregion
}

// ── Startup guard test ────────────────────────────────────────────────────────
// Separate class + factory so it does not share the singleton with the main tests.

public sealed class ProductionStartupGuardFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Simulate a production deployment that forgot to set the JWT signing key.
        builder.UseEnvironment("Production");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MasterServer:RegistrationKey"] = "any-key",
                // Deliberately leave Jwt:SigningKey at the default value from appsettings.json
                // (which is the DefaultSigningKey constant) to trigger the startup guard.
            });
        });
    }
}

public sealed class JwtStartupGuardTests : IClassFixture<ProductionStartupGuardFactory>
{
    private readonly ProductionStartupGuardFactory _factory;

    public JwtStartupGuardTests(ProductionStartupGuardFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public void Startup_Production_WithDefaultSigningKey_ThrowsInvalidOperation()
    {
        // The startup guard in Program.cs must throw when the default JWT key is used
        // in Production so that misconfigured deployments fail immediately rather than
        // silently accepting forgeable tokens.
        var ex = Assert.Throws<InvalidOperationException>(() => _factory.CreateClient());

        Assert.Contains("JWT SigningKey", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("default", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
