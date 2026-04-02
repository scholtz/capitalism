using Api.Data.Entities;

namespace Api.Engine.Phases;

/// <summary>
/// Processes R&amp;D building units: PRODUCT_QUALITY and BRAND_QUALITY.
/// PRODUCT_QUALITY gradually increases the company's internal knowledge for
/// manufacturing a product, modelled as the product-specific brand quality.
/// BRAND_QUALITY researches branding efficiency (raises brand quality for
/// a specific scope: product, industry, or company-wide).
/// </summary>
public sealed class ResearchPhase : ITickPhase
{
    public string Name => "Research";
    public int Order => 800;

    public Task ProcessAsync(TickContext context)
    {
        if (!context.BuildingsByType.TryGetValue(BuildingType.ResearchDevelopment, out var rdBuildings))
            return Task.CompletedTask;

        foreach (var building in rdBuildings)
        {
            if (!context.UnitsByBuilding.TryGetValue(building.Id, out var units))
                continue;

            foreach (var unit in units)
            {
                switch (unit.UnitType)
                {
                    case UnitType.ProductQuality:
                        ProcessProductQuality(context, building, unit);
                        break;
                    case UnitType.BrandQuality:
                        ProcessBrandQuality(context, building, unit);
                        break;
                }
            }
        }

        return Task.CompletedTask;
    }

    private static void ProcessProductQuality(TickContext context, Building building, BuildingUnit unit)
    {
        if (!unit.ProductTypeId.HasValue) return;
        if (!context.ProductTypesById.TryGetValue(unit.ProductTypeId.Value, out var productType))
            return;

        var rate = GameConstants.ResearchQualityRate(unit.Level);
        var brand = context.GetOrCreateBrand(building.CompanyId, productType.Id, productType.Name);
        brand.Quality = Math.Min(1m, brand.Quality + rate);
    }

    private static void ProcessBrandQuality(TickContext context, Building building, BuildingUnit unit)
    {
        var rate = GameConstants.ResearchEfficiencyRate(unit.Level);

        // Determine scope from unit configuration.
        var scope = unit.BrandScope ?? BrandScope.Company;
        var selectedProductType = unit.ProductTypeId.HasValue && context.ProductTypesById.TryGetValue(unit.ProductTypeId.Value, out var productType)
            ? productType
            : null;

        if (scope is BrandScope.Product or BrandScope.Category && selectedProductType is null)
        {
            return;
        }

        // BRAND_QUALITY research raises the marketing efficiency multiplier on scope-matching brands.
        // This means marketing budget becomes more effective — it does NOT directly grant awareness.
        var brand = scope switch
        {
            BrandScope.Product when selectedProductType is not null =>
                context.GetOrCreateBrand(building.CompanyId, selectedProductType.Id, selectedProductType.Name),
            BrandScope.Category when selectedProductType is not null =>
                context.GetOrCreateCategoryBrand(building.CompanyId, selectedProductType.Industry),
            _ =>
                context.GetOrCreateCompanyBrand(building.CompanyId)
        };

        brand.MarketingEfficiencyMultiplier = Math.Min(
            GameConstants.MaxMarketingEfficiencyMultiplier,
            brand.MarketingEfficiencyMultiplier + rate);
    }
}
