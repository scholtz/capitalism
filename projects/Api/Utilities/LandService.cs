using Api.Data;
using Api.Data.Entities;
using Api.Engine;
using Microsoft.EntityFrameworkCore;

namespace Api.Utilities;

/// <summary>
/// Creates and reappraises land so buildings always attach to real map parcels.
/// </summary>
public static class LandService
{
    private const decimal MinPopulationIndex = 0.35m;
    private const decimal MaxPopulationIndex = 1.85m;
    private const double NeighborhoodRadiusKm = 1.5d;

    /// <summary>
    /// Land capture rate: the fraction of the quality-discounted total extractable resource value
    /// that is added to the asking price as a raw-material deposit premium.
    /// Rationale: a mine owner captures ongoing extraction benefit, so land price reflects
    /// a portion (10%) of the discounted resource value rather than the full reserve.
    /// </summary>
    public const decimal ResourcePremiumCaptureRate = 0.10m;

    public static async Task EnsureMinimumAvailableLotsAsync(
        AppDbContext db,
        long currentTick,
        IEnumerable<Guid>? cityIds = null)
    {
        var cityIdSet = cityIds?.Distinct().ToHashSet();

        var cities = await db.Cities
            .Where(city => cityIdSet == null || cityIdSet.Contains(city.Id))
            .ToListAsync();

        var lots = await db.BuildingLots
            .Include(lot => lot.ResourceType)
            .Where(lot => cityIdSet == null || cityIdSet.Contains(lot.CityId))
            .ToListAsync();

        var buildings = await db.Buildings
            .Where(building => cityIdSet == null || cityIdSet.Contains(building.CityId))
            .ToListAsync();

        EnsureMinimumAvailableLots(db, cities, lots, buildings, currentTick);
    }

    public static void EnsureMinimumAvailableLots(
        AppDbContext db,
        IReadOnlyCollection<City> cities,
        IReadOnlyCollection<BuildingLot> existingLots,
        IReadOnlyCollection<Building> buildings,
        long currentTick)
    {
        foreach (var city in cities)
        {
            var cityBuildings = buildings.Where(building => building.CityId == city.Id).ToList();
            var cityLots = existingLots.Where(lot => lot.CityId == city.Id).ToList();

            foreach (var buildingType in BuildingType.All)
            {
                var availableCount = cityLots.Count(lot => lot.OwnerCompanyId == null && SupportsBuildingType(lot, buildingType));
                var missingCount = Math.Max(0, GameConstants.MinimumAvailableLotsPerBuildingType - availableCount);

                for (var offset = 0; offset < missingCount; offset++)
                {
                    var sequence = cityLots.Count + 1;
                    var generatedLot = CreateGeneratedLot(city, buildingType, sequence, cityBuildings, currentTick);
                    db.BuildingLots.Add(generatedLot);
                    cityLots.Add(generatedLot);
                }
            }

            foreach (var lot in cityLots)
            {
                RefreshLandState(lot, city, cityBuildings, currentTick);

                if (lot.BuildingId is not Guid buildingId)
                {
                    continue;
                }

                var building = cityBuildings.FirstOrDefault(candidate => candidate.Id == buildingId);
                if (building is null)
                {
                    continue;
                }

                // Land coordinates are authoritative. Keep attached buildings pinned to the parcel.
                building.Latitude = lot.Latitude;
                building.Longitude = lot.Longitude;
            }
        }
    }

    public static void RefreshLandState(
        BuildingLot lot,
        City city,
        IReadOnlyCollection<Building> cityBuildings,
        long currentTick)
    {
        if (lot.BasePrice <= 0m)
        {
            lot.BasePrice = lot.Price > 0m
                ? lot.Price
                : ComputeBasePrice(city, FirstSuitableType(lot), ComputeDistanceKmToCityCenter(lot, city));
        }

        lot.PopulationIndex = ComputePopulationIndex(lot, city, cityBuildings, currentTick);
        // Price = appraised land value + raw-material deposit premium (when applicable).
        // The land component fluctuates with population index; the resource premium is fixed
        // by the deposit characteristics and gives mine lots a price above the base land value.
        lot.Price = ComputeAppraisedPrice(lot.BasePrice, lot.PopulationIndex)
                  + ComputeResourcePremium(lot.ResourceType, lot.MaterialQuality, lot.MaterialQuantity);
    }

