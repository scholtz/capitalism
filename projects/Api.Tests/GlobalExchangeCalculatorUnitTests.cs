using Api.Data.Entities;
using Api.Utilities;

namespace Api.Tests;

/// <summary>
/// Pure-unit tests for <see cref="GlobalExchangeCalculator"/>.
/// These tests exercise the math in isolation — no database, no HTTP.
/// Known city coordinates (Bratislava, Prague, Vienna) are used so expected
/// values can be validated against real geography.
/// </summary>
public sealed class GlobalExchangeCalculatorUnitTests
{
    // ---------------------------------------------------------------------------
    // ComputeDistanceKm
    // ---------------------------------------------------------------------------

    [Fact]
    public void ComputeDistanceKm_SameCoordinates_ReturnsZero()
    {
        var dist = GlobalExchangeCalculator.ComputeDistanceKm(48.15, 17.11, 48.15, 17.11);
        Assert.Equal(0d, dist, 6);
    }

    [Fact]
    public void ComputeDistanceKm_BratislavaToPrague_ReturnsApproxCorrectKm()
    {
        // Bratislava (48.15°N, 17.11°E) → Prague (50.08°N, 14.43°E)
        // Great-circle ≈ 277 km
        var dist = GlobalExchangeCalculator.ComputeDistanceKm(48.15, 17.11, 50.08, 14.43);
        Assert.InRange(dist, 255d, 310d);
    }

    [Fact]
    public void ComputeDistanceKm_BratislavaToVienna_ReturnsApproxCorrectKm()
    {
        // Bratislava (48.15°N, 17.11°E) → Vienna (48.21°N, 16.37°E)
        // Great-circle ≈ 55–60 km
        var dist = GlobalExchangeCalculator.ComputeDistanceKm(48.15, 17.11, 48.21, 16.37);
        Assert.InRange(dist, 40d, 80d);
    }

    [Fact]
    public void ComputeDistanceKm_IsSymmetric()
    {
        var ab = GlobalExchangeCalculator.ComputeDistanceKm(48.15, 17.11, 50.08, 14.43);
        var ba = GlobalExchangeCalculator.ComputeDistanceKm(50.08, 14.43, 48.15, 17.11);
        Assert.Equal(ab, ba, 6);
    }

    // ---------------------------------------------------------------------------
    // ComputeTransitCostPerUnit
    // ---------------------------------------------------------------------------

    private static City MakeCity(Guid id, double lat, double lon) =>
        new City { Id = id, Name = "Test", Latitude = lat, Longitude = lon, AverageRentPerSqm = 15m };

    private static ResourceType MakeResource(decimal basePrice, decimal weightPerUnit) =>
        new ResourceType { Id = Guid.NewGuid(), Name = "TestRes", Slug = "test-res", BasePrice = basePrice, WeightPerUnit = weightPerUnit };

    [Fact]
    public void ComputeTransitCostPerUnit_SameCity_ReturnsZero()
    {
        var city = MakeCity(Guid.NewGuid(), 48.15, 17.11);
        var resource = MakeResource(10m, 1.0m);

        var cost = GlobalExchangeCalculator.ComputeTransitCostPerUnit(city, city, resource);

        Assert.Equal(0m, cost);
    }

    [Fact]
    public void ComputeTransitCostPerUnit_CrossCity_ReturnsPositiveCost()
    {
        var bratislava = MakeCity(Guid.NewGuid(), 48.15, 17.11);
        var prague = MakeCity(Guid.NewGuid(), 50.08, 14.43);
        var resource = MakeResource(10m, 1.0m);

        var cost = GlobalExchangeCalculator.ComputeTransitCostPerUnit(prague, bratislava, resource);

        Assert.True(cost > 0m);
    }

    [Fact]
    public void ComputeTransitCostPerUnit_EnforcesMinimumCostOf005()
    {
        // Very light resource over very short distance should still incur minimum cost.
        var cityA = MakeCity(Guid.NewGuid(), 48.15, 17.11);
        var cityB = MakeCity(Guid.NewGuid(), 48.16, 17.12); // ~1 km apart
        var veryLightResource = MakeResource(10m, 0.001m);

        var cost = GlobalExchangeCalculator.ComputeTransitCostPerUnit(cityA, cityB, veryLightResource);

        Assert.True(cost >= 0.05m, $"Expected minimum 0.05 transit cost but got {cost}");
    }

