using Api.Data.Entities;

namespace Api.Engine.Phases;

/// <summary>
/// Processes MARKETING units inside SALES_SHOP buildings.
/// Deducts the configured budget from the owning company's cash,
/// routes the spend as income to the media-house owner (if a media house is selected),
/// applies channel effectiveness (TV > Radio > Newspaper) and R&amp;D efficiency multipliers,
/// and increases brand awareness for products sold in linked PUBLIC_SALES units.
/// </summary>
public sealed class MarketingPhase : ITickPhase
{
    public string Name => "Marketing";
    public int Order => 700;

    public Task ProcessAsync(TickContext context)
    {
        if (!context.BuildingsByType.TryGetValue(BuildingType.SalesShop, out var shops))
            return Task.CompletedTask;

        foreach (var building in shops)
        {
            if (!context.UnitsByBuilding.TryGetValue(building.Id, out var units))
                continue;
            if (!context.CompaniesById.TryGetValue(building.CompanyId, out var company))
                continue;

            foreach (var unit in units)
            {
                if (unit.UnitType != UnitType.Marketing) continue;
                ProcessMarketingUnit(context, building, unit, company, units);
            }
        }

        return Task.CompletedTask;
    }

    private static void ProcessMarketingUnit(
        TickContext context,
        Building building,
        BuildingUnit unit,
        Company company,
        List<BuildingUnit> allUnits)
    {
        if (unit.Budget is null || unit.Budget <= 0m) return;

        var budget = Math.Min(unit.Budget.Value, company.Cash);
        if (budget <= 0m) return;

        // Find product types in linked PUBLIC_SALES units to target.
        var linkedUnits = context.GetOutgoingLinkedUnits(unit);
        var productIds = linkedUnits
            .Where(u => u.UnitType == UnitType.PublicSales && u.ProductTypeId.HasValue)
            .Select(u => u.ProductTypeId!.Value)
            .Distinct()
            .ToList();

        // Fallback: if no direct links, use all PUBLIC_SALES units in same building.
        if (productIds.Count == 0)
        {
            productIds = allUnits
                .Where(u => u.UnitType == UnitType.PublicSales && u.ProductTypeId.HasValue)
                .Select(u => u.ProductTypeId!.Value)
                .Distinct()
                .ToList();
        }

        if (productIds.Count == 0) return;

        // Resolve the selected media house and compute channel effectiveness.
        Building? mediaHouse = null;
        var channelMultiplier = 1.0m;
        var channelDescription = "direct";

        if (unit.MediaHouseBuildingId.HasValue)
        {
            mediaHouse = context.BuildingsById.TryGetValue(unit.MediaHouseBuildingId.Value, out var mh)
                && mh.Type == BuildingType.MediaHouse
                && mh.CityId == building.CityId
                ? mh : null;

            if (mediaHouse is not null)
            {
                channelMultiplier = MediaType.EffectivenessMultiplier(mediaHouse.MediaType);
                channelDescription = mediaHouse.MediaType?.ToLowerInvariant() ?? "media";
            }
        }

        company.Cash -= budget;

        context.Db.LedgerEntries.Add(new LedgerEntry
        {
            Id = Guid.NewGuid(),
            CompanyId = company.Id,
            BuildingId = building.Id,
            BuildingUnitId = unit.Id,
            Category = LedgerCategory.Marketing,
            Description = $"Marketing spend via {channelDescription}",
            Amount = -budget,
            RecordedAtTick = context.CurrentTick,
            RecordedAtUtc = DateTime.UtcNow,
        });

        // Route income to the media house owner, if applicable and if a different company.
        if (mediaHouse is not null
            && context.CompaniesById.TryGetValue(mediaHouse.CompanyId, out var mediaOwner)
            && mediaOwner.Id != company.Id)
        {
            mediaOwner.Cash += budget;
            context.Db.LedgerEntries.Add(new LedgerEntry
            {
                Id = Guid.NewGuid(),
                CompanyId = mediaOwner.Id,
                BuildingId = mediaHouse.Id,
                Category = LedgerCategory.MediaHouseIncome,
                Description = $"Advertising revenue ({channelDescription})",
                Amount = budget,
                RecordedAtTick = context.CurrentTick,
                RecordedAtUtc = DateTime.UtcNow,
            });
        }

        var budgetPerProduct = budget / productIds.Count;

        foreach (var productId in productIds)
        {
            var productName = context.ProductTypesById.TryGetValue(productId, out var pt) ? pt.Name : "Product";
            var brand = context.GetOrCreateBrand(building.CompanyId, productId, $"{company.Cash:F0} – {productName}");

            // Apply marketing efficiency multiplier from BRAND_QUALITY R&D.
            // This is the causal chain: R&D → higher efficiency → marketing budget produces more awareness.
            var efficiencyBrand = context.FindBrand(building.CompanyId, productId, pt?.Industry);
            var efficiencyMultiplier = efficiencyBrand?.MarketingEfficiencyMultiplier ?? 1m;

            // Combined: channel reach × R&D efficiency.
            brand.Awareness = Math.Min(1m, brand.Awareness + budgetPerProduct * GameConstants.BrandAwarenessPerBudget * channelMultiplier * efficiencyMultiplier);
        }
    }
}
