using System.Globalization;
using Api.Data;
using Api.Data.Entities;
using Api.Security;
using Api.Utilities;
using HotChocolate.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Api.Types;

/// <summary>
/// Building operations mutations: configuration, sales, rent, lot purchase,
/// public sales pricing, storage flushing, and unit upgrades.
/// </summary>
public sealed partial class Mutation
{
    /// <summary>Queues a building configuration update that becomes active after the required ticks have passed.</summary>
    [Authorize]
    public async Task<BuildingConfigurationPlan> StoreBuildingConfiguration(
        StoreBuildingConfigurationInput input,
        [Service] AppDbContext db,
        [Service] IHttpContextAccessor httpContextAccessor)
    {
        var userId = httpContextAccessor.HttpContext!.User.GetRequiredUserId();

        var gameState = await db.GameStates.FirstOrDefaultAsync()
            ?? throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Game state is not initialized.")
                    .SetCode("GAME_STATE_NOT_FOUND")
                    .Build());

        await BuildingConfigurationService.ApplyDuePlansAsync(db, gameState.CurrentTick);

        var building = await db.Buildings
            .Include(candidate => candidate.Company)
            .Include(candidate => candidate.Units)
            .Include(candidate => candidate.PendingConfiguration)
            .ThenInclude(plan => plan!.Units)
            .Include(candidate => candidate.PendingConfiguration)
            .ThenInclude(plan => plan!.Removals)
            .AsSplitQuery()
            .FirstOrDefaultAsync(candidate => candidate.Id == input.BuildingId);

