using Api.Data.Entities;

namespace Api.Utilities;

/// <summary>
/// Centralized pricing, quality, and transit estimation for city-level
/// global exchange offers.
/// </summary>
public static class GlobalExchangeCalculator
{
    public const decimal DefaultMissingAbundance = 0.05m;
    public const decimal TransitCostRatePerKmPerWeightUnit = 0.0025m;
    public const decimal MinimumTransitCostPerUnit = 0.01m;
    public const decimal MinimumCityTransitCostPerUnit = 0.05m;
    public const decimal MinimumWeightPerUnit = 0.1m;

    public static decimal ComputeExchangePrice(City city, ResourceType resourceType, decimal abundance)
    {
        var normalizedAbundance = Math.Clamp(abundance, 0m, 1m);
        var scarcityMultiplier = 1.55m - (normalizedAbundance * 0.75m);
        var cityMultiplier = 0.95m + (city.AverageRentPerSqm / 100m);
        return decimal.Round(resourceType.BasePrice * scarcityMultiplier * cityMultiplier, 2, MidpointRounding.AwayFromZero);
    }

    public static decimal ComputeExchangeQuality(decimal abundance)
    {
        var normalizedAbundance = Math.Clamp(abundance, 0m, 1m);
        return decimal.Round(Math.Clamp(0.35m + (normalizedAbundance * 0.60m), 0.35m, 0.95m), 4, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Computes the quality band for global exchange sourcing at a given abundance.
    /// The band width scales from 20% at zero abundance (scarce, uncertain) down to
    /// 5% at full abundance (plentiful, consistent). The centre of the band is the
    /// same value returned by <see cref="ComputeExchangeQuality"/>.
    /// </summary>
    /// <returns>
    /// A tuple of (min, max) quality clamped to [0.05, 0.99].
    /// </returns>
    public static (decimal min, decimal max) ComputeExchangeQualityBand(decimal abundance)
    {
        var normalizedAbundance = Math.Clamp(abundance, 0m, 1m);
        var centralQuality = 0.35m + (normalizedAbundance * 0.60m);
        var bandWidth = 0.05m + ((1m - normalizedAbundance) * 0.15m);
        var halfBand = bandWidth / 2m;
        var min = decimal.Round(Math.Clamp(centralQuality - halfBand, 0.05m, 0.99m), 4, MidpointRounding.AwayFromZero);
        var max = decimal.Round(Math.Clamp(centralQuality + halfBand, 0.05m, 0.99m), 4, MidpointRounding.AwayFromZero);
        return (min, max);
    }

    /// <summary>
    /// Samples a deterministic quality value within the quality band for a given
    /// (tick, sourceCityId, resourceTypeId) combination.
    /// The same inputs always produce the same output, ensuring tick-reproducibility.
    /// </summary>
    public static decimal SampleExchangeQuality(decimal abundance, long tick, Guid sourceCityId, Guid resourceTypeId)
    {
        var (min, max) = ComputeExchangeQualityBand(abundance);
        // Deterministic hash mixing to derive a fraction in [0, 1)
        unchecked
        {
            var h = (long)tick * 1_000_003L
                    ^ (long)(uint)sourceCityId.GetHashCode() * 999_983L
                    ^ (long)(uint)resourceTypeId.GetHashCode() * 998_993L;
            var fraction = (Math.Abs(h) % 10_000L) / 10_000m;
            var sampled = min + (fraction * (max - min));
            return decimal.Round(Math.Clamp(sampled, min, max), 4, MidpointRounding.AwayFromZero);
        }
    }

    public static decimal ComputeTransitCostPerUnit(City sourceCity, City destinationCity, ResourceType resourceType)
    {
        if (sourceCity.Id == destinationCity.Id)
        {
            return 0m;
        }

        return Math.Max(
            ComputeTransitCostPerUnit(
            sourceCity.Latitude,
            sourceCity.Longitude,
            destinationCity.Latitude,
            destinationCity.Longitude,
            resourceType.WeightPerUnit),
            MinimumCityTransitCostPerUnit);
    }

    public static decimal ComputeTransitCostPerUnit(
        double latitudeA,
        double longitudeA,
        double latitudeB,
        double longitudeB,
        decimal weightPerUnit)
    {
        var distanceKm = ComputeDistanceKm(latitudeA, longitudeA, latitudeB, longitudeB);
        var rawTransitCost = (decimal)distanceKm * Math.Max(weightPerUnit, MinimumWeightPerUnit) * TransitCostRatePerKmPerWeightUnit;
        return decimal.Round(Math.Max(rawTransitCost, MinimumTransitCostPerUnit), 2, MidpointRounding.AwayFromZero);
    }

    public static decimal ComputeItemWeightPerUnit(
        Guid? resourceTypeId,
        Guid? productTypeId,
        IReadOnlyDictionary<Guid, ResourceType> resourceTypesById,
        IReadOnlyDictionary<Guid, ProductType> productTypesById,
        IReadOnlyDictionary<Guid, List<ProductRecipe>> recipesByProduct)
    {
        if (resourceTypeId.HasValue && resourceTypesById.TryGetValue(resourceTypeId.Value, out var resourceType))
        {
            return Math.Max(resourceType.WeightPerUnit, MinimumWeightPerUnit);
        }

        if (!productTypeId.HasValue || !productTypesById.TryGetValue(productTypeId.Value, out var productType))
        {
            return MinimumWeightPerUnit;
        }

        return Math.Max(
            ComputeProductWeightPerUnit(productType, resourceTypesById, productTypesById, recipesByProduct, [], []),
            MinimumWeightPerUnit);
    }

    private static decimal ComputeProductWeightPerUnit(
        ProductType productType,
        IReadOnlyDictionary<Guid, ResourceType> resourceTypesById,
        IReadOnlyDictionary<Guid, ProductType> productTypesById,
        IReadOnlyDictionary<Guid, List<ProductRecipe>> recipesByProduct,
        Dictionary<Guid, decimal> cache,
        HashSet<Guid> visiting)
    {
        if (cache.TryGetValue(productType.Id, out var cachedWeight))
        {
            return cachedWeight;
        }

        if (!visiting.Add(productType.Id))
        {
            return MinimumWeightPerUnit;
        }

        var recipes = recipesByProduct.GetValueOrDefault(productType.Id) ?? productType.Recipes.ToList();
        if (recipes.Count == 0)
        {
            cache[productType.Id] = MinimumWeightPerUnit;
            visiting.Remove(productType.Id);
            return MinimumWeightPerUnit;
        }

        var totalInputWeight = 0m;
        foreach (var recipe in recipes)
        {
            if (recipe.ResourceTypeId.HasValue && resourceTypesById.TryGetValue(recipe.ResourceTypeId.Value, out var resource))
            {
                totalInputWeight += recipe.Quantity * Math.Max(resource.WeightPerUnit, MinimumWeightPerUnit);
                continue;
            }

            if (recipe.InputProductTypeId.HasValue && productTypesById.TryGetValue(recipe.InputProductTypeId.Value, out var inputProduct))
            {
                totalInputWeight += recipe.Quantity * ComputeProductWeightPerUnit(
                    inputProduct,
                    resourceTypesById,
                    productTypesById,
                    recipesByProduct,
                    cache,
                    visiting);
            }
        }

        visiting.Remove(productType.Id);

        var outputQuantity = Math.Max(productType.OutputQuantity, 1m);
        var weightPerUnit = totalInputWeight > 0m
            ? totalInputWeight / outputQuantity
            : MinimumWeightPerUnit;
        cache[productType.Id] = weightPerUnit;
        return weightPerUnit;
    }

    public static double ComputeDistanceKm(double latitudeA, double longitudeA, double latitudeB, double longitudeB)
    {
        const double earthRadiusKm = 6371d;
        var deltaLatitude = DegreesToRadians(latitudeB - latitudeA);
        var deltaLongitude = DegreesToRadians(longitudeB - longitudeA);
        var originLatitude = DegreesToRadians(latitudeA);
        var destinationLatitude = DegreesToRadians(latitudeB);

        var sinLatitude = Math.Sin(deltaLatitude / 2d);
        var sinLongitude = Math.Sin(deltaLongitude / 2d);

        var haversine = (sinLatitude * sinLatitude)
                        + (Math.Cos(originLatitude) * Math.Cos(destinationLatitude) * sinLongitude * sinLongitude);
        var arc = 2d * Math.Atan2(Math.Sqrt(haversine), Math.Sqrt(1d - haversine));
        return earthRadiusKm * arc;
    }

    private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180d;
}
