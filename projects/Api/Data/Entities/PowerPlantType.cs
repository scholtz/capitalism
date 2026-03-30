namespace Api.Data.Entities;

/// <summary>
/// Defines the fuel/generation types for POWER_PLANT buildings.
/// </summary>
public static class PowerPlantType
{
    /// <summary>Coal-fired thermal power plant. High capacity, fuel-dependent.</summary>
    public const string Coal = "COAL";

    /// <summary>Natural-gas combined-cycle power plant. Moderate capacity, fuel-dependent.</summary>
    public const string Gas = "GAS";

    /// <summary>Solar photovoltaic farm. Renewable, no fuel cost.</summary>
    public const string Solar = "SOLAR";

    /// <summary>Wind turbine farm. Renewable, no fuel cost.</summary>
    public const string Wind = "WIND";

    /// <summary>Nuclear fission plant. Very high capacity, complex operation.</summary>
    public const string Nuclear = "NUCLEAR";

    /// <summary>All valid power plant types.</summary>
    public static readonly string[] All = [Coal, Gas, Solar, Wind, Nuclear];
}
