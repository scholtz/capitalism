using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Api.Data;
using Api.Data.Entities;
using Api.Engine;
using Api.Security;
using Api.Utilities;
using HotChocolate.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Api.Types;

/// <summary>
/// GraphQL mutation type for the Capitalism V game.
/// Handles authentication, admin, company management, onboarding, building configuration, and market actions.
/// Split across multiple partial files, one per domain:
/// <list type="bullet">
/// <item><see cref="Mutation"/> (this file) — shared constants, helpers, and result payloads</item>
/// <item><c>Mutation.Auth.cs</c> — auth and admin impersonation</item>
/// <item><c>Mutation.Admin.cs</c> — game-admin role, chat invisibility, and news mutations</item>
/// <item><c>Mutation.Company.cs</c> — company creation, settings, building placement, context switching, merging</item>
/// <item><c>Mutation.Onboarding.cs</c> — staged onboarding mutations</item>
/// <item><c>Mutation.BuildingConfiguration.cs</c> — queued building layout changes and validation helpers</item>
/// <item><c>Mutation.UnitUpgrade.cs</c> — scheduled building unit upgrades</item>
/// <item><c>Mutation.RealEstate.cs</c> — real-estate pricing, lot purchase, and first-sale milestone operations</item>
/// <item><c>Mutation.PublicSales.cs</c> — public sales pricing and storage flushing</item>
/// <item><c>Mutation.Chat.cs</c> — in-game chat</item>
/// <item><c>Mutation.Lending.cs</c> — bank loan publishing, updating, and accepting</item>
/// <item><c>Mutation.StockExchange.cs</c> — share buying/selling and trading account helpers</item>
/// </list>
/// </summary>
public sealed partial class Mutation
{
    private const decimal StarterFounderContribution = 200_000m;
    private const decimal DefaultDividendPayoutRatio = 0.2m;
    private const decimal DefaultCompanyShareCount = 10_000m;

    private static readonly IReadOnlyDictionary<string, string> StarterOnboardingProductByIndustry = new Dictionary<string, string>
    {
        [Industry.Furniture] = "wooden-chair",
        [Industry.FoodProcessing] = "bread",
        [Industry.Healthcare] = "basic-medicine"
    };


    // ── Onboarding & Shared Private Helpers ──────────────────────────────────────

    private static StarterIpoSelection ResolveStarterIpoSelection(decimal? raiseTarget)
    {
        var normalizedRaiseTarget = raiseTarget ?? 400_000m;
        return normalizedRaiseTarget switch
        {
            400_000m => new StarterIpoSelection(400_000m, 0.5m),
            600_000m => new StarterIpoSelection(600_000m, 0.3333m),
            800_000m => new StarterIpoSelection(800_000m, 0.25m),
            _ => throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("Supported IPO raise targets are 400000, 600000, or 800000.")
                    .SetCode("INVALID_IPO_RAISE_TARGET")
                    .Build())
        };
    }

    private static async Task<long> GetCurrentTickAsync(AppDbContext db)
    {
        return await db.GameStates
            .Select(gameState => (long?)gameState.CurrentTick)
            .FirstOrDefaultAsync() ?? 0L;
    }

    private static void AddCompanyLedgerEntry(
        AppDbContext db,
        Company company,
        string category,
        string description,
        decimal amount,
        long currentTick)
    {
        db.LedgerEntries.Add(new LedgerEntry
        {
            Id = Guid.NewGuid(),
            CompanyId = company.Id,
            Category = category,
            Description = description,
            Amount = amount,
            RecordedAtTick = currentTick,
            RecordedAtUtc = DateTime.UtcNow,
        });
    }

    private static ImpersonationAccountContext ResolveImpersonationAccountContext(
        Player targetPlayer,
        StartAdminImpersonationInput input)
    {
        if (string.Equals(input.AccountType, AccountContextType.Person, StringComparison.OrdinalIgnoreCase))
        {
            return new ImpersonationAccountContext(AccountContextType.Person, null, null);
        }

        if (!string.Equals(input.AccountType, AccountContextType.Company, StringComparison.OrdinalIgnoreCase) || input.CompanyId is null)
        {
            throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("A valid person or company account must be selected for impersonation.")
                    .SetCode("INVALID_IMPERSONATION_ACCOUNT")
                    .Build());
        }

        var targetCompany = targetPlayer.Companies.FirstOrDefault(company => company.Id == input.CompanyId.Value)
            ?? throw new GraphQLException(
                ErrorBuilder.New()
                    .SetMessage("The selected company does not belong to the target player.")
                    .SetCode("IMPERSONATION_COMPANY_NOT_FOUND")
                    .Build());

        return new ImpersonationAccountContext(AccountContextType.Company, targetCompany.Id, targetCompany.Name);
    }
}
