using Api.Data;
using Api.Data.Entities;
using Api.Utilities;
using Microsoft.EntityFrameworkCore;

namespace Api.Engine;

public sealed record EndgameTargetPerson(string Name, decimal EstimatedUsdWealth);

public sealed record EndgamePlayerWealth(
    Guid PlayerId,
    string DisplayName,
    decimal TotalWealth);

public sealed record EndgameOutcome(
    EndgamePlayerWealth Winner,
    EndgameTargetPerson SurpassedTarget,
    IReadOnlyList<EndgamePlayerWealth> FinalRanking);

public static class EndgameService
{
    private static readonly IReadOnlyList<EndgameTargetPerson> TargetRichList =
    [
        new("Elon Musk", 430_000_000_000m),
        new("Jeff Bezos", 240_000_000_000m),
        new("Mark Zuckerberg", 220_000_000_000m),
        new("Larry Ellison", 190_000_000_000m),
        new("Bernard Arnault", 170_000_000_000m),
    ];

    public static IReadOnlyList<EndgameTargetPerson> GetTargetRichList() => TargetRichList;

    public static decimal GetWinThresholdWealth() => TargetRichList[^1].EstimatedUsdWealth;

    public static EndgameTargetPerson? GetHighestSurpassedTarget(decimal wealth)
    {
        return TargetRichList
            .Where(target => wealth >= target.EstimatedUsdWealth)
            .OrderByDescending(target => target.EstimatedUsdWealth)
            .FirstOrDefault();
    }

    public static async Task<List<EndgamePlayerWealth>> ComputePlayerWealthRankingAsync(AppDbContext db, CancellationToken ct = default)
    {
        var players = await db.Players
            .AsNoTracking()
            .Where(player => player.Role != PlayerRole.Admin)
            .ToListAsync(ct);

        var companies = await db.Companies.AsNoTracking().ToListAsync(ct);
        var buildings = await db.Buildings.AsNoTracking().ToListAsync(ct);
        var lots = await db.BuildingLots
            .AsNoTracking()
            .Where(lot => lot.OwnerCompanyId.HasValue)
            .ToListAsync(ct);
        var inventories = await db.Inventories
            .AsNoTracking()
            .Include(inventory => inventory.ResourceType)
            .Include(inventory => inventory.ProductType)
            .ToListAsync(ct);
        var shareholdings = await db.Shareholdings.AsNoTracking().ToListAsync(ct);

        var baseEquityByCompany = SharePriceCalculator.ComputeBaseEquityByCompany(companies, buildings, lots, inventories);
        var sharePriceByCompany = SharePriceCalculator.ComputeQuotedSharePriceByCompany(companies, baseEquityByCompany, shareholdings);

        return players
            .Select(player =>
            {
                var sharesValue = shareholdings
                    .Where(holding => holding.OwnerPlayerId == player.Id && holding.ShareCount > 0m)
                    .Sum(holding => decimal.Round(
                        holding.ShareCount * sharePriceByCompany.GetValueOrDefault(holding.CompanyId),
                        4,
                        MidpointRounding.AwayFromZero));

                var totalWealth = decimal.Round(player.PersonalCash + sharesValue, 4, MidpointRounding.AwayFromZero);
                return new EndgamePlayerWealth(
                    player.Id,
                    player.PersonalAccountName ?? player.DisplayName,
                    totalWealth);
            })
            .OrderByDescending(row => row.TotalWealth)
            .ToList();
    }

    public static async Task<EndgameOutcome?> EvaluateWinConditionAsync(AppDbContext db, CancellationToken ct = default)
    {
        var ranking = await ComputePlayerWealthRankingAsync(db, ct);
        if (ranking.Count == 0)
        {
            return null;
        }

        var winner = ranking[0];
        if (winner.TotalWealth < GetWinThresholdWealth())
        {
            return null;
        }

        var surpassedTarget = GetHighestSurpassedTarget(winner.TotalWealth);
        if (surpassedTarget is null)
        {
            return null;
        }

        return new EndgameOutcome(
            winner,
            surpassedTarget,
            ranking);
    }
}