    [Fact]
    public void ComputeTransitCostPerUnit_ScalesWithResourceWeight()
    {
        var bratislava = MakeCity(Guid.NewGuid(), 48.15, 17.11);
        var prague = MakeCity(Guid.NewGuid(), 50.08, 14.43);

        var lightResource = MakeResource(10m, 0.5m);
        var heavyResource = MakeResource(10m, 5.0m);

        var lightCost = GlobalExchangeCalculator.ComputeTransitCostPerUnit(prague, bratislava, lightResource);
        var heavyCost = GlobalExchangeCalculator.ComputeTransitCostPerUnit(prague, bratislava, heavyResource);

        Assert.True(heavyCost > lightCost, $"Heavy resource ({heavyCost}) should cost more than light ({lightCost})");
    }

    [Fact]
    public void ComputeTransitCostPerUnit_ScalesWithDistance()
    {
        var bratislava = MakeCity(Guid.NewGuid(), 48.15, 17.11);
        var prague = MakeCity(Guid.NewGuid(), 50.08, 14.43); // ~277 km
        var vienna = MakeCity(Guid.NewGuid(), 48.21, 16.37); // ~55 km
        var resource = MakeResource(10m, 1.0m);

        var longRouteCost = GlobalExchangeCalculator.ComputeTransitCostPerUnit(prague, bratislava, resource);
        var shortRouteCost = GlobalExchangeCalculator.ComputeTransitCostPerUnit(vienna, bratislava, resource);

        Assert.True(longRouteCost > shortRouteCost,
            $"Prague→Bratislava ({longRouteCost}) should cost more than Vienna→Bratislava ({shortRouteCost})");
    }

    [Fact]
    public void ComputeTransitCostPerUnit_RoundsToTwoDecimalPlaces()
    {
        var bratislava = MakeCity(Guid.NewGuid(), 48.15, 17.11);
        var prague = MakeCity(Guid.NewGuid(), 50.08, 14.43);
        var resource = MakeResource(10m, 1.5m);

        var cost = GlobalExchangeCalculator.ComputeTransitCostPerUnit(prague, bratislava, resource);

        // Decimal.GetBits gives the scale factor. Alternatively: round-trip check.
        var roundTripped = decimal.Round(cost, 2, MidpointRounding.AwayFromZero);
        Assert.Equal(cost, roundTripped);
    }

    [Fact]
    public void ComputeTransitCostPerUnit_UsesMinimumEffectiveWeightOf01()
    {
        var bratislava = MakeCity(Guid.NewGuid(), 48.15, 17.11);
        var prague = MakeCity(Guid.NewGuid(), 50.08, 14.43);

        var negligibleWeightResource = MakeResource(10m, 0.001m);
        var minWeightResource = MakeResource(10m, 0.1m);

        var negligibleCost = GlobalExchangeCalculator.ComputeTransitCostPerUnit(prague, bratislava, negligibleWeightResource);
        var minCost = GlobalExchangeCalculator.ComputeTransitCostPerUnit(prague, bratislava, minWeightResource);

        Assert.Equal(negligibleCost, minCost);
    }

    // ---------------------------------------------------------------------------
    // ComputeExchangePrice
    // ---------------------------------------------------------------------------

    [Fact]
    public void ComputeExchangePrice_HighAbundance_LowerPrice()
    {
        var city = new City { Id = Guid.NewGuid(), Name = "TestCity", AverageRentPerSqm = 15m, Latitude = 48.15, Longitude = 17.11 };
        var resource = MakeResource(10m, 1m);

        var highAbundancePrice = GlobalExchangeCalculator.ComputeExchangePrice(city, resource, 0.9m);
        var lowAbundancePrice = GlobalExchangeCalculator.ComputeExchangePrice(city, resource, 0.1m);

        Assert.True(lowAbundancePrice > highAbundancePrice,
            $"Low abundance price ({lowAbundancePrice}) should exceed high abundance price ({highAbundancePrice})");
    }

