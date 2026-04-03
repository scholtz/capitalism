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
        // Perform the state transition in a single database UPDATE rather than loading
        // all completing buildings into memory.
        await context.Db.Buildings
            .Where(b => b.IsUnderConstruction
                        && b.ConstructionCompletesAtTick.HasValue
                        && b.ConstructionCompletesAtTick.Value <= context.CurrentTick)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(b => b.IsUnderConstruction, false)
                .SetProperty(b => b.ConstructionCompletesAtTick, (long?)null));
    }
}
