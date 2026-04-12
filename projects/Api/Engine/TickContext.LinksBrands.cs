using Api.Data.Entities;

namespace Api.Engine;

public sealed partial class TickContext
{
    /// <summary>Returns units that this unit pushes resources TO (outgoing links).</summary>
    public List<BuildingUnit> GetOutgoingLinkedUnits(BuildingUnit unit)
    {
        if (!UnitsByBuildingPosition.TryGetValue(unit.BuildingId, out var posMap))
            return [];

        var neighbors = new List<BuildingUnit>();
        if (unit.LinkRight && posMap.TryGetValue((unit.GridX + 1, unit.GridY), out var right))
            neighbors.Add(right);
        if (unit.LinkLeft && posMap.TryGetValue((unit.GridX - 1, unit.GridY), out var left))
            neighbors.Add(left);
        if (unit.LinkDown && posMap.TryGetValue((unit.GridX, unit.GridY + 1), out var down))
            neighbors.Add(down);
        if (unit.LinkUp && posMap.TryGetValue((unit.GridX, unit.GridY - 1), out var up))
            neighbors.Add(up);
        if (unit.LinkDownRight && posMap.TryGetValue((unit.GridX + 1, unit.GridY + 1), out var downRight))
            neighbors.Add(downRight);
        if (unit.LinkDownLeft && posMap.TryGetValue((unit.GridX - 1, unit.GridY + 1), out var downLeft))
            neighbors.Add(downLeft);
        if (unit.LinkUpRight && posMap.TryGetValue((unit.GridX + 1, unit.GridY - 1), out var upRight))
            neighbors.Add(upRight);
        if (unit.LinkUpLeft && posMap.TryGetValue((unit.GridX - 1, unit.GridY - 1), out var upLeft))
            neighbors.Add(upLeft);

        return neighbors;
    }

    /// <summary>Returns units that push resources INTO this unit (incoming links).</summary>
    public List<BuildingUnit> GetIncomingLinkedUnits(BuildingUnit unit)
    {
        if (!UnitsByBuildingPosition.TryGetValue(unit.BuildingId, out var posMap))
            return [];

        var incoming = new List<BuildingUnit>();
        if (posMap.TryGetValue((unit.GridX - 1, unit.GridY), out var left) && left.LinkRight)
            incoming.Add(left);
        if (posMap.TryGetValue((unit.GridX + 1, unit.GridY), out var right) && right.LinkLeft)
            incoming.Add(right);
        if (posMap.TryGetValue((unit.GridX, unit.GridY - 1), out var up) && up.LinkDown)
            incoming.Add(up);
        if (posMap.TryGetValue((unit.GridX, unit.GridY + 1), out var down) && down.LinkUp)
            incoming.Add(down);
        if (posMap.TryGetValue((unit.GridX - 1, unit.GridY - 1), out var upLeft) && upLeft.LinkDownRight)
            incoming.Add(upLeft);
        if (posMap.TryGetValue((unit.GridX + 1, unit.GridY - 1), out var upRight) && upRight.LinkDownLeft)
            incoming.Add(upRight);
        if (posMap.TryGetValue((unit.GridX - 1, unit.GridY + 1), out var downLeft) && downLeft.LinkUpRight)
            incoming.Add(downLeft);
        if (posMap.TryGetValue((unit.GridX + 1, unit.GridY + 1), out var downRight) && downRight.LinkUpLeft)
            incoming.Add(downRight);

        return incoming;
    }

    /// <summary>Finds the best matching brand for a company and product.</summary>
    public Brand? FindBrand(Guid companyId, Guid? productTypeId, string? industry)
    {
        if (!BrandsByCompany.TryGetValue(companyId, out var brands))
            return null;

        if (productTypeId.HasValue)
        {
            var productBrand = brands.FirstOrDefault(b =>
                b.Scope == BrandScope.Product && b.ProductTypeId == productTypeId);
            if (productBrand is not null) return productBrand;
        }

        if (!string.IsNullOrEmpty(industry))
        {
            var categoryBrand = brands.FirstOrDefault(b =>
                b.Scope == BrandScope.Category && b.IndustryCategory == industry);
            if (categoryBrand is not null) return categoryBrand;
        }

        return brands.FirstOrDefault(b => b.Scope == BrandScope.Company);
    }

