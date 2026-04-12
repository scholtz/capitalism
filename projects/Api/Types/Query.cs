namespace Api.Types;

/// <summary>
/// GraphQL query type for the Capitalism V game.
/// Provides read access to game data including players, cities, resources, products, and buildings.
/// Split across multiple partial files, one per domain:
/// <list type="bullet">
/// <item><see cref="Query"/> (this file) — shared query constants</item>
/// <item><c>Query.Auth.cs</c> — authenticated player profile and personal account queries</item>
/// <item><c>Query.Admin.cs</c> — admin session/dashboard queries and admin summary helpers</item>
/// <item><c>Query.News.cs</c> — shared news feed queries</item>
/// <item><c>Query.StockExchange.cs</c> — stock exchange listings, history, and shareholder queries</item>
/// <item><c>Query.World.cs</c> — world, resource, encyclopedia, and product catalog queries</item>
/// <item><c>Query.Types.cs</c> — shared query DTO/result payload types</item>
/// <item><c>Query.Exchange.cs</c>, <c>Query.Inventory.cs</c>, <c>Query.Operations.cs</c>, <c>Query.BuildingActivity.cs</c>, <c>Query.Ledger.cs</c>, and <c>Query.Analytics*.cs</c> — building, exchange, analytics, and ledger queries</item>
/// <item><c>Query.Chat.cs</c> — in-game chat feed</item>
/// <item><c>Query.Rankings.cs</c> — player/company rankings and game state</item>
/// <item><c>Query.Lending.cs</c> — bank loan offers and player loans</item>
/// </list>
/// </summary>
public sealed partial class Query
{
    private const int MaxRecentStockPriceHistoryPoints = 12;
    private const int DefaultChatMessageLimit = 50;
    private const int MaxChatMessageLimit = 100;
}
