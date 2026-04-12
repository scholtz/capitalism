using System.Globalization;
using Api.Data;
using Api.Data.Entities;
using Api.Security;
using Api.Utilities;
using HotChocolate.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Api.Types;

public sealed partial class Mutation
{
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
