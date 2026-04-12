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
    /// <summary>
    /// Returns product-level analytics for a MANUFACTURING unit.
    /// Shows labor/energy cost history, production quantity, and estimated economics
    /// (basePrice × produced) so players can evaluate manufacturing profitability.
    /// </summary>
    [Authorize]
    public async Task<UnitProductAnalytics?> GetUnitProductAnalytics(
        Guid unitId,
        [Service] AppDbContext db,
        [Service] IHttpContextAccessor httpContextAccessor)
    {
        var userId = httpContextAccessor.HttpContext!.User.GetRequiredUserId();

        var unit = await db.BuildingUnits
            .Include(u => u.Building)
            .ThenInclude(b => b.Company)
            .FirstOrDefaultAsync(u => u.Id == unitId);

        if (unit is null || unit.Building.Company.PlayerId != userId) return null;

        // Currently only supported for MANUFACTURING units
        if (unit.UnitType != UnitType.Manufacturing) return null;

        var productTypeId = unit.ProductTypeId;
        string? productName = null;
        decimal? basePrice = null;

        if (productTypeId.HasValue)
        {
            var product = await db.ProductTypes.FindAsync(productTypeId.Value);
            if (product is not null)
            {
                productName = product.Name;
                basePrice = product.BasePrice;
            }
        }

        // Load cost ledger entries for this unit (last 100 ticks, ordered descending then ascending)
        var ledgerEntries = await db.LedgerEntries
            .Where(e => e.BuildingUnitId == unitId
                && (e.Category == LedgerCategory.LaborCost || e.Category == LedgerCategory.EnergyCost))
            .OrderByDescending(e => e.RecordedAtTick)
            .Take(100)
            .ToListAsync();

        // Load resource history for production quantities for this unit
        var resourceHistory = await db.BuildingUnitResourceHistories
            .Where(h => h.BuildingUnitId == unitId && h.ProducedQuantity > 0)
            .OrderByDescending(h => h.Tick)
            .Take(100)
            .ToListAsync();

        // Merge all ticks from both sources
        var allTicks = ledgerEntries.Select(e => e.RecordedAtTick)
            .Union(resourceHistory.Select(h => h.Tick))
            .Distinct()
            .OrderBy(t => t)
            .ToList();

        var snapshots = allTicks.Select(tick =>
        {
            var labor = ledgerEntries
                .Where(e => e.RecordedAtTick == tick && e.Category == LedgerCategory.LaborCost)
                .Sum(e => -e.Amount);
            var energy = ledgerEntries
                .Where(e => e.RecordedAtTick == tick && e.Category == LedgerCategory.EnergyCost)
                .Sum(e => -e.Amount);
            var produced = resourceHistory
                .Where(h => h.Tick == tick)
                .Sum(h => h.ProducedQuantity);
            var totalCost = labor + energy;
            decimal? estRevenue = basePrice.HasValue ? Math.Round(produced * basePrice.Value, 2) : null;
            decimal? estProfit = estRevenue.HasValue ? Math.Round(estRevenue.Value - totalCost, 2) : null;

            return new UnitProductTickSnapshot
            {
                Tick = tick,
                LaborCost = Math.Round(labor, 2),
                EnergyCost = Math.Round(energy, 2),
                TotalCost = Math.Round(totalCost, 2),
                QuantityProduced = produced,
                EstimatedRevenue = estRevenue,
                EstimatedProfit = estProfit,
            };
        }).ToList();

        var totalCostSum = Math.Round(snapshots.Sum(s => s.TotalCost), 2);
        var totalProduced = snapshots.Sum(s => s.QuantityProduced);
        decimal? estRevSum = basePrice.HasValue ? Math.Round(totalProduced * basePrice.Value, 2) : null;
        decimal? estProfitSum = estRevSum.HasValue ? Math.Round(estRevSum.Value - totalCostSum, 2) : null;

        return new UnitProductAnalytics
        {
            BuildingUnitId = unit.Id,
            UnitType = unit.UnitType,
            ProductTypeId = productTypeId,
            ProductName = productName,
            DataFromTick = allTicks.Count > 0 ? allTicks.First() : 0,
            DataToTick = allTicks.Count > 0 ? allTicks.Last() : 0,
            TotalCost = totalCostSum,
            TotalQuantityProduced = totalProduced,
            EstimatedRevenue = estRevSum,
            EstimatedProfit = estProfitSum,
            Snapshots = snapshots,
        };
    }

    /// <summary>
    /// Returns the city-level power balance: total supply from all power plants,
    /// total demand from all consuming buildings, the reserve margin, and a
    /// human-readable status string (BALANCED, CONSTRAINED, or CRITICAL).
    ///
    /// This query is public (no auth required) so players can assess a city
    /// before purchasing a lot.
    /// </summary>
    public async Task<CityPowerBalance> GetCityPowerBalance(Guid cityId, [Service] AppDbContext db)
    {
        var buildings = await db.Buildings
            .Where(b => b.CityId == cityId)
            .ToListAsync();

        var powerPlants = buildings.Where(b => b.Type == Data.Entities.BuildingType.PowerPlant).ToList();
        var consumers = buildings.Where(b => b.Type != Data.Entities.BuildingType.PowerPlant).ToList();

        var totalSupplyMw = powerPlants.Sum(plant =>
            plant.PowerOutput > 0m
                ? plant.PowerOutput!.Value
                : GameConstants.DefaultPowerOutputMw(plant.PowerPlantType));

        var totalDemandMw = consumers.Sum(building =>
            building.PowerConsumption > 0m
                ? building.PowerConsumption
                : GameConstants.PowerDemandMw(building.Type, building.Level));

        var reserveMw = totalSupplyMw - totalDemandMw;
        var reservePercent = totalDemandMw > 0m
            ? decimal.Round(reserveMw / totalDemandMw * 100m, 1, MidpointRounding.AwayFromZero)
            : 100m;

        string status;
        if (totalDemandMw == 0m || reserveMw >= 0m)
            status = "BALANCED";
        else if (totalSupplyMw >= totalDemandMw * 0.5m)
            status = "CONSTRAINED";
        else
            status = "CRITICAL";

        var powerPlantSummaries = powerPlants.Select(p => new PowerPlantSummary
        {
            BuildingId = p.Id,
            BuildingName = p.Name,
            PlantType = p.PowerPlantType ?? Data.Entities.PowerPlantType.Coal,
            OutputMw = p.PowerOutput > 0m
                ? p.PowerOutput!.Value
                : GameConstants.DefaultPowerOutputMw(p.PowerPlantType),
            PowerStatus = p.PowerStatus,
        }).ToList();

        return new CityPowerBalance
        {
            CityId = cityId,
            TotalSupplyMw = totalSupplyMw,
            TotalDemandMw = totalDemandMw,
            ReserveMw = reserveMw,
            ReservePercent = reservePercent,
            Status = status,
            PowerPlants = powerPlantSummaries,
            PowerPlantCount = powerPlants.Count,
            ConsumerBuildingCount = consumers.Count,
        };
    }

    /// <summary>
    /// Returns the authenticated player's first-sale mission status.
    /// The mission tracks the player's onboarding sales shop from initial configuration
    /// through to the moment a real public-sales record is created in the simulation.
    ///
    /// Phase values:
    ///   NO_SHOP          — onboarding is not yet complete (no shop building tracked).
    ///   CONFIGURE_SHOP   — shop exists but has readiness blockers preventing the first sale.
    ///   AWAITING_FIRST_SALE — shop is fully configured; waiting for the simulation to record a sale.
    ///   FIRST_SALE_RECORDED — a real PublicSalesRecord exists for this shop; mission complete.
    ///   ALREADY_COMPLETED   — the first-sale milestone was previously acknowledged and persisted.
    ///
    /// The <see cref="FirstSaleMissionStatus.Blockers"/> list explains WHY the shop is not ready
    /// (only relevant when phase is CONFIGURE_SHOP).
    /// </summary>
    [Authorize]
    public async Task<FirstSaleMissionStatus> GetFirstSaleMission(
        [Service] AppDbContext db,
        [Service] IHttpContextAccessor httpContextAccessor)
    {
        var userId = httpContextAccessor.HttpContext!.User.GetRequiredUserId();

        var player = await db.Players
            .Include(p => p.Companies)
            .FirstOrDefaultAsync(p => p.Id == userId);

        if (player is null)
            return new FirstSaleMissionStatus { Phase = FirstSaleMissionPhase.NoShop };

        // Already acknowledged by the player
        if (player.OnboardingFirstSaleCompletedAtUtc is not null)
            return new FirstSaleMissionStatus { Phase = FirstSaleMissionPhase.AlreadyCompleted };

        // No shop being tracked
        if (player.OnboardingShopBuildingId is null)
            return new FirstSaleMissionStatus { Phase = FirstSaleMissionPhase.NoShop };

        var shopBuilding = await db.Buildings
            .Include(b => b.Units)
            .FirstOrDefaultAsync(b => b.Id == player.OnboardingShopBuildingId);

        if (shopBuilding is null)
            return new FirstSaleMissionStatus { Phase = FirstSaleMissionPhase.NoShop };

        // Check if a real sale has already happened for this shop
        var firstSaleRecord = await db.PublicSalesRecords
            .Include(r => r.ProductType)
            .Where(r => r.BuildingId == shopBuilding.Id && r.QuantitySold > 0m)
            .OrderBy(r => r.Tick)
            .FirstOrDefaultAsync();

        if (firstSaleRecord is not null)
        {
            return new FirstSaleMissionStatus
            {
                Phase = FirstSaleMissionPhase.FirstSaleRecorded,
                ShopBuildingId = shopBuilding.Id,
                ShopName = shopBuilding.Name,
                FirstSaleRevenue = firstSaleRecord.Revenue,
                FirstSaleProductName = firstSaleRecord.ProductType?.Name,
                FirstSaleTick = firstSaleRecord.Tick,
                FirstSaleQuantity = firstSaleRecord.QuantitySold,
                FirstSalePricePerUnit = firstSaleRecord.PricePerUnit,
            };
        }

        // Shop exists — compute blockers
        var blockers = new List<string>();

        if (shopBuilding.IsUnderConstruction)
        {
            blockers.Add(FirstSaleMissionBlocker.BuildingUnderConstruction);
        }

        var publicSalesUnit = shopBuilding.Units
            .FirstOrDefault(u => string.Equals(u.UnitType, UnitType.PublicSales, StringComparison.Ordinal));

        if (publicSalesUnit is null)
        {
            blockers.Add(FirstSaleMissionBlocker.PublicSalesUnitMissing);
        }
        else
        {
            if (publicSalesUnit.MinPrice is null or <= 0m)
                blockers.Add(FirstSaleMissionBlocker.PriceNotSet);

            // Check inventory in the shop's public-sales unit
            var hasInventory = await db.Inventories
                .AnyAsync(inv => inv.BuildingUnitId == publicSalesUnit.Id && inv.Quantity > 0m);

            if (!hasInventory)
                blockers.Add(FirstSaleMissionBlocker.NoInventory);
        }

        var phase = blockers.Count == 0
            ? FirstSaleMissionPhase.AwaitingFirstSale
            : FirstSaleMissionPhase.ConfigureShop;

        return new FirstSaleMissionStatus
        {
            Phase = phase,
            ShopBuildingId = shopBuilding.Id,
            ShopName = shopBuilding.Name,
            Blockers = blockers,
        };
    }

    /// <summary>
    /// Lists all MEDIA_HOUSE buildings in a city. Public — no auth required.
    /// Includes channel type (NEWSPAPER, RADIO, TV), owner company name, and effectiveness multiplier.
    /// Players use this to discover where to route their marketing budget.
    /// </summary>
    public async Task<List<CityMediaHouseInfo>> GetCityMediaHouses(
        Guid cityId,
        [Service] AppDbContext db)
    {
        var mediaHouses = await db.Buildings
            .Where(b => b.CityId == cityId && b.Type == Data.Entities.BuildingType.MediaHouse)
            .Include(b => b.Company)
            .AsNoTracking()
            .ToListAsync();

        return mediaHouses.Select(b => new CityMediaHouseInfo
        {
            Id = b.Id,
            Name = b.Name,
            CityId = b.CityId,
            MediaType = b.MediaType,
            OwnerCompanyId = b.CompanyId,
            OwnerCompanyName = b.Company.Name,
            EffectivenessMultiplier = Data.Entities.MediaType.EffectivenessMultiplier(b.MediaType),
            PowerStatus = b.PowerStatus,
            IsUnderConstruction = b.IsUnderConstruction,
        }).ToList();
    }

    /// <summary>
    /// Returns brand awareness and quality metrics for products of a company. Requires authentication.
    /// Consumers: building-detail marketing unit configuration, analytics dashboards.
    /// Delegates to companyBrands — use that query for brand awareness reads.
    /// </summary>
    [Authorize]
    public async Task<List<ResearchBrandState>> GetCompanyMarketingStats(
        Guid companyId,
        [Service] AppDbContext db,
        [Service] IHttpContextAccessor httpContextAccessor)
    {
        var userId = httpContextAccessor.HttpContext!.User.GetRequiredUserId();
        var company = await db.Companies.FirstOrDefaultAsync(c => c.Id == companyId && c.PlayerId == userId);
        if (company is null) return [];

        var brands = await db.Brands.Where(b => b.CompanyId == companyId).ToListAsync();
        var productTypeIds = brands.Where(b => b.ProductTypeId.HasValue).Select(b => b.ProductTypeId!.Value).Distinct().ToList();
        var productTypes = await db.ProductTypes.Where(pt => productTypeIds.Contains(pt.Id)).ToListAsync();

        return brands.Select(b =>
        {
            var pt = b.ProductTypeId.HasValue ? productTypes.FirstOrDefault(p => p.Id == b.ProductTypeId.Value) : null;
            return new ResearchBrandState
            {
                Id = b.Id,
                CompanyId = b.CompanyId,
                Name = b.Name,
                Scope = b.Scope,
                ProductTypeId = b.ProductTypeId,
                ProductName = pt?.Name,
                IndustryCategory = b.IndustryCategory,
                Awareness = b.Awareness,
                Quality = b.Quality,
                MarketingEfficiencyMultiplier = b.MarketingEfficiencyMultiplier,
            };
        }).ToList();
    }
}