        if (building is null || building.Company.PlayerId != userId)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Building not found or you don't own it.")
                    .SetCode("BUILDING_NOT_FOUND")
                    .Build());
        }

        if (!BuildingConfigurationService.GetAllowedUnitTypes(building.Type).Any())
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("This building type does not support editable unit configurations.")
                    .SetCode("BUILDING_CONFIGURATION_NOT_SUPPORTED")
                    .Build());
        }

        var subscriptionEndsAtUtc = await db.Players
            .Where(player => player.Id == userId)
            .Select(player => player.ProSubscriptionEndsAtUtc)
            .FirstOrDefaultAsync();
        var hasActiveProSubscription = ProductAccessService.HasActiveProSubscription(subscriptionEndsAtUtc, DateTime.UtcNow);
        await EnsureSubmittedProductsAreAccessibleAsync(db, building, input.Units, hasActiveProSubscription);
        await ValidateMediaHouseReferencesAsync(db, building, input.Units);
        await ValidateProductTopologyAsync(db, building.Id, input.Units);

        var plan = await BuildingConfigurationService.StoreConfigurationAsync(db, building, input.Units, gameState.CurrentTick);
        await db.SaveChangesAsync();

        return await db.BuildingConfigurationPlans
            .Include(candidate => candidate.Units)
            .Include(candidate => candidate.Removals)
            .FirstAsync(candidate => candidate.Id == plan.Id);
    }

    /// <summary>Cancels a queued building configuration plan, reverting in-progress unit additions using roadmap-aligned rollback timing (10% of the original wait).</summary>
    [Authorize]
    public async Task<BuildingConfigurationPlan> CancelBuildingConfiguration(
        CancelBuildingConfigurationInput input,
        [Service] AppDbContext db,
        [Service] IHttpContextAccessor httpContextAccessor)
    {
        var userId = httpContextAccessor.HttpContext!.User.GetRequiredUserId();

        var gameState = await db.GameStates.FirstOrDefaultAsync()
            ?? throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Game state is not initialized.")
                    .SetCode("GAME_STATE_NOT_FOUND")
                    .Build());

        await BuildingConfigurationService.ApplyDuePlansAsync(db, gameState.CurrentTick);

        var building = await db.Buildings
            .Include(candidate => candidate.Company)
            .Include(candidate => candidate.Units)
            .Include(candidate => candidate.PendingConfiguration)
            .ThenInclude(plan => plan!.Units)
            .Include(candidate => candidate.PendingConfiguration)
            .ThenInclude(plan => plan!.Removals)
            .AsSplitQuery()
            .FirstOrDefaultAsync(candidate => candidate.Id == input.BuildingId);

        if (building is null || building.Company.PlayerId != userId)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Building not found or you don't own it.")
                    .SetCode("BUILDING_NOT_FOUND")
                    .Build());
        }

        if (building.PendingConfiguration is null)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("This building does not have a pending configuration plan to cancel.")
                    .SetCode("NO_PENDING_CONFIGURATION")
                    .Build());
        }

        // Cancel by submitting the current active layout, which causes the service to schedule
        // rollback of any in-progress unit additions with 10% of the original wait time.
        var activeUnitInputs = building.Units
            .Select(unit => new BuildingConfigurationUnitInput
            {
                UnitType = unit.UnitType,
                GridX = unit.GridX,
                GridY = unit.GridY,
                LinkUp = unit.LinkUp,
                LinkDown = unit.LinkDown,
                LinkLeft = unit.LinkLeft,
                LinkRight = unit.LinkRight,
                LinkUpLeft = unit.LinkUpLeft,
                LinkUpRight = unit.LinkUpRight,
                LinkDownLeft = unit.LinkDownLeft,
                LinkDownRight = unit.LinkDownRight,
                ResourceTypeId = unit.ResourceTypeId,
                ProductTypeId = unit.ProductTypeId,
                MinPrice = unit.MinPrice,
                MaxPrice = unit.MaxPrice,
                PurchaseSource = unit.PurchaseSource,
                SaleVisibility = unit.SaleVisibility,
                Budget = unit.Budget,
                MediaHouseBuildingId = unit.MediaHouseBuildingId,
                MinQuality = unit.MinQuality,
                BrandScope = unit.BrandScope,
                VendorLockCompanyId = unit.VendorLockCompanyId,
                LockedCityId = unit.LockedCityId,
            })
            .ToList();

        var plan = await BuildingConfigurationService.StoreConfigurationAsync(db, building, activeUnitInputs, gameState.CurrentTick);
        await db.SaveChangesAsync();

        return await db.BuildingConfigurationPlans
            .Include(candidate => candidate.Units)
            .Include(candidate => candidate.Removals)
            .FirstAsync(candidate => candidate.Id == plan.Id);
    }

    /// <summary>Sets or clears the for-sale status and asking price of a building.</summary>
    [Authorize]
    public async Task<Building> SetBuildingForSale(
        SetBuildingForSaleInput input,
        [Service] AppDbContext db,
        [Service] IHttpContextAccessor httpContextAccessor)
    {
        var userId = httpContextAccessor.HttpContext!.User.GetRequiredUserId();

        var building = await db.Buildings
            .Include(b => b.Company)
            .Include(b => b.Units)
            .FirstOrDefaultAsync(b => b.Id == input.BuildingId);

        if (building is null || building.Company.PlayerId != userId)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Building not found or you don't own it.")
                    .SetCode("BUILDING_NOT_FOUND")
                    .Build());
        }

        building.IsForSale = input.IsForSale;
        building.AskingPrice = input.IsForSale ? input.AskingPrice : null;

        await db.SaveChangesAsync();
        return building;
    }

    /// <summary>
    /// Schedules a new rent per m² for an apartment or commercial building.
    /// The change is stored as pending and activates after one in-game day (24 ticks).
    /// </summary>
    [Authorize]
    public async Task<Building> SetRentPerSqm(
        SetRentPerSqmInput input,
        [Service] AppDbContext db,
        [Service] IHttpContextAccessor httpContextAccessor)
    {
        var userId = httpContextAccessor.HttpContext!.User.GetRequiredUserId();

        var building = await db.Buildings
            .Include(b => b.Company)
            .FirstOrDefaultAsync(b => b.Id == input.BuildingId);

        if (building is null || building.Company.PlayerId != userId)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Building not found or you don't own it.")
                    .SetCode("BUILDING_NOT_FOUND")
                    .Build());
        }

        if (building.Type != BuildingType.Apartment && building.Type != BuildingType.Commercial)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Only apartment and commercial buildings support rent pricing.")
                    .SetCode("INVALID_BUILDING_TYPE")
                    .Build());
        }

        if (input.RentPerSqm < 0m)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Rent per m² must be a non-negative value.")
                    .SetCode("INVALID_RENT")
                    .Build());
        }

        var gameState = await db.GameStates.FirstOrDefaultAsync()
            ?? throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Game state not found.")
                    .SetCode("GAME_STATE_NOT_FOUND")
                    .Build());

        // Schedule the rent change – takes effect after one in-game day (24 ticks).
        building.PendingPricePerSqm = input.RentPerSqm;
        building.PendingPriceActivationTick = gameState.CurrentTick + Engine.GameConstants.TicksPerDay;

        await db.SaveChangesAsync();
        return building;
    }

    /// <summary>Purchases a building lot and places a building on it.</summary>
    [Authorize]
    public async Task<PurchaseLotResult> PurchaseLot(
        PurchaseLotInput input,
        [Service] AppDbContext db,
        [Service] IHttpContextAccessor httpContextAccessor)
    {
        var userId = httpContextAccessor.HttpContext!.User.GetRequiredUserId();

        var company = await db.Companies.FirstOrDefaultAsync(
            c => c.Id == input.CompanyId && c.PlayerId == userId);

        if (company is null)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Company not found or you don't own it.")
                    .SetCode("COMPANY_NOT_FOUND")
                    .Build());
        }

        var (lot, building) = await PrepareLotPurchaseAsync(
            db,
            company,
            input.LotId,
            input.BuildingType,
            input.BuildingName,
            Engine.GameConstants.PowerDemandMw(input.BuildingType, 1),
            DateTime.UtcNow,
            powerPlantType: input.PowerPlantType,
            applyConstructionDelay: true);

        // Validate and apply media house channel type.
        if (input.BuildingType == BuildingType.MediaHouse)
        {
            if (string.IsNullOrEmpty(input.MediaType) || !Data.Entities.MediaType.All.Contains(input.MediaType))
            {
                throw new GraphQLException(
                    ErrorBuilder.New()
                        .SetMessage($"A valid mediaType (NEWSPAPER, RADIO, TV) is required for media house buildings. Received: '{input.MediaType}'.")
                        .SetCode("INVALID_MEDIA_TYPE")
                        .Build());
            }
            building.MediaType = input.MediaType;
        }

        try
        {
            var currentTick = await db.GameStates
                .AsNoTracking()
                .Select(state => state.CurrentTick)
                .FirstOrDefaultAsync();
            await LandService.EnsureMinimumAvailableLotsAsync(db, currentTick, [lot.CityId]);
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("This lot has already been purchased.")
                    .SetCode("LOT_ALREADY_OWNED")
                    .Build());
        }

        return new PurchaseLotResult
        {
            Lot = lot,
            Building = building,
            Company = company
        };
    }

    /// <summary>
    /// Marks the first-sale onboarding milestone as completed for the current player.
    /// Validates backend-authoritative conditions: the player must have a sales shop
    /// created during onboarding with at least one configured PUBLIC_SALES unit.
    /// Idempotent once the milestone has been granted.
    /// </summary>
    [Authorize]
    public async Task<Player> CompleteFirstSaleMilestone(
        [Service] AppDbContext db,
        [Service] IHttpContextAccessor httpContextAccessor)
    {
        var userId = httpContextAccessor.HttpContext!.User.GetRequiredUserId();
        var player = await db.Players
            .Include(p => p.Companies)
            .FirstOrDefaultAsync(p => p.Id == userId)
            ?? throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Player not found.")
                    .SetCode("PLAYER_NOT_FOUND")
                    .Build());

        // Idempotent: already completed
        if (player.OnboardingFirstSaleCompletedAtUtc is not null)
        {
            return player;
        }

        if (player.OnboardingShopBuildingId is null)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("No sales shop was found for this onboarding milestone. Please complete the onboarding setup first.")
                    .SetCode("SHOP_NOT_FOUND")
                    .Build());
        }

        // Verify the shop belongs to this player and has a configured public-sales unit
        var shopBuilding = await db.Buildings
            .Include(b => b.Units)
            .FirstOrDefaultAsync(b => b.Id == player.OnboardingShopBuildingId);

        if (shopBuilding is null)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Sales shop building not found.")
                    .SetCode("SHOP_NOT_FOUND")
                    .Build());
        }

        // Verify ownership via the company chain
        var ownsShop = await db.Companies
            .AnyAsync(c => c.Id == shopBuilding.CompanyId && c.PlayerId == userId);

        if (!ownsShop)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("You do not own this sales shop.")
                    .SetCode("SHOP_NOT_FOUND")
                    .Build());
        }

        // Check backend-authoritative condition: shop must have a PUBLIC_SALES unit with a price set
        var hasSalesUnit = shopBuilding.Units.Any(u =>
            string.Equals(u.UnitType, UnitType.PublicSales, StringComparison.Ordinal)
            && u.MinPrice > 0);

        if (!hasSalesUnit)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Your sales shop is not yet configured. Please set up a public sales unit with a selling price and return here to complete the milestone.")
                    .SetCode("SHOP_NOT_CONFIGURED")
                    .Build());
        }

        // Check backend-authoritative condition: a real public sale must have occurred in the simulation
        var hasRealSale = await db.PublicSalesRecords
            .AnyAsync(r => r.BuildingId == shopBuilding.Id && r.QuantitySold > 0m);

        if (!hasRealSale)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Your shop has not made its first real sale yet. Wait for the simulation to process the next tick and try again after your shop has sold at least one item.")
                    .SetCode("FIRST_SALE_NOT_RECORDED")
                    .Build());
        }

        player.OnboardingFirstSaleCompletedAtUtc = DateTime.UtcNow;
        player.OnboardingShopBuildingId = null;
        await db.SaveChangesAsync();

        return player;
    }

    /// <summary>
    /// Instantly updates the minimum sale price on a PUBLIC_SALES building unit.
    /// Unlike StoreBuildingConfiguration, this takes effect immediately (next tick)
    /// without requiring a queued upgrade, because price is just a runtime parameter.
    /// </summary>
    [Authorize]
    public async Task<BuildingUnit> UpdatePublicSalesPrice(
        UpdatePublicSalesPriceInput input,
        [Service] AppDbContext db,
        [Service] IHttpContextAccessor httpContextAccessor)
    {
        var userId = httpContextAccessor.HttpContext!.User.GetRequiredUserId();

        var unit = await db.BuildingUnits
            .Include(u => u.Building)
            .ThenInclude(b => b.Company)
            .FirstOrDefaultAsync(u => u.Id == input.UnitId);

        if (unit is null || unit.Building.Company.PlayerId != userId)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Unit not found or you don't own it.")
                    .SetCode("UNIT_NOT_FOUND")
                    .Build());
        }

        if (unit.UnitType != UnitType.PublicSales)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Only PUBLIC_SALES units support instant price updates.")
                    .SetCode("INVALID_UNIT_TYPE")
                    .Build());
        }

        if (input.NewMinPrice <= 0m)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Minimum sale price must be greater than zero.")
                    .SetCode("INVALID_PRICE")
                    .Build());
        }

        unit.MinPrice = input.NewMinPrice;
        await db.SaveChangesAsync();

        return unit;
    }

    /// <summary>
    /// Discards all inventory stored in a storage-capable building unit.
    /// A ledger entry with category DISCARDED_RESOURCES is recorded for each
    /// distinct item flushed, so the loss is visible in the company ledger.
    /// Returns a summary of what was discarded.
    /// </summary>
    [Authorize]
    public async Task<FlushStorageResult> FlushStorage(
        FlushStorageInput input,
        [Service] AppDbContext db,
        [Service] IHttpContextAccessor httpContextAccessor)
    {
        var userId = httpContextAccessor.HttpContext!.User.GetRequiredUserId();

        var unit = await db.BuildingUnits
            .Include(u => u.Building)
            .ThenInclude(b => b.Company)
            .FirstOrDefaultAsync(u => u.Id == input.BuildingUnitId);

        if (unit is null || unit.Building.Company.PlayerId != userId)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Unit not found or you don't own it.")
                    .SetCode("UNIT_NOT_FOUND")
                    .Build());
        }

        // Only allow flushing units that can physically hold inventory.
        var flushableTypes = new HashSet<string>
        {
            UnitType.Storage,
            UnitType.Mining,
            UnitType.Manufacturing,
        };

        if (!flushableTypes.Contains(unit.UnitType))
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Only STORAGE, MINING and MANUFACTURING units can be flushed.")
                    .SetCode("INVALID_UNIT_TYPE")
                    .Build());
        }

        var currentTick = await db.GameStates
            .AsNoTracking()
            .Select(state => state.CurrentTick)
            .FirstAsync();

        var inventory = await db.Inventories
            .Where(i => i.BuildingUnitId == unit.Id && i.Quantity > 0m)
            .ToListAsync();

        if (inventory.Count == 0)
        {
            return new FlushStorageResult
            {
                DiscardedItemCount = 0,
                TotalDiscardedValue = 0m,
                DiscardedEntries = [],
            };
        }

        var nowUtc = DateTime.UtcNow;
        var discardedEntries = new List<FlushStorageEntry>();

        // Pre-load resource and product names in a single query each to avoid N+1.
        var resourceTypeIds = inventory.Where(i => i.ResourceTypeId.HasValue).Select(i => i.ResourceTypeId!.Value).ToHashSet();
        var productTypeIds = inventory.Where(i => i.ProductTypeId.HasValue).Select(i => i.ProductTypeId!.Value).ToHashSet();

        var resourceNames = resourceTypeIds.Count > 0
            ? await db.ResourceTypes.AsNoTracking()
                .Where(r => resourceTypeIds.Contains(r.Id))
                .ToDictionaryAsync(r => r.Id, r => r.Name)
            : new Dictionary<Guid, string>();

        var productNames = productTypeIds.Count > 0
            ? await db.ProductTypes.AsNoTracking()
                .Where(p => productTypeIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, p => p.Name)
            : new Dictionary<Guid, string>();

        foreach (var item in inventory)
        {
            var itemName = item.ResourceTypeId.HasValue
                ? (resourceNames.TryGetValue(item.ResourceTypeId.Value, out var rn) ? rn : "Resource")
                : item.ProductTypeId.HasValue
                    ? (productNames.TryGetValue(item.ProductTypeId.Value, out var pn) ? pn : "Product")
                    : "Item";

            db.LedgerEntries.Add(new LedgerEntry
            {
                Id = Guid.NewGuid(),
                CompanyId = unit.Building.CompanyId,
                BuildingId = unit.BuildingId,
                BuildingUnitId = unit.Id,
                Category = LedgerCategory.DiscardedResources,
                Description = $"Flushed {item.Quantity:F2} × {itemName} from storage",
                Amount = -item.SourcingCostTotal,
                RecordedAtTick = currentTick,
                RecordedAtUtc = nowUtc,
                ResourceTypeId = item.ResourceTypeId,
                ProductTypeId = item.ProductTypeId,
            });

            discardedEntries.Add(new FlushStorageEntry
            {
                ItemName = itemName,
                Quantity = item.Quantity,
                SourcingCostLost = item.SourcingCostTotal,
                ResourceTypeId = item.ResourceTypeId,
                ProductTypeId = item.ProductTypeId,
            });
        }

        db.Inventories.RemoveRange(inventory);
        await db.SaveChangesAsync();

        return new FlushStorageResult
        {
            DiscardedItemCount = discardedEntries.Count,
            TotalDiscardedValue = discardedEntries.Sum(e => e.SourcingCostLost),
            DiscardedEntries = discardedEntries,
        };
    }

    /// <summary>
    /// Schedules a level upgrade for a building unit.
    /// Deducts the upgrade cost from the owning company's cash immediately
    /// and creates a queued building configuration plan that applies after the required ticks.
    /// </summary>
    [Authorize]
    public async Task<BuildingConfigurationPlan> ScheduleUnitUpgrade(
        ScheduleUnitUpgradeInput input,
        [Service] AppDbContext db,
        [Service] IHttpContextAccessor httpContextAccessor)
    {
        var userId = httpContextAccessor.HttpContext!.User.GetRequiredUserId();

        var gameState = await db.GameStates.FirstOrDefaultAsync()
            ?? throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Game state is not initialized.")
                    .SetCode("GAME_STATE_NOT_FOUND")
                    .Build());

        await BuildingConfigurationService.ApplyDuePlansAsync(db, gameState.CurrentTick);

        var unit = await db.BuildingUnits
            .Include(u => u.Building)
            .ThenInclude(b => b.Company)
            .Include(u => u.Building)
            .ThenInclude(b => b.Units)
            .Include(u => u.Building)
            .ThenInclude(b => b.PendingConfiguration)
            .ThenInclude(plan => plan!.Units)
            .Include(u => u.Building)
            .ThenInclude(b => b.PendingConfiguration)
            .ThenInclude(plan => plan!.Removals)
            .AsSplitQuery()
            .FirstOrDefaultAsync(u => u.Id == input.UnitId);

        if (unit is null || unit.Building.Company.PlayerId != userId)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Unit not found or you don't own it.")
                    .SetCode("UNIT_NOT_FOUND")
                    .Build());
        }

        if (!Engine.GameConstants.IsUpgradableUnitType(unit.UnitType))
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage($"Unit type {unit.UnitType} does not support level upgrades.")
                    .SetCode("UNIT_NOT_UPGRADABLE")
                    .Build());
        }

        if (unit.Level >= Engine.GameConstants.MaxUnitLevel)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage($"This unit is already at maximum level ({Engine.GameConstants.MaxUnitLevel}).")
                    .SetCode("MAX_LEVEL_REACHED")
                    .Build());
        }

        if (unit.Building.PendingConfiguration is not null)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("This building already has a pending configuration change. Wait for it to complete or cancel it before scheduling an upgrade.")
                    .SetCode("PENDING_CONFIGURATION_EXISTS")
                    .Build());
        }

        var upgradeCost = Engine.GameConstants.UnitUpgradeCost(unit.UnitType, unit.Level);
        var company = unit.Building.Company;

        if (company.Cash < upgradeCost)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage($"Insufficient funds. Upgrade costs ${upgradeCost.ToString("N0", CultureInfo.InvariantCulture)} but your company only has ${company.Cash.ToString("N0", CultureInfo.InvariantCulture)}.")
                    .SetCode("INSUFFICIENT_FUNDS")
                    .Build());
        }

        company.Cash -= upgradeCost;

        var upgradeTicks = Engine.GameConstants.UnitUpgradeTicks(unit.Level);
        var planId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        var plan = new BuildingConfigurationPlan
        {
            Id = planId,
            BuildingId = unit.BuildingId,
            SubmittedAtUtc = now,
            SubmittedAtTick = gameState.CurrentTick,
            AppliesAtTick = gameState.CurrentTick + upgradeTicks,
            TotalTicksRequired = upgradeTicks,
        };

        // Snapshot all active units; only the target unit gets a level bump and a timer.
        // DistinctBy is defensive deduplication per coding guidelines; AsSplitQuery() above prevents
        // Cartesian explosion, but we deduplicate by position as a safety net.
        var allActiveUnits = unit.Building.Units.DistinctBy(u => (u.GridX, u.GridY)).ToList();
        foreach (var activeUnit in allActiveUnits)
        {
            bool isTarget = activeUnit.Id == unit.Id;
            plan.Units.Add(new BuildingConfigurationPlanUnit
            {
                Id = Guid.NewGuid(),
                BuildingConfigurationPlanId = planId,
                UnitType = activeUnit.UnitType,
                GridX = activeUnit.GridX,
                GridY = activeUnit.GridY,
                Level = isTarget ? activeUnit.Level + 1 : activeUnit.Level,
                LinkUp = activeUnit.LinkUp,
                LinkDown = activeUnit.LinkDown,
                LinkLeft = activeUnit.LinkLeft,
                LinkRight = activeUnit.LinkRight,
                LinkUpLeft = activeUnit.LinkUpLeft,
                LinkUpRight = activeUnit.LinkUpRight,
                LinkDownLeft = activeUnit.LinkDownLeft,
                LinkDownRight = activeUnit.LinkDownRight,
                StartedAtTick = gameState.CurrentTick,
                AppliesAtTick = isTarget ? gameState.CurrentTick + upgradeTicks : gameState.CurrentTick,
                TicksRequired = isTarget ? upgradeTicks : 0,
                IsChanged = isTarget,
                ResourceTypeId = activeUnit.ResourceTypeId,
                ProductTypeId = activeUnit.ProductTypeId,
                MinPrice = activeUnit.MinPrice,
                MaxPrice = activeUnit.MaxPrice,
                PurchaseSource = activeUnit.PurchaseSource,
                SaleVisibility = activeUnit.SaleVisibility,
                Budget = activeUnit.Budget,
                MediaHouseBuildingId = activeUnit.MediaHouseBuildingId,
                MinQuality = activeUnit.MinQuality,
                BrandScope = activeUnit.BrandScope,
                VendorLockCompanyId = activeUnit.VendorLockCompanyId,
                LockedCityId = activeUnit.LockedCityId,
            });
        }

        db.BuildingConfigurationPlans.Add(plan);

        db.LedgerEntries.Add(new LedgerEntry
        {
            Id = Guid.NewGuid(),
            CompanyId = company.Id,
            BuildingId = unit.BuildingId,
            BuildingUnitId = unit.Id,
            Category = LedgerCategory.UnitUpgrade,
            Description = $"Unit upgrade: {unit.UnitType} Lv{unit.Level}→{unit.Level + 1} ({unit.Building.Name})",
            Amount = -upgradeCost,
            RecordedAtTick = gameState.CurrentTick,
            RecordedAtUtc = now,
        });

        await db.SaveChangesAsync();

        return await db.BuildingConfigurationPlans
            .Include(p => p.Units)
            .Include(p => p.Removals)
            .FirstAsync(p => p.Id == planId);
    }

    // ── Building Validation Helpers ───────────────────────────────────────────────

    private static async Task EnsureSubmittedProductsAreAccessibleAsync(
        AppDbContext db,
        Building building,
        IReadOnlyCollection<BuildingConfigurationUnitInput> submittedUnits,
        bool hasActiveProSubscription)
    {
        var submittedProductIds = submittedUnits
            .Where(unit => unit.ProductTypeId is not null)
            .Select(unit => unit.ProductTypeId!.Value)
            .Distinct()
            .ToList();

        if (submittedProductIds.Count == 0)
        {
            return;
        }

        var productsById = await db.ProductTypes
            .Where(product => submittedProductIds.Contains(product.Id))
            .ToDictionaryAsync(product => product.Id);

        foreach (var unit in submittedUnits.Where(candidate => candidate.ProductTypeId is not null))
        {
            var productId = unit.ProductTypeId!.Value;
            if (!productsById.TryGetValue(productId, out var product))
            {
                throw new GraphQLException(
                    ErrorBuilder.New()
                        .SetMessage("Product not found.")
                        .SetCode("INVALID_PRODUCT")
                        .Build());
            }

            if (!product.IsProOnly
                || hasActiveProSubscription
                || IsRetainingExistingProProduct(building, unit.UnitType, unit.GridX, unit.GridY, productId))
            {
                continue;
            }

            throw ProductAccessService.CreateProAccessException(product.Name);
        }
    }

    private static bool IsRetainingExistingProProduct(Building building, string unitType, int gridX, int gridY, Guid productTypeId)
    {
        return building.Units.Any(unit =>
                   unit.UnitType == unitType
                   && unit.GridX == gridX
                   && unit.GridY == gridY
                   && unit.ProductTypeId == productTypeId)
               || (building.PendingConfiguration?.Units.Any(unit =>
                   unit.UnitType == unitType
                    && unit.GridX == gridX
                    && unit.GridY == gridY
                    && unit.ProductTypeId == productTypeId) ?? false);
    }

    /// <summary>
    /// Validates that products assigned to STORAGE and B2B_SALES units are topologically
    /// reachable within the submitted configuration plan.
    ///
    /// Rules:
    /// <list type="bullet">
    ///   <item>STORAGE: productTypeId must match a MANUFACTURING unit in the submitted plan,
    ///   or be currently present in the building's inventory stock.</item>
    ///   <item>B2B_SALES: productTypeId must match a MANUFACTURING or STORAGE unit in the
    ///   submitted plan.</item>
    /// </list>
    /// </summary>
    private static async Task ValidateProductTopologyAsync(
        AppDbContext db,
        Guid buildingId,
        IReadOnlyCollection<BuildingConfigurationUnitInput> submittedUnits)
    {
        // Gather product IDs configured on MANUFACTURING units in this plan.
        var mfgProductIds = submittedUnits
            .Where(u => u.UnitType == "MANUFACTURING" && u.ProductTypeId.HasValue)
            .Select(u => u.ProductTypeId!.Value)
            .ToHashSet();

        var purchaseProductIds = submittedUnits
            .Where(u => u.UnitType == "PURCHASE" && u.ProductTypeId.HasValue)
            .Select(u => u.ProductTypeId!.Value)
            .ToHashSet();

        // Gather product IDs configured on STORAGE units in this plan.
        var storageProductIds = submittedUnits
            .Where(u => u.UnitType == "STORAGE" && u.ProductTypeId.HasValue)
            .Select(u => u.ProductTypeId!.Value)
            .ToHashSet();

        // Validate STORAGE units.
        var storageUnitsWithProduct = submittedUnits
            .Where(u => u.UnitType == "STORAGE" && u.ProductTypeId.HasValue)
            .ToList();

        if (storageUnitsWithProduct.Count > 0)
        {
            // Allowed products for STORAGE = MFG products in plan + purchase products
            // in plan + current inventory. This lets Sales Shops buffer purchased
            // products before routing them to Public Sales.
            var inventoryProductIds = await db.Inventories
                .Where(i => i.BuildingId == buildingId && i.ProductTypeId.HasValue && i.Quantity > 0)
                .Select(i => i.ProductTypeId!.Value)
                .Distinct()
                .ToHashSetAsync();
            var allowedProductIds = mfgProductIds.Union(purchaseProductIds).Union(inventoryProductIds).ToHashSet();

            foreach (var unit in storageUnitsWithProduct)
            {
                var pid = unit.ProductTypeId!.Value;
                if (!allowedProductIds.Contains(pid))
                {
                    throw new GraphQLException(
                        ErrorBuilder.New()
                            .SetMessage(
                                "A STORAGE unit's product must match a MANUFACTURING or PURCHASE unit in this configuration or be present in the building's current inventory.")
                            .SetCode("STORAGE_PRODUCT_NOT_REACHABLE")
                            .Build());
                }
            }
        }

        // Validate B2B_SALES units.
        var b2bUnitsWithProduct = submittedUnits
            .Where(u => u.UnitType == "B2B_SALES" && u.ProductTypeId.HasValue)
            .ToList();

        if (b2bUnitsWithProduct.Count > 0)
        {
            // Allowed products for B2B_SALES = MFG products + STORAGE products in plan.
            var allowedForB2B = mfgProductIds.Union(storageProductIds).ToHashSet();

            foreach (var unit in b2bUnitsWithProduct)
            {
                var pid = unit.ProductTypeId!.Value;
                if (!allowedForB2B.Contains(pid))
                {
                    throw new GraphQLException(
                        ErrorBuilder.New()
                            .SetMessage(
                                "A B2B_SALES unit's product must match a MANUFACTURING or STORAGE unit in this configuration.")
                            .SetCode("B2B_PRODUCT_NOT_REACHABLE")
                            .Build());
                }
            }
        }
    }

    /// <summary>
    /// Validates that any MediaHouseBuildingId on MARKETING units references an actual
    /// MEDIA_HOUSE building in the same city as the shop being configured.
    /// </summary>
    private static async Task ValidateMediaHouseReferencesAsync(
        AppDbContext db,
        Building building,
        IReadOnlyCollection<BuildingConfigurationUnitInput> submittedUnits)
    {
        var mediaHouseIds = submittedUnits
            .Where(u => u.UnitType == UnitType.Marketing && u.MediaHouseBuildingId.HasValue)
            .Select(u => u.MediaHouseBuildingId!.Value)
            .Distinct()
            .ToList();

        if (mediaHouseIds.Count == 0) return;

        foreach (var mediaHouseId in mediaHouseIds)
        {
            var mediaHouse = await db.Buildings
                .AsNoTracking()
                .FirstOrDefaultAsync(b => b.Id == mediaHouseId);

            if (mediaHouse is null)
            {
                throw new GraphQLException(
                    ErrorBuilder.New()
                        .SetMessage($"Media house building {mediaHouseId} not found.")
                        .SetCode("MEDIA_HOUSE_NOT_FOUND")
                        .Build());
            }

            if (mediaHouse.Type != BuildingType.MediaHouse)
            {
                throw new GraphQLException(
                    ErrorBuilder.New()
                        .SetMessage($"Building {mediaHouseId} is not a media house.")
                        .SetCode("BUILDING_NOT_MEDIA_HOUSE")
                        .Build());
            }

            if (mediaHouse.CityId != building.CityId)
            {
                throw new GraphQLException(
                    ErrorBuilder.New()
                        .SetMessage("The selected media house must be in the same city as the marketing building.")
                        .SetCode("MEDIA_HOUSE_WRONG_CITY")
                        .Build());
            }
        }
    }
}

/// <summary>Summary of a flush-storage operation.</summary>
public sealed class FlushStorageResult
{
    /// <summary>Number of distinct inventory lines discarded.</summary>
    public int DiscardedItemCount { get; set; }

    /// <summary>Total sourcing-cost value of all discarded items.</summary>
    public decimal TotalDiscardedValue { get; set; }

    /// <summary>Per-item breakdown of what was discarded.</summary>
    public List<FlushStorageEntry> DiscardedEntries { get; set; } = [];
}

/// <summary>A single item line in a flush-storage result.</summary>
public sealed class FlushStorageEntry
{
    public string ItemName { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal SourcingCostLost { get; set; }
    public Guid? ResourceTypeId { get; set; }
    public Guid? ProductTypeId { get; set; }
}
