using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Api.Data;
using Api.Data.Entities;
using Api.Tests.Infrastructure;
using Api.Utilities;
using Microsoft.EntityFrameworkCore;
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

    private async Task<(string CompanyId, string ProductId, JsonElement Result)> CompleteOnboardingAsync(
        string token,
        string companyName = "My First Co")
    {
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

        var result = await ExecuteGraphQlAsync(
            """
            mutation CompleteOnboarding($input: OnboardingInput!) {
              completeOnboarding(input: $input) {
                company { id name cash }
                factory { id name type }
                salesShop { id name type }
                selectedProduct { name industry }
                startupPackOffer {
                  status
                  companyCashGrant
                  proDurationDays
                  expiresAtUtc
                }
              }
            }
            """,
            new { input = new { industry = "FURNITURE", cityId, productTypeId = productId, companyName } },
            token);

        var companyId = result.GetProperty("data").GetProperty("completeOnboarding").GetProperty("company").GetProperty("id").GetString()!;
        return (companyId, productId!, result);
    }

    private async Task<string> GetCityIdByNameAsync(string cityName = "Bratislava")
    {
        var citiesResult = await ExecuteGraphQlAsync("{ cities { id name } }");
        return citiesResult.GetProperty("data").GetProperty("cities").EnumerateArray()
            .First(city => city.GetProperty("name").GetString() == cityName)
            .GetProperty("id")
            .GetString()!;
    }

    private async Task<string> GetStarterProductIdAsync(string industry = "FURNITURE", string slug = "wooden-chair")
    {
        var productsResult = await ExecuteGraphQlAsync($"query {{ productTypes(industry: \"{industry}\") {{ id slug }} }}");
        return productsResult.GetProperty("data").GetProperty("productTypes")
            .EnumerateArray()
            .Single(product => product.GetProperty("slug").GetString() == slug)
            .GetProperty("id")
            .GetString()!;
    }

    private async Task<string> GetAvailableLotIdAsync(string cityId, string suitableType)
    {
        var lotsResult = await ExecuteGraphQlAsync(
            """
            query CityLots($cityId: UUID!) {
              cityLots(cityId: $cityId) { id suitableTypes ownerCompanyId }
            }
            """,
            new { cityId });

        return lotsResult.GetProperty("data").GetProperty("cityLots").EnumerateArray()
            .First(lot => lot.GetProperty("suitableTypes").GetString()!.Contains(suitableType)
                          && lot.GetProperty("ownerCompanyId").ValueKind == JsonValueKind.Null)
            .GetProperty("id")
            .GetString()!;
    }

    private async Task<string> CreateTestLotAsync(
        string cityId,
        string suitableTypes,
        string district,
        decimal price = 75_000m,
        string? name = null)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var city = await db.Cities.FirstAsync(candidate => candidate.Id == Guid.Parse(cityId));
        var lot = new BuildingLot
        {
            Id = Guid.NewGuid(),
            CityId = city.Id,
            Name = name ?? $"Test Lot {Guid.NewGuid():N}"[..17],
            Description = "Test lot for onboarding flow coverage.",
            District = district,
            Latitude = city.Latitude + 0.01,
            Longitude = city.Longitude + 0.01,
            Price = price,
            SuitableTypes = suitableTypes,
            ConcurrencyToken = Guid.NewGuid()
        };
        db.BuildingLots.Add(lot);
        await db.SaveChangesAsync();
        return lot.Id.ToString();
    }

    private async Task<(string CompanyId, string FactoryLotId, string CityId, JsonElement Result)> StartOnboardingCompanyAsync(
        string token,
        string companyName = "Map Starter Co",
        string? factoryLotId = null)
    {
        var cityId = await GetCityIdByNameAsync();
        factoryLotId ??= await CreateTestLotAsync(cityId, "FACTORY,MINE", "Industrial Zone");

        var result = await ExecuteGraphQlAsync(
            """
            mutation StartOnboardingCompany($input: StartOnboardingCompanyInput!) {
              startOnboardingCompany(input: $input) {
                nextStep
                company { id name cash }
                factory { id name type latitude longitude }
                factoryLot { id ownerCompanyId buildingId }
              }
            }
            """,
            new { input = new { industry = "FURNITURE", cityId, companyName, factoryLotId } },
            token);

        var companyId = result.GetProperty("data").GetProperty("startOnboardingCompany").GetProperty("company").GetProperty("id").GetString()!;
        return (companyId, factoryLotId, cityId, result);
    }

    private async Task<JsonElement> FinishOnboardingAsync(string token, string productId, string shopLotId)
    {
        return await ExecuteGraphQlAsync(
            """
            mutation FinishOnboarding($input: FinishOnboardingInput!) {
              finishOnboarding(input: $input) {
                company { id name cash }
                factory { id name type }
                salesShop { id name type }
                selectedProduct { id name industry }
              }
            }
            """,
            new { input = new { productTypeId = productId, shopLotId } },
            token);
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
    public async Task ProductTypes_ReturnAccessMetadataForFreeAndProPlayers()
    {
        var freeResult = await ExecuteGraphQlAsync(
            """
            {
              productTypes(industry: "ELECTRONICS") {
                slug
                isProOnly
                isUnlockedForCurrentPlayer
              }
            }
            """);

        var freeProduct = freeResult.GetProperty("data").GetProperty("productTypes")
            .EnumerateArray()
            .First(product => product.GetProperty("slug").GetString() == "electronic-components");

        Assert.True(freeProduct.GetProperty("isProOnly").GetBoolean());
        Assert.False(freeProduct.GetProperty("isUnlockedForCurrentPlayer").GetBoolean());

        var token = await RegisterAndGetTokenAsync("pro-catalog@test.com", "Pro Catalog");
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var player = await db.Players.SingleAsync(candidate => candidate.Email == "pro-catalog@test.com");
            player.ProSubscriptionEndsAtUtc = DateTime.UtcNow.AddDays(30);
            await db.SaveChangesAsync();
        }

        var proResult = await ExecuteGraphQlAsync(
            """
            {
              productTypes(industry: "ELECTRONICS") {
                slug
                isProOnly
                isUnlockedForCurrentPlayer
              }
            }
            """,
            token: token);

        var proProduct = proResult.GetProperty("data").GetProperty("productTypes")
            .EnumerateArray()
            .First(product => product.GetProperty("slug").GetString() == "electronic-components");

        Assert.True(proProduct.GetProperty("isProOnly").GetBoolean());
        Assert.True(proProduct.GetProperty("isUnlockedForCurrentPlayer").GetBoolean());
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
    public async Task StoreBuildingConfiguration_NonProCannotAssignNewProProduct()
    {
        var token = await RegisterAndGetTokenAsync("pro-config@test.com", "Pro Config");

        var companyResult = await ExecuteGraphQlAsync(
            "mutation CreateCompany($input: CreateCompanyInput!) { createCompany(input: $input) { id } }",
            new { input = new { name = "Locked Products Ltd" } },
            token);
        var companyId = companyResult.GetProperty("data").GetProperty("createCompany").GetProperty("id").GetString();

        var citiesResult = await ExecuteGraphQlAsync("{ cities { id } }");
        var cityId = citiesResult.GetProperty("data").GetProperty("cities")[0].GetProperty("id").GetString();

        var buildingResult = await ExecuteGraphQlAsync(
            "mutation PlaceBuilding($input: PlaceBuildingInput!) { placeBuilding(input: $input) { id } }",
            new { input = new { companyId, cityId, type = "FACTORY", name = "Locked Factory" } },
            token);
        var buildingId = buildingResult.GetProperty("data").GetProperty("placeBuilding").GetProperty("id").GetString();

        var productResult = await ExecuteGraphQlAsync(
            """
            {
              productTypes(industry: "ELECTRONICS") {
                id
                slug
              }
            }
            """);
        var proProductId = productResult.GetProperty("data").GetProperty("productTypes")
            .EnumerateArray()
            .First(product => product.GetProperty("slug").GetString() == "electronic-components")
            .GetProperty("id")
            .GetString();

        var result = await ExecuteGraphQlAsync(
            """
            mutation StoreBuildingConfiguration($input: StoreBuildingConfigurationInput!) {
              storeBuildingConfiguration(input: $input) {
                id
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
                        new { unitType = "PURCHASE", gridX = 0, gridY = 0, linkUp = false, linkDown = false, linkLeft = false, linkRight = true, linkUpLeft = false, linkUpRight = false, linkDownLeft = false, linkDownRight = false, resourceTypeId = (string?)null, productTypeId = (string?)null },
                        new { unitType = "MANUFACTURING", gridX = 1, gridY = 0, linkUp = false, linkDown = false, linkLeft = true, linkRight = false, linkUpLeft = false, linkUpRight = false, linkDownLeft = false, linkDownRight = false, resourceTypeId = (string?)null, productTypeId = proProductId }
                    }
                }
            },
            token);

        Assert.True(result.TryGetProperty("errors", out var errors));
        Assert.Equal("PRO_SUBSCRIPTION_REQUIRED", errors[0].GetProperty("extensions").GetProperty("code").GetString());
        Assert.Contains("unlocks additional products to manufacture and sell", errors[0].GetProperty("message").GetString());
    }

    [Fact]
    public async Task StoreBuildingConfiguration_ExpiredProCanKeepExistingLockedProductInPlace()
    {
        var token = await RegisterAndGetTokenAsync("expired-pro@test.com", "Expired Pro");

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var player = await db.Players.SingleAsync(candidate => candidate.Email == "expired-pro@test.com");
            player.ProSubscriptionEndsAtUtc = DateTime.UtcNow.AddDays(30);
            await db.SaveChangesAsync();
        }

        var companyResult = await ExecuteGraphQlAsync(
            "mutation CreateCompany($input: CreateCompanyInput!) { createCompany(input: $input) { id } }",
            new { input = new { name = "Legacy Pro Co" } },
            token);
        var companyId = companyResult.GetProperty("data").GetProperty("createCompany").GetProperty("id").GetString();

        var citiesResult = await ExecuteGraphQlAsync("{ cities { id } }");
        var cityId = citiesResult.GetProperty("data").GetProperty("cities")[0].GetProperty("id").GetString();

        var buildingResult = await ExecuteGraphQlAsync(
            "mutation PlaceBuilding($input: PlaceBuildingInput!) { placeBuilding(input: $input) { id } }",
            new { input = new { companyId, cityId, type = "FACTORY", name = "Legacy Pro Factory" } },
            token);
        var buildingId = buildingResult.GetProperty("data").GetProperty("placeBuilding").GetProperty("id").GetString()!;

        var productResult = await ExecuteGraphQlAsync(
            """
            {
              productTypes(industry: "ELECTRONICS") {
                id
                slug
              }
            }
            """,
            token: token);
        var proProductId = productResult.GetProperty("data").GetProperty("productTypes")
            .EnumerateArray()
            .First(product => product.GetProperty("slug").GetString() == "electronic-components")
            .GetProperty("id")
            .GetString();

        await ExecuteGraphQlAsync(
            """
            mutation StoreBuildingConfiguration($input: StoreBuildingConfigurationInput!) {
              storeBuildingConfiguration(input: $input) {
                id
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
                        new { unitType = "PURCHASE", gridX = 0, gridY = 0, linkUp = false, linkDown = false, linkLeft = false, linkRight = true, linkUpLeft = false, linkUpRight = false, linkDownLeft = false, linkDownRight = false, resourceTypeId = (string?)null, productTypeId = (string?)null },
                        new { unitType = "MANUFACTURING", gridX = 1, gridY = 0, linkUp = false, linkDown = false, linkLeft = true, linkRight = false, linkUpLeft = false, linkUpRight = false, linkDownLeft = false, linkDownRight = false, resourceTypeId = (string?)null, productTypeId = proProductId }
                    }
                }
            },
            token);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var player = await db.Players.SingleAsync(candidate => candidate.Email == "expired-pro@test.com");
            player.ProSubscriptionEndsAtUtc = DateTime.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
        }

        var retainResult = await ExecuteGraphQlAsync(
            """
            mutation StoreBuildingConfiguration($input: StoreBuildingConfigurationInput!) {
              storeBuildingConfiguration(input: $input) {
                id
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
                        new { unitType = "PURCHASE", gridX = 0, gridY = 0, linkUp = false, linkDown = false, linkLeft = false, linkRight = true, linkUpLeft = false, linkUpRight = false, linkDownLeft = false, linkDownRight = false, resourceTypeId = (string?)null, productTypeId = (string?)null },
                        new { unitType = "MANUFACTURING", gridX = 1, gridY = 0, linkUp = false, linkDown = false, linkLeft = true, linkRight = true, linkUpLeft = false, linkUpRight = false, linkDownLeft = false, linkDownRight = false, resourceTypeId = (string?)null, productTypeId = proProductId }
                    }
                }
            },
            token);

        Assert.False(retainResult.TryGetProperty("errors", out _));

        var moveResult = await ExecuteGraphQlAsync(
            """
            mutation StoreBuildingConfiguration($input: StoreBuildingConfigurationInput!) {
              storeBuildingConfiguration(input: $input) {
                id
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
                        new { unitType = "PURCHASE", gridX = 0, gridY = 0, linkUp = false, linkDown = false, linkLeft = false, linkRight = false, linkUpLeft = false, linkUpRight = false, linkDownLeft = false, linkDownRight = false, resourceTypeId = (string?)null, productTypeId = (string?)null },
                        new { unitType = "MANUFACTURING", gridX = 2, gridY = 0, linkUp = false, linkDown = false, linkLeft = false, linkRight = false, linkUpLeft = false, linkUpRight = false, linkDownLeft = false, linkDownRight = false, resourceTypeId = (string?)null, productTypeId = proProductId }
                    }
                }
            },
            token);

        Assert.True(moveResult.TryGetProperty("errors", out var moveErrors));
        Assert.Equal("PRO_SUBSCRIPTION_REQUIRED", moveErrors[0].GetProperty("extensions").GetProperty("code").GetString());
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
        var (_, productId, result) = await CompleteOnboardingAsync(token);

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
        await CompleteOnboardingAsync(token, "Done Corp");

        // Verify me query returns onboardingCompletedAtUtc
        var meResult = await ExecuteGraphQlAsync(
            "{ me { onboardingCompletedAtUtc } }",
            token: token);

        var completedAt = meResult.GetProperty("data").GetProperty("me").GetProperty("onboardingCompletedAtUtc");
        Assert.Equal(JsonValueKind.String, completedAt.ValueKind);
        Assert.True(DateTime.TryParse(completedAt.GetString(), out _));
    }

    [Fact]
    public async Task StartOnboardingCompany_PurchasesFactoryLot_AndStoresResumeMetadata()
    {
        var token = await RegisterAndGetTokenAsync($"onboard-map-start-{Guid.NewGuid()}@test.com", "Factory Founder");
        var (_, factoryLotId, _, result) = await StartOnboardingCompanyAsync(token, "Factory Founder Co");

        var data = result.GetProperty("data").GetProperty("startOnboardingCompany");
        Assert.Equal("SHOP_SELECTION", data.GetProperty("nextStep").GetString());
        Assert.Equal("FACTORY", data.GetProperty("factory").GetProperty("type").GetString());
        Assert.Equal("Factory Founder Co", data.GetProperty("company").GetProperty("name").GetString());
        Assert.True(data.GetProperty("company").GetProperty("cash").GetDecimal() < 500_000m);

        var meResult = await ExecuteGraphQlAsync(
            """
            {
              me {
                onboardingCompletedAtUtc
                onboardingCurrentStep
                onboardingIndustry
                onboardingCityId
                onboardingCompanyId
                onboardingFactoryLotId
                companies { id name cash }
              }
            }
            """,
            token: token);

        var me = meResult.GetProperty("data").GetProperty("me");
        Assert.Equal(JsonValueKind.Null, me.GetProperty("onboardingCompletedAtUtc").ValueKind);
        Assert.Equal("SHOP_SELECTION", me.GetProperty("onboardingCurrentStep").GetString());
        Assert.Equal("FURNITURE", me.GetProperty("onboardingIndustry").GetString());
        Assert.Equal(factoryLotId, me.GetProperty("onboardingFactoryLotId").GetString());
        Assert.Equal(1, me.GetProperty("companies").GetArrayLength());
    }

    [Fact]
    public async Task FinishOnboarding_UsesAuthoritativeLotValidation_AndCompletesFlow()
    {
        var token = await RegisterAndGetTokenAsync($"onboard-map-finish-{Guid.NewGuid()}@test.com", "Retail Founder");
        var (companyId, _, cityId, startResult) = await StartOnboardingCompanyAsync(token, "Retail Founder Co");
        var cashAfterFactory = startResult.GetProperty("data").GetProperty("startOnboardingCompany").GetProperty("company").GetProperty("cash").GetDecimal();
        var productId = await GetStarterProductIdAsync();
        var shopLotId = await CreateTestLotAsync(cityId, "SALES_SHOP,COMMERCIAL", "Commercial District", 95_000m, "Test Shop Lot");

        var result = await FinishOnboardingAsync(token, productId, shopLotId);
        var data = result.GetProperty("data").GetProperty("finishOnboarding");

        Assert.Equal(companyId, data.GetProperty("company").GetProperty("id").GetString());
        Assert.Equal("SALES_SHOP", data.GetProperty("salesShop").GetProperty("type").GetString());
        Assert.Equal("FURNITURE", data.GetProperty("selectedProduct").GetProperty("industry").GetString());
        Assert.True(data.GetProperty("company").GetProperty("cash").GetDecimal() < cashAfterFactory);

        var meResult = await ExecuteGraphQlAsync(
            """
            {
              me {
                onboardingCompletedAtUtc
                onboardingCurrentStep
                onboardingCompanyId
                onboardingFactoryLotId
                onboardingShopBuildingId
              }
            }
            """,
            token: token);

        var me = meResult.GetProperty("data").GetProperty("me");
        Assert.Equal(JsonValueKind.String, me.GetProperty("onboardingCompletedAtUtc").ValueKind);
        Assert.Equal(JsonValueKind.Null, me.GetProperty("onboardingCurrentStep").ValueKind);
        Assert.Equal(JsonValueKind.Null, me.GetProperty("onboardingCompanyId").ValueKind);
        Assert.Equal(JsonValueKind.Null, me.GetProperty("onboardingFactoryLotId").ValueKind);
        // Shop building ID should be persisted for the post-completion configure-guide step
        Assert.Equal(JsonValueKind.String, me.GetProperty("onboardingShopBuildingId").ValueKind);
    }

    [Fact]
    public async Task StartOnboardingCompany_UnsuitableFactoryLot_Fails()
    {
        var token = await RegisterAndGetTokenAsync($"onboard-map-invalid-{Guid.NewGuid()}@test.com", "Wrong Plot");
        var cityId = await GetCityIdByNameAsync();
        var apartmentLotId = await CreateTestLotAsync(cityId, "APARTMENT", "Residential Quarter", 110_000m, "Unsuitable Apartment Lot");

        var result = await ExecuteGraphQlAsync(
            """
            mutation StartOnboardingCompany($input: StartOnboardingCompanyInput!) {
              startOnboardingCompany(input: $input) { company { id } }
            }
            """,
            new { input = new { industry = "FURNITURE", cityId, companyName = "Wrong Plot Co", factoryLotId = apartmentLotId } },
            token);

        Assert.True(result.TryGetProperty("errors", out var errors));
        Assert.Contains("not suitable", errors[0].GetProperty("message").GetString());
    }

    [Fact]
    public async Task FinishOnboarding_WhenShopLotBecomesUnavailable_PlayerCanResumeWithAnotherLot()
    {
        var token1 = await RegisterAndGetTokenAsync($"onboard-map-recover-a-{Guid.NewGuid()}@test.com", "Recover A");
        var (_, _, cityId, _) = await StartOnboardingCompanyAsync(token1, "Recover A Co");
        var productId = await GetStarterProductIdAsync();
        var sharedShopLotId = await CreateTestLotAsync(cityId, "SALES_SHOP,COMMERCIAL", "Commercial District", 90_000m, "Shared Shop Lot");

        var token2 = await RegisterAndGetTokenAsync($"onboard-map-recover-b-{Guid.NewGuid()}@test.com", "Recover B");
        var (companyId2, _, _) = await CompleteOnboardingAsync(token2, "Recover B Co");
        await ExecuteGraphQlAsync(
            """
            mutation PurchaseLot($input: PurchaseLotInput!) {
              purchaseLot(input: $input) { lot { id } }
            }
            """,
            new { input = new { companyId = companyId2, lotId = sharedShopLotId, buildingType = "SALES_SHOP", buildingName = "Blocking Shop" } },
            token2);

        var failedResult = await FinishOnboardingAsync(token1, productId, sharedShopLotId);
        Assert.True(failedResult.TryGetProperty("errors", out var errors));
        Assert.Contains("already been purchased", errors[0].GetProperty("message").GetString());

        var meResult = await ExecuteGraphQlAsync(
            """
            {
              me {
                onboardingCompletedAtUtc
                onboardingCurrentStep
              }
            }
            """,
            token: token1);

        var me = meResult.GetProperty("data").GetProperty("me");
        Assert.Equal(JsonValueKind.Null, me.GetProperty("onboardingCompletedAtUtc").ValueKind);
        Assert.Equal("SHOP_SELECTION", me.GetProperty("onboardingCurrentStep").GetString());
    }

    [Fact]
    public async Task CompleteOnboarding_ReturnsEligibleStartupPackOffer()
    {
        var token = await RegisterAndGetTokenAsync("startup-pack@test.com", "StartupPacker");

        var (_, _, result) = await CompleteOnboardingAsync(token, "Offer Corp");

        var offer = result.GetProperty("data").GetProperty("completeOnboarding").GetProperty("startupPackOffer");
        Assert.Equal("ELIGIBLE", offer.GetProperty("status").GetString());
        Assert.Equal(250000m, offer.GetProperty("companyCashGrant").GetDecimal());
        Assert.Equal(90, offer.GetProperty("proDurationDays").GetInt32());
        Assert.True(DateTime.TryParse(offer.GetProperty("expiresAtUtc").GetString(), out _));
    }

    [Fact]
    public async Task StartupPackOffer_PreviouslyOnboardedPlayer_IsBackfilledOnFirstQuery()
    {
        const string email = "backfill@test.com";
        var token = await RegisterAndGetTokenAsync(email, "BackfillPlayer");

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var player = await db.Players.SingleAsync(candidate => candidate.Email == email);
            player.OnboardingCompletedAtUtc = DateTime.UtcNow.AddDays(-2);
            db.Companies.Add(new Api.Data.Entities.Company
            {
                Id = Guid.NewGuid(),
                PlayerId = player.Id,
                Name = "Legacy Corp",
                Cash = 600000m,
                FoundedAtUtc = DateTime.UtcNow.AddDays(-2)
            });
            await db.SaveChangesAsync();
        }

        var result = await ExecuteGraphQlAsync(
            "{ startupPackOffer { status companyCashGrant proDurationDays } }",
            token: token);

        var offer = result.GetProperty("data").GetProperty("startupPackOffer");
        Assert.Equal("ELIGIBLE", offer.GetProperty("status").GetString());
        Assert.Equal(250000m, offer.GetProperty("companyCashGrant").GetDecimal());
        Assert.Equal(90, offer.GetProperty("proDurationDays").GetInt32());
    }

    [Fact]
    public async Task StartupPackOffer_BeforeOnboarding_ReturnsNull()
    {
        var token = await RegisterAndGetTokenAsync("startup-pack-none@test.com", "NoOfferYet");

        var result = await ExecuteGraphQlAsync(
            "{ startupPackOffer { status } }",
            token: token);

        Assert.Equal(JsonValueKind.Null, result.GetProperty("data").GetProperty("startupPackOffer").ValueKind);
    }

    [Fact]
    public async Task StartupPackOffer_ShownAndDismissed_StoresLifecycleState()
    {
        var token = await RegisterAndGetTokenAsync("startup-pack-lifecycle@test.com", "LifecyclePlayer");
        await CompleteOnboardingAsync(token, "Lifecycle Corp");

        var shownResult = await ExecuteGraphQlAsync(
            """
            mutation {
              markStartupPackOfferShown {
                status
                shownAtUtc
                dismissedAtUtc
              }
            }
            """,
            token: token);

        var shownOffer = shownResult.GetProperty("data").GetProperty("markStartupPackOfferShown");
        Assert.Equal("SHOWN", shownOffer.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.String, shownOffer.GetProperty("shownAtUtc").ValueKind);
        Assert.Equal(JsonValueKind.Null, shownOffer.GetProperty("dismissedAtUtc").ValueKind);

        var dismissedResult = await ExecuteGraphQlAsync(
            """
            mutation {
              dismissStartupPackOffer {
                status
                shownAtUtc
                dismissedAtUtc
              }
            }
            """,
            token: token);

        var dismissedOffer = dismissedResult.GetProperty("data").GetProperty("dismissStartupPackOffer");
        Assert.Equal("DISMISSED", dismissedOffer.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.String, dismissedOffer.GetProperty("shownAtUtc").ValueKind);
        Assert.Equal(JsonValueKind.String, dismissedOffer.GetProperty("dismissedAtUtc").ValueKind);
    }

    [Fact]
    public async Task ClaimStartupPack_IsIdempotentAndGrantsEntitlementsOnce()
    {
        var token = await RegisterAndGetTokenAsync("startup-pack-claim@test.com", "ClaimPlayer");
        var (companyId, _, _) = await CompleteOnboardingAsync(token, "Claim Corp");

        var firstClaim = await ExecuteGraphQlAsync(
            """
            mutation ClaimStartupPack($input: ClaimStartupPackInput!) {
              claimStartupPack(input: $input) {
                offer { status claimedAtUtc grantedCompanyId }
                company { id cash }
                proSubscriptionEndsAtUtc
              }
            }
            """,
            new { input = new { companyId } },
            token);

        var firstClaimData = firstClaim.GetProperty("data").GetProperty("claimStartupPack");
        Assert.Equal("CLAIMED", firstClaimData.GetProperty("offer").GetProperty("status").GetString());
        Assert.Equal(500_000m + StartupPackService.CompanyCashGrant, firstClaimData.GetProperty("company").GetProperty("cash").GetDecimal());
        var firstProEndsAt = firstClaimData.GetProperty("proSubscriptionEndsAtUtc").GetString();

        var secondClaim = await ExecuteGraphQlAsync(
            """
            mutation ClaimStartupPack($input: ClaimStartupPackInput!) {
              claimStartupPack(input: $input) {
                offer { status claimedAtUtc grantedCompanyId }
                company { id cash }
                proSubscriptionEndsAtUtc
              }
            }
            """,
            new { input = new { companyId } },
            token);

        var secondClaimData = secondClaim.GetProperty("data").GetProperty("claimStartupPack");
        Assert.Equal("CLAIMED", secondClaimData.GetProperty("offer").GetProperty("status").GetString());
        Assert.Equal(500_000m + StartupPackService.CompanyCashGrant, secondClaimData.GetProperty("company").GetProperty("cash").GetDecimal());
        Assert.Equal(firstProEndsAt, secondClaimData.GetProperty("proSubscriptionEndsAtUtc").GetString());

        var meResult = await ExecuteGraphQlAsync(
            "{ me { proSubscriptionEndsAtUtc } }",
            token: token);
        Assert.Equal(firstProEndsAt, meResult.GetProperty("data").GetProperty("me").GetProperty("proSubscriptionEndsAtUtc").GetString());
    }

    [Fact]
    public async Task ClaimStartupPack_ConcurrentRequests_SettleEconomyAndPremiumOnce()
    {
        var token = await RegisterAndGetTokenAsync("startup-pack-concurrent@test.com", "ConcurrentPlayer");
        var (companyId, _, _) = await CompleteOnboardingAsync(token, "Concurrent Corp");

        const string claimMutation = """
            mutation ClaimStartupPack($input: ClaimStartupPackInput!) {
              claimStartupPack(input: $input) {
                offer { status claimedAtUtc grantedCompanyId }
                company { id cash }
                proSubscriptionEndsAtUtc
              }
            }
            """;

        // Monetization integrity matters here: duplicate network submits or two tabs racing
        // must converge on a single durable grant so premium time and company cash never double-credit.
        var claimTasks = Enumerable.Range(0, 8)
            .Select(_ => ExecuteGraphQlAsync(claimMutation, new { input = new { companyId } }, token))
            .ToArray();

        var claimResults = await Task.WhenAll(claimTasks);

        var claimPayloads = claimResults
            .Select(result => result.GetProperty("data").GetProperty("claimStartupPack"))
            .ToList();

        Assert.All(claimPayloads, payload =>
        {
            Assert.Equal("CLAIMED", payload.GetProperty("offer").GetProperty("status").GetString());
            Assert.Equal(companyId, payload.GetProperty("offer").GetProperty("grantedCompanyId").GetString());
            Assert.Equal(500_000m + StartupPackService.CompanyCashGrant, payload.GetProperty("company").GetProperty("cash").GetDecimal());
        });

        var distinctProEndTimes = claimPayloads
            .Select(payload => payload.GetProperty("proSubscriptionEndsAtUtc").GetString())
            .Distinct()
            .ToList();
        Assert.Single(distinctProEndTimes);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var player = await db.Players.SingleAsync(candidate => candidate.Email == "startup-pack-concurrent@test.com");
        var offer = await db.StartupPackOffers.SingleAsync(candidate => candidate.PlayerId == player.Id);
        var company = await db.Companies.SingleAsync(candidate => candidate.Id == Guid.Parse(companyId));

        Assert.Equal(StartupPackOfferStatus.Claimed, offer.Status);
        Assert.NotNull(offer.ClaimedAtUtc);
        Assert.Equal(Guid.Parse(companyId), offer.GrantedCompanyId);
        Assert.Equal(500_000m + StartupPackService.CompanyCashGrant, company.Cash);
        Assert.NotNull(player.ProSubscriptionEndsAtUtc);
        var returnedProEndsAtUtc = DateTime.Parse(distinctProEndTimes[0]!).ToUniversalTime();
        Assert.True(
            Math.Abs((player.ProSubscriptionEndsAtUtc!.Value.ToUniversalTime() - returnedProEndsAtUtc).TotalMilliseconds) < 5,
            "Concurrent claims should converge to the same durable Pro end timestamp within sub-millisecond serialization precision.");
    }

    [Fact]
    public async Task StartupPackOffer_ExpiresAndRejectsClaims()
    {
        var token = await RegisterAndGetTokenAsync("startup-pack-expired@test.com", "ExpiredPlayer");
        var (companyId, _, _) = await CompleteOnboardingAsync(token, "Expired Corp");

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var player = await db.Players.SingleAsync(candidate => candidate.Email == "startup-pack-expired@test.com");
            var offer = await db.StartupPackOffers.SingleAsync(candidate => candidate.PlayerId == player.Id);
            offer.ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-5);
            await db.SaveChangesAsync();
        }

        var offerResult = await ExecuteGraphQlAsync(
            "{ startupPackOffer { status } }",
            token: token);
        Assert.Equal("EXPIRED", offerResult.GetProperty("data").GetProperty("startupPackOffer").GetProperty("status").GetString());

        var claimResult = await ExecuteGraphQlAsync(
            """
            mutation ClaimStartupPack($input: ClaimStartupPackInput!) {
              claimStartupPack(input: $input) {
                offer { status }
              }
            }
            """,
            new { input = new { companyId } },
            token);

        Assert.True(claimResult.TryGetProperty("errors", out var errors));
        Assert.Contains(errors.EnumerateArray(), error => error.GetProperty("extensions").GetProperty("code").GetString() == "STARTUP_PACK_EXPIRED");
    }

    [Fact]
    public async Task FinishOnboarding_ReturnsEligibleStartupPackOffer()
    {
        var token = await RegisterAndGetTokenAsync($"finish-onboard-offer-{Guid.NewGuid()}@test.com", "FinishOfferPlayer");
        var (_, _, cityId, _) = await StartOnboardingCompanyAsync(token, "Finish Offer Co");
        var productId = await GetStarterProductIdAsync();
        var shopLotId = await CreateTestLotAsync(cityId, "SALES_SHOP,COMMERCIAL", "Commercial District", 95_000m, "Finish Offer Shop");

        var result = await ExecuteGraphQlAsync(
            """
            mutation FinishOnboarding($input: FinishOnboardingInput!) {
              finishOnboarding(input: $input) {
                company { id }
                startupPackOffer {
                  status
                  companyCashGrant
                  proDurationDays
                  expiresAtUtc
                }
              }
            }
            """,
            new { input = new { productTypeId = productId, shopLotId } },
            token);

        var offer = result.GetProperty("data").GetProperty("finishOnboarding").GetProperty("startupPackOffer");
        Assert.Equal("ELIGIBLE", offer.GetProperty("status").GetString());
        Assert.Equal(250_000m, offer.GetProperty("companyCashGrant").GetDecimal());
        Assert.Equal(90, offer.GetProperty("proDurationDays").GetInt32());
        Assert.True(DateTime.TryParse(offer.GetProperty("expiresAtUtc").GetString(), out _));
    }

    [Fact]
    public async Task StartupPackOffer_MarkShown_IsIdempotent()
    {
        var token = await RegisterAndGetTokenAsync("startup-pack-shown-idempotent@test.com", "IdempotentShown");
        await CompleteOnboardingAsync(token, "Idempotent Shown Corp");

        var first = await ExecuteGraphQlAsync(
            "mutation { markStartupPackOfferShown { status shownAtUtc } }",
            token: token);
        var firstOffer = first.GetProperty("data").GetProperty("markStartupPackOfferShown");
        Assert.Equal("SHOWN", firstOffer.GetProperty("status").GetString());
        var firstShownAt = firstOffer.GetProperty("shownAtUtc").GetString();

        var second = await ExecuteGraphQlAsync(
            "mutation { markStartupPackOfferShown { status shownAtUtc } }",
            token: token);
        var secondOffer = second.GetProperty("data").GetProperty("markStartupPackOfferShown");
        Assert.Equal("SHOWN", secondOffer.GetProperty("status").GetString());
        // shownAtUtc must not be overwritten on subsequent calls
        Assert.Equal(firstShownAt, secondOffer.GetProperty("shownAtUtc").GetString());
    }

    [Fact]
    public async Task ClaimStartupPack_GrantsProSubscriptionThatUnlocksProProducts()
    {
        var token = await RegisterAndGetTokenAsync($"startup-pro-unlock-{Guid.NewGuid()}@test.com", "ProUnlockPlayer");
        var (companyId, _, _) = await CompleteOnboardingAsync(token, "Pro Unlock Corp");

        // Before claiming: electronics products should be pro-locked
        var beforeResult = await ExecuteGraphQlAsync(
            """
            {
              productTypes(industry: "ELECTRONICS") {
                slug
                isProOnly
                isUnlockedForCurrentPlayer
              }
            }
            """,
            token: token);
        var electronicsBefore = beforeResult.GetProperty("data").GetProperty("productTypes")
            .EnumerateArray()
            .First(p => p.GetProperty("slug").GetString() == "electronic-components");
        Assert.True(electronicsBefore.GetProperty("isProOnly").GetBoolean());
        Assert.False(electronicsBefore.GetProperty("isUnlockedForCurrentPlayer").GetBoolean());

        // Claim the startup pack
        await ExecuteGraphQlAsync(
            """
            mutation ClaimStartupPack($input: ClaimStartupPackInput!) {
              claimStartupPack(input: $input) {
                offer { status }
                proSubscriptionEndsAtUtc
              }
            }
            """,
            new { input = new { companyId } },
            token);

        // After claiming: electronics products should now be unlocked
        var afterResult = await ExecuteGraphQlAsync(
            """
            {
              productTypes(industry: "ELECTRONICS") {
                slug
                isProOnly
                isUnlockedForCurrentPlayer
              }
            }
            """,
            token: token);
        var electronicsAfter = afterResult.GetProperty("data").GetProperty("productTypes")
            .EnumerateArray()
            .First(p => p.GetProperty("slug").GetString() == "electronic-components");
        Assert.True(electronicsAfter.GetProperty("isProOnly").GetBoolean());
        Assert.True(electronicsAfter.GetProperty("isUnlockedForCurrentPlayer").GetBoolean());
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

    #region BuildingLots

    [Fact]
    public async Task CityLots_ReturnsSeedLotsForBratislava()
    {
        var citiesResult = await ExecuteGraphQlAsync("{ cities { id name } }");
        var bratislava = citiesResult.GetProperty("data").GetProperty("cities").EnumerateArray()
            .First(c => c.GetProperty("name").GetString() == "Bratislava");
        var cityId = bratislava.GetProperty("id").GetString();

        var result = await ExecuteGraphQlAsync(
            """
            query CityLots($cityId: UUID!) {
              cityLots(cityId: $cityId) {
                id name description district latitude longitude price suitableTypes
                ownerCompanyId buildingId
              }
            }
            """,
            new { cityId });

        var lots = result.GetProperty("data").GetProperty("cityLots");
        Assert.True(lots.GetArrayLength() > 0, "Expected seeded lots for Bratislava");

        var firstLot = lots[0];
        Assert.False(string.IsNullOrEmpty(firstLot.GetProperty("name").GetString()));
        Assert.False(string.IsNullOrEmpty(firstLot.GetProperty("district").GetString()));
        Assert.True(firstLot.GetProperty("price").GetDecimal() > 0);
        Assert.False(string.IsNullOrEmpty(firstLot.GetProperty("suitableTypes").GetString()));
    }

    [Fact]
    public async Task CityLots_ReturnsValidCoordinatesForAllSeedLots()
    {
        // Seed lots must have real Bratislava coordinates so the map renders correctly.
        var citiesResult = await ExecuteGraphQlAsync("{ cities { id name } }");
        var bratislavaId = citiesResult.GetProperty("data").GetProperty("cities").EnumerateArray()
            .First(c => c.GetProperty("name").GetString() == "Bratislava")
            .GetProperty("id").GetString();

        var result = await ExecuteGraphQlAsync(
            """
            query CityLots($cityId: UUID!) {
              cityLots(cityId: $cityId) { id name latitude longitude }
            }
            """,
            new { cityId = bratislavaId });

        var lots = result.GetProperty("data").GetProperty("cityLots");
        Assert.True(lots.GetArrayLength() > 0);

        foreach (var lot in lots.EnumerateArray())
        {
            var lat = lot.GetProperty("latitude").GetDouble();
            var lon = lot.GetProperty("longitude").GetDouble();
            var name = lot.GetProperty("name").GetString();

            // Bratislava is roughly 47.9–48.3°N, 16.9–17.4°E
            Assert.True(lat is > 47.8 and < 48.4, $"Lot '{name}' latitude {lat} is outside expected Bratislava range");
            Assert.True(lon is > 16.8 and < 17.5, $"Lot '{name}' longitude {lon} is outside expected Bratislava range");
        }
    }

    [Fact]
    public async Task CityLots_IsAccessibleWithoutAuthentication()
    {
        // City lots should be publicly readable – no token required – so
        // unauthenticated visitors can browse the map before logging in.
        var citiesResult = await ExecuteGraphQlAsync("{ cities { id name } }");
        var bratislavaId = citiesResult.GetProperty("data").GetProperty("cities").EnumerateArray()
            .First(c => c.GetProperty("name").GetString() == "Bratislava")
            .GetProperty("id").GetString();

        // Execute without a token
        var result = await ExecuteGraphQlAsync(
            """
            query CityLots($cityId: UUID!) {
              cityLots(cityId: $cityId) { id name price }
            }
            """,
            new { cityId = bratislavaId });

        Assert.False(result.TryGetProperty("errors", out _), "cityLots should be accessible without authentication");
        var lots = result.GetProperty("data").GetProperty("cityLots");
        Assert.True(lots.GetArrayLength() > 0);
    }

    [Fact]
    public async Task PurchaseLot_BuildingInheritsLotCoordinates()
    {
        // The building placed on a lot must carry the lot's GPS coordinates
        // so the city map can render it at the correct position.
        var token = await RegisterAndGetTokenAsync($"lot-coords-{Guid.NewGuid()}@test.com");
        var (companyId, _, _) = await CompleteOnboardingAsync(token, "Coord Check Co");

        var citiesResult = await ExecuteGraphQlAsync("{ cities { id name } }");
        var bratislavaId = citiesResult.GetProperty("data").GetProperty("cities").EnumerateArray()
            .First(c => c.GetProperty("name").GetString() == "Bratislava")
            .GetProperty("id").GetString();

        // Get a factory lot with its coordinates
        var lotsResult = await ExecuteGraphQlAsync(
            """
            query CityLots($cityId: UUID!) {
              cityLots(cityId: $cityId) { id latitude longitude suitableTypes ownerCompanyId }
            }
            """,
            new { cityId = bratislavaId });

        var sourceLot = lotsResult.GetProperty("data").GetProperty("cityLots").EnumerateArray()
            .First(l => l.GetProperty("suitableTypes").GetString()!.Contains("FACTORY")
                     && l.GetProperty("ownerCompanyId").ValueKind == JsonValueKind.Null);

        var lotId = sourceLot.GetProperty("id").GetString();
        var lotLat = sourceLot.GetProperty("latitude").GetDouble();
        var lotLon = sourceLot.GetProperty("longitude").GetDouble();

        var result = await ExecuteGraphQlAsync(
            """
            mutation PurchaseLot($input: PurchaseLotInput!) {
              purchaseLot(input: $input) {
                building { id latitude longitude }
              }
            }
            """,
            new { input = new { companyId, lotId, buildingType = "FACTORY", buildingName = "Coord Factory" } },
            token);

        var building = result.GetProperty("data").GetProperty("purchaseLot").GetProperty("building");
        Assert.Equal(lotLat, building.GetProperty("latitude").GetDouble());
        Assert.Equal(lotLon, building.GetProperty("longitude").GetDouble());
    }

    [Fact]
    public async Task GetLot_ReturnsCoordinates()
    {
        // The lot(id) query must expose latitude/longitude for direct look-ups.
        var citiesResult = await ExecuteGraphQlAsync("{ cities { id name } }");
        var bratislavaId = citiesResult.GetProperty("data").GetProperty("cities").EnumerateArray()
            .First(c => c.GetProperty("name").GetString() == "Bratislava")
            .GetProperty("id").GetString();

        var lotsResult = await ExecuteGraphQlAsync(
            "query CityLots($cityId: UUID!) { cityLots(cityId: $cityId) { id } }",
            new { cityId = bratislavaId });
        var lotId = lotsResult.GetProperty("data").GetProperty("cityLots")[0].GetProperty("id").GetString();

        var result = await ExecuteGraphQlAsync(
            """
            query GetLot($id: UUID!) {
              lot(id: $id) { id latitude longitude district }
            }
            """,
            new { id = lotId });

        var lot = result.GetProperty("data").GetProperty("lot");
        Assert.NotEqual(0.0, lot.GetProperty("latitude").GetDouble());
        Assert.NotEqual(0.0, lot.GetProperty("longitude").GetDouble());
        Assert.False(string.IsNullOrEmpty(lot.GetProperty("district").GetString()));
    }

    [Fact]
    public async Task PurchaseLot_ValidInput_CreatesBuilding()
    {
        var token = await RegisterAndGetTokenAsync($"lot-buyer-{Guid.NewGuid()}@test.com");
        var (companyId, _, _) = await CompleteOnboardingAsync(token, "Lot Buyer Co");

        // Get a factory-suitable lot
        var citiesResult = await ExecuteGraphQlAsync("{ cities { id name } }");
        var bratislavaId = citiesResult.GetProperty("data").GetProperty("cities").EnumerateArray()
            .First(c => c.GetProperty("name").GetString() == "Bratislava")
            .GetProperty("id").GetString();

        var lotsResult = await ExecuteGraphQlAsync(
            """
            query CityLots($cityId: UUID!) {
              cityLots(cityId: $cityId) { id name suitableTypes price ownerCompanyId }
            }
            """,
            new { cityId = bratislavaId });

        var availableLot = lotsResult.GetProperty("data").GetProperty("cityLots").EnumerateArray()
            .First(l => l.GetProperty("suitableTypes").GetString()!.Contains("FACTORY")
                     && l.GetProperty("ownerCompanyId").ValueKind == JsonValueKind.Null);
        var lotId = availableLot.GetProperty("id").GetString();
        var lotPrice = availableLot.GetProperty("price").GetDecimal();

        var result = await ExecuteGraphQlAsync(
            """
            mutation PurchaseLot($input: PurchaseLotInput!) {
              purchaseLot(input: $input) {
                lot { id ownerCompanyId buildingId }
                building { id name type latitude longitude }
                company { id cash }
              }
            }
            """,
            new { input = new { companyId, lotId, buildingType = "FACTORY", buildingName = "My Lot Factory" } },
            token);

        var data = result.GetProperty("data").GetProperty("purchaseLot");
        var purchasedLot = data.GetProperty("lot");
        var building = data.GetProperty("building");
        var company = data.GetProperty("company");

        Assert.Equal(companyId, purchasedLot.GetProperty("ownerCompanyId").GetString());
        Assert.NotNull(purchasedLot.GetProperty("buildingId").GetString());
        Assert.Equal("My Lot Factory", building.GetProperty("name").GetString());
        Assert.Equal("FACTORY", building.GetProperty("type").GetString());
        // Company cash should be reduced by lot price
        Assert.True(company.GetProperty("cash").GetDecimal() < 500_000m);
    }

    [Fact]
    public async Task PurchaseLot_AlreadyOwned_Fails()
    {
        // First buyer
        var token1 = await RegisterAndGetTokenAsync($"lot-first-{Guid.NewGuid()}@test.com");
        var (companyId1, _, _) = await CompleteOnboardingAsync(token1, "First Co");

        var citiesResult = await ExecuteGraphQlAsync("{ cities { id name } }");
        var bratislavaId = citiesResult.GetProperty("data").GetProperty("cities").EnumerateArray()
            .First(c => c.GetProperty("name").GetString() == "Bratislava")
            .GetProperty("id").GetString();

        var lotsResult = await ExecuteGraphQlAsync(
            """
            query CityLots($cityId: UUID!) {
              cityLots(cityId: $cityId) { id suitableTypes ownerCompanyId }
            }
            """,
            new { cityId = bratislavaId });

        var lot = lotsResult.GetProperty("data").GetProperty("cityLots").EnumerateArray()
            .First(l => l.GetProperty("suitableTypes").GetString()!.Contains("SALES_SHOP")
                     && l.GetProperty("ownerCompanyId").ValueKind == JsonValueKind.Null);
        var lotId = lot.GetProperty("id").GetString();

        // First purchase succeeds
        await ExecuteGraphQlAsync(
            """
            mutation PurchaseLot($input: PurchaseLotInput!) {
              purchaseLot(input: $input) { lot { id } }
            }
            """,
            new { input = new { companyId = companyId1, lotId, buildingType = "SALES_SHOP", buildingName = "Shop 1" } },
            token1);

        // Second buyer tries to purchase the same lot
        var token2 = await RegisterAndGetTokenAsync($"lot-second-{Guid.NewGuid()}@test.com");
        var (companyId2, _, _) = await CompleteOnboardingAsync(token2, "Second Co");

        var result = await ExecuteGraphQlAsync(
            """
            mutation PurchaseLot($input: PurchaseLotInput!) {
              purchaseLot(input: $input) { lot { id } }
            }
            """,
            new { input = new { companyId = companyId2, lotId, buildingType = "SALES_SHOP", buildingName = "Shop 2" } },
            token2);

        Assert.True(result.TryGetProperty("errors", out var errors));
        Assert.Contains("already been purchased", errors[0].GetProperty("message").GetString());
    }

    [Fact]
    public async Task PurchaseLot_UnsuitableBuildingType_Fails()
    {
        var token = await RegisterAndGetTokenAsync($"lot-unsuit-{Guid.NewGuid()}@test.com");
        var (companyId, _, _) = await CompleteOnboardingAsync(token, "Wrong Type Co");

        var citiesResult = await ExecuteGraphQlAsync("{ cities { id name } }");
        var bratislavaId = citiesResult.GetProperty("data").GetProperty("cities").EnumerateArray()
            .First(c => c.GetProperty("name").GetString() == "Bratislava")
            .GetProperty("id").GetString();

        var lotsResult = await ExecuteGraphQlAsync(
            """
            query CityLots($cityId: UUID!) {
              cityLots(cityId: $cityId) { id suitableTypes ownerCompanyId }
            }
            """,
            new { cityId = bratislavaId });

        // Pick a lot that is only suitable for APARTMENT
        var lot = lotsResult.GetProperty("data").GetProperty("cityLots").EnumerateArray()
            .First(l => l.GetProperty("suitableTypes").GetString() == "APARTMENT"
                     && l.GetProperty("ownerCompanyId").ValueKind == JsonValueKind.Null);
        var lotId = lot.GetProperty("id").GetString();

        var result = await ExecuteGraphQlAsync(
            """
            mutation PurchaseLot($input: PurchaseLotInput!) {
              purchaseLot(input: $input) { lot { id } }
            }
            """,
            new { input = new { companyId, lotId, buildingType = "FACTORY", buildingName = "Wrong Factory" } },
            token);

        Assert.True(result.TryGetProperty("errors", out var errors));
        Assert.Contains("not suitable", errors[0].GetProperty("message").GetString());
    }

    [Fact]
    public async Task PurchaseLot_InsufficientFunds_Fails()
    {
        var token = await RegisterAndGetTokenAsync($"lot-broke-{Guid.NewGuid()}@test.com");
        var (companyId, _, _) = await CompleteOnboardingAsync(token, "Broke Co");

        // Drain the company's cash
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var company = await db.Companies.FirstAsync(c => c.Id == Guid.Parse(companyId));
            company.Cash = 0;
            await db.SaveChangesAsync();
        }

        var citiesResult = await ExecuteGraphQlAsync("{ cities { id name } }");
        var bratislavaId = citiesResult.GetProperty("data").GetProperty("cities").EnumerateArray()
            .First(c => c.GetProperty("name").GetString() == "Bratislava")
            .GetProperty("id").GetString();

        var lotsResult = await ExecuteGraphQlAsync(
            """
            query CityLots($cityId: UUID!) {
              cityLots(cityId: $cityId) { id suitableTypes price ownerCompanyId }
            }
            """,
            new { cityId = bratislavaId });

        var lot = lotsResult.GetProperty("data").GetProperty("cityLots").EnumerateArray()
            .First(l => l.GetProperty("suitableTypes").GetString()!.Contains("FACTORY")
                     && l.GetProperty("ownerCompanyId").ValueKind == JsonValueKind.Null);
        var lotId = lot.GetProperty("id").GetString();

        var result = await ExecuteGraphQlAsync(
            """
            mutation PurchaseLot($input: PurchaseLotInput!) {
              purchaseLot(input: $input) { lot { id } }
            }
            """,
            new { input = new { companyId, lotId, buildingType = "FACTORY", buildingName = "No Money Factory" } },
            token);

        Assert.True(result.TryGetProperty("errors", out var errors));
        Assert.Contains("Insufficient funds", errors[0].GetProperty("message").GetString());
    }

    [Fact]
    public async Task GetLot_ReturnsSingleLot()
    {
        var citiesResult = await ExecuteGraphQlAsync("{ cities { id name } }");
        var bratislavaId = citiesResult.GetProperty("data").GetProperty("cities").EnumerateArray()
            .First(c => c.GetProperty("name").GetString() == "Bratislava")
            .GetProperty("id").GetString();

        var lotsResult = await ExecuteGraphQlAsync(
            """
            query CityLots($cityId: UUID!) {
              cityLots(cityId: $cityId) { id }
            }
            """,
            new { cityId = bratislavaId });

        var lotId = lotsResult.GetProperty("data").GetProperty("cityLots")[0].GetProperty("id").GetString();

        var result = await ExecuteGraphQlAsync(
            """
            query GetLot($id: UUID!) {
              lot(id: $id) { id name district price suitableTypes }
            }
            """,
            new { id = lotId });

        var lot = result.GetProperty("data").GetProperty("lot");
        Assert.False(string.IsNullOrEmpty(lot.GetProperty("name").GetString()));
        Assert.False(string.IsNullOrEmpty(lot.GetProperty("district").GetString()));
    }

    [Fact]
    public async Task PurchaseLot_ConcurrentBuyers_OnlyOneSucceeds()
    {
        // Register two independent buyers, each with their own company
        var token1 = await RegisterAndGetTokenAsync($"lot-race-a-{Guid.NewGuid()}@test.com");
        var (companyId1, _, _) = await CompleteOnboardingAsync(token1, "Race A Co");

        var token2 = await RegisterAndGetTokenAsync($"lot-race-b-{Guid.NewGuid()}@test.com");
        var (companyId2, _, _) = await CompleteOnboardingAsync(token2, "Race B Co");

        // Find an available factory lot
        var citiesResult = await ExecuteGraphQlAsync("{ cities { id name } }");
        var bratislavaId = citiesResult.GetProperty("data").GetProperty("cities").EnumerateArray()
            .First(c => c.GetProperty("name").GetString() == "Bratislava")
            .GetProperty("id").GetString();

        var lotsResult = await ExecuteGraphQlAsync(
            """
            query CityLots($cityId: UUID!) {
              cityLots(cityId: $cityId) { id suitableTypes ownerCompanyId }
            }
            """,
            new { cityId = bratislavaId });

        var targetLot = lotsResult.GetProperty("data").GetProperty("cityLots").EnumerateArray()
            .First(l => l.GetProperty("suitableTypes").GetString()!.Contains("FACTORY")
                     && l.GetProperty("ownerCompanyId").ValueKind == JsonValueKind.Null);
        var lotId = targetLot.GetProperty("id").GetString();

        const string mutation = """
            mutation PurchaseLot($input: PurchaseLotInput!) {
              purchaseLot(input: $input) {
                lot { id ownerCompanyId }
                building { id }
                company { id cash }
              }
            }
            """;

        // Fire both purchases concurrently against the same lot
        var task1 = ExecuteGraphQlAsync(mutation,
            new { input = new { companyId = companyId1, lotId, buildingType = "FACTORY", buildingName = "Race A Factory" } },
            token1);
        var task2 = ExecuteGraphQlAsync(mutation,
            new { input = new { companyId = companyId2, lotId, buildingType = "FACTORY", buildingName = "Race B Factory" } },
            token2);

        var results = await Task.WhenAll(task1, task2);

        // Exactly one should succeed, the other should fail with LOT_ALREADY_OWNED
        var successes = results.Count(r => r.TryGetProperty("data", out var d)
            && d.ValueKind == JsonValueKind.Object
            && d.TryGetProperty("purchaseLot", out var pl)
            && pl.ValueKind == JsonValueKind.Object);
        var failures = results.Count(r => r.TryGetProperty("errors", out _));

        Assert.Equal(1, successes);
        Assert.Equal(1, failures);

        var failedResult = results.First(r => r.TryGetProperty("errors", out _));
        Assert.Contains("already been purchased", failedResult.GetProperty("errors")[0].GetProperty("message").GetString());

        // Verify only one building was created for this lot
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var purchasedLot = await db.BuildingLots.FirstAsync(l => l.Id == Guid.Parse(lotId!));
        Assert.NotNull(purchasedLot.OwnerCompanyId);
        Assert.NotNull(purchasedLot.BuildingId);

        var buildingsOnLot = await db.Buildings.CountAsync(b => b.Id == purchasedLot.BuildingId);
        Assert.Equal(1, buildingsOnLot);

        // Verify only the winning company was charged
        var company1 = await db.Companies.FirstAsync(c => c.Id == Guid.Parse(companyId1));
        var company2 = await db.Companies.FirstAsync(c => c.Id == Guid.Parse(companyId2));
        var chargedCount = (company1.Cash < 500_000m ? 1 : 0) + (company2.Cash < 500_000m ? 1 : 0);
        Assert.Equal(1, chargedCount);
    }

    #endregion

    #region First-sale milestone

    [Fact]
    public async Task CompleteFirstSaleMilestone_WithoutOnboarding_ReturnsShopNotFound()
    {
        // A player who has not completed onboarding has no OnboardingShopBuildingId
        var token = await RegisterAndGetTokenAsync($"first-sale-noboard-{Guid.NewGuid()}@test.com", "No Onboard");

        var result = await ExecuteGraphQlAsync(
            """
            mutation {
              completeFirstSaleMilestone {
                onboardingFirstSaleCompletedAtUtc
              }
            }
            """,
            token: token);

        Assert.True(result.TryGetProperty("errors", out var errors));
        Assert.Contains("SHOP_NOT_FOUND", errors[0].GetProperty("extensions").GetProperty("code").GetString());
    }

    [Fact]
    public async Task CompleteFirstSaleMilestone_AfterFinishOnboarding_SetsTimestampAndClearsShopId()
    {
        // First, complete full onboarding so OnboardingShopBuildingId is set
        var token = await RegisterAndGetTokenAsync($"first-sale-full-{Guid.NewGuid()}@test.com", "Full Seller");
        var (_, _, cityId, _) = await StartOnboardingCompanyAsync(token, "Full Seller Co");
        var productId = await GetStarterProductIdAsync();
        var shopLotId = await CreateTestLotAsync(cityId, "SALES_SHOP,COMMERCIAL", "Commercial District", 90_000m, "Full Seller Shop Lot");

        var finishResult = await FinishOnboardingAsync(token, productId, shopLotId);
        var shopBuildingId = finishResult.GetProperty("data").GetProperty("finishOnboarding").GetProperty("salesShop").GetProperty("id").GetString();

        // Verify onboardingShopBuildingId is set on player
        var meBeforeResult = await ExecuteGraphQlAsync(
            """
            {
              me {
                onboardingShopBuildingId
                onboardingFirstSaleCompletedAtUtc
              }
            }
            """,
            token: token);

        var meBefore = meBeforeResult.GetProperty("data").GetProperty("me");
        Assert.Equal(shopBuildingId, meBefore.GetProperty("onboardingShopBuildingId").GetString());
        Assert.Equal(JsonValueKind.Null, meBefore.GetProperty("onboardingFirstSaleCompletedAtUtc").ValueKind);

        // Call the milestone mutation
        var milestoneResult = await ExecuteGraphQlAsync(
            """
            mutation {
              completeFirstSaleMilestone {
                onboardingFirstSaleCompletedAtUtc
                onboardingShopBuildingId
              }
            }
            """,
            token: token);

        var milestone = milestoneResult.GetProperty("data").GetProperty("completeFirstSaleMilestone");
        Assert.Equal(JsonValueKind.String, milestone.GetProperty("onboardingFirstSaleCompletedAtUtc").ValueKind);
        // onboardingShopBuildingId should be cleared after milestone
        Assert.Equal(JsonValueKind.Null, milestone.GetProperty("onboardingShopBuildingId").ValueKind);

        // Verify via me query
        var meAfterResult = await ExecuteGraphQlAsync(
            """
            {
              me {
                onboardingShopBuildingId
                onboardingFirstSaleCompletedAtUtc
              }
            }
            """,
            token: token);

        var meAfter = meAfterResult.GetProperty("data").GetProperty("me");
        Assert.Equal(JsonValueKind.String, meAfter.GetProperty("onboardingFirstSaleCompletedAtUtc").ValueKind);
        Assert.Equal(JsonValueKind.Null, meAfter.GetProperty("onboardingShopBuildingId").ValueKind);
    }

    [Fact]
    public async Task CompleteFirstSaleMilestone_IsIdempotent_SecondCallPreservesOriginalTimestamp()
    {
        var token = await RegisterAndGetTokenAsync($"first-sale-idempotent-{Guid.NewGuid()}@test.com", "Idempotent Seller");
        var (_, _, cityId, _) = await StartOnboardingCompanyAsync(token, "Idempotent Seller Co");
        var productId = await GetStarterProductIdAsync();
        var shopLotId = await CreateTestLotAsync(cityId, "SALES_SHOP,COMMERCIAL", "Commercial District", 90_000m, "Idempotent Shop Lot");
        await FinishOnboardingAsync(token, productId, shopLotId);

        var firstResult = await ExecuteGraphQlAsync(
            """
            mutation {
              completeFirstSaleMilestone {
                onboardingFirstSaleCompletedAtUtc
              }
            }
            """,
            token: token);

        var firstTimestamp = firstResult
            .GetProperty("data")
            .GetProperty("completeFirstSaleMilestone")
            .GetProperty("onboardingFirstSaleCompletedAtUtc")
            .GetString();

        // Wait a tiny bit to ensure any re-write would produce a different timestamp
        await Task.Delay(10);

        var secondResult = await ExecuteGraphQlAsync(
            """
            mutation {
              completeFirstSaleMilestone {
                onboardingFirstSaleCompletedAtUtc
              }
            }
            """,
            token: token);

        var secondTimestamp = secondResult
            .GetProperty("data")
            .GetProperty("completeFirstSaleMilestone")
            .GetProperty("onboardingFirstSaleCompletedAtUtc")
            .GetString();

        // The timestamp must not change on subsequent calls
        Assert.Equal(firstTimestamp, secondTimestamp);
    }

    [Fact]
    public async Task FinishOnboarding_SetsOnboardingShopBuildingId()
    {
        var token = await RegisterAndGetTokenAsync($"shop-building-id-{Guid.NewGuid()}@test.com", "Shop Id Player");
        var (_, _, cityId, _) = await StartOnboardingCompanyAsync(token, "Shop Id Co");
        var productId = await GetStarterProductIdAsync();
        var shopLotId = await CreateTestLotAsync(cityId, "SALES_SHOP,COMMERCIAL", "Commercial District", 90_000m, "Shop Id Lot");

        var finishResult = await FinishOnboardingAsync(token, productId, shopLotId);
        var shopBuildingId = finishResult.GetProperty("data").GetProperty("finishOnboarding").GetProperty("salesShop").GetProperty("id").GetString();

        var meResult = await ExecuteGraphQlAsync(
            """
            {
              me {
                onboardingShopBuildingId
                onboardingCompletedAtUtc
              }
            }
            """,
            token: token);

        var me = meResult.GetProperty("data").GetProperty("me");
        Assert.Equal(JsonValueKind.String, me.GetProperty("onboardingCompletedAtUtc").ValueKind);
        Assert.Equal(shopBuildingId, me.GetProperty("onboardingShopBuildingId").GetString());
    }

    [Fact]
    public async Task CompleteFirstSaleMilestone_ExposedInMeQuery()
    {
        var token = await RegisterAndGetTokenAsync($"first-sale-me-{Guid.NewGuid()}@test.com", "Me Seller");
        var (_, _, cityId, _) = await StartOnboardingCompanyAsync(token, "Me Seller Co");
        var productId = await GetStarterProductIdAsync();
        var shopLotId = await CreateTestLotAsync(cityId, "SALES_SHOP,COMMERCIAL", "Commercial District", 90_000m, "Me Seller Shop Lot");
        await FinishOnboardingAsync(token, productId, shopLotId);

        // Before milestone — field should be null
        var beforeResult = await ExecuteGraphQlAsync(
            """
            {
              me {
                onboardingFirstSaleCompletedAtUtc
              }
            }
            """,
            token: token);

        Assert.Equal(
            JsonValueKind.Null,
            beforeResult.GetProperty("data").GetProperty("me").GetProperty("onboardingFirstSaleCompletedAtUtc").ValueKind);

        // Complete the milestone
        await ExecuteGraphQlAsync(
            """
            mutation {
              completeFirstSaleMilestone {
                onboardingFirstSaleCompletedAtUtc
              }
            }
            """,
            token: token);

        // After milestone — field should be a string (ISO timestamp)
        var afterResult = await ExecuteGraphQlAsync(
            """
            {
              me {
                onboardingFirstSaleCompletedAtUtc
              }
            }
            """,
            token: token);

        Assert.Equal(
            JsonValueKind.String,
            afterResult.GetProperty("data").GetProperty("me").GetProperty("onboardingFirstSaleCompletedAtUtc").ValueKind);
    }

    [Fact]
    public async Task CompleteFirstSaleMilestone_Unauthenticated_ReturnsError()
    {
        var result = await ExecuteGraphQlAsync(
            """
            mutation {
              completeFirstSaleMilestone {
                onboardingFirstSaleCompletedAtUtc
              }
            }
            """);

        Assert.True(result.TryGetProperty("errors", out var errors));
        Assert.NotEmpty(errors.EnumerateArray().ToList());
    }

    #endregion
}

