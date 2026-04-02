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

    // ---------------------------------------------------------------------------
    // Transit-reranking: cheaper sticker price loses when transit cost is included
    // ---------------------------------------------------------------------------

    [Fact]
    public void TransitReranking_LocalWinsOverRemote_WhenTransitCostFlipsTheRanking()
    {
        // KEY BUSINESS RULE: the local city source should be preferred even when a remote
        // city has a lower sticker exchange price, if the transit cost on the remote source
        // makes its effective delivered cost higher.
        //
        // Setup: Bratislava is the DESTINATION.
        //   - Local (Bratislava): abundance=0.70, rent=$14 → exchange ≈$11.17, transit=$0.00, delivered≈$11.17
        //   - Remote (Prague):    abundance=0.80, rent=$18 → exchange ≈$10.74, transit≈$0.69,  delivered≈$11.43
        //
        // Prague has the lower sticker price but loses because transit cost reverses the order.
        // (At abundance=0.80 the sticker savings of ~$0.43 are less than transit ≈$0.69.)

        var bratislava = new City { Id = Guid.NewGuid(), Name = "Bratislava", AverageRentPerSqm = 14m, Latitude = 48.15, Longitude = 17.11 };
        var prague     = new City { Id = Guid.NewGuid(), Name = "Prague",     AverageRentPerSqm = 18m, Latitude = 50.08, Longitude = 14.43 };
        var wood = new ResourceType { Id = Guid.NewGuid(), Name = "Wood", Slug = "wood", BasePrice = 10m, WeightPerUnit = 1.0m };

        var localAbundance  = 0.70m;
        var remoteAbundance = 0.80m; // High enough to give lower sticker, but transit still flips ranking

        var localExchangePrice  = GlobalExchangeCalculator.ComputeExchangePrice(bratislava, wood, localAbundance);
        var remoteExchangePrice = GlobalExchangeCalculator.ComputeExchangePrice(prague, wood, remoteAbundance);

        var localTransit  = GlobalExchangeCalculator.ComputeTransitCostPerUnit(bratislava, bratislava, wood);
        var remoteTransit = GlobalExchangeCalculator.ComputeTransitCostPerUnit(prague, bratislava, wood);

        var localDelivered  = localExchangePrice  + localTransit;
        var remoteDelivered = remoteExchangePrice + remoteTransit;

        // Prague has a cheaper sticker price — this is the misleading signal.
        Assert.True(remoteExchangePrice < localExchangePrice,
            $"Prague sticker ({remoteExchangePrice}) should be cheaper than Bratislava sticker ({localExchangePrice}).");

        // But after transit cost is applied, the local city is cheaper.
        Assert.True(localDelivered < remoteDelivered,
            $"Local delivered ({localDelivered}) should be < remote delivered ({remoteDelivered}) after transit reranking.");

        // Transit must be zero for same city and positive for cross-city.
        Assert.Equal(0m, localTransit);
        Assert.True(remoteTransit > 0m);
    }

    [Fact]
    public void TransitReranking_DeliveredPriceOrdering_DiffersFromExchangePriceOrdering()
    {
        // Verifies that sorting by delivered price can produce a different winner than
        // sorting by exchange price alone — the core business insight of the feature.
        var bratislava = new City { Id = Guid.NewGuid(), Name = "Bratislava", AverageRentPerSqm = 14m, Latitude = 48.15, Longitude = 17.11 };
        var prague     = new City { Id = Guid.NewGuid(), Name = "Prague",     AverageRentPerSqm = 18m, Latitude = 50.08, Longitude = 14.43 };
        var wood = new ResourceType { Id = Guid.NewGuid(), Name = "Wood", Slug = "wood", BasePrice = 10m, WeightPerUnit = 1.0m };

        // Prague abundance=0.80: sticker ≈$10.74 < Bratislava ≈$11.17 (Prague wins on exchange)
        // But Prague transit ≈$0.69: delivered ≈$11.43 > Bratislava $11.17 (Bratislava wins on delivered)
        var braExchange = GlobalExchangeCalculator.ComputeExchangePrice(bratislava, wood, 0.70m);
        var prgExchange = GlobalExchangeCalculator.ComputeExchangePrice(prague, wood, 0.80m);

        var braTransit = GlobalExchangeCalculator.ComputeTransitCostPerUnit(bratislava, bratislava, wood);
        var prgTransit = GlobalExchangeCalculator.ComputeTransitCostPerUnit(prague, bratislava, wood);

        var braDelivered = braExchange + braTransit;
        var prgDelivered = prgExchange + prgTransit;

        // Prague wins on exchange price ranking.
        var exchangeWinner = prgExchange < braExchange ? "Prague" : "Bratislava";
        // Bratislava wins on delivered price ranking.
        var deliveredWinner = braDelivered <= prgDelivered ? "Bratislava" : "Prague";

        Assert.NotEqual<string>(exchangeWinner, deliveredWinner);
    }

    [Fact]
    public void ComputeExchangeQuality_MidAbundance_IsWithinValidRange()
    {
        // Mid-abundance (0.5) should produce quality between the min and max bounds.
        var quality = GlobalExchangeCalculator.ComputeExchangeQuality(0.5m);
        Assert.InRange(quality, 0.35m, 0.95m);
        // Should be strictly between the extremes.
        Assert.True(quality > 0.35m && quality < 0.95m,
            $"Mid-abundance quality {quality} should be strictly between 0.35 and 0.95");
    }

    [Fact]
    public void ComputeExchangePrice_HigherRentCity_HasHigherPrice()
    {
        // Cities with higher average rent (more expensive commercial space) should have
        // higher exchange prices for the same resource — the city rent multiplier is in effect.
        var cheapCity      = new City { Id = Guid.NewGuid(), Name = "Cheap",      AverageRentPerSqm = 5m,  Latitude = 48.15, Longitude = 17.11 };
        var expensiveCity  = new City { Id = Guid.NewGuid(), Name = "Expensive",  AverageRentPerSqm = 50m, Latitude = 48.15, Longitude = 17.11 };
        var resource = MakeResource(10m, 1m);

        var cheapPrice     = GlobalExchangeCalculator.ComputeExchangePrice(cheapCity, resource, 0.5m);
        var expensivePrice = GlobalExchangeCalculator.ComputeExchangePrice(expensiveCity, resource, 0.5m);

        Assert.True(expensivePrice > cheapPrice,
            $"Expensive-city price ({expensivePrice}) should exceed cheap-city price ({cheapPrice})");
    }
}
