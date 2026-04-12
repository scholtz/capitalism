using Api.Data;
using Api.Data.Entities;
using Api.Engine;
using Api.Security;
using Api.Utilities;
using HotChocolate.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Api.Types;

public sealed partial class Query
{
    /// <summary>Lists building lots for a city, including ownership and availability state.</summary>
    public async Task<List<BuildingLot>> GetCityLots(Guid cityId, [Service] AppDbContext db)
    {
        return await db.BuildingLots
            .Include(lot => lot.OwnerCompany)
            .Include(lot => lot.Building)
            .Include(lot => lot.ResourceType)
            .Where(lot => lot.CityId == cityId)
            .OrderBy(lot => lot.District)
            .ThenBy(lot => lot.Name)
            .ToListAsync();
    }

    /// <summary>Gets a single building lot by ID.</summary>
    public async Task<BuildingLot?> GetLot(Guid id, [Service] AppDbContext db)
    {
        return await db.BuildingLots
            .Include(lot => lot.OwnerCompany)
            .Include(lot => lot.Building)
            .Include(lot => lot.ResourceType)
            .FirstOrDefaultAsync(lot => lot.Id == id);
    }

    /// <summary>
    /// Returns product marketplace listings from active player SELL exchange orders.
    /// Covers finished goods, intermediate goods, and any product types on the exchange.
    /// This query is public and does not require authentication.
    /// </summary>
    public async Task<List<GlobalExchangeProductListing>> GetGlobalExchangeProductListings(
        Guid? productTypeId,
        [Service] AppDbContext db)
    {
        var ordersQuery = db.ExchangeOrders
            .Where(o => o.Side == "SELL" && o.IsActive && o.RemainingQuantity > 0m
                        && o.ProductTypeId.HasValue && !o.ResourceTypeId.HasValue)
            .Include(o => o.Company)
            .Include(o => o.ExchangeBuilding)
            .AsQueryable();

        if (productTypeId.HasValue)
            ordersQuery = ordersQuery.Where(o => o.ProductTypeId == productTypeId.Value);

        var orders = await ordersQuery
            .OrderBy(o => o.ProductTypeId)
            .ThenBy(o => o.PricePerUnit)
            .ToListAsync();

        var cityIds = orders.Select(o => o.ExchangeBuilding.CityId).Distinct().ToList();
        var cities = await db.Cities
            .Where(c => cityIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id);

        var productTypeIds = orders.Select(o => o.ProductTypeId!.Value).Distinct().ToList();
        var productTypes = await db.ProductTypes
            .Where(p => productTypeIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id);

        return orders
            .Select(o =>
            {
                cities.TryGetValue(o.ExchangeBuilding.CityId, out var city);
                productTypes.TryGetValue(o.ProductTypeId!.Value, out var product);
                return new GlobalExchangeProductListing
                {
                    OrderId = o.Id,
                    ProductTypeId = o.ProductTypeId!.Value,
                    ProductName = product?.Name ?? string.Empty,
                    ProductSlug = product?.Slug ?? string.Empty,
                    ProductIndustry = product?.Industry ?? string.Empty,
                    UnitSymbol = product?.UnitSymbol ?? string.Empty,
                    UnitName = product?.UnitName ?? string.Empty,
                    BasePrice = product?.BasePrice ?? 0m,
                    PricePerUnit = o.PricePerUnit,
                    RemainingQuantity = o.RemainingQuantity,
                    SellerCityId = city?.Id ?? Guid.Empty,
                    SellerCityName = city?.Name ?? string.Empty,
                    SellerCompanyId = o.CompanyId,
                    SellerCompanyName = o.Company.Name,
                    CreatedAtUtc = o.CreatedAtUtc,
                };
            })
            .OrderBy(l => l.ProductName)
            .ThenBy(l => l.PricePerUnit)
            .ToList();
    }

    /// <summary>
    /// Returns city-level global exchange offers for raw materials, including
    /// quality and estimated transit cost into the destination city.
    /// </summary>
    public async Task<List<GlobalExchangeOffer>> GetGlobalExchangeOffers(
        Guid destinationCityId,
        Guid? resourceTypeId,
        [Service] AppDbContext db)
    {
        var destinationCity = await db.Cities.FirstOrDefaultAsync(city => city.Id == destinationCityId);
        if (destinationCity is null)
        {
            return [];
        }

        var cities = await db.Cities
            .Include(city => city.Resources)
            .OrderBy(city => city.Name)
            .ToListAsync();

        var resourceQuery = db.ResourceTypes.AsQueryable();
        if (resourceTypeId.HasValue)
        {
            resourceQuery = resourceQuery.Where(resource => resource.Id == resourceTypeId.Value);
        }

        var resources = await resourceQuery
            .OrderBy(resource => resource.Name)
            .ToListAsync();

        return cities
            .SelectMany(city => resources.Select(resource =>
            {
                var abundance = city.Resources
                    .FirstOrDefault(entry => entry.ResourceTypeId == resource.Id)?.Abundance
                    ?? GlobalExchangeCalculator.DefaultMissingAbundance;
                var exchangePrice = GlobalExchangeCalculator.ComputeExchangePrice(city, resource, abundance);
                var transitCost = GlobalExchangeCalculator.ComputeTransitCostPerUnit(city, destinationCity, resource);

                return new GlobalExchangeOffer
                {
                    CityId = city.Id,
                    CityName = city.Name,
                    ResourceTypeId = resource.Id,
                    ResourceName = resource.Name,
                    ResourceSlug = resource.Slug,
                    UnitSymbol = resource.UnitSymbol,
                    LocalAbundance = decimal.Round(abundance, 4, MidpointRounding.AwayFromZero),
                    ExchangePricePerUnit = exchangePrice,
                    EstimatedQuality = GlobalExchangeCalculator.ComputeExchangeQuality(abundance),
                    TransitCostPerUnit = transitCost,
                    DeliveredPricePerUnit = exchangePrice + transitCost,
                    DistanceKm = decimal.Round(
                        (decimal)GlobalExchangeCalculator.ComputeDistanceKm(
                            city.Latitude,
                            city.Longitude,
                            destinationCity.Latitude,
                            destinationCity.Longitude),
                        1,
                        MidpointRounding.AwayFromZero)
                };
            }))
            .OrderBy(offer => offer.DeliveredPricePerUnit)
            .ThenByDescending(offer => offer.EstimatedQuality)
            .ThenBy(offer => offer.CityName)
            .ToList();
    }
}
