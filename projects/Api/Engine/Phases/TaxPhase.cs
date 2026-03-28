using Api.Data.Entities;

namespace Api.Engine.Phases;

/// <summary>
/// Applies the global tax rate to all companies on every tax cycle tick.
/// Tax is deducted as a percentage of each company's current cash balance.
/// </summary>
public sealed class TaxPhase : ITickPhase
{
    public string Name => "Tax";
    public int Order => 1000;

    public Task ProcessAsync(TickContext context)
    {
        var gs = context.GameState;
        if (gs.TaxCycleTicks <= 0) return Task.CompletedTask;
        if (gs.CurrentTick % gs.TaxCycleTicks != 0) return Task.CompletedTask;

        var rate = gs.TaxRate / 100m;
        if (rate <= 0m) return Task.CompletedTask;

        foreach (var company in context.CompaniesById.Values)
        {
            if (company.Cash <= 0m) continue;
            var tax = company.Cash * rate;
            company.Cash = Math.Max(0m, company.Cash - tax);

            context.Db.LedgerEntries.Add(new LedgerEntry
            {
                Id = Guid.NewGuid(),
                CompanyId = company.Id,
                Category = LedgerCategory.Tax,
                Description = $"Tax ({gs.TaxRate}% rate)",
                Amount = -tax,
                RecordedAtTick = context.CurrentTick,
                RecordedAtUtc = DateTime.UtcNow,
            });
        }

        return Task.CompletedTask;
    }
}
