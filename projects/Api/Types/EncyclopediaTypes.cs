using Api.Data.Entities;

namespace Api.Types;

/// <summary>
/// Aggregated payload returned by the <c>encyclopediaResource</c> query.
/// Bundles the requested resource with all products that directly consume it so the
/// encyclopedia detail view can be rendered in a single round-trip.
/// </summary>
public sealed class EncyclopediaResourceDetail
{
    /// <summary>The requested resource type (always non-null when the query succeeds).</summary>
    public ResourceType Resource { get; set; } = null!;

    /// <summary>
    /// All product types that include this resource as a direct ingredient
    /// (i.e. at least one <see cref="ProductRecipe"/> whose <c>ResourceTypeId</c> matches).
    /// Ordered by product name. Access metadata (<c>isUnlockedForCurrentPlayer</c>) is already
    /// applied based on the caller's Pro subscription status.
    /// </summary>
    public List<ProductType> ProductsUsingResource { get; set; } = [];
}
