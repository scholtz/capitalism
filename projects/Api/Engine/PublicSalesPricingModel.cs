namespace Api.Engine;

/// <summary>
/// Shared pricing helpers for public-sales demand.
/// Price elasticity is stored on ProductType as a 0-1 sensitivity coefficient,
/// where higher values mean buyers react more strongly to markups.
/// </summary>
public static class PublicSalesPricingModel
{
    public const decimal DefaultPriceElasticity = 0.35m;

    public static decimal NormalizePriceElasticity(decimal priceElasticity)
    {
        var effectiveElasticity = priceElasticity <= 0m ? DefaultPriceElasticity : priceElasticity;
        return Math.Clamp(effectiveElasticity, 0.05m, 1m);
    }

    /// <summary>
    /// Returns the markup ceiling where the price index reaches zero.
    /// Medium-elastic products reach zero around 2x base price, while highly
    /// elastic products hit zero earlier and inelastic products later.
    /// </summary>
    public static decimal ComputeMaxPriceRatio(decimal priceElasticity)
    {
        var elasticity = NormalizePriceElasticity(priceElasticity);
        return 2.5m - elasticity;
    }

    /// <summary>
    /// Computes a 0-1 price index used by the public-sales model.
    /// At or below base price the index is 1.0. At the elasticity-driven max
    /// markup threshold the index reaches 0.0.
    /// </summary>
    public static decimal ComputePriceIndex(decimal basePrice, decimal price, decimal priceElasticity)
    {
        if (basePrice <= 0m || price <= basePrice)
        {
            return 1m;
        }

        var elasticity = NormalizePriceElasticity(priceElasticity);
        var maxPriceRatio = ComputeMaxPriceRatio(elasticity);
        var priceRatio = price / basePrice;
        if (priceRatio >= maxPriceRatio)
        {
            return 0m;
        }

        var normalizedMarkup = Math.Clamp(
            (priceRatio - 1m) / Math.Max(0.0001m, maxPriceRatio - 1m),
            0m,
            1m);
        var slopeExponent = 1m + (elasticity * 2m);
        var priceIndex = (decimal)Math.Pow((double)(1m - normalizedMarkup), (double)slopeExponent);
        return decimal.Round(Math.Clamp(priceIndex, 0m, 1m), 4, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Maps the stored 0-1 elasticity coefficient to a player-facing negative
    /// elasticity score used by the market-intelligence panel.
    /// </summary>
    public static decimal ComputeElasticityIndex(decimal priceElasticity)
    {
        var elasticity = NormalizePriceElasticity(priceElasticity);
        return decimal.Round(-(0.5m + (elasticity * 1.5m)), 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Returns a purchasing-power multiplier based on the city's average base salary.
    /// A salary equal to <see cref="GameConstants.ReferenceSalaryPerManhour"/> produces 1.0.
    /// Higher-wage cities yield up to 2.0; lower-wage cities yield a minimum of 0.5.
    /// A zero or unset salary returns 1.0 (neutral, used by test cities and legacy data).
    /// </summary>
    public static decimal ComputeSalaryPurchasingPowerFactor(decimal baseSalaryPerManhour)
    {
        if (baseSalaryPerManhour <= 0m)
            return 1m;

        return Math.Clamp(
            baseSalaryPerManhour / GameConstants.ReferenceSalaryPerManhour,
            0.5m,
            2.0m);
    }

    /// <summary>
    /// Returns a purchasing-power factor derived from the actual game currency
    /// paid out as salaries in the city over the past
    /// <see cref="GameConstants.RecentSalaryWindowTicks"/> ticks.
    /// This fulfils the ROADMAP requirement: "the game currency collected by
    /// salaries in past 10 ticks".
    /// <para>
    /// When <paramref name="recentSalaryTotal"/> is zero (no salary data yet —
    /// e.g. very early game or isolated test city), returns 1.0 (neutral) so that
    /// new-player cities are not penalised before companies start operating.
    /// </para>
    /// <para>
    /// The reference is: what would a city of this size spend on salaries if
    /// <see cref="GameConstants.ExpectedSalaryParticipationRate"/> of its
    /// population were employed at the reference salary for
    /// <see cref="GameConstants.RecentSalaryWindowTicks"/> ticks.  Values above
    /// the reference produce a factor > 1.0 (economic boom); values below
    /// produce a factor between 0.5 and 1.0 (economic depression).
    /// </para>
    /// </summary>
    /// <param name="recentSalaryTotal">
    /// Absolute sum of <c>LaborCost</c> ledger amounts in this city for the
    /// past <see cref="GameConstants.RecentSalaryWindowTicks"/> ticks.
    /// </param>
    /// <param name="cityPopulation">The city's current population count.</param>
    public static decimal ComputeRecentSalaryPurchasingPowerFactor(
        decimal recentSalaryTotal,
        long cityPopulation)
    {
        if (recentSalaryTotal <= 0m)
            return 1m;  // neutral — no economic data yet

        if (cityPopulation <= 0)
            return 1m;

        // Reference = what we'd expect if a small fraction of the population
        // (ExpectedSalaryParticipationRate) earned the reference wage for the
        // salary window length (RecentSalaryWindowTicks).
        var reference = cityPopulation
            * GameConstants.ExpectedSalaryParticipationRate
            * GameConstants.ReferenceSalaryPerManhour
            * GameConstants.RecentSalaryWindowTicks;

        if (reference <= 0m)
            return 1m;

        return Math.Clamp(
            recentSalaryTotal / reference,
            0.5m,
            2.0m);
    }

    /// <summary>
    /// Blends the static wage-level signal with the dynamic recent-spending
    /// signal to produce a single city purchasing-power factor.
    /// When recent salary data is available (post-game-start) the dynamic
    /// signal carries 50 % weight; the static city wage carries the remainder.
    /// When there is no recent salary data the static signal is used alone.
    /// </summary>
    public static decimal ComputeBlendedSalaryFactor(
        decimal baseSalaryPerManhour,
        decimal recentSalaryTotal,
        long cityPopulation)
    {
        var staticFactor = ComputeSalaryPurchasingPowerFactor(baseSalaryPerManhour);
        if (recentSalaryTotal <= 0m)
            return staticFactor;

        var dynamicFactor = ComputeRecentSalaryPurchasingPowerFactor(recentSalaryTotal, cityPopulation);
        // 50 % static (stable baseline) + 50 % dynamic (actual economic activity)
        return Math.Clamp(0.5m * staticFactor + 0.5m * dynamicFactor, 0.5m, 2.0m);
    }
}