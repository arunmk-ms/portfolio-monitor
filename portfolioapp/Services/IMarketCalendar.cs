namespace PortfolioApp.Services;

/// <summary>Why the market is not currently trading.</summary>
public enum MarketClosedReason
{
    None = 0,
    Weekend,
    Holiday,
    BeforeOpen,
    AfterClose
}

/// <param name="IsOpen">True when the exchange is in its trading session.</param>
/// <param name="Reason">Why it is closed; <see cref="MarketClosedReason.None"/> when open.</param>
/// <param name="LocalTime">The evaluated instant in exchange-local time, for logging.</param>
public readonly record struct MarketStatus(bool IsOpen, MarketClosedReason Reason, DateTime LocalTime);

/// <summary>Answers whether the exchange is trading at a given instant.</summary>
public interface IMarketCalendar
{
    MarketStatus GetStatus(DateTimeOffset instant);
}
