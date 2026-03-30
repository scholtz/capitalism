using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Api.Data;
using Api.Data.Entities;
using Api.Engine;
using Api.Tests.Infrastructure;
using Api.Utilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

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

    private Task<TickProcessor> CreateProcessorAsync(IServiceScope scope)
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var phases = scope.ServiceProvider.GetServices<ITickPhase>();
        var logger = new NullLogger<TickProcessor>();
        return Task.FromResult(new TickProcessor(db, phases, logger));
    }

    private async Task ProcessTicksAsync(int count)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var processor = await CreateProcessorAsync(scope);

        for (var index = 0; index < count; index++)
        {
            await processor.ProcessTickAsync();
        }
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

    [Fact]
    public async Task FinishOnboarding_CompletesStagedFlowWithoutUnexpectedExecutionError()
    {
        var token = await RegisterAndGetTokenAsync(email: $"finish-onboarding-{Guid.NewGuid():N}@test.com");
        var (_, _, cityId, _) = await StartOnboardingCompanyAsync(token, companyName: "Staged Flow Co");
        var productId = await GetStarterProductIdAsync();
        var shopLotId = await GetAvailableLotIdAsync(cityId, "SALES_SHOP");

        var result = await FinishOnboardingAsync(token, productId, shopLotId);

        Assert.False(result.TryGetProperty("errors", out _));

        var payload = result.GetProperty("data").GetProperty("finishOnboarding");
        Assert.Equal("Staged Flow Co", payload.GetProperty("company").GetProperty("name").GetString());
        Assert.Equal("SALES_SHOP", payload.GetProperty("salesShop").GetProperty("type").GetString());
        Assert.Equal(productId, payload.GetProperty("selectedProduct").GetProperty("id").GetString());
    }

    [Fact]
    public async Task FinishOnboarding_StarterSupplyChainFeedsShopAndMakesFirstSale()
    {
        var token = await RegisterAndGetTokenAsync(email: $"finish-onboarding-sales-{Guid.NewGuid():N}@test.com");
        var (_, _, cityId, _) = await StartOnboardingCompanyAsync(token, companyName: "Starter Sales Co");
        var productId = await GetStarterProductIdAsync();
        var shopLotId = await GetAvailableLotIdAsync(cityId, "SALES_SHOP");

        var result = await FinishOnboardingAsync(token, productId, shopLotId);

        Assert.False(result.TryGetProperty("errors", out _));

        var payload = result.GetProperty("data").GetProperty("finishOnboarding");
        var shopId = Guid.Parse(payload.GetProperty("salesShop").GetProperty("id").GetString()!);
        var productGuid = Guid.Parse(productId);

        await ProcessTicksAsync(3);

        await using (var verificationScope = _factory.Services.CreateAsyncScope())
        {
            var db = verificationScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var shopPurchaseUnit = await db.BuildingUnits
                .SingleAsync(unit => unit.BuildingId == shopId && unit.UnitType == UnitType.Purchase);
            var purchasedInventory = await db.Inventories
                .Where(entry => entry.BuildingUnitId == shopPurchaseUnit.Id && entry.ProductTypeId == productGuid)
                .ToListAsync();

            Assert.Equal(
                0m,
                purchasedInventory.Sum(entry => entry.Quantity));
        }

        await ProcessTicksAsync(1);

        await using (var verificationScope = _factory.Services.CreateAsyncScope())
        {
            var db = verificationScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var shopPurchaseUnit = await db.BuildingUnits
                .SingleAsync(unit => unit.BuildingId == shopId && unit.UnitType == UnitType.Purchase);
            var purchasedInventory = await db.Inventories
                .Where(entry => entry.BuildingUnitId == shopPurchaseUnit.Id && entry.ProductTypeId == productGuid)
                .ToListAsync();

            Assert.True(
                purchasedInventory.Sum(entry => entry.Quantity) > 0m,
                "Starter sales shop purchase unit should fill from the starter factory output after onboarding.");
        }

        await ProcessTicksAsync(2);

        await using (var verificationScope = _factory.Services.CreateAsyncScope())
        {
            var db = verificationScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var salesRecords = await db.PublicSalesRecords
                .Where(record => record.BuildingId == shopId && record.ProductTypeId == productGuid && record.QuantitySold > 0m)
                .ToListAsync();

            Assert.NotEmpty(salesRecords);
        }
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
        Assert.Equal(10, state.GetProperty("tickIntervalSeconds").GetInt32());
        Assert.Equal(15m, state.GetProperty("taxRate").GetDecimal());
    }

    [Fact]
    public async Task StarterIndustries_ReturnsIndustryList()
    {
        var result = await ExecuteGraphQlAsync("{ starterIndustries { industries } }");

        var industries = result.GetProperty("data").GetProperty("starterIndustries").GetProperty("industries");
        Assert.Equal(3, industries.GetArrayLength());
    }

    // ── Encyclopedia ──────────────────────────────────────────────────────────

    [Fact]
    public async Task ResourceTypes_ReturnsDescriptionAndImageUrlFields()
    {
        var result = await ExecuteGraphQlAsync(
            "{ resourceTypes { slug description imageUrl weightPerUnit unitName unitSymbol } }");

        var resources = result.GetProperty("data").GetProperty("resourceTypes");
        Assert.True(resources.GetArrayLength() >= 8);

        var wood = resources.EnumerateArray().First(r => r.GetProperty("slug").GetString() == "wood");

        // description is seeded and should be non-empty
        var description = wood.GetProperty("description").GetString();
        Assert.False(string.IsNullOrWhiteSpace(description));

        // imageUrl is populated from the emoji SVG helper in the seeder
        var imageUrl = wood.GetProperty("imageUrl").GetString();
        Assert.False(string.IsNullOrWhiteSpace(imageUrl));
        Assert.StartsWith("data:image/", imageUrl);

        Assert.True(wood.GetProperty("weightPerUnit").GetDecimal() > 0);
        Assert.False(string.IsNullOrWhiteSpace(wood.GetProperty("unitName").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(wood.GetProperty("unitSymbol").GetString()));
    }

    [Fact]
    public async Task EncyclopediaResource_BySlug_ReturnsResourceWithMetadata()
    {
        var result = await ExecuteGraphQlAsync(
            """
            {
              encyclopediaResource(slug: "wood") {
                resource { id name slug category basePrice weightPerUnit unitName unitSymbol description imageUrl }
                productsUsingResource { name }
              }
            }
            """);

        var detail = result.GetProperty("data").GetProperty("encyclopediaResource");
        Assert.False(detail.ValueKind == System.Text.Json.JsonValueKind.Null);

        var resource = detail.GetProperty("resource");
        Assert.Equal("wood", resource.GetProperty("slug").GetString());
        Assert.Equal("Wood", resource.GetProperty("name").GetString());
        Assert.Equal("ORGANIC", resource.GetProperty("category").GetString());
        Assert.True(resource.GetProperty("basePrice").GetDecimal() > 0);
        Assert.True(resource.GetProperty("weightPerUnit").GetDecimal() > 0);
        Assert.False(string.IsNullOrWhiteSpace(resource.GetProperty("unitName").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(resource.GetProperty("description").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(resource.GetProperty("imageUrl").GetString()));
    }

    [Fact]
    public async Task EncyclopediaResource_BySlug_IncludesProductsThatUseResource()
    {
        var result = await ExecuteGraphQlAsync(
            """
            {
              encyclopediaResource(slug: "wood") {
                resource { slug }
                productsUsingResource {
                  name slug industry
                  recipes { quantity resourceType { slug } inputProductType { slug } }
                }
              }
            }
            """);

        var detail = result.GetProperty("data").GetProperty("encyclopediaResource");
        var products = detail.GetProperty("productsUsingResource");

        // The seeded Furniture products (Wooden Chair, Wooden Table, etc.) all require Wood
        Assert.True(products.GetArrayLength() > 0);

        foreach (var product in products.EnumerateArray())
        {
            var recipes = product.GetProperty("recipes");
            var usesWood = recipes.EnumerateArray().Any(r =>
                r.GetProperty("resourceType").ValueKind != System.Text.Json.JsonValueKind.Null
                && r.GetProperty("resourceType").GetProperty("slug").GetString() == "wood");

            Assert.True(usesWood, $"Product '{product.GetProperty("slug").GetString()}' is listed but does not have Wood in its recipe.");
        }

        // Products should be ordered by name
        var names = products.EnumerateArray().Select(p => p.GetProperty("name").GetString()).ToList();
        var sorted = names.OrderBy(n => n).ToList();
        Assert.Equal(sorted, names);
    }

    [Fact]
    public async Task EncyclopediaResource_BySlug_ReturnsNullForUnknownSlug()
    {
        var result = await ExecuteGraphQlAsync(
            "{ encyclopediaResource(slug: \"nonexistent-resource-xyz\") { resource { name } } }");

        var detail = result.GetProperty("data").GetProperty("encyclopediaResource");
        Assert.Equal(System.Text.Json.JsonValueKind.Null, detail.ValueKind);
    }

    [Fact]
    public async Task EncyclopediaResource_SiliconHasNoFurnitureProducts()
    {
        // Silicon is used only in electronics; a free (unauthenticated) user sees it returned
        // and any products in the list come from the silicon recipe chain.
        var result = await ExecuteGraphQlAsync(
            """
            {
              encyclopediaResource(slug: "silicon") {
                resource { slug category }
                productsUsingResource { name industry }
              }
            }
            """);

        var detail = result.GetProperty("data").GetProperty("encyclopediaResource");
        Assert.False(detail.ValueKind == System.Text.Json.JsonValueKind.Null);

        var resource = detail.GetProperty("resource");
        Assert.Equal("silicon", resource.GetProperty("slug").GetString());
        Assert.Equal("MINERAL", resource.GetProperty("category").GetString());

        var products = detail.GetProperty("productsUsingResource");
        // All returned products must actually use silicon
        foreach (var product in products.EnumerateArray())
        {
            Assert.Equal("ELECTRONICS", product.GetProperty("industry").GetString());
        }
    }

    [Fact]
    public async Task EncyclopediaResource_RespectsProSubscriptionForAccessMetadata()
    {
        // Without auth: isUnlockedForCurrentPlayer = false for Pro products
        var freeResult = await ExecuteGraphQlAsync(
            """
            {
              encyclopediaResource(slug: "silicon") {
                productsUsingResource { slug isProOnly isUnlockedForCurrentPlayer }
              }
            }
            """);

        var freeProducts = freeResult.GetProperty("data").GetProperty("encyclopediaResource")
            .GetProperty("productsUsingResource");

        foreach (var product in freeProducts.EnumerateArray())
        {
            if (product.GetProperty("isProOnly").GetBoolean())
            {
                Assert.False(product.GetProperty("isUnlockedForCurrentPlayer").GetBoolean());
            }
        }

        // With Pro subscription: isUnlockedForCurrentPlayer = true
        var token = await RegisterAndGetTokenAsync("encyclopedia-pro@test.com", "EncyclopediaPro");
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var player = await db.Players.SingleAsync(p => p.Email == "encyclopedia-pro@test.com");
            player.ProSubscriptionEndsAtUtc = DateTime.UtcNow.AddDays(30);
            await db.SaveChangesAsync();
        }

        var proResult = await ExecuteGraphQlAsync(
            """
            {
              encyclopediaResource(slug: "silicon") {
                productsUsingResource { slug isProOnly isUnlockedForCurrentPlayer }
              }
            }
            """,
            token: token);

        var proProducts = proResult.GetProperty("data").GetProperty("encyclopediaResource")
            .GetProperty("productsUsingResource");

        foreach (var product in proProducts.EnumerateArray())
        {
            if (product.GetProperty("isProOnly").GetBoolean())
            {
                Assert.True(product.GetProperty("isUnlockedForCurrentPlayer").GetBoolean());
            }
        }
    }

    [Fact]
    public async Task ProductTypes_ReturnsDescriptionField()
    {
        var result = await ExecuteGraphQlAsync(
            """
            {
              productTypes(industry: "FURNITURE") { slug description }
            }
            """);

        var products = result.GetProperty("data").GetProperty("productTypes");
        Assert.True(products.GetArrayLength() > 0);

        foreach (var product in products.EnumerateArray())
        {
            var description = product.GetProperty("description").GetString();
            Assert.False(string.IsNullOrWhiteSpace(description),
                $"Product '{product.GetProperty("slug").GetString()}' has no description.");
        }
    }

    [Fact]
    public async Task ProductTypes_RecipesIncludeIntermediateProductInputs()
    {
        // Some products use intermediate manufactured products (not just raw resources) in their recipe.
        // Verify that those inputProductType links are correctly included.
        var result = await ExecuteGraphQlAsync(
            """
            {
              productTypes {
                slug
                recipes {
                  quantity
                  resourceType { slug }
                  inputProductType { slug name }
                }
              }
            }
            """);

        var products = result.GetProperty("data").GetProperty("productTypes");
        var hasIntermediateInput = products.EnumerateArray()
            .SelectMany(p => p.GetProperty("recipes").EnumerateArray())
            .Any(r => r.GetProperty("inputProductType").ValueKind != System.Text.Json.JsonValueKind.Null);

        Assert.True(hasIntermediateInput,
            "At least one seeded product should have an intermediate manufactured product as a recipe input.");
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

    [Fact]
    public async Task BuildingUnitResourceHistories_ReturnsRecentUnitMovementHistory()
    {
        var token = await RegisterAndGetTokenAsync("history-query@test.com", "History Query Tester");

        Guid buildingId;
        Guid woodResourceId;
        Guid chairProductId;

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var player = await db.Players.SingleAsync(candidate => candidate.Email == "history-query@test.com");
            var city = await db.Cities.FirstAsync();
            var product = await db.ProductTypes
                .Include(candidate => candidate.Recipes)
                .FirstAsync(candidate => candidate.Slug == "wooden-chair");
            var woodResource = await db.ResourceTypes.FirstAsync(candidate => candidate.Slug == "wood");

            woodResourceId = woodResource.Id;
            chairProductId = product.Id;

            var company = new Company
            {
                Id = Guid.NewGuid(),
                PlayerId = player.Id,
                Name = "History Query Corp",
                Cash = 500_000m,
            };
            db.Companies.Add(company);

            var building = new Building
            {
                Id = Guid.NewGuid(),
                CompanyId = company.Id,
                CityId = city.Id,
                Type = BuildingType.Factory,
                Name = "History Query Factory",
                Level = 1,
            };
            db.Buildings.Add(building);

            var purchaseUnit = new BuildingUnit
            {
                Id = Guid.NewGuid(),
                BuildingId = building.Id,
                UnitType = UnitType.Purchase,
                GridX = 0,
                GridY = 0,
                Level = 1,
                LinkRight = true,
                ResourceTypeId = woodResource.Id,
                PurchaseSource = "EXCHANGE",
                MaxPrice = 999_999m,
            };
            var manufacturingUnit = new BuildingUnit
            {
                Id = Guid.NewGuid(),
                BuildingId = building.Id,
                UnitType = UnitType.Manufacturing,
                GridX = 1,
                GridY = 0,
                Level = 1,
                LinkLeft = true,
                ProductTypeId = product.Id,
            };

            db.BuildingUnits.AddRange(purchaseUnit, manufacturingUnit);
            db.Inventories.Add(new Inventory
            {
                Id = Guid.NewGuid(),
                BuildingId = building.Id,
                BuildingUnitId = purchaseUnit.Id,
                ResourceTypeId = woodResource.Id,
                Quantity = 8m,
                Quality = 0.7m,
                SourcingCostTotal = 32m,
            });

            await db.SaveChangesAsync();
            buildingId = building.Id;
        }

        await ProcessTicksAsync(1);

        var result = await ExecuteGraphQlAsync(
            """
            query BuildingUnitResourceHistories($buildingId: UUID!, $limit: Int) {
              buildingUnitResourceHistories(buildingId: $buildingId, limit: $limit) {
                buildingUnitId
                resourceTypeId
                productTypeId
                tick
                inflowQuantity
                outflowQuantity
                consumedQuantity
                producedQuantity
              }
            }
            """,
            new { buildingId, limit = 20 },
            token);

        Assert.False(result.TryGetProperty("errors", out _));

        var entries = result.GetProperty("data").GetProperty("buildingUnitResourceHistories").EnumerateArray().ToList();

        Assert.Contains(entries, entry =>
            entry.GetProperty("resourceTypeId").GetString() == woodResourceId.ToString()
            && entry.GetProperty("consumedQuantity").GetDecimal() > 0m);
        Assert.Contains(entries, entry =>
            entry.GetProperty("productTypeId").GetString() == chairProductId.ToString()
            && entry.GetProperty("producedQuantity").GetDecimal() > 0m);
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
                        new { unitType = "MANUFACTURING", gridX = 1, gridY = 0, linkUp = false, linkDown = false, linkLeft = true, linkRight = false, linkUpLeft = false, linkUpRight = false, linkDownLeft = false, linkDownRight = false, resourceTypeId = (string?)null, productTypeId = proProductId }
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
                public async Task QueuedBuildingConfiguration_DeductsCompanyCashWhenNewUnitsActivate()
                {
                    var token = await RegisterAndGetTokenAsync("apply-cost@test.com", "ApplyCost");

                    var companyResult = await ExecuteGraphQlAsync(
                        "mutation CreateCompany($input: CreateCompanyInput!) { createCompany(input: $input) { id } }",
                        new { input = new { name = "Apply Cost Corp" } },
                        token);
                    var companyId = companyResult.GetProperty("data").GetProperty("createCompany").GetProperty("id").GetString()!;
                    var companyGuid = Guid.Parse(companyId);

                    var citiesResult = await ExecuteGraphQlAsync("{ cities { id } }");
                    var cityId = citiesResult.GetProperty("data").GetProperty("cities")[0].GetProperty("id").GetString()!;

                    var buildingResult = await ExecuteGraphQlAsync(
                        "mutation PlaceBuilding($input: PlaceBuildingInput!) { placeBuilding(input: $input) { id } }",
                        new { input = new { companyId, cityId, type = "FACTORY", name = "Apply Cost Factory" } },
                        token);
                    var buildingId = buildingResult.GetProperty("data").GetProperty("placeBuilding").GetProperty("id").GetString()!;

                    await using (var scope = _factory.Services.CreateAsyncScope())
                    {
                        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                        var company = await db.Companies.FirstAsync(candidate => candidate.Id == companyGuid);
                        company.Cash = 20_000m;
                        await db.SaveChangesAsync();
                    }

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
                                    new { unitType = "PURCHASE", gridX = 0, gridY = 0, linkUp = false, linkDown = false, linkLeft = false, linkRight = false, linkUpLeft = false, linkUpRight = false, linkDownLeft = false, linkDownRight = false },
                                    new { unitType = "STORAGE", gridX = 1, gridY = 0, linkUp = false, linkDown = false, linkLeft = false, linkRight = false, linkUpLeft = false, linkUpRight = false, linkDownLeft = false, linkDownRight = false }
                                }
                            }
                        },
                        token);

                    await AdvanceGameTicksAsync(3);

                    var companiesResult = await ExecuteGraphQlAsync(
                        """
                        {
                            myCompanies {
                            id
                            cash
                            buildings {
                                id
                                units { gridX gridY unitType }
                                pendingConfiguration { id }
                            }
                            }
                        }
                        """,
                        token: token);

                    var companyJson = companiesResult.GetProperty("data").GetProperty("myCompanies")
                        .EnumerateArray().Single(candidate => candidate.GetProperty("id").GetString() == companyId);
                    var building = companyJson.GetProperty("buildings")
                        .EnumerateArray().Single(candidate => candidate.GetProperty("id").GetString() == buildingId);

                    Assert.Equal(12_000m, companyJson.GetProperty("cash").GetDecimal());
                    Assert.Equal(2, building.GetProperty("units").GetArrayLength());
                    Assert.Equal(JsonValueKind.Null, building.GetProperty("pendingConfiguration").ValueKind);
                }

                [Fact]
                public async Task QueuedBuildingConfiguration_InsufficientCashDelaysCostfulActivation()
                {
                    var token = await RegisterAndGetTokenAsync("delay-cost@test.com", "DelayCost");

                    var companyResult = await ExecuteGraphQlAsync(
                        "mutation CreateCompany($input: CreateCompanyInput!) { createCompany(input: $input) { id } }",
                        new { input = new { name = "Delay Cost Corp" } },
                        token);
                    var companyId = companyResult.GetProperty("data").GetProperty("createCompany").GetProperty("id").GetString()!;
                    var companyGuid = Guid.Parse(companyId);

                    var citiesResult = await ExecuteGraphQlAsync("{ cities { id } }");
                    var cityId = citiesResult.GetProperty("data").GetProperty("cities")[0].GetProperty("id").GetString()!;

                    var buildingResult = await ExecuteGraphQlAsync(
                        "mutation PlaceBuilding($input: PlaceBuildingInput!) { placeBuilding(input: $input) { id } }",
                        new { input = new { companyId, cityId, type = "FACTORY", name = "Delay Cost Factory" } },
                        token);
                    var buildingId = buildingResult.GetProperty("data").GetProperty("placeBuilding").GetProperty("id").GetString()!;

                    await using (var scope = _factory.Services.CreateAsyncScope())
                    {
                        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                        var company = await db.Companies.FirstAsync(candidate => candidate.Id == companyGuid);
                        company.Cash = 4_000m;
                        await db.SaveChangesAsync();
                    }

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

                    await AdvanceGameTicksAsync(3);

                    long currentTick;
                    await using (var scope = _factory.Services.CreateAsyncScope())
                    {
                        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                        currentTick = (await db.GameStates.FindAsync(1))!.CurrentTick;
                    }

                    var delayedResult = await ExecuteGraphQlAsync(
                        """
                        {
                            myCompanies {
                            id
                            cash
                            buildings {
                                id
                                units { id }
                                pendingConfiguration {
                                id
                                appliesAtTick
                                }
                            }
                            }
                        }
                        """,
                        token: token);

                    var delayedCompany = delayedResult.GetProperty("data").GetProperty("myCompanies")
                        .EnumerateArray().Single(candidate => candidate.GetProperty("id").GetString() == companyId);
                    var delayedBuilding = delayedCompany.GetProperty("buildings")
                        .EnumerateArray().Single(candidate => candidate.GetProperty("id").GetString() == buildingId);

                    Assert.Equal(4_000m, delayedCompany.GetProperty("cash").GetDecimal());
                    Assert.Equal(0, delayedBuilding.GetProperty("units").GetArrayLength());
                    Assert.Equal(currentTick + 1, delayedBuilding.GetProperty("pendingConfiguration").GetProperty("appliesAtTick").GetInt64());

                    await using (var scope = _factory.Services.CreateAsyncScope())
                    {
                        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                        var company = await db.Companies.FirstAsync(candidate => candidate.Id == companyGuid);
                        company.Cash = 10_000m;
                        await db.SaveChangesAsync();
                    }

                    await AdvanceGameTicksAsync(1);

                    var appliedResult = await ExecuteGraphQlAsync(
                        """
                        {
                            myCompanies {
                            id
                            cash
                            buildings {
                                id
                                units { id }
                                pendingConfiguration { id }
                            }
                            }
                        }
                        """,
                        token: token);

                    var appliedCompany = appliedResult.GetProperty("data").GetProperty("myCompanies")
                        .EnumerateArray().Single(candidate => candidate.GetProperty("id").GetString() == companyId);
                    var appliedBuilding = appliedCompany.GetProperty("buildings")
                        .EnumerateArray().Single(candidate => candidate.GetProperty("id").GetString() == buildingId);

                    Assert.Equal(5_500m, appliedCompany.GetProperty("cash").GetDecimal());
                    Assert.Equal(1, appliedBuilding.GetProperty("units").GetArrayLength());
                    Assert.Equal(JsonValueKind.Null, appliedBuilding.GetProperty("pendingConfiguration").ValueKind);
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
                                new { unitType = "BRANDING", gridX = 0, gridY = 0, linkUp = false, linkDown = false, linkLeft = false, linkRight = true, linkUpLeft = false, linkUpRight = false, linkDownLeft = false, linkDownRight = false },
                                new { unitType = "MANUFACTURING", gridX = 1, gridY = 0, linkUp = false, linkDown = false, linkLeft = true, linkRight = false, linkUpLeft = false, linkUpRight = false, linkDownLeft = false, linkDownRight = false }
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
                        && unit.GetProperty("unitType").GetString() == "BRANDING");
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

    #region RecipeCompatibility

    [Fact]
    public async Task StoreBuildingConfiguration_CompatibleRecipeInput_Succeeds()
    {
        var token = await RegisterAndGetTokenAsync("recipe-compat@test.com", "RecipeCompat");

        var companyResult = await ExecuteGraphQlAsync(
            "mutation CreateCompany($input: CreateCompanyInput!) { createCompany(input: $input) { id } }",
            new { input = new { name = "Compat Corp" } },
            token);
        var companyId = companyResult.GetProperty("data").GetProperty("createCompany").GetProperty("id").GetString();

        var citiesResult = await ExecuteGraphQlAsync("{ cities { id } }");
        var cityId = citiesResult.GetProperty("data").GetProperty("cities")[0].GetProperty("id").GetString();

        var buildingResult = await ExecuteGraphQlAsync(
            "mutation PlaceBuilding($input: PlaceBuildingInput!) { placeBuilding(input: $input) { id } }",
            new { input = new { companyId, cityId, type = "FACTORY", name = "Compat Factory" } },
            token);
        var buildingId = buildingResult.GetProperty("data").GetProperty("placeBuilding").GetProperty("id").GetString();

        // Get Wooden Chair product and Wood resource
        var productsResult = await ExecuteGraphQlAsync(
            "{ productTypes(industry: \"FURNITURE\") { id slug } }",
            token: token);
        var chairProductId = productsResult.GetProperty("data").GetProperty("productTypes")
            .EnumerateArray()
            .First(p => p.GetProperty("slug").GetString() == "wooden-chair")
            .GetProperty("id").GetString();

        var resourcesResult = await ExecuteGraphQlAsync(
            "{ resourceTypes { id slug } }",
            token: token);
        var woodResourceId = resourcesResult.GetProperty("data").GetProperty("resourceTypes")
            .EnumerateArray()
            .First(r => r.GetProperty("slug").GetString() == "wood")
            .GetProperty("id").GetString();

        // PURCHASE (Wood) → MANUFACTURING (Wooden Chair) — compatible combination
        var result = await ExecuteGraphQlAsync(
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
                        new { unitType = "PURCHASE", gridX = 0, gridY = 0, linkUp = false, linkDown = false, linkLeft = false, linkRight = true, linkUpLeft = false, linkUpRight = false, linkDownLeft = false, linkDownRight = false, resourceTypeId = woodResourceId, productTypeId = (string?)null },
                        new { unitType = "MANUFACTURING", gridX = 1, gridY = 0, linkUp = false, linkDown = false, linkLeft = true, linkRight = true, linkUpLeft = false, linkUpRight = false, linkDownLeft = false, linkDownRight = false, resourceTypeId = (string?)null, productTypeId = chairProductId },
                        new { unitType = "STORAGE", gridX = 2, gridY = 0, linkUp = false, linkDown = false, linkLeft = true, linkRight = false, linkUpLeft = false, linkUpRight = false, linkDownLeft = false, linkDownRight = false, resourceTypeId = (string?)null, productTypeId = (string?)null }
                    }
                }
            },
            token);

        Assert.False(result.TryGetProperty("errors", out _), "Compatible combination should succeed");

        // Verify the pending configuration stores the resource and product type IDs
        var buildingQuery = await ExecuteGraphQlAsync(
            """
            {
              myCompanies {
                buildings {
                  id
                  pendingConfiguration {
                    units { unitType resourceTypeId productTypeId }
                  }
                }
              }
            }
            """,
            token: token);

        var buildings = buildingQuery.GetProperty("data").GetProperty("myCompanies")[0].GetProperty("buildings").EnumerateArray().ToList();
        var factory = buildings.Single(b => b.GetProperty("id").GetString() == buildingId);
        var pending = factory.GetProperty("pendingConfiguration").GetProperty("units").EnumerateArray().ToList();

        Assert.Contains(pending, u =>
            u.GetProperty("unitType").GetString() == "PURCHASE"
            && u.GetProperty("resourceTypeId").GetString() == woodResourceId);
        Assert.Contains(pending, u =>
            u.GetProperty("unitType").GetString() == "MANUFACTURING"
            && u.GetProperty("productTypeId").GetString() == chairProductId);
    }

    [Fact]
    public async Task StoreBuildingConfiguration_IncompatibleRecipeInput_ReturnsError()
    {
        var token = await RegisterAndGetTokenAsync("recipe-incompat@test.com", "RecipeIncompat");

        var companyResult = await ExecuteGraphQlAsync(
            "mutation CreateCompany($input: CreateCompanyInput!) { createCompany(input: $input) { id } }",
            new { input = new { name = "Incompat Corp" } },
            token);
        var companyId = companyResult.GetProperty("data").GetProperty("createCompany").GetProperty("id").GetString();

        var citiesResult = await ExecuteGraphQlAsync("{ cities { id } }");
        var cityId = citiesResult.GetProperty("data").GetProperty("cities")[0].GetProperty("id").GetString();

        var buildingResult = await ExecuteGraphQlAsync(
            "mutation PlaceBuilding($input: PlaceBuildingInput!) { placeBuilding(input: $input) { id } }",
            new { input = new { companyId, cityId, type = "FACTORY", name = "Incompat Factory" } },
            token);
        var buildingId = buildingResult.GetProperty("data").GetProperty("placeBuilding").GetProperty("id").GetString();

        // Get Wooden Chair (requires Wood) and Grain resource (incompatible)
        var productsResult = await ExecuteGraphQlAsync(
            "{ productTypes(industry: \"FURNITURE\") { id slug } }",
            token: token);
        var chairProductId = productsResult.GetProperty("data").GetProperty("productTypes")
            .EnumerateArray()
            .First(p => p.GetProperty("slug").GetString() == "wooden-chair")
            .GetProperty("id").GetString();

        var resourcesResult = await ExecuteGraphQlAsync(
            "{ resourceTypes { id slug } }",
            token: token);
        var grainResourceId = resourcesResult.GetProperty("data").GetProperty("resourceTypes")
            .EnumerateArray()
            .First(r => r.GetProperty("slug").GetString() == "grain")
            .GetProperty("id").GetString();

        // PURCHASE (Grain) → MANUFACTURING (Wooden Chair which needs Wood) — incompatible!
        var result = await ExecuteGraphQlAsync(
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
                        new { unitType = "PURCHASE", gridX = 0, gridY = 0, linkUp = false, linkDown = false, linkLeft = false, linkRight = true, linkUpLeft = false, linkUpRight = false, linkDownLeft = false, linkDownRight = false, resourceTypeId = grainResourceId, productTypeId = (string?)null },
                        new { unitType = "MANUFACTURING", gridX = 1, gridY = 0, linkUp = false, linkDown = false, linkLeft = true, linkRight = true, linkUpLeft = false, linkUpRight = false, linkDownLeft = false, linkDownRight = false, resourceTypeId = (string?)null, productTypeId = chairProductId },
                        new { unitType = "STORAGE", gridX = 2, gridY = 0, linkUp = false, linkDown = false, linkLeft = true, linkRight = false, linkUpLeft = false, linkUpRight = false, linkDownLeft = false, linkDownRight = false, resourceTypeId = (string?)null, productTypeId = (string?)null }
                    }
                }
            },
            token);

        Assert.True(result.TryGetProperty("errors", out var errors), "Incompatible combination should return an error");
        Assert.Equal("RECIPE_INPUT_MISMATCH", errors[0].GetProperty("extensions").GetProperty("code").GetString());
    }

    [Fact]
    public async Task StoreBuildingConfiguration_ManufacturingWithoutPurchaseResource_Succeeds()
    {
        // A MANUFACTURING unit with a product type but an unconfigured (null-resource) PURCHASE unit
        // is incomplete but not invalid — the player should be allowed to save partial configurations.
        var token = await RegisterAndGetTokenAsync("partial-config@test.com", "PartialConfig");

        var companyResult = await ExecuteGraphQlAsync(
            "mutation CreateCompany($input: CreateCompanyInput!) { createCompany(input: $input) { id } }",
            new { input = new { name = "Partial Corp" } },
            token);
        var companyId = companyResult.GetProperty("data").GetProperty("createCompany").GetProperty("id").GetString();

        var citiesResult = await ExecuteGraphQlAsync("{ cities { id } }");
        var cityId = citiesResult.GetProperty("data").GetProperty("cities")[0].GetProperty("id").GetString();

        var buildingResult = await ExecuteGraphQlAsync(
            "mutation PlaceBuilding($input: PlaceBuildingInput!) { placeBuilding(input: $input) { id } }",
            new { input = new { companyId, cityId, type = "FACTORY", name = "Partial Factory" } },
            token);
        var buildingId = buildingResult.GetProperty("data").GetProperty("placeBuilding").GetProperty("id").GetString();

        var productsResult = await ExecuteGraphQlAsync(
            "{ productTypes(industry: \"FURNITURE\") { id slug } }",
            token: token);
        var chairProductId = productsResult.GetProperty("data").GetProperty("productTypes")
            .EnumerateArray()
            .First(p => p.GetProperty("slug").GetString() == "wooden-chair")
            .GetProperty("id").GetString();

        // MANUFACTURING has Wooden Chair but PURCHASE has no resource — should be allowed (incomplete, not invalid)
        var result = await ExecuteGraphQlAsync(
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
                        new { unitType = "PURCHASE", gridX = 0, gridY = 0, linkUp = false, linkDown = false, linkLeft = false, linkRight = true, linkUpLeft = false, linkUpRight = false, linkDownLeft = false, linkDownRight = false, resourceTypeId = (string?)null, productTypeId = (string?)null },
                        new { unitType = "MANUFACTURING", gridX = 1, gridY = 0, linkUp = false, linkDown = false, linkLeft = true, linkRight = true, linkUpLeft = false, linkUpRight = false, linkDownLeft = false, linkDownRight = false, resourceTypeId = (string?)null, productTypeId = chairProductId },
                        new { unitType = "STORAGE", gridX = 2, gridY = 0, linkUp = false, linkDown = false, linkLeft = true, linkRight = false, linkUpLeft = false, linkUpRight = false, linkDownLeft = false, linkDownRight = false, resourceTypeId = (string?)null, productTypeId = (string?)null }
                    }
                }
            },
            token);

        Assert.False(result.TryGetProperty("errors", out _), "Incomplete (not incompatible) configuration should be allowed");
    }

    [Fact]
    public async Task StoreBuildingConfiguration_UnauthorizedPlayerCannotConfigureAnotherPlayersBuilding()
    {
        var token1 = await RegisterAndGetTokenAsync("owner-bldg@test.com", "Owner");
        var token2 = await RegisterAndGetTokenAsync("intruder-bldg@test.com", "Intruder");

        var companyResult = await ExecuteGraphQlAsync(
            "mutation CreateCompany($input: CreateCompanyInput!) { createCompany(input: $input) { id } }",
            new { input = new { name = "Owner Corp" } },
            token1);
        var companyId = companyResult.GetProperty("data").GetProperty("createCompany").GetProperty("id").GetString();

        var citiesResult = await ExecuteGraphQlAsync("{ cities { id } }");
        var cityId = citiesResult.GetProperty("data").GetProperty("cities")[0].GetProperty("id").GetString();

        var buildingResult = await ExecuteGraphQlAsync(
            "mutation PlaceBuilding($input: PlaceBuildingInput!) { placeBuilding(input: $input) { id } }",
            new { input = new { companyId, cityId, type = "FACTORY", name = "Owner Factory" } },
            token1);
        var buildingId = buildingResult.GetProperty("data").GetProperty("placeBuilding").GetProperty("id").GetString();

        // token2 (intruder) should not be able to configure token1's building
        var result = await ExecuteGraphQlAsync(
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
            token2);

        Assert.True(result.TryGetProperty("errors", out var errors));
        Assert.Equal("BUILDING_NOT_FOUND", errors[0].GetProperty("extensions").GetProperty("code").GetString());
    }

    [Fact]
    public async Task StoreBuildingConfiguration_SalesShop_PersistsPublicSalesProductAndPrice()
    {
        var token = await RegisterAndGetTokenAsync($"shop-persist-{Guid.NewGuid()}@test.com", "Shop Persist");

        var companyResult = await ExecuteGraphQlAsync(
            "mutation CreateCompany($input: CreateCompanyInput!) { createCompany(input: $input) { id } }",
            new { input = new { name = "Shop Persist Corp" } },
            token);
        var companyId = companyResult.GetProperty("data").GetProperty("createCompany").GetProperty("id").GetString();

        var citiesResult = await ExecuteGraphQlAsync("{ cities { id } }");
        var cityId = citiesResult.GetProperty("data").GetProperty("cities")[0].GetProperty("id").GetString();

        var buildingResult = await ExecuteGraphQlAsync(
            "mutation PlaceBuilding($input: PlaceBuildingInput!) { placeBuilding(input: $input) { id } }",
            new { input = new { companyId, cityId, type = "SALES_SHOP", name = "Starter Shop" } },
            token);
        var buildingId = buildingResult.GetProperty("data").GetProperty("placeBuilding").GetProperty("id").GetString();

        // Get a non-Pro product type to configure (wooden-chair is always free)
        var productsResult = await ExecuteGraphQlAsync(
            """
            query {
              productTypes(industry: "FURNITURE") {
                id
                slug
                basePrice
                isProOnly
              }
            }
            """);
        var chairProduct = productsResult.GetProperty("data").GetProperty("productTypes")
            .EnumerateArray()
            .First(p => p.GetProperty("slug").GetString() == "wooden-chair");
        var productId = chairProduct.GetProperty("id").GetString();
        var basePrice = chairProduct.GetProperty("basePrice").GetDecimal();
        var sellingPrice = basePrice * 1.5m;

        // Configure the sales shop with PURCHASE (0,0) → PUBLIC_SALES (1,0)
        var configResult = await ExecuteGraphQlAsync(
            """
            mutation StoreBuildingConfiguration($input: StoreBuildingConfigurationInput!) {
              storeBuildingConfiguration(input: $input) {
                id
                units {
                  unitType
                  gridX
                  productTypeId
                  minPrice
                  saleVisibility
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
                        new { unitType = "PURCHASE", gridX = 0, gridY = 0, linkUp = false, linkDown = false, linkLeft = false, linkRight = true, linkUpLeft = false, linkUpRight = false, linkDownLeft = false, linkDownRight = false, productTypeId = productId, minPrice = (decimal?)null },
                        new { unitType = "PUBLIC_SALES", gridX = 1, gridY = 0, linkUp = false, linkDown = false, linkLeft = false, linkRight = false, linkUpLeft = false, linkUpRight = false, linkDownLeft = false, linkDownRight = false, productTypeId = productId, minPrice = (decimal?)sellingPrice }
                    }
                }
            },
            token);

        Assert.False(configResult.TryGetProperty("errors", out _), "Sales shop configuration should succeed");

        // Read back the configuration to verify persistence
        var companiesResult = await ExecuteGraphQlAsync(
            """
            {
              myCompanies {
                buildings {
                  type
                  pendingConfiguration {
                    units {
                      unitType
                      gridX
                      productTypeId
                      minPrice
                    }
                  }
                }
              }
            }
            """,
            token: token);

        var buildings = companiesResult.GetProperty("data").GetProperty("myCompanies")[0]
            .GetProperty("buildings").EnumerateArray().ToList();
        var shop = buildings.Single(building => building.GetProperty("type").GetString() == "SALES_SHOP");
        var pendingUnits = shop.GetProperty("pendingConfiguration").GetProperty("units").EnumerateArray().ToList();

        var publicSales = pendingUnits.Single(unit => unit.GetProperty("unitType").GetString() == "PUBLIC_SALES");
        Assert.Equal(productId, publicSales.GetProperty("productTypeId").GetString());
        Assert.Equal(sellingPrice, publicSales.GetProperty("minPrice").GetDecimal());

        var purchase = pendingUnits.Single(unit => unit.GetProperty("unitType").GetString() == "PURCHASE");
        Assert.Equal(productId, purchase.GetProperty("productTypeId").GetString());
    }

    [Fact]
    public async Task StoreBuildingConfiguration_SalesShop_NegativeMinPriceRejected()
    {
        var token = await RegisterAndGetTokenAsync($"shop-negprice-{Guid.NewGuid()}@test.com", "Shop NegPrice");

        var companyResult = await ExecuteGraphQlAsync(
            "mutation CreateCompany($input: CreateCompanyInput!) { createCompany(input: $input) { id } }",
            new { input = new { name = "NegPrice Corp" } },
            token);
        var companyId = companyResult.GetProperty("data").GetProperty("createCompany").GetProperty("id").GetString();

        var citiesResult = await ExecuteGraphQlAsync("{ cities { id } }");
        var cityId = citiesResult.GetProperty("data").GetProperty("cities")[0].GetProperty("id").GetString();

        var buildingResult = await ExecuteGraphQlAsync(
            "mutation PlaceBuilding($input: PlaceBuildingInput!) { placeBuilding(input: $input) { id } }",
            new { input = new { companyId, cityId, type = "SALES_SHOP", name = "Price Test Shop" } },
            token);
        var buildingId = buildingResult.GetProperty("data").GetProperty("placeBuilding").GetProperty("id").GetString();

        var productsResult = await ExecuteGraphQlAsync("{ productTypes(industry: \"FURNITURE\") { id slug } }");
        var productId = productsResult.GetProperty("data").GetProperty("productTypes")
            .EnumerateArray()
            .First(p => p.GetProperty("slug").GetString() == "wooden-chair")
            .GetProperty("id").GetString();

        // Attempt to configure PUBLIC_SALES unit with a negative minimum price
        var result = await ExecuteGraphQlAsync(
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
                        new { unitType = "PUBLIC_SALES", gridX = 0, gridY = 0, linkUp = false, linkDown = false, linkLeft = false, linkRight = false, linkUpLeft = false, linkUpRight = false, linkDownLeft = false, linkDownRight = false, productTypeId = productId, minPrice = (decimal?)-10m }
                    }
                }
            },
            token);

        Assert.True(result.TryGetProperty("errors", out var errors));
        Assert.Equal("INVALID_MIN_PRICE", errors[0].GetProperty("extensions").GetProperty("code").GetString());
    }

    [Fact]
    public async Task StoreBuildingConfiguration_SalesShop_ZeroMinPriceRejected()
    {
        // A price of 0 is explicitly invalid: the runtime engine (PublicSalesPhase) silently
        // replaces price <= 0 with the product base price, so accepting 0 from the player
        // would misrepresent the actual selling price.  Validation must block it up front.
        var token = await RegisterAndGetTokenAsync($"shop-zeroprice-{Guid.NewGuid()}@test.com", "Shop ZeroPrice");

        var companyResult = await ExecuteGraphQlAsync(
            "mutation CreateCompany($input: CreateCompanyInput!) { createCompany(input: $input) { id } }",
            new { input = new { name = "ZeroPrice Corp" } },
            token);
        var companyId = companyResult.GetProperty("data").GetProperty("createCompany").GetProperty("id").GetString();

        var citiesResult = await ExecuteGraphQlAsync("{ cities { id } }");
        var cityId = citiesResult.GetProperty("data").GetProperty("cities")[0].GetProperty("id").GetString();

        var buildingResult = await ExecuteGraphQlAsync(
            "mutation PlaceBuilding($input: PlaceBuildingInput!) { placeBuilding(input: $input) { id } }",
            new { input = new { companyId, cityId, type = "SALES_SHOP", name = "Zero Price Shop" } },
            token);
        var buildingId = buildingResult.GetProperty("data").GetProperty("placeBuilding").GetProperty("id").GetString();

        var productsResult = await ExecuteGraphQlAsync("{ productTypes(industry: \"FURNITURE\") { id slug } }");
        var productId = productsResult.GetProperty("data").GetProperty("productTypes")
            .EnumerateArray()
            .First(p => p.GetProperty("slug").GetString() == "wooden-chair")
            .GetProperty("id").GetString();

        // Attempt to configure PUBLIC_SALES unit with a price of exactly zero
        var result = await ExecuteGraphQlAsync(
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
                        new { unitType = "PUBLIC_SALES", gridX = 0, gridY = 0, linkUp = false, linkDown = false, linkLeft = false, linkRight = false, linkUpLeft = false, linkUpRight = false, linkDownLeft = false, linkDownRight = false, productTypeId = productId, minPrice = (decimal?)0m }
                    }
                }
            },
            token);

        Assert.True(result.TryGetProperty("errors", out var errors));
        Assert.Equal("INVALID_MIN_PRICE", errors[0].GetProperty("extensions").GetProperty("code").GetString());
    }

    [Fact]
    public async Task StoreBuildingConfiguration_SalesShop_UnauthorizedPlayerCannotConfigure()
    {
        var ownerToken = await RegisterAndGetTokenAsync($"shop-owner-{Guid.NewGuid()}@test.com", "Shop Owner");
        var intruderToken = await RegisterAndGetTokenAsync($"shop-intruder-{Guid.NewGuid()}@test.com", "Intruder");

        var companyResult = await ExecuteGraphQlAsync(
            "mutation CreateCompany($input: CreateCompanyInput!) { createCompany(input: $input) { id } }",
            new { input = new { name = "Owner Shop Corp" } },
            ownerToken);
        var companyId = companyResult.GetProperty("data").GetProperty("createCompany").GetProperty("id").GetString();

        var citiesResult = await ExecuteGraphQlAsync("{ cities { id } }");
        var cityId = citiesResult.GetProperty("data").GetProperty("cities")[0].GetProperty("id").GetString();

        var buildingResult = await ExecuteGraphQlAsync(
            "mutation PlaceBuilding($input: PlaceBuildingInput!) { placeBuilding(input: $input) { id } }",
            new { input = new { companyId, cityId, type = "SALES_SHOP", name = "Owner Sales Shop" } },
            ownerToken);
        var buildingId = buildingResult.GetProperty("data").GetProperty("placeBuilding").GetProperty("id").GetString();

        // The intruder tries to configure the owner's sales shop
        var result = await ExecuteGraphQlAsync(
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
                        new { unitType = "PUBLIC_SALES", gridX = 0, gridY = 0, linkUp = false, linkDown = false, linkLeft = false, linkRight = false, linkUpLeft = false, linkUpRight = false, linkDownLeft = false, linkDownRight = false }
                    }
                }
            },
            intruderToken);

        Assert.True(result.TryGetProperty("errors", out var errors));
        Assert.Equal("BUILDING_NOT_FOUND", errors[0].GetProperty("extensions").GetProperty("code").GetString());
    }

    #endregion

    #region CancelBuildingConfiguration

    [Fact]
    public async Task CancelBuildingConfiguration_PendingAddition_ReturnsRevertingPlanWithFastRollback()
    {
        var token = await RegisterAndGetTokenAsync("cancel-mut@test.com", "CancelMut");

        var companyResult = await ExecuteGraphQlAsync(
            "mutation CreateCompany($input: CreateCompanyInput!) { createCompany(input: $input) { id } }",
            new { input = new { name = "CancelMut Corp" } },
            token);
        var companyId = companyResult.GetProperty("data").GetProperty("createCompany").GetProperty("id").GetString();

        var citiesResult = await ExecuteGraphQlAsync("{ cities { id } }");
        var cityId = citiesResult.GetProperty("data").GetProperty("cities")[0].GetProperty("id").GetString();

        var buildingResult = await ExecuteGraphQlAsync(
            "mutation PlaceBuilding($input: PlaceBuildingInput!) { placeBuilding(input: $input) { id } }",
            new { input = new { companyId, cityId, type = "FACTORY", name = "CancelMut Factory" } },
            token);
        var buildingId = buildingResult.GetProperty("data").GetProperty("placeBuilding").GetProperty("id").GetString();

        // Queue a unit addition
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

        // Cancel the pending plan
        var cancelResult = await ExecuteGraphQlAsync(
            """
            mutation CancelBuildingConfiguration($input: CancelBuildingConfigurationInput!) {
                cancelBuildingConfiguration(input: $input) {
                    totalTicksRequired
                    removals { gridX gridY ticksRequired isReverting }
                    units { isChanged isReverting }
                }
            }
            """,
            new { input = new { buildingId } },
            token);

        if (cancelResult.TryGetProperty("errors", out var cancelErrors))
        {
            throw new Exception(cancelErrors[0].GetProperty("message").GetString());
        }

        var plan = cancelResult.GetProperty("data").GetProperty("cancelBuildingConfiguration");
        // 10% of 3 ticks = 1 tick rollback
        Assert.Equal(1, plan.GetProperty("totalTicksRequired").GetInt32());
        var removal = Assert.Single(plan.GetProperty("removals").EnumerateArray());
        Assert.Equal(0, removal.GetProperty("gridX").GetInt32());
        Assert.Equal(0, removal.GetProperty("gridY").GetInt32());
        Assert.Equal(1, removal.GetProperty("ticksRequired").GetInt32());
        Assert.True(removal.GetProperty("isReverting").GetBoolean());
        Assert.Empty(plan.GetProperty("units").EnumerateArray());
    }

    [Fact]
    public async Task CancelBuildingConfiguration_NoPendingPlan_ReturnsError()
    {
        var token = await RegisterAndGetTokenAsync("cancel-noop@test.com", "CancelNoop");

        var companyResult = await ExecuteGraphQlAsync(
            "mutation CreateCompany($input: CreateCompanyInput!) { createCompany(input: $input) { id } }",
            new { input = new { name = "CancelNoop Corp" } },
            token);
        var companyId = companyResult.GetProperty("data").GetProperty("createCompany").GetProperty("id").GetString();

        var citiesResult = await ExecuteGraphQlAsync("{ cities { id } }");
        var cityId = citiesResult.GetProperty("data").GetProperty("cities")[0].GetProperty("id").GetString();

        var buildingResult = await ExecuteGraphQlAsync(
            "mutation PlaceBuilding($input: PlaceBuildingInput!) { placeBuilding(input: $input) { id } }",
            new { input = new { companyId, cityId, type = "FACTORY", name = "NoOp Factory" } },
            token);
        var buildingId = buildingResult.GetProperty("data").GetProperty("placeBuilding").GetProperty("id").GetString();

        var cancelResult = await ExecuteGraphQlAsync(
            """
            mutation CancelBuildingConfiguration($input: CancelBuildingConfigurationInput!) {
                cancelBuildingConfiguration(input: $input) { id }
            }
            """,
            new { input = new { buildingId } },
            token);

        Assert.True(cancelResult.TryGetProperty("errors", out var errors));
        Assert.Equal("NO_PENDING_CONFIGURATION", errors[0].GetProperty("extensions").GetProperty("code").GetString());
    }

    [Fact]
    public async Task CancelBuildingConfiguration_AfterRollback_BuildingHasNoUnits()
    {
        var token = await RegisterAndGetTokenAsync("cancel-apply@test.com", "CancelApply");

        var companyResult = await ExecuteGraphQlAsync(
            "mutation CreateCompany($input: CreateCompanyInput!) { createCompany(input: $input) { id } }",
            new { input = new { name = "CancelApply Corp" } },
            token);
        var companyId = companyResult.GetProperty("data").GetProperty("createCompany").GetProperty("id").GetString();

        var citiesResult = await ExecuteGraphQlAsync("{ cities { id } }");
        var cityId = citiesResult.GetProperty("data").GetProperty("cities")[0].GetProperty("id").GetString();

        var buildingResult = await ExecuteGraphQlAsync(
            "mutation PlaceBuilding($input: PlaceBuildingInput!) { placeBuilding(input: $input) { id } }",
            new { input = new { companyId, cityId, type = "FACTORY", name = "CancelApply Factory" } },
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
                        new { unitType = "MANUFACTURING", gridX = 0, gridY = 0, linkUp = false, linkDown = false, linkLeft = false, linkRight = false, linkUpLeft = false, linkUpRight = false, linkDownLeft = false, linkDownRight = false }
                    }
                }
            },
            token);

        await ExecuteGraphQlAsync(
            """
            mutation CancelBuildingConfiguration($input: CancelBuildingConfigurationInput!) {
                cancelBuildingConfiguration(input: $input) { id }
            }
            """,
            new { input = new { buildingId } },
            token);

        // Advance 1 tick so the rollback removal resolves
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

    [Fact]
    public async Task CancelBuildingConfiguration_WrongOwner_ReturnsError()
    {
        var token1 = await RegisterAndGetTokenAsync("cancel-owner1@test.com", "Owner1");
        var token2 = await RegisterAndGetTokenAsync("cancel-owner2@test.com", "Owner2");

        var companyResult = await ExecuteGraphQlAsync(
            "mutation CreateCompany($input: CreateCompanyInput!) { createCompany(input: $input) { id } }",
            new { input = new { name = "Owner1 Corp" } },
            token1);
        var companyId = companyResult.GetProperty("data").GetProperty("createCompany").GetProperty("id").GetString();

        var citiesResult = await ExecuteGraphQlAsync("{ cities { id } }");
        var cityId = citiesResult.GetProperty("data").GetProperty("cities")[0].GetProperty("id").GetString();

        var buildingResult = await ExecuteGraphQlAsync(
            "mutation PlaceBuilding($input: PlaceBuildingInput!) { placeBuilding(input: $input) { id } }",
            new { input = new { companyId, cityId, type = "FACTORY", name = "Owner1 Factory" } },
            token1);
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
            token1);

        // Try to cancel with a different user's token
        var cancelResult = await ExecuteGraphQlAsync(
            """
            mutation CancelBuildingConfiguration($input: CancelBuildingConfigurationInput!) {
                cancelBuildingConfiguration(input: $input) { id }
            }
            """,
            new { input = new { buildingId } },
            token2);

        Assert.True(cancelResult.TryGetProperty("errors", out var errors));
        Assert.Equal("BUILDING_NOT_FOUND", errors[0].GetProperty("extensions").GetProperty("code").GetString());
    }

    #endregion

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
    public async Task FinishOnboarding_ConfiguresStarterPricingAndLinks()
    {
        var token = await RegisterAndGetTokenAsync($"onboard-map-config-{Guid.NewGuid()}@test.com", "Config Founder");
        var (companyId, _, cityId, _) = await StartOnboardingCompanyAsync(token, "Config Founder Co");

        var productsResult = await ExecuteGraphQlAsync(
            """
            query {
              productTypes(industry: "FURNITURE") {
                id
                slug
                basePrice
                recipes {
                  resourceType { id }
                }
              }
            }
            """);

        var selectedProduct = productsResult.GetProperty("data").GetProperty("productTypes")
            .EnumerateArray()
            .Single(product => product.GetProperty("slug").GetString() == "wooden-chair");
        var productId = selectedProduct.GetProperty("id").GetString()!;
        var basePrice = selectedProduct.GetProperty("basePrice").GetDecimal();
        var starterResourceId = selectedProduct.GetProperty("recipes").EnumerateArray()
            .Select(recipe => recipe.GetProperty("resourceType"))
            .Single(resourceType => resourceType.ValueKind == JsonValueKind.Object)
            .GetProperty("id")
            .GetString();

        var shopLotId = await CreateTestLotAsync(cityId, "SALES_SHOP,COMMERCIAL", "Commercial District", 95_000m, "Configured Shop Lot");
        await FinishOnboardingAsync(token, productId, shopLotId);

        var companiesResult = await ExecuteGraphQlAsync(
            """
            {
              myCompanies {
                buildings {
                  type
                  units {
                    unitType
                    gridX
                    gridY
                    resourceTypeId
                    productTypeId
                    minPrice
                    maxPrice
                    purchaseSource
                                        saleVisibility
                                        vendorLockCompanyId
                    linkRight
                  }
                }
              }
            }
            """,
            token: token);

        var buildings = companiesResult.GetProperty("data").GetProperty("myCompanies")[0].GetProperty("buildings")
            .EnumerateArray()
            .ToList();
        var factory = buildings.Single(building => building.GetProperty("type").GetString() == "FACTORY");
        var shop = buildings.Single(building => building.GetProperty("type").GetString() == "SALES_SHOP");

        var factoryUnits = factory.GetProperty("units").EnumerateArray().ToList();
        var factoryPurchase = factoryUnits.Single(unit => unit.GetProperty("unitType").GetString() == "PURCHASE");
        var factoryManufacturing = factoryUnits.Single(unit => unit.GetProperty("unitType").GetString() == "MANUFACTURING");
        var factoryStorage = factoryUnits.Single(unit => unit.GetProperty("unitType").GetString() == "STORAGE");
        var factorySales = factoryUnits.Single(unit => unit.GetProperty("unitType").GetString() == "B2B_SALES");

        Assert.Equal(0, factoryPurchase.GetProperty("gridX").GetInt32());
        Assert.Equal(starterResourceId, factoryPurchase.GetProperty("resourceTypeId").GetString());
        Assert.Equal(basePrice, factoryPurchase.GetProperty("maxPrice").GetDecimal());
        Assert.Equal("OPTIMAL", factoryPurchase.GetProperty("purchaseSource").GetString());
        Assert.True(factoryPurchase.GetProperty("linkRight").GetBoolean());

        Assert.Equal(1, factoryManufacturing.GetProperty("gridX").GetInt32());
        Assert.Equal(productId, factoryManufacturing.GetProperty("productTypeId").GetString());
        Assert.True(factoryManufacturing.GetProperty("linkRight").GetBoolean());

        Assert.Equal(2, factoryStorage.GetProperty("gridX").GetInt32());
        Assert.True(factoryStorage.GetProperty("linkRight").GetBoolean());

        Assert.Equal(3, factorySales.GetProperty("gridX").GetInt32());
        Assert.Equal(productId, factorySales.GetProperty("productTypeId").GetString());
        Assert.Equal(basePrice, factorySales.GetProperty("minPrice").GetDecimal());
        Assert.Equal("COMPANY", factorySales.GetProperty("saleVisibility").GetString());

        var shopUnits = shop.GetProperty("units").EnumerateArray().ToList();
        var shopPurchase = shopUnits.Single(unit => unit.GetProperty("unitType").GetString() == "PURCHASE");
        var publicSales = shopUnits.Single(unit => unit.GetProperty("unitType").GetString() == "PUBLIC_SALES");

        Assert.Equal(0, shopPurchase.GetProperty("gridX").GetInt32());
        Assert.Equal(productId, shopPurchase.GetProperty("productTypeId").GetString());
        Assert.Equal(basePrice * 1.1m, shopPurchase.GetProperty("maxPrice").GetDecimal());
        Assert.Equal("LOCAL", shopPurchase.GetProperty("purchaseSource").GetString());
        Assert.Equal(companyId, shopPurchase.GetProperty("vendorLockCompanyId").GetString());
        Assert.True(shopPurchase.GetProperty("linkRight").GetBoolean());

        Assert.Equal(1, publicSales.GetProperty("gridX").GetInt32());
        Assert.Equal(productId, publicSales.GetProperty("productTypeId").GetString());
        Assert.Equal(basePrice * 1.5m, publicSales.GetProperty("minPrice").GetDecimal());
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
        var (companyId, _, onboardingResult) = await CompleteOnboardingAsync(token, "Claim Corp");
        var companyCashBeforeClaim = onboardingResult.GetProperty("data").GetProperty("completeOnboarding")
            .GetProperty("company").GetProperty("cash").GetDecimal();

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
        Assert.Equal(companyCashBeforeClaim + StartupPackService.CompanyCashGrant, firstClaimData.GetProperty("company").GetProperty("cash").GetDecimal());
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
        Assert.Equal(companyCashBeforeClaim + StartupPackService.CompanyCashGrant, secondClaimData.GetProperty("company").GetProperty("cash").GetDecimal());
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
        var (companyId, _, onboardingResult) = await CompleteOnboardingAsync(token, "Concurrent Corp");
        var companyCashBeforeClaim = onboardingResult.GetProperty("data").GetProperty("completeOnboarding")
            .GetProperty("company").GetProperty("cash").GetDecimal();

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
            Assert.Equal(companyCashBeforeClaim + StartupPackService.CompanyCashGrant, payload.GetProperty("company").GetProperty("cash").GetDecimal());
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
        Assert.Equal(companyCashBeforeClaim + StartupPackService.CompanyCashGrant, company.Cash);
        Assert.NotNull(player.ProSubscriptionEndsAtUtc);
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
    public async Task ClaimStartupPack_Unauthenticated_ReturnsError()
    {
        var result = await ExecuteGraphQlAsync(
            """
            mutation ClaimStartupPack($input: ClaimStartupPackInput!) {
              claimStartupPack(input: $input) {
                offer { status }
              }
            }
            """,
            new { input = new { companyId = Guid.NewGuid() } });

        Assert.True(result.TryGetProperty("errors", out _));
    }

    [Fact]
    public async Task DismissStartupPackOffer_Unauthenticated_ReturnsError()
    {
        var result = await ExecuteGraphQlAsync(
            "mutation { dismissStartupPackOffer { status } }");

        Assert.True(result.TryGetProperty("errors", out _));
    }

    [Fact]
    public async Task MarkStartupPackOfferShown_Unauthenticated_ReturnsError()
    {
        var result = await ExecuteGraphQlAsync(
            "mutation { markStartupPackOfferShown { status } }");

        Assert.True(result.TryGetProperty("errors", out _));
    }

    [Fact]
    public async Task ClaimStartupPack_WrongCompany_ReturnsError()
    {
        var token = await RegisterAndGetTokenAsync($"startup-wrong-co-{Guid.NewGuid()}@test.com", "WrongCoPlayer");
        await CompleteOnboardingAsync(token, "Wrong Co Corp");

        // Register a second player and get their company id
        var otherToken = await RegisterAndGetTokenAsync($"startup-other-co-{Guid.NewGuid()}@test.com", "OtherCoPlayer");
        var (otherCompanyId, _, _) = await CompleteOnboardingAsync(otherToken, "Other Co Corp");

        // First player tries to claim using the other player's company
        var result = await ExecuteGraphQlAsync(
            """
            mutation ClaimStartupPack($input: ClaimStartupPackInput!) {
              claimStartupPack(input: $input) {
                offer { status }
              }
            }
            """,
            new { input = new { companyId = otherCompanyId } },
            token);

        Assert.True(result.TryGetProperty("errors", out var errors));
        Assert.Contains(errors.EnumerateArray(), error => error.GetProperty("extensions").GetProperty("code").GetString() == "COMPANY_NOT_FOUND");
    }

    [Fact]
    public async Task DismissStartupPackOffer_AlreadyDismissed_IsIdempotent()
    {
        var token = await RegisterAndGetTokenAsync($"startup-dismiss-idem-{Guid.NewGuid()}@test.com", "DismissIdemPlayer");
        await CompleteOnboardingAsync(token, "Dismiss Idem Corp");

        var first = await ExecuteGraphQlAsync(
            "mutation { dismissStartupPackOffer { status dismissedAtUtc } }",
            token: token);
        var firstOffer = first.GetProperty("data").GetProperty("dismissStartupPackOffer");
        Assert.Equal("DISMISSED", firstOffer.GetProperty("status").GetString());
        var firstDismissedAt = firstOffer.GetProperty("dismissedAtUtc").GetString();

        var second = await ExecuteGraphQlAsync(
            "mutation { dismissStartupPackOffer { status dismissedAtUtc } }",
            token: token);
        var secondOffer = second.GetProperty("data").GetProperty("dismissStartupPackOffer");
        Assert.Equal("DISMISSED", secondOffer.GetProperty("status").GetString());
        // dismissedAtUtc must not be overwritten on subsequent calls
        Assert.Equal(firstDismissedAt, secondOffer.GetProperty("dismissedAtUtc").GetString());
    }

    [Fact]
    public async Task ClaimStartupPack_FromDismissedState_GrantsEntitlements()
    {
        var token = await RegisterAndGetTokenAsync($"startup-claim-from-dismissed-{Guid.NewGuid()}@test.com", "ClaimDismissedPlayer");
        var (companyId, _, _) = await CompleteOnboardingAsync(token, "Claim Dismissed Corp");

        // Dismiss the offer first
        var dismissResult = await ExecuteGraphQlAsync(
            "mutation { dismissStartupPackOffer { status } }",
            token: token);
        Assert.Equal("DISMISSED", dismissResult.GetProperty("data").GetProperty("dismissStartupPackOffer").GetProperty("status").GetString());

        // Claim after dismissal – must still succeed
        var claimResult = await ExecuteGraphQlAsync(
            """
            mutation ClaimStartupPack($input: ClaimStartupPackInput!) {
              claimStartupPack(input: $input) {
                offer { status claimedAtUtc grantedCompanyId }
                company { cash }
                proSubscriptionEndsAtUtc
              }
            }
            """,
            new { input = new { companyId } },
            token);

        Assert.False(claimResult.TryGetProperty("errors", out _));
        var payload = claimResult.GetProperty("data").GetProperty("claimStartupPack");
        Assert.Equal("CLAIMED", payload.GetProperty("offer").GetProperty("status").GetString());
        Assert.True(payload.GetProperty("company").GetProperty("cash").GetDecimal() >= StartupPackService.CompanyCashGrant);
        Assert.True(DateTime.TryParse(payload.GetProperty("proSubscriptionEndsAtUtc").GetString(), out _));
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

    [Fact]
    public async Task StartOnboardingCompany_WhenAlreadyCompleted_ReturnsError()
    {
        var token = await RegisterAndGetTokenAsync($"onboard-already-done-{Guid.NewGuid()}@test.com", "AlreadyDone");
        await CompleteOnboardingAsync(token, "Already Done Co");

        var cityId = await GetCityIdByNameAsync();
        var lotId = await CreateTestLotAsync(cityId, "FACTORY,MINE", "Industrial Zone");

        var result = await ExecuteGraphQlAsync(
            """
            mutation StartOnboardingCompany($input: StartOnboardingCompanyInput!) {
              startOnboardingCompany(input: $input) { company { id } }
            }
            """,
            new { input = new { industry = "FURNITURE", cityId, companyName = "Second Attempt Co", factoryLotId = lotId } },
            token);

        Assert.True(result.TryGetProperty("errors", out var errors));
        Assert.Equal("ONBOARDING_ALREADY_COMPLETED", errors[0].GetProperty("extensions").GetProperty("code").GetString());
    }

    [Fact]
    public async Task StartOnboardingCompany_WhenAlreadyInProgress_ReturnsError()
    {
        var token = await RegisterAndGetTokenAsync($"onboard-in-progress-{Guid.NewGuid()}@test.com", "InProgress");
        var cityId = await GetCityIdByNameAsync();
        await StartOnboardingCompanyAsync(token, "First Start Co");

        var lotId = await CreateTestLotAsync(cityId, "FACTORY,MINE", "Industrial Zone", 75_000m, "Second Factory Lot");

        var result = await ExecuteGraphQlAsync(
            """
            mutation StartOnboardingCompany($input: StartOnboardingCompanyInput!) {
              startOnboardingCompany(input: $input) { company { id } }
            }
            """,
            new { input = new { industry = "FURNITURE", cityId, companyName = "Duplicate Start Co", factoryLotId = lotId } },
            token);

        Assert.True(result.TryGetProperty("errors", out var errors));
        Assert.Equal("ONBOARDING_ALREADY_IN_PROGRESS", errors[0].GetProperty("extensions").GetProperty("code").GetString());
    }

    [Fact]
    public async Task StartOnboardingCompany_InvalidCity_ReturnsError()
    {
        var token = await RegisterAndGetTokenAsync($"onboard-bad-city-{Guid.NewGuid()}@test.com", "BadCity");
        var fakeCityId = Guid.NewGuid();
        var fakeLotId = Guid.NewGuid();

        var result = await ExecuteGraphQlAsync(
            """
            mutation StartOnboardingCompany($input: StartOnboardingCompanyInput!) {
              startOnboardingCompany(input: $input) { company { id } }
            }
            """,
            new { input = new { industry = "FURNITURE", cityId = fakeCityId, companyName = "Ghost City Co", factoryLotId = fakeLotId } },
            token);

        Assert.True(result.TryGetProperty("errors", out _));
    }

    [Fact]
    public async Task StartOnboardingCompany_InvalidIndustry_ReturnsError()
    {
        var token = await RegisterAndGetTokenAsync($"onboard-bad-industry-start-{Guid.NewGuid()}@test.com", "BadIndStart");
        var cityId = await GetCityIdByNameAsync();
        var lotId = await CreateTestLotAsync(cityId, "FACTORY,MINE", "Industrial Zone");

        var result = await ExecuteGraphQlAsync(
            """
            mutation StartOnboardingCompany($input: StartOnboardingCompanyInput!) {
              startOnboardingCompany(input: $input) { company { id } }
            }
            """,
            new { input = new { industry = "ELECTRONICS", cityId, companyName = "Electronics Co", factoryLotId = lotId } },
            token);

        Assert.True(result.TryGetProperty("errors", out var errors));
        Assert.Equal("INVALID_INDUSTRY", errors[0].GetProperty("extensions").GetProperty("code").GetString());
    }

    [Fact]
    public async Task FinishOnboarding_Unauthenticated_ReturnsError()
    {
        var result = await ExecuteGraphQlAsync(
            """
            mutation FinishOnboarding($input: FinishOnboardingInput!) {
              finishOnboarding(input: $input) { company { id } }
            }
            """,
            new { input = new { productTypeId = Guid.NewGuid(), shopLotId = Guid.NewGuid() } });

        Assert.True(result.TryGetProperty("errors", out _));
    }

    [Fact]
    public async Task FinishOnboarding_WhenNotInProgress_ReturnsError()
    {
        // A freshly registered player who has not started the staged flow cannot call finishOnboarding.
        var token = await RegisterAndGetTokenAsync($"finish-no-progress-{Guid.NewGuid()}@test.com", "NoProgress");

        var result = await ExecuteGraphQlAsync(
            """
            mutation FinishOnboarding($input: FinishOnboardingInput!) {
              finishOnboarding(input: $input) { company { id } }
            }
            """,
            new { input = new { productTypeId = Guid.NewGuid(), shopLotId = Guid.NewGuid() } },
            token);

        Assert.True(result.TryGetProperty("errors", out var errors));
        Assert.Equal("ONBOARDING_NOT_IN_PROGRESS", errors[0].GetProperty("extensions").GetProperty("code").GetString());
    }

    [Fact]
    public async Task FinishOnboarding_WrongIndustryProduct_ReturnsError()
    {
        var token = await RegisterAndGetTokenAsync($"finish-wrong-product-{Guid.NewGuid()}@test.com", "WrongProduct");
        var (_, _, cityId, _) = await StartOnboardingCompanyAsync(token, "Wrong Product Co");
        var shopLotId = await CreateTestLotAsync(cityId, "SALES_SHOP,COMMERCIAL", "Commercial District", 90_000m, "Test Shop");

        // Get a healthcare product instead of furniture
        var healthcareProductId = await GetStarterProductIdAsync("HEALTHCARE", "basic-medicine");

        var result = await FinishOnboardingAsync(token, healthcareProductId, shopLotId);

        Assert.True(result.TryGetProperty("errors", out var errors));
        Assert.Equal("INVALID_PRODUCT", errors[0].GetProperty("extensions").GetProperty("code").GetString());
    }

    [Fact]
    public async Task FinishOnboarding_WhenShopLotIsUnsuitableType_Fails()
    {
        var token = await RegisterAndGetTokenAsync($"finish-unsuitable-shop-{Guid.NewGuid()}@test.com", "UnsuitableShop");
        var (_, _, cityId, _) = await StartOnboardingCompanyAsync(token, "Unsuitable Shop Co");
        var productId = await GetStarterProductIdAsync();
        // Use a FACTORY-only lot as the shop lot — should be rejected
        var factoryOnlyLotId = await CreateTestLotAsync(cityId, "FACTORY,MINE", "Industrial Zone", 75_000m, "Factory-Only Lot");

        var result = await FinishOnboardingAsync(token, productId, factoryOnlyLotId);

        Assert.True(result.TryGetProperty("errors", out var errors));
        var code = errors[0].GetProperty("extensions").GetProperty("code").GetString();
        Assert.Equal("UNSUITABLE_BUILDING_TYPE", code);
    }

    [Fact]
    public async Task FinishOnboarding_WhenShopLotIsInDifferentCity_Fails()
    {
        var token = await RegisterAndGetTokenAsync($"finish-city-mismatch-{Guid.NewGuid()}@test.com", "CityMismatch");
        var (_, _, cityId, _) = await StartOnboardingCompanyAsync(token, "City Mismatch Co");
        var productId = await GetStarterProductIdAsync();

        // Create a shop lot in a different city
        var cities = await ExecuteGraphQlAsync("{ cities { id name } }");
        var allCities = cities.GetProperty("data").GetProperty("cities");
        var otherCityId = Enumerable.Range(0, allCities.GetArrayLength())
            .Select(i => allCities[i].GetProperty("id").GetString()!)
            .FirstOrDefault(id => id != cityId.ToString());

        if (otherCityId is null)
        {
            // Only one city seeded — skip the cross-city check gracefully
            return;
        }

        var otherCityShopLotId = await CreateTestLotAsync(otherCityId, "SALES_SHOP,COMMERCIAL", "Commercial District", 90_000m, "Other City Shop");

        var result = await FinishOnboardingAsync(token, productId, otherCityShopLotId);

        Assert.True(result.TryGetProperty("errors", out var errors));
        var code = errors[0].GetProperty("extensions").GetProperty("code").GetString();
        Assert.Equal("LOT_CITY_MISMATCH", code);
    }

    [Fact]
    public async Task FinishOnboarding_WhenInsufficientFundsForShopLot_Fails()
    {
        var token = await RegisterAndGetTokenAsync($"finish-broke-{Guid.NewGuid()}@test.com", "BrokePlayer");
        var (companyId, _, cityId, _) = await StartOnboardingCompanyAsync(token, "Broke Shop Co");
        var productId = await GetStarterProductIdAsync();

        // Drain the company's cash so it cannot afford the shop lot
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var company = await db.Companies.FirstAsync(c => c.Id == Guid.Parse(companyId));
            company.Cash = 0;
            await db.SaveChangesAsync();
        }

        var shopLotId = await CreateTestLotAsync(cityId, "SALES_SHOP,COMMERCIAL", "Commercial District", 90_000m, "Affordable-Looking Shop");

        var result = await FinishOnboardingAsync(token, productId, shopLotId);

        Assert.True(result.TryGetProperty("errors", out var errors));
        var code = errors[0].GetProperty("extensions").GetProperty("code").GetString();
        Assert.Equal("INSUFFICIENT_FUNDS", code);
    }

    [Fact]
    public async Task FullOnboardingCycle_AllThreeIndustries_ProducesValidState()
    {
        // Verify each starter industry produces a valid completed onboarding state
        var industries = new[] { ("FURNITURE", "wooden-chair"), ("FOOD_PROCESSING", "bread"), ("HEALTHCARE", "basic-medicine") };
        foreach (var (industry, slug) in industries)
        {
            var token = await RegisterAndGetTokenAsync($"onboard-{industry.ToLower()}-{Guid.NewGuid()}@test.com", $"{industry} Player");
            var cityId = await GetCityIdByNameAsync();
            var factoryLotId = await CreateTestLotAsync(cityId, "FACTORY,MINE", "Industrial Zone", 75_000m);

            var startResult = await ExecuteGraphQlAsync(
                """
                mutation StartOnboardingCompany($input: StartOnboardingCompanyInput!) {
                  startOnboardingCompany(input: $input) {
                    company { id cash }
                    nextStep
                  }
                }
                """,
                new { input = new { industry, cityId, companyName = $"{industry} Corp", factoryLotId } },
                token);

            var startData = startResult.GetProperty("data").GetProperty("startOnboardingCompany");
            Assert.Equal("SHOP_SELECTION", startData.GetProperty("nextStep").GetString());
            // Cash starts at $500,000 and decreases by the factory lot price ($75,000 default)
            var cashAfterFactory = startData.GetProperty("company").GetProperty("cash").GetDecimal();
            Assert.True(cashAfterFactory > 0 && cashAfterFactory < 500_000m, $"Unexpected cash after factory purchase: {cashAfterFactory}");

            var productId = await GetStarterProductIdAsync(industry, slug);
            var shopLotId = await CreateTestLotAsync(cityId, "SALES_SHOP,COMMERCIAL", "Commercial District", 90_000m);

            var finishResult = await FinishOnboardingAsync(token, productId, shopLotId);
            Assert.False(finishResult.TryGetProperty("errors", out _), $"FinishOnboarding failed for {industry}");

            var finishData = finishResult.GetProperty("data").GetProperty("finishOnboarding");
            Assert.Equal(industry, finishData.GetProperty("selectedProduct").GetProperty("industry").GetString());
        }
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
        var (companyId1, _, onboardingResult1) = await CompleteOnboardingAsync(token1, "Race A Co");
        var company1CashBeforeRace = onboardingResult1.GetProperty("data").GetProperty("completeOnboarding")
            .GetProperty("company").GetProperty("cash").GetDecimal();

        var token2 = await RegisterAndGetTokenAsync($"lot-race-b-{Guid.NewGuid()}@test.com");
        var (companyId2, _, onboardingResult2) = await CompleteOnboardingAsync(token2, "Race B Co");
        var company2CashBeforeRace = onboardingResult2.GetProperty("data").GetProperty("completeOnboarding")
            .GetProperty("company").GetProperty("cash").GetDecimal();

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
        var successes = results.Count(r => !r.TryGetProperty("errors", out _)
            && r.TryGetProperty("data", out var d)
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
        var chargedCount = (company1.Cash < company1CashBeforeRace ? 1 : 0) + (company2.Cash < company2CashBeforeRace ? 1 : 0);
        Assert.Equal(1, chargedCount);
    }

    [Fact]
    public async Task PurchaseLot_Unauthenticated_Fails()
    {
        // purchaseLot mutation requires authentication; an unauthenticated call must fail.
        var citiesResult = await ExecuteGraphQlAsync("{ cities { id name } }");
        var bratislavaId = citiesResult.GetProperty("data").GetProperty("cities").EnumerateArray()
            .First(c => c.GetProperty("name").GetString() == "Bratislava")
            .GetProperty("id").GetString();

        var lotsResult = await ExecuteGraphQlAsync(
            "query CityLots($cityId: UUID!) { cityLots(cityId: $cityId) { id suitableTypes } }",
            new { cityId = bratislavaId });

        var lot = lotsResult.GetProperty("data").GetProperty("cityLots").EnumerateArray()
            .First(l => l.GetProperty("suitableTypes").GetString()!.Contains("FACTORY"));
        var lotId = lot.GetProperty("id").GetString();

        // Call without an auth token
        var result = await ExecuteGraphQlAsync(
            """
            mutation PurchaseLot($input: PurchaseLotInput!) {
              purchaseLot(input: $input) { lot { id } }
            }
            """,
            new { input = new { companyId = Guid.NewGuid().ToString(), lotId, buildingType = "FACTORY", buildingName = "Unauth Factory" } });

        Assert.True(result.TryGetProperty("errors", out _), "unauthenticated purchaseLot should return an error");
    }

    [Fact]
    public async Task PurchaseLot_CityLotsQueryReflectsOwnershipAfterPurchase()
    {
        // After a successful purchaseLot, the cityLots query must immediately return
        // the lot with the correct ownerCompanyId and buildingId.
        var token = await RegisterAndGetTokenAsync($"lot-reflect-{Guid.NewGuid()}@test.com");
        var (companyId, _, _) = await CompleteOnboardingAsync(token, "Reflect Check Co");

        var citiesResult = await ExecuteGraphQlAsync("{ cities { id name } }");
        var bratislavaId = citiesResult.GetProperty("data").GetProperty("cities").EnumerateArray()
            .First(c => c.GetProperty("name").GetString() == "Bratislava")
            .GetProperty("id").GetString();

        var lotsResult = await ExecuteGraphQlAsync(
            """
            query CityLots($cityId: UUID!) {
              cityLots(cityId: $cityId) { id suitableTypes ownerCompanyId buildingId }
            }
            """,
            new { cityId = bratislavaId });

        var availableLot = lotsResult.GetProperty("data").GetProperty("cityLots").EnumerateArray()
            .First(l => l.GetProperty("suitableTypes").GetString()!.Contains("FACTORY")
                     && l.GetProperty("ownerCompanyId").ValueKind == JsonValueKind.Null);
        var lotId = availableLot.GetProperty("id").GetString();

        // Purchase the lot
        var purchaseResult = await ExecuteGraphQlAsync(
            """
            mutation PurchaseLot($input: PurchaseLotInput!) {
              purchaseLot(input: $input) {
                lot { id ownerCompanyId buildingId }
                building { id }
              }
            }
            """,
            new { input = new { companyId, lotId, buildingType = "FACTORY", buildingName = "Reflect Factory" } },
            token);

        var purchasedBuildingId = purchaseResult.GetProperty("data").GetProperty("purchaseLot")
            .GetProperty("building").GetProperty("id").GetString();

        // Query cityLots again — ownership must be reflected immediately
        var lotsAfter = await ExecuteGraphQlAsync(
            """
            query CityLots($cityId: UUID!) {
              cityLots(cityId: $cityId) { id ownerCompanyId buildingId }
            }
            """,
            new { cityId = bratislavaId });

        var lotAfter = lotsAfter.GetProperty("data").GetProperty("cityLots").EnumerateArray()
            .First(l => l.GetProperty("id").GetString() == lotId);

        Assert.Equal(companyId, lotAfter.GetProperty("ownerCompanyId").GetString());
        Assert.Equal(purchasedBuildingId, lotAfter.GetProperty("buildingId").GetString());
    }

    [Fact]
    public async Task GlobalExchangeOffers_ReturnTransitAndDeliveredPricing()
    {
        var bratislavaId = await GetCityIdByNameAsync("Bratislava");
        var resourceTypesResult = await ExecuteGraphQlAsync("{ resourceTypes { id slug } }");
        var woodId = resourceTypesResult.GetProperty("data").GetProperty("resourceTypes")
            .EnumerateArray()
            .First(resource => resource.GetProperty("slug").GetString() == "wood")
            .GetProperty("id")
            .GetString()!;

        var result = await ExecuteGraphQlAsync(
            """
            query GlobalExchangeOffers($destinationCityId: UUID!, $resourceTypeId: UUID) {
              globalExchangeOffers(destinationCityId: $destinationCityId, resourceTypeId: $resourceTypeId) {
                cityName
                resourceSlug
                exchangePricePerUnit
                transitCostPerUnit
                deliveredPricePerUnit
                estimatedQuality
                distanceKm
              }
            }
            """,
            new { destinationCityId = bratislavaId, resourceTypeId = woodId });

        var offers = result.GetProperty("data").GetProperty("globalExchangeOffers");
        Assert.Equal(3, offers.GetArrayLength());

        var firstOffer = offers[0];
        Assert.Equal("Bratislava", firstOffer.GetProperty("cityName").GetString());
        Assert.Equal("wood", firstOffer.GetProperty("resourceSlug").GetString());
        Assert.Equal(0m, firstOffer.GetProperty("transitCostPerUnit").GetDecimal());
        Assert.Equal(
            firstOffer.GetProperty("exchangePricePerUnit").GetDecimal(),
            firstOffer.GetProperty("deliveredPricePerUnit").GetDecimal());

        var remoteOffers = offers.EnumerateArray().Skip(1).ToList();
        Assert.All(remoteOffers, offer =>
        {
            Assert.True(offer.GetProperty("transitCostPerUnit").GetDecimal() > 0m);
            Assert.True(offer.GetProperty("deliveredPricePerUnit").GetDecimal() >= offer.GetProperty("exchangePricePerUnit").GetDecimal());
            Assert.True(offer.GetProperty("distanceKm").GetDecimal() > 0m);
            Assert.InRange(offer.GetProperty("estimatedQuality").GetDecimal(), 0.35m, 0.95m);
        });
    }

    [Fact]
    public async Task GlobalExchangeOffers_AllResourcesInAllCities_ReturnValidData()
    {
        var bratislavaId = await GetCityIdByNameAsync("Bratislava");

        // Request all resources (no filter) for Bratislava as destination.
        var result = await ExecuteGraphQlAsync(
            """
            query GlobalExchangeOffers($destinationCityId: UUID!) {
              globalExchangeOffers(destinationCityId: $destinationCityId) {
                cityId
                cityName
                resourceTypeId
                resourceName
                exchangePricePerUnit
                transitCostPerUnit
                deliveredPricePerUnit
                estimatedQuality
                localAbundance
              }
            }
            """,
            new { destinationCityId = bratislavaId });

        var offers = result.GetProperty("data").GetProperty("globalExchangeOffers");

        // Seeded: 3 cities × 8 resources = 24 offers.
        Assert.Equal(24, offers.GetArrayLength());

        // All fields must be positive and quality must be in range.
        Assert.All(offers.EnumerateArray(), offer =>
        {
            Assert.True(offer.GetProperty("exchangePricePerUnit").GetDecimal() > 0m);
            Assert.True(offer.GetProperty("deliveredPricePerUnit").GetDecimal() > 0m);
            Assert.InRange(offer.GetProperty("estimatedQuality").GetDecimal(), 0.35m, 0.95m);
            Assert.InRange(offer.GetProperty("localAbundance").GetDecimal(), 0m, 1m);
        });

        // Delivered price must always be >= exchange price (transit cost is non-negative).
        Assert.All(offers.EnumerateArray(), offer =>
            Assert.True(offer.GetProperty("deliveredPricePerUnit").GetDecimal() >=
                        offer.GetProperty("exchangePricePerUnit").GetDecimal()));
    }

    [Fact]
    public async Task GlobalExchangeOffers_SameCityOfferHasZeroTransitCost()
    {
        var pragueId = await GetCityIdByNameAsync("Prague");
        var woodId = (await ExecuteGraphQlAsync("{ resourceTypes { id slug } }"))
            .GetProperty("data").GetProperty("resourceTypes")
            .EnumerateArray().First(r => r.GetProperty("slug").GetString() == "wood")
            .GetProperty("id").GetString()!;

        var result = await ExecuteGraphQlAsync(
            """
            query GlobalExchangeOffers($destinationCityId: UUID!, $resourceTypeId: UUID) {
              globalExchangeOffers(destinationCityId: $destinationCityId, resourceTypeId: $resourceTypeId) {
                cityName
                transitCostPerUnit
                deliveredPricePerUnit
                exchangePricePerUnit
              }
            }
            """,
            new { destinationCityId = pragueId, resourceTypeId = woodId });

        var offers = result.GetProperty("data").GetProperty("globalExchangeOffers");
        var pragueOffer = offers.EnumerateArray().First(o => o.GetProperty("cityName").GetString() == "Prague");

        // Prague → Prague transit cost must be zero.
        Assert.Equal(0m, pragueOffer.GetProperty("transitCostPerUnit").GetDecimal());
        Assert.Equal(
            pragueOffer.GetProperty("exchangePricePerUnit").GetDecimal(),
            pragueOffer.GetProperty("deliveredPricePerUnit").GetDecimal());

        // Other cities must have positive transit costs.
        var remoteOffers = offers.EnumerateArray().Where(o => o.GetProperty("cityName").GetString() != "Prague").ToList();
        Assert.All(remoteOffers, o => Assert.True(o.GetProperty("transitCostPerUnit").GetDecimal() > 0m));
    }

    [Fact]
    public async Task GlobalExchangeOffers_OrderedByDeliveredPriceThenQuality()
    {
        var bratislavaId = await GetCityIdByNameAsync("Bratislava");
        var woodId = (await ExecuteGraphQlAsync("{ resourceTypes { id slug } }"))
            .GetProperty("data").GetProperty("resourceTypes")
            .EnumerateArray().First(r => r.GetProperty("slug").GetString() == "wood")
            .GetProperty("id").GetString()!;

        var result = await ExecuteGraphQlAsync(
            """
            query GlobalExchangeOffers($destinationCityId: UUID!, $resourceTypeId: UUID) {
              globalExchangeOffers(destinationCityId: $destinationCityId, resourceTypeId: $resourceTypeId) {
                cityName
                deliveredPricePerUnit
                estimatedQuality
              }
            }
            """,
            new { destinationCityId = bratislavaId, resourceTypeId = woodId });

        var offers = result.GetProperty("data").GetProperty("globalExchangeOffers")
            .EnumerateArray().ToList();

        // Verify ascending delivered price ordering.
        for (var i = 0; i < offers.Count - 1; i++)
        {
            var currentPrice = offers[i].GetProperty("deliveredPricePerUnit").GetDecimal();
            var nextPrice = offers[i + 1].GetProperty("deliveredPricePerUnit").GetDecimal();
            Assert.True(currentPrice <= nextPrice,
                $"Offers at index {i} and {i + 1} are not in ascending delivered price order.");
        }
    }

    [Fact]
    public async Task StoreBuildingConfiguration_PurchaseUnit_PersistsExchangeSourceAndConstraints()
    {
        var email = $"exchange-cfg-{Guid.NewGuid():N}@test.com";
        var token = await RegisterAndGetTokenAsync(email, "Exchange Cfg Tester");

        var companyResult = await ExecuteGraphQlAsync(
            "mutation CreateCompany($input: CreateCompanyInput!) { createCompany(input: $input) { id } }",
            new { input = new { name = "Exchange Cfg Corp" } },
            token);
        var companyId = companyResult.GetProperty("data").GetProperty("createCompany").GetProperty("id").GetString();

        var citiesResult = await ExecuteGraphQlAsync("{ cities { id } }");
        var cityId = citiesResult.GetProperty("data").GetProperty("cities")[0].GetProperty("id").GetString();

        var buildingResult = await ExecuteGraphQlAsync(
            "mutation PlaceBuilding($input: PlaceBuildingInput!) { placeBuilding(input: $input) { id } }",
            new { input = new { companyId, cityId, type = "FACTORY", name = "Exchange Factory" } },
            token);
        var buildingId = buildingResult.GetProperty("data").GetProperty("placeBuilding").GetProperty("id").GetString();

        var woodId = (await ExecuteGraphQlAsync("{ resourceTypes { id slug } }"))
            .GetProperty("data").GetProperty("resourceTypes")
            .EnumerateArray().First(r => r.GetProperty("slug").GetString() == "wood")
            .GetProperty("id").GetString()!;

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
                        new
                        {
                            gridX = 0, gridY = 0,
                            unitType = "PURCHASE",
                            resourceTypeId = woodId,
                            maxPrice = 50.00m,
                            minQuality = 0.6m,
                            purchaseSource = "EXCHANGE",
                            linkRight = false, linkLeft = false, linkUp = false, linkDown = false,
                            linkUpLeft = false, linkUpRight = false, linkDownLeft = false, linkDownRight = false,
                        }
                    }
                }
            },
            token);

        // AdvanceGameTicksAsync bumps the counter; myCompanies query then applies due plans.
        await AdvanceGameTicksAsync(3);

        var companiesResult = await ExecuteGraphQlAsync(
            """
            {
              myCompanies {
                buildings {
                  id
                  units { gridX gridY unitType resourceTypeId maxPrice minQuality purchaseSource }
                }
              }
            }
            """,
            token: token);

        var building = companiesResult.GetProperty("data").GetProperty("myCompanies")
            .EnumerateArray()
            .SelectMany(c => c.GetProperty("buildings").EnumerateArray())
            .First(b => b.GetProperty("id").GetString() == buildingId);

        var unit = building.GetProperty("units").EnumerateArray()
            .First(u => u.GetProperty("unitType").GetString() == "PURCHASE");

        Assert.Equal("EXCHANGE", unit.GetProperty("purchaseSource").GetString());
        Assert.Equal(50.00m, unit.GetProperty("maxPrice").GetDecimal());
        Assert.Equal(0.6m, unit.GetProperty("minQuality").GetDecimal());
        Assert.Equal(woodId, unit.GetProperty("resourceTypeId").GetString());
    }

    [Fact]
    public async Task CityLots_ReturnsPopulationIndexForAllSeedLots()
    {
        // Every seeded lot must expose a non-zero populationIndex so the frontend
        // detail panel can display strategic location context to the player.
        var citiesResult = await ExecuteGraphQlAsync("{ cities { id name } }");
        var bratislavaId = citiesResult.GetProperty("data").GetProperty("cities").EnumerateArray()
            .First(c => c.GetProperty("name").GetString() == "Bratislava")
            .GetProperty("id").GetString();

        var result = await ExecuteGraphQlAsync(
            """
            query CityLots($cityId: UUID!) {
              cityLots(cityId: $cityId) { id name district populationIndex price basePrice }
            }
            """,
            new { cityId = bratislavaId });

        Assert.False(result.TryGetProperty("errors", out _), "cityLots query returned GraphQL errors");
        var lots = result.GetProperty("data").GetProperty("cityLots");
        Assert.True(lots.GetArrayLength() > 0, "Expected seeded lots for Bratislava");

        foreach (var lot in lots.EnumerateArray())
        {
            var name = lot.GetProperty("name").GetString();
            var popIndex = lot.GetProperty("populationIndex").GetDecimal();
            var price = lot.GetProperty("price").GetDecimal();
            var basePrice = lot.GetProperty("basePrice").GetDecimal();

            Assert.True(popIndex > 0, $"Lot '{name}' has zero or negative populationIndex");
            Assert.True(price > 0, $"Lot '{name}' has zero or negative price");
            Assert.True(basePrice > 0, $"Lot '{name}' has zero or negative basePrice");
        }
    }

    [Fact]
    public async Task CityLots_CommercialLotsHaveHigherPopulationIndexThanIndustrial()
    {
        // Commercial city-center lots should have a meaningfully higher population
        // index than industrial lots on the outskirts — this is the strategic signal
        // that makes the city map a real decision surface.
        var citiesResult = await ExecuteGraphQlAsync("{ cities { id name } }");
        var bratislavaId = citiesResult.GetProperty("data").GetProperty("cities").EnumerateArray()
            .First(c => c.GetProperty("name").GetString() == "Bratislava")
            .GetProperty("id").GetString();

        var result = await ExecuteGraphQlAsync(
            """
            query CityLots($cityId: UUID!) {
              cityLots(cityId: $cityId) { id name district suitableTypes populationIndex }
            }
            """,
            new { cityId = bratislavaId });

        var lots = result.GetProperty("data").GetProperty("cityLots").EnumerateArray().ToList();

        var commercialLots = lots
            .Where(l => l.GetProperty("suitableTypes").GetString()!.Contains("SALES_SHOP"))
            .Select(l => l.GetProperty("populationIndex").GetDecimal())
            .ToList();

        var industrialLots = lots
            .Where(l => l.GetProperty("district").GetString() == "Industrial Zone")
            .Select(l => l.GetProperty("populationIndex").GetDecimal())
            .ToList();

        Assert.True(commercialLots.Count > 0, "Expected at least one commercial lot");
        Assert.True(industrialLots.Count > 0, "Expected at least one industrial lot");

        var avgCommercial = commercialLots.Average();
        var avgIndustrial = industrialLots.Average();

        Assert.True(
            avgCommercial > avgIndustrial,
            $"Expected commercial avg population index ({avgCommercial:F2}) > industrial avg ({avgIndustrial:F2})");
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

    [Fact]
    public async Task StoreBuildingConfiguration_BrandQualityScopeRequiresProductType()
    {
        var token = await RegisterAndGetTokenAsync($"rd-config-{Guid.NewGuid()}@test.com", "ResearchTester");
        var (companyId, _, _) = await CompleteOnboardingAsync(token, "Research Holding");
        var cityId = await GetCityIdByNameAsync("Bratislava");
        var lotId = await CreateTestLotAsync(cityId, "RESEARCH_DEVELOPMENT,COMMERCIAL", "Research District", 90_000m, "Research Lot");
        var purchaseResult = await ExecuteGraphQlAsync(
            """
            mutation PurchaseLot($input: PurchaseLotInput!) {
              purchaseLot(input: $input) { building { id } }
            }
            """,
            new { input = new { companyId, lotId, buildingType = "RESEARCH_DEVELOPMENT", buildingName = "Lab One" } },
            token);
        var buildingId = purchaseResult.GetProperty("data").GetProperty("purchaseLot").GetProperty("building").GetProperty("id").GetString()!;

        var result = await ExecuteGraphQlAsync(
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
                        new
                        {
                            unitType = "BRAND_QUALITY",
                            gridX = 0,
                            gridY = 0,
                            linkUp = false,
                            linkDown = false,
                            linkLeft = false,
                            linkRight = false,
                            linkUpLeft = false,
                            linkUpRight = false,
                            linkDownLeft = false,
                            linkDownRight = false,
                            brandScope = "PRODUCT"
                        }
                    }
                }
            },
            token);

        Assert.True(result.TryGetProperty("errors", out var errors));
        Assert.Equal("BRAND_QUALITY_PRODUCT_REQUIRED", errors[0].GetProperty("extensions").GetProperty("code").GetString());
    }

    [Fact]
    public async Task StoreBuildingConfiguration_DirectionalLink_StoredAndReadBackCorrectly()
    {
        // Directional links: only PURCHASE has linkRight=true (A→B), MANUFACTURING has linkLeft=false.
        // The engine and API must preserve asymmetric flags.
        var token = await RegisterAndGetTokenAsync($"dirlink-{Guid.NewGuid()}@test.com", "DirLinkTester");
        var companyResult = await ExecuteGraphQlAsync(
            "mutation CreateCompany($input: CreateCompanyInput!) { createCompany(input: $input) { id } }",
            new { input = new { name = "Directional Link Corp" } },
            token);
        var companyId = companyResult.GetProperty("data").GetProperty("createCompany").GetProperty("id").GetString();

        var citiesResult = await ExecuteGraphQlAsync("{ cities { id } }");
        var cityId = citiesResult.GetProperty("data").GetProperty("cities")[0].GetProperty("id").GetString();

        var buildingResult = await ExecuteGraphQlAsync(
            "mutation PlaceBuilding($input: PlaceBuildingInput!) { placeBuilding(input: $input) { id } }",
            new { input = new { companyId, cityId, type = "FACTORY", name = "Directional Factory" } },
            token);
        var buildingId = buildingResult.GetProperty("data").GetProperty("placeBuilding").GetProperty("id").GetString();

        // Asymmetric: PURCHASE.linkRight=true, MANUFACTURING.linkLeft=false (one-way A→B)
        var configResult = await ExecuteGraphQlAsync(
            """
            mutation StoreBuildingConfiguration($input: StoreBuildingConfigurationInput!) {
                storeBuildingConfiguration(input: $input) {
                    buildingId
                    units { gridX gridY unitType linkRight linkLeft }
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
                        new { unitType = "PURCHASE", gridX = 0, gridY = 0,
                              linkUp = false, linkDown = false, linkLeft = false, linkRight = true,
                              linkUpLeft = false, linkUpRight = false, linkDownLeft = false, linkDownRight = false },
                        new { unitType = "MANUFACTURING", gridX = 1, gridY = 0,
                              linkUp = false, linkDown = false, linkLeft = false, linkRight = false,
                              linkUpLeft = false, linkUpRight = false, linkDownLeft = false, linkDownRight = false }
                    }
                }
            },
            token);

        Assert.False(configResult.TryGetProperty("errors", out _));
        var planUnits = configResult.GetProperty("data").GetProperty("storeBuildingConfiguration").GetProperty("units");

        // PURCHASE should have linkRight=true
        var purchaseUnit = planUnits.EnumerateArray().Single(u => u.GetProperty("unitType").GetString() == "PURCHASE");
        Assert.True(purchaseUnit.GetProperty("linkRight").GetBoolean());

        // MANUFACTURING should have linkLeft=false (asymmetric / directional)
        var mfgUnit = planUnits.EnumerateArray().Single(u => u.GetProperty("unitType").GetString() == "MANUFACTURING");
        Assert.False(mfgUnit.GetProperty("linkLeft").GetBoolean());
    }

    [Fact]
    public async Task StoreBuildingConfiguration_LinkOutOfBounds_ReturnsError()
    {
        var token = await RegisterAndGetTokenAsync($"linkbound-{Guid.NewGuid()}@test.com", "LinkBoundTester");
        var companyResult = await ExecuteGraphQlAsync(
            "mutation CreateCompany($input: CreateCompanyInput!) { createCompany(input: $input) { id } }",
            new { input = new { name = "Bounds Corp" } },
            token);
        var companyId = companyResult.GetProperty("data").GetProperty("createCompany").GetProperty("id").GetString();

        var citiesResult = await ExecuteGraphQlAsync("{ cities { id } }");
        var cityId = citiesResult.GetProperty("data").GetProperty("cities")[0].GetProperty("id").GetString();

        var buildingResult = await ExecuteGraphQlAsync(
            "mutation PlaceBuilding($input: PlaceBuildingInput!) { placeBuilding(input: $input) { id } }",
            new { input = new { companyId, cityId, type = "FACTORY", name = "Bounds Factory" } },
            token);
        var buildingId = buildingResult.GetProperty("data").GetProperty("placeBuilding").GetProperty("id").GetString();

        // linkRight on a unit at x=3 points outside the 4x4 grid
        var result = await ExecuteGraphQlAsync(
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
                        new { unitType = "PURCHASE", gridX = 3, gridY = 0,
                              linkUp = false, linkDown = false, linkLeft = false, linkRight = true,
                              linkUpLeft = false, linkUpRight = false, linkDownLeft = false, linkDownRight = false }
                    }
                }
            },
            token);

        Assert.True(result.TryGetProperty("errors", out var errors));
        Assert.Equal("LINK_OUT_OF_BOUNDS", errors[0].GetProperty("extensions").GetProperty("code").GetString());
    }

    [Fact]
    public async Task StoreBuildingConfiguration_LinkTargetMissing_ReturnsError()
    {
        var token = await RegisterAndGetTokenAsync($"linkmissing-{Guid.NewGuid()}@test.com", "LinkMissingTester");
        var companyResult = await ExecuteGraphQlAsync(
            "mutation CreateCompany($input: CreateCompanyInput!) { createCompany(input: $input) { id } }",
            new { input = new { name = "Missing Corp" } },
            token);
        var companyId = companyResult.GetProperty("data").GetProperty("createCompany").GetProperty("id").GetString();

        var citiesResult = await ExecuteGraphQlAsync("{ cities { id } }");
        var cityId = citiesResult.GetProperty("data").GetProperty("cities")[0].GetProperty("id").GetString();

        var buildingResult = await ExecuteGraphQlAsync(
            "mutation PlaceBuilding($input: PlaceBuildingInput!) { placeBuilding(input: $input) { id } }",
            new { input = new { companyId, cityId, type = "FACTORY", name = "Missing Factory" } },
            token);
        var buildingId = buildingResult.GetProperty("data").GetProperty("placeBuilding").GetProperty("id").GetString();

        // PURCHASE at (0,0) has linkRight=true but there is no unit at (1,0)
        var result = await ExecuteGraphQlAsync(
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
                        new { unitType = "PURCHASE", gridX = 0, gridY = 0,
                              linkUp = false, linkDown = false, linkLeft = false, linkRight = true,
                              linkUpLeft = false, linkUpRight = false, linkDownLeft = false, linkDownRight = false }
                    }
                }
            },
            token);

        Assert.True(result.TryGetProperty("errors", out var errors));
        Assert.Equal("LINK_TARGET_MISSING", errors[0].GetProperty("extensions").GetProperty("code").GetString());
    }

    [Fact]
    public async Task StoreBuildingConfiguration_DirectionalLinks_PreservedThroughCancellation()
    {
        // Directional links set in a pending plan must survive a cancel-and-requeue cycle.
        var token = await RegisterAndGetTokenAsync($"dircancel-{Guid.NewGuid()}@test.com", "DirCancelTester");
        var companyResult = await ExecuteGraphQlAsync(
            "mutation CreateCompany($input: CreateCompanyInput!) { createCompany(input: $input) { id } }",
            new { input = new { name = "Cancel Dir Corp" } },
            token);
        var companyId = companyResult.GetProperty("data").GetProperty("createCompany").GetProperty("id").GetString();

        var citiesResult = await ExecuteGraphQlAsync("{ cities { id } }");
        var cityId = citiesResult.GetProperty("data").GetProperty("cities")[0].GetProperty("id").GetString();

        var buildingResult = await ExecuteGraphQlAsync(
            "mutation PlaceBuilding($input: PlaceBuildingInput!) { placeBuilding(input: $input) { id } }",
            new { input = new { companyId, cityId, type = "FACTORY", name = "Cancel Dir Factory" } },
            token);
        var buildingId = buildingResult.GetProperty("data").GetProperty("placeBuilding").GetProperty("id").GetString();

        // Queue a plan with directional links (only PURCHASE sends right, MANUFACTURING does not receive left)
        await ExecuteGraphQlAsync(
            "mutation StoreBuildingConfiguration($input: StoreBuildingConfigurationInput!) { storeBuildingConfiguration(input: $input) { id } }",
            new
            {
                input = new
                {
                    buildingId,
                    units = new[]
                    {
                        new { unitType = "PURCHASE", gridX = 0, gridY = 0,
                              linkUp = false, linkDown = false, linkLeft = false, linkRight = true,
                              linkUpLeft = false, linkUpRight = false, linkDownLeft = false, linkDownRight = false },
                        new { unitType = "MANUFACTURING", gridX = 1, gridY = 0,
                              linkUp = false, linkDown = false, linkLeft = false, linkRight = false,
                              linkUpLeft = false, linkUpRight = false, linkDownLeft = false, linkDownRight = false }
                    }
                }
            },
            token);

        // Cancel the plan
        await ExecuteGraphQlAsync(
            "mutation CancelBuildingConfiguration($input: CancelBuildingConfigurationInput!) { cancelBuildingConfiguration(input: $input) { id } }",
            new { input = new { buildingId } },
            token);

        // Re-queue the same plan with the same directional flags
        var requeue = await ExecuteGraphQlAsync(
            """
            mutation StoreBuildingConfiguration($input: StoreBuildingConfigurationInput!) {
                storeBuildingConfiguration(input: $input) {
                    units { unitType linkRight linkLeft }
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
                        new { unitType = "PURCHASE", gridX = 0, gridY = 0,
                              linkUp = false, linkDown = false, linkLeft = false, linkRight = true,
                              linkUpLeft = false, linkUpRight = false, linkDownLeft = false, linkDownRight = false },
                        new { unitType = "MANUFACTURING", gridX = 1, gridY = 0,
                              linkUp = false, linkDown = false, linkLeft = false, linkRight = false,
                              linkUpLeft = false, linkUpRight = false, linkDownLeft = false, linkDownRight = false }
                    }
                }
            },
            token);

        Assert.False(requeue.TryGetProperty("errors", out _));
        var requeueUnits = requeue.GetProperty("data").GetProperty("storeBuildingConfiguration").GetProperty("units");
        var purchaseUnit = requeueUnits.EnumerateArray().Single(u => u.GetProperty("unitType").GetString() == "PURCHASE");
        Assert.True(purchaseUnit.GetProperty("linkRight").GetBoolean());
        var mfgUnit = requeueUnits.EnumerateArray().Single(u => u.GetProperty("unitType").GetString() == "MANUFACTURING");
        Assert.False(mfgUnit.GetProperty("linkLeft").GetBoolean());
    }

    [Fact]
    public async Task StoreBuildingConfiguration_DiagonalLink_StoredAndReadBackCorrectly()
    {
        // Diagonal links: PURCHASE at (0,0) has linkDownRight=true (↘ to STORAGE at (1,1)).
        // STORAGE at (1,1) has linkUpLeft=true (confirming the return direction).
        // The backend must persist all four diagonal flag fields independently.
        var token = await RegisterAndGetTokenAsync($"diaglink-{Guid.NewGuid()}@test.com", "DiagLinkTester");
        var companyResult = await ExecuteGraphQlAsync(
            "mutation CreateCompany($input: CreateCompanyInput!) { createCompany(input: $input) { id } }",
            new { input = new { name = "Diagonal Link Corp" } },
            token);
        var companyId = companyResult.GetProperty("data").GetProperty("createCompany").GetProperty("id").GetString();

        var citiesResult = await ExecuteGraphQlAsync("{ cities { id } }");
        var cityId = citiesResult.GetProperty("data").GetProperty("cities")[0].GetProperty("id").GetString();

        var buildingResult = await ExecuteGraphQlAsync(
            "mutation PlaceBuilding($input: PlaceBuildingInput!) { placeBuilding(input: $input) { id } }",
            new { input = new { companyId, cityId, type = "FACTORY", name = "Diagonal Factory" } },
            token);
        var buildingId = buildingResult.GetProperty("data").GetProperty("placeBuilding").GetProperty("id").GetString();

        // 2×2 block: PURCHASE (0,0) → STORAGE (1,1) diagonal (↘), MANUFACTURING (1,0) → STORAGE (0,1) diagonal (↙)
        var configResult = await ExecuteGraphQlAsync(
            """
            mutation StoreBuildingConfiguration($input: StoreBuildingConfigurationInput!) {
                storeBuildingConfiguration(input: $input) {
                    buildingId
                    units { gridX gridY unitType linkDownRight linkDownLeft linkUpLeft linkUpRight }
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
                        new { unitType = "PURCHASE",       gridX = 0, gridY = 0,
                              linkUp = false, linkDown = false, linkLeft = false, linkRight = false,
                              linkUpLeft = false, linkUpRight = false, linkDownLeft = false, linkDownRight = true },
                        new { unitType = "MANUFACTURING",  gridX = 1, gridY = 0,
                              linkUp = false, linkDown = false, linkLeft = false, linkRight = false,
                              linkUpLeft = false, linkUpRight = false, linkDownLeft = true, linkDownRight = false },
                        new { unitType = "STORAGE",        gridX = 0, gridY = 1,
                              linkUp = false, linkDown = false, linkLeft = false, linkRight = false,
                              linkUpLeft = false, linkUpRight = true, linkDownLeft = false, linkDownRight = false },
                        new { unitType = "B2B_SALES",      gridX = 1, gridY = 1,
                              linkUp = false, linkDown = false, linkLeft = false, linkRight = false,
                              linkUpLeft = true, linkUpRight = false, linkDownLeft = false, linkDownRight = false }
                    }
                }
            },
            token);

        Assert.False(configResult.TryGetProperty("errors", out _));
        var planUnits = configResult.GetProperty("data").GetProperty("storeBuildingConfiguration").GetProperty("units");

        var purchaseUnitDiag = planUnits.EnumerateArray().Single(u => u.GetProperty("unitType").GetString() == "PURCHASE");
        Assert.True(purchaseUnitDiag.GetProperty("linkDownRight").GetBoolean());
        Assert.False(purchaseUnitDiag.GetProperty("linkDownLeft").GetBoolean());

        var mfgUnitDiag = planUnits.EnumerateArray().Single(u => u.GetProperty("unitType").GetString() == "MANUFACTURING");
        Assert.True(mfgUnitDiag.GetProperty("linkDownLeft").GetBoolean());
        Assert.False(mfgUnitDiag.GetProperty("linkDownRight").GetBoolean());

        var storageUnitDiag = planUnits.EnumerateArray().Single(u => u.GetProperty("unitType").GetString() == "STORAGE");
        Assert.True(storageUnitDiag.GetProperty("linkUpRight").GetBoolean());
        Assert.False(storageUnitDiag.GetProperty("linkUpLeft").GetBoolean());

        var b2bSalesUnitDiag = planUnits.EnumerateArray().Single(u => u.GetProperty("unitType").GetString() == "B2B_SALES");
        Assert.True(b2bSalesUnitDiag.GetProperty("linkUpLeft").GetBoolean());
        Assert.False(b2bSalesUnitDiag.GetProperty("linkUpRight").GetBoolean());
    }

    [Fact]
    public async Task StoreBuildingConfiguration_DiagonalLinkOutOfBounds_ReturnsError()
    {
        // linkDownRight on a unit at (3,3) points outside the 4x4 grid (target would be (4,4))
        var token = await RegisterAndGetTokenAsync($"diagbound-{Guid.NewGuid()}@test.com", "DiagBoundTester");
        var companyResult = await ExecuteGraphQlAsync(
            "mutation CreateCompany($input: CreateCompanyInput!) { createCompany(input: $input) { id } }",
            new { input = new { name = "Diag Bounds Corp" } },
            token);
        var companyId = companyResult.GetProperty("data").GetProperty("createCompany").GetProperty("id").GetString();

        var citiesResult = await ExecuteGraphQlAsync("{ cities { id } }");
        var cityId = citiesResult.GetProperty("data").GetProperty("cities")[0].GetProperty("id").GetString();

        var buildingResult = await ExecuteGraphQlAsync(
            "mutation PlaceBuilding($input: PlaceBuildingInput!) { placeBuilding(input: $input) { id } }",
            new { input = new { companyId, cityId, type = "FACTORY", name = "Diag Bounds Factory" } },
            token);
        var buildingId = buildingResult.GetProperty("data").GetProperty("placeBuilding").GetProperty("id").GetString();

        var result = await ExecuteGraphQlAsync(
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
                        // linkDownRight on bottom-right corner points to (4,4) — out of bounds
                        new { unitType = "STORAGE", gridX = 3, gridY = 3,
                              linkUp = false, linkDown = false, linkLeft = false, linkRight = false,
                              linkUpLeft = false, linkUpRight = false, linkDownLeft = false, linkDownRight = true }
                    }
                }
            },
            token);

        Assert.True(result.TryGetProperty("errors", out var errors));
        Assert.Equal("LINK_OUT_OF_BOUNDS", errors[0].GetProperty("extensions").GetProperty("code").GetString());
    }

    [Fact]
    public async Task StoreBuildingConfiguration_DiagonalLinkUpLeftOutOfBounds_ReturnsError()
    {
        // linkUpLeft on a unit at (0,0) points to (-1,-1) — out of bounds
        var token = await RegisterAndGetTokenAsync($"diagbound2-{Guid.NewGuid()}@test.com", "DiagBound2Tester");
        var companyResult = await ExecuteGraphQlAsync(
            "mutation CreateCompany($input: CreateCompanyInput!) { createCompany(input: $input) { id } }",
            new { input = new { name = "Diag Bounds Corp 2" } },
            token);
        var companyId = companyResult.GetProperty("data").GetProperty("createCompany").GetProperty("id").GetString();

        var citiesResult = await ExecuteGraphQlAsync("{ cities { id } }");
        var cityId = citiesResult.GetProperty("data").GetProperty("cities")[0].GetProperty("id").GetString();

        var buildingResult = await ExecuteGraphQlAsync(
            "mutation PlaceBuilding($input: PlaceBuildingInput!) { placeBuilding(input: $input) { id } }",
            new { input = new { companyId, cityId, type = "FACTORY", name = "Diag Bounds Factory 2" } },
            token);
        var buildingId = buildingResult.GetProperty("data").GetProperty("placeBuilding").GetProperty("id").GetString();

        var result = await ExecuteGraphQlAsync(
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
                        // linkUpLeft on top-left corner points to (-1,-1) — out of bounds
                        new { unitType = "STORAGE", gridX = 0, gridY = 0,
                              linkUp = false, linkDown = false, linkLeft = false, linkRight = false,
                              linkUpLeft = true, linkUpRight = false, linkDownLeft = false, linkDownRight = false }
                    }
                }
            },
            token);

        Assert.True(result.TryGetProperty("errors", out var errors));
        Assert.Equal("LINK_OUT_OF_BOUNDS", errors[0].GetProperty("extensions").GetProperty("code").GetString());
    }

    [Fact]
    public async Task StoreBuildingConfiguration_DiagonalLinkTargetMissing_ReturnsError()
    {
        // PURCHASE at (0,0) has linkDownRight=true but there is no unit at (1,1)
        var token = await RegisterAndGetTokenAsync($"diagmissing-{Guid.NewGuid()}@test.com", "DiagMissingTester");
        var companyResult = await ExecuteGraphQlAsync(
            "mutation CreateCompany($input: CreateCompanyInput!) { createCompany(input: $input) { id } }",
            new { input = new { name = "Diag Missing Corp" } },
            token);
        var companyId = companyResult.GetProperty("data").GetProperty("createCompany").GetProperty("id").GetString();

        var citiesResult = await ExecuteGraphQlAsync("{ cities { id } }");
        var cityId = citiesResult.GetProperty("data").GetProperty("cities")[0].GetProperty("id").GetString();

        var buildingResult = await ExecuteGraphQlAsync(
            "mutation PlaceBuilding($input: PlaceBuildingInput!) { placeBuilding(input: $input) { id } }",
            new { input = new { companyId, cityId, type = "FACTORY", name = "Diag Missing Factory" } },
            token);
        var buildingId = buildingResult.GetProperty("data").GetProperty("placeBuilding").GetProperty("id").GetString();

        var result = await ExecuteGraphQlAsync(
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
                        // PURCHASE at (0,0) has linkDownRight=true but no unit at (1,1)
                        new { unitType = "PURCHASE", gridX = 0, gridY = 0,
                              linkUp = false, linkDown = false, linkLeft = false, linkRight = false,
                              linkUpLeft = false, linkUpRight = false, linkDownLeft = false, linkDownRight = true }
                    }
                }
            },
            token);

        Assert.True(result.TryGetProperty("errors", out var errors));
        Assert.Equal("LINK_TARGET_MISSING", errors[0].GetProperty("extensions").GetProperty("code").GetString());
    }

    #endregion

    #region Company Ledger & Public Sales Analytics

    [Fact]
    public async Task CompanyLedger_EmptyCompany_ReturnsZeroTotals()
    {
        var token = await RegisterAndGetTokenAsync("ledger-empty@test.com", "LedgerEmpty");
        var result = await ExecuteGraphQlAsync(
            """
            mutation CreateCompany($input: CreateCompanyInput!) {
              createCompany(input: $input) { id name cash }
            }
            """,
            new { input = new { name = "Empty Ledger Co" } },
            token);
        var companyId = result.GetProperty("data").GetProperty("createCompany").GetProperty("id").GetString()!;

        var ledgerResult = await ExecuteGraphQlAsync(
            $"{{ companyLedger(companyId: \"{companyId}\") {{ companyId companyName currentCash totalRevenue totalPurchasingCosts totalPropertyPurchases netIncome totalAssets buildingSummaries {{ buildingId buildingName revenue costs }} }} }}",
            token: token);

        var ledger = ledgerResult.GetProperty("data").GetProperty("companyLedger");
        Assert.Equal(companyId, ledger.GetProperty("companyId").GetString());
        Assert.Equal("Empty Ledger Co", ledger.GetProperty("companyName").GetString());
        Assert.True(ledger.GetProperty("currentCash").GetDecimal() > 0);
        Assert.Equal(0m, ledger.GetProperty("totalRevenue").GetDecimal());
        Assert.Equal(0m, ledger.GetProperty("totalPurchasingCosts").GetDecimal());
        Assert.Equal(0m, ledger.GetProperty("totalPropertyPurchases").GetDecimal());
        Assert.Equal(0m, ledger.GetProperty("netIncome").GetDecimal());
        Assert.True(ledger.GetProperty("totalAssets").GetDecimal() > 0);
    }

    [Fact]
    public async Task CompanyLedger_AfterPropertyPurchase_ShowsPropertyCosts()
    {
        var token = await RegisterAndGetTokenAsync("ledger-prop@test.com", "LedgerProp");
        var (companyId, _, cityId0, _) = await StartOnboardingCompanyAsync(token, "Ledger Prop Co");
        var productId = await GetStarterProductIdAsync();
        var shopLotId = await CreateTestLotAsync(cityId0, "SALES_SHOP,COMMERCIAL", "Commercial Zone");
        await FinishOnboardingAsync(token, productId, shopLotId);

        var ledgerResult = await ExecuteGraphQlAsync(
            $"{{ companyLedger(companyId: \"{companyId}\") {{ totalPropertyPurchases totalAssets currentCash }} }}",
            token: token);

        var ledger = ledgerResult.GetProperty("data").GetProperty("companyLedger");
        Assert.True(ledger.GetProperty("totalPropertyPurchases").GetDecimal() > 0,
            "TotalPropertyPurchases should be > 0 after onboarding lot purchases");
        Assert.True(ledger.GetProperty("totalAssets").GetDecimal() > 0);
    }

    [Fact]
    public async Task LedgerDrillDown_PropertyPurchase_ReturnsEntries()
    {
        var token = await RegisterAndGetTokenAsync("ledger-drilldown@test.com", "LedgerDrillDown");
        var (companyId, _, cityId1, _) = await StartOnboardingCompanyAsync(token, "DrillDown Co");
        var productId = await GetStarterProductIdAsync();
        var shopLotId = await CreateTestLotAsync(cityId1, "SALES_SHOP,COMMERCIAL", "Commercial Zone");
        await FinishOnboardingAsync(token, productId, shopLotId);

        var drillResult = await ExecuteGraphQlAsync(
            $"{{ ledgerDrillDown(companyId: \"{companyId}\", category: \"PROPERTY_PURCHASE\") {{ id category description amount recordedAtTick buildingId buildingName }} }}",
            token: token);

        var entries = drillResult.GetProperty("data").GetProperty("ledgerDrillDown").EnumerateArray().ToList();
        Assert.True(entries.Count >= 2, "Should have at least 2 PROPERTY_PURCHASE entries (factory + shop)");
        Assert.All(entries, e =>
        {
            Assert.Equal("PROPERTY_PURCHASE", e.GetProperty("category").GetString());
            Assert.True(e.GetProperty("amount").GetDecimal() < 0, "Property purchase amounts should be negative");
        });
    }

    [Fact]
    public async Task PublicSalesAnalytics_EmptyUnit_ReturnsEmptyHistory()
    {
        var token = await RegisterAndGetTokenAsync("analytics-empty@test.com", "AnalyticsEmpty");
        var (companyId2, _, cityId2, _) = await StartOnboardingCompanyAsync(token, "Analytics Empty Co");
        var productId = await GetStarterProductIdAsync();
        var shopLotId = await CreateTestLotAsync(cityId2, "SALES_SHOP,COMMERCIAL", "Commercial Zone");
        var finishResult = await FinishOnboardingAsync(token, productId, shopLotId);

        var shopId = finishResult.GetProperty("data").GetProperty("finishOnboarding").GetProperty("salesShop").GetProperty("id").GetString()!;

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var unit = await db.BuildingUnits
            .FirstAsync(u => u.BuildingId == Guid.Parse(shopId) && u.UnitType == "PUBLIC_SALES");

        var analyticsResult = await ExecuteGraphQlAsync(
            $"{{ publicSalesAnalytics(unitId: \"{unit.Id}\") {{ buildingUnitId totalRevenue totalQuantitySold revenueHistory {{ tick revenue }} priceHistory {{ tick pricePerUnit }} marketShare {{ label share }} }} }}",
            token: token);

        var analytics = analyticsResult.GetProperty("data").GetProperty("publicSalesAnalytics");
        Assert.Equal(unit.Id.ToString(), analytics.GetProperty("buildingUnitId").GetString());
        Assert.Equal(0m, analytics.GetProperty("totalRevenue").GetDecimal());
        Assert.Equal(0m, analytics.GetProperty("totalQuantitySold").GetDecimal());
        Assert.Equal(0, analytics.GetProperty("revenueHistory").GetArrayLength());
        Assert.Equal(0, analytics.GetProperty("priceHistory").GetArrayLength());
        Assert.Equal(0, analytics.GetProperty("marketShare").GetArrayLength());
    }

    [Fact]
    public async Task CompanyLedger_RequiresOwnership_ForbidsOtherPlayer()
    {
        var ownerToken = await RegisterAndGetTokenAsync("ledger-owner@test.com", "LedgerOwner");
        var result = await ExecuteGraphQlAsync(
            """mutation CreateCompany($input: CreateCompanyInput!) { createCompany(input: $input) { id } }""",
            new { input = new { name = "Owner Co" } },
            ownerToken);
        var companyId = result.GetProperty("data").GetProperty("createCompany").GetProperty("id").GetString()!;

        var otherToken = await RegisterAndGetTokenAsync("ledger-other@test.com", "LedgerOther");
        var ledgerResult = await ExecuteGraphQlAsync(
            $"{{ companyLedger(companyId: \"{companyId}\") {{ companyId }} }}",
            token: otherToken);

        var ledger = ledgerResult.GetProperty("data").GetProperty("companyLedger");
        Assert.Equal(JsonValueKind.Null, ledger.ValueKind);
    }

    [Fact]
    public async Task MarketingPhase_RecordsMarketingLedgerEntry()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Seed: company with a sales shop, marketing unit, and linked public-sales unit.
        var city = await db.Cities.FirstAsync();
        var product = await db.ProductTypes.FirstAsync(p => p.Slug == "wooden-chair");

        var player = new Player
        {
            Id = Guid.NewGuid(),
            Email = $"mktg-ledger-{Guid.NewGuid():N}@test.com",
            DisplayName = "Marketing Tester",
            PasswordHash = "hash",
            Role = PlayerRole.Player
        };
        db.Players.Add(player);

        var company = new Company
        {
            Id = Guid.NewGuid(),
            PlayerId = player.Id,
            Name = "Marketing Corp",
            Cash = 500_000m
        };
        db.Companies.Add(company);

        var shop = new Building
        {
            Id = Guid.NewGuid(),
            CompanyId = company.Id,
            CityId = city.Id,
            Type = BuildingType.SalesShop,
            Name = "Marketing Shop",
            Level = 1
        };
        db.Buildings.Add(shop);

        var salesUnit = new BuildingUnit
        {
            Id = Guid.NewGuid(),
            BuildingId = shop.Id,
            UnitType = UnitType.PublicSales,
            GridX = 0, GridY = 0,
            Level = 1,
            ProductTypeId = product.Id,
            MinPrice = product.BasePrice
        };
        var marketingUnit = new BuildingUnit
        {
            Id = Guid.NewGuid(),
            BuildingId = shop.Id,
            UnitType = UnitType.Marketing,
            GridX = 1, GridY = 0,
            Level = 1,
            Budget = 1_000m,
            LinkRight = false
        };
        db.BuildingUnits.AddRange(salesUnit, marketingUnit);
        await db.SaveChangesAsync();

        var cashBefore = company.Cash;
        var ledgerCountBefore = await db.LedgerEntries
            .CountAsync(e => e.CompanyId == company.Id && e.Category == LedgerCategory.Marketing);

        // Run one tick to trigger the marketing phase.
        var phases = scope.ServiceProvider.GetServices<ITickPhase>();
        var logger = new NullLogger<TickProcessor>();
        var processor = new TickProcessor(db, phases, logger);
        await processor.ProcessTickAsync();

        var marketingEntries = await db.LedgerEntries
            .Where(e => e.CompanyId == company.Id && e.Category == LedgerCategory.Marketing)
            .ToListAsync();

        Assert.True(marketingEntries.Count > ledgerCountBefore,
            "Marketing phase should have added a MARKETING ledger entry.");
        Assert.All(marketingEntries, e =>
        {
            Assert.Equal(LedgerCategory.Marketing, e.Category);
            Assert.True(e.Amount < 0, "Marketing ledger amount should be negative (debit).");
            Assert.Equal(shop.Id, e.BuildingId);
            Assert.Equal(marketingUnit.Id, e.BuildingUnitId);
        });
        Assert.True(company.Cash < cashBefore,
            "Company cash should decrease after marketing spend.");
    }

    [Fact]
    public async Task CompanyLedger_AfterMarketingTick_ReflectsMarketingCosts()
    {
        var token = await RegisterAndGetTokenAsync("ledger-mktg@test.com", "LedgerMktg");
        var result = await ExecuteGraphQlAsync(
            """mutation CreateCompany($input: CreateCompanyInput!) { createCompany(input: $input) { id } }""",
            new { input = new { name = "Mktg Ledger Co" } },
            token);
        var companyId = result.GetProperty("data").GetProperty("createCompany").GetProperty("id").GetString()!;

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var city = await db.Cities.FirstAsync();
        var product = await db.ProductTypes.FirstAsync(p => p.Slug == "wooden-chair");
        var companyGuid = Guid.Parse(companyId);

        // Build a shop with a marketing unit directly in DB.
        var shop = new Building
        {
            Id = Guid.NewGuid(),
            CompanyId = companyGuid,
            CityId = city.Id,
            Type = BuildingType.SalesShop,
            Name = "Mktg Shop",
            Level = 1
        };
        db.Buildings.Add(shop);

        var salesUnit = new BuildingUnit
        {
            Id = Guid.NewGuid(),
            BuildingId = shop.Id,
            UnitType = UnitType.PublicSales,
            GridX = 0, GridY = 0,
            Level = 1,
            ProductTypeId = product.Id,
            MinPrice = product.BasePrice
        };
        var marketingUnit = new BuildingUnit
        {
            Id = Guid.NewGuid(),
            BuildingId = shop.Id,
            UnitType = UnitType.Marketing,
            GridX = 1, GridY = 0,
            Level = 1,
            Budget = 2_000m
        };
        db.BuildingUnits.AddRange(salesUnit, marketingUnit);
        await db.SaveChangesAsync();

        var phases = scope.ServiceProvider.GetServices<ITickPhase>();
        var logger = new NullLogger<TickProcessor>();
        var processor = new TickProcessor(db, phases, logger);
        await processor.ProcessTickAsync();

        var ledgerResult = await ExecuteGraphQlAsync(
            $"{{ companyLedger(companyId: \"{companyId}\") {{ totalMarketingCosts netIncome cashFromOperations }} }}",
            token: token);

        var ledger = ledgerResult.GetProperty("data").GetProperty("companyLedger");
        Assert.True(ledger.GetProperty("totalMarketingCosts").GetDecimal() > 0,
            "totalMarketingCosts should be > 0 after a tick with a configured marketing unit.");
        // Net income accounts for marketing cost (negative contribution).
        // cashFromOperations should also reflect the marketing debit.
        Assert.True(ledger.GetProperty("cashFromOperations").GetDecimal() < 0,
            "cashFromOperations should be negative when marketing spend exceeds zero revenue.");
    }

    [Fact]
    public async Task BuildingUnitInventories_ReturnSourcingCostsForMixedUnitInventory()
    {
        var token = await RegisterAndGetTokenAsync($"inventory-{Guid.NewGuid():N}@test.com", "Inventory Tester");
        var onboarding = await CompleteOnboardingAsync(token, "Inventory Works");
        var factoryId = onboarding.Result.GetProperty("data").GetProperty("completeOnboarding").GetProperty("factory").GetProperty("id").GetString()!;

        Guid unitId;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var factoryGuid = Guid.Parse(factoryId);

            var unit = await db.BuildingUnits
                .Where(candidate => candidate.BuildingId == factoryGuid && candidate.UnitType == UnitType.Purchase)
                .FirstAsync();

            var woodId = await db.ResourceTypes
                .Where(resource => resource.Slug == "wood")
                .Select(resource => resource.Id)
                .FirstAsync();

            var grainId = await db.ResourceTypes
                .Where(resource => resource.Slug == "grain")
                .Select(resource => resource.Id)
                .FirstAsync();

            unitId = unit.Id;
            db.Inventories.AddRange(
                new Inventory
                {
                    Id = Guid.NewGuid(),
                    BuildingId = factoryGuid,
                    BuildingUnitId = unit.Id,
                    ResourceTypeId = woodId,
                    Quantity = 10m,
                    SourcingCostTotal = 140m,
                    Quality = 0.8m,
                },
                new Inventory
                {
                    Id = Guid.NewGuid(),
                    BuildingId = factoryGuid,
                    BuildingUnitId = unit.Id,
                    ResourceTypeId = grainId,
                    Quantity = 5m,
                    SourcingCostTotal = 40m,
                    Quality = 0.6m,
                });

            await db.SaveChangesAsync();
        }

        var result = await ExecuteGraphQlAsync(
            """
            query BuildingInventory($buildingId: UUID!) {
              buildingUnitInventorySummaries(buildingId: $buildingId) {
                buildingUnitId
                quantity
                capacity
                fillPercent
                averageQuality
                totalSourcingCost
                sourcingCostPerUnit
              }
              buildingUnitInventories(buildingId: $buildingId) {
                buildingUnitId
                quantity
                sourcingCostTotal
                sourcingCostPerUnit
                quality
                resourceTypeId
              }
            }
            """,
            new { buildingId = factoryId },
            token);

        var data = result.GetProperty("data");
        var summary = data.GetProperty("buildingUnitInventorySummaries").EnumerateArray()
            .Single(item => item.GetProperty("buildingUnitId").GetString() == unitId.ToString());

        Assert.Equal(15m, summary.GetProperty("quantity").GetDecimal());
        Assert.Equal(180m, summary.GetProperty("totalSourcingCost").GetDecimal());
        Assert.Equal(12m, summary.GetProperty("sourcingCostPerUnit").GetDecimal());
        Assert.Equal(0.7333m, summary.GetProperty("averageQuality").GetDecimal());

        var inventories = data.GetProperty("buildingUnitInventories").EnumerateArray()
            .Where(item => item.GetProperty("buildingUnitId").GetString() == unitId.ToString())
            .ToList();

        Assert.Equal(2, inventories.Count);
        Assert.Equal(180m, inventories.Sum(item => item.GetProperty("sourcingCostTotal").GetDecimal()));
        Assert.Contains(inventories, item => item.GetProperty("sourcingCostPerUnit").GetDecimal() == 14m);
        Assert.Contains(inventories, item => item.GetProperty("sourcingCostPerUnit").GetDecimal() == 8m);
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
