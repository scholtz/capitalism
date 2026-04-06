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
}