    [Fact]
    public void ComputeExchangePrice_ClampsAbundanceBelowZero()
    {
        var city = new City { Id = Guid.NewGuid(), Name = "TestCity", AverageRentPerSqm = 15m, Latitude = 48.15, Longitude = 17.11 };
        var resource = MakeResource(10m, 1m);

        var negative = GlobalExchangeCalculator.ComputeExchangePrice(city, resource, -0.5m);
        var zero = GlobalExchangeCalculator.ComputeExchangePrice(city, resource, 0m);

        Assert.Equal(zero, negative);
    }

    [Fact]
    public void ComputeExchangePrice_ClampsAbundanceAboveOne()
    {
        var city = new City { Id = Guid.NewGuid(), Name = "TestCity", AverageRentPerSqm = 15m, Latitude = 48.15, Longitude = 17.11 };
        var resource = MakeResource(10m, 1m);

        var tooHigh = GlobalExchangeCalculator.ComputeExchangePrice(city, resource, 1.5m);
        var maxAbundance = GlobalExchangeCalculator.ComputeExchangePrice(city, resource, 1m);

        Assert.Equal(maxAbundance, tooHigh);
    }

    // ---------------------------------------------------------------------------
    // ComputeExchangeQuality
    // ---------------------------------------------------------------------------

    [Fact]
    public void ComputeExchangeQuality_ZeroAbundance_ReturnsMinQuality()
    {
        Assert.Equal(0.35m, GlobalExchangeCalculator.ComputeExchangeQuality(0m));
    }

    [Fact]
    public void ComputeExchangeQuality_FullAbundance_ReturnsMaxQuality()
    {
        Assert.Equal(0.95m, GlobalExchangeCalculator.ComputeExchangeQuality(1m));
    }

    [Fact]
    public void ComputeExchangeQuality_IncreasesWithAbundance()
    {
        var low = GlobalExchangeCalculator.ComputeExchangeQuality(0.2m);
        var mid = GlobalExchangeCalculator.ComputeExchangeQuality(0.5m);
        var high = GlobalExchangeCalculator.ComputeExchangeQuality(0.8m);

        Assert.True(low < mid && mid < high, $"Quality must increase: {low} < {mid} < {high}");
    }

    // ---------------------------------------------------------------------------
    // Landed-cost composition
    // ---------------------------------------------------------------------------

    [Fact]
    public void LandedCost_EqualsSumOfExchangePriceAndTransitCost()
    {
        var bratislava = new City { Id = Guid.NewGuid(), Name = "Bratislava", AverageRentPerSqm = 14m, Latitude = 48.15, Longitude = 17.11 };
        var prague = new City { Id = Guid.NewGuid(), Name = "Prague", AverageRentPerSqm = 18m, Latitude = 50.08, Longitude = 14.43 };
        var resource = new ResourceType { Id = Guid.NewGuid(), Name = "Wood", Slug = "wood", BasePrice = 10m, WeightPerUnit = 1.0m };

        var abundance = 0.6m;
        var exchangePrice = GlobalExchangeCalculator.ComputeExchangePrice(prague, resource, abundance);
        var transitCost = GlobalExchangeCalculator.ComputeTransitCostPerUnit(prague, bratislava, resource);
        var deliveredPrice = exchangePrice + transitCost;

        Assert.True(exchangePrice > 0m);
        Assert.True(transitCost > 0m);
        Assert.Equal(deliveredPrice, exchangePrice + transitCost);
        Assert.True(deliveredPrice > exchangePrice, "Delivered price must exceed exchange price for cross-city");
    }

    [Fact]
    public void LandedCost_SameCity_EqualExchangePrice()
    {
        var bratislava = new City { Id = Guid.NewGuid(), Name = "Bratislava", AverageRentPerSqm = 14m, Latitude = 48.15, Longitude = 17.11 };
        var resource = new ResourceType { Id = Guid.NewGuid(), Name = "Wood", Slug = "wood", BasePrice = 10m, WeightPerUnit = 1.0m };

        var abundance = 0.7m;
        var exchangePrice = GlobalExchangeCalculator.ComputeExchangePrice(bratislava, resource, abundance);
        var transitCost = GlobalExchangeCalculator.ComputeTransitCostPerUnit(bratislava, bratislava, resource);

        Assert.Equal(0m, transitCost);
        Assert.Equal(exchangePrice, exchangePrice + transitCost);
    }
}
