namespace PortfolioApp.Models;

/// <summary>How a symbol entered the monitored set.</summary>
public enum WatchlistSource
{
    /// <summary>Added automatically because it appears as a holding in an imported file.</summary>
    Import = 0,

    /// <summary>Added by the user to watch without owning.</summary>
    Manual = 1
}

/// <summary>
/// The set of symbols the monitoring run fetches quotes for: owned positions plus any manually
/// watched tickers. Held separately from <see cref="Holding"/> so a symbol can be watched
/// without being owned, and so selling out of a position doesn't silently stop monitoring it.
/// </summary>
public sealed class WatchlistSymbol
{
    public int Id { get; set; }

    public string Symbol { get; set; } = string.Empty;

    public WatchlistSource Source { get; set; }

    /// <summary>Lets a symbol be suspended without losing its history or rules.</summary>
    public bool IsEnabled { get; set; } = true;

    public DateTimeOffset AddedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
