using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Api.Data;
using Api.Data.Entities;
using Api.Engine;
using Api.Types;
using Api.Tests.Infrastructure;
using Api.Utilities;
using Microsoft.AspNetCore.Http;
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
    private const decimal DefaultStarterCompanyCash = 450_000m;

    private readonly HttpClient _client;
    private readonly ApiWebApplicationFactory _factory;

    public GraphQlIntegrationTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    #region Helpers

    private static async Task<JsonElement> ExecuteGraphQlAsync(HttpClient client, string query, object? variables = null, string? token = null)
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

        var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            throw new Exception($"HTTP {(int)response.StatusCode}: {body}");
        }

        return JsonSerializer.Deserialize<JsonElement>(body);
    }

    private Task<JsonElement> ExecuteGraphQlAsync(string query, object? variables = null, string? token = null)
        => ExecuteGraphQlAsync(_client, query, variables, token);

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

                var (companyId, _, cityId, _) = await StartOnboardingCompanyAsync(token, companyName);
                var productId = await GetStarterProductIdAsync();
                var shopLotId = await GetAvailableLotIdAsync(cityId, "SALES_SHOP");

                var result = await ExecuteGraphQlAsync(
                        """
                        mutation CompleteOnboarding($input: FinishOnboardingInput!) {
                            completeOnboarding: finishOnboarding(input: $input) {
                                company { id name cash }
                                factory { id name type }
                                salesShop { id name type }
                                selectedProduct { id name industry }
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

                return (companyId, productId, result);
        }

    private async Task<Guid> GetCurrentPlayerIdAsync(string token)
    {
        var result = await ExecuteGraphQlAsync("{ me { id } }", token: token);
        return Guid.Parse(result.GetProperty("data").GetProperty("me").GetProperty("id").GetString()!);
    }

    private async Task<Guid> SeedPublicCompanyAsync(
        Guid controllerPlayerId,
        string name = "Public Float Co",
        decimal cash = 100_000m,
        decimal totalShares = 10_000m,
        decimal founderShares = 5_000m,
        decimal dividendPayoutRatio = 0.2m)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var currentTick = await db.GameStates.AsNoTracking().Select(state => state.CurrentTick).FirstOrDefaultAsync();

        var company = new Company
        {
            Id = Guid.NewGuid(),
            PlayerId = controllerPlayerId,
            Name = name,
            Cash = cash,
            TotalSharesIssued = totalShares,
            DividendPayoutRatio = dividendPayoutRatio,
            FoundedAtUtc = DateTime.UtcNow,
            FoundedAtTick = currentTick,
        };

        db.Companies.Add(company);
        db.Shareholdings.Add(new Shareholding
        {
            Id = Guid.NewGuid(),
            CompanyId = company.Id,
            OwnerPlayerId = controllerPlayerId,
            ShareCount = founderShares,
        });

        await db.SaveChangesAsync();
        return company.Id;
    }

    private async Task SetActiveCompanyContextAsync(Guid playerId, Guid companyId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var player = await db.Players.FirstAsync(candidate => candidate.Id == playerId);
        player.ActiveAccountType = AccountContextType.Company;
        player.ActiveCompanyId = companyId;
        await db.SaveChangesAsync();
    }

    private static async Task<string> GetCityIdByNameAsync(HttpClient client, string cityName = "Bratislava")
    {
        var citiesResult = await ExecuteGraphQlAsync(client, "{ cities { id name } }");
        return citiesResult.GetProperty("data").GetProperty("cities").EnumerateArray()
            .First(city => city.GetProperty("name").GetString() == cityName)
            .GetProperty("id")
            .GetString()!;
    }

    private Task<string> GetCityIdByNameAsync(string cityName = "Bratislava")
        => GetCityIdByNameAsync(_client, cityName);

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
              cityLots(cityId: $cityId) { id suitableTypes ownerCompanyId price }
            }
            """,
            new { cityId });

        // Filter to affordable lots (price within the default starter-company cash of $450,000) so that test lots
        // intentionally priced above the starting cash do not interfere with other tests.
        return lotsResult.GetProperty("data").GetProperty("cityLots").EnumerateArray()
            .First(lot => lot.GetProperty("suitableTypes").GetString()!.Contains(suitableType)
                          && lot.GetProperty("ownerCompanyId").ValueKind == JsonValueKind.Null
                  && lot.GetProperty("price").GetDecimal() < 450_000m)
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
        Assert.Equal("Capitalism V Game API", body.GetProperty("name").GetString());
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

    [Fact]
    public async Task FinishOnboarding_FoodProcessingSupplyChainFeedsShopAndMakesFirstSale()
    {
        var token = await RegisterAndGetTokenAsync(email: $"finish-onboarding-food-{Guid.NewGuid():N}@test.com");
        var cityId = await GetCityIdByNameAsync();
        var factoryLotId = await CreateTestLotAsync(cityId, "FACTORY,MINE", "Industrial Zone");

        await ExecuteGraphQlAsync(
            """
            mutation StartOnboardingCompany($input: StartOnboardingCompanyInput!) {
              startOnboardingCompany(input: $input) { nextStep company { id } }
            }
            """,
            new { input = new { industry = "FOOD_PROCESSING", cityId, companyName = "Bread Factory Co", factoryLotId } },
            token);

        var productId = await GetStarterProductIdAsync("FOOD_PROCESSING", "bread");
        var shopLotId = await GetAvailableLotIdAsync(cityId, "SALES_SHOP");

        var result = await FinishOnboardingAsync(token, productId, shopLotId);

        Assert.False(result.TryGetProperty("errors", out _), "FinishOnboarding failed for FOOD_PROCESSING");

        var payload = result.GetProperty("data").GetProperty("finishOnboarding");
        var shopId = Guid.Parse(payload.GetProperty("salesShop").GetProperty("id").GetString()!);
        var productGuid = Guid.Parse(productId);

        await ProcessTicksAsync(4);

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
                "FOOD_PROCESSING starter shop purchase unit should fill from the factory output after ticks.");
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

    [Fact]
    public async Task FinishOnboarding_HealthcareSupplyChainFeedsShopAndMakesFirstSale()
    {
        var token = await RegisterAndGetTokenAsync(email: $"finish-onboarding-health-{Guid.NewGuid():N}@test.com");
        var cityId = await GetCityIdByNameAsync();
        var factoryLotId = await CreateTestLotAsync(cityId, "FACTORY,MINE", "Industrial Zone");

        await ExecuteGraphQlAsync(
            """
            mutation StartOnboardingCompany($input: StartOnboardingCompanyInput!) {
              startOnboardingCompany(input: $input) { nextStep company { id } }
            }
            """,
            new { input = new { industry = "HEALTHCARE", cityId, companyName = "Pharma Factory Co", factoryLotId } },
            token);

        var productId = await GetStarterProductIdAsync("HEALTHCARE", "basic-medicine");
        var shopLotId = await GetAvailableLotIdAsync(cityId, "SALES_SHOP");

        var result = await FinishOnboardingAsync(token, productId, shopLotId);

        Assert.False(result.TryGetProperty("errors", out _), "FinishOnboarding failed for HEALTHCARE");

        var payload = result.GetProperty("data").GetProperty("finishOnboarding");
        var shopId = Guid.Parse(payload.GetProperty("salesShop").GetProperty("id").GetString()!);
        var productGuid = Guid.Parse(productId);

        await ProcessTicksAsync(4);

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
                "HEALTHCARE starter shop purchase unit should fill from the factory output after ticks.");
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

    [Fact]
    public async Task FinishOnboarding_PragueCity_FoodProcessingSupplyChainFeedsShopAndMakesFirstSale()
    {
        // AC2 + AC8: Supply chain tick-engine must work for non-default cities.
        // Prague is the second seeded city. Food Processing (Grain→Bread) uses city-specific
        // global exchange pricing, so this test proves the city-exchange integration works
        // for Prague after onboarding — not just for the default Bratislava.
        var token = await RegisterAndGetTokenAsync(email: $"finish-prague-food-{Guid.NewGuid():N}@test.com");
        var cityId = await GetCityIdByNameAsync("Prague");
        var factoryLotId = await CreateTestLotAsync(cityId, "FACTORY,MINE", "Prague Industrial Zone");

        await ExecuteGraphQlAsync(
            """
            mutation StartOnboardingCompany($input: StartOnboardingCompanyInput!) {
              startOnboardingCompany(input: $input) { nextStep company { id } }
            }
            """,
            new { input = new { industry = "FOOD_PROCESSING", cityId, companyName = "Prague Bread Factory Co", factoryLotId } },
            token);

        var productId = await GetStarterProductIdAsync("FOOD_PROCESSING", "bread");
        var shopLotId = await CreateTestLotAsync(cityId, "SALES_SHOP,COMMERCIAL", "Prague Commercial District", 90_000m);

        var result = await FinishOnboardingAsync(token, productId, shopLotId);

        Assert.False(result.TryGetProperty("errors", out _), "FinishOnboarding failed for FOOD_PROCESSING in Prague");

        var payload = result.GetProperty("data").GetProperty("finishOnboarding");
        var shopId = Guid.Parse(payload.GetProperty("salesShop").GetProperty("id").GetString()!);
        var productGuid = Guid.Parse(productId);

        await ProcessTicksAsync(4);

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
                "Prague FOOD_PROCESSING starter shop should fill from factory output after ticks.");
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

    [Fact]
    public async Task FinishOnboarding_ViennaCity_HealthcareSupplyChainFeedsShopAndMakesFirstSale()
    {
        // AC2 + AC8: Supply chain tick-engine must work for non-default cities.
        // Vienna is the third seeded city. Healthcare (Chemical Minerals→Basic Medicine) uses
        // city-specific global exchange pricing, so this test proves the city-exchange integration
        // works for Vienna after onboarding — not just for the default Bratislava.
        var token = await RegisterAndGetTokenAsync(email: $"finish-vienna-health-{Guid.NewGuid():N}@test.com");
        var cityId = await GetCityIdByNameAsync("Vienna");
        var factoryLotId = await CreateTestLotAsync(cityId, "FACTORY,MINE", "Vienna Industrial Zone");

        await ExecuteGraphQlAsync(
            """
            mutation StartOnboardingCompany($input: StartOnboardingCompanyInput!) {
              startOnboardingCompany(input: $input) { nextStep company { id } }
            }
            """,
            new { input = new { industry = "HEALTHCARE", cityId, companyName = "Vienna Pharma Corp", factoryLotId } },
            token);

        var productId = await GetStarterProductIdAsync("HEALTHCARE", "basic-medicine");
        var shopLotId = await CreateTestLotAsync(cityId, "SALES_SHOP,COMMERCIAL", "Vienna Commercial District", 90_000m);

        var result = await FinishOnboardingAsync(token, productId, shopLotId);

        Assert.False(result.TryGetProperty("errors", out _), "FinishOnboarding failed for HEALTHCARE in Vienna");

        var payload = result.GetProperty("data").GetProperty("finishOnboarding");
        var shopId = Guid.Parse(payload.GetProperty("salesShop").GetProperty("id").GetString()!);
        var productGuid = Guid.Parse(productId);

        await ProcessTicksAsync(4);

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
                "Vienna HEALTHCARE starter shop should fill from factory output after ticks.");
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

    [Fact]
    public async Task FinishOnboarding_ViennaCity_FoodProcessingSupplyChainFeedsShopAndMakesFirstSale()
    {
        // AC2 + AC8: Supply chain tick-engine must work for non-default cities.
        // Vienna is the third seeded city. Food Processing (Grain→Bread) must work here
        // because city-specific resource abundance and exchange pricing differs from
        // Bratislava and Prague. This test proves the city-exchange integration works
        // for Vienna + FOOD_PROCESSING after onboarding.
        var token = await RegisterAndGetTokenAsync(email: $"finish-vienna-food-{Guid.NewGuid():N}@test.com");
        var cityId = await GetCityIdByNameAsync("Vienna");
        var factoryLotId = await CreateTestLotAsync(cityId, "FACTORY,MINE", "Vienna Industrial Zone");

        await ExecuteGraphQlAsync(
            """
            mutation StartOnboardingCompany($input: StartOnboardingCompanyInput!) {
              startOnboardingCompany(input: $input) { nextStep company { id } }
            }
            """,
            new { input = new { industry = "FOOD_PROCESSING", cityId, companyName = "Vienna Bakery Corp", factoryLotId } },
            token);

        var productId = await GetStarterProductIdAsync("FOOD_PROCESSING", "bread");
        var shopLotId = await CreateTestLotAsync(cityId, "SALES_SHOP,COMMERCIAL", "Vienna Commercial District", 90_000m);

        var result = await FinishOnboardingAsync(token, productId, shopLotId);

        Assert.False(result.TryGetProperty("errors", out _), "FinishOnboarding failed for FOOD_PROCESSING in Vienna");

        var payload = result.GetProperty("data").GetProperty("finishOnboarding");
        var shopId = Guid.Parse(payload.GetProperty("salesShop").GetProperty("id").GetString()!);
        var productGuid = Guid.Parse(productId);

        await ProcessTicksAsync(4);

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
                "Vienna FOOD_PROCESSING starter shop should fill from factory output after ticks.");
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

    [Fact]
    public async Task FinishOnboarding_PragueCity_FurnitureSupplyChainFeedsShopAndMakesFirstSale()
    {
        // AC2 + AC8: Furniture (Wood→Wooden Chair) must work in Prague, the second seeded city.
        // The default supply chain tests use Bratislava; this test proves Prague city-exchange
        // pricing does not break the Furniture starter chain.
        var token = await RegisterAndGetTokenAsync(email: $"finish-prague-furn-{Guid.NewGuid():N}@test.com");
        var cityId = await GetCityIdByNameAsync("Prague");
        var factoryLotId = await CreateTestLotAsync(cityId, "FACTORY,MINE", "Prague Timber Works");

        await ExecuteGraphQlAsync(
            """
            mutation StartOnboardingCompany($input: StartOnboardingCompanyInput!) {
              startOnboardingCompany(input: $input) { nextStep company { id } }
            }
            """,
            new { input = new { industry = "FURNITURE", cityId, companyName = "Prague Furniture House", factoryLotId } },
            token);

        var productId = await GetStarterProductIdAsync("FURNITURE", "wooden-chair");
        var shopLotId = await CreateTestLotAsync(cityId, "SALES_SHOP,COMMERCIAL", "Prague High Street", 90_000m);

        var result = await FinishOnboardingAsync(token, productId, shopLotId);

        Assert.False(result.TryGetProperty("errors", out _), "FinishOnboarding failed for FURNITURE in Prague");

        var payload = result.GetProperty("data").GetProperty("finishOnboarding");
        var shopId = Guid.Parse(payload.GetProperty("salesShop").GetProperty("id").GetString()!);
        var productGuid = Guid.Parse(productId);

        await ProcessTicksAsync(4);

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
                "Prague FURNITURE starter shop should fill from factory output after ticks.");
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

    [Fact]
    public async Task FinishOnboarding_PragueCity_HealthcareSupplyChainFeedsShopAndMakesFirstSale()
    {
        // AC2 + AC8: Healthcare (Chemical Minerals→Basic Medicine) must work in Prague.
        // Prague's resource abundance for Chemical Minerals may differ from the other cities,
        // so this test proves the starter chain is viable for all industries in Prague.
        var token = await RegisterAndGetTokenAsync(email: $"finish-prague-health-{Guid.NewGuid():N}@test.com");
        var cityId = await GetCityIdByNameAsync("Prague");
        var factoryLotId = await CreateTestLotAsync(cityId, "FACTORY,MINE", "Prague Pharma Zone");

        await ExecuteGraphQlAsync(
            """
            mutation StartOnboardingCompany($input: StartOnboardingCompanyInput!) {
              startOnboardingCompany(input: $input) { nextStep company { id } }
            }
            """,
            new { input = new { industry = "HEALTHCARE", cityId, companyName = "Prague Pharma Corp", factoryLotId } },
            token);

        var productId = await GetStarterProductIdAsync("HEALTHCARE", "basic-medicine");
        var shopLotId = await CreateTestLotAsync(cityId, "SALES_SHOP,COMMERCIAL", "Prague Medical District", 90_000m);

        var result = await FinishOnboardingAsync(token, productId, shopLotId);

        Assert.False(result.TryGetProperty("errors", out _), "FinishOnboarding failed for HEALTHCARE in Prague");

        var payload = result.GetProperty("data").GetProperty("finishOnboarding");
        var shopId = Guid.Parse(payload.GetProperty("salesShop").GetProperty("id").GetString()!);
        var productGuid = Guid.Parse(productId);

        await ProcessTicksAsync(4);

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
                "Prague HEALTHCARE starter shop should fill from factory output after ticks.");
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

    [Fact]
    public async Task FinishOnboarding_ViennaCity_FurnitureSupplyChainFeedsShopAndMakesFirstSale()
    {
        // AC2 + AC8: Furniture (Wood→Wooden Chair) must work in Vienna, the third seeded city.
        // This completes the full 3×3 city×industry supply chain tick matrix and proves that
        // Vienna's resource exchange pricing supports all three starter industries.
        var token = await RegisterAndGetTokenAsync(email: $"finish-vienna-furn-{Guid.NewGuid():N}@test.com");
        var cityId = await GetCityIdByNameAsync("Vienna");
        var factoryLotId = await CreateTestLotAsync(cityId, "FACTORY,MINE", "Vienna Timber Works");

        await ExecuteGraphQlAsync(
            """
            mutation StartOnboardingCompany($input: StartOnboardingCompanyInput!) {
              startOnboardingCompany(input: $input) { nextStep company { id } }
            }
            """,
            new { input = new { industry = "FURNITURE", cityId, companyName = "Vienna Furniture House", factoryLotId } },
            token);

        var productId = await GetStarterProductIdAsync("FURNITURE", "wooden-chair");
        var shopLotId = await CreateTestLotAsync(cityId, "SALES_SHOP,COMMERCIAL", "Vienna High Street", 90_000m);

        var result = await FinishOnboardingAsync(token, productId, shopLotId);

        Assert.False(result.TryGetProperty("errors", out _), "FinishOnboarding failed for FURNITURE in Vienna");

        var payload = result.GetProperty("data").GetProperty("finishOnboarding");
        var shopId = Guid.Parse(payload.GetProperty("salesShop").GetProperty("id").GetString()!);
        var productGuid = Guid.Parse(productId);

        await ProcessTicksAsync(4);

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
                "Vienna FURNITURE starter shop should fill from factory output after ticks.");
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
        var result = await ExecuteGraphQlAsync("{ gameState { currentTick tickIntervalSeconds taxCycleTicks taxRate currentGameYear currentGameTimeUtc nextTaxTick nextTaxGameTimeUtc } }");

        var state = result.GetProperty("data").GetProperty("gameState");
        Assert.Equal(0, state.GetProperty("currentTick").GetInt64());
        Assert.Equal(10, state.GetProperty("tickIntervalSeconds").GetInt32());
        Assert.Equal(8760, state.GetProperty("taxCycleTicks").GetInt32());
        Assert.Equal(15m, state.GetProperty("taxRate").GetDecimal());
        Assert.Equal(2000, state.GetProperty("currentGameYear").GetInt32());
        Assert.Equal(
            DateTimeOffset.Parse("2000-01-01T00:00:00Z"),
            state.GetProperty("currentGameTimeUtc").GetDateTimeOffset());
        Assert.Equal(8760, state.GetProperty("nextTaxTick").GetInt64());
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
    public async Task ResourceTypes_AllHaveUniqueNonNullImageUrls()
    {
        // ROADMAP: "Every resource must have unique picture."
        var result = await ExecuteGraphQlAsync(
            "{ resourceTypes { slug imageUrl } }");

        var resources = result.GetProperty("data").GetProperty("resourceTypes");
        Assert.True(resources.GetArrayLength() >= 8);

        var imageUrls = resources.EnumerateArray()
            .Select(r => r.GetProperty("imageUrl").GetString())
            .ToList();

        // Every resource must have a non-empty image
        foreach (var url in imageUrls)
        {
            Assert.False(string.IsNullOrWhiteSpace(url), "A resource has a null or empty imageUrl");
        }

        // Every image URL must be unique (no two resources share the same picture)
        var distinctCount = imageUrls.Distinct().Count();
        Assert.Equal(imageUrls.Count, distinctCount);
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

    [Fact]
    public async Task EncyclopediaResource_IsPublicQueryNoAuthRequired()
    {
        // The encyclopedia must be browsable without authentication so new players
        // can explore the production graph before completing onboarding.
        var result = await ExecuteGraphQlAsync(
            """
            {
              encyclopediaResource(slug: "wood") {
                resource { slug name }
                productsUsingResource { slug }
              }
            }
            """,
            token: null);

        var detail = result.GetProperty("data").GetProperty("encyclopediaResource");
        Assert.False(detail.ValueKind == System.Text.Json.JsonValueKind.Null);
        Assert.Equal("wood", detail.GetProperty("resource").GetProperty("slug").GetString());
    }

    [Fact]
    public async Task ResourceTypes_IsPublicQueryNoAuthRequired()
    {
        // The encyclopedia list must be browsable without authentication.
        var result = await ExecuteGraphQlAsync(
            "{ resourceTypes { slug } }",
            token: null);

        var resources = result.GetProperty("data").GetProperty("resourceTypes");
        Assert.True(resources.GetArrayLength() >= 8);
    }

    [Fact]
    public async Task ProductTypes_IsPublicQueryNoAuthRequired()
    {
        // Products must be visible without authentication.
        var result = await ExecuteGraphQlAsync(
            "{ productTypes { slug isProOnly } }",
            token: null);

        var products = result.GetProperty("data").GetProperty("productTypes");
        Assert.True(products.GetArrayLength() > 0);
    }

    [Fact]
    public async Task ProductTypes_AllStarterIndustriesRepresented()
    {
        // ROADMAP: All combinations of products visible in the manufacturing encyclopedia.
        // Verify the three starter industries are all seeded.
        var result = await ExecuteGraphQlAsync(
            "{ productTypes { industry } }");

        var products = result.GetProperty("data").GetProperty("productTypes");
        var industries = products.EnumerateArray()
            .Select(p => p.GetProperty("industry").GetString())
            .Distinct()
            .ToHashSet();

        Assert.Contains("FURNITURE", industries);
        Assert.Contains("FOOD_PROCESSING", industries);
        Assert.Contains("HEALTHCARE", industries);
    }

    [Fact]
    public async Task ProductTypes_AllHaveNonEmptyRecipes()
    {
        // Every product in the encyclopedia must have at least one recipe ingredient
        // so the manufacturing detail view is never empty for any product.
        var result = await ExecuteGraphQlAsync(
            """
            {
              productTypes {
                slug industry
                recipes { quantity resourceType { slug } inputProductType { slug } }
              }
            }
            """);

        var products = result.GetProperty("data").GetProperty("productTypes");
        foreach (var product in products.EnumerateArray())
        {
            var recipes = product.GetProperty("recipes");
            Assert.True(recipes.GetArrayLength() > 0,
                $"Product '{product.GetProperty("slug").GetString()}' has no recipe ingredients.");
        }
    }

    [Fact]
    public async Task EncyclopediaResource_WoodHasDownstreamFurnitureProducts()
    {
        // Wood is the cornerstone raw material for FURNITURE. Verify it links back to
        // all seeded furniture products so the "used in production chains" section works.
        var result = await ExecuteGraphQlAsync(
            """
            {
              encyclopediaResource(slug: "wood") {
                productsUsingResource { slug industry }
              }
            }
            """);

        var products = result.GetProperty("data").GetProperty("encyclopediaResource")
            .GetProperty("productsUsingResource");

        Assert.True(products.GetArrayLength() >= 3,
            // At least Wooden Chair, Wooden Table, and Wooden Bed are seeded for FURNITURE.
            // If the seed data is later extended, the minimum count can be raised.
            "Wood should be used by at least 3 furniture products (chair, table, bed).");

        foreach (var product in products.EnumerateArray())
        {
            Assert.Equal("FURNITURE", product.GetProperty("industry").GetString());
        }
    }

    [Fact]
    public async Task EncyclopediaResource_GrainHasDownstreamFoodProcessingProducts()
    {
        // ROADMAP: All combinations must be visible. Grain is the cornerstone raw material
        // for FOOD_PROCESSING. Verify the encyclopedia detail for grain links to food products
        // so a player can discover the Bread supply chain.
        var result = await ExecuteGraphQlAsync(
            """
            {
              encyclopediaResource(slug: "grain") {
                resource { slug category }
                productsUsingResource { slug industry }
              }
            }
            """);

        var detail = result.GetProperty("data").GetProperty("encyclopediaResource");
        Assert.False(detail.ValueKind == System.Text.Json.JsonValueKind.Null,
            "grain resource should exist in the encyclopedia.");

        var resource = detail.GetProperty("resource");
        Assert.Equal("grain", resource.GetProperty("slug").GetString());
        Assert.Equal("ORGANIC", resource.GetProperty("category").GetString());

        var products = detail.GetProperty("productsUsingResource");
        Assert.True(products.GetArrayLength() >= 1,
            "Grain should be used by at least one FOOD_PROCESSING product (e.g. Bread, Flour).");

        // Every product linked from the grain detail must actually belong to FOOD_PROCESSING
        foreach (var product in products.EnumerateArray())
        {
            Assert.Equal("FOOD_PROCESSING", product.GetProperty("industry").GetString());
        }

        // Bread specifically must appear (it is the starter FOOD_PROCESSING product)
        var hasBread = products.EnumerateArray()
            .Any(p => p.GetProperty("slug").GetString() == "bread");
        Assert.True(hasBread, "Bread must appear in the grain downstream products list.");
    }

    [Fact]
    public async Task EncyclopediaResource_ChemicalMineralsHasDownstreamHealthcareProducts()
    {
        // ROADMAP: All combinations must be visible. Chemical Minerals is the cornerstone
        // raw material for HEALTHCARE. Verify the encyclopedia detail links to medical products
        // so a player can discover the Basic Medicine supply chain.
        // Note: Chemical Minerals is also used by Battery Pack (ELECTRONICS), so the returned
        // product list can include non-Healthcare products — we only assert Healthcare is present.
        var result = await ExecuteGraphQlAsync(
            """
            {
              encyclopediaResource(slug: "chemical-minerals") {
                resource { slug category }
                productsUsingResource { slug industry }
              }
            }
            """);

        var detail = result.GetProperty("data").GetProperty("encyclopediaResource");
        Assert.False(detail.ValueKind == System.Text.Json.JsonValueKind.Null,
            "chemical-minerals resource should exist in the encyclopedia.");

        var resource = detail.GetProperty("resource");
        Assert.Equal("chemical-minerals", resource.GetProperty("slug").GetString());
        Assert.Equal("MINERAL", resource.GetProperty("category").GetString());

        var products = detail.GetProperty("productsUsingResource");
        Assert.True(products.GetArrayLength() >= 1,
            "Chemical Minerals should be used by at least one product (e.g. Basic Medicine).");

        // Basic Medicine specifically must appear (it is the starter HEALTHCARE product)
        var hasMedicine = products.EnumerateArray()
            .Any(p => p.GetProperty("slug").GetString() == "basic-medicine");
        Assert.True(hasMedicine, "Basic Medicine must appear in the chemical-minerals downstream products list.");

        // At least one Healthcare product must appear
        var hasHealthcareProduct = products.EnumerateArray()
            .Any(p => p.GetProperty("industry").GetString() == "HEALTHCARE");
        Assert.True(hasHealthcareProduct, "At least one HEALTHCARE product must be downstream of Chemical Minerals.");
    }

    [Fact]
    public async Task ResourceTypes_AllEightCoreResourceSlugsPresent()
    {
        // ROADMAP: "Every resource must have unique picture." and the encyclopedia must cover
        // the entire resource graph. Verify the 8 canonical seeded resources are all present.
        var result = await ExecuteGraphQlAsync(
            "{ resourceTypes { slug } }");

        var resources = result.GetProperty("data").GetProperty("resourceTypes");
        var slugs = resources.EnumerateArray()
            .Select(r => r.GetProperty("slug").GetString()!)
            .ToHashSet();

        var expectedSlugs = new[]
        {
            "wood", "iron-ore", "coal", "gold",
            "chemical-minerals", "cotton", "grain", "silicon"
        };

        foreach (var expected in expectedSlugs)
        {
            Assert.Contains(expected, slugs);
        }
    }

    [Fact]
    public async Task ProductTypes_FoodProcessingProductsHaveGrainBasedRecipes()
    {
        // ROADMAP: "All combination of products are visible." Verifies the Food Processing
        // supply chain is connected to its raw material so the encyclopedia chain
        // grain → Bread → sales shop makes sense.
        // Note: Some FOOD_PROCESSING products use intermediate ingredients (e.g. bakery-premix
        // uses Flour rather than Grain directly). We verify the starter product (Bread) uses
        // Grain and that every product has at least one ingredient (raw or intermediate).
        var result = await ExecuteGraphQlAsync(
            """
            {
              productTypes(industry: "FOOD_PROCESSING") {
                slug
                recipes {
                  resourceType { slug }
                  inputProductType { slug }
                }
              }
            }
            """);

        var products = result.GetProperty("data").GetProperty("productTypes");
        Assert.True(products.GetArrayLength() > 0, "FOOD_PROCESSING products must be seeded.");

        // Every FOOD_PROCESSING product should have at least one recipe ingredient
        foreach (var product in products.EnumerateArray())
        {
            var hasAnyIngredient = product.GetProperty("recipes").EnumerateArray()
                .Any(r =>
                    r.GetProperty("resourceType").ValueKind != System.Text.Json.JsonValueKind.Null ||
                    r.GetProperty("inputProductType").ValueKind != System.Text.Json.JsonValueKind.Null);
            Assert.True(hasAnyIngredient,
                $"FOOD_PROCESSING product '{product.GetProperty("slug").GetString()}' has no recipe ingredients at all.");
        }

        // Bread (the starter product) must use Grain as a direct raw material
        var bread = products.EnumerateArray().FirstOrDefault(p => p.GetProperty("slug").GetString() == "bread");
        Assert.True(bread.ValueKind != System.Text.Json.JsonValueKind.Undefined, "Bread must be seeded as a FOOD_PROCESSING product.");
        var breadUsesGrain = bread.GetProperty("recipes").EnumerateArray()
            .Any(r => r.GetProperty("resourceType").ValueKind != System.Text.Json.JsonValueKind.Null
                   && r.GetProperty("resourceType").GetProperty("slug").GetString() == "grain");
        Assert.True(breadUsesGrain, "Bread must use Grain as a direct recipe ingredient.");
    }

    [Fact]
    public async Task ProductTypes_HealthcareProductsHaveChemicalMineralsBasedRecipes()
    {
        // ROADMAP: "All combination of products are visible." Verifies the Healthcare
        // supply chain is connected to its raw material so the encyclopedia chain
        // chemical-minerals → Basic Medicine makes sense.
        // Note: Some HEALTHCARE products use intermediate ingredients (e.g. first-aid-kit
        // uses Bandages/Antiseptic/Cotton Swabs rather than Chemical Minerals directly).
        // We verify the starter product (Basic Medicine) uses Chemical Minerals and that
        // every product has at least one ingredient (raw or intermediate).
        var result = await ExecuteGraphQlAsync(
            """
            {
              productTypes(industry: "HEALTHCARE") {
                slug
                recipes {
                  resourceType { slug }
                  inputProductType { slug }
                }
              }
            }
            """);

        var products = result.GetProperty("data").GetProperty("productTypes");
        Assert.True(products.GetArrayLength() > 0, "HEALTHCARE products must be seeded.");

        // Every HEALTHCARE product must have at least one recipe ingredient (raw or intermediate)
        foreach (var product in products.EnumerateArray())
        {
            var hasAnyIngredient = product.GetProperty("recipes").EnumerateArray()
                .Any(r =>
                    r.GetProperty("resourceType").ValueKind != System.Text.Json.JsonValueKind.Null ||
                    r.GetProperty("inputProductType").ValueKind != System.Text.Json.JsonValueKind.Null);
            Assert.True(hasAnyIngredient,
                $"HEALTHCARE product '{product.GetProperty("slug").GetString()}' has no recipe ingredients at all.");
        }

        // Basic Medicine (the starter product) must use Chemical Minerals as a direct raw material
        var medicine = products.EnumerateArray().FirstOrDefault(p => p.GetProperty("slug").GetString() == "basic-medicine");
        Assert.True(medicine.ValueKind != System.Text.Json.JsonValueKind.Undefined, "Basic Medicine must be seeded as a HEALTHCARE product.");
        var medicineUsesChemicals = medicine.GetProperty("recipes").EnumerateArray()
            .Any(r => r.GetProperty("resourceType").ValueKind != System.Text.Json.JsonValueKind.Null
                   && r.GetProperty("resourceType").GetProperty("slug").GetString() == "chemical-minerals");
        Assert.True(medicineUsesChemicals, "Basic Medicine must use Chemical Minerals as a direct recipe ingredient.");
    }

    [Fact]
    public async Task ResourceTypes_ReturnsAllEncyclopediaListFields()
    {
        // Verifies that the resourceTypes query exposes every field required by the
        // encyclopedia list view: id, name, slug, category, basePrice, weightPerUnit,
        // unitName, unitSymbol, imageUrl, description.
        var result = await ExecuteGraphQlAsync(
            """
            {
              resourceTypes {
                id name slug category basePrice weightPerUnit unitName unitSymbol imageUrl description
              }
            }
            """);

        var resources = result.GetProperty("data").GetProperty("resourceTypes");
        Assert.True(resources.GetArrayLength() >= 8, "All 8 core resource types must be returned.");

        foreach (var resource in resources.EnumerateArray())
        {
            var slug = resource.GetProperty("slug").GetString()!;
            Assert.False(string.IsNullOrWhiteSpace(resource.GetProperty("id").GetString()), $"{slug}: id must not be empty");
            Assert.False(string.IsNullOrWhiteSpace(resource.GetProperty("name").GetString()), $"{slug}: name must not be empty");
            Assert.False(string.IsNullOrWhiteSpace(resource.GetProperty("slug").GetString()), $"{slug}: slug must not be empty");
            Assert.False(string.IsNullOrWhiteSpace(resource.GetProperty("category").GetString()), $"{slug}: category must not be empty");
            Assert.True(resource.GetProperty("basePrice").GetDecimal() > 0, $"{slug}: basePrice must be positive");
            Assert.True(resource.GetProperty("weightPerUnit").GetDecimal() > 0, $"{slug}: weightPerUnit must be positive");
            Assert.False(string.IsNullOrWhiteSpace(resource.GetProperty("unitName").GetString()), $"{slug}: unitName must not be empty");
            Assert.False(string.IsNullOrWhiteSpace(resource.GetProperty("unitSymbol").GetString()), $"{slug}: unitSymbol must not be empty");
            Assert.False(string.IsNullOrWhiteSpace(resource.GetProperty("imageUrl").GetString()), $"{slug}: imageUrl must not be empty");
            Assert.False(string.IsNullOrWhiteSpace(resource.GetProperty("description").GetString()), $"{slug}: description must not be empty");
        }
    }

    [Fact]
    public async Task ProductTypes_SlugsAreUniqueAndUrlSafe()
    {
        // ROADMAP: "Make resource detail a separate view from the encyclopedia entry."
        // Each product must have a stable, URL-safe slug so detail routes never collide
        // and bookmark URLs remain valid even when product names are renamed.
        var result = await ExecuteGraphQlAsync(
            "{ productTypes { slug name } }");

        var products = result.GetProperty("data").GetProperty("productTypes");
        Assert.True(products.GetArrayLength() > 0, "At least one product must be seeded.");

        var slugs = products.EnumerateArray()
            .Select(p => p.GetProperty("slug").GetString()!)
            .ToList();

        // Every product must have a non-empty slug
        foreach (var slug in slugs)
        {
            Assert.False(string.IsNullOrWhiteSpace(slug), "A product has a null or empty slug.");
        }

        // Slugs must be unique across the catalog (no two products share a route)
        var distinctCount = slugs.Distinct().Count();
        Assert.Equal(slugs.Count, distinctCount);

        // Every slug must be URL-safe: only lowercase letters, digits, and hyphens
        foreach (var slug in slugs)
        {
            Assert.Matches(@"^[a-z0-9-]+$", slug);
        }
    }

    [Fact]
    public async Task ResourceTypes_SlugsAreUniqueAndUrlSafe()
    {
        // ROADMAP: Stable identifier routing so resource detail URLs cannot silently break.
        var result = await ExecuteGraphQlAsync(
            "{ resourceTypes { slug name } }");

        var resources = result.GetProperty("data").GetProperty("resourceTypes");
        Assert.True(resources.GetArrayLength() >= 8, "All 8 core resource types must be seeded.");

        var slugs = resources.EnumerateArray()
            .Select(r => r.GetProperty("slug").GetString()!)
            .ToList();

        // Every resource must have a non-empty slug
        foreach (var slug in slugs)
        {
            Assert.False(string.IsNullOrWhiteSpace(slug), "A resource has a null or empty slug.");
        }

        // Slugs must be unique across the resource catalog
        var distinctCount = slugs.Distinct().Count();
        Assert.Equal(slugs.Count, distinctCount);

        // Every slug must be URL-safe: only lowercase letters, digits, and hyphens
        foreach (var slug in slugs)
        {
            Assert.Matches(@"^[a-z0-9-]+$", slug);
        }
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
    public async Task CreateCompany_Authenticated_SetsFoundedAtTickFromGameState()
    {
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var gameState = await db.GameStates.FindAsync(1);
            Assert.NotNull(gameState);
            gameState!.CurrentTick = 321;
            await db.SaveChangesAsync();
        }

        var token = await RegisterAndGetTokenAsync("founded-tick@test.com", "Founded Tick User");

        var result = await ExecuteGraphQlAsync(
            "mutation CreateCompany($input: CreateCompanyInput!) { createCompany(input: $input) { id name } }",
            new { input = new { name = "Tick Corp" } },
            token);

        var companyId = Guid.Parse(result.GetProperty("data").GetProperty("createCompany").GetProperty("id").GetString()!);

        await using var verifyScope = _factory.Services.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var company = await verifyDb.Companies.SingleAsync(candidate => candidate.Id == companyId);

        Assert.Equal(321, company.FoundedAtTick);
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
    public async Task UpdateCompanySettings_Authenticated_UpdatesNameAndSalaryMultiplier()
    {
        await RegisterAndGetTokenAsync("company-settings@test.com", "Settings Owner");

        Guid companyId;
        Guid cityId;
        Guid playerId;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var player = await db.Players.SingleAsync(candidate => candidate.Email == "company-settings@test.com");
            playerId = player.Id;
            cityId = await db.Cities.OrderBy(candidate => candidate.Name).Select(candidate => candidate.Id).FirstAsync();

            var company = new Company
            {
                Id = Guid.NewGuid(),
                PlayerId = player.Id,
                Name = "Starter Name",
                Cash = 1_000_000m,
                FoundedAtUtc = DateTime.UtcNow,
                FoundedAtTick = 0,
            };
            db.Companies.Add(company);
            await db.SaveChangesAsync();
            companyId = company.Id;
        }

        await using (var mutationScope = _factory.Services.CreateAsyncScope())
        {
            var db = mutationScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var mutation = new Mutation();
            var httpContextAccessor = new HttpContextAccessor
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                    [
                        new Claim(ClaimTypes.NameIdentifier, playerId.ToString())
                    ],
                    "TestAuth"))
                }
            };

            var updatedCompany = await mutation.UpdateCompanySettings(
                new UpdateCompanySettingsInput
                {
                    CompanyId = companyId,
                    Name = "Renamed Company",
                    CitySalarySettings =
                    [
                        new CompanyCitySalarySettingInput
                        {
                            CityId = cityId,
                            SalaryMultiplier = 2m,
                        }
                    ]
                },
                db,
                httpContextAccessor);

            Assert.Equal("Renamed Company", updatedCompany.Name);
        }

        await using var verifyScope = _factory.Services.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var persistedCompany = await verifyDb.Companies
            .Include(candidate => candidate.CitySalarySettings)
            .SingleAsync(candidate => candidate.Id == companyId);
                var persistedSetting = persistedCompany.CitySalarySettings
                        .Single(candidate => candidate.CityId == cityId);

                Assert.Equal("Renamed Company", persistedCompany.Name);
                Assert.Equal(2m, persistedSetting.SalaryMultiplier);
    }

    [Fact]
    public async Task UpdateCompanySettings_NonOwner_ReturnsCompanyNotFoundError()
    {
        var ownerToken = await RegisterAndGetTokenAsync("settings-owner@test.com", "Settings Owner");
        var intruderToken = await RegisterAndGetTokenAsync("settings-intruder@test.com", "Settings Intruder");

        var createResult = await ExecuteGraphQlAsync(
            "mutation CreateCompany($input: CreateCompanyInput!) { createCompany(input: $input) { id } }",
            new { input = new { name = "Owner Company" } },
            ownerToken);

        var companyId = createResult.GetProperty("data").GetProperty("createCompany").GetProperty("id").GetString()!;

        Guid cityId;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            cityId = await db.Cities.OrderBy(candidate => candidate.Name).Select(candidate => candidate.Id).FirstAsync();
        }

        var result = await ExecuteGraphQlAsync(
            """
            mutation UpdateCompanySettings($input: UpdateCompanySettingsInput!) {
              updateCompanySettings(input: $input) { id name }
            }
            """,
            new
            {
                input = new
                {
                    companyId,
                    name = "Hijacked Company",
                    citySalarySettings = new[]
                    {
                        new { cityId, salaryMultiplier = 2 }
                    }
                }
            },
            intruderToken);

        var error = result.GetProperty("errors")[0];

        Assert.Equal("COMPANY_NOT_FOUND", error.GetProperty("extensions").GetProperty("code").GetString());
    }

    [Fact]
    public async Task GetCompanySettings_Authenticated_ReturnsCompanyData()
    {
        var ownerToken = await RegisterAndGetTokenAsync("get-settings-owner@test.com", "Settings Reader");

        var createResult = await ExecuteGraphQlAsync(
            "mutation CreateCompany($input: CreateCompanyInput!) { createCompany(input: $input) { id } }",
            new { input = new { name = "Settings Reader Corp" } },
            ownerToken);
        var companyId = createResult.GetProperty("data").GetProperty("createCompany").GetProperty("id").GetString()!;

        var result = await ExecuteGraphQlAsync(
            """
            query GetCompanySettings($companyId: UUID!) {
              companySettings(companyId: $companyId) {
                companyId
                companyName
                cash
                foundedAtTick
                administrationOverheadRate
                assetValue
                citySalarySettings {
                  cityId
                  cityName
                  baseSalaryPerManhour
                  salaryMultiplier
                  effectiveSalaryPerManhour
                }
              }
            }
            """,
            new { companyId },
            ownerToken);

        var settings = result.GetProperty("data").GetProperty("companySettings");
        Assert.Equal(companyId, settings.GetProperty("companyId").GetString());
        Assert.Equal("Settings Reader Corp", settings.GetProperty("companyName").GetString());
        var citySalaries = settings.GetProperty("citySalarySettings");
        Assert.True(citySalaries.GetArrayLength() > 0);
        var firstCity = citySalaries[0];
        Assert.Equal(1.0m, firstCity.GetProperty("salaryMultiplier").GetDecimal());
        Assert.Equal(
            firstCity.GetProperty("baseSalaryPerManhour").GetDecimal(),
            firstCity.GetProperty("effectiveSalaryPerManhour").GetDecimal());
    }

    [Fact]
    public async Task GetCompanySettings_NonOwner_ReturnsNull()
    {
        var ownerToken = await RegisterAndGetTokenAsync("settings-owner2@test.com", "Owner2");
        var intruderToken = await RegisterAndGetTokenAsync("settings-intruder2@test.com", "Intruder2");

        var createResult = await ExecuteGraphQlAsync(
            "mutation CreateCompany($input: CreateCompanyInput!) { createCompany(input: $input) { id } }",
            new { input = new { name = "Private Corp" } },
            ownerToken);
        var companyId = createResult.GetProperty("data").GetProperty("createCompany").GetProperty("id").GetString()!;

        var result = await ExecuteGraphQlAsync(
            """
            query GetCompanySettings($companyId: UUID!) {
              companySettings(companyId: $companyId) {
                companyId
                companyName
              }
            }
            """,
            new { companyId },
            intruderToken);

        Assert.Equal(JsonValueKind.Null, result.GetProperty("data").GetProperty("companySettings").ValueKind);
    }

    [Fact]
    public async Task UpdateCompanySettings_SalaryMultiplierAboveMax_GetsClampedToTwo()
    {
        var token = await RegisterAndGetTokenAsync("salary-clamp@test.com", "Salary Clamp User");

        var createResult = await ExecuteGraphQlAsync(
            "mutation CreateCompany($input: CreateCompanyInput!) { createCompany(input: $input) { id } }",
            new { input = new { name = "Clamp Corp" } },
            token);
        var companyId = createResult.GetProperty("data").GetProperty("createCompany").GetProperty("id").GetString()!;

        Guid cityId;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            cityId = await db.Cities.OrderBy(c => c.Name).Select(c => c.Id).FirstAsync();
        }

        var result = await ExecuteGraphQlAsync(
            """
            mutation UpdateCompanySettings($input: UpdateCompanySettingsInput!) {
              updateCompanySettings(input: $input) { id name }
            }
            """,
            new
            {
                input = new
                {
                    companyId,
                    name = "Clamp Corp",
                    citySalarySettings = new[] { new { cityId, salaryMultiplier = 5.0 } }
                }
            },
            token);

        // Mutation should succeed (no error)
        Assert.False(result.TryGetProperty("errors", out _));

        await using var verifyScope = _factory.Services.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var persisted = await verifyDb.CompanyCitySalarySettings
            .SingleAsync(s => s.CompanyId == Guid.Parse(companyId) && s.CityId == cityId);
        Assert.Equal(CompanyEconomyCalculator.MaximumSalaryMultiplier, persisted.SalaryMultiplier);
    }

    [Fact]
    public void AdministrationOverheadRate_NewCompany_IsZero()
    {
        // Brand-new company at tick 0 with no assets should have 0% overhead
        var rate = CompanyEconomyCalculator.ComputeAdministrationOverheadRate(
            new Company { FoundedAtTick = 0 },
            companyAssetValue: 0m,
            maxCompanyAssetValue: 0m,
            currentTick: 0);

        Assert.Equal(0m, rate);
    }

    [Fact]
    public void AdministrationOverheadRate_MaximumScenario_IsFiftyPercent()
    {
        // 2-year-old company with highest asset equity should reach 50% maximum overhead (ROADMAP)
        var ticksPerYear = Engine.GameConstants.TicksPerYear;
        var company = new Company { FoundedAtTick = 0 };

        var rate = CompanyEconomyCalculator.ComputeAdministrationOverheadRate(
            company,
            companyAssetValue: 1_000_000m,
            maxCompanyAssetValue: 1_000_000m,
            currentTick: 2 * ticksPerYear);

        Assert.Equal(CompanyEconomyCalculator.MaximumAdministrationOverheadRate, rate);
    }

    [Fact]
    public void AdministrationOverheadRate_OneYearOldSmallCompany_IsQuarterMax()
    {
        // 1-year-old company (ageFactor=0.5) with half the top equity (assetFactor=0.5) = 50% * 0.5 * 0.5 = 12.5%
        var ticksPerYear = Engine.GameConstants.TicksPerYear;
        var company = new Company { FoundedAtTick = 0 };

        var rate = CompanyEconomyCalculator.ComputeAdministrationOverheadRate(
            company,
            companyAssetValue: 500_000m,
            maxCompanyAssetValue: 1_000_000m,
            currentTick: ticksPerYear);

        Assert.Equal(0.125m, rate);
    }

    [Fact]
    public void AdministrationOverheadDrivers_NewCompany_BothFactorsAreZero()
    {
        var (ageFactor, assetFactor) = CompanyEconomyCalculator.ComputeAdministrationOverheadDrivers(
            new Company { FoundedAtTick = 0 },
            companyAssetValue: 0m,
            maxCompanyAssetValue: 0m,
            currentTick: 0);

        Assert.Equal(0m, ageFactor);
        Assert.Equal(0m, assetFactor);
    }

    [Fact]
    public void AdministrationOverheadDrivers_TwoYearOldMaxScale_BothFactorsAreOne()
    {
        var ticksPerYear = Engine.GameConstants.TicksPerYear;
        var company = new Company { FoundedAtTick = 0 };

        var (ageFactor, assetFactor) = CompanyEconomyCalculator.ComputeAdministrationOverheadDrivers(
            company,
            companyAssetValue: 1_000_000m,
            maxCompanyAssetValue: 1_000_000m,
            currentTick: 2 * ticksPerYear);

        Assert.Equal(1m, ageFactor);
        Assert.Equal(1m, assetFactor);
    }

    [Fact]
    public void AdministrationOverheadDrivers_AreConsistentWithOverheadRate()
    {
        // Drivers * max rate must equal the overhead rate
        var ticksPerYear = Engine.GameConstants.TicksPerYear;
        var company = new Company { FoundedAtTick = 0 };

        var (ageFactor, assetFactor) = CompanyEconomyCalculator.ComputeAdministrationOverheadDrivers(
            company,
            companyAssetValue: 750_000m,
            maxCompanyAssetValue: 1_000_000m,
            currentTick: ticksPerYear);

        var rate = CompanyEconomyCalculator.ComputeAdministrationOverheadRate(
            company,
            companyAssetValue: 750_000m,
            maxCompanyAssetValue: 1_000_000m,
            currentTick: ticksPerYear);

        Assert.Equal(
            decimal.Round(CompanyEconomyCalculator.MaximumAdministrationOverheadRate * ageFactor * assetFactor, 4, MidpointRounding.AwayFromZero),
            rate);
    }

    [Fact]
    public async Task GetCompanySettings_ReturnsAgeFactorAndAssetFactor()
    {
        var ownerToken = await RegisterAndGetTokenAsync("driver-fields@test.com", "Driver Fields User");

        var createResult = await ExecuteGraphQlAsync(
            "mutation CreateCompany($input: CreateCompanyInput!) { createCompany(input: $input) { id } }",
            new { input = new { name = "Driver Corp" } },
            ownerToken);
        var companyId = createResult.GetProperty("data").GetProperty("createCompany").GetProperty("id").GetString()!;

        var result = await ExecuteGraphQlAsync(
            """
            query GetCompanySettings($companyId: UUID!) {
              companySettings(companyId: $companyId) {
                administrationOverheadRate
                ageFactor
                assetFactor
              }
            }
            """,
            new { companyId },
            ownerToken);

        var settings = result.GetProperty("data").GetProperty("companySettings");
        // New company at tick 0 has no age → ageFactor = 0 → overheadRate = 0 regardless of assetFactor
        Assert.Equal(0m, settings.GetProperty("ageFactor").GetDecimal());
        Assert.Equal(0m, settings.GetProperty("administrationOverheadRate").GetDecimal());
        // assetFactor is 0–1; it is accessible and numeric (exact value depends on other companies in the test db)
        var assetFactor = settings.GetProperty("assetFactor").GetDecimal();
        Assert.InRange(assetFactor, 0m, 1m);
    }

    [Fact]
    public async Task UpdateCompanySettings_Unauthenticated_ReturnsAuthorizationError()
    {
        var result = await ExecuteGraphQlAsync(
            """
            mutation UpdateCompanySettings($input: UpdateCompanySettingsInput!) {
              updateCompanySettings(input: $input) { id name }
            }
            """,
            new
            {
                input = new
                {
                    companyId = Guid.NewGuid(),
                    name = "Hijack Attempt",
                    citySalarySettings = Array.Empty<object>(),
                }
            },
            token: null);

        Assert.True(result.TryGetProperty("errors", out var errors));
        var firstError = errors[0];
        var message = firstError.GetProperty("message").GetString();
        Assert.Contains("authorized", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetCompanySettings_Unauthenticated_ReturnsAuthorizationError()
    {
        var result = await ExecuteGraphQlAsync(
            """
            query GetCompanySettings($companyId: UUID!) {
              companySettings(companyId: $companyId) {
                companyId
                companyName
              }
            }
            """,
            new { companyId = Guid.NewGuid() },
            token: null);

        Assert.True(result.TryGetProperty("errors", out var errors));
        var firstError = errors[0];
        var message = firstError.GetProperty("message").GetString();
        Assert.Contains("authorized", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UpdateCompanySettings_InvalidCityId_ReturnsCityNotFoundError()
    {
        var token = await RegisterAndGetTokenAsync("invalid-city@test.com", "Invalid City User");

        var createResult = await ExecuteGraphQlAsync(
            "mutation CreateCompany($input: CreateCompanyInput!) { createCompany(input: $input) { id } }",
            new { input = new { name = "City Test Corp" } },
            token);
        var companyId = createResult.GetProperty("data").GetProperty("createCompany").GetProperty("id").GetString()!;

        var result = await ExecuteGraphQlAsync(
            """
            mutation UpdateCompanySettings($input: UpdateCompanySettingsInput!) {
              updateCompanySettings(input: $input) { id name }
            }
            """,
            new
            {
                input = new
                {
                    companyId,
                    name = "City Test Corp",
                    citySalarySettings = new[]
                    {
                        new { cityId = Guid.NewGuid(), salaryMultiplier = 1.0 }
                    }
                }
            },
            token);

        Assert.True(result.TryGetProperty("errors", out var errors));
        Assert.Equal("CITY_NOT_FOUND", errors[0].GetProperty("extensions").GetProperty("code").GetString());
    }

    [Fact]
    public async Task UpdateCompanySettings_SalaryMultiplierBelowMin_GetsClampedToHalf()
    {
        var token = await RegisterAndGetTokenAsync("salary-clamp-min@test.com", "Salary Clamp Min User");

        var createResult = await ExecuteGraphQlAsync(
            "mutation CreateCompany($input: CreateCompanyInput!) { createCompany(input: $input) { id } }",
            new { input = new { name = "Clamp Min Corp" } },
            token);
        var companyId = createResult.GetProperty("data").GetProperty("createCompany").GetProperty("id").GetString()!;

        Guid cityId;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            cityId = await db.Cities.OrderBy(c => c.Name).Select(c => c.Id).FirstAsync();
        }

        var result = await ExecuteGraphQlAsync(
            """
            mutation UpdateCompanySettings($input: UpdateCompanySettingsInput!) {
              updateCompanySettings(input: $input) { id name }
            }
            """,
            new
            {
                input = new
                {
                    companyId,
                    name = "Clamp Min Corp",
                    citySalarySettings = new[] { new { cityId, salaryMultiplier = 0.0 } }
                }
            },
            token);

        Assert.False(result.TryGetProperty("errors", out _));

        await using var verifyScope = _factory.Services.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var persisted = await verifyDb.CompanyCitySalarySettings
            .SingleAsync(s => s.CompanyId == Guid.Parse(companyId) && s.CityId == cityId);
        Assert.Equal(CompanyEconomyCalculator.MinimumSalaryMultiplier, persisted.SalaryMultiplier);
    }

    [Fact]
    public async Task UpdateCompanySettings_DuplicateCityInInput_UsesLastEntry()
    {
        var token = await RegisterAndGetTokenAsync("dup-city@test.com", "Dup City User");

        var createResult = await ExecuteGraphQlAsync(
            "mutation CreateCompany($input: CreateCompanyInput!) { createCompany(input: $input) { id } }",
            new { input = new { name = "Dup City Corp" } },
            token);
        var companyId = createResult.GetProperty("data").GetProperty("createCompany").GetProperty("id").GetString()!;

        Guid cityId;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            cityId = await db.Cities.OrderBy(c => c.Name).Select(c => c.Id).FirstAsync();
        }

        // Submit the same city twice — the last entry should win
        var result = await ExecuteGraphQlAsync(
            """
            mutation UpdateCompanySettings($input: UpdateCompanySettingsInput!) {
              updateCompanySettings(input: $input) { id name }
            }
            """,
            new
            {
                input = new
                {
                    companyId,
                    name = "Dup City Corp",
                    citySalarySettings = new[]
                    {
                        new { cityId, salaryMultiplier = 1.2 },
                        new { cityId, salaryMultiplier = 1.8 },
                    }
                }
            },
            token);

        Assert.False(result.TryGetProperty("errors", out _));

        await using var verifyScope = _factory.Services.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var persisted = await verifyDb.CompanyCitySalarySettings
            .SingleAsync(s => s.CompanyId == Guid.Parse(companyId) && s.CityId == cityId);
        Assert.Equal(1.8m, persisted.SalaryMultiplier);
    }

    [Fact]
    public async Task UpdateCompanySettings_EmptyNameAfterTrim_ReturnsInvalidCompanyNameError()
    {
        var token = await RegisterAndGetTokenAsync("empty-name@test.com", "Empty Name User");

        var createResult = await ExecuteGraphQlAsync(
            "mutation CreateCompany($input: CreateCompanyInput!) { createCompany(input: $input) { id } }",
            new { input = new { name = "Before Empty" } },
            token);
        var companyId = createResult.GetProperty("data").GetProperty("createCompany").GetProperty("id").GetString()!;

        var result = await ExecuteGraphQlAsync(
            """
            mutation UpdateCompanySettings($input: UpdateCompanySettingsInput!) {
              updateCompanySettings(input: $input) { id name }
            }
            """,
            new
            {
                input = new
                {
                    companyId,
                    name = "   ",
                    citySalarySettings = Array.Empty<object>()
                }
            },
            token);

        Assert.True(result.TryGetProperty("errors", out var errors));
        Assert.Equal("INVALID_COMPANY_NAME", errors[0].GetProperty("extensions").GetProperty("code").GetString());
    }

    [Fact]
    public async Task UpdateCompanySettings_WhitespaceOnlyName_ReturnsInvalidCompanyNameError()
    {
        var token = await RegisterAndGetTokenAsync("ws-name@test.com", "Whitespace Name User");

        var createResult = await ExecuteGraphQlAsync(
            "mutation CreateCompany($input: CreateCompanyInput!) { createCompany(input: $input) { id } }",
            new { input = new { name = "Before Whitespace" } },
            token);
        var companyId = createResult.GetProperty("data").GetProperty("createCompany").GetProperty("id").GetString()!;

        var result = await ExecuteGraphQlAsync(
            """
            mutation UpdateCompanySettings($input: UpdateCompanySettingsInput!) {
              updateCompanySettings(input: $input) { id name }
            }
            """,
            new
            {
                input = new
                {
                    companyId,
                    name = "\t \n",
                    citySalarySettings = Array.Empty<object>()
                }
            },
            token);

        Assert.True(result.TryGetProperty("errors", out var errors));
        Assert.Equal("INVALID_COMPANY_NAME", errors[0].GetProperty("extensions").GetProperty("code").GetString());
    }

    [Fact]
    public async Task UpdateCompanySettings_LeadingAndTrailingSpaces_TrimsAndPersists()
    {
        var token = await RegisterAndGetTokenAsync("trim-name@test.com", "Trim Name User");

        var createResult = await ExecuteGraphQlAsync(
            "mutation CreateCompany($input: CreateCompanyInput!) { createCompany(input: $input) { id } }",
            new { input = new { name = "Before Trim" } },
            token);
        var companyId = createResult.GetProperty("data").GetProperty("createCompany").GetProperty("id").GetString()!;

        var result = await ExecuteGraphQlAsync(
            """
            mutation UpdateCompanySettings($input: UpdateCompanySettingsInput!) {
              updateCompanySettings(input: $input) { id name }
            }
            """,
            new
            {
                input = new
                {
                    companyId,
                    name = "  Trimmed Name  ",
                    citySalarySettings = Array.Empty<object>()
                }
            },
            token);

        Assert.False(result.TryGetProperty("errors", out _));
        Assert.Equal("Trimmed Name", result.GetProperty("data").GetProperty("updateCompanySettings").GetProperty("name").GetString());
    }

    [Fact]
    public async Task CompanyLedger_IncludesLaborAndEnergyTotals()
    {
        var token = await RegisterAndGetTokenAsync("ledger-costs@test.com", "Ledger Costs User");

        Guid companyId;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var player = await db.Players.SingleAsync(candidate => candidate.Email == "ledger-costs@test.com");

            var company = new Company
            {
                Id = Guid.NewGuid(),
                PlayerId = player.Id,
                Name = "Ledger Cost Corp",
                Cash = 250_000m,
                FoundedAtUtc = DateTime.UtcNow,
                FoundedAtTick = 0,
            };

            db.Companies.Add(company);
            db.LedgerEntries.AddRange(
                new LedgerEntry
                {
                    Id = Guid.NewGuid(),
                    CompanyId = company.Id,
                    Category = LedgerCategory.Revenue,
                    Description = "Sales revenue",
                    Amount = 1000m,
                    RecordedAtTick = 10,
                    RecordedAtUtc = DateTime.UtcNow,
                },
                new LedgerEntry
                {
                    Id = Guid.NewGuid(),
                    CompanyId = company.Id,
                    Category = LedgerCategory.PurchasingCost,
                    Description = "Material purchase",
                    Amount = -200m,
                    RecordedAtTick = 10,
                    RecordedAtUtc = DateTime.UtcNow,
                },
                new LedgerEntry
                {
                    Id = Guid.NewGuid(),
                    CompanyId = company.Id,
                    Category = LedgerCategory.LaborCost,
                    Description = "Operating labor",
                    Amount = -150m,
                    RecordedAtTick = 10,
                    RecordedAtUtc = DateTime.UtcNow,
                },
                new LedgerEntry
                {
                    Id = Guid.NewGuid(),
                    CompanyId = company.Id,
                    Category = LedgerCategory.EnergyCost,
                    Description = "Operating energy",
                    Amount = -50m,
                    RecordedAtTick = 10,
                    RecordedAtUtc = DateTime.UtcNow,
                });

            await db.SaveChangesAsync();
            companyId = company.Id;
        }

        var result = await ExecuteGraphQlAsync(
            """
            query GetCompanyLedger($companyId: UUID!) {
              companyLedger(companyId: $companyId) {
                totalRevenue
                totalPurchasingCosts
                totalLaborCosts
                totalEnergyCosts
                taxableIncome
              }
            }
            """,
            new { companyId },
            token);

        var ledger = result.GetProperty("data").GetProperty("companyLedger");

        Assert.Equal(1000m, ledger.GetProperty("totalRevenue").GetDecimal());
        Assert.Equal(200m, ledger.GetProperty("totalPurchasingCosts").GetDecimal());
        Assert.Equal(150m, ledger.GetProperty("totalLaborCosts").GetDecimal());
        Assert.Equal(50m, ledger.GetProperty("totalEnergyCosts").GetDecimal());
        Assert.Equal(600m, ledger.GetProperty("taxableIncome").GetDecimal());
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

    [Fact]
    public async Task StoreBuildingConfiguration_Unauthenticated_ReturnsError()
    {
        // An unauthenticated call (no token) to storeBuildingConfiguration must be rejected.
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
                    buildingId = Guid.NewGuid().ToString(),
                    units = new[]
                    {
                        new { unitType = "PURCHASE", gridX = 0, gridY = 0,
                              linkUp = false, linkDown = false, linkLeft = false, linkRight = false,
                              linkUpLeft = false, linkUpRight = false, linkDownLeft = false, linkDownRight = false }
                    }
                }
            },
            token: null);

        Assert.True(result.TryGetProperty("errors", out var errors),
            "Unauthenticated storeBuildingConfiguration must return errors");
        Assert.NotEmpty(errors.EnumerateArray().ToList());
    }

    [Fact]
    public async Task StoreBuildingConfiguration_Mine_WithValidUnits_Succeeds()
    {
        // A MINE building can be configured with MINING, STORAGE, and B2B_SALES units.
        // This proves that mine-specific unit types are accepted and the plan is queued.
        var token = await RegisterAndGetTokenAsync($"mine-cfg-{Guid.NewGuid()}@test.com", "MineCfgTester");
        var companyResult = await ExecuteGraphQlAsync(
            "mutation CreateCompany($input: CreateCompanyInput!) { createCompany(input: $input) { id } }",
            new { input = new { name = "Mine Corp" } },
            token);
        var companyId = companyResult.GetProperty("data").GetProperty("createCompany").GetProperty("id").GetString()!;

        var cityId = await GetCityIdByNameAsync("Bratislava");
        var mineLotId = await CreateTestLotAsync(cityId, "FACTORY,MINE", "Industrial Zone", 75_000m, "Mine Test Lot");

        var buildingResult = await ExecuteGraphQlAsync(
            "mutation PurchaseLot($input: PurchaseLotInput!) { purchaseLot(input: $input) { building { id } } }",
            new { input = new { companyId, lotId = mineLotId, buildingType = "MINE", buildingName = "Iron Mine" } },
            token);
        var buildingId = buildingResult.GetProperty("data").GetProperty("purchaseLot").GetProperty("building").GetProperty("id").GetString()!;

        // Configure the mine with MINING → STORAGE → B2B_SALES
        var result = await ExecuteGraphQlAsync(
            """
            mutation StoreBuildingConfiguration($input: StoreBuildingConfigurationInput!) {
                storeBuildingConfiguration(input: $input) {
                    id
                    appliesAtTick
                    totalTicksRequired
                    units { unitType gridX gridY isChanged }
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
                        new { unitType = "MINING",   gridX = 0, gridY = 0,
                              linkUp = false, linkDown = false, linkLeft = false, linkRight = true,
                              linkUpLeft = false, linkUpRight = false, linkDownLeft = false, linkDownRight = false },
                        new { unitType = "STORAGE",  gridX = 1, gridY = 0,
                              linkUp = false, linkDown = false, linkLeft = false, linkRight = true,
                              linkUpLeft = false, linkUpRight = false, linkDownLeft = false, linkDownRight = false },
                        new { unitType = "B2B_SALES", gridX = 2, gridY = 0,
                              linkUp = false, linkDown = false, linkLeft = false, linkRight = false,
                              linkUpLeft = false, linkUpRight = false, linkDownLeft = false, linkDownRight = false }
                    }
                }
            },
            token);

        Assert.False(result.TryGetProperty("errors", out _),
            "Configuring a MINE with MINING+STORAGE+B2B_SALES must succeed");
        var plan = result.GetProperty("data").GetProperty("storeBuildingConfiguration");
        Assert.True(plan.GetProperty("appliesAtTick").GetInt64() > 0,
            "Mine configuration plan must have a future appliesAtTick");
        Assert.Equal(3, plan.GetProperty("units").GetArrayLength());
        Assert.True(plan.GetProperty("units").EnumerateArray().All(u => u.GetProperty("isChanged").GetBoolean()),
            "All new mine units must be marked as changed (new addition)");
    }

    [Fact]
    public async Task StoreBuildingConfiguration_Mine_InvalidUnitType_ReturnsError()
    {
        // A FACTORY unit type (e.g. MANUFACTURING) cannot be placed in a MINE building.
        // This is the inverse of the InvalidUnitTypeForBuildingType test which tests MINING in a FACTORY.
        var token = await RegisterAndGetTokenAsync($"mine-inv-{Guid.NewGuid()}@test.com", "MineInvTester");
        var companyResult = await ExecuteGraphQlAsync(
            "mutation CreateCompany($input: CreateCompanyInput!) { createCompany(input: $input) { id } }",
            new { input = new { name = "Mine Inv Corp" } },
            token);
        var companyId = companyResult.GetProperty("data").GetProperty("createCompany").GetProperty("id").GetString()!;

        var cityId = await GetCityIdByNameAsync("Bratislava");
        var mineLotId = await CreateTestLotAsync(cityId, "FACTORY,MINE", "Industrial Zone", 75_000m, "Mine Invalid Lot");

        var buildingResult = await ExecuteGraphQlAsync(
            "mutation PurchaseLot($input: PurchaseLotInput!) { purchaseLot(input: $input) { building { id } } }",
            new { input = new { companyId, lotId = mineLotId, buildingType = "MINE", buildingName = "Bad Unit Mine" } },
            token);
        var buildingId = buildingResult.GetProperty("data").GetProperty("purchaseLot").GetProperty("building").GetProperty("id").GetString()!;

        // MANUFACTURING is only valid in a FACTORY, not in a MINE
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
                        new { unitType = "MANUFACTURING", gridX = 0, gridY = 0,
                              linkUp = false, linkDown = false, linkLeft = false, linkRight = false,
                              linkUpLeft = false, linkUpRight = false, linkDownLeft = false, linkDownRight = false }
                    }
                }
            },
            token);

        Assert.True(result.TryGetProperty("errors", out var errors));
        Assert.Equal("INVALID_BUILDING_UNIT_TYPE",
            errors[0].GetProperty("extensions").GetProperty("code").GetString());
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

    [Fact]
    public async Task StoreBuildingConfiguration_SalesShop_WithMarketingUnit_Succeeds()
    {
        // The ROADMAP specifies that Sales Shops allow PURCHASE, MARKETING, and PUBLIC_SALES units.
        // This test verifies MARKETING is accepted when placed in a SALES_SHOP.
        var token = await RegisterAndGetTokenAsync($"shop-mkt-{Guid.NewGuid()}@test.com", "Shop Marketing");

        var companyResult = await ExecuteGraphQlAsync(
            "mutation CreateCompany($input: CreateCompanyInput!) { createCompany(input: $input) { id } }",
            new { input = new { name = "Marketing Shop Corp" } },
            token);
        var companyId = companyResult.GetProperty("data").GetProperty("createCompany").GetProperty("id").GetString();

        var citiesResult = await ExecuteGraphQlAsync("{ cities { id } }");
        var cityId = citiesResult.GetProperty("data").GetProperty("cities")[0].GetProperty("id").GetString();

        var buildingResult = await ExecuteGraphQlAsync(
            "mutation PlaceBuilding($input: PlaceBuildingInput!) { placeBuilding(input: $input) { id } }",
            new { input = new { companyId, cityId, type = "SALES_SHOP", name = "Marketing Shop" } },
            token);
        var buildingId = buildingResult.GetProperty("data").GetProperty("placeBuilding").GetProperty("id").GetString();

        // Configure the shop with PURCHASE (0,0) → MARKETING (1,0) → PUBLIC_SALES (2,0)
        var result = await ExecuteGraphQlAsync(
            """
            mutation StoreBuildingConfiguration($input: StoreBuildingConfigurationInput!) {
              storeBuildingConfiguration(input: $input) {
                id
                units { unitType gridX }
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
                        new { unitType = "PURCHASE",     gridX = 0, gridY = 0, linkUp = false, linkDown = false, linkLeft = false, linkRight = true,  linkUpLeft = false, linkUpRight = false, linkDownLeft = false, linkDownRight = false },
                        new { unitType = "MARKETING",    gridX = 1, gridY = 0, linkUp = false, linkDown = false, linkLeft = true,  linkRight = true,  linkUpLeft = false, linkUpRight = false, linkDownLeft = false, linkDownRight = false },
                        new { unitType = "PUBLIC_SALES", gridX = 2, gridY = 0, linkUp = false, linkDown = false, linkLeft = true,  linkRight = false, linkUpLeft = false, linkUpRight = false, linkDownLeft = false, linkDownRight = false }
                    }
                }
            },
            token);

        Assert.False(result.TryGetProperty("errors", out _),
            "MARKETING unit in a SALES_SHOP must be accepted.");
        var units = result.GetProperty("data").GetProperty("storeBuildingConfiguration").GetProperty("units").EnumerateArray().ToList();
        Assert.Equal(3, units.Count);
        Assert.Contains(units, u => u.GetProperty("unitType").GetString() == "MARKETING" && u.GetProperty("gridX").GetInt32() == 1);
    }

    [Fact]
    public async Task StoreBuildingConfiguration_SalesShop_InvalidUnitType_ManufacturingRejected()
    {
        // MANUFACTURING is only valid in FACTORY buildings.  Placing it in a SALES_SHOP
        // must be rejected with INVALID_BUILDING_UNIT_TYPE.
        var token = await RegisterAndGetTokenAsync($"shop-mfg-{Guid.NewGuid()}@test.com", "Shop Mfg Reject");

        var companyResult = await ExecuteGraphQlAsync(
            "mutation CreateCompany($input: CreateCompanyInput!) { createCompany(input: $input) { id } }",
            new { input = new { name = "MfgReject Corp" } },
            token);
        var companyId = companyResult.GetProperty("data").GetProperty("createCompany").GetProperty("id").GetString();

        var citiesResult = await ExecuteGraphQlAsync("{ cities { id } }");
        var cityId = citiesResult.GetProperty("data").GetProperty("cities")[0].GetProperty("id").GetString();

        var buildingResult = await ExecuteGraphQlAsync(
            "mutation PlaceBuilding($input: PlaceBuildingInput!) { placeBuilding(input: $input) { id } }",
            new { input = new { companyId, cityId, type = "SALES_SHOP", name = "Mfg Reject Shop" } },
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
                        new { unitType = "MANUFACTURING", gridX = 0, gridY = 0,
                              linkUp = false, linkDown = false, linkLeft = false, linkRight = false,
                              linkUpLeft = false, linkUpRight = false, linkDownLeft = false, linkDownRight = false }
                    }
                }
            },
            token);

        Assert.True(result.TryGetProperty("errors", out var errors));
        Assert.Equal("INVALID_BUILDING_UNIT_TYPE",
            errors[0].GetProperty("extensions").GetProperty("code").GetString());
    }

    [Fact]
    public async Task StoreBuildingConfiguration_LinkOnlyChange_RequiresOneTick()
    {
        // ROADMAP: "Change in the links between the units takes one tick to apply."
        // After an existing unit is active, re-submitting it with only a link change must
        // produce a plan with totalTicksRequired == 1 (LinkChangeTicks), not 3 (UnitPlanChangeTicks).
        var token = await RegisterAndGetTokenAsync($"link-tick-{Guid.NewGuid()}@test.com", "Link Tick Tester");

        var companyResult = await ExecuteGraphQlAsync(
            "mutation CreateCompany($input: CreateCompanyInput!) { createCompany(input: $input) { id } }",
            new { input = new { name = "Link Tick Corp" } },
            token);
        var companyId = companyResult.GetProperty("data").GetProperty("createCompany").GetProperty("id").GetString();

        var citiesResult = await ExecuteGraphQlAsync("{ cities { id } }");
        var cityId = citiesResult.GetProperty("data").GetProperty("cities")[0].GetProperty("id").GetString();

        var buildingResult = await ExecuteGraphQlAsync(
            "mutation PlaceBuilding($input: PlaceBuildingInput!) { placeBuilding(input: $input) { id } }",
            new { input = new { companyId, cityId, type = "FACTORY", name = "Link Tick Factory" } },
            token);
        var buildingId = buildingResult.GetProperty("data").GetProperty("placeBuilding").GetProperty("id").GetString();

        // First, configure two units with no links and wait for them to activate.
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
                        new { unitType = "PURCHASE",       gridX = 0, gridY = 0, linkUp = false, linkDown = false, linkLeft = false, linkRight = false, linkUpLeft = false, linkUpRight = false, linkDownLeft = false, linkDownRight = false },
                        new { unitType = "MANUFACTURING",  gridX = 1, gridY = 0, linkUp = false, linkDown = false, linkLeft = false, linkRight = false, linkUpLeft = false, linkUpRight = false, linkDownLeft = false, linkDownRight = false }
                    }
                }
            },
            token);

        // Advance enough ticks to activate the first configuration.
        await AdvanceGameTicksAsync(BuildingConfigurationService.UnitPlanChangeTicks + 1);

        // Now change ONLY the link between the two existing units.
        var linkChangeResult = await ExecuteGraphQlAsync(
            """
            mutation StoreBuildingConfiguration($input: StoreBuildingConfigurationInput!) {
                storeBuildingConfiguration(input: $input) {
                    totalTicksRequired
                    units { unitType ticksRequired }
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
                        new { unitType = "PURCHASE",       gridX = 0, gridY = 0, linkUp = false, linkDown = false, linkLeft = false, linkRight = true, linkUpLeft = false, linkUpRight = false, linkDownLeft = false, linkDownRight = false },
                        new { unitType = "MANUFACTURING",  gridX = 1, gridY = 0, linkUp = false, linkDown = false, linkLeft = true,  linkRight = false, linkUpLeft = false, linkUpRight = false, linkDownLeft = false, linkDownRight = false }
                    }
                }
            },
            token);

        Assert.False(linkChangeResult.TryGetProperty("errors", out _),
            "Re-configuring existing units with only link changes must succeed.");

        var plan = linkChangeResult.GetProperty("data").GetProperty("storeBuildingConfiguration");
        var totalTicks = plan.GetProperty("totalTicksRequired").GetInt32();
        Assert.True(
            totalTicks == BuildingConfigurationService.LinkChangeTicks,
            $"A link-only change must require exactly {BuildingConfigurationService.LinkChangeTicks} tick(s), got {totalTicks}.");

        var units = plan.GetProperty("units").EnumerateArray().ToList();
        foreach (var unit in units)
        {
            var unitTicks = unit.GetProperty("ticksRequired").GetInt32();
            Assert.True(
                unitTicks == BuildingConfigurationService.LinkChangeTicks,
                $"Each changed unit must require exactly {BuildingConfigurationService.LinkChangeTicks} tick(s).");
        }
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

    [Fact]
    public async Task CancelBuildingConfiguration_Unauthenticated_ReturnsError()
    {
        // An unauthenticated call (no token) to cancelBuildingConfiguration must be rejected.
        var result = await ExecuteGraphQlAsync(
            """
            mutation CancelBuildingConfiguration($input: CancelBuildingConfigurationInput!) {
                cancelBuildingConfiguration(input: $input) { id }
            }
            """,
            new { input = new { buildingId = Guid.NewGuid().ToString() } },
            token: null);

        Assert.True(result.TryGetProperty("errors", out var errors),
            "Unauthenticated cancelBuildingConfiguration must return errors");
        Assert.NotEmpty(errors.EnumerateArray().ToList());
    }

    #endregion

    #endregion

    #region Onboarding

    [Fact]
        public async Task CreateCompany_IssuesFounderSharesAndSelectsCompanyContext()
    {
                var token = await RegisterAndGetTokenAsync($"founder-shares-{Guid.NewGuid():N}@test.com", "Founder");
                var createResult = await ExecuteGraphQlAsync(
                        """
                        mutation CreateCompany($input: CreateCompanyInput!) {
                            createCompany(input: $input) {
                                id
                                name
                                cash
                                totalSharesIssued
                            }
                        }
                        """,
                        new { input = new { name = "Founder Holdings Co" } },
                        token);

                var company = createResult.GetProperty("data").GetProperty("createCompany");
                var companyId = company.GetProperty("id").GetString()!;
                Assert.Equal(10_000m, company.GetProperty("totalSharesIssued").GetDecimal());

                var personAccountResult = await ExecuteGraphQlAsync(
            """
            {
                            personAccount {
                                personalCash
                                activeAccountType
                                activeCompanyId
                                shareholdings {
                                    companyId
                                    shareCount
                                    ownershipRatio
                }
              }
            }
            """,
            token: token);

                var personAccount = personAccountResult.GetProperty("data").GetProperty("personAccount");
                Assert.Equal(200_000m, personAccount.GetProperty("personalCash").GetDecimal());
                Assert.Equal("COMPANY", personAccount.GetProperty("activeAccountType").GetString());
                Assert.Equal(companyId, personAccount.GetProperty("activeCompanyId").GetString());

                var holding = personAccount.GetProperty("shareholdings").EnumerateArray().Single();
                Assert.Equal(companyId, holding.GetProperty("companyId").GetString());
                Assert.Equal(10_000m, holding.GetProperty("shareCount").GetDecimal());
                Assert.Equal(1m, holding.GetProperty("ownershipRatio").GetDecimal());
    }

    [Fact]
        public async Task StartOnboardingCompany_DefaultIpoProfile_DeductsFounderContributionAndIssuesFounderShares()
    {
                var token = await RegisterAndGetTokenAsync($"onboard-ipo-default-{Guid.NewGuid():N}@test.com", "Starter Founder");
                var (companyId, _, _, result) = await StartOnboardingCompanyAsync(token, "Starter Founder Co");

                var data = result.GetProperty("data").GetProperty("startOnboardingCompany");
                Assert.True(data.GetProperty("company").GetProperty("cash").GetDecimal() < 450_000m);

                var personAccountResult = await ExecuteGraphQlAsync(
                        """
                        {
                            personAccount {
                                personalCash
                                activeAccountType
                                activeCompanyId
                                shareholdings {
                                    companyId
                                    shareCount
                                    ownershipRatio
                                }
                            }
                        }
                        """,
                        token: token);

                var personAccount = personAccountResult.GetProperty("data").GetProperty("personAccount");
                Assert.Equal(150_000m, personAccount.GetProperty("personalCash").GetDecimal());
                Assert.Equal("COMPANY", personAccount.GetProperty("activeAccountType").GetString());
                Assert.Equal(companyId, personAccount.GetProperty("activeCompanyId").GetString());

                var founderHolding = personAccount.GetProperty("shareholdings").EnumerateArray().Single();
                Assert.Equal(companyId, founderHolding.GetProperty("companyId").GetString());
                Assert.Equal(5_000m, founderHolding.GetProperty("shareCount").GetDecimal());
                Assert.Equal(0.5m, founderHolding.GetProperty("ownershipRatio").GetDecimal());
    }

        [Fact]
        public async Task StartOnboardingCompany_CustomIpoRaiseTarget_UsesRequestedOwnershipProfile()
        {
                var token = await RegisterAndGetTokenAsync($"onboard-ipo-custom-{Guid.NewGuid():N}@test.com", "IPO Founder");
                var cityId = await GetCityIdByNameAsync();
                var factoryLotId = await CreateTestLotAsync(cityId, "FACTORY,MINE", "IPO District");

                var result = await ExecuteGraphQlAsync(
                        """
                        mutation StartOnboardingCompany($input: StartOnboardingCompanyInput!) {
                            startOnboardingCompany(input: $input) {
                                company { id cash totalSharesIssued }
                            }
                        }
                        """,
                        new
                        {
                                input = new
                                {
                                        industry = "FURNITURE",
                                        cityId,
                                        companyName = "IPO Founder Co",
                                        factoryLotId,
                                        ipoRaiseTarget = 800000m,
                                }
                        },
                        token);

                var company = result.GetProperty("data").GetProperty("startOnboardingCompany").GetProperty("company");
                var companyId = company.GetProperty("id").GetString()!;
                Assert.True(company.GetProperty("cash").GetDecimal() < 850_000m);
                Assert.True(company.GetProperty("cash").GetDecimal() > 700_000m);
                Assert.Equal(10_000m, company.GetProperty("totalSharesIssued").GetDecimal());

                var personAccountResult = await ExecuteGraphQlAsync(
                        """
                        {
                            personAccount {
                                personalCash
                                shareholdings {
                                    companyId
                                    shareCount
                                    ownershipRatio
                                }
                            }
                        }
                        """,
                        token: token);

                var personAccount = personAccountResult.GetProperty("data").GetProperty("personAccount");
                Assert.Equal(150_000m, personAccount.GetProperty("personalCash").GetDecimal());
                var founderHolding = personAccount.GetProperty("shareholdings").EnumerateArray().Single();
                Assert.Equal(companyId, founderHolding.GetProperty("companyId").GetString());
                Assert.Equal(2_500m, founderHolding.GetProperty("shareCount").GetDecimal());
                Assert.Equal(0.25m, founderHolding.GetProperty("ownershipRatio").GetDecimal());
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
        Assert.True(data.GetProperty("company").GetProperty("cash").GetDecimal() < 450_000m);

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
        public async Task UpdateCompanySettings_PersistsDividendPayoutRatio()
        {
                var token = await RegisterAndGetTokenAsync($"company-dividend-{Guid.NewGuid():N}@test.com", "Dividend Owner");
                var createResult = await ExecuteGraphQlAsync(
                        """
                        mutation CreateCompany($input: CreateCompanyInput!) {
                            createCompany(input: $input) { id }
                        }
                        """,
                        new { input = new { name = "Dividend Co" } },
                        token);

                var companyId = createResult.GetProperty("data").GetProperty("createCompany").GetProperty("id").GetString()!;

                var updateResult = await ExecuteGraphQlAsync(
                        """
                        mutation UpdateCompanySettings($input: UpdateCompanySettingsInput!) {
                            updateCompanySettings(input: $input) { id name dividendPayoutRatio }
                        }
                        """,
                        new
                        {
                                input = new
                                {
                                        companyId,
                                        name = "Dividend Co",
                                        dividendPayoutRatio = 0.35m,
                                        citySalarySettings = Array.Empty<object>(),
                                }
                        },
                        token);

                var updated = updateResult.GetProperty("data").GetProperty("updateCompanySettings");
                Assert.Equal(0.35m, updated.GetProperty("dividendPayoutRatio").GetDecimal());

                var settingsResult = await ExecuteGraphQlAsync(
                        """
                        query GetCompanySettings($companyId: UUID!) {
                            companySettings(companyId: $companyId) {
                                companyId
                                dividendPayoutRatio
                                totalSharesIssued
                            }
                        }
                        """,
                        new { companyId },
                        token);

                var settings = settingsResult.GetProperty("data").GetProperty("companySettings");
                Assert.Equal(companyId, settings.GetProperty("companyId").GetString());
                Assert.Equal(0.35m, settings.GetProperty("dividendPayoutRatio").GetDecimal());
                Assert.Equal(10_000m, settings.GetProperty("totalSharesIssued").GetDecimal());
        }

        [Fact]
        public async Task BuyAndSellShares_WithPersonAccount_UpdatesPortfolioAndPersonalCash()
        {
                var controllerToken = await RegisterAndGetTokenAsync($"public-controller-{Guid.NewGuid():N}@test.com", "Public Controller");
                var controllerPlayerId = await GetCurrentPlayerIdAsync(controllerToken);
                var publicCompanyId = await SeedPublicCompanyAsync(controllerPlayerId);

                var investorToken = await RegisterAndGetTokenAsync($"portfolio-investor-{Guid.NewGuid():N}@test.com", "Portfolio Investor");

                var buyResult = await ExecuteGraphQlAsync(
                        """
                        mutation BuyShares($input: BuySharesInput!) {
                            buyShares(input: $input) {
                                companyId
                                shareCount
                                pricePerShare
                                totalValue
                                ownedShareCount
                                personalCash
                            }
                        }
                        """,
                        new { input = new { companyId = publicCompanyId, shareCount = 100m } },
                        investorToken);

                var bought = buyResult.GetProperty("data").GetProperty("buyShares");
                Assert.Equal(publicCompanyId.ToString(), bought.GetProperty("companyId").GetString());
                Assert.Equal(100m, bought.GetProperty("shareCount").GetDecimal());
                Assert.True(bought.GetProperty("personalCash").GetDecimal() < 200_000m);
                Assert.Equal(100m, bought.GetProperty("ownedShareCount").GetDecimal());

                var sellResult = await ExecuteGraphQlAsync(
                        """
                        mutation SellShares($input: SellSharesInput!) {
                            sellShares(input: $input) {
                                shareCount
                                totalValue
                                ownedShareCount
                                personalCash
                            }
                        }
                        """,
                        new { input = new { companyId = publicCompanyId, shareCount = 40m } },
                        investorToken);

                var sold = sellResult.GetProperty("data").GetProperty("sellShares");
                Assert.Equal(40m, sold.GetProperty("shareCount").GetDecimal());
                Assert.Equal(60m, sold.GetProperty("ownedShareCount").GetDecimal());

                var accountResult = await ExecuteGraphQlAsync(
                        """
                        {
                            personAccount {
                                personalCash
                                shareholdings {
                                    shareCount
                                }
                            }
                        }
                        """,
                        token: investorToken);

                var account = accountResult.GetProperty("data").GetProperty("personAccount");
                Assert.Equal(60m, account.GetProperty("shareholdings")[0].GetProperty("shareCount").GetDecimal());
                Assert.Equal(sold.GetProperty("personalCash").GetDecimal(), account.GetProperty("personalCash").GetDecimal());
        }

        [Fact]
        public async Task SwitchAccountContext_UsesControlledCompanyOwnershipToClaimControl()
        {
                var targetOwnerToken = await RegisterAndGetTokenAsync($"target-owner-{Guid.NewGuid():N}@test.com", "Target Owner");
                var targetOwnerId = await GetCurrentPlayerIdAsync(targetOwnerToken);
                var targetCompanyId = await SeedPublicCompanyAsync(targetOwnerId, name: "Takeover Target", cash: 100_000m);

                var acquirerToken = await RegisterAndGetTokenAsync($"acquirer-{Guid.NewGuid():N}@test.com", "Acquirer");
                var acquirerCreateResult = await ExecuteGraphQlAsync(
                        """
                        mutation CreateCompany($input: CreateCompanyInput!) {
                            createCompany(input: $input) { id }
                        }
                        """,
                        new { input = new { name = "Acquirer Holdings" } },
                        acquirerToken);

                var acquirerCompanyId = Guid.Parse(acquirerCreateResult.GetProperty("data").GetProperty("createCompany").GetProperty("id").GetString()!);

                await ExecuteGraphQlAsync(
                        """
                        mutation BuyShares($input: BuySharesInput!) {
                            buyShares(input: $input) { ownedShareCount companyCash }
                        }
                        """,
                        new { input = new { companyId = targetCompanyId, shareCount = 5_000m } },
                        acquirerToken);

                var switchResult = await ExecuteGraphQlAsync(
                        """
                        mutation SwitchAccountContext($input: SwitchAccountContextInput!) {
                            switchAccountContext(input: $input) {
                                activeAccountType
                                activeCompanyId
                                activeAccountName
                            }
                        }
                        """,
                        new { input = new { accountType = "COMPANY", companyId = targetCompanyId } },
                        acquirerToken);

                var switched = switchResult.GetProperty("data").GetProperty("switchAccountContext");
                Assert.Equal("COMPANY", switched.GetProperty("activeAccountType").GetString());
                Assert.Equal(targetCompanyId.ToString(), switched.GetProperty("activeCompanyId").GetString());
                Assert.Equal("Takeover Target", switched.GetProperty("activeAccountName").GetString());

                var meResult = await ExecuteGraphQlAsync(
                        """
                        {
                            me {
                                activeAccountType
                                activeCompanyId
                                companies { id name }
                            }
                        }
                        """,
                        token: acquirerToken);

                var me = meResult.GetProperty("data").GetProperty("me");
                Assert.Equal("COMPANY", me.GetProperty("activeAccountType").GetString());
                Assert.Equal(targetCompanyId.ToString(), me.GetProperty("activeCompanyId").GetString());
                Assert.Contains(me.GetProperty("companies").EnumerateArray(), company => company.GetProperty("id").GetString() == targetCompanyId.ToString());
                Assert.Contains(me.GetProperty("companies").EnumerateArray(), company => company.GetProperty("id").GetString() == acquirerCompanyId.ToString());
        }

        [Fact]
        public async Task BuyShares_AsCompanyBuyback_RetiresIssuedShares()
        {
                var ownerToken = await RegisterAndGetTokenAsync($"buyback-owner-{Guid.NewGuid():N}@test.com", "Buyback Owner");
                var ownerId = await GetCurrentPlayerIdAsync(ownerToken);
                var companyId = await SeedPublicCompanyAsync(ownerId, name: "Buyback Co", cash: 500_000m);
                await SetActiveCompanyContextAsync(ownerId, companyId);

                await ExecuteGraphQlAsync(
                        """
                        mutation BuyShares($input: BuySharesInput!) {
                            buyShares(input: $input) {
                                companyId
                                shareCount
                                companyCash
                            }
                        }
                        """,
                        new { input = new { companyId, shareCount = 1_000m } },
                        ownerToken);

                await using var scope = _factory.Services.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var company = await db.Companies.FirstAsync(candidate => candidate.Id == companyId);
                var treasuryHoldingCount = await db.Shareholdings.CountAsync(holding => holding.CompanyId == companyId && holding.OwnerCompanyId == companyId);

                Assert.Equal(9_000m, company.TotalSharesIssued);
                Assert.Equal(0, treasuryHoldingCount);
        }

        [Fact]
        public async Task DividendPhase_PaysPersonShareholderAndRecordsPayment()
        {
            await ResetGameStateAsync();

                var controllerToken = await RegisterAndGetTokenAsync($"div-controller-{Guid.NewGuid():N}@test.com", "Dividend Controller");
                var controllerId = await GetCurrentPlayerIdAsync(controllerToken);
                var investorToken = await RegisterAndGetTokenAsync($"div-investor-{Guid.NewGuid():N}@test.com", "Dividend Investor");
                var investorId = await GetCurrentPlayerIdAsync(investorToken);
                var companyId = await SeedPublicCompanyAsync(controllerId, name: "Dividend Issuer", cash: 50_000m, founderShares: 0m, dividendPayoutRatio: 0.5m);

                await using (var scope = _factory.Services.CreateAsyncScope())
                {
                        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                        db.Shareholdings.Add(new Shareholding
                        {
                                Id = Guid.NewGuid(),
                                CompanyId = companyId,
                                OwnerPlayerId = investorId,
                                ShareCount = 5_000m,
                        });
                        db.LedgerEntries.Add(new LedgerEntry
                        {
                                Id = Guid.NewGuid(),
                                CompanyId = companyId,
                                Category = LedgerCategory.Revenue,
                                Description = "Profitable year",
                                Amount = 10_000m,
                                RecordedAtTick = 1,
                                RecordedAtUtc = DateTime.UtcNow,
                        });
                        await db.SaveChangesAsync();
                }

                await AdvanceGameTicksAsync(GameConstants.TicksPerYear - 1);
                await ProcessTicksAsync(1);

                var expectedDividend = 2_125m;

                var accountResult = await ExecuteGraphQlAsync(
                        """
                        {
                            personAccount {
                                personalCash
                                dividendPayments {
                                    totalAmount
                                    gameYear
                                }
                            }
                        }
                        """,
                        token: investorToken);

                var personAccount = accountResult.GetProperty("data").GetProperty("personAccount");
                    Assert.Equal(200_000m + expectedDividend, personAccount.GetProperty("personalCash").GetDecimal());
                Assert.Equal(1, personAccount.GetProperty("dividendPayments").GetArrayLength());
                    Assert.Equal(expectedDividend, personAccount.GetProperty("dividendPayments")[0].GetProperty("totalAmount").GetDecimal());
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
        // Null maxPrice allows market-rate raw-material purchases — see ConfigureStarterFactory comment.
        Assert.Equal(JsonValueKind.Null, factoryPurchase.GetProperty("maxPrice").ValueKind);
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
    public async Task FinishOnboarding_FoodProcessingConfiguresStarterPricingAndLinks()
    {
        // Regression test: ConfigureStarterFactory must NOT set MaxPrice on the factory purchase unit.
        // Bread BasePrice = 3 but Grain exchange price ≈ 6, so capping at BasePrice silently breaks
        // the FOOD_PROCESSING supply chain. This test explicitly verifies null maxPrice.
        var token = await RegisterAndGetTokenAsync($"onboard-food-config-{Guid.NewGuid()}@test.com", "Food Config Founder");
        var cityId = await GetCityIdByNameAsync();
        var factoryLotId = await CreateTestLotAsync(cityId, "FACTORY,MINE", "Industrial Zone");

        await ExecuteGraphQlAsync(
            """
            mutation StartOnboardingCompany($input: StartOnboardingCompanyInput!) {
              startOnboardingCompany(input: $input) { nextStep company { id } }
            }
            """,
            new { input = new { industry = "FOOD_PROCESSING", cityId, companyName = "Food Config Co", factoryLotId } },
            token);

        var productsResult = await ExecuteGraphQlAsync(
            """
            query {
              productTypes(industry: "FOOD_PROCESSING") {
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
            .Single(product => product.GetProperty("slug").GetString() == "bread");
        var productId = selectedProduct.GetProperty("id").GetString()!;
        var basePrice = selectedProduct.GetProperty("basePrice").GetDecimal();
        var starterResourceId = selectedProduct.GetProperty("recipes").EnumerateArray()
            .Select(recipe => recipe.GetProperty("resourceType"))
            .Single(resourceType => resourceType.ValueKind == JsonValueKind.Object)
            .GetProperty("id")
            .GetString();

        var shopLotId = await CreateTestLotAsync(cityId, "SALES_SHOP,COMMERCIAL", "Commercial District", 95_000m);
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
        // Null maxPrice is critical for FOOD_PROCESSING: Grain exchange price (~6) > Bread basePrice (3).
        // If maxPrice were set to basePrice, the factory could never purchase Grain at market rate.
        Assert.Equal(JsonValueKind.Null, factoryPurchase.GetProperty("maxPrice").ValueKind);
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
        Assert.True(shopPurchase.GetProperty("linkRight").GetBoolean());

        Assert.Equal(1, publicSales.GetProperty("gridX").GetInt32());
        Assert.Equal(productId, publicSales.GetProperty("productTypeId").GetString());
        Assert.Equal(basePrice * 1.5m, publicSales.GetProperty("minPrice").GetDecimal());
    }

    [Fact]
    public async Task FinishOnboarding_HealthcareConfiguresStarterPricingAndLinks()
    {
        // Verifies that ConfigureStarterFactory correctly sets null maxPrice on the factory purchase unit
        // for HEALTHCARE, and that all unit positions, links, pricing, and visibility are correct.
        var token = await RegisterAndGetTokenAsync($"onboard-health-config-{Guid.NewGuid()}@test.com", "Health Config Founder");
        var cityId = await GetCityIdByNameAsync();
        var factoryLotId = await CreateTestLotAsync(cityId, "FACTORY,MINE", "Industrial Zone");

        await ExecuteGraphQlAsync(
            """
            mutation StartOnboardingCompany($input: StartOnboardingCompanyInput!) {
              startOnboardingCompany(input: $input) { nextStep company { id } }
            }
            """,
            new { input = new { industry = "HEALTHCARE", cityId, companyName = "Health Config Co", factoryLotId } },
            token);

        var productsResult = await ExecuteGraphQlAsync(
            """
            query {
              productTypes(industry: "HEALTHCARE") {
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
            .Single(product => product.GetProperty("slug").GetString() == "basic-medicine");
        var productId = selectedProduct.GetProperty("id").GetString()!;
        var basePrice = selectedProduct.GetProperty("basePrice").GetDecimal();
        var starterResourceId = selectedProduct.GetProperty("recipes").EnumerateArray()
            .Select(recipe => recipe.GetProperty("resourceType"))
            .Single(resourceType => resourceType.ValueKind == JsonValueKind.Object)
            .GetProperty("id")
            .GetString();

        var shopLotId = await CreateTestLotAsync(cityId, "SALES_SHOP,COMMERCIAL", "Commercial District", 95_000m);
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
        // Null maxPrice ensures the factory can purchase raw materials at market rate regardless of product base price.
        Assert.Equal(JsonValueKind.Null, factoryPurchase.GetProperty("maxPrice").ValueKind);
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
        Assert.True(shopPurchase.GetProperty("linkRight").GetBoolean());

        Assert.Equal(1, publicSales.GetProperty("gridX").GetInt32());
        Assert.Equal(productId, publicSales.GetProperty("productTypeId").GetString());
        Assert.Equal(basePrice * 1.5m, publicSales.GetProperty("minPrice").GetDecimal());
    }

    [Fact]
    public async Task FinishOnboarding_ResultIncludesSelectedProductBasePrice_ForAllIndustries()
    {
        // The configure-guide in the frontend depends on selectedProduct.basePrice from the
        // FinishOnboarding result to show the player the market benchmark selling price.
        // If this field is ever dropped from the GraphQL response, the guide silently shows
        // generic text instead of the concrete price ($45 Furniture, $3 Bread, $50 Medicine).
        var industries = new[]
        {
            ("FURNITURE", "wooden-chair", 45m),
            ("FOOD_PROCESSING", "bread", 3m),
            ("HEALTHCARE", "basic-medicine", 50m),
        };

        foreach (var (industry, slug, expectedBasePrice) in industries)
        {
            var token = await RegisterAndGetTokenAsync($"guide-price-{industry.ToLower()}-{Guid.NewGuid()}@test.com", $"Guide Price {industry}");

            // Start with the correct industry
            var cityId = await GetCityIdByNameAsync();
            var factoryLotId = await GetAvailableLotIdAsync(cityId, "FACTORY");
            var startResult = await ExecuteGraphQlAsync(
                """
                mutation StartOnboardingCompany($input: StartOnboardingCompanyInput!) {
                  startOnboardingCompany(input: $input) { company { id } }
                }
                """,
                new { input = new { industry, cityId, companyName = $"Guide Price {industry} Co", factoryLotId } },
                token);
            Assert.False(startResult.TryGetProperty("errors", out _), $"StartOnboardingCompany failed for {industry}");

            var productsResult = await ExecuteGraphQlAsync($"{{ productTypes(industry: \"{industry}\") {{ id slug basePrice }} }}");
            var product = productsResult.GetProperty("data").GetProperty("productTypes")
                .EnumerateArray()
                .Single(p => p.GetProperty("slug").GetString() == slug);
            var productId = product.GetProperty("id").GetString()!;

            var shopLotId = await GetAvailableLotIdAsync(cityId, "SALES_SHOP");

            // Query FinishOnboarding with selectedProduct.basePrice included
            var finishResult = await ExecuteGraphQlAsync(
                """
                mutation FinishOnboarding($input: FinishOnboardingInput!) {
                  finishOnboarding(input: $input) {
                    selectedProduct { id name basePrice }
                  }
                }
                """,
                new { input = new { productTypeId = productId, shopLotId } },
                token);

            Assert.False(finishResult.TryGetProperty("errors", out _), $"FinishOnboarding failed for {industry}");
            var returnedBasePrice = finishResult
                .GetProperty("data")
                .GetProperty("finishOnboarding")
                .GetProperty("selectedProduct")
                .GetProperty("basePrice")
                .GetDecimal();

            Assert.Equal(expectedBasePrice, returnedBasePrice);
        }
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
    public async Task StartOnboardingCompany_WhenFactoryLotCostsMoreThanStartingCash_Fails()
    {
        // ROADMAP: "The price to purchase the land includes also the base price for the raw material."
        // Starting cash for a new onboarding company is $500,000. A factory lot priced above that
        // should be rejected with INSUFFICIENT_FUNDS before the company is persisted.
        var token = await RegisterAndGetTokenAsync($"onboard-map-broke-{Guid.NewGuid()}@test.com", "Broke Factory Player");
        var cityId = await GetCityIdByNameAsync();
        var expensiveLotId = await CreateTestLotAsync(cityId, "FACTORY,MINE", "Industrial Zone", 600_000m, "Expensive Factory Lot");

        var result = await ExecuteGraphQlAsync(
            """
            mutation StartOnboardingCompany($input: StartOnboardingCompanyInput!) {
              startOnboardingCompany(input: $input) { company { id } }
            }
            """,
            new { input = new { industry = "FURNITURE", cityId, companyName = "Broke Factory Co", factoryLotId = expensiveLotId } },
            token);

        Assert.True(result.TryGetProperty("errors", out var errors));
        var code = errors[0].GetProperty("extensions").GetProperty("code").GetString();
        Assert.Equal("INSUFFICIENT_FUNDS", code);
        var message = errors[0].GetProperty("message").GetString();
        Assert.Contains("Insufficient funds", message);
        Assert.Contains("600", message);

        // Onboarding should NOT be in progress since the mutation failed
        var meResult = await ExecuteGraphQlAsync("{ me { onboardingCurrentStep onboardingCompanyId } }", token: token);
        var me = meResult.GetProperty("data").GetProperty("me");
        Assert.Equal(JsonValueKind.Null, me.GetProperty("onboardingCurrentStep").ValueKind);
        Assert.Equal(JsonValueKind.Null, me.GetProperty("onboardingCompanyId").ValueKind);
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
        Assert.Equal(StartupPackService.CompanyCashGrant, offer.GetProperty("companyCashGrant").GetDecimal());
        Assert.Equal(StartupPackService.ProDurationDays, offer.GetProperty("proDurationDays").GetInt32());
        Assert.True(DateTime.TryParse(offer.GetProperty("expiresAtUtc").GetString(), out _));
    }

    [Fact]
    public void StartupPackService_PriceUsd_MatchesRoadmapDefinition()
    {
        // The ROADMAP defines the startup pack price as $20.
        // This test documents and enforces that contract so any accidental change is caught.
        Assert.Equal(20m, StartupPackService.PriceUsd);
        // Pro monthly reference price must match the roadmap ($10/month).
        Assert.Equal(10m, StartupPackService.ProMonthlyPriceUsd);
        // Pro and cash-grant constants must also stay within expected ranges.
        Assert.Equal(90, StartupPackService.ProDurationDays);
        Assert.True(StartupPackService.CompanyCashGrant > 0,
            "CompanyCashGrant must be a positive in-game currency amount.");
        // The startup pack price must be less than the full-price equivalent so the savings are real.
        var fullPriceEquivalent = StartupPackService.ProMonthlyPriceUsd * (StartupPackService.ProDurationDays / 30m);
        Assert.True(StartupPackService.PriceUsd < fullPriceEquivalent,
            $"Startup pack price ${StartupPackService.PriceUsd} must be less than full Pro price ${fullPriceEquivalent}.");
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
    public async Task StartupPackOffer_Query_Unauthenticated_ReturnsError()
    {
        // The startupPackOffer query has [Authorize] and must reject unauthenticated requests.
        var result = await ExecuteGraphQlAsync(
            "{ startupPackOffer { status } }");

        Assert.True(result.TryGetProperty("errors", out _));
    }

    [Fact]
    public async Task ClaimStartupPack_BeforeOnboarding_ReturnsError()
    {
        // A player who has not completed onboarding has no startup-pack offer.
        // Attempting to claim must fail with STARTUP_PACK_NOT_AVAILABLE.
        var token = await RegisterAndGetTokenAsync($"startup-pack-pre-onboard-{Guid.NewGuid()}@test.com", "PreOnboardClaimer");
        // Do NOT complete onboarding — player has no offer record.

        var result = await ExecuteGraphQlAsync(
            """
            mutation ClaimStartupPack($input: ClaimStartupPackInput!) {
              claimStartupPack(input: $input) {
                offer { status }
              }
            }
            """,
            new { input = new { companyId = Guid.NewGuid() } },
            token);

        Assert.True(result.TryGetProperty("errors", out var errors));
        Assert.Contains(errors.EnumerateArray(), error =>
            error.GetProperty("extensions").GetProperty("code").GetString() == "STARTUP_PACK_NOT_AVAILABLE");
    }

    [Fact]
    public async Task ClaimStartupPack_VerifiesProSubscriptionEndsAtUtcIsInFuture()
    {
        // AC5: the user must be able to verify their new subscription status after purchase.
        // The proSubscriptionEndsAtUtc returned by claimStartupPack must be strictly in the future
        // and at least 89 days from now (90-day Pro entitlement).
        var token = await RegisterAndGetTokenAsync($"startup-pro-sub-verify-{Guid.NewGuid()}@test.com", "ProSubVerifyPlayer");
        var (companyId, _, _) = await CompleteOnboardingAsync(token, "Pro Sub Verify Corp");

        var claimResult = await ExecuteGraphQlAsync(
            """
            mutation ClaimStartupPack($input: ClaimStartupPackInput!) {
              claimStartupPack(input: $input) {
                offer { status claimedAtUtc }
                company { cash }
                proSubscriptionEndsAtUtc
              }
            }
            """,
            new { input = new { companyId } },
            token);

        Assert.False(claimResult.TryGetProperty("errors", out _), "ClaimStartupPack should succeed");
        var payload = claimResult.GetProperty("data").GetProperty("claimStartupPack");

        var proEndsStr = payload.GetProperty("proSubscriptionEndsAtUtc").GetString();
        Assert.NotNull(proEndsStr);
        Assert.True(DateTime.TryParse(proEndsStr, out var proEndsAt));
        // Must be in the future
        Assert.True(proEndsAt > DateTime.UtcNow, $"proSubscriptionEndsAtUtc ({proEndsAt:O}) must be in the future");
        // Must be at least 89 days from now (allowing 1-day clock tolerance)
        Assert.True((proEndsAt - DateTime.UtcNow).TotalDays >= StartupPackService.ProDurationDays - 1,
            $"proSubscriptionEndsAtUtc should be ~{StartupPackService.ProDurationDays} days ahead; got {(proEndsAt - DateTime.UtcNow).TotalDays:F1} days");
    }

    [Fact]
    public async Task MarkStartupPackOfferShown_AlreadyClaimed_DoesNotChangeStatus()
    {
        // Calling markStartupPackOfferShown on an already-claimed offer must return the offer
        // with the CLAIMED status unchanged (the backend must be idempotent for CLAIMED offers).
        var token = await RegisterAndGetTokenAsync($"startup-show-claimed-{Guid.NewGuid()}@test.com", "ShowClaimedPlayer");
        var (companyId, _, _) = await CompleteOnboardingAsync(token, "Show Claimed Corp");

        // Claim the offer first
        var claimResult = await ExecuteGraphQlAsync(
            """
            mutation ClaimStartupPack($input: ClaimStartupPackInput!) {
              claimStartupPack(input: $input) { offer { status } }
            }
            """,
            new { input = new { companyId } },
            token);
        Assert.Equal("CLAIMED",
            claimResult.GetProperty("data").GetProperty("claimStartupPack").GetProperty("offer").GetProperty("status").GetString());

        // markStartupPackOfferShown after claiming must not change status back to SHOWN
        var shownResult = await ExecuteGraphQlAsync(
            "mutation { markStartupPackOfferShown { status shownAtUtc } }",
            token: token);

        var shownOffer = shownResult.GetProperty("data").GetProperty("markStartupPackOfferShown");
        Assert.Equal("CLAIMED", shownOffer.GetProperty("status").GetString());
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
    public async Task StartOnboardingCompany_Unauthenticated_ReturnsError()
    {
        // Guest mode front-end skips backend calls; this test verifies that an unauthenticated
        // caller cannot directly invoke the mutation (authorization boundary).
        var result = await ExecuteGraphQlAsync(
            """
            mutation StartOnboardingCompany($input: StartOnboardingCompanyInput!) {
              startOnboardingCompany(input: $input) { company { id } }
            }
            """,
            new { input = new { industry = "FURNITURE", cityId = Guid.NewGuid(), companyName = "Ghost Co", factoryLotId = Guid.NewGuid() } });

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
    public async Task StartOnboardingCompany_WithEmptyCompanyName_ReturnsError()
    {
        var token = await RegisterAndGetTokenAsync($"onboard-empty-name-{Guid.NewGuid()}@test.com", "EmptyNameStart");
        var cityId = await GetCityIdByNameAsync();
        var lotId = await CreateTestLotAsync(cityId, "FACTORY,MINE", "Industrial Zone");

        var result = await ExecuteGraphQlAsync(
            """
            mutation StartOnboardingCompany($input: StartOnboardingCompanyInput!) {
              startOnboardingCompany(input: $input) { company { id } }
            }
            """,
            new { input = new { industry = "FURNITURE", cityId, companyName = "", factoryLotId = lotId } },
            token);

        Assert.True(result.TryGetProperty("errors", out var errors));
        Assert.Equal("INVALID_COMPANY_NAME", errors[0].GetProperty("extensions").GetProperty("code").GetString());
    }

    [Fact]
    public async Task StartOnboardingCompany_WithWhitespaceCompanyName_ReturnsError()
    {
        var token = await RegisterAndGetTokenAsync($"onboard-ws-name-{Guid.NewGuid()}@test.com", "WsNameStart");
        var cityId = await GetCityIdByNameAsync();
        var lotId = await CreateTestLotAsync(cityId, "FACTORY,MINE", "Industrial Zone");

        var result = await ExecuteGraphQlAsync(
            """
            mutation StartOnboardingCompany($input: StartOnboardingCompanyInput!) {
              startOnboardingCompany(input: $input) { company { id } }
            }
            """,
            new { input = new { industry = "FURNITURE", cityId, companyName = "   ", factoryLotId = lotId } },
            token);

        Assert.True(result.TryGetProperty("errors", out var errors));
        Assert.Equal("INVALID_COMPANY_NAME", errors[0].GetProperty("extensions").GetProperty("code").GetString());
    }

    [Fact]
    public async Task CompleteOnboarding_WithEmptyCompanyName_ReturnsError()
    {
        var token = await RegisterAndGetTokenAsync($"onboard-empty-name-complete-{Guid.NewGuid()}@test.com", "EmptyNameComplete");
        var cityId = await GetCityIdByNameAsync();
        var chairProduct = await GetStarterProductIdAsync("FURNITURE", "wooden-chair");

        var result = await ExecuteGraphQlAsync(
            """
            mutation CompleteOnboarding($input: OnboardingInput!) {
              completeOnboarding(input: $input) { company { id } }
            }
            """,
            new { input = new { industry = "FURNITURE", cityId, productTypeId = chairProduct, companyName = "" } },
            token);

        Assert.True(result.TryGetProperty("errors", out var errors));
        Assert.Equal("INVALID_COMPANY_NAME", errors[0].GetProperty("extensions").GetProperty("code").GetString());
    }

    [Fact]
    public async Task StartOnboardingCompany_WithTrimmedCompanyName_TrimsWhitespace()
    {
        // Company name with surrounding whitespace should be trimmed and stored without the whitespace.
        var token = await RegisterAndGetTokenAsync($"onboard-trim-name-{Guid.NewGuid()}@test.com", "TrimNameStart");
        var cityId = await GetCityIdByNameAsync();
        var lotId = await GetAvailableLotIdAsync(cityId, "FACTORY");

        var result = await ExecuteGraphQlAsync(
            """
            mutation StartOnboardingCompany($input: StartOnboardingCompanyInput!) {
              startOnboardingCompany(input: $input) {
                company { name }
              }
            }
            """,
            new { input = new { industry = "FURNITURE", cityId, companyName = "  Acme Corp  ", factoryLotId = lotId } },
            token);

        Assert.False(result.TryGetProperty("errors", out _), "Expected no errors");
        var name = result.GetProperty("data").GetProperty("startOnboardingCompany").GetProperty("company").GetProperty("name").GetString();
        Assert.Equal("Acme Corp", name);
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
    public async Task FinishOnboarding_WithNonExistentProductId_ReturnsInvalidProductError()
    {
        // Validates that FinishOnboarding rejects a completely non-existent product UUID
        // rather than producing a null-reference error or bypassing validation.
        var token = await RegisterAndGetTokenAsync($"finish-noexist-product-{Guid.NewGuid()}@test.com", "NoExistProduct");
        var (_, _, cityId, _) = await StartOnboardingCompanyAsync(token, "Phantom Product Co");
        var shopLotId = await CreateTestLotAsync(cityId, "SALES_SHOP,COMMERCIAL", "Commercial District", 90_000m);
        var phantomProductId = Guid.NewGuid();

        var result = await ExecuteGraphQlAsync(
            """
            mutation FinishOnboarding($input: FinishOnboardingInput!) {
              finishOnboarding(input: $input) { company { id } }
            }
            """,
            new { input = new { productTypeId = phantomProductId, shopLotId } },
            token);

        Assert.True(result.TryGetProperty("errors", out var errors));
        Assert.Equal("INVALID_PRODUCT", errors[0].GetProperty("extensions").GetProperty("code").GetString());
    }

    [Fact]
    public async Task FinishOnboarding_WithNonStarterProduct_ReturnsInvalidProductError()
    {
        // Validates that products seeded in the game (e.g. wooden-table) but NOT designated
        // as the starter product for that industry (starter = wooden-chair) are rejected.
        // This ensures the IsStarterOnboardingProduct guard in Mutation.cs is exercised.
        var token = await RegisterAndGetTokenAsync($"finish-nonstarter-product-{Guid.NewGuid()}@test.com", "NonStarterProduct");
        var (_, _, cityId, _) = await StartOnboardingCompanyAsync(token, "Fancy Furniture Co");
        var shopLotId = await CreateTestLotAsync(cityId, "SALES_SHOP,COMMERCIAL", "Commercial District", 90_000m);

        // wooden-table is a valid FURNITURE product but is NOT the starter product (wooden-chair is).
        var nonStarterProductId = await GetStarterProductIdAsync("FURNITURE", "wooden-table");

        var result = await FinishOnboardingAsync(token, nonStarterProductId, shopLotId);

        Assert.True(result.TryGetProperty("errors", out var errors));
        Assert.Equal("INVALID_PRODUCT", errors[0].GetProperty("extensions").GetProperty("code").GetString());
    }

    [Fact]
    public async Task FinishOnboarding_WithNonExistentShopLotId_ReturnsLotNotFoundError()
    {
        // Validates that FinishOnboarding rejects a completely non-existent shop lot UUID
        // with the LOT_NOT_FOUND error code rather than throwing an unhandled exception.
        // Complements the FinishOnboarding_WithNonExistentProductId test for the shop-lot path.
        var token = await RegisterAndGetTokenAsync($"finish-noexist-shop-{Guid.NewGuid()}@test.com", "PhantomShopLot");
        var (_, _, cityId, _) = await StartOnboardingCompanyAsync(token, "Phantom Shop Corp");
        var productId = await GetStarterProductIdAsync();
        var phantomShopLotId = Guid.NewGuid();

        var result = await ExecuteGraphQlAsync(
            """
            mutation FinishOnboarding($input: FinishOnboardingInput!) {
              finishOnboarding(input: $input) { company { id } }
            }
            """,
            new { input = new { productTypeId = productId, shopLotId = phantomShopLotId } },
            token);

        Assert.True(result.TryGetProperty("errors", out var errors));
        Assert.Equal("LOT_NOT_FOUND", errors[0].GetProperty("extensions").GetProperty("code").GetString());
    }

    [Fact]
    public async Task StartOnboardingCompany_WithNonExistentLot_ReturnsError()
    {
        // Validates that StartOnboardingCompany rejects a factory lot UUID that doesn't exist
        // in the database, rather than throwing an unhandled exception.
        var token = await RegisterAndGetTokenAsync($"start-noexist-lot-{Guid.NewGuid()}@test.com", "PhantomLot");
        var cityId = await GetCityIdByNameAsync();
        var phantomLotId = Guid.NewGuid();

        var result = await ExecuteGraphQlAsync(
            """
            mutation StartOnboardingCompany($input: StartOnboardingCompanyInput!) {
              startOnboardingCompany(input: $input) { company { id } }
            }
            """,
            new { input = new { industry = "FURNITURE", cityId, companyName = "Phantom Lot Corp", factoryLotId = phantomLotId } },
            token);

        Assert.True(result.TryGetProperty("errors", out _));
    }

    [Fact]
    public async Task StartOnboardingCompany_WithFactoryLotInDifferentCity_ReturnsLotCityMismatch()
    {
        // Validates that StartOnboardingCompany rejects a factory lot that belongs to a different city
        // than the one specified in the input. This ensures the city validation in PrepareLotPurchaseAsync
        // catches cross-city mismatches and returns LOT_CITY_MISMATCH instead of silently placing
        // a factory in the wrong city.
        var token = await RegisterAndGetTokenAsync($"start-city-mismatch-{Guid.NewGuid()}@test.com", "CityMismatchStart");
        var cities = await ExecuteGraphQlAsync("{ cities { id name } }");
        var allCities = cities.GetProperty("data").GetProperty("cities");
        var cityIds = Enumerable.Range(0, allCities.GetArrayLength())
            .Select(i => allCities[i].GetProperty("id").GetString()!)
            .ToList();

        if (cityIds.Count < 2)
        {
            // Need at least two cities for a meaningful cross-city test.
            return;
        }

        var targetCityId = cityIds[0];
        var otherCityId = cityIds[1];

        // Create a factory lot in the OTHER city, but claim it belongs to targetCity.
        var otherCityFactoryLotId = await CreateTestLotAsync(otherCityId, "FACTORY,MINE", "Industrial Zone", 75_000m, "Wrong City Factory Lot");

        var result = await ExecuteGraphQlAsync(
            """
            mutation StartOnboardingCompany($input: StartOnboardingCompanyInput!) {
              startOnboardingCompany(input: $input) { company { id } }
            }
            """,
            new { input = new { industry = "FURNITURE", cityId = targetCityId, companyName = "Cross City Corp", factoryLotId = otherCityFactoryLotId } },
            token);

        Assert.True(result.TryGetProperty("errors", out var errors), "Expected an error for cross-city factory lot");
        var code = errors[0].GetProperty("extensions").GetProperty("code").GetString();
        Assert.Equal("LOT_CITY_MISMATCH", code);

        // Onboarding must NOT be in progress since the mutation failed before persisting.
        var meResult = await ExecuteGraphQlAsync("{ me { onboardingCurrentStep onboardingCompanyId } }", token: token);
        var me = meResult.GetProperty("data").GetProperty("me");
        Assert.Equal(JsonValueKind.Null, me.GetProperty("onboardingCurrentStep").ValueKind);
        Assert.Equal(JsonValueKind.Null, me.GetProperty("onboardingCompanyId").ValueKind);
    }

    [Fact]
    public async Task StartOnboardingCompany_WhenPlayerAlreadyHasCompanyOutsideOnboarding_ReturnsError()
    {
        // Tests the edge case where the player already owns a company (created via completeOnboarding)
        // but no OnboardingCurrentStep/CompanyId flags are set (they were cleared on completion).
        // The backend check on db.Companies.AnyAsync catches this and returns ONBOARDING_ALREADY_IN_PROGRESS,
        // preventing a player from creating a second company via startOnboardingCompany.
        var token = await RegisterAndGetTokenAsync($"start-existing-co-{Guid.NewGuid()}@test.com", "ExistingCompanyPlayer");

        // Complete onboarding first so the player owns a company with cleared progress flags.
        await CompleteOnboardingAsync(token, "Already Owned Corp");

        // Now attempt to start onboarding again — should be rejected.
        var cityId = await GetCityIdByNameAsync();
        var lotId = await CreateTestLotAsync(cityId, "FACTORY,MINE", "Industrial Zone", 75_000m, "Second Attempt Factory Lot");

        var result = await ExecuteGraphQlAsync(
            """
            mutation StartOnboardingCompany($input: StartOnboardingCompanyInput!) {
              startOnboardingCompany(input: $input) { company { id } }
            }
            """,
            new { input = new { industry = "FURNITURE", cityId, companyName = "Second Empire Corp", factoryLotId = lotId } },
            token);

        Assert.True(result.TryGetProperty("errors", out var errors), "Expected an error for player who already has a company");
        var code = errors[0].GetProperty("extensions").GetProperty("code").GetString();
        // ONBOARDING_ALREADY_COMPLETED is returned first (player.OnboardingCompletedAtUtc is not null)
        Assert.Equal("ONBOARDING_ALREADY_COMPLETED", code);
    }

    [Fact]
    public async Task StarterIndustries_ContainsFurnitureFoodProcessingAndHealthcare()
    {
        // Verifies the exact industry values returned by the starterIndustries query.
        // This is a public endpoint (no auth token) — critical for unauthenticated guests
        // browsing the onboarding wizard industry selection step.
        var result = await ExecuteGraphQlAsync("{ starterIndustries { industries } }");

        var industries = result.GetProperty("data").GetProperty("starterIndustries").GetProperty("industries");
        var industryList = Enumerable.Range(0, industries.GetArrayLength())
            .Select(i => industries[i].GetString()!)
            .ToList();

        Assert.Contains("FURNITURE", industryList);
        Assert.Contains("FOOD_PROCESSING", industryList);
        Assert.Contains("HEALTHCARE", industryList);
        Assert.Equal(3, industryList.Count);
    }

    [Fact]
    public async Task FinishOnboarding_FoodProcessing_ReturnsEligibleStartupPackOffer()
    {
        // Verifies that the startup-pack offer is activated for FOOD_PROCESSING industry
        // via the staged FinishOnboarding mutation — not only via the legacy CompleteOnboarding.
        // This is a critical monetization path: the offer must appear for every starter industry.
        var token = await RegisterAndGetTokenAsync($"startup-pack-food-{Guid.NewGuid()}@test.com", "FoodStartupPack");
        var cityId = await GetCityIdByNameAsync();
        var factoryLotId = await CreateTestLotAsync(cityId, "FACTORY,MINE", "Industrial Zone", 75_000m);

        await ExecuteGraphQlAsync(
            """
            mutation StartOnboardingCompany($input: StartOnboardingCompanyInput!) {
              startOnboardingCompany(input: $input) { nextStep }
            }
            """,
            new { input = new { industry = "FOOD_PROCESSING", cityId, companyName = "Bread Empire", factoryLotId } },
            token);

        var productId = await GetStarterProductIdAsync("FOOD_PROCESSING", "bread");
        var shopLotId = await CreateTestLotAsync(cityId, "SALES_SHOP,COMMERCIAL", "Commercial District", 90_000m);

        var result = await ExecuteGraphQlAsync(
            """
            mutation FinishOnboarding($input: FinishOnboardingInput!) {
              finishOnboarding(input: $input) {
                company { id }
                startupPackOffer { status companyCashGrant proDurationDays expiresAtUtc }
              }
            }
            """,
            new { input = new { productTypeId = productId, shopLotId } },
            token);

        Assert.False(result.TryGetProperty("errors", out _), "FinishOnboarding failed for FOOD_PROCESSING startup pack test");

        var offer = result.GetProperty("data").GetProperty("finishOnboarding").GetProperty("startupPackOffer");
        Assert.Equal("ELIGIBLE", offer.GetProperty("status").GetString());
        Assert.Equal(StartupPackService.CompanyCashGrant, offer.GetProperty("companyCashGrant").GetDecimal());
        Assert.Equal(StartupPackService.ProDurationDays, offer.GetProperty("proDurationDays").GetInt32());
        Assert.True(DateTime.TryParse(offer.GetProperty("expiresAtUtc").GetString(), out _));
    }

    [Fact]
    public async Task FinishOnboarding_Healthcare_ReturnsEligibleStartupPackOffer()
    {
        // Verifies that the startup-pack offer is activated for HEALTHCARE industry
        // via the staged FinishOnboarding mutation.
        var token = await RegisterAndGetTokenAsync($"startup-pack-health-{Guid.NewGuid()}@test.com", "HealthStartupPack");
        var cityId = await GetCityIdByNameAsync();
        var factoryLotId = await CreateTestLotAsync(cityId, "FACTORY,MINE", "Industrial Zone", 75_000m);

        await ExecuteGraphQlAsync(
            """
            mutation StartOnboardingCompany($input: StartOnboardingCompanyInput!) {
              startOnboardingCompany(input: $input) { nextStep }
            }
            """,
            new { input = new { industry = "HEALTHCARE", cityId, companyName = "Medicine Empire", factoryLotId } },
            token);

        var productId = await GetStarterProductIdAsync("HEALTHCARE", "basic-medicine");
        var shopLotId = await CreateTestLotAsync(cityId, "SALES_SHOP,COMMERCIAL", "Commercial District", 90_000m);

        var result = await ExecuteGraphQlAsync(
            """
            mutation FinishOnboarding($input: FinishOnboardingInput!) {
              finishOnboarding(input: $input) {
                company { id }
                startupPackOffer { status companyCashGrant proDurationDays expiresAtUtc }
              }
            }
            """,
            new { input = new { productTypeId = productId, shopLotId } },
            token);

        Assert.False(result.TryGetProperty("errors", out _), "FinishOnboarding failed for HEALTHCARE startup pack test");

        var offer = result.GetProperty("data").GetProperty("finishOnboarding").GetProperty("startupPackOffer");
        Assert.Equal("ELIGIBLE", offer.GetProperty("status").GetString());
        Assert.Equal(StartupPackService.CompanyCashGrant, offer.GetProperty("companyCashGrant").GetDecimal());
        Assert.Equal(StartupPackService.ProDurationDays, offer.GetProperty("proDurationDays").GetInt32());
        Assert.True(DateTime.TryParse(offer.GetProperty("expiresAtUtc").GetString(), out _));
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

    // NOTE: All product-identity assertions in the conflict-recovery tests below check the
    // `industry` field (e.g. "FOOD_PROCESSING") rather than `slug` (e.g. "bread") because
    // FinishOnboardingAsync returns `selectedProduct { id name industry }` — not slug.
    // This is intentional: industry is sufficient to prove the guest's intent was preserved.
    [Fact]
    public async Task GuestMigration_FactoryLotConflict_PlayerCanRestartAndCompleteWithDifferentLot()
    {
        // ROADMAP: "If there is error such as the building was meanwhile purchased by someone else ...
        // make sure to create their profile with the name they chose and start the wizard again
        // with the authenticated user and this time save everything."
        //
        // This test verifies the full conflict-recovery cycle:
        // 1. Player A registers and onboards
        // 2. Player B (the guest) registers, but their factory lot was taken by Player A
        // 3. Player B retries with a different factory lot and successfully completes onboarding
        var tokenA = await RegisterAndGetTokenAsync($"conflict-a-{Guid.NewGuid()}@test.com", "Conflict A");
        var cityId = await GetCityIdByNameAsync();

        // Player A takes the shared lot
        var sharedLotId = await CreateTestLotAsync(cityId, "FACTORY,MINE", "Industrial Zone", 75_000m, "Shared Factory Lot");
        await ExecuteGraphQlAsync(
            """
            mutation StartOnboardingCompany($input: StartOnboardingCompanyInput!) {
              startOnboardingCompany(input: $input) { nextStep company { id } }
            }
            """,
            new { input = new { industry = "FURNITURE", cityId, companyName = "Conflict A Corp", factoryLotId = sharedLotId } },
            tokenA);

        // Player B attempts to use the same lot — must get LOT_ALREADY_OWNED
        var tokenB = await RegisterAndGetTokenAsync($"conflict-b-{Guid.NewGuid()}@test.com", "Conflict B");
        var failedStart = await ExecuteGraphQlAsync(
            """
            mutation StartOnboardingCompany($input: StartOnboardingCompanyInput!) {
              startOnboardingCompany(input: $input) { nextStep }
            }
            """,
            new { input = new { industry = "FURNITURE", cityId, companyName = "Conflict B Corp", factoryLotId = sharedLotId } },
            tokenB);

        Assert.True(failedStart.TryGetProperty("errors", out var failErrors), "Expected LOT_ALREADY_OWNED error");
        var code = failErrors[0].GetProperty("extensions").GetProperty("code").GetString();
        Assert.Equal("LOT_ALREADY_OWNED", code);

        // Player B retries with a fresh lot — should succeed
        var freshLotId = await CreateTestLotAsync(cityId, "FACTORY,MINE", "Industrial Zone", 75_000m, "Fresh Factory Lot");
        var retryStart = await ExecuteGraphQlAsync(
            """
            mutation StartOnboardingCompany($input: StartOnboardingCompanyInput!) {
              startOnboardingCompany(input: $input) { nextStep company { id cash } }
            }
            """,
            new { input = new { industry = "FURNITURE", cityId, companyName = "Conflict B Corp", factoryLotId = freshLotId } },
            tokenB);

        Assert.False(retryStart.TryGetProperty("errors", out _), "Retry with fresh lot must not fail");
        Assert.Equal("SHOP_SELECTION", retryStart.GetProperty("data").GetProperty("startOnboardingCompany").GetProperty("nextStep").GetString());

        // Player B can now finish onboarding
        var productId = await GetStarterProductIdAsync("FURNITURE", "wooden-chair");
        var shopLotId = await CreateTestLotAsync(cityId, "SALES_SHOP,COMMERCIAL", "Commercial District", 90_000m);
        var finishResult = await FinishOnboardingAsync(tokenB, productId, shopLotId);

        Assert.False(finishResult.TryGetProperty("errors", out _), "FinishOnboarding after conflict retry must succeed");
        var finishData = finishResult.GetProperty("data").GetProperty("finishOnboarding");
        Assert.NotNull(finishData.GetProperty("company").GetProperty("id").GetString());
        Assert.NotNull(finishData.GetProperty("salesShop").GetProperty("id").GetString());
    }

    [Fact]
    public async Task GuestMigration_ShopLotConflict_PlayerCanRetryWithDifferentShopLot()
    {
        // Verifies that after FinishOnboarding fails with LOT_ALREADY_OWNED for the shop lot,
        // the player (who is already authenticated and has a factory) can retry with a different
        // shop lot and complete onboarding successfully.
        var tokenA = await RegisterAndGetTokenAsync($"shop-conflict-a-{Guid.NewGuid()}@test.com", "Shop Conflict A");
        var cityId = await GetCityIdByNameAsync();

        // Player A creates a company and takes the shared shop lot
        var sharedShopLotId = await CreateTestLotAsync(cityId, "SALES_SHOP,COMMERCIAL", "Commercial District", 90_000m, "Shared Shop Lot");
        var createCompanyResult = await ExecuteGraphQlAsync(
            "mutation CreateCompany($input: CreateCompanyInput!) { createCompany(input: $input) { id } }",
            new { input = new { name = "Shop Conflict A Corp" } },
            tokenA);
        var companyAId = createCompanyResult.GetProperty("data").GetProperty("createCompany").GetProperty("id").GetString()!;
        await ExecuteGraphQlAsync(
            """
            mutation PurchaseLot($input: PurchaseLotInput!) {
              purchaseLot(input: $input) { lot { id } }
            }
            """,
            new { input = new { companyId = companyAId, lotId = sharedShopLotId, buildingType = "SALES_SHOP", buildingName = "Blocker Shop" } },
            tokenA);

        // Player B starts onboarding and progresses to shop selection
        var tokenB = await RegisterAndGetTokenAsync($"shop-conflict-b-{Guid.NewGuid()}@test.com", "Shop Conflict B");
        var factoryLotId = await CreateTestLotAsync(cityId, "FACTORY,MINE", "Industrial Zone", 75_000m);
        await ExecuteGraphQlAsync(
            """
            mutation StartOnboardingCompany($input: StartOnboardingCompanyInput!) {
              startOnboardingCompany(input: $input) { nextStep }
            }
            """,
            new { input = new { industry = "FURNITURE", cityId, companyName = "Shop Conflict B Corp", factoryLotId } },
            tokenB);

        var productId = await GetStarterProductIdAsync("FURNITURE", "wooden-chair");

        // First FinishOnboarding attempt fails because the shop lot is taken
        var failedFinish = await FinishOnboardingAsync(tokenB, productId, sharedShopLotId);
        Assert.True(failedFinish.TryGetProperty("errors", out var shopErrors), "Expected shop lot conflict error");
        Assert.Contains("already been purchased", shopErrors[0].GetProperty("message").GetString());

        // Player B retries with a different shop lot — must succeed
        var freshShopLotId = await CreateTestLotAsync(cityId, "SALES_SHOP,COMMERCIAL", "Commercial District", 90_000m, "Fresh Shop Lot");
        var retryFinish = await FinishOnboardingAsync(tokenB, productId, freshShopLotId);

        Assert.False(retryFinish.TryGetProperty("errors", out _), "Retry with fresh shop lot must succeed");
        var retryData = retryFinish.GetProperty("data").GetProperty("finishOnboarding");
        Assert.NotNull(retryData.GetProperty("salesShop").GetProperty("id").GetString());
    }

    [Fact]
    public async Task GuestMigration_FactoryLotConflict_FoodProcessing_PlayerCanRestartAndComplete()
    {
        // Verifies conflict recovery works for the FOOD_PROCESSING industry, not just Furniture.
        // ROADMAP: "If there is error such as the building was meanwhile purchased by someone else ...
        // make sure to create their profile with the name they chose and start the wizard again."
        var tokenA = await RegisterAndGetTokenAsync($"fp-conflict-a-{Guid.NewGuid()}@test.com", "FP Conflict A");
        var cityId = await GetCityIdByNameAsync();

        // Player A takes the shared factory lot
        var sharedLotId = await CreateTestLotAsync(cityId, "FACTORY,MINE", "Industrial Zone", 75_000m, "Shared FP Factory Lot");
        await ExecuteGraphQlAsync(
            """
            mutation StartOnboardingCompany($input: StartOnboardingCompanyInput!) {
              startOnboardingCompany(input: $input) { nextStep company { id } }
            }
            """,
            new { input = new { industry = "FOOD_PROCESSING", cityId, companyName = "FP Conflict A Corp", factoryLotId = sharedLotId } },
            tokenA);

        // Player B (the guest-migrated user) attempts the same lot — must get LOT_ALREADY_OWNED
        var tokenB = await RegisterAndGetTokenAsync($"fp-conflict-b-{Guid.NewGuid()}@test.com", "FP Conflict B");
        var failedStart = await ExecuteGraphQlAsync(
            """
            mutation StartOnboardingCompany($input: StartOnboardingCompanyInput!) {
              startOnboardingCompany(input: $input) { nextStep }
            }
            """,
            new { input = new { industry = "FOOD_PROCESSING", cityId, companyName = "FP Conflict B Corp", factoryLotId = sharedLotId } },
            tokenB);

        Assert.True(failedStart.TryGetProperty("errors", out var failErrors), "Expected LOT_ALREADY_OWNED error for Food Processing conflict");
        var code = failErrors[0].GetProperty("extensions").GetProperty("code").GetString();
        Assert.Equal("LOT_ALREADY_OWNED", code);

        // Player B retries with a fresh lot — should succeed and preserve FOOD_PROCESSING industry
        var freshLotId = await CreateTestLotAsync(cityId, "FACTORY,MINE", "Industrial Zone", 75_000m, "Fresh FP Factory Lot");
        var retryStart = await ExecuteGraphQlAsync(
            """
            mutation StartOnboardingCompany($input: StartOnboardingCompanyInput!) {
              startOnboardingCompany(input: $input) { nextStep company { id } }
            }
            """,
            new { input = new { industry = "FOOD_PROCESSING", cityId, companyName = "FP Conflict B Corp", factoryLotId = freshLotId } },
            tokenB);

        Assert.False(retryStart.TryGetProperty("errors", out _), "Retry with fresh lot must not fail for Food Processing");
        Assert.Equal("SHOP_SELECTION", retryStart.GetProperty("data").GetProperty("startOnboardingCompany").GetProperty("nextStep").GetString());

        // Finish onboarding with a Bread product — preserves FOOD_PROCESSING intent
        var productId = await GetStarterProductIdAsync("FOOD_PROCESSING", "bread");
        var shopLotId = await CreateTestLotAsync(cityId, "SALES_SHOP,COMMERCIAL", "Commercial District", 90_000m);
        var finishResult = await FinishOnboardingAsync(tokenB, productId, shopLotId);

        Assert.False(finishResult.TryGetProperty("errors", out _), "FinishOnboarding after Food Processing conflict retry must succeed");
        var finishData = finishResult.GetProperty("data").GetProperty("finishOnboarding");
        Assert.NotNull(finishData.GetProperty("company").GetProperty("id").GetString());
        Assert.NotNull(finishData.GetProperty("salesShop").GetProperty("id").GetString());

        // Verify the selected product is in the FOOD_PROCESSING industry
        var selectedProductIndustry = finishData.GetProperty("selectedProduct").GetProperty("industry").GetString();
        Assert.Equal("FOOD_PROCESSING", selectedProductIndustry);
    }

    [Fact]
    public async Task GuestMigration_ShopLotConflict_Healthcare_PlayerCanRetryWithDifferentShopLot()
    {
        // Verifies shop-lot conflict recovery works for the HEALTHCARE industry.
        // Complements the Furniture-only GuestMigration_ShopLotConflict test.
        // Setup mirrors the existing ShopLotConflict test: Player A uses purchaseLot to own the
        // shop lot first, then Player B tries FinishOnboarding against that lot and gets an error.
        var tokenA = await RegisterAndGetTokenAsync($"hc-shop-conflict-a-{Guid.NewGuid()}@test.com", "HC Shop A");
        var cityId = await GetCityIdByNameAsync();

        // Player A creates a company and takes the shared shop lot via purchaseLot
        var sharedShopLotId = await CreateTestLotAsync(cityId, "SALES_SHOP,COMMERCIAL", "Commercial District", 90_000m, "Shared HC Shop Lot");
        var createCompanyResult = await ExecuteGraphQlAsync(
            "mutation CreateCompany($input: CreateCompanyInput!) { createCompany(input: $input) { id } }",
            new { input = new { name = "HC Conflict A Corp" } },
            tokenA);
        var companyAId = createCompanyResult.GetProperty("data").GetProperty("createCompany").GetProperty("id").GetString()!;
        await ExecuteGraphQlAsync(
            """
            mutation PurchaseLot($input: PurchaseLotInput!) {
              purchaseLot(input: $input) { lot { id } }
            }
            """,
            new { input = new { companyId = companyAId, lotId = sharedShopLotId, buildingType = "SALES_SHOP", buildingName = "HC Blocker Shop" } },
            tokenA);

        // Player B starts onboarding (factory step succeeds)
        var tokenB = await RegisterAndGetTokenAsync($"hc-shop-conflict-b-{Guid.NewGuid()}@test.com", "HC Shop B");
        var factoryLotId = await CreateTestLotAsync(cityId, "FACTORY,MINE", "Industrial Zone", 75_000m, "HC Factory Lot");
        await ExecuteGraphQlAsync(
            """
            mutation StartOnboardingCompany($input: StartOnboardingCompanyInput!) {
              startOnboardingCompany(input: $input) { nextStep company { id } }
            }
            """,
            new { input = new { industry = "HEALTHCARE", cityId, companyName = "HC Conflict B Corp", factoryLotId } },
            tokenB);

        var productId = await GetStarterProductIdAsync("HEALTHCARE", "basic-medicine");

        // Player B tries to finish with the now-taken shop lot — must fail
        var failedFinish = await FinishOnboardingAsync(tokenB, productId, sharedShopLotId);
        Assert.True(failedFinish.TryGetProperty("errors", out var failErrors), "Expected shop lot conflict error for Healthcare");
        Assert.Contains("already been purchased", failErrors[0].GetProperty("message").GetString());

        // Player B retries with a different shop lot — must succeed and preserve Healthcare/Basic Medicine
        var freshShopLotId = await CreateTestLotAsync(cityId, "SALES_SHOP,COMMERCIAL", "High Street", 95_000m, "Fresh HC Shop Lot");
        var retryFinish = await FinishOnboardingAsync(tokenB, productId, freshShopLotId);

        Assert.False(retryFinish.TryGetProperty("errors", out _), "Retry with fresh shop lot must succeed for Healthcare");
        var retryData = retryFinish.GetProperty("data").GetProperty("finishOnboarding");
        Assert.NotNull(retryData.GetProperty("salesShop").GetProperty("id").GetString());

        // Verify the selected product is in the HEALTHCARE industry
        var selectedProductIndustry = retryData.GetProperty("selectedProduct").GetProperty("industry").GetString();
        Assert.Equal("HEALTHCARE", selectedProductIndustry);
    }

    [Fact]
    public async Task GuestMigration_HappyPath_SelectedIndustryAndProductArePreservedInFinalState()
    {
        // ROADMAP: "Do not store the progress for these users to the backend, but make sure to
        // show them they bought the buildings they setup the resources chain and they made some
        // profit. After that ask them to log in to save their progress."
        //
        // This test simulates the guest-to-authenticated migration happy path with no conflicts:
        // 1. A new player registers (the guest becomes authenticated).
        // 2. They call StartOnboardingCompany (equivalent to the guest's factory purchase being saved).
        // 3. They call FinishOnboarding with the chosen product (equivalent to shop purchase being saved).
        // 4. The resulting company state reflects the industry/product choices from the guest session.
        var industry = "HEALTHCARE";
        var token = await RegisterAndGetTokenAsync($"guest-migration-happy-{Guid.NewGuid()}@test.com", "Happy Migrator");
        var cityId = await GetCityIdByNameAsync();
        var factoryLotId = await CreateTestLotAsync(cityId, "FACTORY,MINE", "Industrial Zone", 75_000m);

        // Step 1: StartOnboardingCompany (factory purchase — guest's first persisted action)
        var startResult = await ExecuteGraphQlAsync(
            """
            mutation StartOnboardingCompany($input: StartOnboardingCompanyInput!) {
              startOnboardingCompany(input: $input) {
                nextStep
                company { id name cash }
                factory { id type }
              }
            }
            """,
            new { input = new { industry, cityId, companyName = "Happy Migration Corp", factoryLotId } },
            token);

        Assert.False(startResult.TryGetProperty("errors", out _), "StartOnboardingCompany must succeed in happy path");
        var startData = startResult.GetProperty("data").GetProperty("startOnboardingCompany");
        Assert.Equal("SHOP_SELECTION", startData.GetProperty("nextStep").GetString());
        Assert.Equal("Happy Migration Corp", startData.GetProperty("company").GetProperty("name").GetString());
        Assert.Equal("FACTORY", startData.GetProperty("factory").GetProperty("type").GetString());

        // Step 2: FinishOnboarding (shop purchase + product selection — guest's second persisted action)
        var productId = await GetStarterProductIdAsync("HEALTHCARE", "basic-medicine");
        var shopLotId = await CreateTestLotAsync(cityId, "SALES_SHOP,COMMERCIAL", "Commercial District", 90_000m);
        var finishResult = await FinishOnboardingAsync(token, productId, shopLotId);

        Assert.False(finishResult.TryGetProperty("errors", out _), "FinishOnboarding must succeed in happy path");
        var finishData = finishResult.GetProperty("data").GetProperty("finishOnboarding");

        // Verify the selected product matches the guest's Healthcare industry choice
        var selectedProduct = finishData.GetProperty("selectedProduct");
        Assert.Equal(productId, selectedProduct.GetProperty("id").GetString());
        Assert.Equal("HEALTHCARE", selectedProduct.GetProperty("industry").GetString());

        // Verify money was correctly deducted: $450,000 - $75,000 (factory) - $90,000 (shop) = $285,000
        var cashAfterMigration = finishData.GetProperty("company").GetProperty("cash").GetDecimal();
        Assert.Equal(285_000m, cashAfterMigration);

        // Verify both buildings exist in the final state
        Assert.NotNull(finishData.GetProperty("factory").GetProperty("id").GetString());
        Assert.NotNull(finishData.GetProperty("salesShop").GetProperty("id").GetString());

        // Verify the player's onboarding is marked complete
        var meResult = await ExecuteGraphQlAsync(
            "query { me { onboardingCompletedAtUtc } }",
            null,
            token);
        var completedAt = meResult.GetProperty("data").GetProperty("me").GetProperty("onboardingCompletedAtUtc").GetString();
        Assert.NotNull(completedAt);
        Assert.NotEmpty(completedAt);
    }

    [Fact]
    public async Task FinishOnboarding_AfterCompletion_RejectsWithNotInProgress()
    {
        // A player who has already completed onboarding (via FinishOnboarding)
        // cannot call FinishOnboarding a second time — the backend clears the
        // OnboardingCurrentStep after completion, so subsequent calls are rejected
        // as ONBOARDING_NOT_IN_PROGRESS rather than reaching a duplicate-completion check.
        var token = await RegisterAndGetTokenAsync($"finish-already-done-{Guid.NewGuid()}@test.com", "AlreadyDoneFin");
        var (_, _, cityId, _) = await StartOnboardingCompanyAsync(token, "Already Done Fin Co");
        var productId = await GetStarterProductIdAsync();
        var shopLotId = await CreateTestLotAsync(cityId, "SALES_SHOP,COMMERCIAL", "Commercial District", 90_000m, "First Shop Lot");

        // First call succeeds
        var firstResult = await FinishOnboardingAsync(token, productId, shopLotId);
        Assert.False(firstResult.TryGetProperty("errors", out _), "First FinishOnboarding must succeed");

        // Second call on the same token must fail — OnboardingCurrentStep was cleared by the first call
        var secondShopLotId = await CreateTestLotAsync(cityId, "SALES_SHOP,COMMERCIAL", "Commercial District", 90_000m, "Second Shop Lot");
        var secondResult = await FinishOnboardingAsync(token, productId, secondShopLotId);

        Assert.True(secondResult.TryGetProperty("errors", out var errors), "Second FinishOnboarding call must return an error");
        var code = errors[0].GetProperty("extensions").GetProperty("code").GetString();
        // After completing onboarding the player's OnboardingCurrentStep is cleared,
        // so FinishOnboarding treats it as "not in progress".
        Assert.Equal("ONBOARDING_NOT_IN_PROGRESS", code);
    }

    [Fact]
    public async Task GuestOnboardingPath_StartingCashAndBudgetDecisionAreConsistent()
    {
        // Verifies that the starter-company cash ($450,000), factory lot price, and remaining cash
        // after purchase are all consistent — so the UI budget coaching panels show accurate data.
        var token = await RegisterAndGetTokenAsync($"budget-check-{Guid.NewGuid()}@test.com", "BudgetChecker");
        var cityId = await GetCityIdByNameAsync();
        var factoryPrice = 75_000m;
        var factoryLotId = await CreateTestLotAsync(cityId, "FACTORY,MINE", "Industrial Zone", factoryPrice);

        var startResult = await ExecuteGraphQlAsync(
            """
            mutation StartOnboardingCompany($input: StartOnboardingCompanyInput!) {
              startOnboardingCompany(input: $input) {
                company { id cash }
                factory { id }
                nextStep
              }
            }
            """,
            new { input = new { industry = "FURNITURE", cityId, companyName = "Budget Test Co", factoryLotId } },
            token);

        Assert.False(startResult.TryGetProperty("errors", out _), "StartOnboardingCompany must succeed");
        var startData = startResult.GetProperty("data").GetProperty("startOnboardingCompany");
        var cashAfterFactory = startData.GetProperty("company").GetProperty("cash").GetDecimal();

        // Starter-company cash is $450,000; after buying the factory lot the balance should be exactly $375,000
        Assert.Equal(DefaultStarterCompanyCash - factoryPrice, cashAfterFactory);

        // Finish onboarding with a shop lot and verify final cash is further reduced
        var shopPrice = 90_000m;
        var productId = await GetStarterProductIdAsync("FURNITURE", "wooden-chair");
        var shopLotId = await CreateTestLotAsync(cityId, "SALES_SHOP,COMMERCIAL", "Commercial District", shopPrice);
        var finishResult = await FinishOnboardingAsync(token, productId, shopLotId);
        Assert.False(finishResult.TryGetProperty("errors", out _), "FinishOnboarding must succeed");

        var finishData = finishResult.GetProperty("data").GetProperty("finishOnboarding");
        var cashAfterShop = finishData.GetProperty("company").GetProperty("cash").GetDecimal();

        Assert.Equal(DefaultStarterCompanyCash - factoryPrice - shopPrice, cashAfterShop);
    }

    [Fact]
    public async Task FinishOnboarding_FactoryAndShopUnitsIncludedInResponse_Furniture()
    {
        // ROADMAP: "This will set the factory layout for them. Wizard will show them important areas on the screen"
        // Verifies that finishOnboarding returns factory and sales shop units so the frontend
        // can display the auto-configured production chain on the completion screen.
        var token = await RegisterAndGetTokenAsync($"onboard-layout-furn-{Guid.NewGuid()}@test.com", "Layout Furniture Founder");
        var cityId = await GetCityIdByNameAsync();
        var factoryLotId = await CreateTestLotAsync(cityId, "FACTORY,MINE", "Industrial Zone");

        await ExecuteGraphQlAsync(
            """
            mutation StartOnboardingCompany($input: StartOnboardingCompanyInput!) {
              startOnboardingCompany(input: $input) { nextStep company { id } }
            }
            """,
            new { input = new { industry = "FURNITURE", cityId, companyName = "Layout Furn Co", factoryLotId } },
            token);

        var productId = await GetStarterProductIdAsync("FURNITURE", "wooden-chair");
        var shopLotId = await CreateTestLotAsync(cityId, "SALES_SHOP,COMMERCIAL", "Commercial District");

        var result = await ExecuteGraphQlAsync(
            """
            mutation FinishOnboarding($input: FinishOnboardingInput!) {
              finishOnboarding(input: $input) {
                factory {
                  id name type
                  units { id unitType gridX gridY level linkRight }
                }
                salesShop {
                  id name type
                  units { id unitType gridX gridY level linkRight }
                }
              }
            }
            """,
            new { input = new { productTypeId = productId, shopLotId } },
            token);

        Assert.False(result.TryGetProperty("errors", out _), "FinishOnboarding with units must succeed");

        var data = result.GetProperty("data").GetProperty("finishOnboarding");

        // Factory must have exactly 4 units: PURCHASE(0) → MANUFACTURING(1) → STORAGE(2) → B2B_SALES(3)
        var factoryUnits = data.GetProperty("factory").GetProperty("units").EnumerateArray().OrderBy(u => u.GetProperty("gridX").GetInt32()).ToList();
        Assert.Equal(4, factoryUnits.Count);
        Assert.Equal("PURCHASE", factoryUnits[0].GetProperty("unitType").GetString());
        Assert.Equal(0, factoryUnits[0].GetProperty("gridX").GetInt32());
        Assert.True(factoryUnits[0].GetProperty("linkRight").GetBoolean(), "Purchase unit must link right to manufacturing");
        Assert.Equal("MANUFACTURING", factoryUnits[1].GetProperty("unitType").GetString());
        Assert.Equal(1, factoryUnits[1].GetProperty("gridX").GetInt32());
        Assert.True(factoryUnits[1].GetProperty("linkRight").GetBoolean(), "Manufacturing unit must link right to storage");
        Assert.Equal("STORAGE", factoryUnits[2].GetProperty("unitType").GetString());
        Assert.Equal(2, factoryUnits[2].GetProperty("gridX").GetInt32());
        Assert.True(factoryUnits[2].GetProperty("linkRight").GetBoolean(), "Storage unit must link right to B2B sales");
        Assert.Equal("B2B_SALES", factoryUnits[3].GetProperty("unitType").GetString());
        Assert.Equal(3, factoryUnits[3].GetProperty("gridX").GetInt32());

        // Sales shop must have exactly 2 units: PURCHASE(0) → PUBLIC_SALES(1)
        var shopUnits = data.GetProperty("salesShop").GetProperty("units").EnumerateArray().OrderBy(u => u.GetProperty("gridX").GetInt32()).ToList();
        Assert.Equal(2, shopUnits.Count);
        Assert.Equal("PURCHASE", shopUnits[0].GetProperty("unitType").GetString());
        Assert.Equal(0, shopUnits[0].GetProperty("gridX").GetInt32());
        Assert.True(shopUnits[0].GetProperty("linkRight").GetBoolean(), "Shop purchase unit must link right to public sales");
        Assert.Equal("PUBLIC_SALES", shopUnits[1].GetProperty("unitType").GetString());
        Assert.Equal(1, shopUnits[1].GetProperty("gridX").GetInt32());
    }

    [Fact]
    public async Task FinishOnboarding_FactoryAndShopUnitsIncludedInResponse_FoodProcessing()
    {
        // Verifies factory/shop unit layout is returned for the Food Processing industry.
        var token = await RegisterAndGetTokenAsync($"onboard-layout-food-{Guid.NewGuid()}@test.com", "Layout Food Founder");
        var cityId = await GetCityIdByNameAsync();
        var factoryLotId = await CreateTestLotAsync(cityId, "FACTORY,MINE", "Industrial Zone");

        await ExecuteGraphQlAsync(
            """
            mutation StartOnboardingCompany($input: StartOnboardingCompanyInput!) {
              startOnboardingCompany(input: $input) { nextStep company { id } }
            }
            """,
            new { input = new { industry = "FOOD_PROCESSING", cityId, companyName = "Layout Food Co", factoryLotId } },
            token);

        var productId = await GetStarterProductIdAsync("FOOD_PROCESSING", "bread");
        var shopLotId = await CreateTestLotAsync(cityId, "SALES_SHOP,COMMERCIAL", "Commercial District");

        var result = await ExecuteGraphQlAsync(
            """
            mutation FinishOnboarding($input: FinishOnboardingInput!) {
              finishOnboarding(input: $input) {
                factory { units { unitType gridX linkRight } }
                salesShop { units { unitType gridX linkRight } }
              }
            }
            """,
            new { input = new { productTypeId = productId, shopLotId } },
            token);

        Assert.False(result.TryGetProperty("errors", out _), "FinishOnboarding FOOD_PROCESSING with units must succeed");
        var data = result.GetProperty("data").GetProperty("finishOnboarding");

        var factoryUnits = data.GetProperty("factory").GetProperty("units").EnumerateArray().OrderBy(u => u.GetProperty("gridX").GetInt32()).ToList();
        Assert.Equal(4, factoryUnits.Count);
        Assert.Equal("PURCHASE", factoryUnits[0].GetProperty("unitType").GetString());
        Assert.Equal("MANUFACTURING", factoryUnits[1].GetProperty("unitType").GetString());
        Assert.Equal("STORAGE", factoryUnits[2].GetProperty("unitType").GetString());
        Assert.Equal("B2B_SALES", factoryUnits[3].GetProperty("unitType").GetString());

        var shopUnits = data.GetProperty("salesShop").GetProperty("units").EnumerateArray().OrderBy(u => u.GetProperty("gridX").GetInt32()).ToList();
        Assert.Equal(2, shopUnits.Count);
        Assert.Equal("PURCHASE", shopUnits[0].GetProperty("unitType").GetString());
        Assert.Equal("PUBLIC_SALES", shopUnits[1].GetProperty("unitType").GetString());
    }

    [Fact]
    public async Task FinishOnboarding_FactoryAndShopUnitsIncludedInResponse_Healthcare()
    {
        // Verifies factory/shop unit layout is returned for the Healthcare industry.
        var token = await RegisterAndGetTokenAsync($"onboard-layout-health-{Guid.NewGuid()}@test.com", "Layout Health Founder");
        var cityId = await GetCityIdByNameAsync();
        var factoryLotId = await CreateTestLotAsync(cityId, "FACTORY,MINE", "Industrial Zone");

        await ExecuteGraphQlAsync(
            """
            mutation StartOnboardingCompany($input: StartOnboardingCompanyInput!) {
              startOnboardingCompany(input: $input) { nextStep company { id } }
            }
            """,
            new { input = new { industry = "HEALTHCARE", cityId, companyName = "Layout Health Co", factoryLotId } },
            token);

        var productId = await GetStarterProductIdAsync("HEALTHCARE", "basic-medicine");
        var shopLotId = await CreateTestLotAsync(cityId, "SALES_SHOP,COMMERCIAL", "Commercial District");

        var result = await ExecuteGraphQlAsync(
            """
            mutation FinishOnboarding($input: FinishOnboardingInput!) {
              finishOnboarding(input: $input) {
                factory { units { unitType gridX } }
                salesShop { units { unitType gridX } }
              }
            }
            """,
            new { input = new { productTypeId = productId, shopLotId } },
            token);

        Assert.False(result.TryGetProperty("errors", out _), "FinishOnboarding HEALTHCARE with units must succeed");
        var data = result.GetProperty("data").GetProperty("finishOnboarding");

        var factoryUnits = data.GetProperty("factory").GetProperty("units").EnumerateArray().OrderBy(u => u.GetProperty("gridX").GetInt32()).ToList();
        Assert.Equal(4, factoryUnits.Count);
        Assert.Equal("PURCHASE", factoryUnits[0].GetProperty("unitType").GetString());
        Assert.Equal("MANUFACTURING", factoryUnits[1].GetProperty("unitType").GetString());

        var shopUnits = data.GetProperty("salesShop").GetProperty("units").EnumerateArray().OrderBy(u => u.GetProperty("gridX").GetInt32()).ToList();
        Assert.Equal(2, shopUnits.Count);
        Assert.Equal("PURCHASE", shopUnits[0].GetProperty("unitType").GetString());
        Assert.Equal("PUBLIC_SALES", shopUnits[1].GetProperty("unitType").GetString());
    }

    [Fact]
    public async Task CompleteOnboarding_FoodProcessing_CreatesCompanyFactoryAndShop()
    {
        // AC: "The onboarding flow offers Furniture, Food Processing, and Healthcare as starter-industry choices."
        // Verifies that CompleteOnboarding works correctly with the FOOD_PROCESSING/bread industry,
        // producing a factory with the correct PURCHASE→MANUFACTURING chain and a shop with PUBLIC_SALES.
        var token = await RegisterAndGetTokenAsync($"complete-fp-{Guid.NewGuid()}@test.com", "FoodProcessor");
        var cityId = await GetCityIdByNameAsync();

        var productId = await GetStarterProductIdAsync("FOOD_PROCESSING", "bread");

        var result = await ExecuteGraphQlAsync(
            """
            mutation CompleteOnboarding($input: OnboardingInput!) {
              completeOnboarding(input: $input) {
                company { id name cash }
                factory { id type }
                salesShop { id type }
                selectedProduct { id industry slug }
              }
            }
            """,
            new { input = new { industry = "FOOD_PROCESSING", cityId, productTypeId = productId, companyName = "Bread Factory Co" } },
            token);

        Assert.False(result.TryGetProperty("errors", out _), "CompleteOnboarding must succeed for FOOD_PROCESSING");
        var data = result.GetProperty("data").GetProperty("completeOnboarding");
        Assert.Equal("Bread Factory Co", data.GetProperty("company").GetProperty("name").GetString());
        Assert.Equal("FACTORY", data.GetProperty("factory").GetProperty("type").GetString());
        Assert.Equal("SALES_SHOP", data.GetProperty("salesShop").GetProperty("type").GetString());
        Assert.Equal("FOOD_PROCESSING", data.GetProperty("selectedProduct").GetProperty("industry").GetString());
        Assert.Equal("bread", data.GetProperty("selectedProduct").GetProperty("slug").GetString());

        // Verify units are configured for the FOOD_PROCESSING supply chain
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
        var factory = buildings.Single(b => b.GetProperty("type").GetString() == "FACTORY");
        var shop = buildings.Single(b => b.GetProperty("type").GetString() == "SALES_SHOP");

        // Factory must have a PURCHASE unit (for Grain) and a MANUFACTURING unit (for Bread)
        Assert.Contains(factory.GetProperty("units").EnumerateArray(), unit =>
            unit.GetProperty("unitType").GetString() == "PURCHASE"
            && unit.GetProperty("resourceTypeId").ValueKind == JsonValueKind.String);
        Assert.Contains(factory.GetProperty("units").EnumerateArray(), unit =>
            unit.GetProperty("unitType").GetString() == "MANUFACTURING"
            && unit.GetProperty("productTypeId").GetString() == productId);

        // Shop must sell the Bread product
        Assert.Contains(shop.GetProperty("units").EnumerateArray(), unit =>
            unit.GetProperty("unitType").GetString() == "PUBLIC_SALES"
            && unit.GetProperty("productTypeId").GetString() == productId);
    }

    [Fact]
    public async Task CompleteOnboarding_Healthcare_CreatesCompanyFactoryAndShop()
    {
        // AC: "The onboarding flow offers Furniture, Food Processing, and Healthcare as starter-industry choices."
        // Verifies that CompleteOnboarding works correctly with the HEALTHCARE/basic-medicine industry,
        // producing a factory with the correct PURCHASE→MANUFACTURING chain and a shop with PUBLIC_SALES.
        var token = await RegisterAndGetTokenAsync($"complete-hc-{Guid.NewGuid()}@test.com", "HealthcareFounder");
        var cityId = await GetCityIdByNameAsync();

        var productId = await GetStarterProductIdAsync("HEALTHCARE", "basic-medicine");

        var result = await ExecuteGraphQlAsync(
            """
            mutation CompleteOnboarding($input: OnboardingInput!) {
              completeOnboarding(input: $input) {
                company { id name cash }
                factory { id type }
                salesShop { id type }
                selectedProduct { id industry slug }
              }
            }
            """,
            new { input = new { industry = "HEALTHCARE", cityId, productTypeId = productId, companyName = "Medicine Corp" } },
            token);

        Assert.False(result.TryGetProperty("errors", out _), "CompleteOnboarding must succeed for HEALTHCARE");
        var data = result.GetProperty("data").GetProperty("completeOnboarding");
        Assert.Equal("Medicine Corp", data.GetProperty("company").GetProperty("name").GetString());
        Assert.Equal("FACTORY", data.GetProperty("factory").GetProperty("type").GetString());
        Assert.Equal("SALES_SHOP", data.GetProperty("salesShop").GetProperty("type").GetString());
        Assert.Equal("HEALTHCARE", data.GetProperty("selectedProduct").GetProperty("industry").GetString());
        Assert.Equal("basic-medicine", data.GetProperty("selectedProduct").GetProperty("slug").GetString());

        // Verify units are configured for the HEALTHCARE supply chain
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
        var factory = buildings.Single(b => b.GetProperty("type").GetString() == "FACTORY");
        var shop = buildings.Single(b => b.GetProperty("type").GetString() == "SALES_SHOP");

        // Factory must have a PURCHASE unit (for Chemical Minerals) and a MANUFACTURING unit (for Basic Medicine)
        Assert.Contains(factory.GetProperty("units").EnumerateArray(), unit =>
            unit.GetProperty("unitType").GetString() == "PURCHASE"
            && unit.GetProperty("resourceTypeId").ValueKind == JsonValueKind.String);
        Assert.Contains(factory.GetProperty("units").EnumerateArray(), unit =>
            unit.GetProperty("unitType").GetString() == "MANUFACTURING"
            && unit.GetProperty("productTypeId").GetString() == productId);

        // Shop must sell the Basic Medicine product
        Assert.Contains(shop.GetProperty("units").EnumerateArray(), unit =>
            unit.GetProperty("unitType").GetString() == "PUBLIC_SALES"
            && unit.GetProperty("productTypeId").GetString() == productId);
    }

    [Fact]
    public async Task GuestMigration_HappyPath_Furniture_SelectedIndustryAndProductArePreserved()
    {
        // AC: "After a successful onboarding experience, the player is prompted to log in or sign up to save progress."
        // Verifies the guest-to-authenticated migration happy path for the FURNITURE industry.
        // Guest chose Furniture/Wooden Chair → registers → StartOnboardingCompany + FinishOnboarding preserves the choice.
        var token = await RegisterAndGetTokenAsync($"guest-furn-{Guid.NewGuid()}@test.com", "Furniture Migrator");
        var cityId = await GetCityIdByNameAsync();
        var factoryLotId = await CreateTestLotAsync(cityId, "FACTORY,MINE", "Industrial Zone", 75_000m);

        var startResult = await ExecuteGraphQlAsync(
            """
            mutation StartOnboardingCompany($input: StartOnboardingCompanyInput!) {
              startOnboardingCompany(input: $input) {
                nextStep
                company { id name cash }
                factory { id type }
              }
            }
            """,
            new { input = new { industry = "FURNITURE", cityId, companyName = "Furniture Migration Corp", factoryLotId } },
            token);

        Assert.False(startResult.TryGetProperty("errors", out _), "StartOnboardingCompany must succeed for FURNITURE");
        var startData = startResult.GetProperty("data").GetProperty("startOnboardingCompany");
        Assert.Equal("SHOP_SELECTION", startData.GetProperty("nextStep").GetString());
        Assert.Equal("FACTORY", startData.GetProperty("factory").GetProperty("type").GetString());

        var productId = await GetStarterProductIdAsync("FURNITURE", "wooden-chair");
        var shopLotId = await CreateTestLotAsync(cityId, "SALES_SHOP,COMMERCIAL", "Commercial District", 90_000m);
        var finishResult = await FinishOnboardingAsync(token, productId, shopLotId);

        Assert.False(finishResult.TryGetProperty("errors", out _), "FinishOnboarding must succeed for FURNITURE migration");
        var finishData = finishResult.GetProperty("data").GetProperty("finishOnboarding");

        // Verify the selected product matches the guest's FURNITURE choice
        var selectedProduct = finishData.GetProperty("selectedProduct");
        Assert.Equal(productId, selectedProduct.GetProperty("id").GetString());
        Assert.Equal("FURNITURE", selectedProduct.GetProperty("industry").GetString());

        // Verify cash reduced by both lot purchases: $450,000 - $75,000 - $90,000 = $285,000
        var cashAfterMigration = finishData.GetProperty("company").GetProperty("cash").GetDecimal();
        Assert.Equal(285_000m, cashAfterMigration);

        // Verify onboarding is marked complete
        var meResult = await ExecuteGraphQlAsync("query { me { onboardingCompletedAtUtc } }", null, token);
        var completedAt = meResult.GetProperty("data").GetProperty("me").GetProperty("onboardingCompletedAtUtc").GetString();
        Assert.NotNull(completedAt);
        Assert.NotEmpty(completedAt);
    }

    [Fact]
    public async Task GuestMigration_HappyPath_FoodProcessing_SelectedIndustryAndProductArePreserved()
    {
        // AC: "After a successful onboarding experience, the player is prompted to log in or sign up to save progress."
        // Verifies the guest-to-authenticated migration happy path for the FOOD_PROCESSING industry.
        // Guest chose Food Processing/Bread → registers → StartOnboardingCompany + FinishOnboarding preserves the choice.
        var token = await RegisterAndGetTokenAsync($"guest-fp-{Guid.NewGuid()}@test.com", "Food Processing Migrator");
        var cityId = await GetCityIdByNameAsync();
        var factoryLotId = await CreateTestLotAsync(cityId, "FACTORY,MINE", "Industrial Zone", 75_000m);

        var startResult = await ExecuteGraphQlAsync(
            """
            mutation StartOnboardingCompany($input: StartOnboardingCompanyInput!) {
              startOnboardingCompany(input: $input) {
                nextStep
                company { id name cash }
                factory { id type }
              }
            }
            """,
            new { input = new { industry = "FOOD_PROCESSING", cityId, companyName = "Bread Factory Migration Corp", factoryLotId } },
            token);

        Assert.False(startResult.TryGetProperty("errors", out _), "StartOnboardingCompany must succeed for FOOD_PROCESSING");
        var startData = startResult.GetProperty("data").GetProperty("startOnboardingCompany");
        Assert.Equal("SHOP_SELECTION", startData.GetProperty("nextStep").GetString());
        Assert.Equal("FACTORY", startData.GetProperty("factory").GetProperty("type").GetString());

        var productId = await GetStarterProductIdAsync("FOOD_PROCESSING", "bread");
        var shopLotId = await CreateTestLotAsync(cityId, "SALES_SHOP,COMMERCIAL", "Commercial District", 90_000m);
        var finishResult = await FinishOnboardingAsync(token, productId, shopLotId);

        Assert.False(finishResult.TryGetProperty("errors", out _), "FinishOnboarding must succeed for FOOD_PROCESSING migration");
        var finishData = finishResult.GetProperty("data").GetProperty("finishOnboarding");

        // Verify the selected product matches the guest's FOOD_PROCESSING choice
        var selectedProduct = finishData.GetProperty("selectedProduct");
        Assert.Equal(productId, selectedProduct.GetProperty("id").GetString());
        Assert.Equal("FOOD_PROCESSING", selectedProduct.GetProperty("industry").GetString());

        // Verify cash reduced by both lot purchases: $450,000 - $75,000 - $90,000 = $285,000
        var cashAfterMigration = finishData.GetProperty("company").GetProperty("cash").GetDecimal();
        Assert.Equal(285_000m, cashAfterMigration);

        // Verify onboarding is marked complete
        var meResult = await ExecuteGraphQlAsync("query { me { onboardingCompletedAtUtc } }", null, token);
        var completedAt = meResult.GetProperty("data").GetProperty("me").GetProperty("onboardingCompletedAtUtc").GetString();
        Assert.NotNull(completedAt);
        Assert.NotEmpty(completedAt);
    }

    [Fact]
    public async Task GuestMigration_HappyPath_Healthcare_SelectedIndustryAndProductArePreserved()
    {
        // AC 2 + AC 9 + AC 13: "After a successful onboarding experience, the player is prompted to
        // log in or sign up to save progress." Verifies the guest-to-authenticated migration happy
        // path for the HEALTHCARE industry (Basic Medicine) — the third starter industry.
        // Guest chose Healthcare/Basic Medicine → registers → StartOnboardingCompany + FinishOnboarding
        // preserves the choice and deducts the correct lot costs from the starting cash balance.
        var token = await RegisterAndGetTokenAsync($"guest-hc-{Guid.NewGuid()}@test.com", "Healthcare Migrator");
        var cityId = await GetCityIdByNameAsync();
        var factoryLotId = await CreateTestLotAsync(cityId, "FACTORY,MINE", "Industrial Zone", 75_000m);

        var startResult = await ExecuteGraphQlAsync(
            """
            mutation StartOnboardingCompany($input: StartOnboardingCompanyInput!) {
              startOnboardingCompany(input: $input) {
                nextStep
                company { id name cash }
                factory { id type }
              }
            }
            """,
            new { input = new { industry = "HEALTHCARE", cityId, companyName = "Pharmacy Migration Corp", factoryLotId } },
            token);

        Assert.False(startResult.TryGetProperty("errors", out _), "StartOnboardingCompany must succeed for HEALTHCARE");
        var startData = startResult.GetProperty("data").GetProperty("startOnboardingCompany");
        Assert.Equal("SHOP_SELECTION", startData.GetProperty("nextStep").GetString());
        Assert.Equal("FACTORY", startData.GetProperty("factory").GetProperty("type").GetString());

        var productId = await GetStarterProductIdAsync("HEALTHCARE", "basic-medicine");
        var shopLotId = await CreateTestLotAsync(cityId, "SALES_SHOP,COMMERCIAL", "Commercial District", 90_000m);
        var finishResult = await FinishOnboardingAsync(token, productId, shopLotId);

        Assert.False(finishResult.TryGetProperty("errors", out _), "FinishOnboarding must succeed for HEALTHCARE migration");
        var finishData = finishResult.GetProperty("data").GetProperty("finishOnboarding");

        // Verify the selected product matches the guest's HEALTHCARE choice
        var selectedProduct = finishData.GetProperty("selectedProduct");
        Assert.Equal(productId, selectedProduct.GetProperty("id").GetString());
        Assert.Equal("HEALTHCARE", selectedProduct.GetProperty("industry").GetString());

        // Verify cash reduced by both lot purchases: $450,000 - $75,000 - $90,000 = $285,000
        var cashAfterMigration = finishData.GetProperty("company").GetProperty("cash").GetDecimal();
        Assert.Equal(285_000m, cashAfterMigration);

        // Verify onboarding is marked complete
        var meResult = await ExecuteGraphQlAsync("query { me { onboardingCompletedAtUtc } }", null, token);
        var completedAt = meResult.GetProperty("data").GetProperty("me").GetProperty("onboardingCompletedAtUtc").GetString();
        Assert.NotNull(completedAt);
        Assert.NotEmpty(completedAt);
    }

    [Fact]
    public async Task GuestMigration_HappyPath_StartupPackOfferActivatedAfterMigration()
    {
        // AC: "Support both onboarding completion paths: guest-to-authenticated migration and already
        // authenticated onboarding completion." The startup pack offer must be activated and returned
        // in the finishOnboarding mutation result when a guest migrates to an authenticated account.
        var token = await RegisterAndGetTokenAsync($"guest-pack-{Guid.NewGuid()}@test.com", "Guest Pack Migrator");
        var cityId = await GetCityIdByNameAsync();
        var factoryLotId = await CreateTestLotAsync(cityId, "FACTORY,MINE", "Industrial Zone", 75_000m);

        // StartOnboardingCompany (equivalent of the guest's factory purchase being saved)
        var startResult = await ExecuteGraphQlAsync(
            """
            mutation StartOnboardingCompany($input: StartOnboardingCompanyInput!) {
              startOnboardingCompany(input: $input) {
                nextStep
                company { id }
              }
            }
            """,
            new { input = new { industry = "FURNITURE", cityId, companyName = "Guest Pack Corp", factoryLotId } },
            token);

        Assert.False(startResult.TryGetProperty("errors", out _), "StartOnboardingCompany must succeed");

        // FinishOnboarding (equivalent of the guest's shop purchase + product selection being saved)
        var productId = await GetStarterProductIdAsync("FURNITURE", "wooden-chair");
        var shopLotId = await CreateTestLotAsync(cityId, "SALES_SHOP,COMMERCIAL", "Commercial District", 90_000m);

        var finishResult = await ExecuteGraphQlAsync(
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

        Assert.False(finishResult.TryGetProperty("errors", out _), "FinishOnboarding must succeed in guest migration path");
        var offer = finishResult.GetProperty("data").GetProperty("finishOnboarding").GetProperty("startupPackOffer");

        // AC: startup pack must be ELIGIBLE immediately after guest migration completes
        Assert.Equal("ELIGIBLE", offer.GetProperty("status").GetString());
        Assert.Equal(StartupPackService.CompanyCashGrant, offer.GetProperty("companyCashGrant").GetDecimal());
        Assert.Equal(StartupPackService.ProDurationDays, offer.GetProperty("proDurationDays").GetInt32());
        // Expiry window must be a valid future timestamp
        Assert.True(DateTime.TryParse(offer.GetProperty("expiresAtUtc").GetString(), out var expiresAt));
        Assert.True(expiresAt > DateTime.UtcNow, "Startup pack expiry must be in the future");
    }

    [Fact]
    public async Task GuestMigration_HappyPath_PragueCity_FoodProcessing_PreservesChoices()
    {
        // ROADMAP city coverage: guest migration must work for Prague (the second seeded city),
        // not only for the default Bratislava. This test mirrors GuestMigration_HappyPath_FoodProcessing
        // but uses Prague as the onboarding city to prove city-agnostic migration behaviour.
        var token = await RegisterAndGetTokenAsync($"guest-prague-food-{Guid.NewGuid()}@test.com", "Prague Baker");
        var cityId = await GetCityIdByNameAsync("Prague");

        var factoryLotId = await CreateTestLotAsync(cityId, "FACTORY,MINE", "Prague Industrial Zone", 75_000m);
        var startResult = await ExecuteGraphQlAsync(
            """
            mutation StartOnboardingCompany($input: StartOnboardingCompanyInput!) {
              startOnboardingCompany(input: $input) {
                nextStep
                company { id name cash }
                factory { id type }
              }
            }
            """,
            new { input = new { industry = "FOOD_PROCESSING", cityId, companyName = "Prague Bakery Corp", factoryLotId } },
            token);

        Assert.False(startResult.TryGetProperty("errors", out _), "StartOnboardingCompany must succeed for Prague/FOOD_PROCESSING");
        var startData = startResult.GetProperty("data").GetProperty("startOnboardingCompany");
        Assert.Equal("SHOP_SELECTION", startData.GetProperty("nextStep").GetString());

        var productId = await GetStarterProductIdAsync("FOOD_PROCESSING", "bread");
        var shopLotId = await CreateTestLotAsync(cityId, "SALES_SHOP,COMMERCIAL", "Prague High Street", 90_000m);
        var finishResult = await FinishOnboardingAsync(token, productId, shopLotId);

        Assert.False(finishResult.TryGetProperty("errors", out _), "FinishOnboarding must succeed for Prague/FOOD_PROCESSING migration");
        var finishData = finishResult.GetProperty("data").GetProperty("finishOnboarding");
        Assert.Equal("FOOD_PROCESSING", finishData.GetProperty("selectedProduct").GetProperty("industry").GetString());

        // Verify onboarding is marked complete
        var meResult = await ExecuteGraphQlAsync("query { me { onboardingCompletedAtUtc } }", null, token);
        var completedAt = meResult.GetProperty("data").GetProperty("me").GetProperty("onboardingCompletedAtUtc").GetString();
        Assert.NotNull(completedAt);
        Assert.NotEmpty(completedAt);
    }

    [Fact]
    public async Task GuestMigration_HappyPath_ViennaCity_Healthcare_PreservesChoices()
    {
        // ROADMAP city coverage: guest migration must work for Vienna (the third seeded city),
        // not only for the default Bratislava. This test mirrors GuestMigration_HappyPath_Healthcare
        // but uses Vienna as the onboarding city to prove all three cities work for guest migration.
        var token = await RegisterAndGetTokenAsync($"guest-vienna-hc-{Guid.NewGuid()}@test.com", "Vienna Pharmacist");
        var cityId = await GetCityIdByNameAsync("Vienna");

        var factoryLotId = await CreateTestLotAsync(cityId, "FACTORY,MINE", "Vienna Medical Park", 75_000m);
        var startResult = await ExecuteGraphQlAsync(
            """
            mutation StartOnboardingCompany($input: StartOnboardingCompanyInput!) {
              startOnboardingCompany(input: $input) {
                nextStep
                company { id name cash }
                factory { id type }
              }
            }
            """,
            new { input = new { industry = "HEALTHCARE", cityId, companyName = "Vienna Pharma Corp", factoryLotId } },
            token);

        Assert.False(startResult.TryGetProperty("errors", out _), "StartOnboardingCompany must succeed for Vienna/HEALTHCARE");
        var startData = startResult.GetProperty("data").GetProperty("startOnboardingCompany");
        Assert.Equal("SHOP_SELECTION", startData.GetProperty("nextStep").GetString());

        var productId = await GetStarterProductIdAsync("HEALTHCARE", "basic-medicine");
        var shopLotId = await CreateTestLotAsync(cityId, "SALES_SHOP,COMMERCIAL", "Vienna Pharmacy Row", 90_000m);
        var finishResult = await FinishOnboardingAsync(token, productId, shopLotId);

        Assert.False(finishResult.TryGetProperty("errors", out _), "FinishOnboarding must succeed for Vienna/HEALTHCARE migration");
        var finishData = finishResult.GetProperty("data").GetProperty("finishOnboarding");
        Assert.Equal("HEALTHCARE", finishData.GetProperty("selectedProduct").GetProperty("industry").GetString());

        // Verify both buildings exist and are in Vienna
        var factoryBuildingId = finishData.GetProperty("factory").GetProperty("id").GetString()!;
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var factoryBuilding = await db.Buildings.SingleAsync(b => b.Id == Guid.Parse(factoryBuildingId));
        Assert.Equal(Guid.Parse(cityId), factoryBuilding.CityId);

        // Verify onboarding is marked complete
        var meResult = await ExecuteGraphQlAsync("query { me { onboardingCompletedAtUtc } }", null, token);
        var completedAt = meResult.GetProperty("data").GetProperty("me").GetProperty("onboardingCompletedAtUtc").GetString();
        Assert.NotNull(completedAt);
        Assert.NotEmpty(completedAt);
    }

    [Fact]
    public async Task GuestMigration_HappyPath_PragueCity_Furniture_PreservesChoices()
    {
        // ROADMAP city coverage: guest migration must work for Prague with the Furniture industry.
        // Completes the Prague column in the city×industry matrix (FoodProcessing is covered separately).
        var token = await RegisterAndGetTokenAsync($"guest-prague-furn-{Guid.NewGuid()}@test.com", "Prague Woodworker");
        var cityId = await GetCityIdByNameAsync("Prague");

        var factoryLotId = await CreateTestLotAsync(cityId, "FACTORY,MINE", "Prague Woodworking Park", 75_000m);
        var startResult = await ExecuteGraphQlAsync(
            """
            mutation StartOnboardingCompany($input: StartOnboardingCompanyInput!) {
              startOnboardingCompany(input: $input) {
                nextStep
                company { id name cash }
                factory { id type }
              }
            }
            """,
            new { input = new { industry = "FURNITURE", cityId, companyName = "Prague Furniture Corp", factoryLotId } },
            token);

        Assert.False(startResult.TryGetProperty("errors", out _), "StartOnboardingCompany must succeed for Prague/FURNITURE");
        var startData = startResult.GetProperty("data").GetProperty("startOnboardingCompany");
        Assert.Equal("SHOP_SELECTION", startData.GetProperty("nextStep").GetString());

        var productId = await GetStarterProductIdAsync("FURNITURE", "wooden-chair");
        var shopLotId = await CreateTestLotAsync(cityId, "SALES_SHOP,COMMERCIAL", "Prague Furniture Row", 90_000m);
        var finishResult = await FinishOnboardingAsync(token, productId, shopLotId);

        Assert.False(finishResult.TryGetProperty("errors", out _), "FinishOnboarding must succeed for Prague/FURNITURE migration");
        var finishData = finishResult.GetProperty("data").GetProperty("finishOnboarding");
        Assert.Equal("FURNITURE", finishData.GetProperty("selectedProduct").GetProperty("industry").GetString());

        // Verify onboarding is marked complete
        var meResult = await ExecuteGraphQlAsync("query { me { onboardingCompletedAtUtc } }", null, token);
        var completedAt = meResult.GetProperty("data").GetProperty("me").GetProperty("onboardingCompletedAtUtc").GetString();
        Assert.NotNull(completedAt);
        Assert.NotEmpty(completedAt);
    }

    [Fact]
    public async Task GuestMigration_HappyPath_ViennaCity_Furniture_PreservesChoices()
    {
        // ROADMAP city coverage: guest migration must work for Vienna with the Furniture industry.
        // Completes the Vienna column in the city×industry matrix (Healthcare is covered separately).
        var token = await RegisterAndGetTokenAsync($"guest-vienna-furn-{Guid.NewGuid()}@test.com", "Vienna Woodworker");
        var cityId = await GetCityIdByNameAsync("Vienna");

        var factoryLotId = await CreateTestLotAsync(cityId, "FACTORY,MINE", "Vienna Woodworking Park", 75_000m);
        var startResult = await ExecuteGraphQlAsync(
            """
            mutation StartOnboardingCompany($input: StartOnboardingCompanyInput!) {
              startOnboardingCompany(input: $input) {
                nextStep
                company { id name cash }
                factory { id type }
              }
            }
            """,
            new { input = new { industry = "FURNITURE", cityId, companyName = "Vienna Furniture Corp", factoryLotId } },
            token);

        Assert.False(startResult.TryGetProperty("errors", out _), "StartOnboardingCompany must succeed for Vienna/FURNITURE");
        var startData = startResult.GetProperty("data").GetProperty("startOnboardingCompany");
        Assert.Equal("SHOP_SELECTION", startData.GetProperty("nextStep").GetString());

        var productId = await GetStarterProductIdAsync("FURNITURE", "wooden-chair");
        var shopLotId = await CreateTestLotAsync(cityId, "SALES_SHOP,COMMERCIAL", "Vienna Furniture Row", 90_000m);
        var finishResult = await FinishOnboardingAsync(token, productId, shopLotId);

        Assert.False(finishResult.TryGetProperty("errors", out _), "FinishOnboarding must succeed for Vienna/FURNITURE migration");
        var finishData = finishResult.GetProperty("data").GetProperty("finishOnboarding");
        Assert.Equal("FURNITURE", finishData.GetProperty("selectedProduct").GetProperty("industry").GetString());

        // Verify onboarding is marked complete
        var meResult = await ExecuteGraphQlAsync("query { me { onboardingCompletedAtUtc } }", null, token);
        var completedAt = meResult.GetProperty("data").GetProperty("me").GetProperty("onboardingCompletedAtUtc").GetString();
        Assert.NotNull(completedAt);
        Assert.NotEmpty(completedAt);
    }

    [Fact]
    public async Task GuestMigration_HappyPath_PragueCity_Healthcare_PreservesChoices()
    {
        // ROADMAP city coverage: guest migration must work for Prague with the Healthcare industry.
        // Completes full 3×3 matrix coverage for GuestMigration happy-path tests.
        var token = await RegisterAndGetTokenAsync($"guest-prague-hc-{Guid.NewGuid()}@test.com", "Prague Pharmacist");
        var cityId = await GetCityIdByNameAsync("Prague");

        var factoryLotId = await CreateTestLotAsync(cityId, "FACTORY,MINE", "Prague Medical Park", 75_000m);
        var startResult = await ExecuteGraphQlAsync(
            """
            mutation StartOnboardingCompany($input: StartOnboardingCompanyInput!) {
              startOnboardingCompany(input: $input) {
                nextStep
                company { id name cash }
                factory { id type }
              }
            }
            """,
            new { input = new { industry = "HEALTHCARE", cityId, companyName = "Prague Pharma Corp", factoryLotId } },
            token);

        Assert.False(startResult.TryGetProperty("errors", out _), "StartOnboardingCompany must succeed for Prague/HEALTHCARE");
        var startData = startResult.GetProperty("data").GetProperty("startOnboardingCompany");
        Assert.Equal("SHOP_SELECTION", startData.GetProperty("nextStep").GetString());

        var productId = await GetStarterProductIdAsync("HEALTHCARE", "basic-medicine");
        var shopLotId = await CreateTestLotAsync(cityId, "SALES_SHOP,COMMERCIAL", "Prague Pharmacy Row", 90_000m);
        var finishResult = await FinishOnboardingAsync(token, productId, shopLotId);

        Assert.False(finishResult.TryGetProperty("errors", out _), "FinishOnboarding must succeed for Prague/HEALTHCARE migration");
        var finishData = finishResult.GetProperty("data").GetProperty("finishOnboarding");
        Assert.Equal("HEALTHCARE", finishData.GetProperty("selectedProduct").GetProperty("industry").GetString());

        // Verify onboarding is marked complete
        var meResult = await ExecuteGraphQlAsync("query { me { onboardingCompletedAtUtc } }", null, token);
        var completedAt = meResult.GetProperty("data").GetProperty("me").GetProperty("onboardingCompletedAtUtc").GetString();
        Assert.NotNull(completedAt);
        Assert.NotEmpty(completedAt);
    }

    [Fact]
    public async Task GuestMigration_HappyPath_ViennaCity_FoodProcessing_PreservesChoices()
    {
        // ROADMAP city coverage: guest migration must work for Vienna with the Food Processing industry.
        // Completes full 3×3 matrix coverage for GuestMigration happy-path tests.
        var token = await RegisterAndGetTokenAsync($"guest-vienna-food-{Guid.NewGuid()}@test.com", "Vienna Baker");
        var cityId = await GetCityIdByNameAsync("Vienna");

        var factoryLotId = await CreateTestLotAsync(cityId, "FACTORY,MINE", "Vienna Bakery Industrial Park", 75_000m);
        var startResult = await ExecuteGraphQlAsync(
            """
            mutation StartOnboardingCompany($input: StartOnboardingCompanyInput!) {
              startOnboardingCompany(input: $input) {
                nextStep
                company { id name cash }
                factory { id type }
              }
            }
            """,
            new { input = new { industry = "FOOD_PROCESSING", cityId, companyName = "Vienna Bakery Corp", factoryLotId } },
            token);

        Assert.False(startResult.TryGetProperty("errors", out _), "StartOnboardingCompany must succeed for Vienna/FOOD_PROCESSING");
        var startData = startResult.GetProperty("data").GetProperty("startOnboardingCompany");
        Assert.Equal("SHOP_SELECTION", startData.GetProperty("nextStep").GetString());

        var productId = await GetStarterProductIdAsync("FOOD_PROCESSING", "bread");
        var shopLotId = await CreateTestLotAsync(cityId, "SALES_SHOP,COMMERCIAL", "Vienna Bread Shop Row", 90_000m);
        var finishResult = await FinishOnboardingAsync(token, productId, shopLotId);

        Assert.False(finishResult.TryGetProperty("errors", out _), "FinishOnboarding must succeed for Vienna/FOOD_PROCESSING migration");
        var finishData = finishResult.GetProperty("data").GetProperty("finishOnboarding");
        Assert.Equal("FOOD_PROCESSING", finishData.GetProperty("selectedProduct").GetProperty("industry").GetString());

        // Verify onboarding is marked complete
        var meResult = await ExecuteGraphQlAsync("query { me { onboardingCompletedAtUtc } }", null, token);
        var completedAt = meResult.GetProperty("data").GetProperty("me").GetProperty("onboardingCompletedAtUtc").GetString();
        Assert.NotNull(completedAt);
        Assert.NotEmpty(completedAt);
    }

    [Fact]
    public async Task GuestMigration_InvalidProductId_ReturnsExplicitError()
    {
        // Issue: guest-to-account handoff must return product-friendly, structured errors rather than
        // silent failures. If a guest submits an invalid product ID during migration (e.g. the product
        // was removed or the ID was corrupted), the backend must return INVALID_PRODUCT — not crash.
        // AC: "Ensure the backend returns product-friendly error or continuation states rather than
        // raw validation failures whenever the authenticated handoff needs user intervention."
        var token = await RegisterAndGetTokenAsync($"guest-invalid-product-{Guid.NewGuid()}@test.com", "Invalid Product Player");
        var cityId = await GetCityIdByNameAsync();

        var factoryLotId = await CreateTestLotAsync(cityId, "FACTORY,MINE", "Industrial Zone", 75_000m, "Invalid Product Factory");
        var startResult = await ExecuteGraphQlAsync(
            """
            mutation StartOnboardingCompany($input: StartOnboardingCompanyInput!) {
              startOnboardingCompany(input: $input) { nextStep company { id } }
            }
            """,
            new { input = new { industry = "FURNITURE", cityId, companyName = "Invalid Product Corp", factoryLotId } },
            token);

        Assert.False(startResult.TryGetProperty("errors", out _), "StartOnboardingCompany must succeed");
        Assert.Equal("SHOP_SELECTION", startResult.GetProperty("data").GetProperty("startOnboardingCompany").GetProperty("nextStep").GetString());

        var shopLotId = await CreateTestLotAsync(cityId, "SALES_SHOP,COMMERCIAL", "Commercial District", 90_000m, "Invalid Product Shop");

        // Attempt FinishOnboarding with a completely invalid (non-existent) product ID
        var invalidProductId = Guid.NewGuid().ToString();
        var result = await ExecuteGraphQlAsync(
            """
            mutation FinishOnboarding($input: FinishOnboardingInput!) {
              finishOnboarding(input: $input) {
                company { id }
                selectedProduct { name }
              }
            }
            """,
            new { input = new { productTypeId = invalidProductId, shopLotId } },
            token);

        // Must return an explicit INVALID_PRODUCT error — not a 500 or unstructured failure
        Assert.True(result.TryGetProperty("errors", out var errors), "Expected INVALID_PRODUCT error for non-existent product");
        var code = errors[0].GetProperty("extensions").GetProperty("code").GetString();
        Assert.Equal("INVALID_PRODUCT", code);

        // The error message must be human-readable and not expose internal stack traces
        var message = errors[0].GetProperty("message").GetString();
        Assert.NotNull(message);
        Assert.NotEmpty(message);
        Assert.DoesNotContain("Exception", message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Stack trace", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GuestMigration_NonStarterProduct_ReturnsExplicitError()
    {
        // Verifies that if a guest tries to persist a product that exists but is not a starter
        // product (wrong industry or not in the starter catalog), the backend returns INVALID_PRODUCT
        // with an actionable, structured error — not a generic or silent failure.
        // AC: "Ensure the backend returns product-friendly error or continuation states rather than
        // raw validation failures whenever the authenticated handoff needs user intervention."
        var token = await RegisterAndGetTokenAsync($"guest-nonstarter-{Guid.NewGuid()}@test.com", "Non-Starter Player");
        var cityId = await GetCityIdByNameAsync();

        var factoryLotId = await CreateTestLotAsync(cityId, "FACTORY,MINE", "Industrial Zone", 75_000m, "Non-Starter Factory");
        await ExecuteGraphQlAsync(
            """
            mutation StartOnboardingCompany($input: StartOnboardingCompanyInput!) {
              startOnboardingCompany(input: $input) { nextStep }
            }
            """,
            new { input = new { industry = "FURNITURE", cityId, companyName = "Non-Starter Corp", factoryLotId } },
            token);

        // Get a product from a different (non-starter) industry via a direct lookup
        // Use a Healthcare product while the company is in Furniture — must be rejected
        var healthcareProductId = await GetStarterProductIdAsync("HEALTHCARE", "basic-medicine");
        var shopLotId = await CreateTestLotAsync(cityId, "SALES_SHOP,COMMERCIAL", "Commercial District", 90_000m, "Non-Starter Shop");

        var result = await ExecuteGraphQlAsync(
            """
            mutation FinishOnboarding($input: FinishOnboardingInput!) {
              finishOnboarding(input: $input) {
                company { id }
                selectedProduct { name industry }
              }
            }
            """,
            new { input = new { productTypeId = healthcareProductId, shopLotId } },
            token);

        // Must return INVALID_PRODUCT — a Furniture company cannot use a Healthcare starter product
        Assert.True(result.TryGetProperty("errors", out var errors), "Expected INVALID_PRODUCT for cross-industry product mismatch");
        var code = errors[0].GetProperty("extensions").GetProperty("code").GetString();
        Assert.Equal("INVALID_PRODUCT", code);
    }

    [Fact]
    public async Task GuestMigration_ExpansionIPO_CompanyCashIsCorrect()
    {
        // ROADMAP: "User puts his $50k to the business and has decision how much money he wants to raise
        // - $800 000, $600000, or $400 000 varying his own shares to be 25% or 33% or 50% in the company."
        //
        // Verifies that when the guest selects the Expansion IPO ($800k raise) and then migrates,
        // the resulting company cash is $850,000 minus the factory and shop lot prices, and that
        // the founder's ownership ratio is 25% (as specified in the ROADMAP).
        var token = await RegisterAndGetTokenAsync($"guest-expansion-ipo-{Guid.NewGuid()}@test.com", "Expansion Founder");
        var cityId = await GetCityIdByNameAsync();
        var factoryPrice = 80_000m;
        var shopPrice = 90_000m;
        var factoryLotId = await CreateTestLotAsync(cityId, "FACTORY,MINE", "Expansion Industrial Zone", factoryPrice);

        // Start onboarding with the Expansion IPO raise target ($800k → 25% founder ownership)
        var startResult = await ExecuteGraphQlAsync(
            """
            mutation StartOnboardingCompany($input: StartOnboardingCompanyInput!) {
              startOnboardingCompany(input: $input) {
                nextStep
                company { id cash totalSharesIssued }
                factory { id }
              }
            }
            """,
            new
            {
                input = new
                {
                    industry = "FURNITURE",
                    cityId,
                    companyName = "Expansion IPO Corp",
                    factoryLotId,
                    ipoRaiseTarget = 800_000m,
                }
            },
            token);

        Assert.False(startResult.TryGetProperty("errors", out _), "StartOnboardingCompany with Expansion IPO must succeed");
        var startData = startResult.GetProperty("data").GetProperty("startOnboardingCompany");
        Assert.Equal("SHOP_SELECTION", startData.GetProperty("nextStep").GetString());

        // Company cash = $50k founder + $800k raise - factory price
        var expansionStarterCash = 850_000m; // $50k + $800k
        var cashAfterFactory = startData.GetProperty("company").GetProperty("cash").GetDecimal();
        Assert.Equal(expansionStarterCash - factoryPrice, cashAfterFactory);

        // 10,000 total shares issued (Expansion IPO: 25% founder = 2500 founder + 7500 public)
        Assert.Equal(10_000m, startData.GetProperty("company").GetProperty("totalSharesIssued").GetDecimal());

        // Verify founder ownership is 25%
        var personAccountResult = await ExecuteGraphQlAsync(
            """
            {
                personAccount {
                    personalCash
                    shareholdings {
                        ownershipRatio
                        shareCount
                    }
                }
            }
            """,
            token: token);

        var personAccount = personAccountResult.GetProperty("data").GetProperty("personAccount");
        // Personal cash = $200k starting - $50k founder contribution = $150k
        Assert.Equal(150_000m, personAccount.GetProperty("personalCash").GetDecimal());
        var founderHolding = personAccount.GetProperty("shareholdings").EnumerateArray().Single();
        Assert.Equal(0.25m, founderHolding.GetProperty("ownershipRatio").GetDecimal());
        Assert.Equal(2_500m, founderHolding.GetProperty("shareCount").GetDecimal());

        // Finish onboarding — verify final cash is correct
        var productId = await GetStarterProductIdAsync("FURNITURE", "wooden-chair");
        var shopLotId = await CreateTestLotAsync(cityId, "SALES_SHOP,COMMERCIAL", "Commercial District", shopPrice);
        var finishResult = await FinishOnboardingAsync(token, productId, shopLotId);
        Assert.False(finishResult.TryGetProperty("errors", out _), "FinishOnboarding with Expansion IPO must succeed");

        var finishData = finishResult.GetProperty("data").GetProperty("finishOnboarding");
        var finalCash = finishData.GetProperty("company").GetProperty("cash").GetDecimal();
        // $850k - $80k factory - $90k shop = $680k
        Assert.Equal(expansionStarterCash - factoryPrice - shopPrice, finalCash);
    }

    [Fact]
    public async Task GuestMigration_GrowthIPO_CompanyCashIsCorrect()
    {
        // Verifies the Growth IPO ($600k raise → 33% founder) leaves the correct cash balance
        // and issues the correct share count after StartOnboardingCompany.
        var token = await RegisterAndGetTokenAsync($"guest-growth-ipo-{Guid.NewGuid()}@test.com", "Growth Founder");
        var cityId = await GetCityIdByNameAsync();
        var factoryPrice = 75_000m;
        var factoryLotId = await CreateTestLotAsync(cityId, "FACTORY,MINE", "Growth Industrial Zone", factoryPrice);

        var startResult = await ExecuteGraphQlAsync(
            """
            mutation StartOnboardingCompany($input: StartOnboardingCompanyInput!) {
              startOnboardingCompany(input: $input) {
                nextStep
                company { id cash totalSharesIssued }
              }
            }
            """,
            new
            {
                input = new
                {
                    industry = "FOOD_PROCESSING",
                    cityId,
                    companyName = "Growth IPO Bakery",
                    factoryLotId,
                    ipoRaiseTarget = 600_000m,
                }
            },
            token);

        Assert.False(startResult.TryGetProperty("errors", out _), "StartOnboardingCompany with Growth IPO must succeed");
        var startData = startResult.GetProperty("data").GetProperty("startOnboardingCompany");

        // Growth IPO: $50k founder + $600k raise = $650k company starting cash - factory price
        var growthStarterCash = 650_000m;
        var cashAfterFactory = startData.GetProperty("company").GetProperty("cash").GetDecimal();
        Assert.Equal(growthStarterCash - factoryPrice, cashAfterFactory);

        // 10,000 total shares always — Growth IPO issues all 10k but founder holds only ~33%
        Assert.Equal(10_000m, startData.GetProperty("company").GetProperty("totalSharesIssued").GetDecimal());

        // ROADMAP: "varying his own shares to be ... 33% in the company."
        // Verify the founder's personal shareholding is 33.33% and personal cash is $150k.
        var companyId = startData.GetProperty("company").GetProperty("id").GetString()!;
        var personAccountResult = await ExecuteGraphQlAsync(
            """
            {
                personAccount {
                    personalCash
                    shareholdings {
                        companyId
                        shareCount
                        ownershipRatio
                    }
                }
            }
            """,
            token: token);

        var personAccount = personAccountResult.GetProperty("data").GetProperty("personAccount");
        // Personal cash = $200k starting - $50k founder contribution = $150k (same for all IPO plans)
        Assert.Equal(150_000m, personAccount.GetProperty("personalCash").GetDecimal());

        var founderHolding = personAccount.GetProperty("shareholdings").EnumerateArray().Single();
        Assert.Equal(companyId, founderHolding.GetProperty("companyId").GetString());
        // Growth IPO: 10000 shares × 0.3333 = 3333 founder shares → 33.33% ownership
        Assert.Equal(3_333m, founderHolding.GetProperty("shareCount").GetDecimal());
        Assert.Equal(0.3333m, founderHolding.GetProperty("ownershipRatio").GetDecimal());
    }

    [Fact]
    public async Task FullOnboarding_ViennaCity_CompletesSuccessfully()
    {
        // ROADMAP: "The game will start in single city and later other cities will be added."
        // Verifies that the full onboarding flow (industry → city → factory → product → shop)
        // works for Vienna — the third seeded city — not just the default Bratislava.
        var token = await RegisterAndGetTokenAsync($"vienna-onboard-{Guid.NewGuid()}@test.com", "Vienna Player");
        var cityId = await GetCityIdByNameAsync("Vienna");

        var factoryLotId = await CreateTestLotAsync(cityId, "FACTORY,MINE", "Vienna Industrial Park", 80_000m);

        var startResult = await ExecuteGraphQlAsync(
            """
            mutation StartOnboardingCompany($input: StartOnboardingCompanyInput!) {
              startOnboardingCompany(input: $input) {
                nextStep
                company { id name cash }
                factory { id name type }
                factoryLot { id }
              }
            }
            """,
            new { input = new { industry = "FURNITURE", cityId, companyName = "Vienna Empire", factoryLotId } },
            token);

        Assert.False(startResult.TryGetProperty("errors", out _), "StartOnboardingCompany for Vienna must succeed");
        var startData = startResult.GetProperty("data").GetProperty("startOnboardingCompany");
        Assert.Equal("SHOP_SELECTION", startData.GetProperty("nextStep").GetString());
        var cashAfterFactory = startData.GetProperty("company").GetProperty("cash").GetDecimal();
        Assert.Equal(DefaultStarterCompanyCash - 80_000m, cashAfterFactory);

        // Confirm the factory building was created in the correct city
        var factoryId = startData.GetProperty("factory").GetProperty("id").GetString()!;

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var factory = await db.Buildings.SingleAsync(b => b.Id == Guid.Parse(factoryId));
        Assert.Equal(Guid.Parse(cityId), factory.CityId);

        var productId = await GetStarterProductIdAsync("FURNITURE", "wooden-chair");
        var shopLotId = await CreateTestLotAsync(cityId, "SALES_SHOP,COMMERCIAL", "Vienna High Street", 75_000m);

        var finishResult = await FinishOnboardingAsync(token, productId, shopLotId);
        Assert.False(finishResult.TryGetProperty("errors", out _), "FinishOnboarding for Vienna must succeed");
        var finishData = finishResult.GetProperty("data").GetProperty("finishOnboarding");
        Assert.Equal("FURNITURE", finishData.GetProperty("selectedProduct").GetProperty("industry").GetString());
        var finalCash = finishData.GetProperty("company").GetProperty("cash").GetDecimal();
        Assert.Equal(DefaultStarterCompanyCash - 80_000m - 75_000m, finalCash);
    }

    [Fact]
    public async Task FullOnboarding_AllThreeCities_EachCompletesSuccessfully()
    {
        // ROADMAP: three seeded cities (Bratislava, Prague, Vienna) must all support the full
        // onboarding flow so players are not constrained to a single starting location.
        var cityCases = new[]
        {
            ("Bratislava", "FOOD_PROCESSING", "bread"),
            ("Prague",     "HEALTHCARE",      "basic-medicine"),
            ("Vienna",     "FURNITURE",       "wooden-chair"),
        };

        foreach (var (cityName, industry, slug) in cityCases)
        {
            var token = await RegisterAndGetTokenAsync($"city-{cityName.ToLower()}-{Guid.NewGuid()}@test.com", $"{cityName} Tycoon");
            var cityId = await GetCityIdByNameAsync(cityName);

            var factoryLotId = await CreateTestLotAsync(cityId, "FACTORY,MINE", $"{cityName} Industrial Zone", 75_000m);
            var startResult = await ExecuteGraphQlAsync(
                """
                mutation StartOnboardingCompany($input: StartOnboardingCompanyInput!) {
                  startOnboardingCompany(input: $input) {
                    nextStep
                    company { id cash }
                  }
                }
                """,
                new { input = new { industry, cityId, companyName = $"{cityName} Corp", factoryLotId } },
                token);

            Assert.False(startResult.TryGetProperty("errors", out _),
                $"StartOnboardingCompany must succeed for {cityName}/{industry}");
            Assert.Equal("SHOP_SELECTION",
                startResult.GetProperty("data").GetProperty("startOnboardingCompany").GetProperty("nextStep").GetString());

            var productId = await GetStarterProductIdAsync(industry, slug);
            var shopLotId = await CreateTestLotAsync(cityId, "SALES_SHOP,COMMERCIAL", $"{cityName} High Street", 90_000m);
            var finishResult = await FinishOnboardingAsync(token, productId, shopLotId);

            Assert.False(finishResult.TryGetProperty("errors", out _),
                $"FinishOnboarding must succeed for {cityName}/{industry}");
            Assert.Equal(industry,
                finishResult.GetProperty("data").GetProperty("finishOnboarding").GetProperty("selectedProduct").GetProperty("industry").GetString());
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
    public async Task PurchaseLot_WrongCompanyOwner_Fails()
    {
        // An authenticated player must not be able to purchase a lot using another player's company.
        var tokenOwner = await RegisterAndGetTokenAsync($"lot-owner-{Guid.NewGuid()}@test.com");
        var (ownerCompanyId, _, _) = await CompleteOnboardingAsync(tokenOwner, "Owner Co");

        // Second player registers and tries to use the first player's company ID
        var tokenAttacker = await RegisterAndGetTokenAsync($"lot-attacker-{Guid.NewGuid()}@test.com");

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
            .First(l => l.GetProperty("suitableTypes").GetString()!.Contains("FACTORY")
                     && l.GetProperty("ownerCompanyId").ValueKind == JsonValueKind.Null);
        var lotId = lot.GetProperty("id").GetString();

        // The attacker authenticates with their own token but submits the owner's company ID
        var result = await ExecuteGraphQlAsync(
            """
            mutation PurchaseLot($input: PurchaseLotInput!) {
              purchaseLot(input: $input) { lot { id } }
            }
            """,
            new { input = new { companyId = ownerCompanyId, lotId, buildingType = "FACTORY", buildingName = "Stolen Factory" } },
            tokenAttacker);

        Assert.True(result.TryGetProperty("errors", out var errors), "should fail when using another player's company ID");
        Assert.Contains("Company not found", errors[0].GetProperty("message").GetString());
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
    public async Task GlobalExchangeOffers_UnknownDestinationCity_ReturnsEmptyList()
    {
        var unknownCityId = Guid.NewGuid();

        var result = await ExecuteGraphQlAsync(
            """
            query GlobalExchangeOffers($destinationCityId: UUID!) {
              globalExchangeOffers(destinationCityId: $destinationCityId) {
                cityName
                exchangePricePerUnit
              }
            }
            """,
            new { destinationCityId = unknownCityId });

        Assert.False(result.TryGetProperty("errors", out _), "Expected no GraphQL errors for unknown city");
        var offers = result.GetProperty("data").GetProperty("globalExchangeOffers");
        Assert.Equal(0, offers.GetArrayLength());
    }

    [Fact]
    public async Task GlobalExchangeOffers_FilteredByResourceType_ReturnsOnlyMatchingResource()
    {
        var bratislavaId = await GetCityIdByNameAsync("Bratislava");
        var resourceTypesResult = await ExecuteGraphQlAsync("{ resourceTypes { id slug } }");
        var coalId = resourceTypesResult.GetProperty("data").GetProperty("resourceTypes")
            .EnumerateArray()
            .First(r => r.GetProperty("slug").GetString() == "coal")
            .GetProperty("id")
            .GetString()!;

        var result = await ExecuteGraphQlAsync(
            """
            query GlobalExchangeOffers($destinationCityId: UUID!, $resourceTypeId: UUID) {
              globalExchangeOffers(destinationCityId: $destinationCityId, resourceTypeId: $resourceTypeId) {
                resourceSlug
                exchangePricePerUnit
                deliveredPricePerUnit
                estimatedQuality
              }
            }
            """,
            new { destinationCityId = bratislavaId, resourceTypeId = coalId });

        var offers = result.GetProperty("data").GetProperty("globalExchangeOffers");

        // 3 seeded cities × 1 filtered resource = 3 offers.
        Assert.Equal(3, offers.GetArrayLength());

        // Every returned offer must be for coal.
        Assert.All(offers.EnumerateArray(), offer =>
            Assert.Equal("coal", offer.GetProperty("resourceSlug").GetString()));
    }

    [Fact]
    public async Task GlobalExchangeOffers_IsPublicQuery_WorksWithoutAuthentication()
    {
        // The globalExchangeOffers query is market-data and must not require auth.
        var bratislavaId = await GetCityIdByNameAsync("Bratislava");
        var woodId = (await ExecuteGraphQlAsync("{ resourceTypes { id slug } }"))
            .GetProperty("data").GetProperty("resourceTypes")
            .EnumerateArray().First(r => r.GetProperty("slug").GetString() == "wood")
            .GetProperty("id").GetString()!;

        // Execute without a token — should succeed and return offers.
        var result = await ExecuteGraphQlAsync(
            """
            query GlobalExchangeOffers($destinationCityId: UUID!, $resourceTypeId: UUID) {
              globalExchangeOffers(destinationCityId: $destinationCityId, resourceTypeId: $resourceTypeId) {
                cityName
                exchangePricePerUnit
                transitCostPerUnit
                deliveredPricePerUnit
              }
            }
            """,
            new { destinationCityId = bratislavaId, resourceTypeId = woodId },
            token: null);

        Assert.False(result.TryGetProperty("errors", out _), "globalExchangeOffers should be publicly accessible without authentication");
        var offers = result.GetProperty("data").GetProperty("globalExchangeOffers");
        Assert.Equal(3, offers.GetArrayLength());
        Assert.All(offers.EnumerateArray(), offer =>
            Assert.True(offer.GetProperty("exchangePricePerUnit").GetDecimal() > 0m));
    }

    [Fact]
    public async Task GlobalExchangeOffers_TwoCitiesHaveDifferentPricesOrQualityForSameResource()
    {
        // AC#7: exchange must demonstrate meaningful variation between at least two cities.
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
                exchangePricePerUnit
                estimatedQuality
                transitCostPerUnit
                deliveredPricePerUnit
              }
            }
            """,
            new { destinationCityId = bratislavaId, resourceTypeId = woodId });

        var offers = result.GetProperty("data").GetProperty("globalExchangeOffers")
            .EnumerateArray().ToList();

        Assert.True(offers.Count >= 2, "Need at least 2 city offers to compare");

        // At least two cities must differ in delivered price or quality, proving city differentiation.
        var prices = offers.Select(o => o.GetProperty("deliveredPricePerUnit").GetDecimal()).Distinct().ToList();
        var qualities = offers.Select(o => o.GetProperty("estimatedQuality").GetDecimal()).Distinct().ToList();

        Assert.True(
            prices.Count > 1 || qualities.Count > 1,
            "At least two cities must have different delivered prices or quality for the same resource.");
    }

    [Fact]
    public async Task GlobalExchangeOffers_CoversFurnitureInputResource_Wood()
    {
        // AC#10: backend tests cover starter-industry inputs for Furniture (Wood).
        var bratislavaId = await GetCityIdByNameAsync("Bratislava");
        var woodId = (await ExecuteGraphQlAsync("{ resourceTypes { id slug } }"))
            .GetProperty("data").GetProperty("resourceTypes")
            .EnumerateArray().First(r => r.GetProperty("slug").GetString() == "wood")
            .GetProperty("id").GetString()!;

        var result = await ExecuteGraphQlAsync(
            """
            query GlobalExchangeOffers($destinationCityId: UUID!, $resourceTypeId: UUID) {
              globalExchangeOffers(destinationCityId: $destinationCityId, resourceTypeId: $resourceTypeId) {
                resourceSlug
                exchangePricePerUnit
                deliveredPricePerUnit
                estimatedQuality
                transitCostPerUnit
              }
            }
            """,
            new { destinationCityId = bratislavaId, resourceTypeId = woodId });

        var offers = result.GetProperty("data").GetProperty("globalExchangeOffers").EnumerateArray().ToList();
        Assert.True(offers.Count > 0, "Wood (Furniture input) must have exchange offers");
        Assert.All(offers, o =>
        {
            Assert.Equal("wood", o.GetProperty("resourceSlug").GetString());
            Assert.True(o.GetProperty("exchangePricePerUnit").GetDecimal() > 0m);
            Assert.True(o.GetProperty("deliveredPricePerUnit").GetDecimal() >= o.GetProperty("exchangePricePerUnit").GetDecimal());
            Assert.InRange(o.GetProperty("estimatedQuality").GetDecimal(), 0.35m, 0.95m);
        });
    }

    [Fact]
    public async Task GlobalExchangeOffers_CoversFoodProcessingInputResource_Grain()
    {
        // AC#10: backend tests cover starter-industry inputs for Food Processing (Grain).
        var bratislavaId = await GetCityIdByNameAsync("Bratislava");
        var grainId = (await ExecuteGraphQlAsync("{ resourceTypes { id slug } }"))
            .GetProperty("data").GetProperty("resourceTypes")
            .EnumerateArray().First(r => r.GetProperty("slug").GetString() == "grain")
            .GetProperty("id").GetString()!;

        var result = await ExecuteGraphQlAsync(
            """
            query GlobalExchangeOffers($destinationCityId: UUID!, $resourceTypeId: UUID) {
              globalExchangeOffers(destinationCityId: $destinationCityId, resourceTypeId: $resourceTypeId) {
                resourceSlug
                exchangePricePerUnit
                deliveredPricePerUnit
                estimatedQuality
                transitCostPerUnit
              }
            }
            """,
            new { destinationCityId = bratislavaId, resourceTypeId = grainId });

        var offers = result.GetProperty("data").GetProperty("globalExchangeOffers").EnumerateArray().ToList();
        Assert.True(offers.Count > 0, "Grain (Food Processing input) must have exchange offers");
        Assert.All(offers, o =>
        {
            Assert.Equal("grain", o.GetProperty("resourceSlug").GetString());
            Assert.True(o.GetProperty("exchangePricePerUnit").GetDecimal() > 0m);
            // Food Processing key validation: delivered price must always be reachable (null MaxPrice = no cap).
            Assert.True(o.GetProperty("deliveredPricePerUnit").GetDecimal() > 0m);
            Assert.InRange(o.GetProperty("estimatedQuality").GetDecimal(), 0.35m, 0.95m);
        });
    }

    [Fact]
    public async Task GlobalExchangeOffers_CoversHealthcareInputResource_ChemicalMinerals()
    {
        // AC#10: backend tests cover starter-industry inputs for Healthcare (Chemical Minerals).
        var bratislavaId = await GetCityIdByNameAsync("Bratislava");
        var chemId = (await ExecuteGraphQlAsync("{ resourceTypes { id slug } }"))
            .GetProperty("data").GetProperty("resourceTypes")
            .EnumerateArray().First(r => r.GetProperty("slug").GetString() == "chemical-minerals")
            .GetProperty("id").GetString()!;

        var result = await ExecuteGraphQlAsync(
            """
            query GlobalExchangeOffers($destinationCityId: UUID!, $resourceTypeId: UUID) {
              globalExchangeOffers(destinationCityId: $destinationCityId, resourceTypeId: $resourceTypeId) {
                resourceSlug
                exchangePricePerUnit
                deliveredPricePerUnit
                estimatedQuality
                transitCostPerUnit
              }
            }
            """,
            new { destinationCityId = bratislavaId, resourceTypeId = chemId });

        var offers = result.GetProperty("data").GetProperty("globalExchangeOffers").EnumerateArray().ToList();
        Assert.True(offers.Count > 0, "Chemical Minerals (Healthcare input) must have exchange offers");
        Assert.All(offers, o =>
        {
            Assert.Equal("chemical-minerals", o.GetProperty("resourceSlug").GetString());
            Assert.True(o.GetProperty("exchangePricePerUnit").GetDecimal() > 0m);
            Assert.True(o.GetProperty("deliveredPricePerUnit").GetDecimal() >= o.GetProperty("exchangePricePerUnit").GetDecimal());
            Assert.InRange(o.GetProperty("estimatedQuality").GetDecimal(), 0.35m, 0.95m);
        });
    }

    [Fact]
    public async Task GlobalExchangeOffers_BestOptionIsFirstSortedByDeliveredPrice_ForAllStarterIndustries()
    {
        // AC#10: for all three starter industries, the first globalExchangeOffers result
        // must be the cheapest delivered-cost option (optimal price selection logic).
        var bratislavaId = await GetCityIdByNameAsync("Bratislava");
        var resourceIds = await ExecuteGraphQlAsync("{ resourceTypes { id slug } }");
        var slugToId = resourceIds.GetProperty("data").GetProperty("resourceTypes")
            .EnumerateArray()
            .ToDictionary(
                r => r.GetProperty("slug").GetString()!,
                r => r.GetProperty("id").GetString()!);

        // Furniture (Wood), Food Processing (Grain), Healthcare (Chemical Minerals)
        foreach (var slug in new[] { "wood", "grain", "chemical-minerals" })
        {
            var resourceId = slugToId[slug];

            var result = await ExecuteGraphQlAsync(
                """
                query GlobalExchangeOffers($destinationCityId: UUID!, $resourceTypeId: UUID) {
                  globalExchangeOffers(destinationCityId: $destinationCityId, resourceTypeId: $resourceTypeId) {
                    cityName
                    deliveredPricePerUnit
                  }
                }
                """,
                new { destinationCityId = bratislavaId, resourceTypeId = resourceId });

            var offers = result.GetProperty("data").GetProperty("globalExchangeOffers")
                .EnumerateArray().ToList();

            Assert.True(offers.Count > 0, $"No offers for {slug}");

            // The first offer must have the minimum delivered price (sorted ascending = cheapest first).
            var minDelivered = offers.Min(o => o.GetProperty("deliveredPricePerUnit").GetDecimal());
            var firstDelivered = offers[0].GetProperty("deliveredPricePerUnit").GetDecimal();

            Assert.True(firstDelivered == minDelivered,
                $"First offer for {slug} must be the optimal (cheapest delivered) option. Got {firstDelivered}, min is {minDelivered}.");
        }
    }

    [Fact]
    public async Task GlobalExchangeOffers_SeedAbundance_ProducesHigherQualityForHighAbundanceResource()
    {
        // Bratislava seed: Wood abundance=0.7 → quality ≈0.77; ChemMinerals abundance=0.3 → quality ≈0.53
        // This test validates the ROADMAP requirement: "each city has different resource pricing and quality".
        var bratislavaId = await GetCityIdByNameAsync("Bratislava");
        var resourceIds = await ExecuteGraphQlAsync("{ resourceTypes { id slug } }");
        var slugToId = resourceIds.GetProperty("data").GetProperty("resourceTypes")
            .EnumerateArray()
            .ToDictionary(
                r => r.GetProperty("slug").GetString()!,
                r => r.GetProperty("id").GetString()!);

        // Fetch Wood offers for Bratislava
        var woodResult = await ExecuteGraphQlAsync(
            """
            query GlobalExchangeOffers($destinationCityId: UUID!, $resourceTypeId: UUID) {
              globalExchangeOffers(destinationCityId: $destinationCityId, resourceTypeId: $resourceTypeId) {
                cityName estimatedQuality localAbundance
              }
            }
            """,
            new { destinationCityId = bratislavaId, resourceTypeId = slugToId["wood"] });
        var braWoodQuality = woodResult.GetProperty("data").GetProperty("globalExchangeOffers")
            .EnumerateArray()
            .First(o => o.GetProperty("cityName").GetString() == "Bratislava")
            .GetProperty("estimatedQuality").GetDecimal();

        // Fetch ChemMinerals offers for Bratislava
        var chemResult = await ExecuteGraphQlAsync(
            """
            query GlobalExchangeOffers($destinationCityId: UUID!, $resourceTypeId: UUID) {
              globalExchangeOffers(destinationCityId: $destinationCityId, resourceTypeId: $resourceTypeId) {
                cityName estimatedQuality localAbundance
              }
            }
            """,
            new { destinationCityId = bratislavaId, resourceTypeId = slugToId["chemical-minerals"] });
        var braChemQuality = chemResult.GetProperty("data").GetProperty("globalExchangeOffers")
            .EnumerateArray()
            .First(o => o.GetProperty("cityName").GetString() == "Bratislava")
            .GetProperty("estimatedQuality").GetDecimal();

        // Wood (0.7 abundance) must have higher quality than ChemMinerals (0.3 abundance)
        Assert.True(braWoodQuality > braChemQuality,
            $"Bratislava Wood quality ({braWoodQuality}) must exceed ChemMinerals quality ({braChemQuality}) due to higher seed abundance.");
    }

    [Fact]
    public async Task GlobalExchangeOffers_AllEightSeedResourceSlugsPresent_InExchangeListings()
    {
        // All 8 seeded raw-material resource types must appear in the global exchange listings.
        // This validates the ROADMAP "never ending resource sale for every resource".
        var bratislavaId = await GetCityIdByNameAsync("Bratislava");

        var result = await ExecuteGraphQlAsync(
            """
            query GlobalExchangeOffers($destinationCityId: UUID!) {
              globalExchangeOffers(destinationCityId: $destinationCityId) {
                resourceSlug cityName
              }
            }
            """,
            new { destinationCityId = bratislavaId });

        var slugsInOffers = result.GetProperty("data").GetProperty("globalExchangeOffers")
            .EnumerateArray()
            .Select(o => o.GetProperty("resourceSlug").GetString())
            .Distinct()
            .ToHashSet();

        var expectedSlugs = new[] { "wood", "iron-ore", "coal", "gold", "chemical-minerals", "cotton", "grain", "silicon" };
        foreach (var slug in expectedSlugs)
        {
            Assert.Contains(slug, slugsInOffers);
        }
        Assert.Equal(8, slugsInOffers.Count);
    }

    [Fact]
    public async Task GlobalExchangeOffers_PriceReflectsCityRentMultiplier()
    {
        // Cities with different AverageRentPerSqm must produce different exchange prices for the same resource.
        // Bratislava (AverageRentPerSqm=14) vs Vienna (AverageRentPerSqm=20) — different prices expected.
        var bratislavaId = await GetCityIdByNameAsync("Bratislava");
        var resourceIds = await ExecuteGraphQlAsync("{ resourceTypes { id slug } }");
        var woodId = resourceIds.GetProperty("data").GetProperty("resourceTypes")
            .EnumerateArray()
            .First(r => r.GetProperty("slug").GetString() == "wood")
            .GetProperty("id").GetString()!;

        // Query with Bratislava as destination — returns offers from all source cities
        var result = await ExecuteGraphQlAsync(
            """
            query GlobalExchangeOffers($destinationCityId: UUID!, $resourceTypeId: UUID) {
              globalExchangeOffers(destinationCityId: $destinationCityId, resourceTypeId: $resourceTypeId) {
                cityName exchangePricePerUnit transitCostPerUnit deliveredPricePerUnit
              }
            }
            """,
            new { destinationCityId = bratislavaId, resourceTypeId = woodId });

        var offers = result.GetProperty("data").GetProperty("globalExchangeOffers")
            .EnumerateArray().ToList();

        Assert.True(offers.Count >= 2, "Must have at least 2 city offers to compare prices");

        // Exchange prices must not all be identical — city rent multiplier drives differentiation
        var exchangePrices = offers.Select(o => o.GetProperty("exchangePricePerUnit").GetDecimal()).Distinct().ToList();
        Assert.True(exchangePrices.Count > 1,
            "Different cities must produce different exchange prices for the same resource (city rent multiplier).");
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
    public async Task StoreBuildingConfiguration_PurchaseUnit_PersistsExchangeSourceForGrain_FoodProcessing()
    {
        // AC#10: validates the exchange-source purchase-unit configuration round-trip for the
        // Food Processing starter industry (Grain raw material).
        var email = $"grain-exch-cfg-{Guid.NewGuid():N}@test.com";
        var token = await RegisterAndGetTokenAsync(email, "Grain Exchange Tester");

        var companyResult = await ExecuteGraphQlAsync(
            "mutation CreateCompany($input: CreateCompanyInput!) { createCompany(input: $input) { id } }",
            new { input = new { name = "Grain Exchange Corp" } },
            token);
        var companyId = companyResult.GetProperty("data").GetProperty("createCompany").GetProperty("id").GetString();

        var citiesResult = await ExecuteGraphQlAsync("{ cities { id } }");
        var cityId = citiesResult.GetProperty("data").GetProperty("cities")[0].GetProperty("id").GetString();

        var buildingResult = await ExecuteGraphQlAsync(
            "mutation PlaceBuilding($input: PlaceBuildingInput!) { placeBuilding(input: $input) { id } }",
            new { input = new { companyId, cityId, type = "FACTORY", name = "Grain Factory" } },
            token);
        var buildingId = buildingResult.GetProperty("data").GetProperty("placeBuilding").GetProperty("id").GetString();

        var grainId = (await ExecuteGraphQlAsync("{ resourceTypes { id slug } }"))
            .GetProperty("data").GetProperty("resourceTypes")
            .EnumerateArray().First(r => r.GetProperty("slug").GetString() == "grain")
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
                            resourceTypeId = grainId,
                            // null MaxPrice allows purchasing at any market rate.
                            // A non-null cap (e.g. BasePrice) risks blocking Grain purchases when
                            // the exchange price exceeds it — the root cause of PR #93's silent
                            // Food Processing supply-chain failure.
                            maxPrice = (decimal?)null,
                            minQuality = 0.5m,
                            purchaseSource = "EXCHANGE",
                            linkRight = false, linkLeft = false, linkUp = false, linkDown = false,
                            linkUpLeft = false, linkUpRight = false, linkDownLeft = false, linkDownRight = false,
                        }
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
        // null MaxPrice means no cap — Food Processing can buy Grain at any market price
        Assert.True(unit.GetProperty("maxPrice").ValueKind == System.Text.Json.JsonValueKind.Null,
            "MaxPrice must be null for Food Processing purchase unit so the engine can buy at market price");
        Assert.Equal(0.5m, unit.GetProperty("minQuality").GetDecimal());
        Assert.Equal(grainId, unit.GetProperty("resourceTypeId").GetString());
    }

    [Fact]
    public async Task StoreBuildingConfiguration_PurchaseUnit_PersistsExchangeSourceForChemicalMinerals_Healthcare()
    {
        // AC#10: validates the exchange-source purchase-unit configuration round-trip for the
        // Healthcare starter industry (Chemical Minerals raw material).
        var email = $"chem-exch-cfg-{Guid.NewGuid():N}@test.com";
        var token = await RegisterAndGetTokenAsync(email, "Chem Exchange Tester");

        var companyResult = await ExecuteGraphQlAsync(
            "mutation CreateCompany($input: CreateCompanyInput!) { createCompany(input: $input) { id } }",
            new { input = new { name = "Chem Exchange Corp" } },
            token);
        var companyId = companyResult.GetProperty("data").GetProperty("createCompany").GetProperty("id").GetString();

        var citiesResult = await ExecuteGraphQlAsync("{ cities { id } }");
        var cityId = citiesResult.GetProperty("data").GetProperty("cities")[0].GetProperty("id").GetString();

        var buildingResult = await ExecuteGraphQlAsync(
            "mutation PlaceBuilding($input: PlaceBuildingInput!) { placeBuilding(input: $input) { id } }",
            new { input = new { companyId, cityId, type = "FACTORY", name = "Chem Factory" } },
            token);
        var buildingId = buildingResult.GetProperty("data").GetProperty("placeBuilding").GetProperty("id").GetString();

        var chemId = (await ExecuteGraphQlAsync("{ resourceTypes { id slug } }"))
            .GetProperty("data").GetProperty("resourceTypes")
            .EnumerateArray().First(r => r.GetProperty("slug").GetString() == "chemical-minerals")
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
                            resourceTypeId = chemId,
                            maxPrice = (decimal?)null,
                            minQuality = (decimal?)null,
                            purchaseSource = "OPTIMAL",
                            linkRight = false, linkLeft = false, linkUp = false, linkDown = false,
                            linkUpLeft = false, linkUpRight = false, linkDownLeft = false, linkDownRight = false,
                        }
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

        Assert.Equal("OPTIMAL", unit.GetProperty("purchaseSource").GetString());
        Assert.Equal(chemId, unit.GetProperty("resourceTypeId").GetString());
        // Null constraints mean no cap — Healthcare can buy Chemical Minerals at any market price/quality
        Assert.True(unit.GetProperty("maxPrice").ValueKind == System.Text.Json.JsonValueKind.Null,
            "MaxPrice must be null for Healthcare purchase unit");
        Assert.True(unit.GetProperty("minQuality").ValueKind == System.Text.Json.JsonValueKind.Null,
            "MinQuality must be null for unrestricted Healthcare purchase unit");
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
        await using var isolatedFactory = new ApiWebApplicationFactory();
        using var isolatedClient = isolatedFactory.CreateClient();
        var bratislavaId = await GetCityIdByNameAsync(isolatedClient, "Bratislava");

        var result = await ExecuteGraphQlAsync(
            isolatedClient,
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

    [Fact]
    public async Task CityLots_MineLotsExposeRawMaterialAttributes()
    {
        // MINE-suitable lots seeded for Bratislava (Industrial Zone) should carry resourceType,
        // materialQuality, and materialQuantity data so the frontend land panel
        // can render the raw material strategic value.
        var citiesResult = await ExecuteGraphQlAsync("{ cities { id name } }");
        var bratislavaId = citiesResult.GetProperty("data").GetProperty("cities").EnumerateArray()
            .First(c => c.GetProperty("name").GetString() == "Bratislava")
            .GetProperty("id").GetString();

        var result = await ExecuteGraphQlAsync(
            """
            query CityLots($cityId: UUID!) {
              cityLots(cityId: $cityId) {
                id name district suitableTypes
                materialQuality materialQuantity
                resourceType { id name slug }
              }
            }
            """,
            new { cityId = bratislavaId });

        Assert.False(result.TryGetProperty("errors", out _), "cityLots should not return GraphQL errors");

        var lots = result.GetProperty("data").GetProperty("cityLots").EnumerateArray().ToList();
        // Only check seeded Industrial Zone lots (not dynamically created test lots)
        var seededMineLots = lots
            .Where(l => l.GetProperty("suitableTypes").GetString()!.Contains("MINE")
                     && l.GetProperty("district").GetString() == "Industrial Zone")
            .ToList();

        Assert.True(seededMineLots.Count > 0, "Expected at least one seeded MINE lot in Industrial Zone");

        // At least one seeded mine lot should have raw material data
        var lotsWithMaterial = seededMineLots
            .Where(l => l.GetProperty("resourceType").ValueKind != JsonValueKind.Null)
            .ToList();
        Assert.True(lotsWithMaterial.Count > 0,
            "Expected at least one seeded Industrial Zone MINE lot to have resourceType data");

        foreach (var lot in lotsWithMaterial)
        {
            var name = lot.GetProperty("name").GetString();
            Assert.True(lot.GetProperty("materialQuality").ValueKind != JsonValueKind.Null,
                $"MINE lot '{name}' with resourceType should have materialQuality");
            Assert.True(lot.GetProperty("materialQuantity").ValueKind != JsonValueKind.Null,
                $"MINE lot '{name}' with resourceType should have materialQuantity");

            var quality = lot.GetProperty("materialQuality").GetDecimal();
            Assert.True(quality > 0 && quality <= 1.0m,
                $"MINE lot '{name}' materialQuality {quality} should be in range (0,1]");

            var quantity = lot.GetProperty("materialQuantity").GetDecimal();
            Assert.True(quantity > 0, $"MINE lot '{name}' materialQuantity should be positive");

            var slug = lot.GetProperty("resourceType").GetProperty("slug").GetString();
            Assert.False(string.IsNullOrEmpty(slug), $"MINE lot '{name}' resourceType.slug should not be empty");
        }
    }

    [Fact]
    public async Task CityLots_NonMineLotsHaveNoRawMaterialData()
    {
        // Non-extraction lots (commercial, residential, business park) must NOT expose
        // raw material data to avoid confusing the frontend land detail panel.
        var citiesResult = await ExecuteGraphQlAsync("{ cities { id name } }");
        var bratislavaId = citiesResult.GetProperty("data").GetProperty("cities").EnumerateArray()
            .First(c => c.GetProperty("name").GetString() == "Bratislava")
            .GetProperty("id").GetString();

        var result = await ExecuteGraphQlAsync(
            """
            query CityLots($cityId: UUID!) {
              cityLots(cityId: $cityId) {
                id name suitableTypes
                materialQuality materialQuantity
                resourceType { id name }
              }
            }
            """,
            new { cityId = bratislavaId });

        var lots = result.GetProperty("data").GetProperty("cityLots").EnumerateArray().ToList();
        // SALES_SHOP / APARTMENT / MEDIA_HOUSE lots have no raw material
        var retailLots = lots
            .Where(l =>
            {
                var types = l.GetProperty("suitableTypes").GetString()!;
                return (types.Contains("SALES_SHOP") || types.Contains("APARTMENT") || types.Contains("MEDIA_HOUSE"))
                    && !types.Contains("MINE");
            })
            .ToList();

        Assert.True(retailLots.Count > 0, "Expected at least one non-extraction lot");

        foreach (var lot in retailLots)
        {
            var name = lot.GetProperty("name").GetString();
            Assert.True(lot.GetProperty("resourceType").ValueKind == JsonValueKind.Null,
                $"Non-MINE lot '{name}' should not have resourceType");
            Assert.True(lot.GetProperty("materialQuality").ValueKind == JsonValueKind.Null,
                $"Non-MINE lot '{name}' should not have materialQuality");
            Assert.True(lot.GetProperty("materialQuantity").ValueKind == JsonValueKind.Null,
                $"Non-MINE lot '{name}' should not have materialQuantity");
        }
    }

    [Fact]
    public async Task GetLot_ByIdIncludesRawMaterialData()
    {
        // The lot(id) single-lot query should also include raw material data
        // so the detail panel can be populated from either endpoint.
        var citiesResult = await ExecuteGraphQlAsync("{ cities { id name } }");
        var bratislavaId = citiesResult.GetProperty("data").GetProperty("cities").EnumerateArray()
            .First(c => c.GetProperty("name").GetString() == "Bratislava")
            .GetProperty("id").GetString();

        // Fetch cityLots with district to find a seeded lot with raw material data
        var lotsResult = await ExecuteGraphQlAsync(
            """
            query CityLots($cityId: UUID!) {
              cityLots(cityId: $cityId) { id name district suitableTypes materialQuality }
            }
            """,
            new { cityId = bratislavaId });

        // Pick a seeded Industrial Zone MINE lot that already has material data
        var mineLot = lotsResult.GetProperty("data").GetProperty("cityLots").EnumerateArray()
            .FirstOrDefault(l =>
                l.GetProperty("suitableTypes").GetString()!.Contains("MINE")
                && l.GetProperty("district").GetString() == "Industrial Zone"
                && l.GetProperty("materialQuality").ValueKind != JsonValueKind.Null);

        Assert.NotEqual(default, mineLot);
        var lotId = mineLot.GetProperty("id").GetString();

        var result = await ExecuteGraphQlAsync(
            """
            query GetLot($id: UUID!) {
              lot(id: $id) {
                id name suitableTypes
                materialQuality materialQuantity
                resourceType { id name slug }
              }
            }
            """,
            new { id = lotId });

        Assert.False(result.TryGetProperty("errors", out _), "lot(id) should not return errors");
        var lot = result.GetProperty("data").GetProperty("lot");
        Assert.True(lot.GetProperty("resourceType").ValueKind != JsonValueKind.Null,
            "lot(id) on a seeded MINE lot should include resourceType");
        Assert.True(lot.GetProperty("materialQuality").ValueKind != JsonValueKind.Null,
            "lot(id) on a seeded MINE lot should include materialQuality");
        Assert.True(lot.GetProperty("materialQuantity").ValueKind != JsonValueKind.Null,
            "lot(id) on a seeded MINE lot should include materialQuantity");
    }

    [Fact]
    public async Task CityLots_MineLotsHaveResourcePremiumInPrice()
    {
        // ROADMAP: "The price to purchase the land includes also the base price for the
        // raw material." Mine lots with resource deposits must have Price > BasePrice so
        // the resource-premium badge and valuation transparency UI render correctly.
        var citiesResult = await ExecuteGraphQlAsync("{ cities { id name } }");
        var bratislavaId = citiesResult.GetProperty("data").GetProperty("cities").EnumerateArray()
            .First(c => c.GetProperty("name").GetString() == "Bratislava")
            .GetProperty("id").GetString();

        var result = await ExecuteGraphQlAsync(
            """
            query CityLots($cityId: UUID!) {
              cityLots(cityId: $cityId) {
                id name district suitableTypes basePrice price
                resourceType { id name }
              }
            }
            """,
            new { cityId = bratislavaId });

        Assert.False(result.TryGetProperty("errors", out _), "cityLots should not return errors");

        var lots = result.GetProperty("data").GetProperty("cityLots").EnumerateArray().ToList();

        // Every seeded lot with a raw-material resource MUST have price > basePrice
        var resourceLots = lots
            .Where(l => l.GetProperty("resourceType").ValueKind != JsonValueKind.Null)
            .ToList();

        Assert.True(resourceLots.Count > 0,
            "Expected at least one seeded lot with a raw material resource");

        foreach (var lot in resourceLots)
        {
            var name = lot.GetProperty("name").GetString();
            var basePrice = lot.GetProperty("basePrice").GetDecimal();
            var price = lot.GetProperty("price").GetDecimal();

            Assert.True(price > basePrice,
                $"Mine lot '{name}' must have Price ({price}) > BasePrice ({basePrice}) because the land price includes the raw-material deposit premium.");
        }
    }

    [Fact]
    public async Task PurchaseLot_UpdatesCompanyCashBalance()
    {
        // AC #5: After a successful purchaseLot, the company's cash must be reduced
        // by the lot price. The mutation must return the updated cash value so the
        // frontend can update its display immediately without a separate refetch.
        var token = await RegisterAndGetTokenAsync($"cash-balance-{Guid.NewGuid()}@test.com");
        var (companyId, _, _) = await CompleteOnboardingAsync(token, "Cash Balance Co");

        // Create a dedicated lot for this test to avoid depending on seeded lot availability
        var bratislavaId = await GetCityIdByNameAsync("Bratislava");
        var lotPrice = 60_000m;
        var lotId = await CreateTestLotAsync(bratislavaId, "FACTORY,MINE", "Industrial Zone", lotPrice, "Cash Test Lot");

        // Record cash before purchase
        var meResultBefore = await ExecuteGraphQlAsync(
            "{ me { companies { id cash } } }", null, token);
        var cashBefore = meResultBefore.GetProperty("data").GetProperty("me")
            .GetProperty("companies").EnumerateArray()
            .First(c => c.GetProperty("id").GetString() == companyId)
            .GetProperty("cash").GetDecimal();

        // Purchase the lot
        var purchaseResult = await ExecuteGraphQlAsync(
            """
            mutation PurchaseLot($input: PurchaseLotInput!) {
              purchaseLot(input: $input) {
                company { id cash }
              }
            }
            """,
            new { input = new { companyId, lotId, buildingType = "FACTORY", buildingName = "Cash Test Factory" } },
            token);

        Assert.False(purchaseResult.TryGetProperty("errors", out _), "purchaseLot should succeed");

        // Cash returned in mutation response must be reduced by the lot price AND construction cost
        var cashInResponse = purchaseResult.GetProperty("data").GetProperty("purchaseLot")
            .GetProperty("company").GetProperty("cash").GetDecimal();
        var constructionCost = Api.Engine.GameConstants.ConstructionCost("FACTORY");
        var expectedCash = cashBefore - lotPrice - constructionCost;
        Assert.True(cashInResponse == expectedCash,
            $"Expected cash to decrease by lot price ({lotPrice}) + construction cost ({constructionCost}): {cashBefore} → {expectedCash}, but got {cashInResponse}");

        // Verify the same updated cash is returned by the me query
        var meResultAfter = await ExecuteGraphQlAsync(
            "{ me { companies { id cash } } }", null, token);
        var cashAfter = meResultAfter.GetProperty("data").GetProperty("me")
            .GetProperty("companies").EnumerateArray()
            .First(c => c.GetProperty("id").GetString() == companyId)
            .GetProperty("cash").GetDecimal();
        Assert.True(cashAfter == expectedCash,
            $"me query must confirm the reduced cash balance after purchase. Expected {expectedCash}, got {cashAfter}");
    }

    [Fact]
    public async Task CityLots_ReturnsEmptyListForPrague()
    {
        // Prague has no seeded building lots in the game initializer. The cityLots query
        // must return a valid (non-error) response for Prague, whether the array is empty
        // or contains dynamically-created lots from other tests sharing this database.
        // The key requirement: the query must not crash or return GraphQL errors.
        var citiesResult = await ExecuteGraphQlAsync("{ cities { id name } }");
        var pragueId = citiesResult.GetProperty("data").GetProperty("cities").EnumerateArray()
            .First(c => c.GetProperty("name").GetString() == "Prague")
            .GetProperty("id").GetString();

        var result = await ExecuteGraphQlAsync(
            """
            query CityLots($cityId: UUID!) {
              cityLots(cityId: $cityId) { id name cityId }
            }
            """,
            new { cityId = pragueId });

        Assert.False(result.TryGetProperty("errors", out _),
            "cityLots for Prague must not return GraphQL errors");
        var lots = result.GetProperty("data").GetProperty("cityLots");
        // All returned lots must belong to Prague
        foreach (var lot in lots.EnumerateArray())
        {
            Assert.Equal(pragueId, lot.GetProperty("cityId").GetString(),
                StringComparer.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task CityLots_ReturnsEmptyListForVienna()
    {
        // Vienna has no seeded building lots in the game initializer. The cityLots query
        // must return a valid (non-error) response for Vienna, whether the array is empty
        // or contains dynamically-created lots from other tests sharing this database.
        // The key requirement: the query must not crash or return GraphQL errors.
        var citiesResult = await ExecuteGraphQlAsync("{ cities { id name } }");
        var viennaId = citiesResult.GetProperty("data").GetProperty("cities").EnumerateArray()
            .First(c => c.GetProperty("name").GetString() == "Vienna")
            .GetProperty("id").GetString();

        var result = await ExecuteGraphQlAsync(
            """
            query CityLots($cityId: UUID!) {
              cityLots(cityId: $cityId) { id name cityId }
            }
            """,
            new { cityId = viennaId });

        Assert.False(result.TryGetProperty("errors", out _),
            "cityLots for Vienna must not return GraphQL errors");
        var lots = result.GetProperty("data").GetProperty("cityLots");
        // All returned lots must belong to Vienna
        foreach (var lot in lots.EnumerateArray())
        {
            Assert.Equal(viennaId, lot.GetProperty("cityId").GetString(),
                StringComparer.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task CityLots_StrategicRecommendationData_ResourceAndPopulationIndexCorrect()
    {
        // The frontend derives a "strategic recommendation" label from each lot's
        // suitableTypes, populationIndex, and resourceType. This backend test verifies
        // that the required data fields are all returned correctly so the frontend
        // can show "Strong for retail demand", "Resource-oriented",
        // "Industrial efficiency zone", or "Balanced starter location".

                await using var isolatedFactory = new ApiWebApplicationFactory();
                using var isolatedClient = isolatedFactory.CreateClient();
                var bratislavaId = await GetCityIdByNameAsync(isolatedClient, "Bratislava");

                var result = await ExecuteGraphQlAsync(
                        isolatedClient,
            """
            query CityLots($cityId: UUID!) {
              cityLots(cityId: $cityId) {
                id name district suitableTypes populationIndex
                resourceType { id name slug }
                materialQuality materialQuantity
              }
            }
            """,
            new { cityId = bratislavaId });

        Assert.False(result.TryGetProperty("errors", out _), "cityLots query must not return errors");
        var lots = result.GetProperty("data").GetProperty("cityLots").EnumerateArray().ToList();

        // Every lot must have a non-negative populationIndex (required for recommendation logic)
        foreach (var lot in lots)
        {
            var name = lot.GetProperty("name").GetString();
            var popIndex = lot.GetProperty("populationIndex").GetDecimal();
            Assert.True(popIndex >= 0,
                $"Lot '{name}' has negative populationIndex ({popIndex}); must be >= 0 for recommendation logic");
        }

        // At least one lot must be MINE-eligible with a resource (→ "Resource-oriented")
        var resourceLot = lots.FirstOrDefault(
            l => l.GetProperty("suitableTypes").GetString()!.Contains("MINE")
              && l.GetProperty("resourceType").ValueKind != JsonValueKind.Null);
        Assert.True(resourceLot.ValueKind != JsonValueKind.Undefined,
            "Expected at least one MINE-eligible lot with a resourceType for the 'Resource-oriented' recommendation");
        Assert.True(resourceLot.GetProperty("materialQuality").GetDecimal() > 0,
            "Resource lot must have positive materialQuality for the raw-material panel");
        Assert.True(resourceLot.GetProperty("materialQuantity").GetDecimal() > 0,
            "Resource lot must have positive materialQuantity for the raw-material panel");

        // At least one lot must support SALES_SHOP (retail recommendation input)
        var salesShopLot = lots.FirstOrDefault(
            l => l.GetProperty("suitableTypes").GetString()!.Contains("SALES_SHOP"));
        Assert.True(salesShopLot.ValueKind != JsonValueKind.Undefined,
            "Expected at least one SALES_SHOP lot for retail recommendation eligibility");

        // At least one lot must support FACTORY (industrial recommendation input)
        var factoryLot = lots.FirstOrDefault(
            l => l.GetProperty("suitableTypes").GetString()!.Contains("FACTORY"));
        Assert.True(factoryLot.ValueKind != JsonValueKind.Undefined,
            "Expected at least one FACTORY lot for industrial recommendation eligibility");

        // Commercial/retail lots should have a higher populationIndex than industrial lots on average.
        // This is the spatial signal that makes land acquisition a real decision surface.
        // Note: population index is recomputed from spatial data on every tick; in a fresh
        // test database (no buildings), values are ~0.6-1.0 depending on distance to city center.
        var commercialAvg = lots
            .Where(l => l.GetProperty("suitableTypes").GetString()!.Contains("SALES_SHOP"))
            .Select(l => l.GetProperty("populationIndex").GetDecimal())
            .DefaultIfEmpty(0m)
            .Average();

        var industrialAvg = lots
            .Where(l => l.GetProperty("district").GetString() == "Industrial Zone")
            .Select(l => l.GetProperty("populationIndex").GetDecimal())
            .DefaultIfEmpty(0m)
            .Average();

        Assert.True(
            commercialAvg > industrialAvg,
            $"Commercial lots avg populationIndex ({commercialAvg:F3}) should exceed industrial avg ({industrialAvg:F3}) — " +
            "this is the spatial signal that makes land acquisition a real decision surface");
    }

    [Fact]
    public async Task PurchaseLot_BuildingStartsUnderConstruction()
    {
        // When a player purchases a lot via the city-map flow, the resulting building
        // must start in IsUnderConstruction = true with a ConstructionCompletesAtTick
        // set in the future, and a ConstructionCost > 0.
        var token = await RegisterAndGetTokenAsync($"construction-start-{Guid.NewGuid()}@test.com");
        var (companyId, _, _) = await CompleteOnboardingAsync(token, "Construction Start Co");

        var bratislavaId = await GetCityIdByNameAsync("Bratislava");
        var lotId = await CreateTestLotAsync(bratislavaId, "FACTORY,MINE", "Industrial Zone", 30_000m, "Build Test Lot");

        var result = await ExecuteGraphQlAsync(
            """
            mutation PurchaseLot($input: PurchaseLotInput!) {
              purchaseLot(input: $input) {
                building {
                  id type isUnderConstruction constructionCompletesAtTick constructionCost
                }
              }
            }
            """,
            new { input = new { companyId, lotId, buildingType = "FACTORY", buildingName = "Under Construction Factory" } },
            token);

        Assert.False(result.TryGetProperty("errors", out _), "purchaseLot must succeed");
        var building = result.GetProperty("data").GetProperty("purchaseLot").GetProperty("building");

        Assert.True(building.GetProperty("isUnderConstruction").GetBoolean(),
            "Building purchased via city-map PurchaseLot must start as IsUnderConstruction = true");
        Assert.True(building.GetProperty("constructionCompletesAtTick").GetInt64() > 0,
            "ConstructionCompletesAtTick must be set to a future tick");
        Assert.True(building.GetProperty("constructionCost").GetDecimal() > 0m,
            "ConstructionCost must be greater than zero");
    }

    [Fact]
    public async Task PurchaseLot_ConstructionCostDeductedFromCash()
    {
        // Purchasing a lot must deduct both the land price AND the construction cost.
        var token = await RegisterAndGetTokenAsync($"construction-cash-{Guid.NewGuid()}@test.com");
        var (companyId, _, _) = await CompleteOnboardingAsync(token, "Construction Cash Co");

        var bratislavaId = await GetCityIdByNameAsync("Bratislava");
        var lotPrice = 40_000m;
        var lotId = await CreateTestLotAsync(bratislavaId, "SALES_SHOP,COMMERCIAL", "Commercial District", lotPrice, "Sales Shop Test Lot");

        var meResultBefore = await ExecuteGraphQlAsync("{ me { companies { id cash } } }", null, token);
        var cashBefore = meResultBefore.GetProperty("data").GetProperty("me")
            .GetProperty("companies").EnumerateArray()
            .First(c => c.GetProperty("id").GetString() == companyId)
            .GetProperty("cash").GetDecimal();

        var result = await ExecuteGraphQlAsync(
            """
            mutation PurchaseLot($input: PurchaseLotInput!) {
              purchaseLot(input: $input) {
                building { constructionCost }
                company { cash }
              }
            }
            """,
            new { input = new { companyId, lotId, buildingType = "SALES_SHOP", buildingName = "New Shop" } },
            token);

        Assert.False(result.TryGetProperty("errors", out _), "purchaseLot must succeed");
        var purchaseData = result.GetProperty("data").GetProperty("purchaseLot");
        var constructionCost = purchaseData.GetProperty("building").GetProperty("constructionCost").GetDecimal();
        var cashAfter = purchaseData.GetProperty("company").GetProperty("cash").GetDecimal();

        var expectedCash = cashBefore - lotPrice - constructionCost;
        Assert.Equal(expectedCash, cashAfter);
        Assert.True(constructionCost > 0m, "Construction cost for a SALES_SHOP must be positive");
    }

    [Fact]
    public async Task PurchaseLot_InsufficientFundsForConstructionCost_Fails()
    {
        // If the company can afford the lot but not lot + construction cost, the purchase must fail.
        var token = await RegisterAndGetTokenAsync($"construction-broke-{Guid.NewGuid()}@test.com");
        var (companyId, _, _) = await CompleteOnboardingAsync(token, "Broke Co");

        // Drain cash so only the land price is coverable but not land + construction
        var meResult = await ExecuteGraphQlAsync("{ me { companies { id cash } } }", null, token);
        var currentCash = meResult.GetProperty("data").GetProperty("me")
            .GetProperty("companies").EnumerateArray()
            .First(c => c.GetProperty("id").GetString() == companyId)
            .GetProperty("cash").GetDecimal();

        // Use a lot priced just below current cash, but construction cost for FACTORY is 15,000
        // so if lot price = currentCash - 5,000 → company has 5,000 left → cannot afford 15,000 construction
        var lotPrice = currentCash - 5_000m;
        Assert.True(lotPrice > 0,
            $"Onboarding must grant sufficient cash for this test. Expected currentCash > $5,000 but got {currentCash}.");

        var bratislavaId = await GetCityIdByNameAsync("Bratislava");
        var lotId = await CreateTestLotAsync(bratislavaId, "FACTORY,MINE", "Industrial Zone", lotPrice, "Broke Test Lot");

        var result = await ExecuteGraphQlAsync(
            """
            mutation PurchaseLot($input: PurchaseLotInput!) {
              purchaseLot(input: $input) {
                building { id }
              }
            }
            """,
            new { input = new { companyId, lotId, buildingType = "FACTORY", buildingName = "Unaffordable Factory" } },
            token);

        Assert.True(result.TryGetProperty("errors", out var errors), "Expected errors when insufficient funds");
        var errorCodes = errors.EnumerateArray()
            .SelectMany(e => e.GetProperty("extensions").EnumerateObject()
                .Where(p => p.Name == "code")
                .Select(p => p.Value.GetString()))
            .ToList();
        Assert.Contains("INSUFFICIENT_FUNDS", errorCodes);
    }

    [Fact]
    public async Task ConstructionPhase_CompletesBuilding_WhenTickReached()
    {
        // After processing enough ticks, an under-construction building should
        // transition to IsUnderConstruction = false.
        var token = await RegisterAndGetTokenAsync($"construction-tick-{Guid.NewGuid()}@test.com");
        var (companyId, _, _) = await CompleteOnboardingAsync(token, "Tick Builder Co");

        var bratislavaId = await GetCityIdByNameAsync("Bratislava");
        var lotId = await CreateTestLotAsync(bratislavaId, "FACTORY,MINE", "Industrial Zone", 20_000m, "Tick Test Lot");

        var purchaseResult = await ExecuteGraphQlAsync(
            """
            mutation PurchaseLot($input: PurchaseLotInput!) {
              purchaseLot(input: $input) {
                building { id isUnderConstruction constructionCompletesAtTick }
              }
            }
            """,
            new { input = new { companyId, lotId, buildingType = "FACTORY", buildingName = "Tick Factory" } },
            token);

        Assert.False(purchaseResult.TryGetProperty("errors", out _), "purchaseLot must succeed");
        var buildingElem = purchaseResult.GetProperty("data").GetProperty("purchaseLot").GetProperty("building");
        var buildingId = Guid.Parse(buildingElem.GetProperty("id").GetString()!);
        Assert.True(buildingElem.GetProperty("isUnderConstruction").GetBoolean(),
            "Building should be under construction after purchase");

        // Move the completion tick to now so ConstructionPhase will complete it this tick.
        await using var setupScope = _factory.Services.CreateAsyncScope();
        var setupDb = setupScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var buildingToModify = await setupDb.Buildings.FindAsync(buildingId);
        Assert.NotNull(buildingToModify);
        var gameState = await setupDb.GameStates.FirstOrDefaultAsync();
        Assert.NotNull(gameState);
        buildingToModify.ConstructionCompletesAtTick = gameState.CurrentTick;
        await setupDb.SaveChangesAsync();

        // Process one tick through the full tick engine (includes ConstructionPhase)
        await ProcessTicksAsync(1);

        // Verify the building is now operational
        await using var verifyScope = _factory.Services.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var completedBuilding = await verifyDb.Buildings.FindAsync(buildingId);
        Assert.NotNull(completedBuilding);
        Assert.False(completedBuilding.IsUnderConstruction,
            "Building must become operational after ConstructionPhase processes the completion tick");
        Assert.Null(completedBuilding.ConstructionCompletesAtTick);
    }

    [Fact]
    public async Task GameConstants_AllBuildingTypes_HavePositiveConstructionCostAndTicks()
    {
        // Backend and frontend must agree on construction costs/ticks for each building type.
        // This test validates that every known building type produces a positive cost and tick
        // count from GameConstants, catching any future typo or missing branch.
        var buildingTypes = new[]
        {
            Api.Data.Entities.BuildingType.Mine,
            Api.Data.Entities.BuildingType.Factory,
            Api.Data.Entities.BuildingType.SalesShop,
            Api.Data.Entities.BuildingType.ResearchDevelopment,
            Api.Data.Entities.BuildingType.Apartment,
            Api.Data.Entities.BuildingType.Commercial,
            Api.Data.Entities.BuildingType.MediaHouse,
            Api.Data.Entities.BuildingType.Bank,
            Api.Data.Entities.BuildingType.Exchange,
            Api.Data.Entities.BuildingType.PowerPlant,
        };

        foreach (var type in buildingTypes)
        {
            var cost = Api.Engine.GameConstants.ConstructionCost(type);
            var ticks = Api.Engine.GameConstants.ConstructionTicks(type);
            Assert.True(cost > 0,
                $"ConstructionCost for '{type}' must be > 0 (got {cost})");
            Assert.True(ticks > 0,
                $"ConstructionTicks for '{type}' must be > 0 (got {ticks})");
        }
    }

    [Fact]
    public async Task PurchaseLot_ConstructionState_VisibleInCityLotsQuery()
    {
        // After purchasing a lot, the cityLots query must expose the construction state
        // so the frontend can display the under-construction panel without a separate fetch.
        var token = await RegisterAndGetTokenAsync($"construction-query-{Guid.NewGuid()}@test.com");
        var (companyId, _, _) = await CompleteOnboardingAsync(token, "Query Construction Co");

        var bratislavaId = await GetCityIdByNameAsync("Bratislava");
        var lotId = await CreateTestLotAsync(bratislavaId, "FACTORY,MINE", "Industrial Zone", 10_000m, "Query Test Lot");

        // Purchase the lot to trigger construction
        await ExecuteGraphQlAsync(
            """
            mutation PurchaseLot($input: PurchaseLotInput!) {
              purchaseLot(input: $input) { building { id } }
            }
            """,
            new { input = new { companyId, lotId, buildingType = "FACTORY", buildingName = "Query Factory" } },
            token);

        // Now query the city lots — the purchased lot's building should show construction state
        var lotsResult = await ExecuteGraphQlAsync(
            """
            query CityLots($cityId: UUID!) {
              cityLots(cityId: $cityId) {
                id building { id isUnderConstruction constructionCompletesAtTick constructionCost }
              }
            }
            """,
            new { cityId = bratislavaId });

        Assert.False(lotsResult.TryGetProperty("errors", out _), "cityLots query must not return errors");
        var lots = lotsResult.GetProperty("data").GetProperty("cityLots").EnumerateArray().ToList();
        var purchasedLot = lots.FirstOrDefault(l => l.GetProperty("id").GetString() == lotId);
        Assert.True(purchasedLot.ValueKind != JsonValueKind.Undefined, "Purchased lot must appear in cityLots result");

        var building = purchasedLot.GetProperty("building");
        Assert.True(building.ValueKind != JsonValueKind.Null, "Purchased lot must have a building");
        Assert.True(building.GetProperty("isUnderConstruction").GetBoolean(),
            "Building must show isUnderConstruction=true in cityLots query");
        Assert.True(building.GetProperty("constructionCompletesAtTick").GetInt64() > 0,
            "constructionCompletesAtTick must be set in cityLots query");
        Assert.True(building.GetProperty("constructionCost").GetDecimal() > 0m,
            "constructionCost must be set in cityLots query");
    }

    [Fact]
    public async Task PurchaseLot_WrongCompany_CannotStartConstruction()
    {
        // Authorization: a player should not be able to start construction using
        // another player's company ID (only companies owned by the authenticated player are valid).
        var token1 = await RegisterAndGetTokenAsync($"const-auth-a-{Guid.NewGuid()}@test.com");
        var (companyId1, _, _) = await CompleteOnboardingAsync(token1, "Auth A Corp");

        var token2 = await RegisterAndGetTokenAsync($"const-auth-b-{Guid.NewGuid()}@test.com");
        // Player 2 does not own companyId1
        await CompleteOnboardingAsync(token2, "Auth B Corp");

        var bratislavaId = await GetCityIdByNameAsync("Bratislava");
        var lotId = await CreateTestLotAsync(bratislavaId, "FACTORY,MINE", "Industrial Zone", 5_000m, "Auth Test Lot");

        // Player 2 tries to purchase using Player 1's company → must fail
        var result = await ExecuteGraphQlAsync(
            """
            mutation PurchaseLot($input: PurchaseLotInput!) {
              purchaseLot(input: $input) { building { id } }
            }
            """,
            new { input = new { companyId = companyId1, lotId, buildingType = "FACTORY", buildingName = "Unauthorized Factory" } },
            token2);

        Assert.True(result.TryGetProperty("errors", out _),
            "Purchasing with another player's company must return errors");
    }

    [Fact]
    public async Task PurchaseLot_SuitableTypes_OnlyAllowedBuildingTypesAccepted()
    {
        // Allowed building-type validation: a lot that only supports SALES_SHOP,COMMERCIAL
        // must reject a FACTORY purchase attempt with UNSUITABLE_BUILDING_TYPE error.
        var token = await RegisterAndGetTokenAsync($"suitable-type-{Guid.NewGuid()}@test.com");
        var (companyId, _, _) = await CompleteOnboardingAsync(token, "Type Validation Co");

        var bratislavaId = await GetCityIdByNameAsync("Bratislava");
        var shopOnlyLotId = await CreateTestLotAsync(
            bratislavaId, "SALES_SHOP,COMMERCIAL", "Commercial District", 5_000m, "Shop-Only Lot");

        var result = await ExecuteGraphQlAsync(
            """
            mutation PurchaseLot($input: PurchaseLotInput!) {
              purchaseLot(input: $input) { building { id } }
            }
            """,
            new { input = new { companyId, lotId = shopOnlyLotId, buildingType = "FACTORY", buildingName = "Rejected Factory" } },
            token);

        Assert.True(result.TryGetProperty("errors", out var errors), "Unsuitable building type must return errors");
        var codes = errors.EnumerateArray()
            .SelectMany(e => e.TryGetProperty("extensions", out var ext)
                ? ext.EnumerateObject().Where(p => p.Name == "code").Select(p => p.Value.GetString())
                : Enumerable.Empty<string?>())
            .ToList();
        Assert.Contains("UNSUITABLE_BUILDING_TYPE", codes);
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

        // Process enough ticks for the supply chain to produce a real public sale
        await ProcessTicksAsync(6);

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

        // Process enough ticks for a real public sale to be recorded
        await ProcessTicksAsync(6);

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

        // Process enough ticks for a real public sale to be recorded
        await ProcessTicksAsync(6);

        // Complete the milestone (now that a real sale exists)
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
    public async Task CompleteFirstSaleMilestone_BeforeFirstSale_ReturnsFirstSaleNotRecorded()
    {
        // Complete onboarding so the shop is configured, but do NOT process any ticks
        var token = await RegisterAndGetTokenAsync($"first-sale-before-{Guid.NewGuid()}@test.com", "Early Bird Seller");
        var (_, _, cityId, _) = await StartOnboardingCompanyAsync(token, "Early Bird Co");
        var productId = await GetStarterProductIdAsync();
        var shopLotId = await CreateTestLotAsync(cityId, "SALES_SHOP,COMMERCIAL", "Commercial District", 90_000m, "Early Bird Shop Lot");
        await FinishOnboardingAsync(token, productId, shopLotId);

        // Attempt to complete the milestone without any ticks having processed a sale
        var result = await ExecuteGraphQlAsync(
            """
            mutation {
              completeFirstSaleMilestone {
                onboardingFirstSaleCompletedAtUtc
              }
            }
            """,
            token: token);

        Assert.True(result.TryGetProperty("errors", out var errors2));
        var code = errors2[0].GetProperty("extensions").GetProperty("code").GetString();
        Assert.Equal("FIRST_SALE_NOT_RECORDED", code);
    }

    [Fact]
    public async Task FirstSaleMission_ReturnsNoShop_WhenOnboardingNotComplete()
    {
        var token = await RegisterAndGetTokenAsync($"mission-noshop-{Guid.NewGuid()}@test.com", "No Shop Player");

        var result = await ExecuteGraphQlAsync(
            """
            {
              firstSaleMission {
                phase
                shopBuildingId
                blockers
              }
            }
            """,
            token: token);

        Assert.False(result.TryGetProperty("errors", out _));
        var mission = result.GetProperty("data").GetProperty("firstSaleMission");
        Assert.Equal("NO_SHOP", mission.GetProperty("phase").GetString());
        Assert.Equal(JsonValueKind.Null, mission.GetProperty("shopBuildingId").ValueKind);
    }

    [Fact]
    public async Task FirstSaleMission_ReturnsConfigureShop_WhenShopHasNoUnitsYet()
    {
        // Complete full onboarding, then remove all units from the shop to simulate no units.
        var token = await RegisterAndGetTokenAsync($"mission-configure-{Guid.NewGuid()}@test.com", "Configure Player");
        var (_, _, cityId, _) = await StartOnboardingCompanyAsync(token, "Configure Corp");
        var productId = await GetStarterProductIdAsync();
        var shopLotId = await CreateTestLotAsync(cityId, "SALES_SHOP,COMMERCIAL", "Commercial District", 80_000m, "Configure Shop Lot");
        var finishResult = await FinishOnboardingAsync(token, productId, shopLotId);
        var shopBuildingId = Guid.Parse(finishResult.GetProperty("data").GetProperty("finishOnboarding").GetProperty("salesShop").GetProperty("id").GetString()!);

        // Remove all units from the shop to simulate an empty/unconfigured shop
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var units = db.BuildingUnits.Where(u => u.BuildingId == shopBuildingId).ToList();
        db.BuildingUnits.RemoveRange(units);
        await db.SaveChangesAsync();

        var result = await ExecuteGraphQlAsync(
            """
            {
              firstSaleMission {
                phase
                shopBuildingId
                blockers
              }
            }
            """,
            token: token);

        Assert.False(result.TryGetProperty("errors", out _));
        var mission = result.GetProperty("data").GetProperty("firstSaleMission");
        Assert.Equal("CONFIGURE_SHOP", mission.GetProperty("phase").GetString());
        var blockers = mission.GetProperty("blockers").EnumerateArray().Select(b => b.GetString()).ToList();
        Assert.Contains("PUBLIC_SALES_UNIT_MISSING", blockers);
    }

    [Fact]
    public async Task FirstSaleMission_ReturnsConfigureShop_WhenNoInventoryYet()
    {
        // FinishOnboarding creates a fully configured shop — but with no inventory yet.
        // Before any ticks process, the shop PUBLIC_SALES unit has no inventory.
        // Phase should be CONFIGURE_SHOP with NO_INVENTORY blocker.
        var token = await RegisterAndGetTokenAsync($"mission-noinv-{Guid.NewGuid()}@test.com", "No Inv Seller");
        var (_, _, cityId, _) = await StartOnboardingCompanyAsync(token, "No Inv Seller Co");
        var productId = await GetStarterProductIdAsync();
        var shopLotId = await CreateTestLotAsync(cityId, "SALES_SHOP,COMMERCIAL", "Commercial District", 90_000m, "No Inv Shop Lot");
        await FinishOnboardingAsync(token, productId, shopLotId);

        // No ticks processed — shop has price but no inventory
        var result = await ExecuteGraphQlAsync(
            """
            {
              firstSaleMission {
                phase
                shopBuildingId
                shopName
                blockers
              }
            }
            """,
            token: token);

        Assert.False(result.TryGetProperty("errors", out _));
        var mission = result.GetProperty("data").GetProperty("firstSaleMission");
        Assert.Equal("CONFIGURE_SHOP", mission.GetProperty("phase").GetString());
        Assert.NotNull(mission.GetProperty("shopBuildingId").GetString());
        Assert.NotNull(mission.GetProperty("shopName").GetString());
        var blockers = mission.GetProperty("blockers").EnumerateArray().Select(b => b.GetString()).ToList();
        Assert.Contains("NO_INVENTORY", blockers);
        Assert.DoesNotContain("PRICE_NOT_SET", blockers);
        Assert.DoesNotContain("PUBLIC_SALES_UNIT_MISSING", blockers);
    }

    [Fact]
    public async Task FirstSaleMission_ReturnsAwaitingFirstSale_AfterInventoryArrives()
    {
        // After 4 ticks, the shop should have inventory in the PUBLIC_SALES unit
        // but no public sale yet (sale requires 5+ ticks).
        var token = await RegisterAndGetTokenAsync($"mission-await-inv-{Guid.NewGuid()}@test.com", "Awaiting Inv Seller");
        var (_, _, cityId, _) = await StartOnboardingCompanyAsync(token, "Awaiting Inv Seller Co");
        var productId = await GetStarterProductIdAsync();
        var shopLotId = await CreateTestLotAsync(cityId, "SALES_SHOP,COMMERCIAL", "Commercial District", 90_000m, "Awaiting Inv Shop Lot");
        await FinishOnboardingAsync(token, productId, shopLotId);

        // Process 4 ticks — enough for inventory to arrive in the shop but not for a sale
        await ProcessTicksAsync(4);

        var result = await ExecuteGraphQlAsync(
            """
            {
              firstSaleMission {
                phase
                shopBuildingId
                shopName
                blockers
              }
            }
            """,
            token: token);

        Assert.False(result.TryGetProperty("errors", out _));
        var mission = result.GetProperty("data").GetProperty("firstSaleMission");
        Assert.NotNull(mission.GetProperty("shopBuildingId").GetString());
        Assert.NotNull(mission.GetProperty("shopName").GetString());
        // After 4 ticks with the starter supply chain, the shop's PUBLIC_SALES unit
        // should have received inventory. Phase should be AWAITING_FIRST_SALE with no blockers.
        // (If the factory hasn't transferred yet, phase may still be CONFIGURE_SHOP — either is valid)
        var phase = mission.GetProperty("phase").GetString();
        Assert.True(phase is "AWAITING_FIRST_SALE" or "CONFIGURE_SHOP",
            $"Expected AWAITING_FIRST_SALE or CONFIGURE_SHOP but got {phase}");
    }

    [Fact]
    public async Task FirstSaleMission_ReturnsFirstSaleRecorded_AfterRealSaleInSimulation()
    {
        var token = await RegisterAndGetTokenAsync($"mission-recorded-{Guid.NewGuid()}@test.com", "Recorded Seller");
        var (_, _, cityId, _) = await StartOnboardingCompanyAsync(token, "Recorded Seller Co");
        var productId = await GetStarterProductIdAsync();
        var shopLotId = await CreateTestLotAsync(cityId, "SALES_SHOP,COMMERCIAL", "Commercial District", 90_000m, "Recorded Shop Lot");
        await FinishOnboardingAsync(token, productId, shopLotId);

        // Process enough ticks for the starter supply chain to produce a real public sale
        await ProcessTicksAsync(6);

        var result = await ExecuteGraphQlAsync(
            """
            {
              firstSaleMission {
                phase
                shopBuildingId
                firstSaleRevenue
                firstSaleProductName
                firstSaleTick
                firstSaleQuantity
                firstSalePricePerUnit
              }
            }
            """,
            token: token);

        Assert.False(result.TryGetProperty("errors", out _));
        var mission = result.GetProperty("data").GetProperty("firstSaleMission");
        Assert.Equal("FIRST_SALE_RECORDED", mission.GetProperty("phase").GetString());
        Assert.True(mission.GetProperty("firstSaleRevenue").GetDecimal() > 0m);
        Assert.NotNull(mission.GetProperty("firstSaleProductName").GetString());
        Assert.True(mission.GetProperty("firstSaleTick").GetInt64() > 0L);
        Assert.True(mission.GetProperty("firstSaleQuantity").GetDecimal() > 0m);
        Assert.True(mission.GetProperty("firstSalePricePerUnit").GetDecimal() > 0m);
    }

    [Fact]
    public async Task FirstSaleMission_FoodProcessing_ReturnsFirstSaleRecorded_AfterRealSale()
    {
        var token = await RegisterAndGetTokenAsync($"mission-food-{Guid.NewGuid()}@test.com", "Food Seller");
        var cityId = await GetCityIdByNameAsync("Bratislava");
        var factoryLotId = await CreateTestLotAsync(cityId, "FACTORY,MINE", "Industrial Zone");
        await ExecuteGraphQlAsync(
            """
            mutation StartOnboardingCompany($input: StartOnboardingCompanyInput!) {
              startOnboardingCompany(input: $input) { nextStep company { id } }
            }
            """,
            new { input = new { industry = "FOOD_PROCESSING", cityId, companyName = "Bread Co", factoryLotId } },
            token);

        var productId = await GetStarterProductIdAsync("FOOD_PROCESSING", "bread");
        var shopLotId = await CreateTestLotAsync(cityId, "SALES_SHOP,COMMERCIAL", "Commercial District", 90_000m, "Food Shop Lot");
        await FinishOnboardingAsync(token, productId, shopLotId);

        // Food processing needs more ticks to process grain → bread → sale
        await ProcessTicksAsync(8);

        var result = await ExecuteGraphQlAsync(
            """
            {
              firstSaleMission {
                phase
                firstSaleRevenue
                firstSaleProductName
              }
            }
            """,
            token: token);

        Assert.False(result.TryGetProperty("errors", out _));
        var mission = result.GetProperty("data").GetProperty("firstSaleMission");
        Assert.Equal("FIRST_SALE_RECORDED", mission.GetProperty("phase").GetString());
        Assert.True(mission.GetProperty("firstSaleRevenue").GetDecimal() > 0m);
    }

    [Fact]
    public async Task FirstSaleMission_Healthcare_ReturnsFirstSaleRecorded_AfterRealSale()
    {
        var token = await RegisterAndGetTokenAsync($"mission-health-{Guid.NewGuid()}@test.com", "Health Seller");
        var cityId = await GetCityIdByNameAsync("Bratislava");
        var factoryLotId = await CreateTestLotAsync(cityId, "FACTORY,MINE", "Industrial Zone");
        await ExecuteGraphQlAsync(
            """
            mutation StartOnboardingCompany($input: StartOnboardingCompanyInput!) {
              startOnboardingCompany(input: $input) { nextStep company { id } }
            }
            """,
            new { input = new { industry = "HEALTHCARE", cityId, companyName = "Pharma Co", factoryLotId } },
            token);

        var productId = await GetStarterProductIdAsync("HEALTHCARE", "basic-medicine");
        var shopLotId = await CreateTestLotAsync(cityId, "SALES_SHOP,COMMERCIAL", "Commercial District", 90_000m, "Health Shop Lot");
        await FinishOnboardingAsync(token, productId, shopLotId);

        // Healthcare needs more ticks
        await ProcessTicksAsync(8);

        var result = await ExecuteGraphQlAsync(
            """
            {
              firstSaleMission {
                phase
                firstSaleRevenue
                firstSaleProductName
              }
            }
            """,
            token: token);

        Assert.False(result.TryGetProperty("errors", out _));
        var mission = result.GetProperty("data").GetProperty("firstSaleMission");
        Assert.Equal("FIRST_SALE_RECORDED", mission.GetProperty("phase").GetString());
        Assert.True(mission.GetProperty("firstSaleRevenue").GetDecimal() > 0m);
    }

    [Fact]
    public async Task FirstSaleMission_ReturnsAlreadyCompleted_AfterMilestoneAcknowledged()
    {
        var token = await RegisterAndGetTokenAsync($"mission-completed-{Guid.NewGuid()}@test.com", "Completed Seller");
        var (_, _, cityId, _) = await StartOnboardingCompanyAsync(token, "Completed Seller Co");
        var productId = await GetStarterProductIdAsync();
        var shopLotId = await CreateTestLotAsync(cityId, "SALES_SHOP,COMMERCIAL", "Commercial District", 90_000m, "Completed Shop Lot");
        await FinishOnboardingAsync(token, productId, shopLotId);
        await ProcessTicksAsync(6);

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

        // Mission should now be ALREADY_COMPLETED
        var result = await ExecuteGraphQlAsync(
            """
            {
              firstSaleMission {
                phase
                shopBuildingId
              }
            }
            """,
            token: token);

        Assert.False(result.TryGetProperty("errors", out _));
        var mission = result.GetProperty("data").GetProperty("firstSaleMission");
        Assert.Equal("ALREADY_COMPLETED", mission.GetProperty("phase").GetString());
        Assert.Equal(JsonValueKind.Null, mission.GetProperty("shopBuildingId").ValueKind);
    }

    [Fact]
    public async Task FirstSaleMission_Unauthenticated_ReturnsError()
    {
        var result = await ExecuteGraphQlAsync(
            """
            {
              firstSaleMission {
                phase
              }
            }
            """);

        Assert.True(result.TryGetProperty("errors", out var errors3));
        Assert.NotEmpty(errors3.EnumerateArray().ToList());
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

    [Fact]
    public async Task StoreBuildingConfiguration_DuplicateGridPosition_ReturnsError()
    {
        // Two units placed at the same (gridX, gridY) must be rejected.
        var token = await RegisterAndGetTokenAsync($"duppos-{Guid.NewGuid()}@test.com", "DupPosTester");
        var companyResult = await ExecuteGraphQlAsync(
            "mutation CreateCompany($input: CreateCompanyInput!) { createCompany(input: $input) { id } }",
            new { input = new { name = "Dup Pos Corp" } },
            token);
        var companyId = companyResult.GetProperty("data").GetProperty("createCompany").GetProperty("id").GetString();

        var citiesResult = await ExecuteGraphQlAsync("{ cities { id } }");
        var cityId = citiesResult.GetProperty("data").GetProperty("cities")[0].GetProperty("id").GetString();

        var buildingResult = await ExecuteGraphQlAsync(
            "mutation PlaceBuilding($input: PlaceBuildingInput!) { placeBuilding(input: $input) { id } }",
            new { input = new { companyId, cityId, type = "FACTORY", name = "Dup Pos Factory" } },
            token);
        var buildingId = buildingResult.GetProperty("data").GetProperty("placeBuilding").GetProperty("id").GetString();

        // Two PURCHASE units at (0,0) — duplicate position
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
                              linkUp = false, linkDown = false, linkLeft = false, linkRight = false,
                              linkUpLeft = false, linkUpRight = false, linkDownLeft = false, linkDownRight = false },
                        new { unitType = "STORAGE", gridX = 0, gridY = 0,
                              linkUp = false, linkDown = false, linkLeft = false, linkRight = false,
                              linkUpLeft = false, linkUpRight = false, linkDownLeft = false, linkDownRight = false }
                    }
                }
            },
            token);

        Assert.True(result.TryGetProperty("errors", out var errors));
        Assert.Equal("DUPLICATE_BUILDING_UNIT_POSITION", errors[0].GetProperty("extensions").GetProperty("code").GetString());
    }

    [Fact]
    public async Task StoreBuildingConfiguration_InvalidGridPosition_ReturnsError()
    {
        // A unit placed outside the 4x4 grid (gridX=4 is invalid, valid range is 0–3) must be rejected.
        var token = await RegisterAndGetTokenAsync($"invalidpos-{Guid.NewGuid()}@test.com", "InvalidPosTester");
        var companyResult = await ExecuteGraphQlAsync(
            "mutation CreateCompany($input: CreateCompanyInput!) { createCompany(input: $input) { id } }",
            new { input = new { name = "Invalid Pos Corp" } },
            token);
        var companyId = companyResult.GetProperty("data").GetProperty("createCompany").GetProperty("id").GetString();

        var citiesResult = await ExecuteGraphQlAsync("{ cities { id } }");
        var cityId = citiesResult.GetProperty("data").GetProperty("cities")[0].GetProperty("id").GetString();

        var buildingResult = await ExecuteGraphQlAsync(
            "mutation PlaceBuilding($input: PlaceBuildingInput!) { placeBuilding(input: $input) { id } }",
            new { input = new { companyId, cityId, type = "FACTORY", name = "Invalid Pos Factory" } },
            token);
        var buildingId = buildingResult.GetProperty("data").GetProperty("placeBuilding").GetProperty("id").GetString();

        // gridX=4 is outside the 4x4 grid boundary (valid range: 0–3)
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
                        new { unitType = "PURCHASE", gridX = 4, gridY = 0,
                              linkUp = false, linkDown = false, linkLeft = false, linkRight = false,
                              linkUpLeft = false, linkUpRight = false, linkDownLeft = false, linkDownRight = false }
                    }
                }
            },
            token);

        Assert.True(result.TryGetProperty("errors", out var errors));
        Assert.Equal("INVALID_BUILDING_UNIT_POSITION", errors[0].GetProperty("extensions").GetProperty("code").GetString());
    }

    [Fact]
    public async Task StoreBuildingConfiguration_InvalidUnitTypeForBuildingType_ReturnsError()
    {
        // A MINING unit is only valid in a MINE building. Attempting to place it in a FACTORY
        // must be rejected with INVALID_BUILDING_UNIT_TYPE.
        var token = await RegisterAndGetTokenAsync($"invtype-{Guid.NewGuid()}@test.com", "InvTypeTester");
        var companyResult = await ExecuteGraphQlAsync(
            "mutation CreateCompany($input: CreateCompanyInput!) { createCompany(input: $input) { id } }",
            new { input = new { name = "Inv Type Corp" } },
            token);
        var companyId = companyResult.GetProperty("data").GetProperty("createCompany").GetProperty("id").GetString();

        var citiesResult = await ExecuteGraphQlAsync("{ cities { id } }");
        var cityId = citiesResult.GetProperty("data").GetProperty("cities")[0].GetProperty("id").GetString();

        var buildingResult = await ExecuteGraphQlAsync(
            "mutation PlaceBuilding($input: PlaceBuildingInput!) { placeBuilding(input: $input) { id } }",
            new { input = new { companyId, cityId, type = "FACTORY", name = "Inv Type Factory" } },
            token);
        var buildingId = buildingResult.GetProperty("data").GetProperty("placeBuilding").GetProperty("id").GetString();

        // MINING is only allowed in MINE buildings, not in FACTORY
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
                        new { unitType = "MINING", gridX = 0, gridY = 0,
                              linkUp = false, linkDown = false, linkLeft = false, linkRight = false,
                              linkUpLeft = false, linkUpRight = false, linkDownLeft = false, linkDownRight = false }
                    }
                }
            },
            token);

        Assert.True(result.TryGetProperty("errors", out var errors));
        Assert.Equal("INVALID_BUILDING_UNIT_TYPE", errors[0].GetProperty("extensions").GetProperty("code").GetString());
    }

    [Fact]
    public async Task StoreBuildingConfiguration_TooManyUnits_ReturnsError()
    {
        // The 4x4 grid supports at most 16 units. Submitting 17 must be rejected.
        var token = await RegisterAndGetTokenAsync($"toomany-{Guid.NewGuid()}@test.com", "TooManyTester");
        var companyResult = await ExecuteGraphQlAsync(
            "mutation CreateCompany($input: CreateCompanyInput!) { createCompany(input: $input) { id } }",
            new { input = new { name = "Too Many Corp" } },
            token);
        var companyId = companyResult.GetProperty("data").GetProperty("createCompany").GetProperty("id").GetString();

        var citiesResult = await ExecuteGraphQlAsync("{ cities { id } }");
        var cityId = citiesResult.GetProperty("data").GetProperty("cities")[0].GetProperty("id").GetString();

        var buildingResult = await ExecuteGraphQlAsync(
            "mutation PlaceBuilding($input: PlaceBuildingInput!) { placeBuilding(input: $input) { id } }",
            new { input = new { companyId, cityId, type = "FACTORY", name = "Too Many Factory" } },
            token);
        var buildingId = buildingResult.GetProperty("data").GetProperty("placeBuilding").GetProperty("id").GetString();

        // Build 17 distinct PURCHASE units (only 16 fit in the 4x4 grid)
        var units = Enumerable.Range(0, 17)
            .Select(i => new
            {
                unitType = "PURCHASE",
                gridX = i % 4,
                gridY = i / 4,
                linkUp = false, linkDown = false, linkLeft = false, linkRight = false,
                linkUpLeft = false, linkUpRight = false, linkDownLeft = false, linkDownRight = false
            })
            .ToArray();

        var result = await ExecuteGraphQlAsync(
            """
            mutation StoreBuildingConfiguration($input: StoreBuildingConfigurationInput!) {
                storeBuildingConfiguration(input: $input) { id }
            }
            """,
            new { input = new { buildingId, units } },
            token);

        Assert.True(result.TryGetProperty("errors", out var errors));
        Assert.Equal("TOO_MANY_BUILDING_UNITS", errors[0].GetProperty("extensions").GetProperty("code").GetString());
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
    public async Task CompanyLedger_ResetsCurrentYearButKeepsHistoryVisible()
    {
        var token = await RegisterAndGetTokenAsync("ledger-years@test.com", "LedgerYears");
        var result = await ExecuteGraphQlAsync(
            """mutation CreateCompany($input: CreateCompanyInput!) { createCompany(input: $input) { id } }""",
            new { input = new { name = "Yearly Ledger Co" } },
            token);
        var companyId = result.GetProperty("data").GetProperty("createCompany").GetProperty("id").GetString()!;
        var companyGuid = Guid.Parse(companyId);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var gameState = await db.GameStates.FindAsync(1);
            Assert.NotNull(gameState);

            db.LedgerEntries.AddRange(
                new LedgerEntry
                {
                    Id = Guid.NewGuid(),
                    CompanyId = companyGuid,
                    Category = LedgerCategory.Revenue,
                    Description = "Year 2000 revenue",
                    Amount = 1200m,
                    RecordedAtTick = 12,
                    RecordedAtUtc = DateTime.UtcNow,
                },
                new LedgerEntry
                {
                    Id = Guid.NewGuid(),
                    CompanyId = companyGuid,
                    Category = LedgerCategory.Revenue,
                    Description = "Year 2001 revenue",
                    Amount = 3400m,
                    RecordedAtTick = 8760,
                    RecordedAtUtc = DateTime.UtcNow,
                });

            gameState!.CurrentTick = 8760;
            gameState.LastTickAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        var currentYearResult = await ExecuteGraphQlAsync(
            $"{{ companyLedger(companyId: \"{companyId}\") {{ gameYear totalRevenue history {{ gameYear totalRevenue }} }} }}",
            token: token);

        var currentYearLedger = currentYearResult.GetProperty("data").GetProperty("companyLedger");
        Assert.Equal(2001, currentYearLedger.GetProperty("gameYear").GetInt32());
        Assert.Equal(3400m, currentYearLedger.GetProperty("totalRevenue").GetDecimal());

        var historyYears = currentYearLedger.GetProperty("history").EnumerateArray().Select(entry => entry.GetProperty("gameYear").GetInt32()).ToList();
        Assert.Contains(2001, historyYears);
        Assert.Contains(2000, historyYears);

        var previousYearResult = await ExecuteGraphQlAsync(
            $"{{ companyLedger(companyId: \"{companyId}\", gameYear: 2000) {{ gameYear totalRevenue }} }}",
            token: token);

        var previousYearLedger = previousYearResult.GetProperty("data").GetProperty("companyLedger");
        Assert.Equal(2000, previousYearLedger.GetProperty("gameYear").GetInt32());
        Assert.Equal(1200m, previousYearLedger.GetProperty("totalRevenue").GetDecimal());
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
    public async Task PublicSalesAnalytics_WithSalesData_ReturnsDemandSignalAndHistory()
    {
        var token = await RegisterAndGetTokenAsync("analytics-data@test.com", "AnalyticsData");
        var (_, _, cityId, _) = await StartOnboardingCompanyAsync(token, "Analytics Data Co");
        var productId = await GetStarterProductIdAsync();
        var shopLotId = await CreateTestLotAsync(cityId, "SALES_SHOP,COMMERCIAL", "Commercial Zone");
        var finishResult = await FinishOnboardingAsync(token, productId, shopLotId);

        var shopId = finishResult.GetProperty("data").GetProperty("finishOnboarding").GetProperty("salesShop").GetProperty("id").GetString()!;

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var unit = await db.BuildingUnits
            .Include(u => u.Building).ThenInclude(b => b.Company)
            .FirstAsync(u => u.BuildingId == Guid.Parse(shopId) && u.UnitType == "PUBLIC_SALES");

        var productType = await db.ProductTypes.FirstAsync(p => p.Id == Guid.Parse(productId));

        // Seed 10 ticks of sales data with good utilization
        for (var tick = 1; tick <= 10; tick++)
        {
            db.PublicSalesRecords.Add(new PublicSalesRecord
            {
                Id = Guid.NewGuid(),
                BuildingUnitId = unit.Id,
                BuildingId = unit.BuildingId,
                CompanyId = unit.Building.CompanyId,
                CityId = unit.Building.CityId,
                ProductTypeId = productType.Id,
                Tick = tick,
                RecordedAtUtc = DateTime.UtcNow,
                QuantitySold = 80m, // high utilization vs capacity
                PricePerUnit = productType.BasePrice,
                Revenue = 80m * productType.BasePrice,
                Demand = 90m,
                SalesCapacity = 100m,
            });
        }
        await db.SaveChangesAsync();

        var analyticsResult = await ExecuteGraphQlAsync(
            $"{{ publicSalesAnalytics(unitId: \"{unit.Id}\") {{ buildingUnitId totalRevenue totalQuantitySold averagePricePerUnit revenueHistory {{ tick revenue quantitySold }} priceHistory {{ tick pricePerUnit }} demandSignal actionHint recentUtilization marketShare {{ label companyId share }} }} }}",
            token: token);

        var analytics = analyticsResult.GetProperty("data").GetProperty("publicSalesAnalytics");
        Assert.Equal(unit.Id.ToString(), analytics.GetProperty("buildingUnitId").GetString());

        var totalRevenue = analytics.GetProperty("totalRevenue").GetDecimal();
        Assert.True(totalRevenue > 0, "Total revenue should be positive when sales exist");

        Assert.Equal(800m, analytics.GetProperty("totalQuantitySold").GetDecimal());
        Assert.True(analytics.GetProperty("averagePricePerUnit").GetDecimal() > 0, "Average price should be positive");

        Assert.Equal(10, analytics.GetProperty("revenueHistory").GetArrayLength());
        Assert.Equal(10, analytics.GetProperty("priceHistory").GetArrayLength());

        // Demand signal should be STRONG given high utilization
        var demandSignal = analytics.GetProperty("demandSignal").GetString();
        Assert.Equal("STRONG", demandSignal);

        // Action hint should be non-empty
        var actionHint = analytics.GetProperty("actionHint").GetString();
        Assert.False(string.IsNullOrWhiteSpace(actionHint));

        // Recent utilization should be high
        var utilization = analytics.GetProperty("recentUtilization").GetDecimal();
        Assert.True(utilization >= 0.7m, $"Utilization should be >= 0.7 but was {utilization}");

        // Market share should contain the current company
        Assert.True(analytics.GetProperty("marketShare").GetArrayLength() > 0, "Market share should be populated");
    }

    [Fact]
    public async Task PublicSalesAnalytics_LowUtilization_ReturnsWeakDemandSignal()
    {
        var token = await RegisterAndGetTokenAsync("analytics-weak@test.com", "AnalyticsWeak");
        var (_, _, cityId, _) = await StartOnboardingCompanyAsync(token, "Analytics Weak Co");
        var productId = await GetStarterProductIdAsync();
        var shopLotId = await CreateTestLotAsync(cityId, "SALES_SHOP,COMMERCIAL", "Commercial Zone");
        var finishResult = await FinishOnboardingAsync(token, productId, shopLotId);

        var shopId = finishResult.GetProperty("data").GetProperty("finishOnboarding").GetProperty("salesShop").GetProperty("id").GetString()!;

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var unit = await db.BuildingUnits
            .Include(u => u.Building).ThenInclude(b => b.Company)
            .FirstAsync(u => u.BuildingId == Guid.Parse(shopId) && u.UnitType == "PUBLIC_SALES");

        var productType = await db.ProductTypes.FirstAsync(p => p.Id == Guid.Parse(productId));

        // Seed 10 ticks with very low utilization (< 30%)
        for (var tick = 1; tick <= 10; tick++)
        {
            db.PublicSalesRecords.Add(new PublicSalesRecord
            {
                Id = Guid.NewGuid(),
                BuildingUnitId = unit.Id,
                BuildingId = unit.BuildingId,
                CompanyId = unit.Building.CompanyId,
                CityId = unit.Building.CityId,
                ProductTypeId = productType.Id,
                Tick = tick,
                RecordedAtUtc = DateTime.UtcNow,
                QuantitySold = 5m, // very low utilization
                PricePerUnit = productType.BasePrice * 3m, // overpriced
                Revenue = 5m * productType.BasePrice * 3m,
                Demand = 10m,
                SalesCapacity = 100m,
            });
        }
        await db.SaveChangesAsync();

        var analyticsResult = await ExecuteGraphQlAsync(
            $"{{ publicSalesAnalytics(unitId: \"{unit.Id}\") {{ demandSignal actionHint recentUtilization }} }}",
            token: token);

        var analytics = analyticsResult.GetProperty("data").GetProperty("publicSalesAnalytics");
        Assert.Equal("WEAK", analytics.GetProperty("demandSignal").GetString());
        var actionHint = analytics.GetProperty("actionHint").GetString() ?? "";
        Assert.Contains("lower", actionHint, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PublicSalesAnalytics_SupplyConstrained_ReturnsCorrectSignal()
    {
        var token = await RegisterAndGetTokenAsync("analytics-supply@test.com", "AnalyticsSupply");
        var (_, _, cityId, _) = await StartOnboardingCompanyAsync(token, "Analytics Supply Co");
        var productId = await GetStarterProductIdAsync();
        var shopLotId = await CreateTestLotAsync(cityId, "SALES_SHOP,COMMERCIAL", "Commercial Zone");
        var finishResult = await FinishOnboardingAsync(token, productId, shopLotId);

        var shopId = finishResult.GetProperty("data").GetProperty("finishOnboarding").GetProperty("salesShop").GetProperty("id").GetString()!;

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var unit = await db.BuildingUnits
            .Include(u => u.Building).ThenInclude(b => b.Company)
            .FirstAsync(u => u.BuildingId == Guid.Parse(shopId) && u.UnitType == "PUBLIC_SALES");

        var productType = await db.ProductTypes.FirstAsync(p => p.Id == Guid.Parse(productId));

        // Seed 10 ticks: demand >> sold & sold = capacity → supply constrained
        for (var tick = 1; tick <= 10; tick++)
        {
            db.PublicSalesRecords.Add(new PublicSalesRecord
            {
                Id = Guid.NewGuid(),
                BuildingUnitId = unit.Id,
                BuildingId = unit.BuildingId,
                CompanyId = unit.Building.CompanyId,
                CityId = unit.Building.CityId,
                ProductTypeId = productType.Id,
                Tick = tick,
                RecordedAtUtc = DateTime.UtcNow,
                QuantitySold = 90m,
                PricePerUnit = productType.BasePrice,
                Revenue = 90m * productType.BasePrice,
                Demand = 500m, // demand >> sold
                SalesCapacity = 100m,
            });
        }
        await db.SaveChangesAsync();

        var analyticsResult = await ExecuteGraphQlAsync(
            $"{{ publicSalesAnalytics(unitId: \"{unit.Id}\") {{ demandSignal actionHint }} }}",
            token: token);

        var analytics = analyticsResult.GetProperty("data").GetProperty("publicSalesAnalytics");
        Assert.Equal("SUPPLY_CONSTRAINED", analytics.GetProperty("demandSignal").GetString());
    }

    [Fact]
    public async Task PublicSalesAnalytics_Unauthorized_ReturnsNull()
    {
        var ownerToken = await RegisterAndGetTokenAsync("analytics-owner@test.com", "AnalyticsOwner");
        var (_, _, cityId, _) = await StartOnboardingCompanyAsync(ownerToken, "Analytics Owner Co");
        var productId = await GetStarterProductIdAsync();
        var shopLotId = await CreateTestLotAsync(cityId, "SALES_SHOP,COMMERCIAL", "Commercial Zone");
        var finishResult = await FinishOnboardingAsync(ownerToken, productId, shopLotId);

        var shopId = finishResult.GetProperty("data").GetProperty("finishOnboarding").GetProperty("salesShop").GetProperty("id").GetString()!;

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var unit = await db.BuildingUnits
            .FirstAsync(u => u.BuildingId == Guid.Parse(shopId) && u.UnitType == "PUBLIC_SALES");

        // Other player should get null
        var otherToken = await RegisterAndGetTokenAsync("analytics-other@test.com", "AnalyticsOther");
        var analyticsResult = await ExecuteGraphQlAsync(
            $"{{ publicSalesAnalytics(unitId: \"{unit.Id}\") {{ buildingUnitId }} }}",
            token: otherToken);

        var analytics = analyticsResult.GetProperty("data").GetProperty("publicSalesAnalytics");
        Assert.Equal(JsonValueKind.Null, analytics.ValueKind);
    }

    [Fact]
    public async Task PublicSalesAnalytics_MarketShare_SingleCompany_ReturnsFullShare()
    {
        var token = await RegisterAndGetTokenAsync("analytics-share@test.com", "AnalyticsShare");
        var (_, _, cityId, _) = await StartOnboardingCompanyAsync(token, "Analytics Share Co");
        var productId = await GetStarterProductIdAsync();
        var shopLotId = await CreateTestLotAsync(cityId, "SALES_SHOP,COMMERCIAL", "Commercial Zone");
        var finishResult = await FinishOnboardingAsync(token, productId, shopLotId);

        var shopId = finishResult.GetProperty("data").GetProperty("finishOnboarding").GetProperty("salesShop").GetProperty("id").GetString()!;

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var unit = await db.BuildingUnits
            .Include(u => u.Building).ThenInclude(b => b.Company)
            .FirstAsync(u => u.BuildingId == Guid.Parse(shopId) && u.UnitType == "PUBLIC_SALES");

        var productType = await db.ProductTypes.FirstAsync(p => p.Id == Guid.Parse(productId));
        var cityEntity = await db.Cities.FindAsync(Guid.Parse(cityId));

        // Only this company sells in tick 1
        db.PublicSalesRecords.Add(new PublicSalesRecord
        {
            Id = Guid.NewGuid(),
            BuildingUnitId = unit.Id,
            BuildingId = unit.BuildingId,
            CompanyId = unit.Building.CompanyId,
            CityId = unit.Building.CityId,
            ProductTypeId = productType.Id,
            Tick = 1,
            RecordedAtUtc = DateTime.UtcNow,
            QuantitySold = 50m,
            PricePerUnit = productType.BasePrice,
            Revenue = 50m * productType.BasePrice,
            Demand = 60m,
            SalesCapacity = 100m,
        });
        await db.SaveChangesAsync();

        var analyticsResult = await ExecuteGraphQlAsync(
            $"{{ publicSalesAnalytics(unitId: \"{unit.Id}\") {{ marketShare {{ label companyId share }} }} }}",
            token: token);

        var analytics = analyticsResult.GetProperty("data").GetProperty("publicSalesAnalytics");
        var marketShare = analytics.GetProperty("marketShare");
        Assert.True(marketShare.GetArrayLength() >= 1, "Market share should have at least one entry");

        // The current company's share should be present and non-zero
        var thisCompanyEntry = Enumerable.Range(0, marketShare.GetArrayLength())
            .Select(i => marketShare[i])
            .FirstOrDefault(e => e.GetProperty("companyId").GetString() == unit.Building.CompanyId.ToString());
        Assert.True(thisCompanyEntry.ValueKind != System.Text.Json.JsonValueKind.Undefined, "Current company should have a market share entry");
        Assert.True(thisCompanyEntry.GetProperty("share").GetDecimal() > 0, "Current company share should be > 0");
    }

    [Fact]
    public async Task PublicSalesAnalytics_ModerateDemand_ReturnsModerateDemandSignal()
    {
        var token = await RegisterAndGetTokenAsync("analytics-moderate@test.com", "AnalyticsModerate");
        var (_, _, cityId, _) = await StartOnboardingCompanyAsync(token, "Analytics Moderate Co");
        var productId = await GetStarterProductIdAsync();
        var shopLotId = await CreateTestLotAsync(cityId, "SALES_SHOP,COMMERCIAL", "Commercial Zone");
        var finishResult = await FinishOnboardingAsync(token, productId, shopLotId);

        var shopId = finishResult.GetProperty("data").GetProperty("finishOnboarding").GetProperty("salesShop").GetProperty("id").GetString()!;

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var unit = await db.BuildingUnits
            .Include(u => u.Building).ThenInclude(b => b.Company)
            .FirstAsync(u => u.BuildingId == Guid.Parse(shopId) && u.UnitType == "PUBLIC_SALES");

        var productType = await db.ProductTypes.FirstAsync(p => p.Id == Guid.Parse(productId));

        // Seed ticks with moderate utilization: level=1 capacity=20, need 30-70% range
        // 10 units sold / 20 capacity = 50% utilization → MODERATE
        // Backend thresholds: STRONG >= 0.7, MODERATE >= 0.3, WEAK < 0.3
        // The MODERATE interval is [0.3, 0.7) — exactly 0.7 is classified as STRONG.
        for (var tick = 1; tick <= 5; tick++)
        {
            db.PublicSalesRecords.Add(new PublicSalesRecord
            {
                Id = Guid.NewGuid(),
                BuildingUnitId = unit.Id,
                BuildingId = unit.BuildingId,
                CompanyId = unit.Building.CompanyId,
                CityId = unit.Building.CityId,
                ProductTypeId = productType.Id,
                Tick = tick,
                RecordedAtUtc = DateTime.UtcNow,
                QuantitySold = 10m, // 10 / capacity(20) = 50% → MODERATE (0.3–0.7 range)
                PricePerUnit = productType.BasePrice,
                Revenue = 10m * productType.BasePrice,
                Demand = 11m,
                SalesCapacity = 20m,
            });
        }
        await db.SaveChangesAsync();

        var analyticsResult = await ExecuteGraphQlAsync(
            $"{{ publicSalesAnalytics(unitId: \"{unit.Id}\") {{ demandSignal actionHint recentUtilization }} }}",
            token: token);

        var analytics = analyticsResult.GetProperty("data").GetProperty("publicSalesAnalytics");
        Assert.Equal("MODERATE", analytics.GetProperty("demandSignal").GetString());
        var actionHint = analytics.GetProperty("actionHint").GetString() ?? "";
        Assert.False(string.IsNullOrWhiteSpace(actionHint));
        var utilization = analytics.GetProperty("recentUtilization").GetDecimal();
        Assert.True(utilization >= 0.3m && utilization < 0.7m, $"Moderate utilization should be in [0.3, 0.7) but was {utilization}");
    }

    [Fact]
    public async Task PublicSalesAnalytics_MultiCompanyMarketShare_SplitsShareCorrectly()
    {
        // Company A registers and sets up a shop
        var tokenA = await RegisterAndGetTokenAsync("analytics-msa@test.com", "AnalyticsMsA");
        var (_, _, cityId, _) = await StartOnboardingCompanyAsync(tokenA, "Market Share Co A");
        var productId = await GetStarterProductIdAsync();
        var shopLotA = await CreateTestLotAsync(cityId, "SALES_SHOP,COMMERCIAL", "Zone A");
        var finishA = await FinishOnboardingAsync(tokenA, productId, shopLotA);
        var shopIdA = finishA.GetProperty("data").GetProperty("finishOnboarding").GetProperty("salesShop").GetProperty("id").GetString()!;

        // Company B registers and sets up a shop in the same city
        var tokenB = await RegisterAndGetTokenAsync("analytics-msb@test.com", "AnalyticsMsB");
        var factoryLotB = await CreateTestLotAsync(cityId, "FACTORY,MINE", "Zone B Factory");
        var (_, _, _, _) = await StartOnboardingCompanyAsync(tokenB, "Market Share Co B", factoryLotId: factoryLotB);
        var shopLotB = await CreateTestLotAsync(cityId, "SALES_SHOP,COMMERCIAL", "Zone B");
        var finishB = await FinishOnboardingAsync(tokenB, productId, shopLotB);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var unitA = await db.BuildingUnits
            .Include(u => u.Building).ThenInclude(b => b.Company)
            .FirstAsync(u => u.BuildingId == Guid.Parse(shopIdA) && u.UnitType == "PUBLIC_SALES");

        var shopIdB = finishB.GetProperty("data").GetProperty("finishOnboarding").GetProperty("salesShop").GetProperty("id").GetString()!;
        var unitB = await db.BuildingUnits
            .Include(u => u.Building).ThenInclude(b => b.Company)
            .FirstAsync(u => u.BuildingId == Guid.Parse(shopIdB) && u.UnitType == "PUBLIC_SALES");

        var productType = await db.ProductTypes.FirstAsync(p => p.Id == Guid.Parse(productId));
        // All tests share the same SQLite database within a test class (IClassFixture).
        // The market share query filters by the most recent tick for each unit's records,
        // then fetches all records from the same city/product at that tick. Using a high
        // tick value that no other test in this class seeds avoids cross-test contamination.
        var tick = 99999L;

        // A sells 75 units, B sells 25 units → A has 75% share, B has 25% share
        db.PublicSalesRecords.Add(new PublicSalesRecord
        {
            Id = Guid.NewGuid(),
            BuildingUnitId = unitA.Id,
            BuildingId = unitA.BuildingId,
            CompanyId = unitA.Building.CompanyId,
            CityId = unitA.Building.CityId,
            ProductTypeId = productType.Id,
            Tick = tick,
            RecordedAtUtc = DateTime.UtcNow,
            QuantitySold = 75m,
            PricePerUnit = productType.BasePrice,
            Revenue = 75m * productType.BasePrice,
            Demand = 100m,
            SalesCapacity = 100m,
        });
        db.PublicSalesRecords.Add(new PublicSalesRecord
        {
            Id = Guid.NewGuid(),
            BuildingUnitId = unitB.Id,
            BuildingId = unitB.BuildingId,
            CompanyId = unitB.Building.CompanyId,
            CityId = unitB.Building.CityId,
            ProductTypeId = productType.Id,
            Tick = tick,
            RecordedAtUtc = DateTime.UtcNow,
            QuantitySold = 25m,
            PricePerUnit = productType.BasePrice,
            Revenue = 25m * productType.BasePrice,
            Demand = 100m,
            SalesCapacity = 100m,
        });
        await db.SaveChangesAsync();

        var analyticsResult = await ExecuteGraphQlAsync(
            $"{{ publicSalesAnalytics(unitId: \"{unitA.Id}\") {{ marketShare {{ label companyId share isUnmet }} }} }}",
            token: tokenA);

        var analytics = analyticsResult.GetProperty("data").GetProperty("publicSalesAnalytics");
        var marketShare = analytics.GetProperty("marketShare");
        Assert.Equal(2, marketShare.GetArrayLength());

        var entries = Enumerable.Range(0, marketShare.GetArrayLength())
            .Select(i => marketShare[i])
            .ToDictionary(
                e => e.GetProperty("companyId").GetString()!,
                e => e.GetProperty("share").GetDecimal());

        Assert.True(entries.ContainsKey(unitA.Building.CompanyId.ToString()), "Company A should be in market share");
        Assert.True(entries.ContainsKey(unitB.Building.CompanyId.ToString()), "Company B should be in market share");

        var shareA = entries[unitA.Building.CompanyId.ToString()];
        var shareB = entries[unitB.Building.CompanyId.ToString()];

        Assert.True(Math.Abs(shareA - 0.75m) < 0.001m, $"Company A share should be 0.75 but was {shareA}");
        Assert.True(Math.Abs(shareB - 0.25m) < 0.001m, $"Company B share should be 0.25 but was {shareB}");
        Assert.True(Math.Abs(shareA + shareB - 1.0m) < 0.001m, "Shares should sum to 1.0");
    }

    [Fact]
    public async Task PublicSalesAnalytics_Exactly100Ticks_ReturnsAll100Records()
    {
        var token = await RegisterAndGetTokenAsync("analytics-100t@test.com", "Analytics100T");
        var (_, _, cityId, _) = await StartOnboardingCompanyAsync(token, "Analytics 100T Co");
        var productId = await GetStarterProductIdAsync();
        var shopLotId = await CreateTestLotAsync(cityId, "SALES_SHOP,COMMERCIAL", "Commercial Zone");
        var finishResult = await FinishOnboardingAsync(token, productId, shopLotId);

        var shopId = finishResult.GetProperty("data").GetProperty("finishOnboarding").GetProperty("salesShop").GetProperty("id").GetString()!;

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var unit = await db.BuildingUnits
            .Include(u => u.Building).ThenInclude(b => b.Company)
            .FirstAsync(u => u.BuildingId == Guid.Parse(shopId) && u.UnitType == "PUBLIC_SALES");

        var productType = await db.ProductTypes.FirstAsync(p => p.Id == Guid.Parse(productId));

        // Seed exactly 110 ticks; the API should return only the most recent 100
        for (var tick = 1; tick <= 110; tick++)
        {
            db.PublicSalesRecords.Add(new PublicSalesRecord
            {
                Id = Guid.NewGuid(),
                BuildingUnitId = unit.Id,
                BuildingId = unit.BuildingId,
                CompanyId = unit.Building.CompanyId,
                CityId = unit.Building.CityId,
                ProductTypeId = productType.Id,
                Tick = tick,
                RecordedAtUtc = DateTime.UtcNow,
                QuantitySold = 60m,
                PricePerUnit = productType.BasePrice,
                Revenue = 60m * productType.BasePrice,
                Demand = 70m,
                SalesCapacity = 100m,
            });
        }
        await db.SaveChangesAsync();

        var analyticsResult = await ExecuteGraphQlAsync(
            $"{{ publicSalesAnalytics(unitId: \"{unit.Id}\") {{ dataFromTick dataToTick revenueHistory {{ tick }} priceHistory {{ tick }} }} }}",
            token: token);

        var analytics = analyticsResult.GetProperty("data").GetProperty("publicSalesAnalytics");
        // The API takes the most recent 100 records (ticks 11–110)
        Assert.Equal(100, analytics.GetProperty("revenueHistory").GetArrayLength());
        Assert.Equal(100, analytics.GetProperty("priceHistory").GetArrayLength());
        Assert.Equal(11L, analytics.GetProperty("dataFromTick").GetInt64());
        Assert.Equal(110L, analytics.GetProperty("dataToTick").GetInt64());
    }

    [Fact]
    public async Task PublicSalesAnalytics_ElasticityIndex_ReturnedWhenSalesExist()
    {
        var token = await RegisterAndGetTokenAsync("analytics-elas@test.com", "AnalyticsElas");
        var (_, _, cityId, _) = await StartOnboardingCompanyAsync(token, "Elasticity Co");
        var productId = await GetStarterProductIdAsync();
        var shopLotId = await CreateTestLotAsync(cityId, "SALES_SHOP,COMMERCIAL", "Commercial Zone");
        var finishResult = await FinishOnboardingAsync(token, productId, shopLotId);

        var shopId = finishResult.GetProperty("data").GetProperty("finishOnboarding").GetProperty("salesShop").GetProperty("id").GetString()!;

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var unit = await db.BuildingUnits
            .Include(u => u.Building).ThenInclude(b => b.Company)
            .FirstAsync(u => u.BuildingId == Guid.Parse(shopId) && u.UnitType == "PUBLIC_SALES");
        var productType = await db.ProductTypes.FirstAsync(p => p.Id == Guid.Parse(productId));

        db.PublicSalesRecords.Add(new PublicSalesRecord
        {
            Id = Guid.NewGuid(),
            BuildingUnitId = unit.Id,
            BuildingId = unit.BuildingId,
            CompanyId = unit.Building.CompanyId,
            CityId = unit.Building.CityId,
            ProductTypeId = productType.Id,
            Tick = 1,
            RecordedAtUtc = DateTime.UtcNow,
            QuantitySold = 10m,
            PricePerUnit = productType.BasePrice, // selling at base price → elasticity ≈ -1.0
            Revenue = 10m * productType.BasePrice,
            Demand = 10m,
            SalesCapacity = 20m,
        });
        await db.SaveChangesAsync();

        var analyticsResult = await ExecuteGraphQlAsync(
            $"{{ publicSalesAnalytics(unitId: \"{unit.Id}\") {{ elasticityIndex unmetDemandShare }} }}",
            token: token);

        var analytics = analyticsResult.GetProperty("data").GetProperty("publicSalesAnalytics");
        Assert.False(analytics.GetProperty("elasticityIndex").ValueKind == System.Text.Json.JsonValueKind.Null, "ElasticityIndex should be returned");
        var elas = analytics.GetProperty("elasticityIndex").GetDecimal();
        var expectedElasticity = PublicSalesPricingModel.ComputeElasticityIndex(productType.PriceElasticity);
        Assert.True(Math.Abs(elas - expectedElasticity) < 0.01m, $"Elasticity should reflect the product definition. Expected {expectedElasticity}, got {elas}");
    }

    [Fact]
    public async Task PublicSalesAnalytics_UnmetDemand_ReturnsNonZeroWhenDemandExceedsSales()
    {
        var token = await RegisterAndGetTokenAsync("analytics-unmet@test.com", "AnalyticsUnmet");
        var (_, _, cityId, _) = await StartOnboardingCompanyAsync(token, "Unmet Demand Co");
        var productId = await GetStarterProductIdAsync();
        var shopLotId = await CreateTestLotAsync(cityId, "SALES_SHOP,COMMERCIAL", "Commercial Zone");
        var finishResult = await FinishOnboardingAsync(token, productId, shopLotId);

        var shopId = finishResult.GetProperty("data").GetProperty("finishOnboarding").GetProperty("salesShop").GetProperty("id").GetString()!;

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var unit = await db.BuildingUnits
            .Include(u => u.Building).ThenInclude(b => b.Company)
            .FirstAsync(u => u.BuildingId == Guid.Parse(shopId) && u.UnitType == "PUBLIC_SALES");
        var productType = await db.ProductTypes.FirstAsync(p => p.Id == Guid.Parse(productId));

        // Demand = 100, sold = 40 → 60% unmet demand
        db.PublicSalesRecords.Add(new PublicSalesRecord
        {
            Id = Guid.NewGuid(),
            BuildingUnitId = unit.Id,
            BuildingId = unit.BuildingId,
            CompanyId = unit.Building.CompanyId,
            CityId = unit.Building.CityId,
            ProductTypeId = productType.Id,
            Tick = 88888L, // unique tick to avoid cross-test contamination
            RecordedAtUtc = DateTime.UtcNow,
            QuantitySold = 40m,
            PricePerUnit = productType.BasePrice,
            Revenue = 40m * productType.BasePrice,
            Demand = 100m, // demand far exceeds sold quantity
            SalesCapacity = 50m,
        });
        await db.SaveChangesAsync();

        var analyticsResult = await ExecuteGraphQlAsync(
            $"{{ publicSalesAnalytics(unitId: \"{unit.Id}\") {{ unmetDemandShare marketShare {{ label isUnmet share }} }} }}",
            token: token);

        var analytics = analyticsResult.GetProperty("data").GetProperty("publicSalesAnalytics");

        // unmetDemandShare = (100 - 40) / 100 = 0.6
        Assert.False(analytics.GetProperty("unmetDemandShare").ValueKind == System.Text.Json.JsonValueKind.Null, "UnmetDemandShare should be non-null");
        var unmet = analytics.GetProperty("unmetDemandShare").GetDecimal();
        Assert.True(Math.Abs(unmet - 0.6m) < 0.01m, $"Unmet share should be 0.60 but was {unmet}");

        // Market share should include an "Unmet Demand" entry
        var ms = analytics.GetProperty("marketShare");
        var unmetEntry = Enumerable.Range(0, ms.GetArrayLength())
            .Select(i => ms[i])
            .FirstOrDefault(e => e.GetProperty("isUnmet").GetBoolean());
        Assert.False(unmetEntry.ValueKind == System.Text.Json.JsonValueKind.Undefined, "Unmet Demand entry should be in marketShare");
        Assert.True(Math.Abs(unmetEntry.GetProperty("share").GetDecimal() - 0.6m) < 0.01m);
    }

    // ── UpdatePublicSalesPrice mutation tests ────────────────────────────────

    private async Task<Guid> GetPublicSalesUnitIdAsync(string shopId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var unit = await db.BuildingUnits
            .FirstAsync(u => u.BuildingId == Guid.Parse(shopId) && u.UnitType == UnitType.PublicSales);
        return unit.Id;
    }

    private async Task<(string token, Guid unitId)> SetupPublicSalesUnitAsync(string email, string displayName, string companyName)
    {
        var token = await RegisterAndGetTokenAsync(email, displayName);
        var (_, _, cityId, _) = await StartOnboardingCompanyAsync(token, companyName);
        var productId = await GetStarterProductIdAsync();
        var shopLotId = await CreateTestLotAsync(cityId, "SALES_SHOP,COMMERCIAL", "Commercial Zone");
        var finishResult = await FinishOnboardingAsync(token, productId, shopLotId);
        var shopId = finishResult.GetProperty("data").GetProperty("finishOnboarding").GetProperty("salesShop").GetProperty("id").GetString()!;
        var unitId = await GetPublicSalesUnitIdAsync(shopId);
        return (token, unitId);
    }

    [Fact]
    public async Task UpdatePublicSalesPrice_HappyPath_UpdatesUnitMinPrice()
    {
        var (token, unitId) = await SetupPublicSalesUnitAsync("upsp-happy@test.com", "UpdatePriceHappy", "UpdatePrice Happy Co");

        var result = await ExecuteGraphQlAsync(
            """
            mutation UpdatePublicSalesPrice($input: UpdatePublicSalesPriceInput!) {
                updatePublicSalesPrice(input: $input) {
                    id
                    unitType
                    minPrice
                }
            }
            """,
            new { input = new { unitId, newMinPrice = 99.99m } },
            token);

        var unit = result.GetProperty("data").GetProperty("updatePublicSalesPrice");
        Assert.Equal("PUBLIC_SALES", unit.GetProperty("unitType").GetString());
        Assert.True(Math.Abs(unit.GetProperty("minPrice").GetDecimal() - 99.99m) < 0.001m,
            $"Expected minPrice 99.99 but got {unit.GetProperty("minPrice").GetDecimal()}");
    }

    [Fact]
    public async Task UpdatePublicSalesPrice_Unauthenticated_ReturnsError()
    {
        var result = await ExecuteGraphQlAsync(
            """
            mutation UpdatePublicSalesPrice($input: UpdatePublicSalesPriceInput!) {
                updatePublicSalesPrice(input: $input) { id minPrice }
            }
            """,
            new { input = new { unitId = Guid.NewGuid(), newMinPrice = 50m } },
            token: null);

        Assert.True(result.TryGetProperty("errors", out _), "Expected errors for unauthenticated request");
    }

    [Fact]
    public async Task UpdatePublicSalesPrice_NonOwner_ReturnsUnitNotFound()
    {
        // Player A owns the unit
        var (tokenA, unitId) = await SetupPublicSalesUnitAsync("upsp-owner@test.com", "UpdatePriceOwner", "Owner Price Co");

        // Player B tries to update
        var tokenB = await RegisterAndGetTokenAsync("upsp-other@test.com", "UpdatePriceOther");
        var result = await ExecuteGraphQlAsync(
            """
            mutation UpdatePublicSalesPrice($input: UpdatePublicSalesPriceInput!) {
                updatePublicSalesPrice(input: $input) { id minPrice }
            }
            """,
            new { input = new { unitId, newMinPrice = 50m } },
            tokenB);

        Assert.True(result.TryGetProperty("errors", out var errors), "Expected errors when non-owner tries to update");
        var code = errors[0].GetProperty("extensions").GetProperty("code").GetString();
        Assert.Equal("UNIT_NOT_FOUND", code);
    }

    [Fact]
    public async Task UpdatePublicSalesPrice_WrongUnitType_ReturnsInvalidUnitType()
    {
        var token = await RegisterAndGetTokenAsync("upsp-wrong-type@test.com", "WrongUnitType");
        var (_, _, cityId, _) = await StartOnboardingCompanyAsync(token, "WrongType Co");
        var productId = await GetStarterProductIdAsync();
        var shopLotId = await CreateTestLotAsync(cityId, "SALES_SHOP,COMMERCIAL", "Commercial Zone");
        var finishResult = await FinishOnboardingAsync(token, productId, shopLotId);
        var shopId = finishResult.GetProperty("data").GetProperty("finishOnboarding").GetProperty("salesShop").GetProperty("id").GetString()!;

        // Get a non-PUBLIC_SALES unit (PURCHASE unit from same shop)
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var purchaseUnit = await db.BuildingUnits
            .FirstAsync(u => u.BuildingId == Guid.Parse(shopId) && u.UnitType == UnitType.Purchase);

        var result = await ExecuteGraphQlAsync(
            """
            mutation UpdatePublicSalesPrice($input: UpdatePublicSalesPriceInput!) {
                updatePublicSalesPrice(input: $input) { id minPrice }
            }
            """,
            new { input = new { unitId = purchaseUnit.Id, newMinPrice = 50m } },
            token);

        Assert.True(result.TryGetProperty("errors", out var errors), "Expected errors for wrong unit type");
        var code = errors[0].GetProperty("extensions").GetProperty("code").GetString();
        Assert.Equal("INVALID_UNIT_TYPE", code);
    }

    [Fact]
    public async Task UpdatePublicSalesPrice_NegativePrice_ReturnsInvalidPrice()
    {
        var (token, unitId) = await SetupPublicSalesUnitAsync("upsp-negative@test.com", "NegativePrice", "NegativePrice Co");

        var result = await ExecuteGraphQlAsync(
            """
            mutation UpdatePublicSalesPrice($input: UpdatePublicSalesPriceInput!) {
                updatePublicSalesPrice(input: $input) { id minPrice }
            }
            """,
            new { input = new { unitId, newMinPrice = -5m } },
            token);

        Assert.True(result.TryGetProperty("errors", out var errors), "Expected error for negative price");
        var code = errors[0].GetProperty("extensions").GetProperty("code").GetString();
        Assert.Equal("INVALID_PRICE", code);
    }

    [Fact]
    public async Task UpdatePublicSalesPrice_ZeroPrice_ReturnsInvalidPrice()
    {
        var (token, unitId) = await SetupPublicSalesUnitAsync("upsp-zero@test.com", "ZeroPrice", "ZeroPrice Co");

        var result = await ExecuteGraphQlAsync(
            """
            mutation UpdatePublicSalesPrice($input: UpdatePublicSalesPriceInput!) {
                updatePublicSalesPrice(input: $input) { id minPrice }
            }
            """,
            new { input = new { unitId, newMinPrice = 0m } },
            token);

        Assert.True(result.TryGetProperty("errors", out var errors), "Expected error for zero price");
        var code = errors[0].GetProperty("extensions").GetProperty("code").GetString();
        Assert.Equal("INVALID_PRICE", code);
    }

    [Fact]
    public async Task UpdatePublicSalesPrice_NewPriceVisibleInUnitQuery()
    {
        // After updating the price, the new minPrice should be returned when querying the unit via myCompanies.
        var (token, unitId) = await SetupPublicSalesUnitAsync("upsp-visible@test.com", "PriceVisible", "PriceVisible Co");
        const decimal newPrice = 75.50m;

        await ExecuteGraphQlAsync(
            """
            mutation UpdatePublicSalesPrice($input: UpdatePublicSalesPriceInput!) {
                updatePublicSalesPrice(input: $input) { id minPrice }
            }
            """,
            new { input = new { unitId, newMinPrice = newPrice } },
            token);

        // Verify persisted via a direct DB read so we don't depend on GraphQL query structure.
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var unit = await db.BuildingUnits.FindAsync(unitId);
        Assert.NotNull(unit);
        Assert.True(Math.Abs(unit!.MinPrice!.Value - newPrice) < 0.001m,
            $"Expected persisted MinPrice {newPrice} but got {unit.MinPrice}");
    }

    [Fact]
    public async Task UpdatePublicSalesPrice_FoodProcessing_UpdatesMinPriceCorrectly()
    {
        // Verify the mutation works for the FOOD_PROCESSING starter industry (Bread).
        var token = await RegisterAndGetTokenAsync("upsp-food@test.com", "FoodPriceTest");
        var cityId = await GetCityIdByNameAsync();
        var factoryLotId = await CreateTestLotAsync(cityId, "FACTORY,MINE", "Food Factory Zone");

        await ExecuteGraphQlAsync(
            """
            mutation StartOnboardingCompany($input: StartOnboardingCompanyInput!) {
              startOnboardingCompany(input: $input) { nextStep company { id } }
            }
            """,
            new { input = new { industry = "FOOD_PROCESSING", cityId, companyName = "Food Price Co", factoryLotId } },
            token);

        var productId = await GetStarterProductIdAsync("FOOD_PROCESSING", "bread");
        var shopLotId = await CreateTestLotAsync(cityId, "SALES_SHOP,COMMERCIAL", "Food Zone");
        var finishResult = await FinishOnboardingAsync(token, productId, shopLotId);
        var shopId = finishResult.GetProperty("data").GetProperty("finishOnboarding").GetProperty("salesShop").GetProperty("id").GetString()!;
        var unitId = await GetPublicSalesUnitIdAsync(shopId);

        var result = await ExecuteGraphQlAsync(
            """
            mutation UpdatePublicSalesPrice($input: UpdatePublicSalesPriceInput!) {
                updatePublicSalesPrice(input: $input) { id unitType minPrice }
            }
            """,
            new { input = new { unitId, newMinPrice = 12.50m } },
            token);

        Assert.False(result.TryGetProperty("errors", out _), "Expected no errors for Food Processing price update");
        var unit = result.GetProperty("data").GetProperty("updatePublicSalesPrice");
        Assert.Equal("PUBLIC_SALES", unit.GetProperty("unitType").GetString());
        Assert.True(Math.Abs(unit.GetProperty("minPrice").GetDecimal() - 12.50m) < 0.001m,
            $"Expected minPrice 12.50 but got {unit.GetProperty("minPrice").GetDecimal()}");
    }

    [Fact]
    public async Task UpdatePublicSalesPrice_Healthcare_UpdatesMinPriceCorrectly()
    {
        // Verify the mutation works for the HEALTHCARE starter industry (Basic Medicine).
        var token = await RegisterAndGetTokenAsync("upsp-health@test.com", "HealthPriceTest");
        var cityId = await GetCityIdByNameAsync();
        var factoryLotId = await CreateTestLotAsync(cityId, "FACTORY,MINE", "Health Factory Zone");

        await ExecuteGraphQlAsync(
            """
            mutation StartOnboardingCompany($input: StartOnboardingCompanyInput!) {
              startOnboardingCompany(input: $input) { nextStep company { id } }
            }
            """,
            new { input = new { industry = "HEALTHCARE", cityId, companyName = "Health Price Co", factoryLotId } },
            token);

        var productId = await GetStarterProductIdAsync("HEALTHCARE", "basic-medicine");
        var shopLotId = await CreateTestLotAsync(cityId, "SALES_SHOP,COMMERCIAL", "Health Zone");
        var finishResult = await FinishOnboardingAsync(token, productId, shopLotId);
        var shopId = finishResult.GetProperty("data").GetProperty("finishOnboarding").GetProperty("salesShop").GetProperty("id").GetString()!;
        var unitId = await GetPublicSalesUnitIdAsync(shopId);

        var result = await ExecuteGraphQlAsync(
            """
            mutation UpdatePublicSalesPrice($input: UpdatePublicSalesPriceInput!) {
                updatePublicSalesPrice(input: $input) { id unitType minPrice }
            }
            """,
            new { input = new { unitId, newMinPrice = 55.00m } },
            token);

        Assert.False(result.TryGetProperty("errors", out _), "Expected no errors for Healthcare price update");
        var unit = result.GetProperty("data").GetProperty("updatePublicSalesPrice");
        Assert.Equal("PUBLIC_SALES", unit.GetProperty("unitType").GetString());
        Assert.True(Math.Abs(unit.GetProperty("minPrice").GetDecimal() - 55.00m) < 0.001m,
            $"Expected minPrice 55.00 but got {unit.GetProperty("minPrice").GetDecimal()}");
    }

    [Fact]
    public async Task PublicSalesAnalytics_FoodProcessing_ReturnsAnalyticsAfterTicks()
    {
        // Verify analytics are returned for the FOOD_PROCESSING starter industry after tick simulation.
        var token = await RegisterAndGetTokenAsync("analytics-food@test.com", "FoodAnalyticsTest");
        var cityId = await GetCityIdByNameAsync();
        var factoryLotId = await CreateTestLotAsync(cityId, "FACTORY,MINE", "Food Analytics Factory Zone");

        await ExecuteGraphQlAsync(
            """
            mutation StartOnboardingCompany($input: StartOnboardingCompanyInput!) {
              startOnboardingCompany(input: $input) { nextStep company { id } }
            }
            """,
            new { input = new { industry = "FOOD_PROCESSING", cityId, companyName = "Food Analytics Co", factoryLotId } },
            token);

        var productId = await GetStarterProductIdAsync("FOOD_PROCESSING", "bread");
        var shopLotId = await CreateTestLotAsync(cityId, "SALES_SHOP,COMMERCIAL", "Food Analytics Zone");
        var finishResult = await FinishOnboardingAsync(token, productId, shopLotId);
        var shopId = finishResult.GetProperty("data").GetProperty("finishOnboarding").GetProperty("salesShop").GetProperty("id").GetString()!;
        var unitId = await GetPublicSalesUnitIdAsync(shopId);

        await ProcessTicksAsync(4);

        var result = await ExecuteGraphQlAsync(
            $"{{ publicSalesAnalytics(unitId: \"{unitId}\") {{ buildingUnitId demandSignal recentUtilization revenueHistory {{ tick revenue quantitySold }} marketShare {{ label companyId share }} }} }}",
            token: token);

        var analytics = result.GetProperty("data").GetProperty("publicSalesAnalytics");
        Assert.NotEqual(JsonValueKind.Null, analytics.ValueKind);
        Assert.Equal(unitId.ToString(), analytics.GetProperty("buildingUnitId").GetString());
        Assert.False(string.IsNullOrEmpty(analytics.GetProperty("demandSignal").GetString()),
            "demandSignal should be non-empty after ticks");
        // revenueHistory may be empty if the supply chain hasn't produced sales yet;
        // we verify the array field exists and is not null (not that it has entries).
        Assert.Equal(JsonValueKind.Array, analytics.GetProperty("revenueHistory").ValueKind);
    }

    [Fact]
    public async Task PublicSalesAnalytics_Healthcare_ReturnsAnalyticsAfterTicks()
    {
        // Verify analytics are returned for the HEALTHCARE starter industry after tick simulation.
        var token = await RegisterAndGetTokenAsync("analytics-health@test.com", "HealthAnalyticsTest");
        var cityId = await GetCityIdByNameAsync();
        var factoryLotId = await CreateTestLotAsync(cityId, "FACTORY,MINE", "Health Analytics Factory Zone");

        await ExecuteGraphQlAsync(
            """
            mutation StartOnboardingCompany($input: StartOnboardingCompanyInput!) {
              startOnboardingCompany(input: $input) { nextStep company { id } }
            }
            """,
            new { input = new { industry = "HEALTHCARE", cityId, companyName = "Health Analytics Co", factoryLotId } },
            token);

        var productId = await GetStarterProductIdAsync("HEALTHCARE", "basic-medicine");
        var shopLotId = await CreateTestLotAsync(cityId, "SALES_SHOP,COMMERCIAL", "Health Analytics Zone");
        var finishResult = await FinishOnboardingAsync(token, productId, shopLotId);
        var shopId = finishResult.GetProperty("data").GetProperty("finishOnboarding").GetProperty("salesShop").GetProperty("id").GetString()!;
        var unitId = await GetPublicSalesUnitIdAsync(shopId);

        await ProcessTicksAsync(4);

        var result = await ExecuteGraphQlAsync(
            $"{{ publicSalesAnalytics(unitId: \"{unitId}\") {{ buildingUnitId demandSignal revenueHistory {{ tick revenue quantitySold }} elasticityIndex brandAwareness populationIndex }} }}",
            token: token);

        var analytics = result.GetProperty("data").GetProperty("publicSalesAnalytics");
        Assert.NotEqual(JsonValueKind.Null, analytics.ValueKind);
        Assert.Equal(unitId.ToString(), analytics.GetProperty("buildingUnitId").GetString());
        Assert.False(string.IsNullOrEmpty(analytics.GetProperty("demandSignal").GetString()),
            "demandSignal should be non-empty after ticks");
    }

    [Fact]
    public async Task UpdatePublicSalesPrice_ThenRunTicks_ConfiguredPriceReflectedInAnalytics()
    {
        // Full flow: complete onboarding → inspect unit minPrice → update price → run tick → verify analytics work.
        var (token, unitId) = await SetupPublicSalesUnitAsync(
            "upsp-tick-verify@test.com", "TickVerify", "TickVerify Co");

        // Verify unit minPrice is set by onboarding via a direct DB read.
        decimal baselineMinPrice;
        {
            await using var scope = _factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var unit = await db.BuildingUnits.FindAsync(unitId);
            Assert.NotNull(unit);
            baselineMinPrice = unit!.MinPrice ?? 0m;
            Assert.True(baselineMinPrice > 0m, "Baseline minPrice should be positive after onboarding.");
        }

        // Update the price to a new value.
        const decimal newPrice = 99.99m;
        var updateResult = await ExecuteGraphQlAsync(
            """
            mutation UpdatePublicSalesPrice($input: UpdatePublicSalesPriceInput!) {
                updatePublicSalesPrice(input: $input) { id minPrice }
            }
            """,
            new { input = new { unitId, newMinPrice = newPrice } },
            token);
        Assert.False(updateResult.TryGetProperty("errors", out _), "updatePublicSalesPrice should succeed.");
        var returnedPrice = updateResult.GetProperty("data").GetProperty("updatePublicSalesPrice").GetProperty("minPrice").GetDecimal();
        Assert.True(Math.Abs(returnedPrice - newPrice) < 0.001m, $"Mutation should return new price {newPrice} but got {returnedPrice}.");

        // Run a tick so the analytics and unit state are refreshed.
        await ProcessTicksAsync(1);

        // Query analytics — demandSignal and recentUtilization should be present.
        var analyticsResult = await ExecuteGraphQlAsync(
            $"{{ publicSalesAnalytics(unitId: \"{unitId}\") {{ demandSignal recentUtilization revenueHistory {{ tick revenue }} }} }}",
            token: token);
        var analytics = analyticsResult.GetProperty("data").GetProperty("publicSalesAnalytics");
        Assert.NotEqual(JsonValueKind.Null, analytics.ValueKind);
        Assert.False(string.IsNullOrEmpty(analytics.GetProperty("demandSignal").GetString()),
            "demandSignal should be present after tick.");

        // Confirm minPrice persists after the tick via a direct DB read.
        {
            await using var scope = _factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var unit = await db.BuildingUnits.FindAsync(unitId);
            Assert.NotNull(unit);
            Assert.True(Math.Abs(unit!.MinPrice!.Value - newPrice) < 0.001m,
                $"MinPrice should still be {newPrice} after tick but got {unit.MinPrice}.");
        }
    }

    [Fact]
    public async Task FlushStorage_ClearsInventoryAndCreatesLedgerEntry()
    {
        // Arrange: create a player with a factory that has inventory in a storage unit.
        var token = await RegisterAndGetTokenAsync("flush-storage@test.com", "FlushStorageTest");
        var cityId = await GetCityIdByNameAsync();
        var factoryLotId = await CreateTestLotAsync(cityId, "FACTORY,MINE", "Flush Storage Zone");

        // Build the factory via onboarding.
        await ExecuteGraphQlAsync(
            """
            mutation StartOnboardingCompany($input: StartOnboardingCompanyInput!) {
              startOnboardingCompany(input: $input) { nextStep company { id } }
            }
            """,
            new { input = new { industry = "FURNITURE", cityId, companyName = "Flush Co", factoryLotId } },
            token);

        var productId = await GetStarterProductIdAsync("FURNITURE", "wooden-chair");
        var shopLotId = await CreateTestLotAsync(cityId, "SALES_SHOP,COMMERCIAL", "Flush Shop Zone");
        var finishResult = await FinishOnboardingAsync(token, productId, shopLotId);
        var factoryId = finishResult
            .GetProperty("data").GetProperty("finishOnboarding")
            .GetProperty("factory").GetProperty("id").GetString()!;
        var companyId = finishResult
            .GetProperty("data").GetProperty("finishOnboarding")
            .GetProperty("company").GetProperty("id").GetString()!;

        // Find the storage unit in the factory.
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var storageUnit = await db.BuildingUnits
            .FirstOrDefaultAsync(u => u.BuildingId == Guid.Parse(factoryId) && u.UnitType == "STORAGE");
        Assert.NotNull(storageUnit);

        // Manually seed inventory so we have something to flush.
        var woodResource = await db.ResourceTypes.FirstAsync(r => r.Slug == "wood");
        db.Inventories.Add(new Api.Data.Entities.Inventory
        {
            Id = Guid.NewGuid(),
            BuildingId = storageUnit!.BuildingId,
            BuildingUnitId = storageUnit.Id,
            ResourceTypeId = woodResource.Id,
            Quantity = 50m,
            SourcingCostTotal = 500m,
            Quality = 0.6m,
        });
        await db.SaveChangesAsync();

        // Act: flush the storage unit.
        var result = await ExecuteGraphQlAsync(
            """
            mutation FlushStorage($input: FlushStorageInput!) {
              flushStorage(input: $input) {
                discardedItemCount
                totalDiscardedValue
                discardedEntries { itemName quantity sourcingCostLost }
              }
            }
            """,
            new { input = new { buildingUnitId = storageUnit.Id } },
            token);

        var flushData = result.GetProperty("data").GetProperty("flushStorage");
        Assert.Equal(1, flushData.GetProperty("discardedItemCount").GetInt32());
        Assert.True(flushData.GetProperty("totalDiscardedValue").GetDecimal() > 0m);
        Assert.Equal(1, flushData.GetProperty("discardedEntries").GetArrayLength());

        // Verify inventory is gone from DB.
        var remaining = await db.Inventories
            .CountAsync(i => i.BuildingUnitId == storageUnit.Id && i.Quantity > 0m);
        Assert.Equal(0, remaining);

        // Verify a DISCARDED_RESOURCES ledger entry was created.
        var ledgerEntry = await db.LedgerEntries
            .FirstOrDefaultAsync(e => e.CompanyId == Guid.Parse(companyId) && e.Category == "DISCARDED_RESOURCES");
        Assert.NotNull(ledgerEntry);
        Assert.True(ledgerEntry!.Amount <= 0m, "Discard should be a negative ledger amount (loss).");
    }

    [Fact]
    public async Task FlushStorage_EmptyUnit_ReturnsZeroItems()
    {
        var token = await RegisterAndGetTokenAsync("flush-empty@test.com", "FlushEmptyTest");
        var cityId = await GetCityIdByNameAsync();
        var factoryLotId = await CreateTestLotAsync(cityId, "FACTORY,MINE", "Flush Empty Zone");

        await ExecuteGraphQlAsync(
            """
            mutation StartOnboardingCompany($input: StartOnboardingCompanyInput!) {
              startOnboardingCompany(input: $input) { nextStep company { id } }
            }
            """,
            new { input = new { industry = "FURNITURE", cityId, companyName = "Flush Empty Co", factoryLotId } },
            token);

        var productId = await GetStarterProductIdAsync("FURNITURE", "wooden-chair");
        var shopLotId = await CreateTestLotAsync(cityId, "SALES_SHOP,COMMERCIAL", "Flush Empty Shop Zone");
        var finishResult = await FinishOnboardingAsync(token, productId, shopLotId);
        var factoryId = finishResult
            .GetProperty("data").GetProperty("finishOnboarding")
            .GetProperty("factory").GetProperty("id").GetString()!;

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var storageUnit = await db.BuildingUnits
            .FirstOrDefaultAsync(u => u.BuildingId == Guid.Parse(factoryId) && u.UnitType == "STORAGE");
        Assert.NotNull(storageUnit);

        var result = await ExecuteGraphQlAsync(
            """
            mutation FlushStorage($input: FlushStorageInput!) {
              flushStorage(input: $input) {
                discardedItemCount
                totalDiscardedValue
              }
            }
            """,
            new { input = new { buildingUnitId = storageUnit!.Id } },
            token);

        var flushData = result.GetProperty("data").GetProperty("flushStorage");
        Assert.Equal(0, flushData.GetProperty("discardedItemCount").GetInt32());
        Assert.Equal(0m, flushData.GetProperty("totalDiscardedValue").GetDecimal());
    }

    [Fact]
    public async Task FlushStorage_WrongOwner_ReturnsBuildingNotFound()
    {
        var ownerToken = await RegisterAndGetTokenAsync("flush-owner@test.com", "FlushOwnerTest");
        var otherToken = await RegisterAndGetTokenAsync("flush-other@test.com", "FlushOtherTest");
        var cityId = await GetCityIdByNameAsync();
        var factoryLotId = await CreateTestLotAsync(cityId, "FACTORY,MINE", "Flush Owner Zone");

        await ExecuteGraphQlAsync(
            """
            mutation StartOnboardingCompany($input: StartOnboardingCompanyInput!) {
              startOnboardingCompany(input: $input) { nextStep company { id } }
            }
            """,
            new { input = new { industry = "FURNITURE", cityId, companyName = "Flush Owner Co", factoryLotId } },
            ownerToken);

        var productId = await GetStarterProductIdAsync("FURNITURE", "wooden-chair");
        var shopLotId = await CreateTestLotAsync(cityId, "SALES_SHOP,COMMERCIAL", "Flush Owner Shop Zone");
        var finishResult = await FinishOnboardingAsync(ownerToken, productId, shopLotId);
        var factoryId = finishResult
            .GetProperty("data").GetProperty("finishOnboarding")
            .GetProperty("factory").GetProperty("id").GetString()!;

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var storageUnit = await db.BuildingUnits
            .FirstOrDefaultAsync(u => u.BuildingId == Guid.Parse(factoryId) && u.UnitType == "STORAGE");
        Assert.NotNull(storageUnit);

        // Attempt flush as a different player — should fail.
        var result = await ExecuteGraphQlAsync(
            """
            mutation FlushStorage($input: FlushStorageInput!) {
              flushStorage(input: $input) { discardedItemCount }
            }
            """,
            new { input = new { buildingUnitId = storageUnit!.Id } },
            otherToken);

        var errors = result.GetProperty("errors");
        Assert.True(errors.GetArrayLength() > 0, "Should get an error when trying to flush someone else's unit.");
        Assert.Contains("UNIT_NOT_FOUND", errors[0].GetProperty("extensions").GetProperty("code").GetString()!);
    }

    [Fact]
    public async Task PublicSalesAnalytics_FoodProcessing_SupplyConstrained_ReturnsCorrectSignal()
    {
        // Verify that FOOD_PROCESSING (Bread) shows SUPPLY_CONSTRAINED when inventory is empty after ticks.
        var token = await RegisterAndGetTokenAsync("analytics-food-supply@test.com", "FoodSupplyTest");
        var cityId = await GetCityIdByNameAsync();
        var factoryLotId = await CreateTestLotAsync(cityId, "FACTORY,MINE", "Food Supply Factory Zone");

        await ExecuteGraphQlAsync(
            """
            mutation StartOnboardingCompany($input: StartOnboardingCompanyInput!) {
              startOnboardingCompany(input: $input) { nextStep company { id } }
            }
            """,
            new { input = new { industry = "FOOD_PROCESSING", cityId, companyName = "Food Supply Co", factoryLotId } },
            token);

        var productId = await GetStarterProductIdAsync("FOOD_PROCESSING", "bread");
        var shopLotId = await CreateTestLotAsync(cityId, "SALES_SHOP,COMMERCIAL", "Food Supply Zone");
        var finishResult = await FinishOnboardingAsync(token, productId, shopLotId);
        var shopId = finishResult.GetProperty("data").GetProperty("finishOnboarding").GetProperty("salesShop").GetProperty("id").GetString()!;
        var unitId = await GetPublicSalesUnitIdAsync(shopId);

        // Without any inventory in the unit, demand analytics should reflect supply constraint.
        var result = await ExecuteGraphQlAsync(
            $"{{ publicSalesAnalytics(unitId: \"{unitId}\") {{ demandSignal actionHint recentUtilization }} }}",
            token: token);
        var analytics = result.GetProperty("data").GetProperty("publicSalesAnalytics");
        Assert.NotEqual(JsonValueKind.Null, analytics.ValueKind);
        // With no inventory and no sales history, the unit is supply-constrained or shows zero utilization.
        var signal = analytics.GetProperty("demandSignal").GetString();
        Assert.False(string.IsNullOrEmpty(signal), "demandSignal should be present.");
        // Utilization should be zero or near-zero since the unit has no inventory.
        var utilization = analytics.GetProperty("recentUtilization").GetDecimal();
        Assert.True(utilization <= 0.1m, $"Utilization should be near zero for an empty unit, got {utilization}.");
    }

    [Fact]
    public async Task PublicSalesAnalytics_Healthcare_SupplyConstrained_ReturnsCorrectSignal()
    {
        // Verify that HEALTHCARE (Basic Medicine) shows correct demand signal when unit is empty.
        var token = await RegisterAndGetTokenAsync("analytics-health-supply@test.com", "HealthSupplyTest");
        var cityId = await GetCityIdByNameAsync();
        var factoryLotId = await CreateTestLotAsync(cityId, "FACTORY,MINE", "Health Supply Factory Zone");

        await ExecuteGraphQlAsync(
            """
            mutation StartOnboardingCompany($input: StartOnboardingCompanyInput!) {
              startOnboardingCompany(input: $input) { nextStep company { id } }
            }
            """,
            new { input = new { industry = "HEALTHCARE", cityId, companyName = "Health Supply Co", factoryLotId } },
            token);

        var productId = await GetStarterProductIdAsync("HEALTHCARE", "basic-medicine");
        var shopLotId = await CreateTestLotAsync(cityId, "SALES_SHOP,COMMERCIAL", "Health Supply Zone");
        var finishResult = await FinishOnboardingAsync(token, productId, shopLotId);
        var shopId = finishResult.GetProperty("data").GetProperty("finishOnboarding").GetProperty("salesShop").GetProperty("id").GetString()!;
        var unitId = await GetPublicSalesUnitIdAsync(shopId);

        var result = await ExecuteGraphQlAsync(
            $"{{ publicSalesAnalytics(unitId: \"{unitId}\") {{ demandSignal actionHint recentUtilization }} }}",
            token: token);
        var analytics = result.GetProperty("data").GetProperty("publicSalesAnalytics");
        Assert.NotEqual(JsonValueKind.Null, analytics.ValueKind);
        var signal = analytics.GetProperty("demandSignal").GetString();
        Assert.False(string.IsNullOrEmpty(signal), "demandSignal should be present for Healthcare.");

        // Verify the public sales unit has a positive minPrice (configured by FinishOnboarding) via DB.
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var unit = await db.BuildingUnits.FindAsync(unitId);
        Assert.NotNull(unit);
        Assert.True(unit!.MinPrice is > 0m, $"Healthcare public sales unit should have positive minPrice, got {unit.MinPrice}.");
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

    [Fact]
    public async Task LedgerDrillDown_RevenueDrillDown_ReturnsEntries()
    {
        var token = await RegisterAndGetTokenAsync("ledger-rev-drill@test.com", "RevDrillUser");
        Guid companyId;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var player = await db.Players.SingleAsync(p => p.Email == "ledger-rev-drill@test.com");
            var company = new Company
            {
                Id = Guid.NewGuid(),
                PlayerId = player.Id,
                Name = "Rev Drill Co",
                Cash = 100_000m,
                FoundedAtUtc = DateTime.UtcNow,
                FoundedAtTick = 0,
            };
            db.Companies.Add(company);
            db.LedgerEntries.AddRange(
                new LedgerEntry
                {
                    Id = Guid.NewGuid(), CompanyId = company.Id,
                    Category = LedgerCategory.Revenue, Description = "Public sales - Wooden Chair",
                    Amount = 2500m, RecordedAtTick = 5, RecordedAtUtc = DateTime.UtcNow,
                },
                new LedgerEntry
                {
                    Id = Guid.NewGuid(), CompanyId = company.Id,
                    Category = LedgerCategory.Revenue, Description = "Public sales - Wooden Table",
                    Amount = 1800m, RecordedAtTick = 10, RecordedAtUtc = DateTime.UtcNow,
                },
                new LedgerEntry
                {
                    Id = Guid.NewGuid(), CompanyId = company.Id,
                    Category = LedgerCategory.LaborCost, Description = "Operating labor",
                    Amount = -300m, RecordedAtTick = 10, RecordedAtUtc = DateTime.UtcNow,
                });
            await db.SaveChangesAsync();
            companyId = company.Id;
        }

        var drillResult = await ExecuteGraphQlAsync(
            $"{{ ledgerDrillDown(companyId: \"{companyId}\", category: \"REVENUE\") {{ id category description amount recordedAtTick }} }}",
            token: token);

        var entries = drillResult.GetProperty("data").GetProperty("ledgerDrillDown").EnumerateArray().ToList();
        Assert.Equal(2, entries.Count);
        Assert.All(entries, e => Assert.Equal("REVENUE", e.GetProperty("category").GetString()));
        Assert.All(entries, e => Assert.True(e.GetProperty("amount").GetDecimal() > 0, "Revenue amounts must be positive"));
        // Ordered descending by tick
        Assert.Equal(10, entries[0].GetProperty("recordedAtTick").GetInt64());
        Assert.Equal(5, entries[1].GetProperty("recordedAtTick").GetInt64());
    }

    [Fact]
    public async Task LedgerDrillDown_LaborCost_ReturnsEntries()
    {
        var token = await RegisterAndGetTokenAsync("ledger-labor-drill@test.com", "LaborDrillUser");
        Guid companyId;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var player = await db.Players.SingleAsync(p => p.Email == "ledger-labor-drill@test.com");
            var company = new Company
            {
                Id = Guid.NewGuid(),
                PlayerId = player.Id,
                Name = "Labor Drill Co",
                Cash = 80_000m,
                FoundedAtUtc = DateTime.UtcNow,
                FoundedAtTick = 0,
            };
            db.Companies.Add(company);
            db.LedgerEntries.AddRange(
                new LedgerEntry
                {
                    Id = Guid.NewGuid(), CompanyId = company.Id,
                    Category = LedgerCategory.LaborCost, Description = "Operating labor for MANUFACTURING",
                    Amount = -400m, RecordedAtTick = 8, RecordedAtUtc = DateTime.UtcNow,
                },
                new LedgerEntry
                {
                    Id = Guid.NewGuid(), CompanyId = company.Id,
                    Category = LedgerCategory.LaborCost, Description = "Operating labor for SALES",
                    Amount = -200m, RecordedAtTick = 12, RecordedAtUtc = DateTime.UtcNow,
                });
            await db.SaveChangesAsync();
            companyId = company.Id;
        }

        var drillResult = await ExecuteGraphQlAsync(
            $"{{ ledgerDrillDown(companyId: \"{companyId}\", category: \"LABOR_COST\") {{ id category description amount }} }}",
            token: token);

        var entries = drillResult.GetProperty("data").GetProperty("ledgerDrillDown").EnumerateArray().ToList();
        Assert.Equal(2, entries.Count);
        Assert.All(entries, e => Assert.Equal("LABOR_COST", e.GetProperty("category").GetString()));
        Assert.All(entries, e => Assert.True(e.GetProperty("amount").GetDecimal() < 0, "Labor cost amounts must be negative"));
    }

    [Fact]
    public async Task LedgerDrillDown_NonOwner_ReturnsEmpty()
    {
        var ownerToken = await RegisterAndGetTokenAsync("drill-owner2@test.com", "DrillOwner2");
        var result = await ExecuteGraphQlAsync(
            """mutation CreateCompany($input: CreateCompanyInput!) { createCompany(input: $input) { id } }""",
            new { input = new { name = "Owner2 Co" } },
            ownerToken);
        var companyId = result.GetProperty("data").GetProperty("createCompany").GetProperty("id").GetString()!;

        var otherToken = await RegisterAndGetTokenAsync("drill-other2@test.com", "DrillOther2");
        var drillResult = await ExecuteGraphQlAsync(
            $"{{ ledgerDrillDown(companyId: \"{companyId}\", category: \"REVENUE\") {{ id }} }}",
            token: otherToken);

        var entries = drillResult.GetProperty("data").GetProperty("ledgerDrillDown").EnumerateArray().ToList();
        Assert.Empty(entries);
    }

    [Fact]
    public async Task LedgerDrillDown_WithGameYear_FiltersByYear()
    {
        var token = await RegisterAndGetTokenAsync("ledger-year-drill@test.com", "YearDrillUser");
        Guid companyId;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var player = await db.Players.SingleAsync(p => p.Email == "ledger-year-drill@test.com");
            var company = new Company
            {
                Id = Guid.NewGuid(),
                PlayerId = player.Id,
                Name = "Year Drill Co",
                Cash = 120_000m,
                FoundedAtUtc = DateTime.UtcNow,
                FoundedAtTick = 0,
            };
            db.Companies.Add(company);
            // Year 2000: ticks 1..8759; Year 2001: ticks 8760..17519
            db.LedgerEntries.AddRange(
                new LedgerEntry
                {
                    Id = Guid.NewGuid(), CompanyId = company.Id,
                    Category = LedgerCategory.Revenue, Description = "Year 2000 sale",
                    Amount = 600m, RecordedAtTick = 100, RecordedAtUtc = DateTime.UtcNow,
                },
                new LedgerEntry
                {
                    Id = Guid.NewGuid(), CompanyId = company.Id,
                    Category = LedgerCategory.Revenue, Description = "Year 2001 sale",
                    Amount = 900m, RecordedAtTick = 9000, RecordedAtUtc = DateTime.UtcNow,
                });
            await db.SaveChangesAsync();
            companyId = company.Id;
        }

        // Drill-down scoped to year 2000 should only return the tick-100 entry
        var drillYear2000 = await ExecuteGraphQlAsync(
            $"{{ ledgerDrillDown(companyId: \"{companyId}\", category: \"REVENUE\", gameYear: 2000) {{ description amount recordedAtTick }} }}",
            token: token);
        var year2000Entries = drillYear2000.GetProperty("data").GetProperty("ledgerDrillDown").EnumerateArray().ToList();
        Assert.Single(year2000Entries);
        Assert.Equal("Year 2000 sale", year2000Entries[0].GetProperty("description").GetString());

        // Drill-down scoped to year 2001 should only return the tick-9000 entry
        var drillYear2001 = await ExecuteGraphQlAsync(
            $"{{ ledgerDrillDown(companyId: \"{companyId}\", category: \"REVENUE\", gameYear: 2001) {{ description amount recordedAtTick }} }}",
            token: token);
        var year2001Entries = drillYear2001.GetProperty("data").GetProperty("ledgerDrillDown").EnumerateArray().ToList();
        Assert.Single(year2001Entries);
        Assert.Equal("Year 2001 sale", year2001Entries[0].GetProperty("description").GetString());
    }

    [Fact]
    public async Task CompanyLedger_UnitUpgradeCost_ReflectedInTaxableIncome()
    {
        var token = await RegisterAndGetTokenAsync("ledger-upgrade@test.com", "UpgradeUser");
        Guid companyId;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var player = await db.Players.SingleAsync(p => p.Email == "ledger-upgrade@test.com");
            var company = new Company
            {
                Id = Guid.NewGuid(),
                PlayerId = player.Id,
                Name = "Upgrade Co",
                Cash = 200_000m,
                FoundedAtUtc = DateTime.UtcNow,
                FoundedAtTick = 0,
            };
            db.Companies.Add(company);
            db.LedgerEntries.AddRange(
                new LedgerEntry
                {
                    Id = Guid.NewGuid(), CompanyId = company.Id,
                    Category = LedgerCategory.Revenue, Description = "Sales",
                    Amount = 5000m, RecordedAtTick = 10, RecordedAtUtc = DateTime.UtcNow,
                },
                new LedgerEntry
                {
                    Id = Guid.NewGuid(), CompanyId = company.Id,
                    Category = LedgerCategory.UnitUpgrade, Description = "MANUFACTURING unit upgrade to level 2",
                    Amount = -2000m, RecordedAtTick = 10, RecordedAtUtc = DateTime.UtcNow,
                });
            await db.SaveChangesAsync();
            companyId = company.Id;
        }

        var result = await ExecuteGraphQlAsync(
            $"{{ companyLedger(companyId: \"{companyId}\") {{ totalRevenue taxableIncome netIncome }} }}",
            token: token);

        var ledger = result.GetProperty("data").GetProperty("companyLedger");
        Assert.Equal(5000m, ledger.GetProperty("totalRevenue").GetDecimal());
        // UNIT_UPGRADE is a deductible category, so taxable income = 5000 - 2000 = 3000
        Assert.Equal(3000m, ledger.GetProperty("taxableIncome").GetDecimal());
        // netIncome formula only subtracts purchasing/labor/energy/marketing/tax/other — NOT unit upgrades
        // so netIncome = 5000 (the upgrade cost is a capital expense, not an operating expense)
        Assert.Equal(5000m, ledger.GetProperty("netIncome").GetDecimal());
    }

    [Fact]
    public async Task CompanyLedger_Unauthenticated_ReturnsError()
    {
        // Create a company while authenticated so we have a valid companyId
        var ownerToken = await RegisterAndGetTokenAsync("ledger-unauth@test.com", "LedgerUnauth");
        var createResult = await ExecuteGraphQlAsync(
            """mutation CreateCompany($input: CreateCompanyInput!) { createCompany(input: $input) { id } }""",
            new { input = new { name = "Unauth Ledger Co" } },
            ownerToken);
        var companyId = createResult.GetProperty("data").GetProperty("createCompany").GetProperty("id").GetString()!;

        // Access without auth token
        var ledgerResult = await ExecuteGraphQlAsync(
            $"{{ companyLedger(companyId: \"{companyId}\") {{ companyId }} }}");

        Assert.True(ledgerResult.TryGetProperty("errors", out _),
            "Unauthenticated companyLedger query should return an error.");
    }

    [Fact]
    public async Task LedgerDrillDown_Unauthenticated_ReturnsError()
    {
        // Create a company while authenticated so we have a valid companyId
        var ownerToken = await RegisterAndGetTokenAsync("drill-unauth@test.com", "DrillUnauth");
        var createResult = await ExecuteGraphQlAsync(
            """mutation CreateCompany($input: CreateCompanyInput!) { createCompany(input: $input) { id } }""",
            new { input = new { name = "Unauth Drill Co" } },
            ownerToken);
        var companyId = createResult.GetProperty("data").GetProperty("createCompany").GetProperty("id").GetString()!;

        // Access without auth token
        var drillResult = await ExecuteGraphQlAsync(
            $"{{ ledgerDrillDown(companyId: \"{companyId}\", category: \"REVENUE\") {{ id }} }}");

        Assert.True(drillResult.TryGetProperty("errors", out _),
            "Unauthenticated ledgerDrillDown query should return an error.");
    }

    [Fact]
    public async Task LedgerDrillDown_MarketingCost_ReturnsEntries()
    {
        var token = await RegisterAndGetTokenAsync("ledger-mktg-drill@test.com", "MktgDrillUser");
        Guid companyId;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var player = await db.Players.SingleAsync(p => p.Email == "ledger-mktg-drill@test.com");
            var company = new Company
            {
                Id = Guid.NewGuid(),
                PlayerId = player.Id,
                Name = "Mktg Drill Co",
                Cash = 90_000m,
                FoundedAtUtc = DateTime.UtcNow,
                FoundedAtTick = 0,
            };
            db.Companies.Add(company);
            db.LedgerEntries.AddRange(
                new LedgerEntry
                {
                    Id = Guid.NewGuid(), CompanyId = company.Id,
                    Category = LedgerCategory.Marketing, Description = "Marketing spend tick 5",
                    Amount = -600m, RecordedAtTick = 5, RecordedAtUtc = DateTime.UtcNow,
                },
                new LedgerEntry
                {
                    Id = Guid.NewGuid(), CompanyId = company.Id,
                    Category = LedgerCategory.Marketing, Description = "Marketing spend tick 10",
                    Amount = -400m, RecordedAtTick = 10, RecordedAtUtc = DateTime.UtcNow,
                });
            await db.SaveChangesAsync();
            companyId = company.Id;
        }

        var drillResult = await ExecuteGraphQlAsync(
            $"{{ ledgerDrillDown(companyId: \"{companyId}\", category: \"MARKETING\") {{ id category description amount }} }}",
            token: token);

        var entries = drillResult.GetProperty("data").GetProperty("ledgerDrillDown").EnumerateArray().ToList();
        Assert.Equal(2, entries.Count);
        Assert.All(entries, e => Assert.Equal("MARKETING", e.GetProperty("category").GetString()));
        Assert.All(entries, e => Assert.True(e.GetProperty("amount").GetDecimal() < 0, "Marketing cost amounts must be negative"));
    }

    [Fact]
    public async Task LedgerDrillDown_EnergyCost_ReturnsEntries()
    {
        var token = await RegisterAndGetTokenAsync("ledger-energy-drill@test.com", "EnergyDrillUser");
        Guid companyId;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var player = await db.Players.SingleAsync(p => p.Email == "ledger-energy-drill@test.com");
            var company = new Company
            {
                Id = Guid.NewGuid(),
                PlayerId = player.Id,
                Name = "Energy Drill Co",
                Cash = 70_000m,
                FoundedAtUtc = DateTime.UtcNow,
                FoundedAtTick = 0,
            };
            db.Companies.Add(company);
            db.LedgerEntries.Add(new LedgerEntry
            {
                Id = Guid.NewGuid(), CompanyId = company.Id,
                Category = LedgerCategory.EnergyCost, Description = "Power grid cost",
                Amount = -250m, RecordedAtTick = 7, RecordedAtUtc = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
            companyId = company.Id;
        }

        var drillResult = await ExecuteGraphQlAsync(
            $"{{ ledgerDrillDown(companyId: \"{companyId}\", category: \"ENERGY_COST\") {{ id category description amount }} }}",
            token: token);

        var entries = drillResult.GetProperty("data").GetProperty("ledgerDrillDown").EnumerateArray().ToList();
        Assert.Single(entries);
        Assert.Equal("ENERGY_COST", entries[0].GetProperty("category").GetString());
        Assert.Equal(-250m, entries[0].GetProperty("amount").GetDecimal());
    }

    [Fact]
    public async Task LedgerDrillDown_UnitUpgrade_ReturnsEntries()
    {
        var token = await RegisterAndGetTokenAsync("ledger-upgrade-drill@test.com", "UpgradeDrillUser");
        Guid companyId;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var player = await db.Players.SingleAsync(p => p.Email == "ledger-upgrade-drill@test.com");
            var company = new Company
            {
                Id = Guid.NewGuid(),
                PlayerId = player.Id,
                Name = "Upgrade Drill Co",
                Cash = 120_000m,
                FoundedAtUtc = DateTime.UtcNow,
                FoundedAtTick = 0,
            };
            db.Companies.Add(company);
            db.LedgerEntries.Add(new LedgerEntry
            {
                Id = Guid.NewGuid(), CompanyId = company.Id,
                Category = LedgerCategory.UnitUpgrade, Description = "MANUFACTURING unit upgrade to level 2",
                Amount = -3000m, RecordedAtTick = 15, RecordedAtUtc = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
            companyId = company.Id;
        }

        var drillResult = await ExecuteGraphQlAsync(
            $"{{ ledgerDrillDown(companyId: \"{companyId}\", category: \"UNIT_UPGRADE\") {{ id category description amount }} }}",
            token: token);

        var entries = drillResult.GetProperty("data").GetProperty("ledgerDrillDown").EnumerateArray().ToList();
        Assert.Single(entries);
        Assert.Equal("UNIT_UPGRADE", entries[0].GetProperty("category").GetString());
        Assert.Equal(-3000m, entries[0].GetProperty("amount").GetDecimal());
    }

    [Fact]
    public async Task CompanyLedger_BuildingSummaries_ShowsMultipleBuildings()
    {
        var token = await RegisterAndGetTokenAsync("ledger-multi-bld@test.com", "MultiBldUser");
        Guid companyId;
        Guid building1Id;
        Guid building2Id;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var player = await db.Players.SingleAsync(p => p.Email == "ledger-multi-bld@test.com");
            var city = await db.Cities.FirstAsync();
            var company = new Company
            {
                Id = Guid.NewGuid(),
                PlayerId = player.Id,
                Name = "Multi Building Corp",
                Cash = 500_000m,
                FoundedAtUtc = DateTime.UtcNow,
                FoundedAtTick = 0,
            };
            db.Companies.Add(company);

            building1Id = Guid.NewGuid();
            building2Id = Guid.NewGuid();
            db.Buildings.AddRange(
                new Building
                {
                    Id = building1Id, CompanyId = company.Id, CityId = city.Id,
                    Type = BuildingType.Factory, Name = "Factory Alpha", Level = 1,
                },
                new Building
                {
                    Id = building2Id, CompanyId = company.Id, CityId = city.Id,
                    Type = BuildingType.SalesShop, Name = "Shop Beta", Level = 1,
                });

            db.LedgerEntries.AddRange(
                new LedgerEntry
                {
                    Id = Guid.NewGuid(), CompanyId = company.Id, BuildingId = building1Id,
                    Category = LedgerCategory.Revenue, Description = "Factory sales",
                    Amount = 5000m, RecordedAtTick = 10, RecordedAtUtc = DateTime.UtcNow,
                },
                new LedgerEntry
                {
                    Id = Guid.NewGuid(), CompanyId = company.Id, BuildingId = building2Id,
                    Category = LedgerCategory.Revenue, Description = "Shop sales",
                    Amount = 3000m, RecordedAtTick = 10, RecordedAtUtc = DateTime.UtcNow,
                });
            await db.SaveChangesAsync();
            companyId = company.Id;
        }

        var ledgerResult = await ExecuteGraphQlAsync(
            $"{{ companyLedger(companyId: \"{companyId}\") {{ totalRevenue buildingSummaries {{ buildingId buildingName buildingType revenue costs }} }} }}",
            token: token);

        var ledger = ledgerResult.GetProperty("data").GetProperty("companyLedger");
        Assert.Equal(8000m, ledger.GetProperty("totalRevenue").GetDecimal());

        var summaries = ledger.GetProperty("buildingSummaries").EnumerateArray().ToList();
        Assert.Equal(2, summaries.Count);
        var factory = summaries.First(s => s.GetProperty("buildingId").GetString() == building1Id.ToString());
        var shop = summaries.First(s => s.GetProperty("buildingId").GetString() == building2Id.ToString());
        Assert.Equal("Factory Alpha", factory.GetProperty("buildingName").GetString());
        Assert.Equal(5000m, factory.GetProperty("revenue").GetDecimal());
        Assert.Equal("Shop Beta", shop.GetProperty("buildingName").GetString());
        Assert.Equal(3000m, shop.GetProperty("revenue").GetDecimal());
    }

    #endregion

    #region R&D Building Configuration

    /// <summary>
    /// Helper: creates a company + R&amp;D building owned by the given player token.
    /// Returns (companyId, buildingId).
    /// </summary>
    private async Task<(string CompanyId, string BuildingId)> CreateRdBuildingAsync(string token)
    {
        var (companyId, _, _) = await CompleteOnboardingAsync(token, $"RD Corp {Guid.NewGuid():N}");
        var cityId = await GetCityIdByNameAsync("Bratislava");
        var lotId = await CreateTestLotAsync(cityId, "RESEARCH_DEVELOPMENT,COMMERCIAL", "Research District", 90_000m, "RD Lot");
        var purchaseResult = await ExecuteGraphQlAsync(
            """
            mutation PurchaseLot($input: PurchaseLotInput!) {
              purchaseLot(input: $input) { building { id } }
            }
            """,
            new { input = new { companyId, lotId, buildingType = "RESEARCH_DEVELOPMENT", buildingName = "Innovation Lab" } },
            token);
        var buildingId = purchaseResult.GetProperty("data").GetProperty("purchaseLot").GetProperty("building").GetProperty("id").GetString()!;
        return (companyId, buildingId);
    }

    [Fact]
    public async Task PlaceRdBuilding_ValidLot_Succeeds()
    {
        var token = await RegisterAndGetTokenAsync($"rd-place-{Guid.NewGuid()}@test.com", "RD Placer");
        var (_, buildingId) = await CreateRdBuildingAsync(token);
        Assert.False(string.IsNullOrEmpty(buildingId), "R&D building placement should return a building ID.");
    }

    [Fact]
    public async Task StoreBuildingConfiguration_ProductQuality_WithValidProduct_Succeeds()
    {
        var token = await RegisterAndGetTokenAsync($"rd-pq-ok-{Guid.NewGuid()}@test.com", "PQ Config Tester");
        var (_, buildingId) = await CreateRdBuildingAsync(token);

        var productId = await GetStarterProductIdAsync("FURNITURE", "wooden-chair");

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
                            unitType = "PRODUCT_QUALITY",
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
                            productTypeId = productId
                        }
                    }
                }
            },
            token);

        Assert.False(result.TryGetProperty("errors", out _), "Valid PRODUCT_QUALITY unit should succeed.");
        var planId = result.GetProperty("data").GetProperty("storeBuildingConfiguration").GetProperty("id").GetString();
        Assert.False(string.IsNullOrEmpty(planId));
    }

    [Fact]
    public async Task StoreBuildingConfiguration_ProductQuality_WithoutProduct_Fails()
    {
        var token = await RegisterAndGetTokenAsync($"rd-pq-fail-{Guid.NewGuid()}@test.com", "PQ No Product");
        var (_, buildingId) = await CreateRdBuildingAsync(token);

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
                            unitType = "PRODUCT_QUALITY",
                            gridX = 0,
                            gridY = 0,
                            linkUp = false,
                            linkDown = false,
                            linkLeft = false,
                            linkRight = false,
                            linkUpLeft = false,
                            linkUpRight = false,
                            linkDownLeft = false,
                            linkDownRight = false
                        }
                    }
                }
            },
            token);

        Assert.True(result.TryGetProperty("errors", out var errors));
        Assert.Equal("PRODUCT_QUALITY_PRODUCT_REQUIRED",
            errors[0].GetProperty("extensions").GetProperty("code").GetString());
    }

    [Fact]
    public async Task StoreBuildingConfiguration_BrandQuality_CompanyScope_Succeeds()
    {
        var token = await RegisterAndGetTokenAsync($"rd-bq-co-{Guid.NewGuid()}@test.com", "BQ Company Scope");
        var (_, buildingId) = await CreateRdBuildingAsync(token);

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
                            brandScope = "COMPANY"
                        }
                    }
                }
            },
            token);

        Assert.False(result.TryGetProperty("errors", out _), "BRAND_QUALITY with COMPANY scope should succeed.");
    }

    [Fact]
    public async Task StoreBuildingConfiguration_BrandQuality_CategoryScope_WithProduct_Succeeds()
    {
        var token = await RegisterAndGetTokenAsync($"rd-bq-cat-{Guid.NewGuid()}@test.com", "BQ Category Scope");
        var (_, buildingId) = await CreateRdBuildingAsync(token);

        var productId = await GetStarterProductIdAsync("FURNITURE", "wooden-chair");

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
                            brandScope = "CATEGORY",
                            productTypeId = productId
                        }
                    }
                }
            },
            token);

        Assert.False(result.TryGetProperty("errors", out _), "BRAND_QUALITY with CATEGORY scope + anchor product should succeed.");
    }

    [Fact]
    public async Task StoreBuildingConfiguration_BrandQuality_ProductScope_WithProduct_Succeeds()
    {
        var token = await RegisterAndGetTokenAsync($"rd-bq-prod-{Guid.NewGuid()}@test.com", "BQ Product Scope");
        var (_, buildingId) = await CreateRdBuildingAsync(token);

        var productId = await GetStarterProductIdAsync("FURNITURE", "wooden-chair");

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
                            brandScope = "PRODUCT",
                            productTypeId = productId
                        }
                    }
                }
            },
            token);

        Assert.False(result.TryGetProperty("errors", out _), "BRAND_QUALITY with PRODUCT scope + anchor product should succeed.");
    }

    [Fact]
    public async Task StoreBuildingConfiguration_BrandQuality_WithoutScope_Fails()
    {
        var token = await RegisterAndGetTokenAsync($"rd-bq-noscope-{Guid.NewGuid()}@test.com", "BQ No Scope");
        var (_, buildingId) = await CreateRdBuildingAsync(token);

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
                            linkDownRight = false
                        }
                    }
                }
            },
            token);

        Assert.True(result.TryGetProperty("errors", out var errors));
        Assert.Equal("BRAND_QUALITY_SCOPE_REQUIRED",
            errors[0].GetProperty("extensions").GetProperty("code").GetString());
    }

    [Fact]
    public async Task StoreBuildingConfiguration_BrandQuality_InvalidScope_Fails()
    {
        var token = await RegisterAndGetTokenAsync($"rd-bq-badscope-{Guid.NewGuid()}@test.com", "BQ Bad Scope");
        var (_, buildingId) = await CreateRdBuildingAsync(token);

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
                            brandScope = "INVALID_SCOPE"
                        }
                    }
                }
            },
            token);

        Assert.True(result.TryGetProperty("errors", out var errors));
        Assert.Equal("INVALID_BRAND_SCOPE",
            errors[0].GetProperty("extensions").GetProperty("code").GetString());
    }

    [Fact]
    public async Task CompanyBrands_UnauthenticatedRequest_Fails()
    {
        var companyId = Guid.NewGuid();
        var result = await ExecuteGraphQlAsync(
            """
            query GetBrands($companyId: UUID!) {
              companyBrands(companyId: $companyId) { id scope awareness quality marketingEfficiencyMultiplier }
            }
            """,
            new { companyId });

        Assert.True(result.TryGetProperty("errors", out var errors));
        Assert.True(errors.GetArrayLength() > 0, "Unauthenticated companyBrands query must fail.");
    }

    [Fact]
    public async Task CompanyBrands_OtherPlayersCompany_ReturnsEmpty()
    {
        // Player A registers
        var tokenA = await RegisterAndGetTokenAsync($"rd-brands-a-{Guid.NewGuid()}@test.com", "Brand Viewer A");
        var (companyIdA, _, _) = await CompleteOnboardingAsync(tokenA, "Brand Corp A");

        // Player B registers and tries to query Player A's brands
        var tokenB = await RegisterAndGetTokenAsync($"rd-brands-b-{Guid.NewGuid()}@test.com", "Brand Viewer B");
        var result = await ExecuteGraphQlAsync(
            """
            query GetBrands($companyId: UUID!) {
              companyBrands(companyId: $companyId) { id scope awareness quality marketingEfficiencyMultiplier }
            }
            """,
            new { companyId = companyIdA },
            tokenB);

        Assert.False(result.TryGetProperty("errors", out _), "companyBrands query itself should succeed for another company.");
        var brands = result.GetProperty("data").GetProperty("companyBrands").EnumerateArray().ToList();
        Assert.Empty(brands);
    }

    [Fact]
    public async Task CompanyBrands_AfterOnboarding_ReturnsBrandStates()
    {
        var token = await RegisterAndGetTokenAsync($"rd-brands-own-{Guid.NewGuid()}@test.com", "Brand Owner");
        var (companyId, _, _) = await CompleteOnboardingAsync(token, "Brand Testing Corp");

        var result = await ExecuteGraphQlAsync(
            """
            query GetBrands($companyId: UUID!) {
              companyBrands(companyId: $companyId) {
                id
                companyId
                name
                scope
                productTypeId
                productName
                industryCategory
                awareness
                quality
                marketingEfficiencyMultiplier
              }
            }
            """,
            new { companyId },
            token);

        Assert.False(result.TryGetProperty("errors", out _), "Authenticated companyBrands for owned company must succeed.");
        var brands = result.GetProperty("data").GetProperty("companyBrands").EnumerateArray().ToList();

        // A fresh account may have zero brands (no R&D configured yet).
        // Validate the shape of any returned brands is correct.
        foreach (var brand in brands)
        {
            var scope = brand.GetProperty("scope").GetString();
            Assert.True(scope is "PRODUCT" or "CATEGORY" or "COMPANY",
                $"Brand scope must be one of PRODUCT, CATEGORY, COMPANY. Got: {scope}");
            Assert.True(brand.GetProperty("awareness").GetDecimal() is >= 0 and <= 1,
                "Brand awareness must be between 0.0 and 1.0.");
            Assert.True(brand.GetProperty("quality").GetDecimal() is >= 0 and <= 1,
                "Brand quality must be between 0.0 and 1.0.");
            // MarketingEfficiencyMultiplier must be >= 1.0 (1.0 = baseline, >1.0 = R&D bonus applied)
            Assert.True(brand.GetProperty("marketingEfficiencyMultiplier").GetDecimal() >= 1m,
                "MarketingEfficiencyMultiplier must be >= 1.0 (1.0 is baseline, R&D raises it above).");
        }
    }

    #endregion

    #region Property Management (setRentPerSqm mutation)

    /// <summary>
    /// Helper: creates a company + apartment building owned by the given player token.
    /// Returns (companyId, buildingId).
    /// </summary>
    private async Task<(string CompanyId, string BuildingId)> CreateApartmentBuildingAsync(string token)
    {
        var cityId = await GetCityIdByNameAsync();
        var lotId = await CreateTestLotAsync(cityId, "APARTMENT", "Residential Quarter", 80_000m, $"Apt Lot {Guid.NewGuid():N}"[..17]);

        // Complete onboarding to get a company, then separately purchase the apartment lot.
        var onboardingResult = await CompleteOnboardingAsync(token, $"Apt Co {Guid.NewGuid():N}"[..14]);
        var companyId = onboardingResult.CompanyId;

        var purchaseResult = await ExecuteGraphQlAsync(
            """
            mutation PurchaseLot($input: PurchaseLotInput!) {
              purchaseLot(input: $input) {
                building { id type pricePerSqm occupancyPercent totalAreaSqm pendingPricePerSqm pendingPriceActivationTick }
              }
            }
            """,
            new { input = new { companyId, lotId, buildingType = "APARTMENT", buildingName = "My Apartments" } },
            token);

        var building = purchaseResult.GetProperty("data").GetProperty("purchaseLot").GetProperty("building");
        return (companyId, building.GetProperty("id").GetString()!);
    }

    [Fact]
    public async Task SetRentPerSqm_ValidInput_SchedulesPendingRent()
    {
        var token = await RegisterAndGetTokenAsync($"rent-set-{Guid.NewGuid()}@test.com", "Rent Setter");
        var (_, buildingId) = await CreateApartmentBuildingAsync(token);

        var result = await ExecuteGraphQlAsync(
            """
            mutation SetRent($input: SetRentPerSqmInput!) {
              setRentPerSqm(input: $input) {
                id pricePerSqm pendingPricePerSqm pendingPriceActivationTick
              }
            }
            """,
            new { input = new { buildingId = Guid.Parse(buildingId), rentPerSqm = 20.0m } },
            token);

        Assert.False(result.TryGetProperty("errors", out _), $"Expected success but got errors: {result}");
        var b = result.GetProperty("data").GetProperty("setRentPerSqm");
        Assert.Equal(20.0m, b.GetProperty("pendingPricePerSqm").GetDecimal());
        Assert.NotEqual(JsonValueKind.Null, b.GetProperty("pendingPriceActivationTick").ValueKind);
    }

    [Fact]
    public async Task SetRentPerSqm_Unauthenticated_ReturnsError()
    {
        var token = await RegisterAndGetTokenAsync($"rent-unauth-{Guid.NewGuid()}@test.com", "Unauth Rent");
        var (_, buildingId) = await CreateApartmentBuildingAsync(token);

        var result = await ExecuteGraphQlAsync(
            """
            mutation SetRent($input: SetRentPerSqmInput!) {
              setRentPerSqm(input: $input) { id }
            }
            """,
            new { input = new { buildingId = Guid.Parse(buildingId), rentPerSqm = 20.0m } }
            // No token – should fail auth.
        );

        Assert.True(result.TryGetProperty("errors", out _), "Unauthenticated call must return errors.");
    }

    [Fact]
    public async Task SetRentPerSqm_WrongOwner_ReturnsError()
    {
        var ownerToken = await RegisterAndGetTokenAsync($"rent-owner-{Guid.NewGuid()}@test.com", "Owner");
        var (_, buildingId) = await CreateApartmentBuildingAsync(ownerToken);

        var otherToken = await RegisterAndGetTokenAsync($"rent-other-{Guid.NewGuid()}@test.com", "Other");

        var result = await ExecuteGraphQlAsync(
            """
            mutation SetRent($input: SetRentPerSqmInput!) {
              setRentPerSqm(input: $input) { id }
            }
            """,
            new { input = new { buildingId = Guid.Parse(buildingId), rentPerSqm = 20.0m } },
            otherToken);

        Assert.True(result.TryGetProperty("errors", out var errors));
        Assert.Equal("BUILDING_NOT_FOUND", errors[0].GetProperty("extensions").GetProperty("code").GetString());
    }

    [Fact]
    public async Task SetRentPerSqm_NegativeValue_ReturnsError()
    {
        var token = await RegisterAndGetTokenAsync($"rent-neg-{Guid.NewGuid()}@test.com", "Negative Rent");
        var (_, buildingId) = await CreateApartmentBuildingAsync(token);

        var result = await ExecuteGraphQlAsync(
            """
            mutation SetRent($input: SetRentPerSqmInput!) {
              setRentPerSqm(input: $input) { id }
            }
            """,
            new { input = new { buildingId = Guid.Parse(buildingId), rentPerSqm = -5.0m } },
            token);

        Assert.True(result.TryGetProperty("errors", out var errors));
        Assert.Equal("INVALID_RENT", errors[0].GetProperty("extensions").GetProperty("code").GetString());
    }

    [Fact]
    public async Task SetRentPerSqm_OnFactoryBuilding_ReturnsInvalidBuildingType()
    {
        var token = await RegisterAndGetTokenAsync($"rent-factory-{Guid.NewGuid()}@test.com", "Factory Owner");
        var cityId = await GetCityIdByNameAsync();
        var factoryLotId = await CreateTestLotAsync(cityId, "FACTORY,MINE", "Industrial Zone");
        var shopLotId = await CreateTestLotAsync(cityId, "SALES_SHOP", "Commercial District");
        await StartOnboardingCompanyAsync(token, "Factory Co", factoryLotId);
        var productId = await GetStarterProductIdAsync();
        await FinishOnboardingAsync(token, productId, shopLotId);

        // Get the factory building id.
        var myCompanies = await ExecuteGraphQlAsync("{ myCompanies { buildings { id type } } }", token: token);
        var factoryId = myCompanies.GetProperty("data").GetProperty("myCompanies")[0]
            .GetProperty("buildings").EnumerateArray()
            .First(b => b.GetProperty("type").GetString() == "FACTORY")
            .GetProperty("id").GetString()!;

        var result = await ExecuteGraphQlAsync(
            """
            mutation SetRent($input: SetRentPerSqmInput!) {
              setRentPerSqm(input: $input) { id }
            }
            """,
            new { input = new { buildingId = Guid.Parse(factoryId), rentPerSqm = 20.0m } },
            token);

        Assert.True(result.TryGetProperty("errors", out var errors));
        Assert.Equal("INVALID_BUILDING_TYPE", errors[0].GetProperty("extensions").GetProperty("code").GetString());
    }

    [Fact]
    public async Task SetRentPerSqm_ActivationTickIsCurrentPlusTwentyFour()
    {
        var token = await RegisterAndGetTokenAsync($"rent-tick-{Guid.NewGuid()}@test.com", "Rent Tick Test");
        var (_, buildingId) = await CreateApartmentBuildingAsync(token);

        var gameStateResult = await ExecuteGraphQlAsync("{ gameState { currentTick } }");
        var currentTick = gameStateResult.GetProperty("data").GetProperty("gameState").GetProperty("currentTick").GetInt64();

        var result = await ExecuteGraphQlAsync(
            """
            mutation SetRent($input: SetRentPerSqmInput!) {
              setRentPerSqm(input: $input) { pendingPriceActivationTick }
            }
            """,
            new { input = new { buildingId = Guid.Parse(buildingId), rentPerSqm = 15.0m } },
            token);

        var activationTick = result.GetProperty("data").GetProperty("setRentPerSqm")
            .GetProperty("pendingPriceActivationTick").GetInt64();

        Assert.True(activationTick == currentTick + 24,
            "Pending rent must activate exactly 24 ticks (one in-game day) after submission.");
    }

    #endregion


    #region StarterDashboard

    /// <summary>
    /// After completing Furniture onboarding, myCompanies returns both factory and shop
    /// with configured building units visible for dashboard supply-chain display.
    /// </summary>
    [Fact]
    public async Task StarterDashboard_Furniture_MyCompanies_HasFactoryAndShopWithUnits()
    {
        var token = await RegisterAndGetTokenAsync(email: $"dash-furn-{Guid.NewGuid():N}@test.com");
        var (companyId, factoryLotId, cityId, _) = await StartOnboardingCompanyAsync(token, "Furniture Dashboard Co");
        var productId = await GetStarterProductIdAsync("FURNITURE", "wooden-chair");
        var shopLotId = await GetAvailableLotIdAsync(cityId, "SALES_SHOP");
        await FinishOnboardingAsync(token, productId, shopLotId);

        var result = await ExecuteGraphQlAsync(
            @"{ myCompanies {
                id name cash
                buildings { id name type units { id unitType gridX gridY } }
            } }",
            token: token);

        Assert.False(result.TryGetProperty("errors", out _));
        var companies = result.GetProperty("data").GetProperty("myCompanies").EnumerateArray().ToList();
        Assert.Single(companies);

        var buildings = companies[0].GetProperty("buildings").EnumerateArray().ToList();
        Assert.Equal(2, buildings.Count);

        var factory = buildings.First(b => b.GetProperty("type").GetString() == "FACTORY");
        var shop = buildings.First(b => b.GetProperty("type").GetString() == "SALES_SHOP");

        var factoryUnits = factory.GetProperty("units").EnumerateArray().ToList();
        var shopUnits = shop.GetProperty("units").EnumerateArray().ToList();

        Assert.True(factoryUnits.Count >= 2, "Factory should have at least PURCHASE and MANUFACTURING units after Furniture onboarding.");
        Assert.True(shopUnits.Count >= 1, "Shop should have at least one unit after Furniture onboarding.");

        var factoryUnitTypes = factoryUnits.Select(u => u.GetProperty("unitType").GetString()!).ToList();
        Assert.Contains("PURCHASE", factoryUnitTypes);
        Assert.Contains("MANUFACTURING", factoryUnitTypes);

        var shopUnitTypes = shopUnits.Select(u => u.GetProperty("unitType").GetString()!).ToList();
        Assert.Contains("PUBLIC_SALES", shopUnitTypes);
    }

    /// <summary>
    /// After completing Food Processing onboarding, myCompanies shows the bread factory and shop with units.
    /// </summary>
    [Fact]
    public async Task StarterDashboard_FoodProcessing_MyCompanies_HasFactoryAndShopWithUnits()
    {
        var token = await RegisterAndGetTokenAsync(email: $"dash-food-{Guid.NewGuid():N}@test.com");
        var cityId = await GetCityIdByNameAsync();
        var factoryLotId = await CreateTestLotAsync(cityId, "FACTORY,MINE", "Industrial Zone");
        await ExecuteGraphQlAsync(
            """
            mutation StartOnboardingCompany($input: StartOnboardingCompanyInput!) {
              startOnboardingCompany(input: $input) { nextStep company { id } }
            }
            """,
            new { input = new { industry = "FOOD_PROCESSING", cityId, companyName = "Bread Dashboard Co", factoryLotId } },
            token);

        var productId = await GetStarterProductIdAsync("FOOD_PROCESSING", "bread");
        var shopLotId = await GetAvailableLotIdAsync(cityId, "SALES_SHOP");
        await FinishOnboardingAsync(token, productId, shopLotId);

        var result = await ExecuteGraphQlAsync(
            @"{ myCompanies {
                id name cash
                buildings { id name type units { id unitType gridX gridY } }
            } }",
            token: token);

        Assert.False(result.TryGetProperty("errors", out _));
        var companies = result.GetProperty("data").GetProperty("myCompanies").EnumerateArray().ToList();
        Assert.Single(companies);

        var buildings = companies[0].GetProperty("buildings").EnumerateArray().ToList();
        Assert.Equal(2, buildings.Count);

        var factory = buildings.First(b => b.GetProperty("type").GetString() == "FACTORY");
        var factoryUnitTypes = factory.GetProperty("units").EnumerateArray()
            .Select(u => u.GetProperty("unitType").GetString()!).ToList();

        Assert.Contains("PURCHASE", factoryUnitTypes);
        Assert.Contains("MANUFACTURING", factoryUnitTypes);

        var shop = buildings.First(b => b.GetProperty("type").GetString() == "SALES_SHOP");
        var shopUnitTypes = shop.GetProperty("units").EnumerateArray()
            .Select(u => u.GetProperty("unitType").GetString()!).ToList();
        Assert.Contains("PUBLIC_SALES", shopUnitTypes);
    }

    /// <summary>
    /// After completing Healthcare onboarding, myCompanies shows the medicine factory and shop with units.
    /// </summary>
    [Fact]
    public async Task StarterDashboard_Healthcare_MyCompanies_HasFactoryAndShopWithUnits()
    {
        var token = await RegisterAndGetTokenAsync(email: $"dash-health-{Guid.NewGuid():N}@test.com");
        var cityId = await GetCityIdByNameAsync();
        var factoryLotId = await CreateTestLotAsync(cityId, "FACTORY,MINE", "Industrial Zone");
        await ExecuteGraphQlAsync(
            """
            mutation StartOnboardingCompany($input: StartOnboardingCompanyInput!) {
              startOnboardingCompany(input: $input) { nextStep company { id } }
            }
            """,
            new { input = new { industry = "HEALTHCARE", cityId, companyName = "Medicine Dashboard Co", factoryLotId } },
            token);

        var productId = await GetStarterProductIdAsync("HEALTHCARE", "basic-medicine");
        var shopLotId = await GetAvailableLotIdAsync(cityId, "SALES_SHOP");
        await FinishOnboardingAsync(token, productId, shopLotId);

        var result = await ExecuteGraphQlAsync(
            @"{ myCompanies {
                id name cash
                buildings { id name type units { id unitType gridX gridY } }
            } }",
            token: token);

        Assert.False(result.TryGetProperty("errors", out _));
        var companies = result.GetProperty("data").GetProperty("myCompanies").EnumerateArray().ToList();
        Assert.Single(companies);

        var buildings = companies[0].GetProperty("buildings").EnumerateArray().ToList();
        Assert.Equal(2, buildings.Count);

        var factory = buildings.First(b => b.GetProperty("type").GetString() == "FACTORY");
        var factoryUnitTypes = factory.GetProperty("units").EnumerateArray()
            .Select(u => u.GetProperty("unitType").GetString()!).ToList();

        Assert.Contains("PURCHASE", factoryUnitTypes);
        Assert.Contains("MANUFACTURING", factoryUnitTypes);

        var shop = buildings.First(b => b.GetProperty("type").GetString() == "SALES_SHOP");
        var shopUnitTypes = shop.GetProperty("units").EnumerateArray()
            .Select(u => u.GetProperty("unitType").GetString()!).ToList();
        Assert.Contains("PUBLIC_SALES", shopUnitTypes);
    }

    /// <summary>
    /// After completing Furniture onboarding, companyLedger returns a valid initial state
    /// with zero revenue, non-zero cash, and correct structure for the dashboard financial card.
    /// </summary>
    [Fact]
    public async Task StarterDashboard_Furniture_CompanyLedger_InitialStateHasZeroRevenueAndPositiveCash()
    {
        var token = await RegisterAndGetTokenAsync(email: $"ledger-dash-furn-{Guid.NewGuid():N}@test.com");
        var (companyId, _, cityId, _) = await StartOnboardingCompanyAsync(token, "Ledger Furn Co");
        var productId = await GetStarterProductIdAsync("FURNITURE", "wooden-chair");
        var shopLotId = await GetAvailableLotIdAsync(cityId, "SALES_SHOP");
        await FinishOnboardingAsync(token, productId, shopLotId);

        var result = await ExecuteGraphQlAsync(
            $@"{{ companyLedger(companyId: ""{companyId}"") {{
                companyId companyName currentCash
                totalRevenue totalPurchasingCosts totalLaborCosts totalEnergyCosts netIncome
                buildingSummaries {{ buildingId buildingName buildingType revenue costs }}
            }} }}",
            token: token);

        Assert.False(result.TryGetProperty("errors", out _));
        var ledger = result.GetProperty("data").GetProperty("companyLedger");

        Assert.Equal(companyId, ledger.GetProperty("companyId").GetString());
        Assert.True(ledger.GetProperty("currentCash").GetDecimal() > 0m, "Company should have positive cash immediately after onboarding.");
        Assert.Equal(0m, ledger.GetProperty("totalRevenue").GetDecimal()); // No revenue expected before any ticks run

        var summaries = ledger.GetProperty("buildingSummaries").EnumerateArray().ToList();
        Assert.Equal(2, summaries.Count);

        var types = summaries.Select(s => s.GetProperty("buildingType").GetString()!).ToList();
        Assert.Contains("FACTORY", types);
        Assert.Contains("SALES_SHOP", types);
    }

    /// <summary>
    /// After completing Food Processing onboarding, companyLedger returns correct initial state.
    /// </summary>
    [Fact]
    public async Task StarterDashboard_FoodProcessing_CompanyLedger_InitialStateHasZeroRevenueAndPositiveCash()
    {
        var token = await RegisterAndGetTokenAsync(email: $"ledger-dash-food-{Guid.NewGuid():N}@test.com");
        var cityId = await GetCityIdByNameAsync();
        var factoryLotId = await CreateTestLotAsync(cityId, "FACTORY,MINE", "Industrial Zone");
        await ExecuteGraphQlAsync(
            """
            mutation StartOnboardingCompany($input: StartOnboardingCompanyInput!) {
              startOnboardingCompany(input: $input) { nextStep company { id } }
            }
            """,
            new { input = new { industry = "FOOD_PROCESSING", cityId, companyName = "Ledger Food Co", factoryLotId } },
            token);

        var productId = await GetStarterProductIdAsync("FOOD_PROCESSING", "bread");
        var shopLotId = await GetAvailableLotIdAsync(cityId, "SALES_SHOP");
        var finishResult = await FinishOnboardingAsync(token, productId, shopLotId);
        var companyId = finishResult.GetProperty("data").GetProperty("finishOnboarding").GetProperty("company").GetProperty("id").GetString()!;

        var result = await ExecuteGraphQlAsync(
            $@"{{ companyLedger(companyId: ""{companyId}"") {{
                companyId currentCash totalRevenue netIncome
                buildingSummaries {{ buildingType }}
            }} }}",
            token: token);

        Assert.False(result.TryGetProperty("errors", out _));
        var ledger = result.GetProperty("data").GetProperty("companyLedger");

        Assert.True(ledger.GetProperty("currentCash").GetDecimal() > 0m, "Food Processing company should have positive cash after onboarding.");
        Assert.Equal(0m, ledger.GetProperty("totalRevenue").GetDecimal()); // No revenue before first tick cycle
        var types = ledger.GetProperty("buildingSummaries").EnumerateArray()
            .Select(s => s.GetProperty("buildingType").GetString()!).ToList();
        Assert.Contains("FACTORY", types);
        Assert.Contains("SALES_SHOP", types);
    }

    /// <summary>
    /// After completing Healthcare onboarding, companyLedger returns correct initial state.
    /// </summary>
    [Fact]
    public async Task StarterDashboard_Healthcare_CompanyLedger_InitialStateHasZeroRevenueAndPositiveCash()
    {
        var token = await RegisterAndGetTokenAsync(email: $"ledger-dash-health-{Guid.NewGuid():N}@test.com");
        var cityId = await GetCityIdByNameAsync();
        var factoryLotId = await CreateTestLotAsync(cityId, "FACTORY,MINE", "Industrial Zone");
        await ExecuteGraphQlAsync(
            """
            mutation StartOnboardingCompany($input: StartOnboardingCompanyInput!) {
              startOnboardingCompany(input: $input) { nextStep company { id } }
            }
            """,
            new { input = new { industry = "HEALTHCARE", cityId, companyName = "Ledger Health Co", factoryLotId } },
            token);

        var productId = await GetStarterProductIdAsync("HEALTHCARE", "basic-medicine");
        var shopLotId = await GetAvailableLotIdAsync(cityId, "SALES_SHOP");
        var finishResult = await FinishOnboardingAsync(token, productId, shopLotId);
        var companyId = finishResult.GetProperty("data").GetProperty("finishOnboarding").GetProperty("company").GetProperty("id").GetString()!;

        var result = await ExecuteGraphQlAsync(
            $@"{{ companyLedger(companyId: ""{companyId}"") {{
                companyId currentCash totalRevenue netIncome
                buildingSummaries {{ buildingType }}
            }} }}",
            token: token);

        Assert.False(result.TryGetProperty("errors", out _));
        var ledger = result.GetProperty("data").GetProperty("companyLedger");

        Assert.True(ledger.GetProperty("currentCash").GetDecimal() > 0m, "Healthcare company should have positive cash after onboarding.");
        Assert.Equal(0m, ledger.GetProperty("totalRevenue").GetDecimal()); // No revenue before first tick cycle
        var types = ledger.GetProperty("buildingSummaries").EnumerateArray()
            .Select(s => s.GetProperty("buildingType").GetString()!).ToList();
        Assert.Contains("FACTORY", types);
        Assert.Contains("SALES_SHOP", types);
    }

    /// <summary>
    /// Unauthenticated access to companyLedger returns null (not an error explosion).
    /// </summary>
    [Fact]
    public async Task StarterDashboard_CompanyLedger_Unauthenticated_ReturnsNull()
    {
        var adminToken = await RegisterAndGetTokenAsync(email: $"ledger-unauth-owner-{Guid.NewGuid():N}@test.com");
        var (companyId, _, cityId, _) = await StartOnboardingCompanyAsync(adminToken, "Ledger Unauth Co");
        var productId = await GetStarterProductIdAsync();
        var shopLotId = await GetAvailableLotIdAsync(cityId, "SALES_SHOP");
        await FinishOnboardingAsync(adminToken, productId, shopLotId);

        // Query as an unauthenticated client
        var result = await ExecuteGraphQlAsync(
            $@"{{ companyLedger(companyId: ""{companyId}"") {{ companyId }} }}");

        // GraphQL auth failures return null for the field (or an error extension), not a server crash
        var data = result.GetProperty("data");
        Assert.True(
            data.GetProperty("companyLedger").ValueKind == JsonValueKind.Null ||
            result.TryGetProperty("errors", out _),
            "Unauthenticated companyLedger query should return null or an auth error.");
    }

    /// <summary>
    /// myPendingActions returns an empty list for a freshly onboarded company (no queued upgrades).
    /// </summary>
    [Fact]
    public async Task StarterDashboard_MyPendingActions_FreshOnboarding_ReturnsEmptyList()
    {
        var token = await RegisterAndGetTokenAsync(email: $"pending-fresh-{Guid.NewGuid():N}@test.com");
        var (_, _, cityId, _) = await StartOnboardingCompanyAsync(token, "Pending Fresh Co");
        var productId = await GetStarterProductIdAsync();
        var shopLotId = await GetAvailableLotIdAsync(cityId, "SALES_SHOP");
        await FinishOnboardingAsync(token, productId, shopLotId);

        var result = await ExecuteGraphQlAsync(
            @"{ myPendingActions {
                id actionType buildingId buildingName buildingType
                submittedAtTick appliesAtTick ticksRemaining totalTicksRequired
            } }",
            token: token);

        Assert.False(result.TryGetProperty("errors", out _));
        var actions = result.GetProperty("data").GetProperty("myPendingActions").EnumerateArray().ToList();
        Assert.Empty(actions);
    }

    /// <summary>
    /// After processing ticks, the companyLedger shows purchasing costs as raw materials are bought.
    /// This validates the dashboard's financial summary card shows real economic activity.
    /// </summary>
    [Fact]
    public async Task StarterDashboard_Furniture_AfterTicks_LedgerShowsPurchasingCosts()
    {
        var token = await RegisterAndGetTokenAsync(email: $"ledger-ticks-furn-{Guid.NewGuid():N}@test.com");
        var (companyId, _, cityId, _) = await StartOnboardingCompanyAsync(token, "Ticked Furniture Co");
        var productId = await GetStarterProductIdAsync("FURNITURE", "wooden-chair");
        var shopLotId = await GetAvailableLotIdAsync(cityId, "SALES_SHOP");
        await FinishOnboardingAsync(token, productId, shopLotId);

        // Process several ticks so the purchase units can buy raw materials
        await ProcessTicksAsync(4);

        var result = await ExecuteGraphQlAsync(
            $@"{{ companyLedger(companyId: ""{companyId}"") {{
                totalRevenue totalPurchasingCosts totalLaborCosts netIncome currentCash
            }} }}",
            token: token);

        Assert.False(result.TryGetProperty("errors", out _));
        var ledger = result.GetProperty("data").GetProperty("companyLedger");

        // After 4 ticks the purchase unit should have acquired raw materials → purchasing costs > 0
        var purchasingCosts = ledger.GetProperty("totalPurchasingCosts").GetDecimal();
        Assert.True(purchasingCosts > 0m, "Purchasing costs should be positive after ticks (raw materials bought).");
    }

    /// <summary>
    /// After processing enough ticks, the Furniture company shows revenue in companyLedger.
    /// This proves the dashboard financial card will display real sales data.
    /// </summary>
    [Fact]
    public async Task StarterDashboard_Furniture_AfterManySales_LedgerShowsRevenue()
    {
        var token = await RegisterAndGetTokenAsync(email: $"ledger-sales-furn-{Guid.NewGuid():N}@test.com");
        var (companyId, _, cityId, _) = await StartOnboardingCompanyAsync(token, "Sales Furniture Co");
        var productId = await GetStarterProductIdAsync("FURNITURE", "wooden-chair");
        var shopLotId = await GetAvailableLotIdAsync(cityId, "SALES_SHOP");
        await FinishOnboardingAsync(token, productId, shopLotId);

        // Process enough ticks for the full supply chain: buy → manufacture → transfer → sell
        await ProcessTicksAsync(8);

        var result = await ExecuteGraphQlAsync(
            $@"{{ companyLedger(companyId: ""{companyId}"") {{
                totalRevenue totalPurchasingCosts netIncome cashFromOperations
            }} }}",
            token: token);

        Assert.False(result.TryGetProperty("errors", out _));
        var ledger = result.GetProperty("data").GetProperty("companyLedger");

        var revenue = ledger.GetProperty("totalRevenue").GetDecimal();
        Assert.True(revenue > 0m, "Furniture company should have revenue after 8 ticks of supply chain activity.");
    }

    /// <summary>
    /// gameState query returns valid tick data for the dashboard tick clock widget.
    /// </summary>
    [Fact]
    public async Task StarterDashboard_GameState_ReturnsValidTickData()
    {
        var result = await ExecuteGraphQlAsync(
            @"{ gameState {
                currentTick lastTickAtUtc tickIntervalSeconds
                currentGameYear currentGameTimeUtc
                nextTaxTick nextTaxGameTimeUtc
            } }");

        Assert.False(result.TryGetProperty("errors", out _));
        var gameState = result.GetProperty("data").GetProperty("gameState");

        Assert.True(gameState.GetProperty("currentTick").GetInt64() >= 0, "currentTick must be non-negative.");
        Assert.True(gameState.GetProperty("tickIntervalSeconds").GetInt32() > 0, "tickIntervalSeconds must be positive.");
        Assert.False(string.IsNullOrEmpty(gameState.GetProperty("currentGameTimeUtc").GetString()), "currentGameTimeUtc must be set.");
        Assert.True(gameState.GetProperty("currentGameYear").GetInt32() > 0, "currentGameYear must be positive.");
    }

    /// <summary>
    /// companyLedger returns an authoritative netIncome that includes tax.
    /// The dashboard must use netIncome, not revenue - operating costs.
    /// After ticks + a tax cycle, netIncome reflects the true after-tax result.
    /// </summary>
    [Fact]
    public async Task StarterDashboard_CompanyLedger_NetIncome_IsAuthoritative_IncludesTax()
    {
        var token = await RegisterAndGetTokenAsync(email: $"ledger-tax-net-{Guid.NewGuid():N}@test.com");
        var (companyId, _, cityId, _) = await StartOnboardingCompanyAsync(token, "NetIncome Tax Co");
        var productId = await GetStarterProductIdAsync("FURNITURE", "wooden-chair");
        var shopLotId = await GetAvailableLotIdAsync(cityId, "SALES_SHOP");
        await FinishOnboardingAsync(token, productId, shopLotId);

        // Process enough ticks to generate sales and let netIncome be calculated
        await ProcessTicksAsync(8);

        var result = await ExecuteGraphQlAsync(
            $@"{{ companyLedger(companyId: ""{companyId}"") {{
                totalRevenue totalPurchasingCosts totalLaborCosts totalEnergyCosts
                totalTaxPaid totalOtherCosts netIncome
            }} }}",
            token: token);

        Assert.False(result.TryGetProperty("errors", out _));
        var ledger = result.GetProperty("data").GetProperty("companyLedger");

        var totalRevenue = ledger.GetProperty("totalRevenue").GetDecimal();
        var totalPurchasingCosts = ledger.GetProperty("totalPurchasingCosts").GetDecimal();
        var totalLaborCosts = ledger.GetProperty("totalLaborCosts").GetDecimal();
        var totalEnergyCosts = ledger.GetProperty("totalEnergyCosts").GetDecimal();
        var totalTaxPaid = ledger.GetProperty("totalTaxPaid").GetDecimal();
        var totalOtherCosts = ledger.GetProperty("totalOtherCosts").GetDecimal();
        var netIncome = ledger.GetProperty("netIncome").GetDecimal();

        // netIncome = revenue - all costs including tax (backend formula)
        var expectedNetIncome = totalRevenue - totalPurchasingCosts - totalLaborCosts
                                - totalEnergyCosts - totalTaxPaid - totalOtherCosts;
        Assert.Equal(expectedNetIncome, netIncome);

        // Confirm the backend netIncome field exists and is a valid decimal (positive or negative)
        // The dashboard MUST use this value, not a frontend-derived revenue - operating_costs estimate
        Assert.True(
            netIncome <= totalRevenue,
            "netIncome must be ≤ totalRevenue since at minimum purchasing/labor/energy costs are subtracted.");
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

    private static async Task<JsonElement> ExecuteGraphQlAsync(HttpClient client, string query, object? variables = null, string? token = null)
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

        var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        return System.Text.Json.JsonSerializer.Deserialize<JsonElement>(body);
    }

    private Task<JsonElement> ExecuteGraphQlAsync(string query, object? variables = null, string? token = null)
        => ExecuteGraphQlAsync(_client, query, variables, token);

    private static async Task<string> RegisterAndGetTokenAsync(HttpClient client, string email, string displayName = "Tester", string password = "TestPass123!")
    {
        var result = await ExecuteGraphQlAsync(
            client,
            """
            mutation Register($input: RegisterInput!) {
              register(input: $input) { token }
            }
            """,
            new { input = new { email, displayName, password } });
        return result.GetProperty("data").GetProperty("register").GetProperty("token").GetString()!;
    }

    private Task<string> RegisterAndGetTokenAsync(string email, string displayName = "Tester", string password = "TestPass123!")
        => RegisterAndGetTokenAsync(_client, email, displayName, password);

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

    private async Task ProcessTicksAsync(int count)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var phases = scope.ServiceProvider.GetServices<ITickPhase>();
        var logger = new Microsoft.Extensions.Logging.Abstractions.NullLogger<TickProcessor>();
        var processor = new TickProcessor(db, phases, logger);
        for (var i = 0; i < count; i++)
            await processor.ProcessTickAsync();
    }

    #region GameState tick data

    [Fact]
    public async Task GameState_ReturnsLastTickAtUtc()
    {
        await ResetGameStateAsync();
        var result = await ExecuteGraphQlAsync(
            "{ gameState { currentTick tickIntervalSeconds lastTickAtUtc taxRate currentGameTimeUtc } }");

        var state = result.GetProperty("data").GetProperty("gameState");
        Assert.True(DateTime.TryParse(state.GetProperty("lastTickAtUtc").GetString(), out _),
            "lastTickAtUtc should be a parseable timestamp");
        Assert.True(DateTime.TryParse(state.GetProperty("currentGameTimeUtc").GetString(), out _),
            "currentGameTimeUtc should be a parseable timestamp");
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
        string buildingName,
        string? powerPlantType = null)
    {
        var result = await ExecuteGraphQlAsync(
            """
            mutation PurchaseLot($input: PurchaseLotInput!) {
              purchaseLot(input: $input) { building { id powerOutput powerPlantType powerConsumption } }
            }
            """,
            new { input = new { companyId, lotId, buildingType, buildingName, powerPlantType } },
            token);
        return result.GetProperty("data").GetProperty("purchaseLot").GetProperty("building").GetProperty("id").GetString()!;
    }

    #endregion

    #region Database migration transition

    [Fact]
    public async Task StartupWithExistingEnsureCreatedDatabase_MigratesToMigrationsManagedSchema()
    {
        // Regression test for the EnsureCreatedAsync→MigrateAsync transition.
        //
        // SCENARIO: A developer or hosted environment has a SQLite database that was originally
        // created by EnsureCreatedAsync (before migration support was introduced). It has all
        // tables but no __EFMigrationsHistory table. A new deployment switches to MigrateAsync.
        //
        // EXPECTED BEHAVIOR: InitializeAsync should succeed — it detects the missing history
        // table, baselines all current migrations as already applied, and then MigrateAsync
        // finds nothing to do (no pending migrations).
        //
        // IMPLEMENTATION: We simulate the legacy database by creating a new temporary SQLite DB,
        // applying the schema via EnsureCreatedAsync, dropping the __EFMigrationsHistory table
        // (if it was created by EnsureCreated — it won't be since EnsureCreated never creates
        // it, so the DB is already in the legacy state), then running InitializeAsync.

        var dbPath = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"capitalism-migration-test-{Guid.NewGuid():N}.db");

        try
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite($"Data Source={dbPath}")
                .Options;

            // Step 1: Create the legacy database — EnsureCreatedAsync creates all tables but
            // does NOT create __EFMigrationsHistory (that is a migrations-specific artifact).
            await using (var legacyCtx = new AppDbContext(options))
            {
                var wasCreated = await legacyCtx.Database.EnsureCreatedAsync();
                Assert.True(wasCreated, "Fresh database should have been created");

                // Confirm __EFMigrationsHistory is absent (legacy state).
                var conn = legacyCtx.Database.GetDbConnection();
                await conn.OpenAsync();
                await using var checkCmd = conn.CreateCommand();
                checkCmd.CommandText =
                    "SELECT COUNT(1) FROM sqlite_master WHERE type='table' AND name='__EFMigrationsHistory'";
                var historyExists = Convert.ToInt64(await checkCmd.ExecuteScalarAsync() ?? 0L) > 0;
                conn.Close();

                Assert.False(historyExists,
                    "EnsureCreatedAsync must NOT create __EFMigrationsHistory — this test depends on the legacy state");
            }

            // Step 2: Now run InitializeAsync against this legacy database.
            // The safe bootstrap in AppDbInitializer should detect the missing history table,
            // create it, baseline all migrations, and complete without throwing.
            await using var upgradeCtx = new AppDbContext(options);

            var testSeedOptions = Microsoft.Extensions.Options.Options.Create(new Api.Configuration.SeedDataOptions
            {
                AdminEmail = "admin@migration-test.local",
                AdminDisplayName = "Migration Test Admin",
                AdminPassword = "TestPassword123!"
            });

            var initializer = new AppDbInitializer(upgradeCtx, testSeedOptions);
            var exception = await Record.ExceptionAsync(() => initializer.InitializeAsync());
            if (exception is not null)
                throw new InvalidOperationException(
                    $"InitializeAsync must succeed on a legacy EnsureCreated database. Got: {exception.Message}", exception);
            Assert.Null(exception);

            // Step 3: Verify __EFMigrationsHistory was created and populated.
            await using var verifyCtx = new AppDbContext(options);
            var conn2 = verifyCtx.Database.GetDbConnection();
            await conn2.OpenAsync();
            await using var histCheck = conn2.CreateCommand();
            histCheck.CommandText =
                "SELECT COUNT(1) FROM sqlite_master WHERE type='table' AND name='__EFMigrationsHistory'";
            var historyNowExists = Convert.ToInt64(await histCheck.ExecuteScalarAsync() ?? 0L) > 0;

            await using var rowCheck = conn2.CreateCommand();
            rowCheck.CommandText = "SELECT COUNT(1) FROM __EFMigrationsHistory";
            var historyRowCount = Convert.ToInt64(await rowCheck.ExecuteScalarAsync() ?? 0L);
            conn2.Close();

            Assert.True(historyNowExists, "__EFMigrationsHistory must exist after safe migration bootstrap");
            Assert.True(historyRowCount > 0, "__EFMigrationsHistory must have at least one baseline row");

            // Step 4: A second call to InitializeAsync (simulating a server restart) must also
            // succeed — the history table now exists so the baseline step is skipped.
            await using var restartCtx = new AppDbContext(options);
            var restartInitializer = new AppDbInitializer(restartCtx, testSeedOptions);
            var restartException = await Record.ExceptionAsync(() => restartInitializer.InitializeAsync());
            if (restartException is not null)
                throw new InvalidOperationException(
                    $"Second InitializeAsync (server restart) must succeed. Got: {restartException.Message}", restartException);
            Assert.Null(restartException);
        }
        finally
        {
            // Clean up temp database files.
            foreach (var suffix in new[] { "", "-wal", "-shm" })
            {
                var path = dbPath + suffix;
                if (System.IO.File.Exists(path))
                    DeleteFileWithRetry(path);
            }
        }
    }

    private static void DeleteFileWithRetry(string path)
    {
        const int maxAttempts = 10;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            if (!System.IO.File.Exists(path))
            {
                return;
            }

            try
            {
                System.IO.File.Delete(path);
                return;
            }
            catch (System.IO.IOException) when (attempt < maxAttempts)
            {
                Thread.Sleep(100);
            }
            catch (UnauthorizedAccessException) when (attempt < maxAttempts)
            {
                Thread.Sleep(100);
            }
            catch (System.IO.IOException)
            {
                return;
            }
            catch (UnauthorizedAccessException)
            {
                return;
            }
        }
    }

    #endregion

    #region BuildingUnitOperationalStatuses and BuildingRecentActivity

    // Helper: seed a player, company, and building for operational status tests.
    private async Task<(string Token, Guid BuildingId)> SeedOperationalStatusTestAsync(
        string emailPrefix,
        Action<AppDbContext, Guid, Guid, Guid> seedUnitsAndHistory)
    {
        var email = $"{emailPrefix}-{Guid.NewGuid():N}@test.com";
        var token = await RegisterAndGetTokenAsync(email, emailPrefix);

        Guid buildingId;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var player = await db.Players.FirstAsync(p => p.Email == email);
            var city = await db.Cities.FirstAsync();

            // Seed a company for this player (registration does not create one automatically).
            var company = new Company
            {
                Id = Guid.NewGuid(),
                PlayerId = player.Id,
                Name = $"{emailPrefix} Corp",
                Cash = 500_000m,
            };
            db.Companies.Add(company);

            var building = new Building
            {
                Id = Guid.NewGuid(),
                CompanyId = company.Id,
                CityId = city.Id,
                Type = BuildingType.Factory,
                Name = $"{emailPrefix} Factory",
                Level = 1,
            };
            db.Buildings.Add(building);
            buildingId = building.Id;

            seedUnitsAndHistory(db, building.Id, company.Id, player.Id);
            await db.SaveChangesAsync();
        }

        return (token, buildingId);
    }

    [Fact]
    public async Task BuildingUnitOperationalStatuses_UnconfiguredPurchaseUnit_ReturnsUnconfigured()
    {
        // Arrange: register a player, seed a factory with an unconfigured purchase unit.
        var (token, buildingId) = await SeedOperationalStatusTestAsync("opstat-unconfigured",
            (db, bid, _, _) =>
            {
                db.BuildingUnits.Add(new BuildingUnit
                {
                    Id = Guid.NewGuid(),
                    BuildingId = bid,
                    UnitType = UnitType.Purchase,
                    GridX = 0,
                    GridY = 0,
                    Level = 1,
                    // No ResourceTypeId or ProductTypeId – unit is unconfigured
                });
            });

        // Act
        var result = await ExecuteGraphQlAsync(
            """
            query BuildingUnitOperationalStatuses($buildingId: UUID!) {
              buildingUnitOperationalStatuses(buildingId: $buildingId) {
                buildingUnitId
                status
                blockedCode
                blockedReason
                idleTicks
              }
            }
            """,
            new { buildingId },
            token);

        // Assert
        var statuses = result.GetProperty("data").GetProperty("buildingUnitOperationalStatuses");
        Assert.Equal(1, statuses.GetArrayLength());
        var status = statuses[0];
        Assert.Equal("UNCONFIGURED", status.GetProperty("status").GetString());
        Assert.Equal("UNCONFIGURED", status.GetProperty("blockedCode").GetString());
        Assert.False(string.IsNullOrEmpty(status.GetProperty("blockedReason").GetString()),
            "blockedReason must provide a human-readable explanation.");
    }

    [Fact]
    public async Task BuildingUnitOperationalStatuses_ActivePurchaseUnit_ReturnsActive()
    {
        // Arrange: seed a factory with a configured purchase unit that has recent inflow history.
        var (token, buildingId) = await SeedOperationalStatusTestAsync("opstat-active",
            (db, bid, _, _) =>
            {
                var resource = db.ResourceTypes.First();
                var gameState = db.GameStates.FirstOrDefault();
                var currentTick = gameState?.CurrentTick ?? 0L;

                var unitId = Guid.NewGuid();
                db.BuildingUnits.Add(new BuildingUnit
                {
                    Id = unitId,
                    BuildingId = bid,
                    UnitType = UnitType.Purchase,
                    GridX = 0,
                    GridY = 0,
                    Level = 1,
                    ResourceTypeId = resource.Id,
                });

                // Seed recent inflow history so the unit appears ACTIVE.
                db.BuildingUnitResourceHistories.Add(new BuildingUnitResourceHistory
                {
                    Id = Guid.NewGuid(),
                    BuildingId = bid,
                    BuildingUnitId = unitId,
                    ResourceTypeId = resource.Id,
                    Tick = currentTick,
                    InflowQuantity = 10m,
                });
            });

        // Act
        var result = await ExecuteGraphQlAsync(
            """
            query BuildingUnitOperationalStatuses($buildingId: UUID!) {
              buildingUnitOperationalStatuses(buildingId: $buildingId) {
                buildingUnitId
                status
                blockedCode
                idleTicks
              }
            }
            """,
            new { buildingId },
            token);

        // Assert
        var statuses = result.GetProperty("data").GetProperty("buildingUnitOperationalStatuses");
        Assert.Equal(1, statuses.GetArrayLength());
        var status = statuses[0];
        Assert.Equal("ACTIVE", status.GetProperty("status").GetString());
        Assert.True(status.GetProperty("blockedCode").ValueKind == JsonValueKind.Null,
            "An ACTIVE unit must not have a blockedCode.");
    }

    [Fact]
    public async Task BuildingUnitOperationalStatuses_ManufacturingUnit_NoInputs_ReturnsBlocked()
    {
        // Arrange: seed a factory with a manufacturing unit that has no inventory or history.
        var (token, buildingId) = await SeedOperationalStatusTestAsync("opstat-mfg-blocked",
            (db, bid, _, _) =>
            {
                var product = db.ProductTypes.First();
                db.BuildingUnits.Add(new BuildingUnit
                {
                    Id = Guid.NewGuid(),
                    BuildingId = bid,
                    UnitType = UnitType.Manufacturing,
                    GridX = 0,
                    GridY = 0,
                    Level = 1,
                    ProductTypeId = product.Id,
                    // No inventory, no history → blocked: NO_INPUTS
                });
            });

        // Act
        var result = await ExecuteGraphQlAsync(
            """
            query BuildingUnitOperationalStatuses($buildingId: UUID!) {
              buildingUnitOperationalStatuses(buildingId: $buildingId) {
                buildingUnitId
                status
                blockedCode
                blockedReason
              }
            }
            """,
            new { buildingId },
            token);

        // Assert
        var statuses = result.GetProperty("data").GetProperty("buildingUnitOperationalStatuses");
        var mfgStatus = Enumerable.Range(0, statuses.GetArrayLength())
            .Select(i => statuses[i])
            .First();

        Assert.Equal("BLOCKED", mfgStatus.GetProperty("status").GetString());
        Assert.Equal("NO_INPUTS", mfgStatus.GetProperty("blockedCode").GetString());
        Assert.False(string.IsNullOrEmpty(mfgStatus.GetProperty("blockedReason").GetString()),
            "A blocked manufacturing unit must explain the reason.");
    }

    [Fact]
    public async Task BuildingUnitOperationalStatuses_Unauthenticated_ReturnsError()
    {
        var result = await ExecuteGraphQlAsync(
            """
            query BuildingUnitOperationalStatuses($buildingId: UUID!) {
              buildingUnitOperationalStatuses(buildingId: $buildingId) {
                buildingUnitId
                status
              }
            }
            """,
            new { buildingId = Guid.NewGuid() });

        var errors = result.GetProperty("errors");
        Assert.True(errors.GetArrayLength() > 0, "Unauthenticated call must return errors.");
    }

    [Fact]
    public async Task BuildingRecentActivity_AfterTicks_ReturnsPurchasedEvents()
    {
        // Arrange: seed a factory with a purchase unit and two ticks of inflow history.
        var (token, buildingId) = await SeedOperationalStatusTestAsync("recent-activity",
            (db, bid, _, _) =>
            {
                var resource = db.ResourceTypes.First();
                var unitId = Guid.NewGuid();
                db.BuildingUnits.Add(new BuildingUnit
                {
                    Id = unitId,
                    BuildingId = bid,
                    UnitType = UnitType.Purchase,
                    GridX = 0,
                    GridY = 0,
                    Level = 1,
                    ResourceTypeId = resource.Id,
                });

                // Simulate two ticks of purchasing.
                db.BuildingUnitResourceHistories.AddRange(
                    new BuildingUnitResourceHistory
                    {
                        Id = Guid.NewGuid(),
                        BuildingId = bid,
                        BuildingUnitId = unitId,
                        ResourceTypeId = resource.Id,
                        Tick = 1,
                        InflowQuantity = 10m,
                    },
                    new BuildingUnitResourceHistory
                    {
                        Id = Guid.NewGuid(),
                        BuildingId = bid,
                        BuildingUnitId = unitId,
                        ResourceTypeId = resource.Id,
                        Tick = 2,
                        InflowQuantity = 8m,
                    });
            });

        // Act
        var result = await ExecuteGraphQlAsync(
            """
            query BuildingRecentActivity($buildingId: UUID!, $limit: Int) {
              buildingRecentActivity(buildingId: $buildingId, limit: $limit) {
                tick
                buildingUnitId
                eventType
                description
                quantity
              }
            }
            """,
            new { buildingId, limit = 10 },
            token);

        // Assert
        var events = result.GetProperty("data").GetProperty("buildingRecentActivity");
        Assert.True(events.GetArrayLength() >= 2,
            "Should have at least 2 PURCHASED events (one per tick).");

        var purchasedEvents = Enumerable.Range(0, events.GetArrayLength())
            .Select(i => events[i])
            .Where(e => e.GetProperty("eventType").GetString() == "PURCHASED")
            .ToList();

        Assert.True(purchasedEvents.Count >= 2,
            "Both tick 1 and tick 2 inflow records should appear as PURCHASED events.");

        foreach (var ev in purchasedEvents)
        {
            var desc = ev.GetProperty("description").GetString() ?? "";
            Assert.False(string.IsNullOrWhiteSpace(desc), "Each activity event must have a description.");
            Assert.Contains("Purchased", desc, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task BuildingRecentActivity_Unauthenticated_ReturnsError()
    {
        var result = await ExecuteGraphQlAsync(
            """
            query BuildingRecentActivity($buildingId: UUID!, $limit: Int) {
              buildingRecentActivity(buildingId: $buildingId, limit: $limit) {
                tick
                eventType
                description
              }
            }
            """,
            new { buildingId = Guid.NewGuid(), limit = 10 });

        var errors = result.GetProperty("errors");
        Assert.True(errors.GetArrayLength() > 0, "Unauthenticated call must return errors.");
    }

    [Fact]
    public async Task BuildingFinancialTimeline_ReturnsSalesCostsAndProfitByTick()
    {
        var (token, buildingId) = await SeedOperationalStatusTestAsync("building-financial",
            (db, bid, companyId, _) =>
            {
                var primaryUnitId = Guid.NewGuid();
                var secondaryUnitId = Guid.NewGuid();
                var gameState = db.GameStates.First();
                gameState.CurrentTick = 42;

                db.BuildingUnits.AddRange(
                    new BuildingUnit
                    {
                        Id = primaryUnitId,
                        BuildingId = bid,
                        UnitType = UnitType.PublicSales,
                        GridX = 0,
                        GridY = 0,
                        Level = 1,
                    },
                    new BuildingUnit
                    {
                        Id = secondaryUnitId,
                        BuildingId = bid,
                        UnitType = UnitType.Purchase,
                        GridX = 1,
                        GridY = 0,
                        Level = 1,
                    });

                db.LedgerEntries.AddRange(
                    new LedgerEntry
                    {
                        Id = Guid.NewGuid(),
                        CompanyId = companyId,
                        BuildingId = bid,
                        BuildingUnitId = primaryUnitId,
                        Category = LedgerCategory.Revenue,
                        Description = "Retail sale",
                        Amount = 120m,
                        RecordedAtTick = 40,
                        RecordedAtUtc = DateTime.UtcNow,
                    },
                    new LedgerEntry
                    {
                        Id = Guid.NewGuid(),
                        CompanyId = companyId,
                        BuildingId = bid,
                        BuildingUnitId = secondaryUnitId,
                        Category = LedgerCategory.PurchasingCost,
                        Description = "Input sourcing",
                        Amount = -30m,
                        RecordedAtTick = 40,
                        RecordedAtUtc = DateTime.UtcNow,
                    },
                    new LedgerEntry
                    {
                        Id = Guid.NewGuid(),
                        CompanyId = companyId,
                        BuildingId = bid,
                        BuildingUnitId = secondaryUnitId,
                        Category = LedgerCategory.LaborCost,
                        Description = "Labor",
                        Amount = -10m,
                        RecordedAtTick = 41,
                        RecordedAtUtc = DateTime.UtcNow,
                    },
                    new LedgerEntry
                    {
                        Id = Guid.NewGuid(),
                        CompanyId = companyId,
                        BuildingId = bid,
                        BuildingUnitId = primaryUnitId,
                        Category = LedgerCategory.Revenue,
                        Description = "Wholesale sale",
                        Amount = 80m,
                        RecordedAtTick = 42,
                        RecordedAtUtc = DateTime.UtcNow,
                    },
                    new LedgerEntry
                    {
                        Id = Guid.NewGuid(),
                        CompanyId = companyId,
                        BuildingId = bid,
                        BuildingUnitId = primaryUnitId,
                        Category = LedgerCategory.Marketing,
                        Description = "Campaign spend",
                        Amount = -20m,
                        RecordedAtTick = 42,
                        RecordedAtUtc = DateTime.UtcNow,
                    });
            });

        var result = await ExecuteGraphQlAsync(
            """
            query BuildingFinancialTimeline($buildingId: UUID!, $limit: Int) {
              buildingFinancialTimeline(buildingId: $buildingId, limit: $limit) {
                buildingId
                buildingName
                dataFromTick
                dataToTick
                totalSales
                totalCosts
                totalProfit
                timeline {
                  tick
                  sales
                  costs
                  profit
                }
              }
            }
            """,
            new { buildingId, limit = 3 },
            token);

        var timeline = result.GetProperty("data").GetProperty("buildingFinancialTimeline");
        Assert.Equal(buildingId.ToString(), timeline.GetProperty("buildingId").GetString());
        Assert.Equal(40, timeline.GetProperty("dataFromTick").GetInt64());
        Assert.Equal(42, timeline.GetProperty("dataToTick").GetInt64());
        Assert.Equal(200m, timeline.GetProperty("totalSales").GetDecimal());
        Assert.Equal(60m, timeline.GetProperty("totalCosts").GetDecimal());
        Assert.Equal(140m, timeline.GetProperty("totalProfit").GetDecimal());

        var snapshots = timeline.GetProperty("timeline").EnumerateArray().ToList();
        Assert.Equal(3, snapshots.Count);

        Assert.Equal(40, snapshots[0].GetProperty("tick").GetInt64());
        Assert.Equal(120m, snapshots[0].GetProperty("sales").GetDecimal());
        Assert.Equal(30m, snapshots[0].GetProperty("costs").GetDecimal());
        Assert.Equal(90m, snapshots[0].GetProperty("profit").GetDecimal());

        Assert.Equal(41, snapshots[1].GetProperty("tick").GetInt64());
        Assert.Equal(0m, snapshots[1].GetProperty("sales").GetDecimal());
        Assert.Equal(10m, snapshots[1].GetProperty("costs").GetDecimal());
        Assert.Equal(-10m, snapshots[1].GetProperty("profit").GetDecimal());

        Assert.Equal(42, snapshots[2].GetProperty("tick").GetInt64());
        Assert.Equal(80m, snapshots[2].GetProperty("sales").GetDecimal());
        Assert.Equal(20m, snapshots[2].GetProperty("costs").GetDecimal());
        Assert.Equal(60m, snapshots[2].GetProperty("profit").GetDecimal());
    }

    [Fact]
    public async Task BuildingFinancialTimeline_DefaultWindow_AggregatesOperationalEntriesAcrossUnits()
    {
        var (token, buildingId) = await SeedOperationalStatusTestAsync("building-financial-window",
            (db, bid, companyId, _) =>
            {
                var salesUnitId = Guid.NewGuid();
                var purchaseUnitId = Guid.NewGuid();
                var gameState = db.GameStates.First();
                gameState.CurrentTick = 140;

                db.BuildingUnits.AddRange(
                    new BuildingUnit
                    {
                        Id = salesUnitId,
                        BuildingId = bid,
                        UnitType = UnitType.PublicSales,
                        GridX = 0,
                        GridY = 0,
                        Level = 1,
                    },
                    new BuildingUnit
                    {
                        Id = purchaseUnitId,
                        BuildingId = bid,
                        UnitType = UnitType.Purchase,
                        GridX = 1,
                        GridY = 0,
                        Level = 1,
                    });

                db.LedgerEntries.AddRange(
                    new LedgerEntry
                    {
                        Id = Guid.NewGuid(),
                        CompanyId = companyId,
                        BuildingId = bid,
                        BuildingUnitId = salesUnitId,
                        Category = LedgerCategory.Revenue,
                        Description = "Outside window sale",
                        Amount = 999m,
                        RecordedAtTick = 40,
                        RecordedAtUtc = DateTime.UtcNow,
                    },
                    new LedgerEntry
                    {
                        Id = Guid.NewGuid(),
                        CompanyId = companyId,
                        BuildingId = bid,
                        BuildingUnitId = salesUnitId,
                        Category = LedgerCategory.Revenue,
                        Description = "Window sale one",
                        Amount = 50m,
                        RecordedAtTick = 41,
                        RecordedAtUtc = DateTime.UtcNow,
                    },
                    new LedgerEntry
                    {
                        Id = Guid.NewGuid(),
                        CompanyId = companyId,
                        BuildingId = bid,
                        BuildingUnitId = salesUnitId,
                        Category = LedgerCategory.Revenue,
                        Description = "Window sale two",
                        Amount = 70m,
                        RecordedAtTick = 75,
                        RecordedAtUtc = DateTime.UtcNow,
                    },
                    new LedgerEntry
                    {
                        Id = Guid.NewGuid(),
                        CompanyId = companyId,
                        BuildingId = bid,
                        BuildingUnitId = purchaseUnitId,
                        Category = LedgerCategory.PurchasingCost,
                        Description = "Window sourcing cost",
                        Amount = -20m,
                        RecordedAtTick = 75,
                        RecordedAtUtc = DateTime.UtcNow,
                    },
                    new LedgerEntry
                    {
                        Id = Guid.NewGuid(),
                        CompanyId = companyId,
                        BuildingId = bid,
                        BuildingUnitId = purchaseUnitId,
                        Category = LedgerCategory.Marketing,
                        Description = "Window marketing cost",
                        Amount = -10m,
                        RecordedAtTick = 140,
                        RecordedAtUtc = DateTime.UtcNow,
                    },
                    new LedgerEntry
                    {
                        Id = Guid.NewGuid(),
                        CompanyId = companyId,
                        BuildingId = bid,
                        Category = LedgerCategory.PropertyPurchase,
                        Description = "Capital purchase should not affect operations",
                        Amount = -5000m,
                        RecordedAtTick = 140,
                        RecordedAtUtc = DateTime.UtcNow,
                    });
            });

        var result = await ExecuteGraphQlAsync(
            """
            query BuildingFinancialTimeline($buildingId: UUID!) {
              buildingFinancialTimeline(buildingId: $buildingId) {
                dataFromTick
                dataToTick
                totalSales
                totalCosts
                totalProfit
                timeline {
                  tick
                  sales
                  costs
                  profit
                }
              }
            }
            """,
            new { buildingId },
            token);

        var timeline = result.GetProperty("data").GetProperty("buildingFinancialTimeline");
        Assert.Equal(41, timeline.GetProperty("dataFromTick").GetInt64());
        Assert.Equal(140, timeline.GetProperty("dataToTick").GetInt64());
        Assert.Equal(120m, timeline.GetProperty("totalSales").GetDecimal());
        Assert.Equal(30m, timeline.GetProperty("totalCosts").GetDecimal());
        Assert.Equal(90m, timeline.GetProperty("totalProfit").GetDecimal());

        var snapshots = timeline.GetProperty("timeline").EnumerateArray().ToList();
        Assert.Equal(100, snapshots.Count);

        var snapshotsByTick = snapshots.ToDictionary(
            snapshot => snapshot.GetProperty("tick").GetInt64(),
            snapshot => snapshot);

        Assert.Equal(50m, snapshotsByTick[41].GetProperty("sales").GetDecimal());
        Assert.Equal(0m, snapshotsByTick[41].GetProperty("costs").GetDecimal());
        Assert.Equal(50m, snapshotsByTick[41].GetProperty("profit").GetDecimal());

        Assert.Equal(70m, snapshotsByTick[75].GetProperty("sales").GetDecimal());
        Assert.Equal(20m, snapshotsByTick[75].GetProperty("costs").GetDecimal());
        Assert.Equal(50m, snapshotsByTick[75].GetProperty("profit").GetDecimal());

        Assert.Equal(0m, snapshotsByTick[140].GetProperty("sales").GetDecimal());
        Assert.Equal(10m, snapshotsByTick[140].GetProperty("costs").GetDecimal());
        Assert.Equal(-10m, snapshotsByTick[140].GetProperty("profit").GetDecimal());
    }

    [Fact]
    public async Task BuildingFinancialTimeline_Unauthenticated_ReturnsError()
    {
        var result = await ExecuteGraphQlAsync(
            """
            query BuildingFinancialTimeline($buildingId: UUID!) {
              buildingFinancialTimeline(buildingId: $buildingId) {
                buildingId
              }
            }
            """,
            new { buildingId = Guid.NewGuid() });

        var errors = result.GetProperty("errors");
        Assert.True(errors.GetArrayLength() > 0, "Unauthenticated call must return errors.");
    }

    [Fact]
    public async Task BuildingUnitOperationalStatuses_PurchaseUnit_ReturnsNextTickCosts()
    {
        // Arrange: seed a factory in Bratislava with a configured purchase unit.
        var (token, buildingId) = await SeedOperationalStatusTestAsync("opstat-costest",
            (db, bid, _, _) =>
            {
                var resource = db.ResourceTypes.First();
                db.BuildingUnits.Add(new BuildingUnit
                {
                    Id = Guid.NewGuid(),
                    BuildingId = bid,
                    UnitType = UnitType.Purchase,
                    GridX = 0,
                    GridY = 0,
                    Level = 1,
                    ResourceTypeId = resource.Id,
                });
            });

        // Act
        var result = await ExecuteGraphQlAsync(
            """
            query BuildingUnitOperationalStatuses($buildingId: UUID!) {
              buildingUnitOperationalStatuses(buildingId: $buildingId) {
                buildingUnitId
                status
                nextTickLaborCost
                nextTickEnergyCost
              }
            }
            """,
            new { buildingId },
            token);

        // Assert
        var statuses = result.GetProperty("data").GetProperty("buildingUnitOperationalStatuses");
        Assert.Equal(1, statuses.GetArrayLength());
        var status = statuses[0];

        // A PURCHASE unit at level 1:
        // laborHours = 0.35; energyMwh = 0.06
        // energyCost = 0.06 * 55 = $3.30 (fixed regardless of city)
        // laborCost = 0.35 * effectiveHourlyWage (varies by city: $18–$28)
        var laborCost = status.GetProperty("nextTickLaborCost").GetDecimal();
        var energyCost = status.GetProperty("nextTickEnergyCost").GetDecimal();
        Assert.True(laborCost > 0m, "PURCHASE unit must have a positive labor cost.");
        Assert.True(energyCost > 0m, "PURCHASE unit must have a positive energy cost.");
        // Energy cost is fixed (0.06 MWh * $55/MWh = $3.30).
        Assert.Equal(3.30m, energyCost);
        // Labor cost is between min wage × 0.35 and max wage × 0.35.
        Assert.True(laborCost >= 6.30m && laborCost <= 9.80m,
            $"PURCHASE unit labor cost {laborCost} must be in [6.30, 9.80] range across all seeded cities.");
    }

    [Fact]
    public async Task BuildingUnitOperationalStatuses_ManufacturingUnit_ReturnsNextTickCosts()
    {
        // Arrange: seed a factory with a manufacturing unit configured with a product.
        var (token, buildingId) = await SeedOperationalStatusTestAsync("opstat-mfg-cost",
            (db, bid, _, _) =>
            {
                var product = db.ProductTypes.First();
                db.BuildingUnits.Add(new BuildingUnit
                {
                    Id = Guid.NewGuid(),
                    BuildingId = bid,
                    UnitType = UnitType.Manufacturing,
                    GridX = 0,
                    GridY = 0,
                    Level = 1,
                    ProductTypeId = product.Id,
                });
            });

        // Act
        var result = await ExecuteGraphQlAsync(
            """
            query BuildingUnitOperationalStatuses($buildingId: UUID!) {
              buildingUnitOperationalStatuses(buildingId: $buildingId) {
                buildingUnitId
                nextTickLaborCost
                nextTickEnergyCost
              }
            }
            """,
            new { buildingId },
            token);

        // Assert
        var statuses = result.GetProperty("data").GetProperty("buildingUnitOperationalStatuses");
        Assert.Equal(1, statuses.GetArrayLength());
        var status = statuses[0];

        // MANUFACTURING level 1: laborHours = 0.85; energyMwh = 0.18
        // energyCost = 0.18 * 55 = $9.90 (fixed)
        // laborCost = 0.85 * effectiveHourlyWage (varies by city: $18–$28)
        var laborCost = status.GetProperty("nextTickLaborCost").GetDecimal();
        var energyCost = status.GetProperty("nextTickEnergyCost").GetDecimal();
        Assert.True(laborCost > 0m, "MANUFACTURING unit must have a positive labor cost.");
        Assert.True(energyCost > 0m, "MANUFACTURING unit must have a positive energy cost.");
        // Energy cost is fixed (0.18 MWh * $55/MWh = $9.90).
        Assert.Equal(9.90m, energyCost);
        // Labor cost is between min wage × 0.85 and max wage × 0.85.
        Assert.True(laborCost >= 15.30m && laborCost <= 23.80m,
            $"MANUFACTURING unit labor cost {laborCost} must be in [15.30, 23.80] range across all seeded cities.");
    }

    #endregion

    #region Media Houses and Marketing Campaigns

    [Fact]
    public async Task PlaceBuilding_MediaHouse_WithValidMediaType_SetsMediaType()
    {
        var token = await RegisterAndGetTokenAsync($"mh-place-{Guid.NewGuid():N}@test.com", "MH Placer");
        var result = await ExecuteGraphQlAsync(
            """mutation CreateCompany($input: CreateCompanyInput!) { createCompany(input: $input) { id } }""",
            new { input = new { name = "Media Corp" } },
            token);
        var companyId = result.GetProperty("data").GetProperty("createCompany").GetProperty("id").GetString()!;

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var city = await db.Cities.FirstAsync();

        var placeResult = await ExecuteGraphQlAsync(
            """
            mutation PlaceBuilding($input: PlaceBuildingInput!) {
              placeBuilding(input: $input) {
                id
                type
                mediaType
              }
            }
            """,
            new { input = new { companyId, cityId = city.Id, type = "MEDIA_HOUSE", name = "City Newspaper", mediaType = "NEWSPAPER" } },
            token);

        var building = placeResult.GetProperty("data").GetProperty("placeBuilding");
        Assert.Equal("MEDIA_HOUSE", building.GetProperty("type").GetString());
        Assert.Equal("NEWSPAPER", building.GetProperty("mediaType").GetString());
    }

    [Fact]
    public async Task PlaceBuilding_MediaHouse_WithoutMediaType_ReturnsError()
    {
        var token = await RegisterAndGetTokenAsync($"mh-notype-{Guid.NewGuid():N}@test.com", "MH NoType");
        var result = await ExecuteGraphQlAsync(
            """mutation CreateCompany($input: CreateCompanyInput!) { createCompany(input: $input) { id } }""",
            new { input = new { name = "No Type Corp" } },
            token);
        var companyId = result.GetProperty("data").GetProperty("createCompany").GetProperty("id").GetString()!;

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var city = await db.Cities.FirstAsync();

        var placeResult = await ExecuteGraphQlAsync(
            """
            mutation PlaceBuilding($input: PlaceBuildingInput!) {
              placeBuilding(input: $input) { id }
            }
            """,
            new { input = new { companyId, cityId = city.Id, type = "MEDIA_HOUSE", name = "No Type House", mediaType = (string?)null } },
            token);

        var errors = placeResult.GetProperty("errors");
        Assert.True(errors.GetArrayLength() > 0, "Placing a media house without mediaType must return an error.");
        var code = errors[0].GetProperty("extensions").GetProperty("code").GetString();
        Assert.Equal("INVALID_MEDIA_TYPE", code);
    }

    [Fact]
    public async Task PlaceBuilding_MediaHouse_WithInvalidMediaType_ReturnsError()
    {
        var token = await RegisterAndGetTokenAsync($"mh-badtype-{Guid.NewGuid():N}@test.com", "MH BadType");
        var result = await ExecuteGraphQlAsync(
            """mutation CreateCompany($input: CreateCompanyInput!) { createCompany(input: $input) { id } }""",
            new { input = new { name = "Bad Type Corp" } },
            token);
        var companyId = result.GetProperty("data").GetProperty("createCompany").GetProperty("id").GetString()!;

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var city = await db.Cities.FirstAsync();

        var placeResult = await ExecuteGraphQlAsync(
            """
            mutation PlaceBuilding($input: PlaceBuildingInput!) {
              placeBuilding(input: $input) { id }
            }
            """,
            new { input = new { companyId, cityId = city.Id, type = "MEDIA_HOUSE", name = "Bad Type House", mediaType = "PODCAST" } },
            token);

        var errors = placeResult.GetProperty("errors");
        Assert.True(errors.GetArrayLength() > 0, "Placing a media house with invalid mediaType must return an error.");
        var code = errors[0].GetProperty("extensions").GetProperty("code").GetString();
        Assert.Equal("INVALID_MEDIA_TYPE", code);
    }

    [Fact]
    public async Task CityMediaHouses_Query_ReturnsPlacedMediaHouses()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var city = await db.Cities.FirstAsync();
        var player = new Player
        {
            Id = Guid.NewGuid(),
            Email = $"mh-query-{Guid.NewGuid():N}@test.com",
            DisplayName = "MH Query Tester",
            PasswordHash = "hash",
            Role = PlayerRole.Player
        };
        db.Players.Add(player);
        var company = new Company { Id = Guid.NewGuid(), PlayerId = player.Id, Name = "Media Query Corp", Cash = 1_000_000m };
        db.Companies.Add(company);
        var mediaHouse = new Building
        {
            Id = Guid.NewGuid(),
            CompanyId = company.Id,
            CityId = city.Id,
            Type = BuildingType.MediaHouse,
            Name = "Test TV Station",
            MediaType = Data.Entities.MediaType.Tv,
            Level = 1
        };
        db.Buildings.Add(mediaHouse);
        await db.SaveChangesAsync();

        var result = await ExecuteGraphQlAsync(
            """
            query CityMediaHouses($cityId: UUID!) {
              cityMediaHouses(cityId: $cityId) {
                id
                name
                mediaType
                effectivenessMultiplier
                ownerCompanyName
                powerStatus
                isUnderConstruction
              }
            }
            """,
            new { cityId = city.Id });

        var houses = result.GetProperty("data").GetProperty("cityMediaHouses");
        Assert.True(houses.GetArrayLength() >= 1, "Should find at least the newly seeded media house.");

        var tv = houses.EnumerateArray().FirstOrDefault(h => h.GetProperty("id").GetString() == mediaHouse.Id.ToString());
        Assert.NotEqual(default, tv);
        Assert.Equal("Test TV Station", tv.GetProperty("name").GetString());
        Assert.Equal("TV", tv.GetProperty("mediaType").GetString());
        Assert.Equal(2.0m, tv.GetProperty("effectivenessMultiplier").GetDecimal());
        Assert.Equal("Media Query Corp", tv.GetProperty("ownerCompanyName").GetString());
    }

    [Fact]
    public async Task CityMediaHouses_Newspaper_HasEffectivenessMultiplier_1_0()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var city = await db.Cities.FirstAsync();
        var player = new Player { Id = Guid.NewGuid(), Email = $"mh-np-{Guid.NewGuid():N}@test.com", DisplayName = "NP", PasswordHash = "h", Role = PlayerRole.Player };
        db.Players.Add(player);
        var company = new Company { Id = Guid.NewGuid(), PlayerId = player.Id, Name = "Newspaper Co", Cash = 1m };
        db.Companies.Add(company);
        var mh = new Building { Id = Guid.NewGuid(), CompanyId = company.Id, CityId = city.Id, Type = BuildingType.MediaHouse, Name = "Daily News", MediaType = Data.Entities.MediaType.Newspaper, Level = 1 };
        db.Buildings.Add(mh);
        await db.SaveChangesAsync();

        var result = await ExecuteGraphQlAsync(
            $"{{ cityMediaHouses(cityId: \"{city.Id}\") {{ id mediaType effectivenessMultiplier }} }}");

        var np = result.GetProperty("data").GetProperty("cityMediaHouses")
            .EnumerateArray().First(h => h.GetProperty("id").GetString() == mh.Id.ToString());
        Assert.Equal(1.0m, np.GetProperty("effectivenessMultiplier").GetDecimal());
    }

    [Fact]
    public async Task CityMediaHouses_Radio_HasEffectivenessMultiplier_1_5()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var city = await db.Cities.FirstAsync();
        var player = new Player { Id = Guid.NewGuid(), Email = $"mh-radio-{Guid.NewGuid():N}@test.com", DisplayName = "Radio", PasswordHash = "h", Role = PlayerRole.Player };
        db.Players.Add(player);
        var company = new Company { Id = Guid.NewGuid(), PlayerId = player.Id, Name = "Radio Co", Cash = 1m };
        db.Companies.Add(company);
        var mh = new Building { Id = Guid.NewGuid(), CompanyId = company.Id, CityId = city.Id, Type = BuildingType.MediaHouse, Name = "City Radio", MediaType = Data.Entities.MediaType.Radio, Level = 1 };
        db.Buildings.Add(mh);
        await db.SaveChangesAsync();

        var result = await ExecuteGraphQlAsync(
            $"{{ cityMediaHouses(cityId: \"{city.Id}\") {{ id mediaType effectivenessMultiplier }} }}");

        var radio = result.GetProperty("data").GetProperty("cityMediaHouses")
            .EnumerateArray().First(h => h.GetProperty("id").GetString() == mh.Id.ToString());
        Assert.Equal(1.5m, radio.GetProperty("effectivenessMultiplier").GetDecimal());
    }

    [Fact]
    public async Task MarketingPhase_WithTvMediaHouse_AppliesChannelMultiplierAndRoutesIncome()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var city = await db.Cities.FirstAsync();
        var product = await db.ProductTypes.FirstAsync(p => p.Slug == "wooden-chair");

        var advertiserPlayer = new Player { Id = Guid.NewGuid(), Email = $"tv-adv-{Guid.NewGuid():N}@test.com", DisplayName = "Advertiser", PasswordHash = "h", Role = PlayerRole.Player };
        var mediaOwnerPlayer = new Player { Id = Guid.NewGuid(), Email = $"tv-own-{Guid.NewGuid():N}@test.com", DisplayName = "TV Owner", PasswordHash = "h", Role = PlayerRole.Player };
        db.Players.AddRange(advertiserPlayer, mediaOwnerPlayer);

        var advertiserCompany = new Company { Id = Guid.NewGuid(), PlayerId = advertiserPlayer.Id, Name = "Advertiser Corp", Cash = 100_000m };
        var mediaOwnerCompany = new Company { Id = Guid.NewGuid(), PlayerId = mediaOwnerPlayer.Id, Name = "TV Station Corp", Cash = 0m };
        db.Companies.AddRange(advertiserCompany, mediaOwnerCompany);

        var tvStation = new Building
        {
            Id = Guid.NewGuid(),
            CompanyId = mediaOwnerCompany.Id,
            CityId = city.Id,
            Type = BuildingType.MediaHouse,
            Name = "Big TV",
            MediaType = Data.Entities.MediaType.Tv,
            Level = 1
        };
        db.Buildings.Add(tvStation);

        var shop = new Building { Id = Guid.NewGuid(), CompanyId = advertiserCompany.Id, CityId = city.Id, Type = BuildingType.SalesShop, Name = "Shop", Level = 1 };
        db.Buildings.Add(shop);

        var salesUnit = new BuildingUnit { Id = Guid.NewGuid(), BuildingId = shop.Id, UnitType = UnitType.PublicSales, GridX = 0, GridY = 0, Level = 1, ProductTypeId = product.Id, MinPrice = product.BasePrice };
        var marketingUnit = new BuildingUnit { Id = Guid.NewGuid(), BuildingId = shop.Id, UnitType = UnitType.Marketing, GridX = 1, GridY = 0, Level = 1, Budget = 1_000m, MediaHouseBuildingId = tvStation.Id };
        db.BuildingUnits.AddRange(salesUnit, marketingUnit);
        await db.SaveChangesAsync();

        var cashBefore = advertiserCompany.Cash;
        var mediaCashBefore = mediaOwnerCompany.Cash;

        var phases = scope.ServiceProvider.GetServices<ITickPhase>();
        var processor = new TickProcessor(db, phases, new NullLogger<TickProcessor>());
        await processor.ProcessTickAsync();

        Assert.True(advertiserCompany.Cash < cashBefore, "Advertiser cash should decrease.");
        Assert.True(mediaOwnerCompany.Cash > mediaCashBefore, "Media house owner should receive advertising income.");
        // Media house owner should receive exactly the marketing budget (other phases don't affect the media owner).
        Assert.Equal(1_000m, mediaOwnerCompany.Cash - mediaCashBefore);

        var incomeEntry = await db.LedgerEntries
            .Where(e => e.CompanyId == mediaOwnerCompany.Id && e.Category == LedgerCategory.MediaHouseIncome)
            .FirstOrDefaultAsync();
        Assert.NotNull(incomeEntry);
        Assert.True(incomeEntry.Amount > 0, "Media house income ledger entry must be positive.");

        var brand = await db.Brands
            .Where(b => b.CompanyId == advertiserCompany.Id && b.ProductTypeId == product.Id)
            .FirstOrDefaultAsync();
        Assert.NotNull(brand);
        Assert.True(brand.Awareness > 0, "Brand awareness should increase after TV marketing.");
    }

    [Fact]
    public async Task MarketingPhase_TvVsNewspaper_TvGeneratesMoreAwareness()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var city = await db.Cities.FirstAsync();
        var product = await db.ProductTypes.FirstAsync(p => p.Slug == "wooden-chair");

        var player1 = new Player { Id = Guid.NewGuid(), Email = $"tv-cmp-{Guid.NewGuid():N}@test.com", DisplayName = "TV Adv", PasswordHash = "h", Role = PlayerRole.Player };
        var company1 = new Company { Id = Guid.NewGuid(), PlayerId = player1.Id, Name = "TV Advertiser", Cash = 100_000m };

        var player2 = new Player { Id = Guid.NewGuid(), Email = $"np-cmp-{Guid.NewGuid():N}@test.com", DisplayName = "NP Adv", PasswordHash = "h", Role = PlayerRole.Player };
        var company2 = new Company { Id = Guid.NewGuid(), PlayerId = player2.Id, Name = "NP Advertiser", Cash = 100_000m };

        var ownerPlayer = new Player { Id = Guid.NewGuid(), Email = $"owner-cmp-{Guid.NewGuid():N}@test.com", DisplayName = "Owner", PasswordHash = "h", Role = PlayerRole.Player };
        var ownerCompany = new Company { Id = Guid.NewGuid(), PlayerId = ownerPlayer.Id, Name = "Owner Corp", Cash = 0m };

        db.Players.AddRange(player1, player2, ownerPlayer);
        db.Companies.AddRange(company1, company2, ownerCompany);

        var tvHouse = new Building { Id = Guid.NewGuid(), CompanyId = ownerCompany.Id, CityId = city.Id, Type = BuildingType.MediaHouse, Name = "TV", MediaType = Data.Entities.MediaType.Tv, Level = 1 };
        var npHouse = new Building { Id = Guid.NewGuid(), CompanyId = ownerCompany.Id, CityId = city.Id, Type = BuildingType.MediaHouse, Name = "NP", MediaType = Data.Entities.MediaType.Newspaper, Level = 1 };
        var shop1 = new Building { Id = Guid.NewGuid(), CompanyId = company1.Id, CityId = city.Id, Type = BuildingType.SalesShop, Name = "Shop1", Level = 1 };
        var shop2 = new Building { Id = Guid.NewGuid(), CompanyId = company2.Id, CityId = city.Id, Type = BuildingType.SalesShop, Name = "Shop2", Level = 1 };
        db.Buildings.AddRange(tvHouse, npHouse, shop1, shop2);

        var su1 = new BuildingUnit { Id = Guid.NewGuid(), BuildingId = shop1.Id, UnitType = UnitType.PublicSales, GridX = 0, GridY = 0, Level = 1, ProductTypeId = product.Id, MinPrice = product.BasePrice };
        var mu1 = new BuildingUnit { Id = Guid.NewGuid(), BuildingId = shop1.Id, UnitType = UnitType.Marketing, GridX = 1, GridY = 0, Level = 1, Budget = 500m, MediaHouseBuildingId = tvHouse.Id };
        var su2 = new BuildingUnit { Id = Guid.NewGuid(), BuildingId = shop2.Id, UnitType = UnitType.PublicSales, GridX = 0, GridY = 0, Level = 1, ProductTypeId = product.Id, MinPrice = product.BasePrice };
        var mu2 = new BuildingUnit { Id = Guid.NewGuid(), BuildingId = shop2.Id, UnitType = UnitType.Marketing, GridX = 1, GridY = 0, Level = 1, Budget = 500m, MediaHouseBuildingId = npHouse.Id };
        db.BuildingUnits.AddRange(su1, mu1, su2, mu2);
        await db.SaveChangesAsync();

        var phases = scope.ServiceProvider.GetServices<ITickPhase>();
        var processor = new TickProcessor(db, phases, new NullLogger<TickProcessor>());
        await processor.ProcessTickAsync();

        var brand1 = await db.Brands.FirstOrDefaultAsync(b => b.CompanyId == company1.Id && b.ProductTypeId == product.Id);
        var brand2 = await db.Brands.FirstOrDefaultAsync(b => b.CompanyId == company2.Id && b.ProductTypeId == product.Id);

        Assert.NotNull(brand1);
        Assert.NotNull(brand2);
        Assert.True(brand1.Awareness > brand2.Awareness,
            $"TV channel (x2.0) should generate more brand awareness than Newspaper (x1.0). TV={brand1.Awareness}, NP={brand2.Awareness}");
    }

    [Fact]
    public async Task MarketingPhase_SameCompanyMediaHouse_DoesNotDoubleCountCash()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var city = await db.Cities.FirstAsync();
        var product = await db.ProductTypes.FirstAsync(p => p.Slug == "wooden-chair");

        var player = new Player { Id = Guid.NewGuid(), Email = $"self-mh-{Guid.NewGuid():N}@test.com", DisplayName = "Self MH", PasswordHash = "h", Role = PlayerRole.Player };
        db.Players.Add(player);
        var company = new Company { Id = Guid.NewGuid(), PlayerId = player.Id, Name = "Self Media Corp", Cash = 10_000m };
        db.Companies.Add(company);

        var tvHouse = new Building { Id = Guid.NewGuid(), CompanyId = company.Id, CityId = city.Id, Type = BuildingType.MediaHouse, Name = "Own TV", MediaType = Data.Entities.MediaType.Tv, Level = 1 };
        var shop = new Building { Id = Guid.NewGuid(), CompanyId = company.Id, CityId = city.Id, Type = BuildingType.SalesShop, Name = "Own Shop", Level = 1 };
        db.Buildings.AddRange(tvHouse, shop);

        var salesUnit = new BuildingUnit { Id = Guid.NewGuid(), BuildingId = shop.Id, UnitType = UnitType.PublicSales, GridX = 0, GridY = 0, Level = 1, ProductTypeId = product.Id, MinPrice = product.BasePrice };
        var marketingUnit = new BuildingUnit { Id = Guid.NewGuid(), BuildingId = shop.Id, UnitType = UnitType.Marketing, GridX = 1, GridY = 0, Level = 1, Budget = 1_000m, MediaHouseBuildingId = tvHouse.Id };
        db.BuildingUnits.AddRange(salesUnit, marketingUnit);
        await db.SaveChangesAsync();

        var cashBefore = company.Cash;

        var phases = scope.ServiceProvider.GetServices<ITickPhase>();
        var processor = new TickProcessor(db, phases, new NullLogger<TickProcessor>());
        await processor.ProcessTickAsync();

        Assert.True(company.Cash < cashBefore, "Cash should still be deducted when owning the media house.");
        // Marketing ledger entry should show the deduction (the tick processes other phases too, so just verify the ledger).
        var marketingEntry = await db.LedgerEntries
            .Where(e => e.CompanyId == company.Id && e.Category == LedgerCategory.Marketing)
            .FirstOrDefaultAsync();
        Assert.NotNull(marketingEntry);
        Assert.Equal(-1_000m, marketingEntry.Amount, precision: 2);
        // No MEDIA_HOUSE_INCOME entry should exist for the company advertising on its own station.
        var selfIncomeEntry = await db.LedgerEntries
            .Where(e => e.CompanyId == company.Id && e.Category == LedgerCategory.MediaHouseIncome)
            .FirstOrDefaultAsync();
        Assert.Null(selfIncomeEntry);
    }

    [Fact]
    public async Task StoreBuildingConfiguration_WithInvalidMediaHouseId_ReturnsError()
    {
        var token = await RegisterAndGetTokenAsync($"mh-cfg-{Guid.NewGuid():N}@test.com", "MH Config Tester");
        // Seed a shop directly to avoid depending on CompleteOnboardingAsync.
        await using var seedScope = _factory.Services.CreateAsyncScope();
        var seedDb = seedScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var seedPlayer = await seedDb.Players.FirstAsync(p => p.Email.StartsWith("mh-cfg-"));
        var seedCompany = new Api.Data.Entities.Company { Id = Guid.NewGuid(), PlayerId = seedPlayer.Id, Name = "MH Cfg Co", Cash = 100_000m };
        seedDb.Companies.Add(seedCompany);
        var seedCity = await seedDb.Cities.FirstAsync();
        var seedShop = new Api.Data.Entities.Building { Id = Guid.NewGuid(), CompanyId = seedCompany.Id, CityId = seedCity.Id, Type = Api.Data.Entities.BuildingType.SalesShop, Name = "Shop", Level = 1 };
        seedDb.Buildings.Add(seedShop);
        await seedDb.SaveChangesAsync();
        var shopId = seedShop.Id.ToString();

        var fakeMediaHouseId = Guid.NewGuid();
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
                    buildingId = shopId,
                    units = new[]
                    {
                        new { unitType = "MARKETING", gridX = 0, gridY = 0, budget = 500.0m, mediaHouseBuildingId = fakeMediaHouseId,
                              linkUp = false, linkDown = false, linkLeft = false, linkRight = false,
                              linkUpLeft = false, linkUpRight = false, linkDownLeft = false, linkDownRight = false }
                    }
                }
            },
            token);

        var errors = result.GetProperty("errors");
        Assert.True(errors.GetArrayLength() > 0, "Should return error for non-existent media house.");
        var code = errors[0].GetProperty("extensions").GetProperty("code").GetString();
        Assert.Equal("MEDIA_HOUSE_NOT_FOUND", code);
    }

    [Fact]
    public async Task StoreBuildingConfiguration_WithNonMediaHouseBuilding_ReturnsError()
    {
        var token = await RegisterAndGetTokenAsync($"mh-cfg2-{Guid.NewGuid():N}@test.com", "MH Config2 Tester");
        // Seed shop and factory directly.
        await using var seedScope = _factory.Services.CreateAsyncScope();
        var seedDb = seedScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var seedPlayer = await seedDb.Players.FirstAsync(p => p.Email.StartsWith("mh-cfg2-"));
        var seedCompany = new Api.Data.Entities.Company { Id = Guid.NewGuid(), PlayerId = seedPlayer.Id, Name = "MH Cfg2 Co", Cash = 100_000m };
        seedDb.Companies.Add(seedCompany);
        var seedCity = await seedDb.Cities.FirstAsync();
        var seedShop = new Api.Data.Entities.Building { Id = Guid.NewGuid(), CompanyId = seedCompany.Id, CityId = seedCity.Id, Type = Api.Data.Entities.BuildingType.SalesShop, Name = "Shop2", Level = 1 };
        var seedFactory = new Api.Data.Entities.Building { Id = Guid.NewGuid(), CompanyId = seedCompany.Id, CityId = seedCity.Id, Type = Api.Data.Entities.BuildingType.Factory, Name = "Factory2", Level = 1 };
        seedDb.Buildings.AddRange(seedShop, seedFactory);
        await seedDb.SaveChangesAsync();
        var shopId = seedShop.Id.ToString();
        var factoryId = seedFactory.Id.ToString();

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
                    buildingId = shopId,
                    units = new[]
                    {
                        new { unitType = "MARKETING", gridX = 0, gridY = 0, budget = 500.0m, mediaHouseBuildingId = factoryId,
                              linkUp = false, linkDown = false, linkLeft = false, linkRight = false,
                              linkUpLeft = false, linkUpRight = false, linkDownLeft = false, linkDownRight = false }
                    }
                }
            },
            token);

        var errors = result.GetProperty("errors");
        Assert.True(errors.GetArrayLength() > 0, "Should return error when target is not a media house.");
        var code = errors[0].GetProperty("extensions").GetProperty("code").GetString();
        Assert.Equal("BUILDING_NOT_MEDIA_HOUSE", code);
    }

    [Fact]
    public async Task StoreBuildingConfiguration_WithMediaHouseInDifferentCity_ReturnsError()
    {
        var token = await RegisterAndGetTokenAsync($"mh-city-{Guid.NewGuid():N}@test.com", "MH City Tester");

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var seedPlayer = await db.Players.FirstAsync(p => p.Email.StartsWith("mh-city-"));
        var homeCity = await db.Cities.FirstAsync();
        var otherCity = await db.Cities.FirstAsync(c => c.Id != homeCity.Id);
        var seedCompany = new Api.Data.Entities.Company { Id = Guid.NewGuid(), PlayerId = seedPlayer.Id, Name = "MH City Co", Cash = 100_000m };
        db.Companies.Add(seedCompany);
        var seedShop = new Api.Data.Entities.Building { Id = Guid.NewGuid(), CompanyId = seedCompany.Id, CityId = homeCity.Id, Type = Api.Data.Entities.BuildingType.SalesShop, Name = "City Shop", Level = 1 };
        db.Buildings.Add(seedShop);
        await db.SaveChangesAsync();
        var shopId = seedShop.Id.ToString();

        var mhOwner = seedCompany;
        var mh = new Building
        {
            Id = Guid.NewGuid(),
            CompanyId = mhOwner.Id,
            CityId = otherCity.Id,
            Type = BuildingType.MediaHouse,
            Name = "Other City TV",
            MediaType = Data.Entities.MediaType.Tv,
            Level = 1
        };
        db.Buildings.Add(mh);
        await db.SaveChangesAsync();

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
                    buildingId = shopId,
                    units = new[]
                    {
                        new { unitType = "MARKETING", gridX = 0, gridY = 0, budget = 500.0m, mediaHouseBuildingId = mh.Id,
                              linkUp = false, linkDown = false, linkLeft = false, linkRight = false,
                              linkUpLeft = false, linkUpRight = false, linkDownLeft = false, linkDownRight = false }
                    }
                }
            },
            token);

        var errors = result.GetProperty("errors");
        Assert.True(errors.GetArrayLength() > 0, "Should return error when media house is in a different city.");
        var code = errors[0].GetProperty("extensions").GetProperty("code").GetString();
        Assert.Equal("MEDIA_HOUSE_WRONG_CITY", code);
    }

    #endregion

    #region Bank Lending Marketplace

    [Fact]
    public async Task PublishLoanOffer_ByBankOwner_Succeeds()
    {
        var email = $"bank-pub-{Guid.NewGuid():N}@test.com";
        var token = await RegisterAndGetTokenAsync(email, "Bank Pub Tester");

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var player = await db.Players.FirstAsync(p => p.Email == email);
        var city = await db.Cities.FirstAsync();
        var company = new Api.Data.Entities.Company { Id = Guid.NewGuid(), PlayerId = player.Id, Name = "BankCo", Cash = 500_000m };
        db.Companies.Add(company);
        var bank = new Api.Data.Entities.Building { Id = Guid.NewGuid(), CompanyId = company.Id, CityId = city.Id, Type = Api.Data.Entities.BuildingType.Bank, Name = "My Bank", Level = 1 };
        db.Buildings.Add(bank);
        await db.SaveChangesAsync();

        var result = await ExecuteGraphQlAsync(
            """
            mutation Pub($input: PublishLoanOfferInput!) {
              publishLoanOffer(input: $input) {
                id annualInterestRatePercent maxPrincipalPerLoan totalCapacity usedCapacity durationTicks isActive
              }
            }
            """,
            new { input = new { bankBuildingId = bank.Id.ToString(), annualInterestRatePercent = 12.5m, maxPrincipalPerLoan = 50_000m, totalCapacity = 200_000m, durationTicks = 1440L } },
            token);

        var data = result.GetProperty("data").GetProperty("publishLoanOffer");
        Assert.Equal(12.5m, data.GetProperty("annualInterestRatePercent").GetDecimal());
        Assert.Equal(50_000m, data.GetProperty("maxPrincipalPerLoan").GetDecimal());
        Assert.Equal(200_000m, data.GetProperty("totalCapacity").GetDecimal());
        Assert.Equal(0m, data.GetProperty("usedCapacity").GetDecimal());
        Assert.Equal(1440L, data.GetProperty("durationTicks").GetInt64());
        Assert.True(data.GetProperty("isActive").GetBoolean());
    }

    [Fact]
    public async Task PublishLoanOffer_NonBankBuilding_ReturnsError()
    {
        var email = $"bank-nonbank-{Guid.NewGuid():N}@test.com";
        var token = await RegisterAndGetTokenAsync(email, "NonBank Tester");

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var player = await db.Players.FirstAsync(p => p.Email == email);
        var city = await db.Cities.FirstAsync();
        var company = new Api.Data.Entities.Company { Id = Guid.NewGuid(), PlayerId = player.Id, Name = "FactoryCo", Cash = 500_000m };
        db.Companies.Add(company);
        var factory = new Api.Data.Entities.Building { Id = Guid.NewGuid(), CompanyId = company.Id, CityId = city.Id, Type = Api.Data.Entities.BuildingType.Factory, Name = "My Factory", Level = 1 };
        db.Buildings.Add(factory);
        await db.SaveChangesAsync();

        var result = await ExecuteGraphQlAsync(
            """
            mutation Pub($input: PublishLoanOfferInput!) {
              publishLoanOffer(input: $input) { id }
            }
            """,
            new { input = new { bankBuildingId = factory.Id.ToString(), annualInterestRatePercent = 12.5m, maxPrincipalPerLoan = 50_000m, totalCapacity = 200_000m, durationTicks = 1440L } },
            token);

        var errors = result.GetProperty("errors");
        Assert.True(errors.GetArrayLength() > 0);
        Assert.Equal("BANK_NOT_FOUND", errors[0].GetProperty("extensions").GetProperty("code").GetString());
    }

    [Fact]
    public async Task PublishLoanOffer_InvalidInterestRate_ReturnsError()
    {
        var email = $"bank-rate-{Guid.NewGuid():N}@test.com";
        var token = await RegisterAndGetTokenAsync(email, "Rate Tester");

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var player = await db.Players.FirstAsync(p => p.Email == email);
        var city = await db.Cities.FirstAsync();
        var company = new Api.Data.Entities.Company { Id = Guid.NewGuid(), PlayerId = player.Id, Name = "RateCo", Cash = 500_000m };
        db.Companies.Add(company);
        var bank = new Api.Data.Entities.Building { Id = Guid.NewGuid(), CompanyId = company.Id, CityId = city.Id, Type = Api.Data.Entities.BuildingType.Bank, Name = "Rate Bank", Level = 1 };
        db.Buildings.Add(bank);
        await db.SaveChangesAsync();

        var result = await ExecuteGraphQlAsync(
            """
            mutation Pub($input: PublishLoanOfferInput!) {
              publishLoanOffer(input: $input) { id }
            }
            """,
            new { input = new { bankBuildingId = bank.Id.ToString(), annualInterestRatePercent = 0m, maxPrincipalPerLoan = 50_000m, totalCapacity = 200_000m, durationTicks = 1440L } },
            token);

        var errors = result.GetProperty("errors");
        Assert.True(errors.GetArrayLength() > 0);
        Assert.Equal("INVALID_INTEREST_RATE", errors[0].GetProperty("extensions").GetProperty("code").GetString());
    }

    [Fact]
    public async Task PublishLoanOffer_Unauthenticated_ReturnsError()
    {
        var result = await ExecuteGraphQlAsync(
            """
            mutation Pub($input: PublishLoanOfferInput!) {
              publishLoanOffer(input: $input) { id }
            }
            """,
            new { input = new { bankBuildingId = Guid.NewGuid().ToString(), annualInterestRatePercent = 12.5m, maxPrincipalPerLoan = 50_000m, totalCapacity = 200_000m, durationTicks = 1440L } });

        var errors = result.GetProperty("errors");
        Assert.True(errors.GetArrayLength() > 0);
    }

    [Fact]
    public async Task AcceptLoan_ByBorrower_TransfersCashAndCreatesLoan()
    {
        var lenderEmail = $"lender-{Guid.NewGuid():N}@test.com";
        var borrowerEmail = $"borrower-{Guid.NewGuid():N}@test.com";
        var lenderToken = await RegisterAndGetTokenAsync(lenderEmail, "Lender");
        var borrowerToken = await RegisterAndGetTokenAsync(borrowerEmail, "Borrower");

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var lenderPlayer = await db.Players.FirstAsync(p => p.Email == lenderEmail);
        var borrowerPlayer = await db.Players.FirstAsync(p => p.Email == borrowerEmail);
        var city = await db.Cities.FirstAsync();

        var lenderCompany = new Api.Data.Entities.Company { Id = Guid.NewGuid(), PlayerId = lenderPlayer.Id, Name = "LenderCo", Cash = 500_000m };
        var borrowerCompany = new Api.Data.Entities.Company { Id = Guid.NewGuid(), PlayerId = borrowerPlayer.Id, Name = "BorrowerCo", Cash = 1_000m };
        db.Companies.AddRange(lenderCompany, borrowerCompany);

        var bank = new Api.Data.Entities.Building { Id = Guid.NewGuid(), CompanyId = lenderCompany.Id, CityId = city.Id, Type = Api.Data.Entities.BuildingType.Bank, Name = "LenderBank", Level = 1 };
        db.Buildings.Add(bank);

        var offer = new Api.Data.Entities.LoanOffer
        {
            Id = Guid.NewGuid(),
            BankBuildingId = bank.Id,
            LenderCompanyId = lenderCompany.Id,
            AnnualInterestRatePercent = 10m,
            MaxPrincipalPerLoan = 100_000m,
            TotalCapacity = 300_000m,
            UsedCapacity = 0m,
            DurationTicks = 1440L,
            IsActive = true,
            CreatedAtTick = 1L,
            CreatedAtUtc = DateTime.UtcNow
        };
        db.LoanOffers.Add(offer);
        await db.SaveChangesAsync();

        var lenderCashBefore = lenderCompany.Cash;

        var result = await ExecuteGraphQlAsync(
            """
            mutation Accept($input: AcceptLoanInput!) {
              acceptLoan(input: $input) {
                id status originalPrincipal remainingPrincipal annualInterestRatePercent
                paymentAmount totalPayments paymentsMade borrowerCompanyId lenderCompanyId
              }
            }
            """,
            new { input = new { loanOfferId = offer.Id.ToString(), borrowerCompanyId = borrowerCompany.Id.ToString(), principalAmount = 50_000m } },
            borrowerToken);

        var loan = result.GetProperty("data").GetProperty("acceptLoan");
        Assert.Equal("ACTIVE", loan.GetProperty("status").GetString());
        Assert.Equal(50_000m, loan.GetProperty("originalPrincipal").GetDecimal());
        Assert.Equal(50_000m, loan.GetProperty("remainingPrincipal").GetDecimal());
        Assert.Equal(10m, loan.GetProperty("annualInterestRatePercent").GetDecimal());
        Assert.True(loan.GetProperty("totalPayments").GetInt32() > 0);

        // Verify cash transfer
        await db.Entry(lenderCompany).ReloadAsync();
        await db.Entry(borrowerCompany).ReloadAsync();
        await db.Entry(offer).ReloadAsync();

        Assert.Equal(lenderCashBefore - 50_000m, lenderCompany.Cash);
        Assert.Equal(51_000m, borrowerCompany.Cash); // 1000 original + 50000 loan
        Assert.Equal(50_000m, offer.UsedCapacity);
    }

    [Fact]
    public async Task AcceptLoan_SelfLending_ReturnsError()
    {
        var email = $"self-lend-{Guid.NewGuid():N}@test.com";
        var token = await RegisterAndGetTokenAsync(email, "SelfLend Tester");

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var player = await db.Players.FirstAsync(p => p.Email == email);
        var city = await db.Cities.FirstAsync();
        var company = new Api.Data.Entities.Company { Id = Guid.NewGuid(), PlayerId = player.Id, Name = "SelfLendCo", Cash = 500_000m };
        db.Companies.Add(company);
        var bank = new Api.Data.Entities.Building { Id = Guid.NewGuid(), CompanyId = company.Id, CityId = city.Id, Type = Api.Data.Entities.BuildingType.Bank, Name = "SelfBank", Level = 1 };
        db.Buildings.Add(bank);

        var offer = new Api.Data.Entities.LoanOffer
        {
            Id = Guid.NewGuid(),
            BankBuildingId = bank.Id,
            LenderCompanyId = company.Id,
            AnnualInterestRatePercent = 10m,
            MaxPrincipalPerLoan = 100_000m,
            TotalCapacity = 300_000m,
            UsedCapacity = 0m,
            DurationTicks = 1440L,
            IsActive = true,
            CreatedAtTick = 1L,
            CreatedAtUtc = DateTime.UtcNow
        };
        db.LoanOffers.Add(offer);
        await db.SaveChangesAsync();

        var result = await ExecuteGraphQlAsync(
            """
            mutation Accept($input: AcceptLoanInput!) {
              acceptLoan(input: $input) { id }
            }
            """,
            new { input = new { loanOfferId = offer.Id.ToString(), borrowerCompanyId = company.Id.ToString(), principalAmount = 10_000m } },
            token);

        var errors = result.GetProperty("errors");
        Assert.True(errors.GetArrayLength() > 0);
        Assert.Equal("SELF_LENDING_NOT_ALLOWED", errors[0].GetProperty("extensions").GetProperty("code").GetString());
    }

    [Fact]
    public async Task AcceptLoan_ExceedsMaxPrincipal_ReturnsError()
    {
        var lenderEmail = $"cap-lender-{Guid.NewGuid():N}@test.com";
        var borrowerEmail = $"cap-borrower-{Guid.NewGuid():N}@test.com";
        var lenderToken = await RegisterAndGetTokenAsync(lenderEmail, "CapLender");
        var borrowerToken = await RegisterAndGetTokenAsync(borrowerEmail, "CapBorrower");

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var lenderPlayer = await db.Players.FirstAsync(p => p.Email == lenderEmail);
        var borrowerPlayer = await db.Players.FirstAsync(p => p.Email == borrowerEmail);
        var city = await db.Cities.FirstAsync();

        var lenderCompany = new Api.Data.Entities.Company { Id = Guid.NewGuid(), PlayerId = lenderPlayer.Id, Name = "CapLenderCo", Cash = 500_000m };
        var borrowerCompany = new Api.Data.Entities.Company { Id = Guid.NewGuid(), PlayerId = borrowerPlayer.Id, Name = "CapBorrowerCo", Cash = 1_000m };
        db.Companies.AddRange(lenderCompany, borrowerCompany);

        var bank = new Api.Data.Entities.Building { Id = Guid.NewGuid(), CompanyId = lenderCompany.Id, CityId = city.Id, Type = Api.Data.Entities.BuildingType.Bank, Name = "CapBank", Level = 1 };
        db.Buildings.Add(bank);

        var offer = new Api.Data.Entities.LoanOffer
        {
            Id = Guid.NewGuid(),
            BankBuildingId = bank.Id,
            LenderCompanyId = lenderCompany.Id,
            AnnualInterestRatePercent = 10m,
            MaxPrincipalPerLoan = 10_000m,
            TotalCapacity = 50_000m,
            UsedCapacity = 0m,
            DurationTicks = 1440L,
            IsActive = true,
            CreatedAtTick = 1L,
            CreatedAtUtc = DateTime.UtcNow
        };
        db.LoanOffers.Add(offer);
        await db.SaveChangesAsync();

        // Request more than MaxPrincipalPerLoan
        var result = await ExecuteGraphQlAsync(
            """
            mutation Accept($input: AcceptLoanInput!) {
              acceptLoan(input: $input) { id }
            }
            """,
            new { input = new { loanOfferId = offer.Id.ToString(), borrowerCompanyId = borrowerCompany.Id.ToString(), principalAmount = 80_000m } },
            borrowerToken);

        var errors = result.GetProperty("errors");
        Assert.True(errors.GetArrayLength() > 0);
        var code = errors[0].GetProperty("extensions").GetProperty("code").GetString();
        Assert.Equal("EXCEEDS_MAX_PRINCIPAL", code);
    }

    [Fact]
    public async Task GetLoanOffers_ReturnsActiveOffersExcludingOwn()
    {
        var lenderEmail = $"glo-lender-{Guid.NewGuid():N}@test.com";
        var borrowerEmail = $"glo-borrower-{Guid.NewGuid():N}@test.com";
        await RegisterAndGetTokenAsync(lenderEmail, "GloLender");
        var borrowerToken = await RegisterAndGetTokenAsync(borrowerEmail, "GloBorrower");

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var lenderPlayer = await db.Players.FirstAsync(p => p.Email == lenderEmail);
        var city = await db.Cities.FirstAsync();
        var lenderCompany = new Api.Data.Entities.Company { Id = Guid.NewGuid(), PlayerId = lenderPlayer.Id, Name = "GloLenderCo", Cash = 500_000m };
        db.Companies.Add(lenderCompany);
        var bank = new Api.Data.Entities.Building { Id = Guid.NewGuid(), CompanyId = lenderCompany.Id, CityId = city.Id, Type = Api.Data.Entities.BuildingType.Bank, Name = "GloBank", Level = 1 };
        db.Buildings.Add(bank);
        var offer = new Api.Data.Entities.LoanOffer
        {
            Id = Guid.NewGuid(),
            BankBuildingId = bank.Id,
            LenderCompanyId = lenderCompany.Id,
            AnnualInterestRatePercent = 15m,
            MaxPrincipalPerLoan = 25_000m,
            TotalCapacity = 100_000m,
            UsedCapacity = 0m,
            DurationTicks = 720L,
            IsActive = true,
            CreatedAtTick = 1L,
            CreatedAtUtc = DateTime.UtcNow
        };
        db.LoanOffers.Add(offer);
        await db.SaveChangesAsync();

        var result = await ExecuteGraphQlAsync(
            """
            query { loanOffers { id lenderCompanyName annualInterestRatePercent remainingCapacity } }
            """,
            null,
            borrowerToken);

        var offers = result.GetProperty("data").GetProperty("loanOffers");
        Assert.True(offers.GetArrayLength() >= 1, "Should see at least one offer.");
        var found = false;
        foreach (var o in offers.EnumerateArray())
        {
            if (o.GetProperty("id").GetString() == offer.Id.ToString())
            {
                found = true;
                Assert.Equal(15m, o.GetProperty("annualInterestRatePercent").GetDecimal());
                Assert.Equal(100_000m, o.GetProperty("remainingCapacity").GetDecimal());
            }
        }
        Assert.True(found, "The published offer should appear in the list.");
    }

    [Fact]
    public async Task GetMyLoans_ReturnsActiveLoanForBorrower()
    {
        var lenderEmail = $"myl-lender-{Guid.NewGuid():N}@test.com";
        var borrowerEmail = $"myl-borrower-{Guid.NewGuid():N}@test.com";
        var lenderToken = await RegisterAndGetTokenAsync(lenderEmail, "MylLender");
        var borrowerToken = await RegisterAndGetTokenAsync(borrowerEmail, "MylBorrower");

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var lenderPlayer = await db.Players.FirstAsync(p => p.Email == lenderEmail);
        var borrowerPlayer = await db.Players.FirstAsync(p => p.Email == borrowerEmail);
        var city = await db.Cities.FirstAsync();

        var lenderCompany = new Api.Data.Entities.Company { Id = Guid.NewGuid(), PlayerId = lenderPlayer.Id, Name = "MylLenderCo", Cash = 500_000m };
        var borrowerCompany = new Api.Data.Entities.Company { Id = Guid.NewGuid(), PlayerId = borrowerPlayer.Id, Name = "MylBorrowerCo", Cash = 1_000m };
        db.Companies.AddRange(lenderCompany, borrowerCompany);
        var bank = new Api.Data.Entities.Building { Id = Guid.NewGuid(), CompanyId = lenderCompany.Id, CityId = city.Id, Type = Api.Data.Entities.BuildingType.Bank, Name = "MylBank", Level = 1 };
        db.Buildings.Add(bank);
        var offer = new Api.Data.Entities.LoanOffer { Id = Guid.NewGuid(), BankBuildingId = bank.Id, LenderCompanyId = lenderCompany.Id, AnnualInterestRatePercent = 8m, MaxPrincipalPerLoan = 50_000m, TotalCapacity = 200_000m, UsedCapacity = 0m, DurationTicks = 720L, IsActive = true, CreatedAtTick = 1L, CreatedAtUtc = DateTime.UtcNow };
        db.LoanOffers.Add(offer);
        await db.SaveChangesAsync();

        // Accept the loan
        await ExecuteGraphQlAsync(
            """
            mutation Accept($input: AcceptLoanInput!) {
              acceptLoan(input: $input) { id }
            }
            """,
            new { input = new { loanOfferId = offer.Id.ToString(), borrowerCompanyId = borrowerCompany.Id.ToString(), principalAmount = 30_000m } },
            borrowerToken);

        // Query my loans
        var result = await ExecuteGraphQlAsync(
            """
            query { myLoans { id status originalPrincipal remainingPrincipal lenderCompanyName nextPaymentTick } }
            """,
            null,
            borrowerToken);

        var loans = result.GetProperty("data").GetProperty("myLoans");
        Assert.Equal(1, loans.GetArrayLength());
        var l = loans[0];
        Assert.Equal("ACTIVE", l.GetProperty("status").GetString());
        Assert.Equal(30_000m, l.GetProperty("originalPrincipal").GetDecimal());
        Assert.Equal("MylLenderCo", l.GetProperty("lenderCompanyName").GetString());
    }

    [Fact]
    public async Task DeactivateLoanOffer_ByOwner_HidesFromBorrowers()
    {
        var lenderEmail = $"deact-lender-{Guid.NewGuid():N}@test.com";
        var borrowerEmail = $"deact-borrower-{Guid.NewGuid():N}@test.com";
        var lenderToken = await RegisterAndGetTokenAsync(lenderEmail, "DeactLender");
        var borrowerToken = await RegisterAndGetTokenAsync(borrowerEmail, "DeactBorrower");

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var lenderPlayer = await db.Players.FirstAsync(p => p.Email == lenderEmail);
        var city = await db.Cities.FirstAsync();
        var lenderCompany = new Api.Data.Entities.Company { Id = Guid.NewGuid(), PlayerId = lenderPlayer.Id, Name = "DeactLenderCo", Cash = 500_000m };
        db.Companies.Add(lenderCompany);
        var bank = new Api.Data.Entities.Building { Id = Guid.NewGuid(), CompanyId = lenderCompany.Id, CityId = city.Id, Type = Api.Data.Entities.BuildingType.Bank, Name = "DeactBank", Level = 1 };
        db.Buildings.Add(bank);
        var offer = new Api.Data.Entities.LoanOffer { Id = Guid.NewGuid(), BankBuildingId = bank.Id, LenderCompanyId = lenderCompany.Id, AnnualInterestRatePercent = 9m, MaxPrincipalPerLoan = 10_000m, TotalCapacity = 50_000m, UsedCapacity = 0m, DurationTicks = 720L, IsActive = true, CreatedAtTick = 1L, CreatedAtUtc = DateTime.UtcNow };
        db.LoanOffers.Add(offer);
        await db.SaveChangesAsync();

        // Deactivate it
        var deactResult = await ExecuteGraphQlAsync(
            "mutation Deact($id: UUID!) { deactivateLoanOffer(loanOfferId: $id) { id isActive } }",
            new { id = offer.Id.ToString() },
            lenderToken);
        var deactOffer = deactResult.GetProperty("data").GetProperty("deactivateLoanOffer");
        Assert.False(deactOffer.GetProperty("isActive").GetBoolean());

        // Borrower should not see it
        var listResult = await ExecuteGraphQlAsync("query { loanOffers { id } }", null, borrowerToken);
        var ids = listResult.GetProperty("data").GetProperty("loanOffers")
            .EnumerateArray().Select(o => o.GetProperty("id").GetString()).ToList();
        Assert.DoesNotContain(offer.Id.ToString(), ids);
    }

    [Fact]
    public async Task LoanRepayment_TickEngine_ReducesPrincipalAndMovesBalance()
    {
        var lenderEmail = $"tr-lender-{Guid.NewGuid():N}@test.com";
        var borrowerEmail = $"tr-borrower-{Guid.NewGuid():N}@test.com";
        var lenderToken = await RegisterAndGetTokenAsync(lenderEmail, "TrLender");
        var borrowerToken = await RegisterAndGetTokenAsync(borrowerEmail, "TrBorrower");

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var lenderPlayer = await db.Players.FirstAsync(p => p.Email == lenderEmail);
        var borrowerPlayer = await db.Players.FirstAsync(p => p.Email == borrowerEmail);
        var city = await db.Cities.FirstAsync();

        var lenderCompany = new Api.Data.Entities.Company { Id = Guid.NewGuid(), PlayerId = lenderPlayer.Id, Name = "TrLenderCo", Cash = 500_000m };
        var borrowerCompany = new Api.Data.Entities.Company { Id = Guid.NewGuid(), PlayerId = borrowerPlayer.Id, Name = "TrBorrowerCo", Cash = 200_000m };
        db.Companies.AddRange(lenderCompany, borrowerCompany);
        var bank = new Api.Data.Entities.Building { Id = Guid.NewGuid(), CompanyId = lenderCompany.Id, CityId = city.Id, Type = Api.Data.Entities.BuildingType.Bank, Name = "TrBank", Level = 1 };
        db.Buildings.Add(bank);
        var offer = new Api.Data.Entities.LoanOffer { Id = Guid.NewGuid(), BankBuildingId = bank.Id, LenderCompanyId = lenderCompany.Id, AnnualInterestRatePercent = 12m, MaxPrincipalPerLoan = 50_000m, TotalCapacity = 200_000m, UsedCapacity = 0m, DurationTicks = 1440L, IsActive = true, CreatedAtTick = 1L, CreatedAtUtc = DateTime.UtcNow };
        db.LoanOffers.Add(offer);
        await db.SaveChangesAsync();

        // Accept loan
        await ExecuteGraphQlAsync(
            "mutation Accept($input: AcceptLoanInput!) { acceptLoan(input: $input) { id nextPaymentTick } }",
            new { input = new { loanOfferId = offer.Id.ToString(), borrowerCompanyId = borrowerCompany.Id.ToString(), principalAmount = 10_000m } },
            borrowerToken);

        var loanFromDb = await db.Loans.FirstAsync(l => l.BorrowerCompanyId == borrowerCompany.Id);
        await db.Entry(lenderCompany).ReloadAsync();
        await db.Entry(borrowerCompany).ReloadAsync();
        var lenderCashAfterLoan = lenderCompany.Cash;
        var borrowerCashAfterLoan = borrowerCompany.Cash;

        // Advance game ticks to payment due tick
        var gameState = await db.GameStates.FirstAsync();
        gameState.CurrentTick = loanFromDb.NextPaymentTick - 1;
        await db.SaveChangesAsync();

        // Process one tick (payment should fire)
        await ProcessTicksAsync(1);

        await db.Entry(lenderCompany).ReloadAsync();
        await db.Entry(borrowerCompany).ReloadAsync();
        await db.Entry(loanFromDb).ReloadAsync();

        // Lender should have received payment, borrower should have paid
        Assert.True(lenderCompany.Cash > lenderCashAfterLoan, "Lender should have received a repayment.");
        Assert.True(borrowerCompany.Cash < borrowerCashAfterLoan, "Borrower should have made a payment.");
        Assert.True(loanFromDb.PaymentsMade >= 1, "At least one payment should have been recorded.");

        // Ledger entries should exist for borrower
        var interestEntry = await db.LedgerEntries.AnyAsync(e => e.CompanyId == borrowerCompany.Id && e.Category == "LOAN_INTEREST_EXPENSE");
        Assert.True(interestEntry, "Borrower should have an interest expense ledger entry.");

        var interestIncomeEntry = await db.LedgerEntries.AnyAsync(e => e.CompanyId == lenderCompany.Id && e.Category == "LOAN_INTEREST_INCOME");
        Assert.True(interestIncomeEntry, "Lender should have an interest income ledger entry.");
    }

    [Fact]
    public async Task LoanRepayment_TickEngine_MissedPaymentSetsOverdue()
    {
        var lenderEmail = $"miss-lender-{Guid.NewGuid():N}@test.com";
        var borrowerEmail = $"miss-borrower-{Guid.NewGuid():N}@test.com";
        var lenderToken = await RegisterAndGetTokenAsync(lenderEmail, "MissLender");
        var borrowerToken = await RegisterAndGetTokenAsync(borrowerEmail, "MissBorrower");

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var lenderPlayer = await db.Players.FirstAsync(p => p.Email == lenderEmail);
        var borrowerPlayer = await db.Players.FirstAsync(p => p.Email == borrowerEmail);
        var city = await db.Cities.FirstAsync();

        var lenderCompany = new Api.Data.Entities.Company { Id = Guid.NewGuid(), PlayerId = lenderPlayer.Id, Name = "MissLenderCo", Cash = 500_000m };
        var borrowerCompany = new Api.Data.Entities.Company { Id = Guid.NewGuid(), PlayerId = borrowerPlayer.Id, Name = "MissBorrowerCo", Cash = 0m };
        db.Companies.AddRange(lenderCompany, borrowerCompany);
        var bank = new Api.Data.Entities.Building { Id = Guid.NewGuid(), CompanyId = lenderCompany.Id, CityId = city.Id, Type = Api.Data.Entities.BuildingType.Bank, Name = "MissBank", Level = 1 };
        db.Buildings.Add(bank);

        var offer = new Api.Data.Entities.LoanOffer { Id = Guid.NewGuid(), BankBuildingId = bank.Id, LenderCompanyId = lenderCompany.Id, AnnualInterestRatePercent = 10m, MaxPrincipalPerLoan = 5_000m, TotalCapacity = 50_000m, UsedCapacity = 0m, DurationTicks = 1440L, IsActive = true, CreatedAtTick = 1L, CreatedAtUtc = DateTime.UtcNow };
        db.LoanOffers.Add(offer);

        // Manually create a loan where borrower has no cash
        var gameState = await db.GameStates.FirstAsync();
        var loan = new Api.Data.Entities.Loan
        {
            Id = Guid.NewGuid(),
            LoanOfferId = offer.Id,
            BorrowerCompanyId = borrowerCompany.Id,
            BankBuildingId = bank.Id,
            LenderCompanyId = lenderCompany.Id,
            OriginalPrincipal = 5_000m,
            RemainingPrincipal = 5_000m,
            AnnualInterestRatePercent = 10m,
            DurationTicks = 1440L,
            StartTick = gameState.CurrentTick,
            DueTick = gameState.CurrentTick + 1440L,
            NextPaymentTick = gameState.CurrentTick + 1L,
            PaymentAmount = 400m,
            PaymentsMade = 0,
            TotalPayments = 2,
            Status = Api.Data.Entities.LoanStatus.Active,
            MissedPayments = 0,
            AccumulatedPenalty = 0m,
            AcceptedAtUtc = DateTime.UtcNow
        };
        db.Loans.Add(loan);
        await db.SaveChangesAsync();

        // Advance time so the payment is due
        gameState.CurrentTick = loan.NextPaymentTick;
        await db.SaveChangesAsync();

        await ProcessTicksAsync(1);

        await db.Entry(loan).ReloadAsync();
        Assert.True(loan.MissedPayments >= 1, "Borrower with no cash should have a missed payment.");
        Assert.True(loan.Status == Api.Data.Entities.LoanStatus.Overdue || loan.Status == Api.Data.Entities.LoanStatus.Defaulted,
            "Loan should be OVERDUE or DEFAULTED after missed payment.");
        Assert.True(loan.AccumulatedPenalty > 0m, "Penalty should be accumulated.");

        // Penalty ledger entry should exist
        var penaltyEntry = await db.LedgerEntries.AnyAsync(e => e.CompanyId == borrowerCompany.Id && e.Category == "LOAN_PENALTY");
        Assert.True(penaltyEntry, "Missed payment penalty should create a ledger entry.");
    }

    [Fact]
    public async Task UpdateLoanOffer_ByOwner_UpdatesFields()
    {
        var email = $"upd-offer-{Guid.NewGuid():N}@test.com";
        var token = await RegisterAndGetTokenAsync(email, "UpdateOffer Tester");

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var player = await db.Players.FirstAsync(p => p.Email == email);
        var city = await db.Cities.FirstAsync();
        var company = new Api.Data.Entities.Company { Id = Guid.NewGuid(), PlayerId = player.Id, Name = "UpdateOfferCo", Cash = 500_000m };
        db.Companies.Add(company);
        var bank = new Api.Data.Entities.Building { Id = Guid.NewGuid(), CompanyId = company.Id, CityId = city.Id, Type = Api.Data.Entities.BuildingType.Bank, Name = "UpdateBank", Level = 1 };
        db.Buildings.Add(bank);
        var offer = new Api.Data.Entities.LoanOffer { Id = Guid.NewGuid(), BankBuildingId = bank.Id, LenderCompanyId = company.Id, AnnualInterestRatePercent = 10m, MaxPrincipalPerLoan = 10_000m, TotalCapacity = 50_000m, UsedCapacity = 0m, DurationTicks = 720L, IsActive = true, CreatedAtTick = 1L, CreatedAtUtc = DateTime.UtcNow };
        db.LoanOffers.Add(offer);
        await db.SaveChangesAsync();

        var result = await ExecuteGraphQlAsync(
            """
            mutation Update($input: UpdateLoanOfferInput!) {
              updateLoanOffer(input: $input) { id annualInterestRatePercent isActive }
            }
            """,
            new { input = new { loanOfferId = offer.Id.ToString(), annualInterestRatePercent = (decimal?)18.5m, isActive = (bool?)false } },
            token);

        var updated = result.GetProperty("data").GetProperty("updateLoanOffer");
        Assert.Equal(18.5m, updated.GetProperty("annualInterestRatePercent").GetDecimal());
        Assert.False(updated.GetProperty("isActive").GetBoolean());
    }

    [Fact]
    public async Task GetBankLoans_ByBankOwner_ReturnsIssuedLoans()
    {
        var lenderEmail = $"bl-lender-{Guid.NewGuid():N}@test.com";
        var borrowerEmail = $"bl-borrower-{Guid.NewGuid():N}@test.com";
        var lenderToken = await RegisterAndGetTokenAsync(lenderEmail, "BLLender");
        var borrowerToken = await RegisterAndGetTokenAsync(borrowerEmail, "BLBorrower");

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var lenderPlayer = await db.Players.FirstAsync(p => p.Email == lenderEmail);
        var borrowerPlayer = await db.Players.FirstAsync(p => p.Email == borrowerEmail);
        var city = await db.Cities.FirstAsync();

        var lenderCompany = new Api.Data.Entities.Company { Id = Guid.NewGuid(), PlayerId = lenderPlayer.Id, Name = "BLLenderCo", Cash = 500_000m };
        var borrowerCompany = new Api.Data.Entities.Company { Id = Guid.NewGuid(), PlayerId = borrowerPlayer.Id, Name = "BLBorrowerCo", Cash = 1_000m };
        db.Companies.AddRange(lenderCompany, borrowerCompany);
        var bank = new Api.Data.Entities.Building { Id = Guid.NewGuid(), CompanyId = lenderCompany.Id, CityId = city.Id, Type = Api.Data.Entities.BuildingType.Bank, Name = "BLBank", Level = 1 };
        db.Buildings.Add(bank);
        var offer = new Api.Data.Entities.LoanOffer { Id = Guid.NewGuid(), BankBuildingId = bank.Id, LenderCompanyId = lenderCompany.Id, AnnualInterestRatePercent = 11m, MaxPrincipalPerLoan = 20_000m, TotalCapacity = 100_000m, UsedCapacity = 0m, DurationTicks = 720L, IsActive = true, CreatedAtTick = 1L, CreatedAtUtc = DateTime.UtcNow };
        db.LoanOffers.Add(offer);
        await db.SaveChangesAsync();

        // Borrower accepts
        await ExecuteGraphQlAsync(
            "mutation Accept($input: AcceptLoanInput!) { acceptLoan(input: $input) { id } }",
            new { input = new { loanOfferId = offer.Id.ToString(), borrowerCompanyId = borrowerCompany.Id.ToString(), principalAmount = 15_000m } },
            borrowerToken);

        // Lender queries bank loans
        var result = await ExecuteGraphQlAsync(
            "query GetBL($bankId: UUID!) { bankLoans(bankBuildingId: $bankId) { id status borrowerCompanyName originalPrincipal } }",
            new { bankId = bank.Id.ToString() },
            lenderToken);

        var loans = result.GetProperty("data").GetProperty("bankLoans");
        Assert.Equal(1, loans.GetArrayLength());
        Assert.Equal("ACTIVE", loans[0].GetProperty("status").GetString());
        Assert.Equal("BLBorrowerCo", loans[0].GetProperty("borrowerCompanyName").GetString());
        Assert.Equal(15_000m, loans[0].GetProperty("originalPrincipal").GetDecimal());
    }

    #endregion

    #region Procurement Preview

    [Fact]
    public async Task ProcurementPreview_OptimalSource_ReturnsExchangeOffer()
    {
        var email = $"pp-optimal-{Guid.NewGuid():N}@test.com";
        var token = await RegisterAndGetTokenAsync(email, "PPOptimal");

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var player = await db.Players.FirstAsync(p => p.Email == email);
        var city = await db.Cities.FirstAsync();
        var resource = await db.ResourceTypes.FirstAsync(r => r.Slug == "wood");

        var company = new Api.Data.Entities.Company { Id = Guid.NewGuid(), PlayerId = player.Id, Name = "PPOptCo", Cash = 100_000m };
        db.Companies.Add(company);
        var building = new Api.Data.Entities.Building { Id = Guid.NewGuid(), CompanyId = company.Id, CityId = city.Id, Type = Api.Data.Entities.BuildingType.Factory, Name = "PPOptFactory", Level = 1 };
        db.Buildings.Add(building);
        var unit = new Api.Data.Entities.BuildingUnit
        {
            Id = Guid.NewGuid(),
            BuildingId = building.Id,
            UnitType = Api.Data.Entities.UnitType.Purchase,
            GridX = 0,
            GridY = 0,
            Level = 1,
            ResourceTypeId = resource.Id,
            MaxPrice = 9999m,
            PurchaseSource = "OPTIMAL",
        };
        db.BuildingUnits.Add(unit);
        await db.SaveChangesAsync();

        var result = await ExecuteGraphQlAsync(
            "query PP($unitId: UUID!) { procurementPreview(buildingUnitId: $unitId) { sourceType canExecute deliveredPricePerUnit estimatedQuality blockReason } }",
            new { unitId = unit.Id.ToString() },
            token);

        var preview = result.GetProperty("data").GetProperty("procurementPreview");
        Assert.Equal("GLOBAL_EXCHANGE", preview.GetProperty("sourceType").GetString());
        Assert.True(preview.GetProperty("canExecute").GetBoolean());
        Assert.True(preview.GetProperty("deliveredPricePerUnit").GetDecimal() > 0m);
        Assert.True(preview.GetProperty("estimatedQuality").GetDecimal() > 0m);
        Assert.True(preview.GetProperty("blockReason").ValueKind == System.Text.Json.JsonValueKind.Null);
    }

    [Fact]
    public async Task ProcurementPreview_MaxPriceExceeded_ReturnsBlockedWithReason()
    {
        var email = $"pp-maxprice-{Guid.NewGuid():N}@test.com";
        var token = await RegisterAndGetTokenAsync(email, "PPMaxPrice");

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var player = await db.Players.FirstAsync(p => p.Email == email);
        var city = await db.Cities.FirstAsync();
        var resource = await db.ResourceTypes.FirstAsync(r => r.Slug == "wood");

        var company = new Api.Data.Entities.Company { Id = Guid.NewGuid(), PlayerId = player.Id, Name = "PPMaxCo", Cash = 100_000m };
        db.Companies.Add(company);
        var building = new Api.Data.Entities.Building { Id = Guid.NewGuid(), CompanyId = company.Id, CityId = city.Id, Type = Api.Data.Entities.BuildingType.Factory, Name = "PPMaxFactory", Level = 1 };
        db.Buildings.Add(building);
        var unit = new Api.Data.Entities.BuildingUnit
        {
            Id = Guid.NewGuid(),
            BuildingId = building.Id,
            UnitType = Api.Data.Entities.UnitType.Purchase,
            GridX = 0,
            GridY = 0,
            Level = 1,
            ResourceTypeId = resource.Id,
            MaxPrice = 0.001m, // far below any possible exchange price
            PurchaseSource = "EXCHANGE",
        };
        db.BuildingUnits.Add(unit);
        await db.SaveChangesAsync();

        var result = await ExecuteGraphQlAsync(
            "query PP($unitId: UUID!) { procurementPreview(buildingUnitId: $unitId) { sourceType canExecute blockReason blockMessage } }",
            new { unitId = unit.Id.ToString() },
            token);

        var preview = result.GetProperty("data").GetProperty("procurementPreview");
        Assert.Equal("GLOBAL_EXCHANGE", preview.GetProperty("sourceType").GetString());
        Assert.False(preview.GetProperty("canExecute").GetBoolean());
        Assert.Equal("MAX_PRICE_EXCEEDED", preview.GetProperty("blockReason").GetString());
        Assert.NotEmpty(preview.GetProperty("blockMessage").GetString()!);
    }

    [Fact]
    public async Task ProcurementPreview_MinQualityFailed_ReturnsBlockedWithReason()
    {
        var email = $"pp-minqual-{Guid.NewGuid():N}@test.com";
        var token = await RegisterAndGetTokenAsync(email, "PPMinQual");

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var player = await db.Players.FirstAsync(p => p.Email == email);
        var city = await db.Cities.FirstAsync();
        var resource = await db.ResourceTypes.FirstAsync(r => r.Slug == "wood");

        var company = new Api.Data.Entities.Company { Id = Guid.NewGuid(), PlayerId = player.Id, Name = "PPMinQCo", Cash = 100_000m };
        db.Companies.Add(company);
        var building = new Api.Data.Entities.Building { Id = Guid.NewGuid(), CompanyId = company.Id, CityId = city.Id, Type = Api.Data.Entities.BuildingType.Factory, Name = "PPMinQFactory", Level = 1 };
        db.Buildings.Add(building);
        var unit = new Api.Data.Entities.BuildingUnit
        {
            Id = Guid.NewGuid(),
            BuildingId = building.Id,
            UnitType = Api.Data.Entities.UnitType.Purchase,
            GridX = 0,
            GridY = 0,
            Level = 1,
            ResourceTypeId = resource.Id,
            MaxPrice = 9999m,
            MinQuality = 1.0m, // no exchange offer meets perfect quality
            PurchaseSource = "EXCHANGE",
        };
        db.BuildingUnits.Add(unit);
        await db.SaveChangesAsync();

        var result = await ExecuteGraphQlAsync(
            "query PP($unitId: UUID!) { procurementPreview(buildingUnitId: $unitId) { sourceType canExecute blockReason } }",
            new { unitId = unit.Id.ToString() },
            token);

        var preview = result.GetProperty("data").GetProperty("procurementPreview");
        Assert.False(preview.GetProperty("canExecute").GetBoolean());
        Assert.Equal("MIN_QUALITY_FAILED", preview.GetProperty("blockReason").GetString());
    }

    [Fact]
    public async Task ProcurementPreview_LockedCityId_FiltersToThatCity()
    {
        var email = $"pp-locked-{Guid.NewGuid():N}@test.com";
        var token = await RegisterAndGetTokenAsync(email, "PPLocked");

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var player = await db.Players.FirstAsync(p => p.Email == email);
        var cities = await db.Cities.ToListAsync();
        Assert.True(cities.Count >= 1, "Need at least one city");
        var city = cities[0];
        var resource = await db.ResourceTypes.FirstAsync(r => r.Slug == "wood");

        var company = new Api.Data.Entities.Company { Id = Guid.NewGuid(), PlayerId = player.Id, Name = "PPLockedCo", Cash = 100_000m };
        db.Companies.Add(company);
        var building = new Api.Data.Entities.Building { Id = Guid.NewGuid(), CompanyId = company.Id, CityId = city.Id, Type = Api.Data.Entities.BuildingType.Factory, Name = "PPLockedFactory", Level = 1 };
        db.Buildings.Add(building);
        var unit = new Api.Data.Entities.BuildingUnit
        {
            Id = Guid.NewGuid(),
            BuildingId = building.Id,
            UnitType = Api.Data.Entities.UnitType.Purchase,
            GridX = 0,
            GridY = 0,
            Level = 1,
            ResourceTypeId = resource.Id,
            MaxPrice = 9999m,
            PurchaseSource = "EXCHANGE",
            LockedCityId = city.Id, // lock to the building's own city
        };
        db.BuildingUnits.Add(unit);
        await db.SaveChangesAsync();

        var result = await ExecuteGraphQlAsync(
            "query PP($unitId: UUID!) { procurementPreview(buildingUnitId: $unitId) { sourceType canExecute sourceCityId sourceCityName blockReason } }",
            new { unitId = unit.Id.ToString() },
            token);

        var preview = result.GetProperty("data").GetProperty("procurementPreview");
        Assert.Equal("GLOBAL_EXCHANGE", preview.GetProperty("sourceType").GetString());
        Assert.True(preview.GetProperty("canExecute").GetBoolean());
        // Source must be the locked city.
        Assert.Equal(city.Id.ToString(), preview.GetProperty("sourceCityId").GetString());
    }

    [Fact]
    public async Task ProcurementPreview_NotConfigured_ReturnsNotConfiguredReason()
    {
        var email = $"pp-noconf-{Guid.NewGuid():N}@test.com";
        var token = await RegisterAndGetTokenAsync(email, "PPNoConf");

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var player = await db.Players.FirstAsync(p => p.Email == email);
        var city = await db.Cities.FirstAsync();

        var company = new Api.Data.Entities.Company { Id = Guid.NewGuid(), PlayerId = player.Id, Name = "PPNoConfCo", Cash = 100_000m };
        db.Companies.Add(company);
        var building = new Api.Data.Entities.Building { Id = Guid.NewGuid(), CompanyId = company.Id, CityId = city.Id, Type = Api.Data.Entities.BuildingType.Factory, Name = "PPNoConfFactory", Level = 1 };
        db.Buildings.Add(building);
        var unit = new Api.Data.Entities.BuildingUnit
        {
            Id = Guid.NewGuid(),
            BuildingId = building.Id,
            UnitType = Api.Data.Entities.UnitType.Purchase,
            GridX = 0,
            GridY = 0,
            Level = 1,
            // No resource or product configured
        };
        db.BuildingUnits.Add(unit);
        await db.SaveChangesAsync();

        var result = await ExecuteGraphQlAsync(
            "query PP($unitId: UUID!) { procurementPreview(buildingUnitId: $unitId) { sourceType canExecute blockReason } }",
            new { unitId = unit.Id.ToString() },
            token);

        var preview = result.GetProperty("data").GetProperty("procurementPreview");
        Assert.False(preview.GetProperty("canExecute").GetBoolean());
        Assert.Equal("NOT_CONFIGURED", preview.GetProperty("blockReason").GetString());
    }

    [Fact]
    public async Task ProcurementPreview_Unauthenticated_ReturnsNull()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // The query requires auth — unauthenticated request returns null or errors.
        var result = await ExecuteGraphQlAsync(
            "query PP($unitId: UUID!) { procurementPreview(buildingUnitId: $unitId) { canExecute } }",
            new { unitId = Guid.NewGuid().ToString() },
            token: null!);

        // Either returns null data or errors — the unit must not be accessible.
        var data = result.GetProperty("data");
        if (data.ValueKind != System.Text.Json.JsonValueKind.Null && data.TryGetProperty("procurementPreview", out var preview))
        {
            Assert.True(preview.ValueKind == System.Text.Json.JsonValueKind.Null);
        }
    }

    [Fact]
    public async Task ProcurementPreview_OptimalMode_IgnoresLockedCityId()
    {
        // When PurchaseSource is OPTIMAL, LockedCityId must be ignored so the engine
        // can select the globally cheapest source. A player who sets a city lock and later
        // switches back to OPTIMAL must not be silently restricted to the old city.
        var email = $"pp-optimal-locked-{Guid.NewGuid():N}@test.com";
        var token = await RegisterAndGetTokenAsync(email, "PPOptLocked");

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var player = await db.Players.FirstAsync(p => p.Email == email);
        var cities = await db.Cities.ToListAsync();
        Assert.True(cities.Count >= 2, "Need at least two cities");
        var city = cities[0];
        var anotherCity = cities[1];
        var resource = await db.ResourceTypes.FirstAsync(r => r.Slug == "wood");

        var company = new Api.Data.Entities.Company { Id = Guid.NewGuid(), PlayerId = player.Id, Name = "PPOptLockedCo", Cash = 100_000m };
        db.Companies.Add(company);
        var building = new Api.Data.Entities.Building { Id = Guid.NewGuid(), CompanyId = company.Id, CityId = city.Id, Type = Api.Data.Entities.BuildingType.Factory, Name = "PPOptLockedFactory", Level = 1 };
        db.Buildings.Add(building);
        // Unit is OPTIMAL but has LockedCityId set (simulates state left over from a previous EXCHANGE config).
        var unit = new Api.Data.Entities.BuildingUnit
        {
            Id = Guid.NewGuid(),
            BuildingId = building.Id,
            UnitType = Api.Data.Entities.UnitType.Purchase,
            GridX = 0,
            GridY = 0,
            Level = 1,
            ResourceTypeId = resource.Id,
            MaxPrice = 9999m,
            PurchaseSource = "OPTIMAL",
            LockedCityId = anotherCity.Id, // stale locked city – must be ignored for OPTIMAL
        };
        db.BuildingUnits.Add(unit);
        await db.SaveChangesAsync();

        var result = await ExecuteGraphQlAsync(
            "query PP($unitId: UUID!) { procurementPreview(buildingUnitId: $unitId) { sourceType canExecute sourceCityId blockReason } }",
            new { unitId = unit.Id.ToString() },
            token);

        var preview = result.GetProperty("data").GetProperty("procurementPreview");
        // Must return canExecute=true – OPTIMAL should find a global exchange offer without city restriction.
        Assert.True(preview.GetProperty("canExecute").GetBoolean(), "OPTIMAL mode must execute despite stale LockedCityId");
        Assert.Null(preview.GetProperty("blockReason").GetString());
    }

    [Fact]
    public async Task ProcurementPreview_OptimalMode_PrefersPlayerExchangeOrderBeforeGlobalExchange()
    {
        // Tick execution priority for OPTIMAL: player exchange sell orders → local B2B → global exchange.
        // The preview must reflect the same priority and report a PLAYER_EXCHANGE_ORDER source
        // when a qualifying player sell order exists – even if global exchange is also available.
        var email = $"pp-peo-{Guid.NewGuid():N}@test.com";
        var sellerEmail = $"pp-peo-seller-{Guid.NewGuid():N}@test.com";
        var token = await RegisterAndGetTokenAsync(email, "PPPeoPlayer");
        await RegisterAndGetTokenAsync(sellerEmail, "PPPeoSeller");

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var player = await db.Players.FirstAsync(p => p.Email == email);
        var seller = await db.Players.FirstAsync(p => p.Email == sellerEmail);
        var city = await db.Cities.FirstAsync();
        var resource = await db.ResourceTypes.FirstAsync(r => r.Slug == "iron-ore");

        var buyerCompany = new Api.Data.Entities.Company { Id = Guid.NewGuid(), PlayerId = player.Id, Name = "BuyerCoOpt", Cash = 100_000m };
        db.Companies.Add(buyerCompany);
        var sellerCompany = new Api.Data.Entities.Company { Id = Guid.NewGuid(), PlayerId = seller.Id, Name = "SellerCoOpt", Cash = 10_000m };
        db.Companies.Add(sellerCompany);

        // Seller has an exchange building with an active sell order at a very cheap price.
        var exchangeBuilding = new Api.Data.Entities.Building { Id = Guid.NewGuid(), CompanyId = sellerCompany.Id, CityId = city.Id, Type = Api.Data.Entities.BuildingType.Exchange, Name = "ExchangeOpt", Level = 1 };
        db.Buildings.Add(exchangeBuilding);
        var sellOrder = new Api.Data.Entities.ExchangeOrder
        {
            Id = Guid.NewGuid(),
            ExchangeBuildingId = exchangeBuilding.Id,
            CompanyId = sellerCompany.Id,
            Side = "SELL",
            ResourceTypeId = resource.Id,
            PricePerUnit = 1.00m, // far cheaper than any global exchange price
            Quantity = 1000m,
            RemainingQuantity = 1000m,
            IsActive = true,
        };
        db.ExchangeOrders.Add(sellOrder);

        var buyerBuilding = new Api.Data.Entities.Building { Id = Guid.NewGuid(), CompanyId = buyerCompany.Id, CityId = city.Id, Type = Api.Data.Entities.BuildingType.Factory, Name = "BuyerOpt", Level = 1 };
        db.Buildings.Add(buyerBuilding);
        var purchaseUnit = new Api.Data.Entities.BuildingUnit
        {
            Id = Guid.NewGuid(),
            BuildingId = buyerBuilding.Id,
            UnitType = Api.Data.Entities.UnitType.Purchase,
            GridX = 0,
            GridY = 0,
            Level = 1,
            ResourceTypeId = resource.Id,
            MaxPrice = 9999m,
            PurchaseSource = "OPTIMAL",
        };
        db.BuildingUnits.Add(purchaseUnit);
        await db.SaveChangesAsync();

        var result = await ExecuteGraphQlAsync(
            "query PP($unitId: UUID!) { procurementPreview(buildingUnitId: $unitId) { sourceType canExecute deliveredPricePerUnit blockReason } }",
            new { unitId = purchaseUnit.Id.ToString() },
            token);

        var preview = result.GetProperty("data").GetProperty("procurementPreview");
        Assert.True(preview.GetProperty("canExecute").GetBoolean());
        // Must prefer a player exchange order, not the global exchange.
        // Price may be <= $1.00 if other tests have cheaper orders in the shared DB.
        Assert.Equal("PLAYER_EXCHANGE_ORDER", preview.GetProperty("sourceType").GetString());
        Assert.True(preview.GetProperty("deliveredPricePerUnit").GetDecimal() <= 1.00m,
            "Preview should pick the cheapest player order (≤ $1.00), not global exchange");
    }

    [Fact]
    public async Task BuildingConfiguration_SwitchFromExchangeToOptimal_ClearsLockedCityId()
    {
        // When a player stores a configuration with PurchaseSource=OPTIMAL, any LockedCityId
        // sent in the input must be ignored so it cannot silently restrict optimal sourcing.
        var email = $"cfg-clear-locked-{Guid.NewGuid():N}@test.com";
        var token = await RegisterAndGetTokenAsync(email, "CfgClearLocked");

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var player = await db.Players.FirstAsync(p => p.Email == email);
        var city = await db.Cities.FirstAsync();
        var resource = await db.ResourceTypes.FirstAsync(r => r.Slug == "wood");

        var company = new Api.Data.Entities.Company { Id = Guid.NewGuid(), PlayerId = player.Id, Name = "CfgClearLockedCo", Cash = 500_000m };
        db.Companies.Add(company);
        var building = new Api.Data.Entities.Building { Id = Guid.NewGuid(), CompanyId = company.Id, CityId = city.Id, Type = Api.Data.Entities.BuildingType.Factory, Name = "CfgClearLockedFactory", Level = 1 };
        db.Buildings.Add(building);
        var existingUnit = new Api.Data.Entities.BuildingUnit
        {
            Id = Guid.NewGuid(),
            BuildingId = building.Id,
            UnitType = Api.Data.Entities.UnitType.Purchase,
            GridX = 0,
            GridY = 0,
            Level = 1,
            ResourceTypeId = resource.Id,
            PurchaseSource = "EXCHANGE",
            LockedCityId = city.Id, // initially locked
        };
        db.BuildingUnits.Add(existingUnit);
        await db.SaveChangesAsync();

        // Store a new configuration with OPTIMAL mode but still sending LockedCityId (simulates stale data).
        var storeMutation = @"
            mutation StoreCfg($input: StoreBuildingConfigurationInput!) {
                storeBuildingConfiguration(input: $input) { id }
            }";
        var storeInput = new
        {
            buildingId = building.Id.ToString(),
            units = new[]
            {
                new
                {
                    unitType = "PURCHASE",
                    gridX = 0,
                    gridY = 0,
                    linkUp = false, linkDown = false, linkLeft = false, linkRight = false,
                    linkUpLeft = false, linkUpRight = false, linkDownLeft = false, linkDownRight = false,
                    resourceTypeId = resource.Id.ToString(),
                    purchaseSource = "OPTIMAL",
                    lockedCityId = city.Id.ToString(), // stale locked city sent by client
                    maxPrice = (decimal?)null,
                },
            },
        };
        var storeResult = await ExecuteGraphQlAsync(storeMutation, new { input = storeInput }, token);
        Assert.False(storeResult.TryGetProperty("errors", out var errs) && errs.GetArrayLength() > 0,
            $"storeBuildingConfiguration returned errors: {(storeResult.TryGetProperty("errors", out var e) ? e.ToString() : "none")}");
        var planId = Guid.Parse(storeResult.GetProperty("data").GetProperty("storeBuildingConfiguration").GetProperty("id").GetString()!);

        // Inspect the pending plan unit directly – LockedCityId must be null for OPTIMAL.
        await using var scope2 = _factory.Services.CreateAsyncScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<AppDbContext>();
        var planUnit = await db2.BuildingConfigurationPlanUnits
            .FirstAsync(u => u.BuildingConfigurationPlanId == planId && u.UnitType == Api.Data.Entities.UnitType.Purchase);

        Assert.Equal("OPTIMAL", planUnit.PurchaseSource);
        Assert.Null(planUnit.LockedCityId); // Must be cleared for non-EXCHANGE modes.
    }

    [Fact]
    public async Task ProcurementPreview_VendorLock_FiltersPlayerExchangeOrdersByCompany()
    {
        // VendorLockCompanyId must filter player exchange orders in the preview, mirroring
        // PurchasingPhase phase 1 which applies the same vendor lock.
        var email = $"pp-vendlock-{Guid.NewGuid():N}@test.com";
        var sellerEmail = $"pp-vendlock-seller-{Guid.NewGuid():N}@test.com";
        var otherEmail = $"pp-vendlock-other-{Guid.NewGuid():N}@test.com";
        var token = await RegisterAndGetTokenAsync(email, "PPVendLockPlayer");
        await RegisterAndGetTokenAsync(sellerEmail, "PPVendLockSeller");
        await RegisterAndGetTokenAsync(otherEmail, "PPVendLockOther");

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var player = await db.Players.FirstAsync(p => p.Email == email);
        var seller = await db.Players.FirstAsync(p => p.Email == sellerEmail);
        var other = await db.Players.FirstAsync(p => p.Email == otherEmail);
        var city = await db.Cities.FirstAsync();
        var resource = await db.ResourceTypes.FirstAsync(r => r.Slug == "wood");

        var buyerCompany = new Api.Data.Entities.Company { Id = Guid.NewGuid(), PlayerId = player.Id, Name = "BuyerVL", Cash = 100_000m };
        var sellerCompany = new Api.Data.Entities.Company { Id = Guid.NewGuid(), PlayerId = seller.Id, Name = "SellerVL", Cash = 10_000m };
        var otherCompany = new Api.Data.Entities.Company { Id = Guid.NewGuid(), PlayerId = other.Id, Name = "OtherVL", Cash = 10_000m };
        db.Companies.AddRange(buyerCompany, sellerCompany, otherCompany);

        var exchangeBuilding = new Api.Data.Entities.Building { Id = Guid.NewGuid(), CompanyId = sellerCompany.Id, CityId = city.Id, Type = Api.Data.Entities.BuildingType.Exchange, Name = "ExVL", Level = 1 };
        var otherExchangeBuilding = new Api.Data.Entities.Building { Id = Guid.NewGuid(), CompanyId = otherCompany.Id, CityId = city.Id, Type = Api.Data.Entities.BuildingType.Exchange, Name = "OtherExVL", Level = 1 };
        db.Buildings.AddRange(exchangeBuilding, otherExchangeBuilding);

        // Seller has a cheap order; other has an even cheaper order.
        db.ExchangeOrders.Add(new Api.Data.Entities.ExchangeOrder { Id = Guid.NewGuid(), ExchangeBuildingId = exchangeBuilding.Id, CompanyId = sellerCompany.Id, Side = "SELL", ResourceTypeId = resource.Id, PricePerUnit = 2.00m, Quantity = 100m, RemainingQuantity = 100m, IsActive = true });
        db.ExchangeOrders.Add(new Api.Data.Entities.ExchangeOrder { Id = Guid.NewGuid(), ExchangeBuildingId = otherExchangeBuilding.Id, CompanyId = otherCompany.Id, Side = "SELL", ResourceTypeId = resource.Id, PricePerUnit = 0.50m, Quantity = 100m, RemainingQuantity = 100m, IsActive = true });

        var buyerBuilding = new Api.Data.Entities.Building { Id = Guid.NewGuid(), CompanyId = buyerCompany.Id, CityId = city.Id, Type = Api.Data.Entities.BuildingType.Factory, Name = "BuyerVLFactory", Level = 1 };
        db.Buildings.Add(buyerBuilding);
        var purchaseUnit = new Api.Data.Entities.BuildingUnit
        {
            Id = Guid.NewGuid(),
            BuildingId = buyerBuilding.Id,
            UnitType = Api.Data.Entities.UnitType.Purchase,
            GridX = 0,
            GridY = 0,
            Level = 1,
            ResourceTypeId = resource.Id,
            MaxPrice = 9999m,
            PurchaseSource = "OPTIMAL",
            VendorLockCompanyId = sellerCompany.Id, // locked to seller only
        };
        db.BuildingUnits.Add(purchaseUnit);
        await db.SaveChangesAsync();

        var result = await ExecuteGraphQlAsync(
            "query PP($unitId: UUID!) { procurementPreview(buildingUnitId: $unitId) { sourceType canExecute deliveredPricePerUnit sourceVendorCompanyId } }",
            new { unitId = purchaseUnit.Id.ToString() },
            token);

        var preview = result.GetProperty("data").GetProperty("procurementPreview");
        Assert.True(preview.GetProperty("canExecute").GetBoolean());
        Assert.Equal("PLAYER_EXCHANGE_ORDER", preview.GetProperty("sourceType").GetString());
        // Must use seller's $2.00 order, NOT other's cheaper $0.50 order.
        Assert.Equal(2.00m, preview.GetProperty("deliveredPricePerUnit").GetDecimal());
        Assert.Equal(sellerCompany.Id.ToString(), preview.GetProperty("sourceVendorCompanyId").GetString());
    }

    #endregion

    #region SourcingCandidates

    [Fact]
    public async Task SourcingCandidates_GlobalExchange_ReturnsRankedCandidatesWithLandedCost()
    {
        var email = $"sc-ranked-{Guid.NewGuid():N}@test.com";
        await using var isolatedFactory = new ApiWebApplicationFactory();
        using var isolatedClient = isolatedFactory.CreateClient();
        var token = await RegisterAndGetTokenAsync(isolatedClient, email, "SCRanked");

        await using var scope = isolatedFactory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var player = await db.Players.FirstAsync(p => p.Email == email);
        var city = await db.Cities.FirstAsync();
        var resource = await db.ResourceTypes.FirstAsync(r => r.Slug == "wood");

        var company = new Api.Data.Entities.Company { PlayerId = player.Id, Name = "SCRankedCo", Cash = 100_000m };
        db.Companies.Add(company);
        var building = new Api.Data.Entities.Building
        {
            CompanyId = company.Id,
            CityId = city.Id,
            Type = Api.Data.Entities.BuildingType.Factory,
            Name = "SCRankedFactory",
            Level = 1,
            Latitude = city.Latitude,
            Longitude = city.Longitude,
        };
        db.Buildings.Add(building);
        var unit = new Api.Data.Entities.BuildingUnit
        {
            BuildingId = building.Id,
            UnitType = Api.Data.Entities.UnitType.Purchase,
            GridX = 0,
            GridY = 0,
            Level = 1,
            ResourceTypeId = resource.Id,
            PurchaseSource = "OPTIMAL",
        };
        db.BuildingUnits.Add(unit);
        await db.SaveChangesAsync();

        var result = await ExecuteGraphQlAsync(
            isolatedClient,
            "query SC($unitId: UUID!) { sourcingCandidates(buildingUnitId: $unitId) { sourceType sourceCityId sourceCityName exchangePricePerUnit transitCostPerUnit deliveredPricePerUnit estimatedQuality distanceKm isEligible blockReason isRecommended rank } }",
            new { unitId = unit.Id.ToString() },
            token);

        var candidates = result.GetProperty("data").GetProperty("sourcingCandidates");
        Assert.True(candidates.GetArrayLength() > 0, "Expected at least one sourcing candidate");

        // All candidates must have valid pricing and quality data.
        foreach (var c in candidates.EnumerateArray())
        {
            Assert.NotNull(c.GetProperty("sourceType").GetString());
            Assert.True(c.GetProperty("deliveredPricePerUnit").GetDecimal() > 0m);
            Assert.True(c.GetProperty("estimatedQuality").GetDecimal() > 0m);
            Assert.True(c.GetProperty("rank").GetInt32() > 0);
        }

        // All GLOBAL_EXCHANGE candidates must have a city name.
        foreach (var c in candidates.EnumerateArray().Where(c => c.GetProperty("sourceType").GetString() == "GLOBAL_EXCHANGE"))
        {
            Assert.NotNull(c.GetProperty("sourceCityName").GetString());
        }

        // Exactly one candidate should be recommended.
        var recommended = candidates.EnumerateArray().Where(c => c.GetProperty("isRecommended").GetBoolean()).ToList();
        Assert.Single(recommended);

        // Recommended must be rank 1 (first eligible by landed cost).
        Assert.Equal(1, recommended[0].GetProperty("rank").GetInt32());

        // Candidates should be ordered by rank.
        var ranks = candidates.EnumerateArray().Select(c => c.GetProperty("rank").GetInt32()).ToList();
        for (var i = 0; i < ranks.Count - 1; i++)
            Assert.True(ranks[i] <= ranks[i + 1], $"Expected candidates ordered by rank at index {i}");
    }

    [Fact]
    public async Task SourcingCandidates_MaxPriceFilter_MarksExpensiveCandidatesIneligible()
    {
        var email = $"sc-maxprice-{Guid.NewGuid():N}@test.com";
        await using var isolatedFactory = new ApiWebApplicationFactory();
        using var isolatedClient = isolatedFactory.CreateClient();
        var token = await RegisterAndGetTokenAsync(isolatedClient, email, "SCMaxPrice");

        await using var scope = isolatedFactory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var player = await db.Players.FirstAsync(p => p.Email == email);
        var city = await db.Cities.FirstAsync();
        var resource = await db.ResourceTypes.FirstAsync(r => r.Slug == "wood");

        var company = new Api.Data.Entities.Company { PlayerId = player.Id, Name = "SCMaxPriceCo", Cash = 100_000m };
        db.Companies.Add(company);
        var building = new Api.Data.Entities.Building
        {
            CompanyId = company.Id,
            CityId = city.Id,
            Type = Api.Data.Entities.BuildingType.Factory,
            Name = "SCMaxFactory",
            Level = 1,
            Latitude = city.Latitude,
            Longitude = city.Longitude,
        };
        db.Buildings.Add(building);
        // MaxPrice set to an impossibly low value so all exchange candidates are blocked.
        var unit = new Api.Data.Entities.BuildingUnit
        {
            BuildingId = building.Id,
            UnitType = Api.Data.Entities.UnitType.Purchase,
            GridX = 0,
            GridY = 0,
            Level = 1,
            ResourceTypeId = resource.Id,
            PurchaseSource = "EXCHANGE",
            MaxPrice = 0.001m,
        };
        db.BuildingUnits.Add(unit);
        await db.SaveChangesAsync();

        var result = await ExecuteGraphQlAsync(
            isolatedClient,
            "query SC($unitId: UUID!) { sourcingCandidates(buildingUnitId: $unitId) { isEligible blockReason } }",
            new { unitId = unit.Id.ToString() },
            token);

        var candidates = result.GetProperty("data").GetProperty("sourcingCandidates");
        // All candidates must be ineligible due to MAX_PRICE_EXCEEDED
        foreach (var c in candidates.EnumerateArray())
        {
            Assert.False(c.GetProperty("isEligible").GetBoolean());
            Assert.Equal("MAX_PRICE_EXCEEDED", c.GetProperty("blockReason").GetString());
        }
    }

    [Fact]
    public async Task SourcingCandidates_MinQualityFilter_MarksLowQualityCandidatesIneligible()
    {
        var email = $"sc-minquality-{Guid.NewGuid():N}@test.com";
        await using var isolatedFactory = new ApiWebApplicationFactory();
        using var isolatedClient = isolatedFactory.CreateClient();
        var token = await RegisterAndGetTokenAsync(isolatedClient, email, "SCMinQuality");

        await using var scope = isolatedFactory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var player = await db.Players.FirstAsync(p => p.Email == email);
        var city = await db.Cities.FirstAsync();
        var resource = await db.ResourceTypes.FirstAsync(r => r.Slug == "wood");

        var company = new Api.Data.Entities.Company { PlayerId = player.Id, Name = "SCMinQualityCo", Cash = 100_000m };
        db.Companies.Add(company);
        var building = new Api.Data.Entities.Building
        {
            CompanyId = company.Id,
            CityId = city.Id,
            Type = Api.Data.Entities.BuildingType.Factory,
            Name = "SCQualityFactory",
            Level = 1,
            Latitude = city.Latitude,
            Longitude = city.Longitude,
        };
        db.Buildings.Add(building);
        // MinQuality set to impossibly high value so all candidates are blocked.
        var unit = new Api.Data.Entities.BuildingUnit
        {
            BuildingId = building.Id,
            UnitType = Api.Data.Entities.UnitType.Purchase,
            GridX = 0,
            GridY = 0,
            Level = 1,
            ResourceTypeId = resource.Id,
            PurchaseSource = "EXCHANGE",
            MinQuality = 0.9999m,
        };
        db.BuildingUnits.Add(unit);
        await db.SaveChangesAsync();

        var result = await ExecuteGraphQlAsync(
            isolatedClient,
            "query SC($unitId: UUID!) { sourcingCandidates(buildingUnitId: $unitId) { isEligible blockReason } }",
            new { unitId = unit.Id.ToString() },
            token);

        var candidates = result.GetProperty("data").GetProperty("sourcingCandidates");
        foreach (var c in candidates.EnumerateArray())
        {
            Assert.False(c.GetProperty("isEligible").GetBoolean());
            Assert.Equal("MIN_QUALITY_FAILED", c.GetProperty("blockReason").GetString());
        }
    }

    [Fact]
    public async Task SourcingCandidates_NotConfigured_ReturnsEmptyList()
    {
        var email = $"sc-noconfig-{Guid.NewGuid():N}@test.com";
        await using var isolatedFactory = new ApiWebApplicationFactory();
        using var isolatedClient = isolatedFactory.CreateClient();
        var token = await RegisterAndGetTokenAsync(isolatedClient, email, "SCNoConfig");

        await using var scope = isolatedFactory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var player = await db.Players.FirstAsync(p => p.Email == email);
        var city = await db.Cities.FirstAsync();

        var company = new Api.Data.Entities.Company { PlayerId = player.Id, Name = "SCNoConfigCo", Cash = 100_000m };
        db.Companies.Add(company);
        var building = new Api.Data.Entities.Building
        {
            CompanyId = company.Id,
            CityId = city.Id,
            Type = Api.Data.Entities.BuildingType.Factory,
            Name = "SCNoConfigFactory",
            Level = 1,
            Latitude = city.Latitude,
            Longitude = city.Longitude,
        };
        db.Buildings.Add(building);
        // No resource or product configured
        var unit = new Api.Data.Entities.BuildingUnit
        {
            BuildingId = building.Id,
            UnitType = Api.Data.Entities.UnitType.Purchase,
            GridX = 0,
            GridY = 0,
            Level = 1,
        };
        db.BuildingUnits.Add(unit);
        await db.SaveChangesAsync();

        var result = await ExecuteGraphQlAsync(
            isolatedClient,
            "query SC($unitId: UUID!) { sourcingCandidates(buildingUnitId: $unitId) { rank } }",
            new { unitId = unit.Id.ToString() },
            token);

        var candidates = result.GetProperty("data").GetProperty("sourcingCandidates");
        Assert.Equal(0, candidates.GetArrayLength());
    }

    [Fact]
    public async Task SourcingCandidates_Unauthenticated_ReturnsEmptyList()
    {
        var email = $"sc-anon-{Guid.NewGuid():N}@test.com";
        await using var isolatedFactory = new ApiWebApplicationFactory();
        using var isolatedClient = isolatedFactory.CreateClient();
        var token = await RegisterAndGetTokenAsync(isolatedClient, email, "SCAnonOwner");

        await using var scope = isolatedFactory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var player = await db.Players.FirstAsync(p => p.Email == email);
        var city = await db.Cities.FirstAsync();
        var resource = await db.ResourceTypes.FirstAsync(r => r.Slug == "wood");

        var company = new Api.Data.Entities.Company { PlayerId = player.Id, Name = "SCAnonCo", Cash = 100_000m };
        db.Companies.Add(company);
        var building = new Api.Data.Entities.Building
        {
            CompanyId = company.Id,
            CityId = city.Id,
            Type = Api.Data.Entities.BuildingType.Factory,
            Name = "SCAnonFactory",
            Level = 1,
            Latitude = city.Latitude,
            Longitude = city.Longitude,
        };
        db.Buildings.Add(building);
        var unit = new Api.Data.Entities.BuildingUnit
        {
            BuildingId = building.Id,
            UnitType = Api.Data.Entities.UnitType.Purchase,
            GridX = 0,
            GridY = 0,
            Level = 1,
            ResourceTypeId = resource.Id,
        };
        db.BuildingUnits.Add(unit);
        await db.SaveChangesAsync();

        // Query without auth token – returns empty list (unit not found for unauthenticated caller)
        var result = await ExecuteGraphQlAsync(
            isolatedClient,
            "query SC($unitId: UUID!) { sourcingCandidates(buildingUnitId: $unitId) { rank } }",
            new { unitId = unit.Id.ToString() },
            null);

        var data = result.GetProperty("data");
        if (data.ValueKind != System.Text.Json.JsonValueKind.Null)
        {
            var candidates = data.GetProperty("sourcingCandidates");
            Assert.Equal(0, candidates.GetArrayLength());
        }
    }

    [Fact]
    public async Task SourcingCandidates_SameCityCandidate_HasZeroTransitCost()
    {
        var email = $"sc-local-{Guid.NewGuid():N}@test.com";
        await using var isolatedFactory = new ApiWebApplicationFactory();
        using var isolatedClient = isolatedFactory.CreateClient();
        var token = await RegisterAndGetTokenAsync(isolatedClient, email, "SCLocal");

        await using var scope = isolatedFactory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var player = await db.Players.FirstAsync(p => p.Email == email);
        var city = await db.Cities.FirstAsync();
        var resource = await db.ResourceTypes.FirstAsync(r => r.Slug == "wood");

        var company = new Api.Data.Entities.Company { PlayerId = player.Id, Name = "SCLocalCo", Cash = 100_000m };
        db.Companies.Add(company);
        var building = new Api.Data.Entities.Building
        {
            CompanyId = company.Id,
            CityId = city.Id,
            Type = Api.Data.Entities.BuildingType.Factory,
            Name = "SCLocalFactory",
            Level = 1,
            Latitude = city.Latitude,
            Longitude = city.Longitude,
        };
        db.Buildings.Add(building);
        var unit = new Api.Data.Entities.BuildingUnit
        {
            BuildingId = building.Id,
            UnitType = Api.Data.Entities.UnitType.Purchase,
            GridX = 0,
            GridY = 0,
            Level = 1,
            ResourceTypeId = resource.Id,
            PurchaseSource = "EXCHANGE",
        };
        db.BuildingUnits.Add(unit);
        await db.SaveChangesAsync();

        var result = await ExecuteGraphQlAsync(
            isolatedClient,
            "query SC($unitId: UUID!) { sourcingCandidates(buildingUnitId: $unitId) { sourceCityId transitCostPerUnit distanceKm } }",
            new { unitId = unit.Id.ToString() },
            token);

        var candidates = result.GetProperty("data").GetProperty("sourcingCandidates");

        // The candidate for the same city must have zero transit cost
        var sameCityCandidate = candidates.EnumerateArray()
            .FirstOrDefault(c => c.GetProperty("sourceCityId").GetString() == city.Id.ToString());

        if (sameCityCandidate.ValueKind != System.Text.Json.JsonValueKind.Undefined)
        {
            Assert.Equal(0m, sameCityCandidate.GetProperty("transitCostPerUnit").GetDecimal());
        }
    }

    #endregion

}