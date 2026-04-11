namespace Api.Types;

public sealed class AccountContextResult
{
    public string ActiveAccountType { get; set; } = string.Empty;
    public Guid? ActiveCompanyId { get; set; }
    public string ActiveAccountName { get; set; } = string.Empty;
}

public sealed class PersonAccountResult
{
    public Guid PlayerId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public decimal PersonalCash { get; set; }
    public string ActiveAccountType { get; set; } = string.Empty;
    public Guid? ActiveCompanyId { get; set; }
    public List<PortfolioHoldingResult> Shareholdings { get; set; } = [];
    public List<DividendPaymentResult> DividendPayments { get; set; } = [];
    public List<PersonTradeRecordResult> StockTrades { get; set; } = [];
}

public sealed class PortfolioHoldingResult
{
    public Guid CompanyId { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    public decimal ShareCount { get; set; }
    public decimal OwnershipRatio { get; set; }
    public decimal SharePrice { get; set; }
    public decimal MarketValue { get; set; }
}

public sealed class DividendPaymentResult
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    public decimal ShareCount { get; set; }
    public decimal AmountPerShare { get; set; }
    public decimal TotalAmount { get; set; }
    public int GameYear { get; set; }
    public long RecordedAtTick { get; set; }
    public DateTime RecordedAtUtc { get; set; }
    public string Description { get; set; } = string.Empty;
}

public sealed class StockExchangeListingResult
{
    public Guid CompanyId { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    public decimal TotalSharesIssued { get; set; }
    public decimal PublicFloatShares { get; set; }
    public decimal SharePrice { get; set; }
    public decimal MarketValue { get; set; }
    public decimal BidPrice { get; set; }
    public decimal AskPrice { get; set; }
    public decimal DividendPayoutRatio { get; set; }
    public decimal PlayerOwnedShares { get; set; }
    public decimal ControlledCompanyOwnedShares { get; set; }
    public decimal CombinedControlledOwnershipRatio { get; set; }
    public bool CanClaimControl { get; set; }
}

public sealed class StockExchangePriceHistoryPointResult
{
    public Guid CompanyId { get; set; }
    public long Tick { get; set; }
    public decimal Price { get; set; }
    public DateTime RecordedAtUtc { get; set; }
}

public sealed class ShareTradeResult
{
    public Guid CompanyId { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    public string AccountType { get; set; } = string.Empty;
    public Guid? AccountCompanyId { get; set; }
    public string AccountName { get; set; } = string.Empty;
    public decimal ShareCount { get; set; }
    public decimal PricePerShare { get; set; }
    public decimal TotalValue { get; set; }
    public decimal OwnedShareCount { get; set; }
    public decimal PublicFloatShares { get; set; }
    public decimal PersonalCash { get; set; }
    public decimal? CompanyCash { get; set; }
}

public sealed class PersonTradeRecordResult
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    public string Direction { get; set; } = string.Empty;
    public decimal ShareCount { get; set; }
    public decimal PricePerShare { get; set; }
    public decimal TotalValue { get; set; }
    public long RecordedAtTick { get; set; }
    public DateTime RecordedAtUtc { get; set; }
}
