namespace PortfolioApp.Models;

/// <summary>
/// A persisted, timestamped price observation — the `PriceSnapshot` entity from DESIGN.md.
/// This is the storage shape, kept separate from <see cref="StockQuote"/> (the provider-facing
/// DTO) so the market-data provider can change without forcing a schema migration.
/// </summary>
public sealed class PriceSnapshot
{
    public long Id { get; set; }

    public string Symbol { get; set; } = string.Empty;

    public decimal Price { get; set; }

    public decimal Open { get; set; }

    public decimal High { get; set; }

    public decimal Low { get; set; }

    public decimal PreviousClose { get; set; }

    public decimal Change { get; set; }

    public decimal ChangePercent { get; set; }

    public long Volume { get; set; }

    /// <summary>
    /// The provider's trading day for this quote. Free-tier data is delayed, so this is what
    /// tells you how stale the price actually is — always surface it alongside the price.
    /// </summary>
    public DateOnly? LatestTradingDay { get; set; }

    /// <summary>When the monitoring run retrieved this quote.</summary>
    public DateTimeOffset CapturedAt { get; set; }

    public static PriceSnapshot FromQuote(StockQuote quote) => new()
    {
        Symbol = quote.Symbol,
        Price = quote.Price,
        Open = quote.Open,
        High = quote.High,
        Low = quote.Low,
        PreviousClose = quote.PreviousClose,
        Change = quote.Change,
        ChangePercent = quote.ChangePercent,
        Volume = quote.Volume,
        LatestTradingDay = quote.LatestTradingDay,
        CapturedAt = quote.RetrievedAt
    };
}
