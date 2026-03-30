namespace Api.Data.Entities;

/// <summary>
/// Represents the power supply status of a building as determined by the
/// city-level power distribution tick phase.
/// </summary>
public static class PowerStatus
{
    /// <summary>
    /// The building has sufficient power and operates at full capacity.
    /// </summary>
    public const string Powered = "POWERED";

    /// <summary>
    /// The building has only partial power and operates at reduced capacity
    /// (see <see cref="Engine.GameConstants.ConstrainedEfficiencyFactor"/>).
    /// </summary>
    public const string Constrained = "CONSTRAINED";

    /// <summary>
    /// The building has no power and is completely non-operational this tick.
    /// </summary>
    public const string Offline = "OFFLINE";
}
