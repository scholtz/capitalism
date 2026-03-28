using Api.Data.Entities;

namespace Api.Utilities;

/// <summary>
/// Centralized pricing, quality, and transit estimation for city-level
/// global exchange offers.
/// </summary>
public static class GlobalExchangeCalculator
{
    public const decimal DefaultMissingAbundance = 0.05m;

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

    public static decimal ComputeTransitCostPerUnit(City sourceCity, City destinationCity, ResourceType resourceType)
    {
        if (sourceCity.Id == destinationCity.Id)
        {
            return 0m;
        }

        var distanceKm = ComputeDistanceKm(sourceCity.Latitude, sourceCity.Longitude, destinationCity.Latitude, destinationCity.Longitude);
        var rawTransitCost = (decimal)distanceKm * Math.Max(resourceType.WeightPerUnit, 0.1m) * 0.0025m;
        return decimal.Round(Math.Max(rawTransitCost, 0.05m), 2, MidpointRounding.AwayFromZero);
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
