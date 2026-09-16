using System.Text.RegularExpressions;
using PortfolioApp.Models;

namespace PortfolioApp.Services;

/// <summary>
/// Decides whether a row from a positions export is a quotable security.
/// </summary>
/// <remarks>
/// This gate matters for cost and correctness: sending a cash sweep like "FCASH**" or a
/// "Pending Activity" row to the quote provider burns a request from a small daily budget and
/// returns an error every time.
/// </remarks>
public static partial class SymbolClassifier
{
    /// <summary>A plain exchange ticker: 1-10 characters, letters with optional dot or dash.</summary>
    [GeneratedRegex(@"^[A-Z][A-Z.\-]{0,9}$", RegexOptions.CultureInvariant)]
    private static partial Regex TickerPattern();

    /// <summary>Non-security rows brokers include in position exports.</summary>
    private static readonly string[] NonSecurityMarkers =
    [
        "PENDING ACTIVITY",
        "TOTAL",
        "CASH"
    ];

    public static PositionType Classify(string? symbol, string? description)
    {
        var ticker = (symbol ?? string.Empty).Trim().ToUpperInvariant();

        if (ticker.Length == 0)
        {
            return PositionType.Other;
        }

        // Fidelity marks money-market and cash sweep positions with a trailing "**".
        if (ticker.EndsWith("**", StringComparison.Ordinal))
        {
            return PositionType.Cash;
        }

        if (NonSecurityMarkers.Any(marker => ticker.Contains(marker, StringComparison.Ordinal)))
        {
            return PositionType.Cash;
        }

        var text = (description ?? string.Empty).ToUpperInvariant();
        if (text.Contains("HELD IN FCASH", StringComparison.Ordinal)
            || text.Contains("PENDING ACTIVITY", StringComparison.Ordinal))
        {
            return PositionType.Cash;
        }

        return TickerPattern().IsMatch(ticker) ? PositionType.Equity : PositionType.Other;
    }

    public static bool IsMonitorable(PositionType type) => type == PositionType.Equity;

    /// <summary>Normalises a ticker for storage and provider lookups.</summary>
    public static string Normalize(string? symbol) =>
        (symbol ?? string.Empty).Trim().ToUpperInvariant();
}
