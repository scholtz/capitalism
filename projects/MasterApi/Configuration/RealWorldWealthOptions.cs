namespace MasterApi.Configuration;

public sealed class RealWorldWealthOptions
{
    public const string SectionName = "RealWorldWealth";

    public List<RealWorldWealthBenchmarkOptions> Benchmarks { get; set; } = [];
}

public sealed class RealWorldWealthBenchmarkOptions
{
    public string Name { get; set; } = string.Empty;

    public decimal EstimatedUsdWealth { get; set; }

    public string Source { get; set; } = string.Empty;

    public DateTime SnapshotDateUtc { get; set; }
}
