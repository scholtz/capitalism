using Microsoft.EntityFrameworkCore;

namespace Api.Engine.Phases;

/// <summary>
/// Completes building construction orders whose timers have expired.
/// Buildings created via the city-map purchase flow start as <c>IsUnderConstruction = true</c>
/// with a scheduled <c>ConstructionCompletesAtTick</c>.  This phase clears the flag so the
/// building becomes fully operational for all subsequent tick phases.
/// </summary>
public sealed class ConstructionPhase : ITickPhase
{
    public string Name => "Construction";

    /// <summary>
    /// Runs before all production phases (Order = 5) so that a building which completes
    /// on the current tick can already participate in mining, manufacturing, etc. this tick.
    /// </summary>
    public int Order => 5;

    public async Task ProcessAsync(TickContext context)
    {
        // Find all buildings that are still under construction but whose completion tick
        // has now been reached (or passed, in case a tick was skipped).
        var completedBuildings = await context.Db.Buildings
            .Where(b => b.IsUnderConstruction
                        && b.ConstructionCompletesAtTick.HasValue
                        && b.ConstructionCompletesAtTick.Value <= context.CurrentTick)
            .ToListAsync();

        foreach (var building in completedBuildings)
        {
            building.IsUnderConstruction = false;
            building.ConstructionCompletesAtTick = null;
        }
    }
}
