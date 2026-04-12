using Api.Data.Entities;
using Api.Utilities;

namespace Api.Engine.Phases;

public sealed partial class PurchasingPhase
{
    /// <summary>
    /// Attempts to fill the purchase unit from the city-level global exchange
    /// (infinite counterparty). Picks the source city with the lowest delivered
    /// price (exchange price + transit cost) that satisfies MaxPrice and MinQuality.
    /// When <paramref name="unit"/> has a <see cref="BuildingUnit.LockedCityId"/> set,
    /// only that specific city is considered as a source.
    /// Returns the amount bought, quality, and total cost.
    /// </summary>
    private static (decimal amountBought, decimal quality, decimal cost) BuyFromGlobalExchange(
        TickContext context,
        Building building,
        BuildingUnit unit,
        Company company,
        Guid resourceId,
        decimal maxAmountToBuy,
        decimal maxPrice,
        decimal minQuality)
    {
        if (!context.ResourceTypesById.TryGetValue(resourceId, out var resource)) return (0m, 0m, 0m);
        if (!context.CitiesById.TryGetValue(building.CityId, out var destinationCity)) return (0m, 0m, 0m);

        var purchaseSource = unit.PurchaseSource ?? "OPTIMAL";
        var candidateCities = unit.LockedCityId.HasValue && purchaseSource == "EXCHANGE"
            ? context.CitiesById.Values.Where(c => c.Id == unit.LockedCityId.Value)
            : context.CitiesById.Values;

        var bestOffer = candidateCities
            .Select(sourceCity =>
            {
                var abundance = context.ResourcesByCity
                    .GetValueOrDefault(sourceCity.Id)
                    ?.FirstOrDefault(cr => cr.ResourceTypeId == resourceId)
                    ?.Abundance ?? GlobalExchangeCalculator.DefaultMissingAbundance;

                var exchangePrice = GlobalExchangeCalculator.ComputeExchangePrice(sourceCity, resource, abundance);
                var transitCost = GlobalExchangeCalculator.ComputeTransitCostPerUnit(sourceCity, destinationCity, resource);
                var deliveredPrice = exchangePrice + transitCost;
                var quality = GlobalExchangeCalculator.ComputeExchangeQuality(abundance);

                return (sourceCity, exchangePrice, transitCost, deliveredPrice, quality);
            })
            .Where(o => o.deliveredPrice <= maxPrice && o.quality >= minQuality)
            .OrderBy(o => o.deliveredPrice)
            .ThenByDescending(o => o.quality)
            .FirstOrDefault();

        if (bestOffer == default) return (0m, 0m, 0m);

        var amountToBuy = maxAmountToBuy;
        var totalCost = amountToBuy * bestOffer.deliveredPrice;

        if (company.Cash < totalCost)
        {
            amountToBuy = company.Cash / bestOffer.deliveredPrice;
            amountToBuy = Math.Floor(amountToBuy * 10000m) / 10000m;
            totalCost = amountToBuy * bestOffer.deliveredPrice;
        }

        if (amountToBuy <= 0m) return (0m, 0m, 0m);

        company.Cash -= totalCost;
        var purchasingCost = amountToBuy * bestOffer.exchangePrice;
        var shippingCost = amountToBuy * bestOffer.transitCost;

        context.Db.LedgerEntries.Add(new LedgerEntry
        {
            Id = Guid.NewGuid(),
            CompanyId = company.Id,
            BuildingId = building.Id,
            BuildingUnitId = unit.Id,
            Category = LedgerCategory.PurchasingCost,
            Description = $"Global exchange purchase: {resource.Name} from {bestOffer.sourceCity.Name}",
            Amount = -purchasingCost,
            RecordedAtTick = context.CurrentTick,
            RecordedAtUtc = DateTime.UtcNow,
            ResourceTypeId = resourceId,
        });

        if (shippingCost > 0m)
        {
            context.Db.LedgerEntries.Add(new LedgerEntry
            {
                Id = Guid.NewGuid(),
                CompanyId = company.Id,
                BuildingId = building.Id,
                BuildingUnitId = unit.Id,
                Category = LedgerCategory.ShippingCost,
                Description = $"Shipping: {resource.Name} from {bestOffer.sourceCity.Name}",
                Amount = -shippingCost,
                RecordedAtTick = context.CurrentTick,
                RecordedAtUtc = DateTime.UtcNow,
                ResourceTypeId = resourceId,
            });
        }

        return (amountToBuy, bestOffer.quality, totalCost);
    }
}