    public decimal GetCompanyAssetValue(Guid companyId)
    {
        if (!CompaniesById.TryGetValue(companyId, out var company))
        {
            return 0m;
        }

        var companyBuildings = BuildingsById.Values
            .Where(building => building.CompanyId == companyId)
            .ToList();
        var buildingValue = companyBuildings.Sum(Api.Utilities.WealthCalculator.GetBuildingValue);
        var inventoryValue = companyBuildings.Sum(building =>
            InventoryByBuilding.TryGetValue(building.Id, out var inventories)
                ? inventories.Sum(inventory => inventory.Quantity * (inventory.ProductTypeId.HasValue
                    ? ProductTypesById.GetValueOrDefault(inventory.ProductTypeId.Value)?.BasePrice ?? 0m
                    : ResourceTypesById.GetValueOrDefault(inventory.ResourceTypeId ?? Guid.Empty)?.BasePrice ?? 0m))
                : 0m);
        var lotValue = LotsByCompany.TryGetValue(companyId, out var lots)
            ? lots.Sum(Api.Utilities.WealthCalculator.GetLandValue)
            : 0m;

        return company.Cash + buildingValue + inventoryValue + lotValue;
    }

    /// <summary>Finds or creates a brand for a company and product.</summary>
    public Brand GetOrCreateBrand(Guid companyId, Guid productTypeId, string brandName)
    {
        if (!BrandsByCompany.TryGetValue(companyId, out var brands))
        {
            brands = [];
            BrandsByCompany[companyId] = brands;
        }

        var existing = brands.FirstOrDefault(b =>
            b.Scope == BrandScope.Product && b.ProductTypeId == productTypeId);
        if (existing is not null) return existing;

        var brand = new Brand
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            Name = brandName,
            Scope = BrandScope.Product,
            ProductTypeId = productTypeId,
            Awareness = 0m,
            Quality = 0m,
            MarketingEfficiencyMultiplier = 1m,
        };
        brands.Add(brand);
        Db.Brands.Add(brand);
        return brand;
    }

    /// <summary>Finds or creates a category-scoped brand for a company and industry.</summary>
    public Brand GetOrCreateCategoryBrand(Guid companyId, string industry)
    {
        if (!BrandsByCompany.TryGetValue(companyId, out var brands))
        {
            brands = [];
            BrandsByCompany[companyId] = brands;
        }

        var existing = brands.FirstOrDefault(b =>
            b.Scope == BrandScope.Category &&
            string.Equals(b.IndustryCategory, industry, StringComparison.OrdinalIgnoreCase));
        if (existing is not null) return existing;

        var brand = new Brand
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            Name = industry,
            Scope = BrandScope.Category,
            IndustryCategory = industry,
            Awareness = 0m,
            Quality = 0m,
            MarketingEfficiencyMultiplier = 1m,
        };
        brands.Add(brand);
        Db.Brands.Add(brand);
        return brand;
    }

    /// <summary>Finds or creates a company-wide brand.</summary>
    public Brand GetOrCreateCompanyBrand(Guid companyId)
    {
        if (!BrandsByCompany.TryGetValue(companyId, out var brands))
        {
            brands = [];
            BrandsByCompany[companyId] = brands;
        }

        var existing = brands.FirstOrDefault(b => b.Scope == BrandScope.Company);
        if (existing is not null) return existing;

        if (!CompaniesById.TryGetValue(companyId, out var company))
        {
            var fallback = new Brand
            {
                Id = Guid.NewGuid(),
                CompanyId = companyId,
                Name = "Company Brand",
                Scope = BrandScope.Company,
                Awareness = 0m,
                Quality = 0m,
                MarketingEfficiencyMultiplier = 1m,
            };
            brands.Add(fallback);
            Db.Brands.Add(fallback);
            return fallback;
        }

        var brand = new Brand
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            Name = company.Name,
            Scope = BrandScope.Company,
            Awareness = 0m,
            Quality = 0m,
            MarketingEfficiencyMultiplier = 1m,
        };
        brands.Add(brand);
        Db.Brands.Add(brand);
        return brand;
    }
}
