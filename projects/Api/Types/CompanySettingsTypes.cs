namespace Api.Types;

public sealed class CompanySettingsResult
{
    public Guid CompanyId { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    public decimal Cash { get; set; }
    public long FoundedAtTick { get; set; }
    public decimal AdministrationOverheadRate { get; set; }
    public decimal AssetValue { get; set; }
    public List<CompanyCitySalarySettingResult> CitySalarySettings { get; set; } = [];
}

public sealed class CompanyCitySalarySettingResult
{
    public Guid CityId { get; set; }
    public string CityName { get; set; } = string.Empty;
    public decimal BaseSalaryPerManhour { get; set; }
    public decimal SalaryMultiplier { get; set; }
    public decimal EffectiveSalaryPerManhour { get; set; }
}