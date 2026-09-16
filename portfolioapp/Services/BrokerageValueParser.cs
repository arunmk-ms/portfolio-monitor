using System.Globalization;

namespace PortfolioApp.Services;

/// <summary>
/// Parses the money, percent, and date formats brokers emit in CSV exports. These are
/// display-formatted rather than machine-readable, so naive <c>decimal.Parse</c> fails on them.
/// </summary>
public static class BrokerageValueParser
{
    /// <summary>
    /// Parses values such as "$6.48 ", "($0.09)", "$3,567.50 ", and "($39,573.87)".
    /// Accounting-style parentheses denote a negative number.
    /// </summary>
    public static decimal? ParseMoney(string? value)
    {
        var text = (value ?? string.Empty).Trim();
        if (text.Length == 0 || text == "--")
        {
            return null;
        }

        var isNegative = false;

        if (text.StartsWith('(') && text.EndsWith(')'))
        {
            isNegative = true;
            text = text[1..^1];
        }

        text = text.Replace("$", string.Empty, StringComparison.Ordinal)
            .Replace(",", string.Empty, StringComparison.Ordinal)
            .Trim();

        // A leading sign can appear either outside or inside the parentheses.
        if (text.StartsWith('-'))
        {
            isNegative = !isNegative;
            text = text[1..].Trim();
        }
        else if (text.StartsWith('+'))
        {
            text = text[1..].Trim();
        }

        if (text.Length == 0 ||
            !decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
        {
            return null;
        }

        return isNegative ? -parsed : parsed;
    }

    /// <summary>Parses values such as "1.67%", "-1.37%", and "(1.37%)".</summary>
    public static decimal? ParsePercent(string? value) =>
        ParseMoney((value ?? string.Empty).Replace("%", string.Empty, StringComparison.Ordinal));

    /// <summary>Parses a plain share count, which may be fractional or blank for cash rows.</summary>
    public static decimal? ParseQuantity(string? value) => ParseMoney(value);

    /// <summary>
    /// Parses the footer stamp, for example "Date downloaded Sep-09-2026 11:41 p.m ET".
    /// The time is exchange-local (ET) and is returned as an absolute instant.
    /// </summary>
    public static DateTimeOffset? ParseDownloadedAt(string? value, TimeZoneInfo? easternTimeZone = null)
    {
        var text = (value ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            return null;
        }

        const string prefix = "Date downloaded";
        var start = text.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
        {
            return null;
        }

        text = text[(start + prefix.Length)..].Trim();

        // Normalise the broker's "11:41 p.m ET" into something DateTime can parse.
        text = text.Replace("p.m.", "PM", StringComparison.OrdinalIgnoreCase)
            .Replace("a.m.", "AM", StringComparison.OrdinalIgnoreCase)
            .Replace("p.m", "PM", StringComparison.OrdinalIgnoreCase)
            .Replace("a.m", "AM", StringComparison.OrdinalIgnoreCase)
            .Replace(" ET", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Trim();

        string[] formats =
        [
            "MMM-dd-yyyy h:mm tt",
            "MMM-dd-yyyy hh:mm tt",
            "MMM-dd-yyyy H:mm",
            "MMM-dd-yyyy",
            "MM/dd/yyyy h:mm tt",
            "MM/dd/yyyy"
        ];

        if (!DateTime.TryParseExact(text, formats, CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces, out var local))
        {
            return null;
        }

        var timeZone = easternTimeZone ?? ResolveEastern();
        return new DateTimeOffset(local, timeZone.GetUtcOffset(local));
    }

    private static TimeZoneInfo ResolveEastern()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
        }
    }
}
