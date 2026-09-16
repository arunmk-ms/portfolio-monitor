namespace PortfolioApp.Services;

/// <summary>
/// Raised when Alpha Vantage rejects a request or returns no usable quote.
/// </summary>
public sealed class AlphaVantageException : Exception
{
    public AlphaVantageException(string symbol, string message, bool isThrottled = false)
        : base(message)
    {
        Symbol = symbol;
        IsThrottled = isThrottled;
    }

    public string Symbol { get; }

    /// <summary>True when the provider rejected the call because a rate or plan limit was hit.</summary>
    public bool IsThrottled { get; }
}
