namespace Api.Data.Entities;

/// <summary>
/// Persists the market trend factor for a specific (city, product/resource) pair across ticks.
/// The trend factor is a demand multiplier in the range [0.5, 1.5]:
///   - 1.0 = neutral market (default)
///   - &gt; 1.0 = rising market (hot trend boosts demand)
///   - &lt; 1.0 = falling market (cold trend suppresses demand)
/// Each tick the factor evolves based on observed sales success:
///   - Strong utilisation → factor rises
///   - Weak utilisation with ample stock → factor falls
///   - Moderate → decay toward 1.0 (neutral)
/// </summary>
public sealed class MarketTrendState
{
    public Guid Id { get; set; }

    /// <summary>City in which the trend applies.</summary>
    public Guid CityId { get; set; }

    /// <summary>
    /// The item tracked by this trend: ProductTypeId when selling manufactured goods,
    /// ResourceTypeId when selling raw materials. Together with <see cref="CityId"/>
    /// forms the unique key for a market trend.
    /// </summary>
    public Guid ItemId { get; set; }

    /// <summary>
    /// Demand multiplier in the range [0.5, 1.5].  1.0 is the neutral baseline.
    /// Applied to <c>cityBaseDemand</c> during public-sales tick resolution.
    /// </summary>
    public decimal TrendFactor { get; set; } = 1.0m;

    /// <summary>Game tick when this state was last updated by the tick engine.</summary>
    public long LastUpdatedTick { get; set; }
}
