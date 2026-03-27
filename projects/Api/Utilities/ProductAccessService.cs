using Api.Data.Entities;
using HotChocolate;

namespace Api.Utilities;

/// <summary>
/// Centralizes Pro-catalog access rules so queries and mutations stay consistent.
/// </summary>
public static class ProductAccessService
{
    public static bool HasActiveProSubscription(Player? player, DateTime nowUtc)
    {
        return HasActiveProSubscription(player?.ProSubscriptionEndsAtUtc, nowUtc);
    }

    public static bool HasActiveProSubscription(DateTime? proSubscriptionEndsAtUtc, DateTime nowUtc)
    {
        return proSubscriptionEndsAtUtc is { } endsAtUtc && endsAtUtc > nowUtc;
    }

    public static bool IsUnlockedForPlayer(ProductType product, bool hasActiveProSubscription)
    {
        return !product.IsProOnly || hasActiveProSubscription;
    }

    public static void ApplyAccessMetadata(IEnumerable<ProductType> products, bool hasActiveProSubscription)
    {
        foreach (var product in products)
        {
            product.IsUnlockedForCurrentPlayer = IsUnlockedForPlayer(product, hasActiveProSubscription);
        }
    }

    public static GraphQLException CreateProAccessException(string productName)
    {
        return new GraphQLException(
            ErrorBuilder.New()
                .SetMessage($"Pro subscription unlocks additional products to manufacture and sell. Activate Pro to use {productName}.")
                .SetCode("PRO_SUBSCRIPTION_REQUIRED")
                .Build());
    }
}