/// <summary>
/// Integration tests for the tick visibility and scheduled action surfaces.
/// These cover gameState timing data and the myPendingActions query.
/// </summary>
public sealed class TickAndScheduledActionsTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly ApiWebApplicationFactory _factory;

    public TickAndScheduledActionsTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private async Task<JsonElement> ExecuteGraphQlAsync(string query, object? variables = null, string? token = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/graphql");
        request.Content = new System.Net.Http.StringContent(
            System.Text.Json.JsonSerializer.Serialize(new { query, variables }),
            System.Text.Encoding.UTF8,
            "application/json");

        if (token is not null)
        {
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        }

        var response = await _client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        return System.Text.Json.JsonSerializer.Deserialize<JsonElement>(body);
    }

    private async Task<string> RegisterAndGetTokenAsync(string email, string displayName = "Tester", string password = "TestPass123!")
    {
        var result = await ExecuteGraphQlAsync(
            """
            mutation Register($input: RegisterInput!) {
              register(input: $input) { token }
            }
            """,
            new { input = new { email, displayName, password } });
        return result.GetProperty("data").GetProperty("register").GetProperty("token").GetString()!;
    }

    private async Task ResetGameStateAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var gameState = await db.GameStates.FindAsync(1);
        if (gameState is not null)
        {
            gameState.CurrentTick = 0;
            gameState.LastTickAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }
    }

    #region GameState tick data

    [Fact]
    public async Task GameState_ReturnsLastTickAtUtc()
    {
        await ResetGameStateAsync();
        var result = await ExecuteGraphQlAsync(
            "{ gameState { currentTick tickIntervalSeconds lastTickAtUtc taxRate } }");

        var state = result.GetProperty("data").GetProperty("gameState");
        Assert.True(DateTime.TryParse(state.GetProperty("lastTickAtUtc").GetString(), out _),
            "lastTickAtUtc should be a parseable timestamp");
        Assert.Equal(0, state.GetProperty("currentTick").GetInt64());
        Assert.True(state.GetProperty("tickIntervalSeconds").GetInt32() > 0,
            "tickIntervalSeconds must be positive");
    }

    [Fact]
    public async Task GameState_LastTickAtUtc_IsRecentAfterReset()
    {
        await ResetGameStateAsync();
        var before = DateTime.UtcNow.AddSeconds(-2);
        var result = await ExecuteGraphQlAsync(
            "{ gameState { lastTickAtUtc } }");

        var lastTickAtUtc = DateTime.Parse(
            result.GetProperty("data").GetProperty("gameState").GetProperty("lastTickAtUtc").GetString()!,
            null, System.Globalization.DateTimeStyles.RoundtripKind);

        Assert.True(lastTickAtUtc >= before, "lastTickAtUtc should be close to the current time after a reset");
    }

    #endregion

    #region myPendingActions

    [Fact]
    public async Task MyPendingActions_Unauthenticated_ReturnsError()
    {
        var result = await ExecuteGraphQlAsync(
            "{ myPendingActions { id actionType buildingId ticksRemaining } }");
        Assert.True(result.TryGetProperty("errors", out _));
    }

    [Fact]
    public async Task MyPendingActions_NoPlans_ReturnsEmpty()
    {
        var token = await RegisterAndGetTokenAsync($"pending-empty-{Guid.NewGuid()}@test.com", "PendingEmpty");
        var result = await ExecuteGraphQlAsync(
            "{ myPendingActions { id actionType buildingId ticksRemaining totalTicksRequired } }",
            token: token);

        var actions = result.GetProperty("data").GetProperty("myPendingActions");
        Assert.Equal(0, actions.GetArrayLength());
    }

    [Fact]
    public async Task MyPendingActions_AfterOnboarding_NoPendingPlans()
    {
        var token = await RegisterAndGetTokenAsync($"pending-onboarded-{Guid.NewGuid()}@test.com", "PendingOnboarded");

        // Complete onboarding using the staged flow
        await ResetGameStateAsync();
        var citiesResult = await ExecuteGraphQlAsync("{ cities { id } }");
        var cityId = citiesResult.GetProperty("data").GetProperty("cities")[0].GetProperty("id").GetString()!;

        var factoryLotId = await CreateTestLotForTickTestAsync(cityId, "FACTORY,MINE", "Industrial Zone");

        await ExecuteGraphQlAsync(
            """
            mutation StartOnboardingCompany($input: StartOnboardingCompanyInput!) {
              startOnboardingCompany(input: $input) { company { id } }
            }
            """,
            new { input = new { industry = "FURNITURE", cityId, companyName = "Pending Corp", factoryLotId } },
            token);

        var productsResult = await ExecuteGraphQlAsync("query { productTypes(industry: \"FURNITURE\") { id slug } }");
        var productId = productsResult.GetProperty("data").GetProperty("productTypes")
            .EnumerateArray()
            .Single(p => p.GetProperty("slug").GetString() == "wooden-chair")
            .GetProperty("id")
            .GetString()!;

        var shopLotId = await CreateTestLotForTickTestAsync(cityId, "SALES_SHOP,COMMERCIAL", "Commercial District", 100_000m);
        await ExecuteGraphQlAsync(
            """
            mutation FinishOnboarding($input: FinishOnboardingInput!) {
              finishOnboarding(input: $input) { company { id } }
            }
            """,
            new { input = new { productTypeId = productId, shopLotId } },
            token);

        var result = await ExecuteGraphQlAsync(
            "{ myPendingActions { id actionType buildingId ticksRemaining } }",
            token: token);

        // No configuration plans should exist after a fresh onboarding
        Assert.Equal(0, result.GetProperty("data").GetProperty("myPendingActions").GetArrayLength());
    }

    [Fact]
    public async Task MyPendingActions_ReturnsPendingBuildingUpgrade()
    {
        var token = await RegisterAndGetTokenAsync($"pending-upgrade-{Guid.NewGuid()}@test.com", "PendingUpgrade");

        await ResetGameStateAsync();
        var citiesResult = await ExecuteGraphQlAsync("{ cities { id } }");
        var cityId = citiesResult.GetProperty("data").GetProperty("cities")[0].GetProperty("id").GetString()!;

        var factoryLotId = await CreateTestLotForTickTestAsync(cityId, "FACTORY,MINE", "Industrial Zone");

        var startResult = await ExecuteGraphQlAsync(
            """
            mutation StartOnboardingCompany($input: StartOnboardingCompanyInput!) {
              startOnboardingCompany(input: $input) {
                company { id }
                factory { id }
              }
            }
            """,
            new { input = new { industry = "FURNITURE", cityId, companyName = "Upgrade Corp", factoryLotId } },
            token);

        var factoryId = startResult.GetProperty("data").GetProperty("startOnboardingCompany")
            .GetProperty("factory").GetProperty("id").GetString()!;

        // Queue a building configuration upgrade on the factory
        var resourcesResult = await ExecuteGraphQlAsync("{ resourceTypes { id slug } }");
        var woodId = resourcesResult.GetProperty("data").GetProperty("resourceTypes")
            .EnumerateArray()
            .First(r => r.GetProperty("slug").GetString() == "wood")
            .GetProperty("id")
            .GetString()!;

        var storageUnit = new
        {
            unitType = "STORAGE",
            gridX = 0,
            gridY = 0,
            linkUp = false, linkDown = false, linkLeft = false, linkRight = false,
            linkUpLeft = false, linkUpRight = false, linkDownLeft = false, linkDownRight = false,
            resourceTypeId = woodId,
        };

        await ExecuteGraphQlAsync(
            """
            mutation StoreBuildingConfiguration($input: StoreBuildingConfigurationInput!) {
              storeBuildingConfiguration(input: $input) { id appliesAtTick totalTicksRequired }
            }
            """,
            new { input = new { buildingId = factoryId, units = new[] { storageUnit } } },
            token);

        var result = await ExecuteGraphQlAsync(
            """
            {
              myPendingActions {
                id
                actionType
                buildingId
                buildingName
                buildingType
                submittedAtUtc
                submittedAtTick
                appliesAtTick
                ticksRemaining
                totalTicksRequired
              }
            }
            """,
            token: token);

        var actions = result.GetProperty("data").GetProperty("myPendingActions");
        Assert.Equal(1, actions.GetArrayLength());

        var action = actions[0];
        Assert.Equal("BUILDING_UPGRADE", action.GetProperty("actionType").GetString());
        Assert.Equal(factoryId, action.GetProperty("buildingId").GetString());
        Assert.True(action.GetProperty("ticksRemaining").GetInt64() > 0,
            "ticksRemaining must be positive for a newly queued upgrade");
        Assert.True(action.GetProperty("totalTicksRequired").GetInt32() > 0,
            "totalTicksRequired must reflect the upgrade cost");
        Assert.True(DateTime.TryParse(action.GetProperty("submittedAtUtc").GetString(), out _));
    }

    [Fact]
    public async Task MyPendingActions_OrderedByAppliesAtTickAscending()
    {
        var token = await RegisterAndGetTokenAsync($"pending-order-{Guid.NewGuid()}@test.com", "PendingOrder");

        await ResetGameStateAsync();
        var citiesResult = await ExecuteGraphQlAsync("{ cities { id } }");
        var cityId = citiesResult.GetProperty("data").GetProperty("cities")[0].GetProperty("id").GetString()!;

        var resourcesResult = await ExecuteGraphQlAsync("{ resourceTypes { id slug } }");
        var woodId = resourcesResult.GetProperty("data").GetProperty("resourceTypes")
            .EnumerateArray()
            .First(r => r.GetProperty("slug").GetString() == "wood")
            .GetProperty("id")
            .GetString()!;

        // Create two buildings with pending upgrades
        var lotId1 = await CreateTestLotForTickTestAsync(cityId, "FACTORY,MINE", "Industrial Zone", 50_000m);
        var lotId2 = await CreateTestLotForTickTestAsync(cityId, "FACTORY,MINE", "Industrial Zone", 50_000m);

        var companyResult = await ExecuteGraphQlAsync(
            "mutation CreateCompany($input: CreateCompanyInput!) { createCompany(input: $input) { id } }",
            new { input = new { name = "Order Corp" } },
            token);
        var companyId = companyResult.GetProperty("data").GetProperty("createCompany").GetProperty("id").GetString()!;

        var b1 = await PurchaseLotAndGetBuildingIdAsync(token, companyId, lotId1, "FACTORY", "Order Factory 1");
        var b2 = await PurchaseLotAndGetBuildingIdAsync(token, companyId, lotId2, "FACTORY", "Order Factory 2");

        var storageUnit = new
        {
            unitType = "STORAGE",
            gridX = 0, gridY = 0,
            linkUp = false, linkDown = false, linkLeft = false, linkRight = false,
            linkUpLeft = false, linkUpRight = false, linkDownLeft = false, linkDownRight = false,
            resourceTypeId = woodId,
        };

        await ExecuteGraphQlAsync(
            "mutation StoreBuildingConfiguration($input: StoreBuildingConfigurationInput!) { storeBuildingConfiguration(input: $input) { id } }",
            new { input = new { buildingId = b1, units = new[] { storageUnit } } },
            token);
        await ExecuteGraphQlAsync(
            "mutation StoreBuildingConfiguration($input: StoreBuildingConfigurationInput!) { storeBuildingConfiguration(input: $input) { id } }",
            new { input = new { buildingId = b2, units = new[] { storageUnit } } },
            token);

        var result = await ExecuteGraphQlAsync(
            "{ myPendingActions { appliesAtTick } }",
            token: token);

        var appliesAtTicks = result.GetProperty("data").GetProperty("myPendingActions")
            .EnumerateArray()
            .Select(a => a.GetProperty("appliesAtTick").GetInt64())
            .ToList();

        for (int i = 1; i < appliesAtTicks.Count; i++)
        {
            Assert.True(appliesAtTicks[i] >= appliesAtTicks[i - 1],
                "myPendingActions should be ordered by appliesAtTick ascending");
        }
    }

    private async Task<string> CreateTestLotForTickTestAsync(
        string cityId,
        string suitableTypes,
        string district,
        decimal price = 75_000m)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var city = await db.Cities.FirstAsync(candidate => candidate.Id == Guid.Parse(cityId));
        var lot = new BuildingLot
        {
            Id = Guid.NewGuid(),
            CityId = city.Id,
            Name = $"Tick Test Lot {Guid.NewGuid():N}"[..22],
            Description = "Tick test lot.",
            District = district,
            Latitude = city.Latitude + 0.01,
            Longitude = city.Longitude + 0.01,
            Price = price,
            SuitableTypes = suitableTypes,
            ConcurrencyToken = Guid.NewGuid()
        };
        db.BuildingLots.Add(lot);
        await db.SaveChangesAsync();
        return lot.Id.ToString();
    }

    private async Task<string> PurchaseLotAndGetBuildingIdAsync(
        string token,
        string companyId,
        string lotId,
        string buildingType,
        string buildingName)
    {
        var result = await ExecuteGraphQlAsync(
            """
            mutation PurchaseLot($input: PurchaseLotInput!) {
              purchaseLot(input: $input) { building { id } }
            }
            """,
            new { input = new { companyId, lotId, buildingType, buildingName } },
            token);
        return result.GetProperty("data").GetProperty("purchaseLot").GetProperty("building").GetProperty("id").GetString()!;
    }

    #endregion
}
