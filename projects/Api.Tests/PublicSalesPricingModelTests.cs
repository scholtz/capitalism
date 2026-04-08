using Api.Engine;

namespace Api.Tests;

/// <summary>
/// Pure unit tests for <see cref="PublicSalesPricingModel"/> helper functions.
/// These tests are deterministic and do not require a database or HTTP server.
/// They verify that the elasticity model, price-index calculation, and
/// elasticity-index conversion behave as specified across a range of inputs.
/// </summary>
public sealed class PublicSalesPricingModelTests
{
    // ── NormalizePriceElasticity ────────────────────────────────────────────

    [Fact]
    public void NormalizePriceElasticity_ZeroInput_ReturnsDefaultElasticity()
    {
        var result = PublicSalesPricingModel.NormalizePriceElasticity(0m);
        Assert.Equal(PublicSalesPricingModel.DefaultPriceElasticity, result);
    }

    [Fact]
    public void NormalizePriceElasticity_NegativeInput_ReturnsDefaultElasticity()
    {
        var result = PublicSalesPricingModel.NormalizePriceElasticity(-0.5m);
        Assert.Equal(PublicSalesPricingModel.DefaultPriceElasticity, result);
    }

    [Fact]
    public void NormalizePriceElasticity_BelowMinimum_ClampsTo005()
    {
        var result = PublicSalesPricingModel.NormalizePriceElasticity(0.01m);
        Assert.Equal(0.05m, result);
    }

    [Fact]
    public void NormalizePriceElasticity_AboveMaximum_ClampedTo1()
    {
        var result = PublicSalesPricingModel.NormalizePriceElasticity(1.5m);
        Assert.Equal(1m, result);
    }

    [Fact]
    public void NormalizePriceElasticity_ValidInput_ReturnsInputUnchanged()
    {
        var result = PublicSalesPricingModel.NormalizePriceElasticity(0.5m);
        Assert.Equal(0.5m, result);
    }

    // ── ComputeMaxPriceRatio ────────────────────────────────────────────────

    [Fact]
    public void ComputeMaxPriceRatio_LowElasticity_HigherCeiling()
    {
        // Inelastic products can sustain a higher markup before demand drops to zero.
        var lowElasticityCeiling = PublicSalesPricingModel.ComputeMaxPriceRatio(0.1m);
        var highElasticityCeiling = PublicSalesPricingModel.ComputeMaxPriceRatio(0.9m);

        Assert.True(lowElasticityCeiling > highElasticityCeiling,
            $"Low-elasticity products should have a higher markup ceiling. Low={lowElasticityCeiling}, High={highElasticityCeiling}");
    }

    [Fact]
    public void ComputeMaxPriceRatio_DefaultElasticity_ReturnsReasonableCeiling()
    {
        // At default elasticity (0.35), the ceiling should be above 1.0 (can mark up at all)
        // but below 3.0 (not infinitely elastic).
        var ceiling = PublicSalesPricingModel.ComputeMaxPriceRatio(PublicSalesPricingModel.DefaultPriceElasticity);
        Assert.True(ceiling > 1m, $"Max price ratio must be above 1.0 (no markup impossible). Got {ceiling}");
        Assert.True(ceiling < 3m, $"Max price ratio must be below 3.0 (sanity cap). Got {ceiling}");
    }

    [Fact]
    public void ComputeMaxPriceRatio_HighElasticity_CeilingAbove1()
    {
        // Even highly elastic products should still allow at least some markup.
        var ceiling = PublicSalesPricingModel.ComputeMaxPriceRatio(1m);
        Assert.True(ceiling > 1m, $"Max price ratio must always be above 1.0. Got {ceiling}");
    }

    // ── ComputePriceIndex ───────────────────────────────────────────────────

    [Fact]
    public void ComputePriceIndex_AtBasePrice_Returns1()
    {
        var index = PublicSalesPricingModel.ComputePriceIndex(basePrice: 100m, price: 100m, priceElasticity: 0.5m);
        Assert.Equal(1m, index);
    }

    [Fact]
    public void ComputePriceIndex_BelowBasePrice_Returns1()
    {
        // Pricing below the base should never reduce the price index.
        var index = PublicSalesPricingModel.ComputePriceIndex(basePrice: 100m, price: 80m, priceElasticity: 0.5m);
        Assert.Equal(1m, index);
    }

    [Fact]
    public void ComputePriceIndex_AtOrAboveMaxPriceRatio_ReturnsZero()
    {
        const decimal elasticity = 0.5m;
        var maxRatio = PublicSalesPricingModel.ComputeMaxPriceRatio(elasticity);
        var index = PublicSalesPricingModel.ComputePriceIndex(basePrice: 100m, price: 100m * maxRatio, priceElasticity: elasticity);
        Assert.Equal(0m, index);
    }

