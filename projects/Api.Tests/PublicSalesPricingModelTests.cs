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
    public void ComputePriceIndex_BelowBasePrice_ReturnsBoostAboveOne()
    {
        // Pricing below base gives a demand boost > 1.0 (ROADMAP: price reductions increase sales).
        var index = PublicSalesPricingModel.ComputePriceIndex(basePrice: 100m, price: 80m, priceElasticity: 0.5m);
        Assert.True(index > 1m,
            $"Below-base pricing should return a demand boost > 1.0. Got {index}");
    }

    [Fact]
    public void ComputePriceIndex_BelowBasePrice_BoostProportionalToDiscountAndElasticity()
    {
        // 20% discount with elasticity 0.5 → boost = 1 + 0.5 × 0.2 = 1.10.
        var index = PublicSalesPricingModel.ComputePriceIndex(basePrice: 100m, price: 80m, priceElasticity: 0.5m);
        Assert.Equal(1.10m, index);
    }

    [Fact]
    public void ComputePriceIndex_BelowBasePrice_HigherElasticity_HigherBoost()
    {
        // More elastic products benefit more from discounting.
        var lessElasticBoost = PublicSalesPricingModel.ComputePriceIndex(basePrice: 100m, price: 80m, priceElasticity: 0.2m);
        var moreElasticBoost = PublicSalesPricingModel.ComputePriceIndex(basePrice: 100m, price: 80m, priceElasticity: 0.8m);
        Assert.True(moreElasticBoost > lessElasticBoost,
            $"More elastic product should receive a higher discount boost. Less={lessElasticBoost}, More={moreElasticBoost}");
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
    public void ComputePriceIndex_ResultIsInExpectedRange()
    {
        // Prices above base are in [0, 1]; prices below base are in (1, MaxDiscountBoostFactor].
        decimal[] aboveOrAtBase = [100m, 150m, 200m, 500m, 1000m];
        foreach (var price in aboveOrAtBase)
        {
            var index = PublicSalesPricingModel.ComputePriceIndex(basePrice: 100m, price: price, priceElasticity: 0.5m);
            Assert.True(index >= 0m && index <= 1m,
                $"Price index for at/above-base price must be in [0, 1] but got {index} for price={price}");
        }
        decimal[] belowBase = [0m, 50m, 80m];
        foreach (var price in belowBase)
        {
            var index = PublicSalesPricingModel.ComputePriceIndex(basePrice: 100m, price: price, priceElasticity: 0.5m);
            Assert.True(index >= 1m && index <= PublicSalesPricingModel.MaxDiscountBoostFactor,
                $"Price index for below-base price must be in [1, {PublicSalesPricingModel.MaxDiscountBoostFactor}] but got {index} for price={price}");
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

    // ── ComputeSalaryPurchasingPowerFactor ──────────────────────────────────

    [Fact]
    public void ComputeSalaryPurchasingPowerFactor_ZeroSalary_ReturnsNeutral()
    {
        // A zero or unset salary must not penalise a city. Factor must be 1.0 (neutral).
        var result = PublicSalesPricingModel.ComputeSalaryPurchasingPowerFactor(0m);
        Assert.Equal(1m, result);
    }

    [Fact]
    public void ComputeSalaryPurchasingPowerFactor_ReferenceSalary_Returns1()
    {
        // A city whose salary equals the reference salary must return exactly 1.0.
        var result = PublicSalesPricingModel.ComputeSalaryPurchasingPowerFactor(GameConstants.ReferenceSalaryPerManhour);
        Assert.Equal(1m, result);
    }

    [Fact]
    public void ComputeSalaryPurchasingPowerFactor_HighSalary_ReturnsAbove1()
    {
        // A city with salary above the reference salary should boost demand (factor > 1.0).
        var result = PublicSalesPricingModel.ComputeSalaryPurchasingPowerFactor(40m);
        Assert.True(result > 1m, $"High-wage city should have factor > 1.0. Got {result}");
    }

    [Fact]
    public void ComputeSalaryPurchasingPowerFactor_LowSalary_ReturnsBetweenHalfAndOne()
    {
        // A city with salary below the reference salary should penalise demand but not too much.
        var result = PublicSalesPricingModel.ComputeSalaryPurchasingPowerFactor(10m);
        Assert.True(result >= 0.5m && result < 1m,
            $"Low-wage city factor should be between 0.5 and 1.0. Got {result}");
    }

    [Fact]
    public void ComputeSalaryPurchasingPowerFactor_IsMonotonicallyIncreasing()
    {
        // Higher salary must always yield a higher or equal factor.
        decimal[] salaries = [5m, 10m, 15m, 20m, 25m, 30m, 40m, 50m, 100m];
        var prev = 0m;
        foreach (var salary in salaries)
        {
            var factor = PublicSalesPricingModel.ComputeSalaryPurchasingPowerFactor(salary);
            Assert.True(factor >= prev,
                $"Factor should be non-decreasing as salary rises. At salary={salary} got {factor} < previous {prev}");
            prev = factor;
        }
    }

    [Fact]
    public void ComputeSalaryPurchasingPowerFactor_IsClampedBetween05And2()
    {
        // Result must always be within [0.5, 2.0] for any positive salary.
        decimal[] salaries = [0.01m, 1m, 5m, 10m, 20m, 30m, 40m, 50m, 100m, 1000m];
        foreach (var salary in salaries)
        {
            var factor = PublicSalesPricingModel.ComputeSalaryPurchasingPowerFactor(salary);
            Assert.True(factor >= 0.5m && factor <= 2.0m,
                $"Factor must be in [0.5, 2.0] but got {factor} for salary={salary}");
        }
    }

    // ── ComputeRecentSalaryPurchasingPowerFactor ───────────────────────────

    [Fact]
    public void ComputeRecentSalaryPurchasingPowerFactor_ZeroSalary_ReturnsNeutral()
    {
        // When there is no salary data yet (early game), must return 1.0 (neutral).
        var result = PublicSalesPricingModel.ComputeRecentSalaryPurchasingPowerFactor(0m, 475_000);
        Assert.Equal(1.0m, result);
    }

    [Fact]
    public void ComputeRecentSalaryPurchasingPowerFactor_ZeroPopulation_ReturnsNeutral()
    {
        // Division-by-zero guard: zero population → neutral.
        var result = PublicSalesPricingModel.ComputeRecentSalaryPurchasingPowerFactor(5_000m, 0);
        Assert.Equal(1.0m, result);
    }

    [Fact]
    public void ComputeRecentSalaryPurchasingPowerFactor_AtReferenceLevel_ReturnsOne()
    {
        // reference = 475_000 * 0.001 * 20 * 10 = 95_000
        var reference = 475_000m * GameConstants.ExpectedSalaryParticipationRate
            * GameConstants.ReferenceSalaryPerManhour
            * GameConstants.RecentSalaryWindowTicks;
        var result = PublicSalesPricingModel.ComputeRecentSalaryPurchasingPowerFactor(reference, 475_000);
        Assert.Equal(1.0m, result);
    }

    [Fact]
    public void ComputeRecentSalaryPurchasingPowerFactor_HighSpending_AboveOne()
    {
        // A city with more salary activity than the reference baseline should produce > 1.0.
        var reference = 475_000m * GameConstants.ExpectedSalaryParticipationRate
            * GameConstants.ReferenceSalaryPerManhour
            * GameConstants.RecentSalaryWindowTicks;
        var result = PublicSalesPricingModel.ComputeRecentSalaryPurchasingPowerFactor(
            reference * 2m, 475_000);
        Assert.True(result > 1.0m, $"Expected > 1.0 but got {result}");
    }

    [Fact]
    public void ComputeRecentSalaryPurchasingPowerFactor_LowSpending_BelowOne()
    {
        // A city with much less activity than the reference baseline should produce < 1.0.
        var reference = 475_000m * GameConstants.ExpectedSalaryParticipationRate
            * GameConstants.ReferenceSalaryPerManhour
            * GameConstants.RecentSalaryWindowTicks;
        var result = PublicSalesPricingModel.ComputeRecentSalaryPurchasingPowerFactor(
            reference * 0.1m, 475_000);
        Assert.True(result < 1.0m, $"Expected < 1.0 but got {result}");
    }

    [Fact]
    public void ComputeRecentSalaryPurchasingPowerFactor_IsClampedBetween05And2()
    {
        decimal[] salaryAmounts = [0m, 1m, 100m, 10_000m, 1_000_000m, 100_000_000m];
        foreach (var amount in salaryAmounts)
        {
            if (amount == 0m) continue; // zero returns neutral 1.0, tested separately
            var factor = PublicSalesPricingModel.ComputeRecentSalaryPurchasingPowerFactor(amount, 475_000);
            Assert.True(factor >= 0.5m && factor <= 2.0m,
                $"Factor must be in [0.5, 2.0] but got {factor} for amount={amount}");
        }
    }

    [Fact]
    public void ComputeRecentSalaryPurchasingPowerFactor_IsMonotonicallyIncreasing()
    {
        decimal prev = 0.5m;
        decimal[] amounts = [100m, 5_000m, 20_000m, 100_000m, 500_000m, 2_000_000m];
        foreach (var amount in amounts)
        {
            var factor = PublicSalesPricingModel.ComputeRecentSalaryPurchasingPowerFactor(amount, 475_000);
            Assert.True(factor >= prev, $"Factor should be non-decreasing: prev={prev}, current={factor} for amount={amount}");
            prev = factor;
        }
    }

    // ── ComputeBlendedSalaryFactor ─────────────────────────────────────────

    [Fact]
    public void ComputeBlendedSalaryFactor_NoRecentData_ReturnsPureStatic()
    {
        // When recentCitySalary = 0, the blended factor equals the static factor.
        var expected = PublicSalesPricingModel.ComputeSalaryPurchasingPowerFactor(28m);
        var result = PublicSalesPricingModel.ComputeBlendedSalaryFactor(28m, 0m, 1_900_000);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ComputeBlendedSalaryFactor_WithRecentData_IsAveragingBothSignals()
    {
        // With recent salary data, blended = 50% static + 50% dynamic.
        var staticFactor = PublicSalesPricingModel.ComputeSalaryPurchasingPowerFactor(18m);
        var reference = 475_000m * GameConstants.ExpectedSalaryParticipationRate
            * GameConstants.ReferenceSalaryPerManhour
            * GameConstants.RecentSalaryWindowTicks;
        // Dynamic factor at exactly the reference → 1.0
        var dynamicFactor = 1.0m;
        var expected = Math.Clamp(0.5m * staticFactor + 0.5m * dynamicFactor, 0.5m, 2.0m);
        var result = PublicSalesPricingModel.ComputeBlendedSalaryFactor(18m, reference, 475_000);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ComputeBlendedSalaryFactor_IsClampedBetween05And2()
    {
        // All combinations should stay in the valid range.
        decimal[] wages = [0m, 5m, 20m, 40m];
        decimal[] recentAmounts = [0m, 100m, 1_000_000m];
        foreach (var wage in wages)
        {
            foreach (var recent in recentAmounts)
            {
                var factor = PublicSalesPricingModel.ComputeBlendedSalaryFactor(wage, recent, 475_000);
                Assert.True(factor >= 0.5m && factor <= 2.0m,
                    $"Blended factor out of range for wage={wage}, recent={recent}: {factor}");
            }
        }
    }

    // ── Market trend constants sanity checks ──────────────────────────────────

    [Fact]
    public void GameConstants_TrendRange_IsValidForMultiplication()
    {
        // TrendMin and TrendMax must be positive so they act as valid demand multipliers.
        Assert.True(GameConstants.TrendMin > 0m, "TrendMin must be positive");
        Assert.True(GameConstants.TrendMax > GameConstants.TrendMin, "TrendMax must exceed TrendMin");
        Assert.True(GameConstants.TrendNeutral >= GameConstants.TrendMin, "TrendNeutral must be >= TrendMin");
        Assert.True(GameConstants.TrendNeutral <= GameConstants.TrendMax, "TrendNeutral must be <= TrendMax");
    }

    [Fact]
    public void GameConstants_TrendRates_AreReasonable()
    {
        // Rise/fall rates must be small enough not to reach the max in a single tick
        // from neutral (otherwise one good tick would instantly peg the trend to TrendMax).
        var riseInOneTick = GameConstants.TrendNeutral + GameConstants.TrendRiseRate;
        Assert.True(riseInOneTick < GameConstants.TrendMax,
            $"A single TrendRiseRate step ({GameConstants.TrendRiseRate}) from neutral must not reach TrendMax ({GameConstants.TrendMax}) immediately.");
        var fallInOneTick = GameConstants.TrendNeutral - GameConstants.TrendFallRate;
        Assert.True(fallInOneTick > GameConstants.TrendMin,
            $"A single TrendFallRate step ({GameConstants.TrendFallRate}) from neutral must not reach TrendMin ({GameConstants.TrendMin}) immediately.");
    }

    [Fact]
    public void GameConstants_TrendRandomAmplitude_IsSmallFraction()
    {
        // Random amplitude must be a small fraction so it enriches gameplay noise without
        // dominating player-controlled variables. 0 < amplitude < 0.3.
        Assert.True(GameConstants.TrendRandomAmplitude > 0m, "RandomAmplitude must be > 0");
        Assert.True(GameConstants.TrendRandomAmplitude < 0.3m,
            $"RandomAmplitude {GameConstants.TrendRandomAmplitude} should be < 0.3 so it doesn't overwhelm player decisions.");
    }

    [Fact]
    public void GameConstants_TrendDecayFraction_ConvergesToNeutral()
    {
        // Given the decay fraction, trend from TrendMax should reach within 5% of neutral
        // within a reasonable number of ticks (e.g., 50 ticks is about 2 in-game days).
        var current = GameConstants.TrendMax;
        const int maxTicks = 50;
        for (var i = 0; i < maxTicks; i++)
        {
            var gap = GameConstants.TrendNeutral - current;
            current = Math.Clamp(
                current + gap * GameConstants.TrendDecayFraction,
                GameConstants.TrendMin,
                GameConstants.TrendMax);
        }
        var tolerance = 0.05m * (GameConstants.TrendMax - GameConstants.TrendMin);
        Assert.True(
            Math.Abs(current - GameConstants.TrendNeutral) <= tolerance,
            $"Trend should converge within 5% of neutral in {maxTicks} ticks, but got {current} (neutral={GameConstants.TrendNeutral}).");
    }
}
