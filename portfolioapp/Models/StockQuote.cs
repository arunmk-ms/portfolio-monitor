namespace PortfolioApp.Models;

/// <summary>
/// A normalized point-in-time quote. Alpha Vantage returns delayed data on the free tier,
/// so <see cref="LatestTradingDay"/> must be surfaced anywhere a price is displayed.
/// </summary>
public sealed record StockQuote(
    string Symbol,
    decimal Price,
    decimal Open,
    decimal High,
    decimal Low,
    decimal PreviousClose,
    decimal Change,
    decimal ChangePercent,
    long Volume,
    DateOnly? LatestTradingDay,
    DateTimeOffset RetrievedAt);