    [Fact]
    public void ComputePriceIndex_HighMarkup_LowerThanLowMarkup()
    {
        // A higher markup should always produce a lower price index.
        const decimal basePrice = 100m;
        const decimal elasticity = 0.5m;
        var lowMarkupIndex = PublicSalesPricingModel.ComputePriceIndex(basePrice, basePrice * 1.1m, elasticity);
        var highMarkupIndex = PublicSalesPricingModel.ComputePriceIndex(basePrice, basePrice * 1.8m, elasticity);

        Assert.True(lowMarkupIndex > highMarkupIndex,
            $"Low markup should produce higher price index. LowMarkup={lowMarkupIndex}, HighMarkup={highMarkupIndex}");
    }

    [Fact]
    public void ComputePriceIndex_MoreElasticProduct_DropsFasterWithMarkup()
    {
        // At the same relative markup, a more elastic product should have a lower price index.
        const decimal basePrice = 100m;
        const decimal markupRatio = 1.3m;   // 30% above base — same for both
        var lessElasticIndex = PublicSalesPricingModel.ComputePriceIndex(basePrice, basePrice * markupRatio, priceElasticity: 0.2m);
        var moreElasticIndex = PublicSalesPricingModel.ComputePriceIndex(basePrice, basePrice * markupRatio, priceElasticity: 0.8m);

        Assert.True(lessElasticIndex > moreElasticIndex,
            $"Less-elastic product should retain higher price index at same markup. Less={lessElasticIndex}, More={moreElasticIndex}");
    }

    [Fact]
    public void ComputePriceIndex_ZeroBasePrice_Returns1()
    {
        // When base price is zero (degenerate), the model must not throw and should return 1.
        var index = PublicSalesPricingModel.ComputePriceIndex(basePrice: 0m, price: 50m, priceElasticity: 0.5m);
        Assert.Equal(1m, index);
    }

    [Fact]
    public void ComputePriceIndex_ResultIsClamped_BetweenZeroAndOne()
    {
        // Verify the result is always within [0, 1] for a range of inputs.
        decimal[] prices = [0m, 50m, 100m, 150m, 200m, 500m, 1000m];
        foreach (var price in prices)
        {
            var index = PublicSalesPricingModel.ComputePriceIndex(basePrice: 100m, price: price, priceElasticity: 0.5m);
            Assert.True(index >= 0m && index <= 1m,
                $"Price index must be in [0, 1] but got {index} for price={price}");
        }
    }

    // ── ComputeElasticityIndex ──────────────────────────────────────────────

    [Fact]
    public void ComputeElasticityIndex_ReturnsNegativeValue()
    {
        // Normal goods have negative price elasticity.
        var elasticity = PublicSalesPricingModel.ComputeElasticityIndex(0.5m);
        Assert.True(elasticity < 0m, $"Elasticity index for a normal good must be negative. Got {elasticity}");
    }

    [Fact]
    public void ComputeElasticityIndex_HighElasticity_MoreNegativeThanLowElasticity()
    {
        // More elastic products should have a more negative elasticity index.
        var lowElasticityIndex = PublicSalesPricingModel.ComputeElasticityIndex(0.2m);
        var highElasticityIndex = PublicSalesPricingModel.ComputeElasticityIndex(0.8m);

        Assert.True(highElasticityIndex < lowElasticityIndex,
            $"High-elasticity product must have a more negative index. Low={lowElasticityIndex}, High={highElasticityIndex}");
    }

    [Fact]
    public void ComputeElasticityIndex_DefaultElasticity_InExpectedRange()
    {
        // The elasticity index for the default coefficient should be between -3 and 0.
        // (The model maps the 0-1 elasticity coefficient to a range of -0.5 to -2.0.)
        var index = PublicSalesPricingModel.ComputeElasticityIndex(PublicSalesPricingModel.DefaultPriceElasticity);
        Assert.True(index >= -3m && index < 0m,
            $"Default elasticity index must be between -3 and 0. Got {index}");
    }

    [Fact]
    public void ComputeElasticityIndex_ResultIsDeterministic()
    {
        // Same input must always produce the same output.
        const decimal elasticity = 0.5m;
        var first = PublicSalesPricingModel.ComputeElasticityIndex(elasticity);
        var second = PublicSalesPricingModel.ComputeElasticityIndex(elasticity);
        Assert.Equal(first, second);
    }

    // ── Monotonicity / integration ──────────────────────────────────────────

    [Fact]
    public void ComputePriceIndex_IsStrictlyDecreasing_AsMarkupIncreases()
    {
        // The price index must decrease monotonically as the price increases above base price.
        const decimal basePrice = 100m;
        const decimal elasticity = 0.5m;
        var maxRatio = PublicSalesPricingModel.ComputeMaxPriceRatio(elasticity);
        var steps = 20;
        var prev = 1m;
        for (var i = 1; i <= steps; i++)
        {
            var ratio = 1m + ((maxRatio - 1m) * i / steps);
            var index = PublicSalesPricingModel.ComputePriceIndex(basePrice, basePrice * ratio, elasticity);
            Assert.True(index <= prev,
                $"Price index should be non-increasing. At ratio={ratio:F2} got {index} > previous {prev}");
            prev = index;
        }
    }
}
