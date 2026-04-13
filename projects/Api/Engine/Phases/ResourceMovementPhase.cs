using Api.Data.Entities;

namespace Api.Engine.Phases;

/// <summary>
/// Moves resources/products along active unit links within each building.
/// Processing order: bottom-right to top-left within each building so downstream
/// units drain first and upstream units refill them later in the same tick.
/// Each inventory row can move at most once per tick; quantities received during
/// the phase are not eligible to move again until the next tick.
/// </summary>
public sealed class ResourceMovementPhase : ITickPhase
{
    public string Name => "ResourceMovement";
    public int Order => 400;

    public Task ProcessAsync(TickContext context)
    {
        foreach (var (buildingId, units) in context.UnitsByBuilding)
        {
            var movableQuantities = BuildMovableQuantitiesSnapshot(context, units);

            // Sort downstream units first for deterministic end-to-start flow.
            var sorted = units
                .OrderByDescending(u => u.GridY)
                .ThenByDescending(u => u.GridX)
                .ToList();

            foreach (var unit in sorted)
            {
                PushFromUnit(context, buildingId, unit, movableQuantities);
            }
        }

        return Task.CompletedTask;
    }

    private static Dictionary<Guid, decimal> BuildMovableQuantitiesSnapshot(
        TickContext context,
        IEnumerable<BuildingUnit> units)
    {
        var movableQuantities = new Dictionary<Guid, decimal>();

        foreach (var unit in units)
        {
            if (!context.InventoryByUnit.TryGetValue(unit.Id, out var inventories))
                continue;

            foreach (var inventory in inventories)
            {
                if (inventory.Quantity > 0m)
                {
                    movableQuantities[inventory.Id] = inventory.Quantity;
                }
            }
        }

        return movableQuantities;
    }

    private static void PushFromUnit(
        TickContext context,
        Guid buildingId,
        BuildingUnit source,
        Dictionary<Guid, decimal> movableQuantities)
    {
        // A unit under upgrade is offline: it neither pushes nor receives inventory.
        if (context.UnitsUnderUpgrade.Contains(source.Id)) return;

        if (!context.InventoryByUnit.TryGetValue(source.Id, out var inventories))
            return;

        var neighbors = context.GetOutgoingLinkedUnits(source);
        if (neighbors.Count == 0) return;

        foreach (var inv in inventories)
        {
            if (!movableQuantities.TryGetValue(inv.Id, out var movableQuantity) || movableQuantity <= 0m)
                continue;

            // For manufacturing units, only push the configured output product, keep inputs in place.
            if (source.UnitType == UnitType.Manufacturing && inv.ProductTypeId != source.ProductTypeId)
                continue;

            // Distribute evenly among neighbors with free space.
            // Also exclude neighbors that are currently under upgrade — they don't receive items.
            var eligibleNeighbors = neighbors
                .Where(n => !context.UnitsUnderUpgrade.Contains(n.Id))
                .Where(n => context.GetUnitReceivingSpace(n, inv.ResourceTypeId, inv.ProductTypeId) > 0m)
                .ToList();
            if (eligibleNeighbors.Count == 0) continue;

            var remainingMovable = movableQuantity;
            var sharePerNeighbor = remainingMovable / eligibleNeighbors.Count;

            foreach (var neighbor in eligibleNeighbors)
            {
                if (remainingMovable <= 0m)
                    break;

                var space = context.GetUnitReceivingSpace(neighbor, inv.ResourceTypeId, inv.ProductTypeId);
                var transfer = Math.Min(Math.Min(sharePerNeighbor, remainingMovable), space);
                transfer = Math.Max(0m, Math.Floor(transfer * 10000m) / 10000m);
                if (transfer <= 0m) continue;

                var moved = context.WithdrawInventory(inv, transfer);
                if (moved.Quantity <= 0m) continue;

                remainingMovable -= moved.Quantity;
                movableQuantities[inv.Id] = remainingMovable;

                context.RecordUnitResourceHistory(
                    buildingId,
                    source.Id,
                    inv.ResourceTypeId,
                    inv.ProductTypeId,
                    outflowQuantity: moved.Quantity);

                var targetInv = context.GetOrCreateUnitInventory(
                    buildingId, neighbor.Id, inv.ResourceTypeId, inv.ProductTypeId);
                context.AddInventory(targetInv, moved.Quantity, moved.SourcingCostTotal, inv.Quality);
                context.RecordUnitResourceHistory(
                    buildingId,
                    neighbor.Id,
                    inv.ResourceTypeId,
                    inv.ProductTypeId,
                    inflowQuantity: moved.Quantity);

                if (inv.Quantity <= 0m) break;
            }
        }
    }
}
