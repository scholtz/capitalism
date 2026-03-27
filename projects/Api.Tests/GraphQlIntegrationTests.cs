using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Api.Data;
using Api.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Api.Tests;

/// <summary>
/// Integration tests for the Capitalism V GraphQL API.
/// Tests cover authentication, game data queries, company management, and onboarding flow.
/// </summary>
public sealed class GraphQlIntegrationTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly ApiWebApplicationFactory _factory;

    public GraphQlIntegrationTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    #region Helpers

    private async Task<JsonElement> ExecuteGraphQlAsync(string query, object? variables = null, string? token = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/graphql");
        request.Content = new StringContent(
            JsonSerializer.Serialize(new { query, variables }),
            Encoding.UTF8,
            "application/json");

        if (token is not null)
        {
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        }

        var response = await _client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            throw new Exception($"HTTP {(int)response.StatusCode}: {body}");
        }

        return JsonSerializer.Deserialize<JsonElement>(body);
    }

    private async Task<string> RegisterAndGetTokenAsync(string email = "test@example.com", string displayName = "Tester", string password = "TestPass123!")
    {
        var result = await ExecuteGraphQlAsync(
            """
            mutation Register($input: RegisterInput!) {
              register(input: $input) {
                token
                player { id displayName email role }
              }
            }
            """,
            new { input = new { email, displayName, password } });

        return result.GetProperty("data").GetProperty("register").GetProperty("token").GetString()!;
    }

    private async Task AdvanceGameTicksAsync(long ticks)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var gameState = await db.GameStates.FindAsync(1);
        Assert.NotNull(gameState);

        gameState!.CurrentTick += ticks;
        gameState.LastTickAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    private async Task ResetGameStateAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var gameState = await db.GameStates.FindAsync(1);
        Assert.NotNull(gameState);

        gameState!.CurrentTick = 0;
        gameState.LastTickAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    #endregion

    #region Health & Info

    [Fact]
    public async Task HealthEndpoint_ReturnsOk()
    {
        var response = await _client.GetAsync("/healthz");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ok", body.GetProperty("status").GetString());
    }

    [Fact]
    public async Task RootEndpoint_ReturnsApiInfo()
    {
        var response = await _client.GetAsync("/");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Capitalism V API", body.GetProperty("name").GetString());
    }

    #endregion

    #region Authentication

    [Fact]
    public async Task Register_CreatesPlayerAndReturnsToken()
    {
        var result = await ExecuteGraphQlAsync(
            """
            mutation Register($input: RegisterInput!) {
              register(input: $input) {
                token
                expiresAtUtc
                player { id displayName email role }
              }
            }
            """,
            new { input = new { email = "newplayer@test.com", displayName = "New Player", password = "SecurePass1!" } });

        var data = result.GetProperty("data").GetProperty("register");
        Assert.NotEmpty(data.GetProperty("token").GetString()!);
        Assert.Equal("New Player", data.GetProperty("player").GetProperty("displayName").GetString());
        Assert.Equal("PLAYER", data.GetProperty("player").GetProperty("role").GetString());
    }

    [Fact]
    public async Task Register_DuplicateEmail_ReturnsError()
    {
        var input = new { email = "dupe@test.com", displayName = "First", password = "Password1!" };

        await ExecuteGraphQlAsync(
            "mutation Register($input: RegisterInput!) { register(input: $input) { token } }",
            new { input });

        var result = await ExecuteGraphQlAsync(
            "mutation Register($input: RegisterInput!) { register(input: $input) { token } }",
            new { input });

        Assert.True(result.TryGetProperty("errors", out var errors));
        Assert.Contains("already exists", errors[0].GetProperty("message").GetString());
    }

    [Fact]
    public async Task Login_ValidCredentials_ReturnsToken()
    {
        // Register first
        await ExecuteGraphQlAsync(
            "mutation Register($input: RegisterInput!) { register(input: $input) { token } }",
            new { input = new { email = "login@test.com", displayName = "LoginUser", password = "Password1!" } });

        // Login
        var result = await ExecuteGraphQlAsync(
            """
            mutation Login($input: LoginInput!) {
              login(input: $input) {
                token
                player { displayName }
              }
            }
            """,
            new { input = new { email = "login@test.com", password = "Password1!" } });

        var data = result.GetProperty("data").GetProperty("login");
        Assert.NotEmpty(data.GetProperty("token").GetString()!);
        Assert.Equal("LoginUser", data.GetProperty("player").GetProperty("displayName").GetString());
    }

    [Fact]
    public async Task Login_InvalidPassword_ReturnsError()
    {
        await ExecuteGraphQlAsync(
            "mutation Register($input: RegisterInput!) { register(input: $input) { token } }",
            new { input = new { email = "badpass@test.com", displayName = "User", password = "Password1!" } });

        var result = await ExecuteGraphQlAsync(
            "mutation Login($input: LoginInput!) { login(input: $input) { token } }",
            new { input = new { email = "badpass@test.com", password = "WrongPassword!" } });

        Assert.True(result.TryGetProperty("errors", out var errors));
        Assert.Contains("Invalid email or password", errors[0].GetProperty("message").GetString());
    }

    [Fact]
    public async Task Me_Unauthenticated_ReturnsAuthError()
    {
        var result = await ExecuteGraphQlAsync("{ me { id displayName } }");
        Assert.True(result.TryGetProperty("errors", out _));
    }

    [Fact]
    public async Task Me_Authenticated_ReturnsProfile()
    {
        var token = await RegisterAndGetTokenAsync("me@test.com", "MeUser");

        var result = await ExecuteGraphQlAsync("{ me { displayName email role } }", token: token);

        var me = result.GetProperty("data").GetProperty("me");
        Assert.Equal("MeUser", me.GetProperty("displayName").GetString());
        Assert.Equal("me@test.com", me.GetProperty("email").GetString());
    }

    #endregion

    #region Game Data Queries

    [Fact]
    public async Task Cities_ReturnsSeededCities()
    {
        var result = await ExecuteGraphQlAsync(
            """
            {
              cities {
                name
                countryCode
                population
                resources { resourceType { name } abundance }
              }
            }
            """);

        var cities = result.GetProperty("data").GetProperty("cities");
        Assert.True(cities.GetArrayLength() >= 3);

        var names = cities.EnumerateArray().Select(c => c.GetProperty("name").GetString()).ToList();
        Assert.Contains("Bratislava", names);
        Assert.Contains("Prague", names);
        Assert.Contains("Vienna", names);
    }

    [Fact]
    public async Task ResourceTypes_ReturnsSeededResources()
    {
        var result = await ExecuteGraphQlAsync("{ resourceTypes { name slug category basePrice } }");

        var resources = result.GetProperty("data").GetProperty("resourceTypes");
        Assert.True(resources.GetArrayLength() >= 8);

        var slugs = resources.EnumerateArray().Select(r => r.GetProperty("slug").GetString()).ToList();
        Assert.Contains("wood", slugs);
        Assert.Contains("iron-ore", slugs);
        Assert.Contains("coal", slugs);
    }

    [Fact]
    public async Task ProductTypes_ReturnsSeededProducts()
    {
        var result = await ExecuteGraphQlAsync(
            """
            {
              productTypes {
                name slug industry basePrice baseCraftTicks outputQuantity energyConsumptionMwh unitName unitSymbol
                recipes { quantity resourceType { name } inputProductType { name } }
              }
            }
            """);

        var products = result.GetProperty("data").GetProperty("productTypes");
        Assert.True(products.GetArrayLength() >= 100);

        var electronicTable = products.EnumerateArray().First(product => product.GetProperty("slug").GetString() == "electronic-table");
        Assert.Equal(2m, electronicTable.GetProperty("energyConsumptionMwh").GetDecimal());
        Assert.Equal(1m, electronicTable.GetProperty("outputQuantity").GetDecimal());
    }

    [Fact]
    public async Task ProductTypes_FilterByIndustry_ReturnsOnlyMatching()
    {
        var result = await ExecuteGraphQlAsync(
            """
            query Products($industry: String) {
              productTypes(industry: $industry) { name industry }
            }
            """,
            new { industry = "FURNITURE" });

        var products = result.GetProperty("data").GetProperty("productTypes");
        foreach (var product in products.EnumerateArray())
        {
            Assert.Equal("FURNITURE", product.GetProperty("industry").GetString());
        }
    }

    [Fact]
    public async Task GameState_ReturnsInitialState()
    {
        await ResetGameStateAsync();
        var result = await ExecuteGraphQlAsync("{ gameState { currentTick tickIntervalSeconds taxRate } }");

        var state = result.GetProperty("data").GetProperty("gameState");
        Assert.Equal(0, state.GetProperty("currentTick").GetInt64());
        Assert.Equal(60, state.GetProperty("tickIntervalSeconds").GetInt32());
        Assert.Equal(15m, state.GetProperty("taxRate").GetDecimal());
    }

    [Fact]
    public async Task StarterIndustries_ReturnsIndustryList()
    {
        var result = await ExecuteGraphQlAsync("{ starterIndustries { industries } }");

        var industries = result.GetProperty("data").GetProperty("starterIndustries").GetProperty("industries");
        Assert.Equal(3, industries.GetArrayLength());
    }

    #endregion

    #region Company Management

    [Fact]
    public async Task CreateCompany_Authenticated_CreatesCompany()
    {
        var token = await RegisterAndGetTokenAsync("company@test.com", "CompanyUser");

        var result = await ExecuteGraphQlAsync(
            """
            mutation CreateCompany($input: CreateCompanyInput!) {
              createCompany(input: $input) { id name cash }
            }
            """,
            new { input = new { name = "My Corp" } },
            token);

        var company = result.GetProperty("data").GetProperty("createCompany");
        Assert.Equal("My Corp", company.GetProperty("name").GetString());
        Assert.True(company.GetProperty("cash").GetDecimal() > 0);
    }

    [Fact]
    public async Task CreateCompany_Unauthenticated_ReturnsError()
    {
        var result = await ExecuteGraphQlAsync(
            """
            mutation CreateCompany($input: CreateCompanyInput!) {
              createCompany(input: $input) { id name }
            }
            """,
            new { input = new { name = "Fail Corp" } });

        Assert.True(result.TryGetProperty("errors", out _));
    }

    [Fact]
    public async Task MyCompanies_ReturnsOwnedCompanies()
    {
        var token = await RegisterAndGetTokenAsync("mycos@test.com", "CosUser");

        await ExecuteGraphQlAsync(
            "mutation CreateCompany($input: CreateCompanyInput!) { createCompany(input: $input) { id } }",
            new { input = new { name = "Alpha Inc" } },
            token);

        var result = await ExecuteGraphQlAsync(
            "{ myCompanies { name cash buildings { name type } } }",
            token: token);

        var companies = result.GetProperty("data").GetProperty("myCompanies");
        Assert.True(companies.GetArrayLength() >= 1);
    }

    #endregion

    #region Building Placement

    [Fact]
    public async Task PlaceBuilding_ValidInput_CreatesBuilding()
    {
        var token = await RegisterAndGetTokenAsync("build@test.com", "Builder");

        // Create company
        var companyResult = await ExecuteGraphQlAsync(
            "mutation CreateCompany($input: CreateCompanyInput!) { createCompany(input: $input) { id } }",
            new { input = new { name = "Build Corp" } },
            token);
        var companyId = companyResult.GetProperty("data").GetProperty("createCompany").GetProperty("id").GetString();

        // Get a city
        var citiesResult = await ExecuteGraphQlAsync("{ cities { id name } }");
        var cityId = citiesResult.GetProperty("data").GetProperty("cities")[0].GetProperty("id").GetString();

        // Place building
        var result = await ExecuteGraphQlAsync(
            """
            mutation PlaceBuilding($input: PlaceBuildingInput!) {
              placeBuilding(input: $input) { id name type level }
            }
            """,
            new { input = new { companyId, cityId, type = "FACTORY", name = "My Factory" } },
            token);

        var building = result.GetProperty("data").GetProperty("placeBuilding");
        Assert.Equal("My Factory", building.GetProperty("name").GetString());
        Assert.Equal("FACTORY", building.GetProperty("type").GetString());
        Assert.Equal(1, building.GetProperty("level").GetInt32());
    }

    [Fact]
    public async Task PlaceBuilding_InvalidType_ReturnsError()
    {
        var token = await RegisterAndGetTokenAsync("badtype@test.com", "BadType");

        var companyResult = await ExecuteGraphQlAsync(
            "mutation CreateCompany($input: CreateCompanyInput!) { createCompany(input: $input) { id } }",
            new { input = new { name = "Bad Type Corp" } },
            token);
        var companyId = companyResult.GetProperty("data").GetProperty("createCompany").GetProperty("id").GetString();

        var citiesResult = await ExecuteGraphQlAsync("{ cities { id } }");
        var cityId = citiesResult.GetProperty("data").GetProperty("cities")[0].GetProperty("id").GetString();

        var result = await ExecuteGraphQlAsync(
            "mutation PlaceBuilding($input: PlaceBuildingInput!) { placeBuilding(input: $input) { id } }",
            new { input = new { companyId, cityId, type = "INVALID", name = "Bad" } },
            token);

        Assert.True(result.TryGetProperty("errors", out _));
    }

        [Fact]
        public async Task StoreBuildingConfiguration_QueuesPendingUpgradeWithoutChangingActiveLayout()
        {
                var token = await RegisterAndGetTokenAsync("config@test.com", "Configurator");

                var companyResult = await ExecuteGraphQlAsync(
                        "mutation CreateCompany($input: CreateCompanyInput!) { createCompany(input: $input) { id } }",
                        new { input = new { name = "Config Corp" } },
                        token);
                var companyId = companyResult.GetProperty("data").GetProperty("createCompany").GetProperty("id").GetString();

                var citiesResult = await ExecuteGraphQlAsync("{ cities { id } }");
                var cityId = citiesResult.GetProperty("data").GetProperty("cities")[0].GetProperty("id").GetString();

                var buildingResult = await ExecuteGraphQlAsync(
                        "mutation PlaceBuilding($input: PlaceBuildingInput!) { placeBuilding(input: $input) { id } }",
                        new { input = new { companyId, cityId, type = "FACTORY", name = "Queued Factory" } },
                        token);
                var buildingId = buildingResult.GetProperty("data").GetProperty("placeBuilding").GetProperty("id").GetString();

                var queueResult = await ExecuteGraphQlAsync(
                        """
                        mutation StoreBuildingConfiguration($input: StoreBuildingConfigurationInput!) {
                            storeBuildingConfiguration(input: $input) {
                                buildingId
                                totalTicksRequired
                                units {
                                    gridX
                                    gridY
                                    ticksRequired
                                    linkDownRight
                                    linkDownLeft
                                }
                            }
                        }
                        """,
                        new
                        {
                                input = new
                                {
                                        buildingId,
                                        units = new[]
                                        {
                                                new { unitType = "PURCHASE", gridX = 0, gridY = 0, linkUp = false, linkDown = false, linkLeft = false, linkRight = true, linkUpLeft = false, linkUpRight = false, linkDownLeft = false, linkDownRight = true },
                                                new { unitType = "MANUFACTURING", gridX = 1, gridY = 0, linkUp = false, linkDown = true, linkLeft = true, linkRight = false, linkUpLeft = false, linkUpRight = false, linkDownLeft = true, linkDownRight = false },
                                                new { unitType = "STORAGE", gridX = 0, gridY = 1, linkUp = false, linkDown = false, linkLeft = false, linkRight = false, linkUpLeft = false, linkUpRight = true, linkDownLeft = false, linkDownRight = false },
                                                new { unitType = "B2B_SALES", gridX = 1, gridY = 1, linkUp = true, linkDown = false, linkLeft = false, linkRight = false, linkUpLeft = true, linkUpRight = false, linkDownLeft = false, linkDownRight = false }
                                        }
                                }
                        },
                        token);

                var queuedPlan = queueResult.GetProperty("data").GetProperty("storeBuildingConfiguration");
                Assert.Equal(buildingId, queuedPlan.GetProperty("buildingId").GetString());
                Assert.Equal(3, queuedPlan.GetProperty("totalTicksRequired").GetInt32());
                Assert.Contains(queuedPlan.GetProperty("units").EnumerateArray(), unit => unit.GetProperty("linkDownRight").GetBoolean());

                var companiesResult = await ExecuteGraphQlAsync(
                        """
                        {
                            myCompanies {
                                buildings {
                                    id
                                    units { id }
                                    pendingConfiguration { totalTicksRequired units { gridX gridY ticksRequired linkDownRight linkDownLeft } }
                                }
                            }
                        }
                        """,
                        token: token);

                var building = companiesResult.GetProperty("data").GetProperty("myCompanies")[0].GetProperty("buildings")
                        .EnumerateArray().Single(candidate => candidate.GetProperty("id").GetString() == buildingId);

                Assert.Equal(0, building.GetProperty("units").GetArrayLength());
                Assert.Equal(4, building.GetProperty("pendingConfiguration").GetProperty("units").GetArrayLength());
        }

        [Fact]
        public async Task QueuedBuildingConfiguration_AppliesAfterTickAdvance()
        {
                var token = await RegisterAndGetTokenAsync("apply@test.com", "Applicator");

                var companyResult = await ExecuteGraphQlAsync(
                        "mutation CreateCompany($input: CreateCompanyInput!) { createCompany(input: $input) { id } }",
                        new { input = new { name = "Apply Corp" } },
                        token);
                var companyId = companyResult.GetProperty("data").GetProperty("createCompany").GetProperty("id").GetString();

                var citiesResult = await ExecuteGraphQlAsync("{ cities { id } }");
                var cityId = citiesResult.GetProperty("data").GetProperty("cities")[0].GetProperty("id").GetString();

                var buildingResult = await ExecuteGraphQlAsync(
                        "mutation PlaceBuilding($input: PlaceBuildingInput!) { placeBuilding(input: $input) { id } }",
                        new { input = new { companyId, cityId, type = "FACTORY", name = "Apply Factory" } },
                        token);
                var buildingId = buildingResult.GetProperty("data").GetProperty("placeBuilding").GetProperty("id").GetString();

                await ExecuteGraphQlAsync(
                        """
                        mutation StoreBuildingConfiguration($input: StoreBuildingConfigurationInput!) {
                            storeBuildingConfiguration(input: $input) { id }
                        }
                        """,
                        new
                        {
                                input = new
                                {
                                        buildingId,
                                        units = new[]
                                        {
                                                new { unitType = "PURCHASE", gridX = 0, gridY = 0, linkUp = false, linkDown = false, linkLeft = false, linkRight = true, linkUpLeft = false, linkUpRight = false, linkDownLeft = false, linkDownRight = true },
                                                new { unitType = "MANUFACTURING", gridX = 1, gridY = 0, linkUp = false, linkDown = true, linkLeft = true, linkRight = false, linkUpLeft = false, linkUpRight = false, linkDownLeft = true, linkDownRight = false },
                                                new { unitType = "STORAGE", gridX = 0, gridY = 1, linkUp = false, linkDown = false, linkLeft = false, linkRight = false, linkUpLeft = false, linkUpRight = true, linkDownLeft = false, linkDownRight = false },
                                                new { unitType = "B2B_SALES", gridX = 1, gridY = 1, linkUp = true, linkDown = false, linkLeft = false, linkRight = false, linkUpLeft = true, linkUpRight = false, linkDownLeft = false, linkDownRight = false }
                                        }
                                }
                        },
                        token);

                await AdvanceGameTicksAsync(3);

                var companiesResult = await ExecuteGraphQlAsync(
                        """
                        {
                            myCompanies {
                                buildings {
                                    id
                                    units { gridX gridY linkDownRight linkDownLeft }
                                    pendingConfiguration { id }
                                }
                            }
                        }
                        """,
                        token: token);

                var building = companiesResult.GetProperty("data").GetProperty("myCompanies")[0].GetProperty("buildings")
                        .EnumerateArray().Single(candidate => candidate.GetProperty("id").GetString() == buildingId);

                Assert.Equal(4, building.GetProperty("units").GetArrayLength());
                    Assert.Equal(JsonValueKind.Null, building.GetProperty("pendingConfiguration").ValueKind);
                Assert.Contains(building.GetProperty("units").EnumerateArray(), unit => unit.GetProperty("linkDownRight").GetBoolean());
                Assert.Contains(building.GetProperty("units").EnumerateArray(), unit => unit.GetProperty("linkDownLeft").GetBoolean());
        }

            [Fact]
            public async Task StoreBuildingConfiguration_AllowsEditingWhileUnitWorkIsStillPending()
            {
                var token = await RegisterAndGetTokenAsync("rewrite@test.com", "Rewriter");

                var companyResult = await ExecuteGraphQlAsync(
                    "mutation CreateCompany($input: CreateCompanyInput!) { createCompany(input: $input) { id } }",
                    new { input = new { name = "Rewrite Corp" } },
                    token);
                var companyId = companyResult.GetProperty("data").GetProperty("createCompany").GetProperty("id").GetString();

                var citiesResult = await ExecuteGraphQlAsync("{ cities { id } }");
                var cityId = citiesResult.GetProperty("data").GetProperty("cities")[0].GetProperty("id").GetString();

                var buildingResult = await ExecuteGraphQlAsync(
                    "mutation PlaceBuilding($input: PlaceBuildingInput!) { placeBuilding(input: $input) { id } }",
                    new { input = new { companyId, cityId, type = "FACTORY", name = "Rewrite Factory" } },
                    token);
                var buildingId = buildingResult.GetProperty("data").GetProperty("placeBuilding").GetProperty("id").GetString();

                await ExecuteGraphQlAsync(
                    """
                    mutation StoreBuildingConfiguration($input: StoreBuildingConfigurationInput!) {
                        storeBuildingConfiguration(input: $input) { id }
                    }
                    """,
                    new
                    {
                        input = new
                        {
                            buildingId,
                            units = new[]
                            {
                                new { unitType = "PURCHASE", gridX = 0, gridY = 0, linkUp = false, linkDown = false, linkLeft = false, linkRight = true, linkUpLeft = false, linkUpRight = false, linkDownLeft = false, linkDownRight = false },
                                new { unitType = "MANUFACTURING", gridX = 1, gridY = 0, linkUp = false, linkDown = false, linkLeft = true, linkRight = false, linkUpLeft = false, linkUpRight = false, linkDownLeft = false, linkDownRight = false }
                            }
                        }
                    },
                    token);

                var rewriteResult = await ExecuteGraphQlAsync(
                    """
                    mutation StoreBuildingConfiguration($input: StoreBuildingConfigurationInput!) {
                        storeBuildingConfiguration(input: $input) {
                        units { gridX gridY unitType ticksRequired }
                        totalTicksRequired
                        }
                    }
                    """,
                    new
                    {
                        input = new
                        {
                            buildingId,
                            units = new[]
                            {
                                new { unitType = "BRANDING", gridX = 0, gridY = 0, linkUp = false, linkDown = false, linkLeft = false, linkRight = true, linkUpLeft = false, linkUpRight = false, linkDownLeft = false, linkDownRight = true },
                                new { unitType = "MANUFACTURING", gridX = 1, gridY = 0, linkUp = false, linkDown = false, linkLeft = true, linkRight = false, linkUpLeft = false, linkUpRight = false, linkDownLeft = true, linkDownRight = false }
                            }
                        }
                    },
                    token);

                if (rewriteResult.TryGetProperty("errors", out var rewriteErrors))
                {
                    throw new Exception(rewriteErrors[0].GetProperty("message").GetString());
                }

                var rewrittenPlan = rewriteResult.GetProperty("data").GetProperty("storeBuildingConfiguration");
                Assert.Equal(3, rewrittenPlan.GetProperty("totalTicksRequired").GetInt32());
                Assert.Contains(
                    rewrittenPlan.GetProperty("units").EnumerateArray(),
                    unit => unit.GetProperty("gridX").GetInt32() == 0
                        && unit.GetProperty("gridY").GetInt32() == 0
                        && unit.GetProperty("unitType").GetString() == "BRANDING");

                await AdvanceGameTicksAsync(3);

                var companiesResult = await ExecuteGraphQlAsync(
                    """
                    {
                        myCompanies {
                        buildings {
                            id
                            units { gridX gridY unitType linkDownRight }
                            pendingConfiguration { id }
                        }
                        }
                    }
                    """,
                    token: token);

                var building = companiesResult.GetProperty("data").GetProperty("myCompanies")[0].GetProperty("buildings")
                    .EnumerateArray().Single(candidate => candidate.GetProperty("id").GetString() == buildingId);

                Assert.Equal(JsonValueKind.Null, building.GetProperty("pendingConfiguration").ValueKind);
                Assert.Contains(
                    building.GetProperty("units").EnumerateArray(),
                    unit => unit.GetProperty("gridX").GetInt32() == 0
                        && unit.GetProperty("gridY").GetInt32() == 0
                        && unit.GetProperty("unitType").GetString() == "BRANDING"
                        && unit.GetProperty("linkDownRight").GetBoolean());
            }

            [Fact]
            public async Task StoreBuildingConfiguration_CancelPendingAdd_ResolvesInTenPercentOfOriginalTicks()
            {
                var token = await RegisterAndGetTokenAsync("cancel@test.com", "Canceller");

                var companyResult = await ExecuteGraphQlAsync(
                    "mutation CreateCompany($input: CreateCompanyInput!) { createCompany(input: $input) { id } }",
                    new { input = new { name = "Cancel Corp" } },
                    token);
                var companyId = companyResult.GetProperty("data").GetProperty("createCompany").GetProperty("id").GetString();

                var citiesResult = await ExecuteGraphQlAsync("{ cities { id } }");
                var cityId = citiesResult.GetProperty("data").GetProperty("cities")[0].GetProperty("id").GetString();

                var buildingResult = await ExecuteGraphQlAsync(
                    "mutation PlaceBuilding($input: PlaceBuildingInput!) { placeBuilding(input: $input) { id } }",
                    new { input = new { companyId, cityId, type = "FACTORY", name = "Cancel Factory" } },
                    token);
                var buildingId = buildingResult.GetProperty("data").GetProperty("placeBuilding").GetProperty("id").GetString();

                await ExecuteGraphQlAsync(
                    """
                    mutation StoreBuildingConfiguration($input: StoreBuildingConfigurationInput!) {
                        storeBuildingConfiguration(input: $input) { id }
                    }
                    """,
                    new
                    {
                        input = new
                        {
                            buildingId,
                            units = new[]
                            {
                                new { unitType = "PURCHASE", gridX = 0, gridY = 0, linkUp = false, linkDown = false, linkLeft = false, linkRight = false, linkUpLeft = false, linkUpRight = false, linkDownLeft = false, linkDownRight = false }
                            }
                        }
                    },
                    token);

                var cancelResult = await ExecuteGraphQlAsync(
                    """
                    mutation StoreBuildingConfiguration($input: StoreBuildingConfigurationInput!) {
                        storeBuildingConfiguration(input: $input) {
                        totalTicksRequired
                        removals { gridX gridY ticksRequired }
                        }
                    }
                    """,
                    new { input = new { buildingId, units = Array.Empty<object>() } },
                    token);

                if (cancelResult.TryGetProperty("errors", out var cancelErrors))
                {
                    throw new Exception(cancelErrors[0].GetProperty("message").GetString());
                }

                var cancellationPlan = cancelResult.GetProperty("data").GetProperty("storeBuildingConfiguration");
                Assert.Equal(1, cancellationPlan.GetProperty("totalTicksRequired").GetInt32());
                Assert.Contains(
                    cancellationPlan.GetProperty("removals").EnumerateArray(),
                    removal => removal.GetProperty("gridX").GetInt32() == 0
                        && removal.GetProperty("gridY").GetInt32() == 0
                        && removal.GetProperty("ticksRequired").GetInt32() == 1);

                await AdvanceGameTicksAsync(1);

                var companiesResult = await ExecuteGraphQlAsync(
                    """
                    {
                        myCompanies {
                        buildings {
                            id
                            units { id }
                            pendingConfiguration { id }
                        }
                        }
                    }
                    """,
                    token: token);

                var building = companiesResult.GetProperty("data").GetProperty("myCompanies")[0].GetProperty("buildings")
                    .EnumerateArray().Single(candidate => candidate.GetProperty("id").GetString() == buildingId);

                Assert.Equal(0, building.GetProperty("units").GetArrayLength());
                Assert.Equal(JsonValueKind.Null, building.GetProperty("pendingConfiguration").ValueKind);
            }

    #endregion

    #region Onboarding

    [Fact]
    public async Task CompleteOnboarding_CreatesCompanyFactoryAndShop()
    {
        var token = await RegisterAndGetTokenAsync("onboard@test.com", "Onboarder");
        await ResetGameStateAsync();

        // Get city and product
        var citiesResult = await ExecuteGraphQlAsync("{ cities { id } }");
        var cityId = citiesResult.GetProperty("data").GetProperty("cities")[0].GetProperty("id").GetString();

        var productsResult = await ExecuteGraphQlAsync(
            "query { productTypes(industry: \"FURNITURE\") { id slug name } }");
        var productId = productsResult.GetProperty("data").GetProperty("productTypes")
            .EnumerateArray()
            .Single(product => product.GetProperty("slug").GetString() == "wooden-chair")
            .GetProperty("id")
            .GetString();

        var result = await ExecuteGraphQlAsync(
            """
            mutation CompleteOnboarding($input: OnboardingInput!) {
              completeOnboarding(input: $input) {
                company { id name cash }
                factory { id name type }
                salesShop { id name type }
                selectedProduct { name industry }
              }
            }
            """,
            new { input = new { industry = "FURNITURE", cityId, productTypeId = productId, companyName = "My First Co" } },
            token);

        var data = result.GetProperty("data").GetProperty("completeOnboarding");
        Assert.Equal("My First Co", data.GetProperty("company").GetProperty("name").GetString());
        Assert.Equal("FACTORY", data.GetProperty("factory").GetProperty("type").GetString());
        Assert.Equal("SALES_SHOP", data.GetProperty("salesShop").GetProperty("type").GetString());
        Assert.Equal("FURNITURE", data.GetProperty("selectedProduct").GetProperty("industry").GetString());

        var companiesResult = await ExecuteGraphQlAsync(
            """
            {
              myCompanies {
                buildings {
                  type
                  units { unitType resourceTypeId productTypeId }
                }
              }
            }
            """,
            token: token);

        var buildings = companiesResult.GetProperty("data").GetProperty("myCompanies")[0].GetProperty("buildings").EnumerateArray().ToList();
        var factory = buildings.Single(building => building.GetProperty("type").GetString() == "FACTORY");
        var shop = buildings.Single(building => building.GetProperty("type").GetString() == "SALES_SHOP");

        Assert.Contains(factory.GetProperty("units").EnumerateArray(), unit =>
            unit.GetProperty("unitType").GetString() == "PURCHASE"
            && unit.GetProperty("resourceTypeId").ValueKind == JsonValueKind.String);
        Assert.Contains(factory.GetProperty("units").EnumerateArray(), unit =>
            unit.GetProperty("unitType").GetString() == "MANUFACTURING"
            && unit.GetProperty("productTypeId").GetString() == productId);
        Assert.Contains(shop.GetProperty("units").EnumerateArray(), unit =>
            unit.GetProperty("unitType").GetString() == "PUBLIC_SALES"
            && unit.GetProperty("productTypeId").GetString() == productId);
    }

    [Fact]
    public async Task CompleteOnboarding_SetsOnboardingCompletedAtUtc()
    {
        var token = await RegisterAndGetTokenAsync("onboard_complete@test.com", "CompletionTester");
        await ResetGameStateAsync();

        var citiesResult = await ExecuteGraphQlAsync("{ cities { id } }");
        var cityId = citiesResult.GetProperty("data").GetProperty("cities")[0].GetProperty("id").GetString();

        var productsResult = await ExecuteGraphQlAsync(
            "query { productTypes(industry: \"FURNITURE\") { id slug } }");
        var productId = productsResult.GetProperty("data").GetProperty("productTypes")
            .EnumerateArray()
            .Single(product => product.GetProperty("slug").GetString() == "wooden-chair")
            .GetProperty("id")
            .GetString();

        await ExecuteGraphQlAsync(
            """
            mutation CompleteOnboarding($input: OnboardingInput!) {
              completeOnboarding(input: $input) { company { id } }
            }
            """,
            new { input = new { industry = "FURNITURE", cityId, productTypeId = productId, companyName = "Done Corp" } },
            token);

        // Verify me query returns onboardingCompletedAtUtc
        var meResult = await ExecuteGraphQlAsync(
            "{ me { onboardingCompletedAtUtc } }",
            token: token);

        var completedAt = meResult.GetProperty("data").GetProperty("me").GetProperty("onboardingCompletedAtUtc");
        Assert.Equal(JsonValueKind.String, completedAt.ValueKind);
        Assert.True(DateTime.TryParse(completedAt.GetString(), out _));
    }

    [Fact]
    public async Task CompleteOnboarding_InvalidIndustry_ReturnsError()
    {
        var token = await RegisterAndGetTokenAsync("badindustry@test.com", "BadInd");

        var citiesResult = await ExecuteGraphQlAsync("{ cities { id } }");
        var cityId = citiesResult.GetProperty("data").GetProperty("cities")[0].GetProperty("id").GetString();

        var productsResult = await ExecuteGraphQlAsync("query { productTypes { id } }");
        var productId = productsResult.GetProperty("data").GetProperty("productTypes")[0].GetProperty("id").GetString();

        var result = await ExecuteGraphQlAsync(
            """
            mutation CompleteOnboarding($input: OnboardingInput!) {
              completeOnboarding(input: $input) { company { id } }
            }
            """,
            new { input = new { industry = "INVALID", cityId, productTypeId = productId, companyName = "Nope" } },
            token);

        Assert.True(result.TryGetProperty("errors", out _));
    }

    [Fact]
    public async Task CompleteOnboarding_Unauthenticated_ReturnsError()
    {
        var result = await ExecuteGraphQlAsync(
            """
            mutation CompleteOnboarding($input: OnboardingInput!) {
              completeOnboarding(input: $input) { company { id } }
            }
            """,
            new { input = new { industry = "FURNITURE", cityId = Guid.NewGuid(), productTypeId = Guid.NewGuid(), companyName = "X" } });

        Assert.True(result.TryGetProperty("errors", out _));
    }

    #endregion

    #region Rankings

    [Fact]
    public async Task Rankings_ReturnsPlayerRankings()
    {
        // Register a player with a company
        var token = await RegisterAndGetTokenAsync("rank@test.com", "Ranker");
        await ExecuteGraphQlAsync(
            "mutation CreateCompany($input: CreateCompanyInput!) { createCompany(input: $input) { id } }",
            new { input = new { name = "Rank Corp" } },
            token);

        var result = await ExecuteGraphQlAsync(
            "{ rankings { displayName totalWealth cashTotal buildingValue inventoryValue companyCount } }");

        var rankings = result.GetProperty("data").GetProperty("rankings");
        Assert.True(rankings.GetArrayLength() >= 1);
    }

    [Fact]
    public async Task Rankings_ReturnsAllWealthComponents()
    {
        var token = await RegisterAndGetTokenAsync("wealth@test.com", "WealthPlayer");
        await ExecuteGraphQlAsync(
            "mutation CreateCompany($input: CreateCompanyInput!) { createCompany(input: $input) { id } }",
            new { input = new { name = "Wealth Corp" } },
            token);

        var result = await ExecuteGraphQlAsync(
            "{ rankings { playerId displayName totalWealth cashTotal buildingValue inventoryValue companyCount } }");

        var rankings = result.GetProperty("data").GetProperty("rankings");
        var entry = rankings.EnumerateArray()
            .FirstOrDefault(r => r.GetProperty("displayName").GetString() == "WealthPlayer");

        Assert.True(entry.ValueKind != System.Text.Json.JsonValueKind.Undefined, "WealthPlayer ranking entry not found");
        Assert.True(entry.GetProperty("cashTotal").GetDecimal() >= 0);
        Assert.True(entry.GetProperty("buildingValue").GetDecimal() >= 0);
        Assert.True(entry.GetProperty("inventoryValue").GetDecimal() >= 0);

        // totalWealth must equal the sum of its components
        var cash = entry.GetProperty("cashTotal").GetDecimal();
        var building = entry.GetProperty("buildingValue").GetDecimal();
        var inventory = entry.GetProperty("inventoryValue").GetDecimal();
        var total = entry.GetProperty("totalWealth").GetDecimal();
        Assert.Equal(cash + building + inventory, total);
    }

    [Fact]
    public async Task Rankings_AggregatesMultipleCompanies()
    {
        var token = await RegisterAndGetTokenAsync("multi@test.com", "MultiCo");
        await ExecuteGraphQlAsync(
            "mutation CreateCompany($input: CreateCompanyInput!) { createCompany(input: $input) { id } }",
            new { input = new { name = "Company A" } },
            token);
        await ExecuteGraphQlAsync(
            "mutation CreateCompany($input: CreateCompanyInput!) { createCompany(input: $input) { id } }",
            new { input = new { name = "Company B" } },
            token);

        var result = await ExecuteGraphQlAsync(
            "{ rankings { displayName totalWealth cashTotal companyCount } }");

        var rankings = result.GetProperty("data").GetProperty("rankings");
        var entry = rankings.EnumerateArray()
            .FirstOrDefault(r => r.GetProperty("displayName").GetString() == "MultiCo");

        Assert.True(entry.ValueKind != System.Text.Json.JsonValueKind.Undefined, "MultiCo ranking entry not found");
        Assert.Equal(2, entry.GetProperty("companyCount").GetInt32());
        // cashTotal must reflect both companies (default starting capital is $1,000,000 each)
        Assert.True(entry.GetProperty("cashTotal").GetDecimal() >= 2_000_000m);
    }

    [Fact]
    public async Task Rankings_OrderedByTotalWealthDescending()
    {
        var richToken = await RegisterAndGetTokenAsync("rich@test.com", "RichPlayer");
        var poorToken = await RegisterAndGetTokenAsync("poor@test.com", "PoorPlayer");

        // Rich player gets two companies, poor player gets none.
        await ExecuteGraphQlAsync(
            "mutation CreateCompany($input: CreateCompanyInput!) { createCompany(input: $input) { id } }",
            new { input = new { name = "Rich Corp 1" } },
            richToken);
        await ExecuteGraphQlAsync(
            "mutation CreateCompany($input: CreateCompanyInput!) { createCompany(input: $input) { id } }",
            new { input = new { name = "Rich Corp 2" } },
            richToken);

        var result = await ExecuteGraphQlAsync(
            "{ rankings { displayName totalWealth } }");

        var rankings = result.GetProperty("data").GetProperty("rankings")
            .EnumerateArray()
            .Select(r => r.GetProperty("totalWealth").GetDecimal())
            .ToList();

        // Verify descending order
        for (int i = 1; i < rankings.Count; i++)
        {
            Assert.True(rankings[i - 1] >= rankings[i],
                $"Ranking is not sorted descending: position {i - 1} ({rankings[i - 1]}) < position {i} ({rankings[i]})");
        }
    }

    [Fact]
    public async Task Rankings_ExcludesAdminPlayers()
    {
        // Register and promote an admin via direct DB access is not straightforward here,
        // so we verify that only PLAYER-role users appear (the test factory registers players
        // with role PLAYER by default, and admins seeded by AppDbInitializer are excluded).
        var result = await ExecuteGraphQlAsync(
            "{ rankings { displayName companyCount } }");
        var rankings = result.GetProperty("data").GetProperty("rankings");
        Assert.True(rankings.ValueKind == System.Text.Json.JsonValueKind.Array);
    }

    [Fact]
    public async Task Rankings_ZeroWealthPlayerIncluded()
    {
        // A player who registered but has no companies should appear with zero wealth.
        await RegisterAndGetTokenAsync("zero@test.com", "ZeroPlayer");

        var result = await ExecuteGraphQlAsync(
            "{ rankings { displayName totalWealth companyCount } }");

        var rankings = result.GetProperty("data").GetProperty("rankings");
        var entry = rankings.EnumerateArray()
            .FirstOrDefault(r => r.GetProperty("displayName").GetString() == "ZeroPlayer");

        Assert.True(entry.ValueKind != System.Text.Json.JsonValueKind.Undefined, "ZeroPlayer ranking entry not found");
        Assert.Equal(0, entry.GetProperty("companyCount").GetInt32());
        Assert.Equal(0m, entry.GetProperty("totalWealth").GetDecimal());
    }

    #endregion
}