    /// <summary>
    /// Computes the resource-deposit premium added to the asking price of a lot.
    /// Formula: quantity × resourceBasePrice × quality × <see cref="ResourcePremiumCaptureRate"/>.
    /// Returns zero when any required value is missing or non-positive.
    /// </summary>
    public static decimal ComputeResourcePremium(
        ResourceType? resourceType,
        decimal? materialQuality,
        decimal? materialQuantity)
    {
        if (resourceType is null || materialQuality is null or <= 0m || materialQuantity is null or <= 0m)
        {
            return 0m;
        }

        var premium = materialQuantity.Value * resourceType.BasePrice * materialQuality.Value * ResourcePremiumCaptureRate;
        return decimal.Round(premium, 2, MidpointRounding.AwayFromZero);
    }

    public static decimal ComputePopulationIndex(
        BuildingLot lot,
        City city,
        IReadOnlyCollection<Building> cityBuildings,
        long currentTick)
    {
        var distanceKm = ComputeDistanceKmToCityCenter(lot, city);
        var distanceScore = Clamp(1.2m - (decimal)(distanceKm / 10d), 0.2m, 1.2m);
        var cityPopulationScore = Clamp(city.Population / 1_500_000m, 0.2m, 1.25m);

        var nearbyDemandDrivers = cityBuildings
            .Where(building => building.Type is BuildingType.Apartment or BuildingType.Commercial)
            .Select(building => new
            {
                Building = building,
                DistanceKm = GlobalExchangeCalculator.ComputeDistanceKm(
                    lot.Latitude,
                    lot.Longitude,
                    building.Latitude,
                    building.Longitude)
            })
            .Where(entry => entry.DistanceKm <= NeighborhoodRadiusKm)
            .Select(entry =>
            {
                var occupancy = entry.Building.OccupancyPercent.HasValue
                    ? Clamp(entry.Building.OccupancyPercent.Value / 100m, 0m, 1.2m)
                    : 0.55m;
                var proximityWeight = 1m / (1m + (decimal)entry.DistanceKm);
                return occupancy * proximityWeight;
            })
            .ToList();

        var neighborhoodScore = nearbyDemandDrivers.Count > 0
            ? Clamp(nearbyDemandDrivers.Average(), 0.15m, 1.25m)
            : 0.35m;

        var dailyTick = currentTick / 24;
        var jitterSeed = Math.Abs(HashCode.Combine(lot.Id, dailyTick));
        var jitter = (jitterSeed % 1000) / 1000m;

        var rawScore = 0.30m
            + (distanceScore * 0.40m)
            + (cityPopulationScore * 0.20m)
            + (neighborhoodScore * 0.20m)
            + (jitter * 0.10m);

        return Clamp(decimal.Round(rawScore, 4, MidpointRounding.AwayFromZero), MinPopulationIndex, MaxPopulationIndex);
    }

    public static decimal ComputeAppraisedPrice(decimal basePrice, decimal populationIndex)
    {
        if (basePrice <= 0m)
        {
            return 0m;
        }

        var multiplier = 0.6m + (Clamp(populationIndex, MinPopulationIndex, MaxPopulationIndex) * 0.4m);
        return decimal.Round(basePrice * multiplier, 2, MidpointRounding.AwayFromZero);
    }

    private static BuildingLot CreateGeneratedLot(
        City city,
        string buildingType,
        int sequence,
        IReadOnlyCollection<Building> cityBuildings,
        long currentTick)
    {
        var radiusKm = PreferredRadiusKm(buildingType, sequence);
        var angleDegrees = Math.Abs(HashCode.Combine(city.Id, buildingType, sequence)) % 360;
        var angleRadians = angleDegrees * (Math.PI / 180d);

        var latitude = city.Latitude + KmToLatitudeDelta(radiusKm * Math.Cos(angleRadians));
        var longitude = city.Longitude + KmToLongitudeDelta(radiusKm * Math.Sin(angleRadians), city.Latitude);
        var basePrice = ComputeBasePrice(city, buildingType, radiusKm);

        var lot = new BuildingLot
        {
            Id = Guid.NewGuid(),
            CityId = city.Id,
            Name = $"{FormatBuildingType(buildingType)} Land {sequence:00}",
            Description = $"Procedurally generated {FormatBuildingType(buildingType).ToLowerInvariant()} parcel in {city.Name}.",
            District = DistrictForBuildingType(buildingType),
            Latitude = latitude,
            Longitude = longitude,
            SuitableTypes = buildingType,
            BasePrice = basePrice,
            Price = basePrice,
            PopulationIndex = 1m,
            ConcurrencyToken = Guid.NewGuid(),
        };

        RefreshLandState(lot, city, cityBuildings, currentTick);
        return lot;
    }

    private static decimal ComputeBasePrice(City city, string? buildingType, double radiusKm)
    {
        var typeBasePrice = buildingType switch
        {
            BuildingType.Mine => 70_000m,
            BuildingType.Factory => 85_000m,
            BuildingType.SalesShop => 95_000m,
            BuildingType.ResearchDevelopment => 120_000m,
            BuildingType.Apartment => 140_000m,
            BuildingType.Commercial => 135_000m,
            BuildingType.MediaHouse => 165_000m,
            BuildingType.Bank => 180_000m,
            BuildingType.Exchange => 170_000m,
            BuildingType.PowerPlant => 150_000m,
            _ => 100_000m,
        };

        var cityMultiplier = 0.85m
            + Clamp(city.AverageRentPerSqm / 40m, 0.10m, 0.70m)
            + Clamp(city.Population / 2_500_000m, 0.05m, 0.60m);
        var radiusDiscount = Clamp(1.15m - ((decimal)radiusKm / 12m), 0.75m, 1.15m);

        return decimal.Round(typeBasePrice * cityMultiplier * radiusDiscount, 2, MidpointRounding.AwayFromZero);
    }

    private static double PreferredRadiusKm(string buildingType, int sequence)
    {
        var baseRadius = buildingType switch
        {
            BuildingType.SalesShop => 0.8d,
            BuildingType.Apartment => 1.0d,
            BuildingType.Commercial => 1.2d,
            BuildingType.Bank => 1.3d,
            BuildingType.MediaHouse => 1.4d,
            BuildingType.ResearchDevelopment => 2.2d,
            BuildingType.Exchange => 2.6d,
            BuildingType.Factory => 3.0d,
            BuildingType.PowerPlant => 4.5d,
            BuildingType.Mine => 5.5d,
            _ => 2.5d,
        };

        return baseRadius + ((sequence - 1) % 5) * 0.8d;
    }

    private static string DistrictForBuildingType(string buildingType)
    {
        return buildingType switch
        {
            BuildingType.SalesShop => "Retail District",
            BuildingType.Apartment => "Residential Quarter",
            BuildingType.Commercial => "Business District",
            BuildingType.Bank => "Financial District",
            BuildingType.MediaHouse => "Media Quarter",
            BuildingType.ResearchDevelopment => "Innovation Park",
            BuildingType.Exchange => "Trade Zone",
            BuildingType.Factory => "Industrial Zone",
            BuildingType.PowerPlant => "Utility Belt",
            BuildingType.Mine => "Extraction Belt",
            _ => "Mixed District",
        };
    }

    private static string FormatBuildingType(string buildingType)
    {
        return buildingType.Replace('_', ' ')
            .ToLowerInvariant()
            .Replace("research development", "research & development")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(segment => char.ToUpperInvariant(segment[0]) + segment[1..])
            .Aggregate((left, right) => $"{left} {right}");
    }

    private static bool SupportsBuildingType(BuildingLot lot, string buildingType)
    {
        return lot.SuitableTypes
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Contains(buildingType, StringComparer.OrdinalIgnoreCase);
    }

    private static string? FirstSuitableType(BuildingLot lot)
    {
        return lot.SuitableTypes
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();
    }

    private static double ComputeDistanceKmToCityCenter(BuildingLot lot, City city)
    {
        return GlobalExchangeCalculator.ComputeDistanceKm(lot.Latitude, lot.Longitude, city.Latitude, city.Longitude);
    }

    private static double KmToLatitudeDelta(double km)
    {
        return km / 110.574d;
    }

    private static double KmToLongitudeDelta(double km, double latitude)
    {
        var latitudeRadians = latitude * (Math.PI / 180d);
        var divisor = 111.320d * Math.Cos(latitudeRadians);
        return divisor == 0d ? 0d : km / divisor;
    }

    private static decimal Clamp(decimal value, decimal min, decimal max)
    {
        return Math.Min(Math.Max(value, min), max);
    }
}