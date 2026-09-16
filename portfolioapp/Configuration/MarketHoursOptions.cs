using System.ComponentModel.DataAnnotations;
using System.Globalization;

namespace PortfolioApp.Configuration;

/// <summary>
/// Trading-session settings. Bound from the "MarketHours" configuration section
/// (application settings use "MarketHours__TimeZone" and so on).
/// </summary>
public sealed class MarketHoursOptions
{
    public const string SectionName = "MarketHours";

    /// <summary>
    /// When false, the monitoring run executes on every timer tick regardless of session state.
    /// Useful for testing; leave enabled in production to avoid burning API quota overnight.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// IANA (or Windows) identifier for the exchange's local time zone. Using a zone id rather
    /// than a fixed UTC offset keeps the session correct across daylight-saving transitions.
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    public string TimeZone { get; set; } = "America/New_York";

    /// <summary>Regular session open, exchange-local (NYSE/Nasdaq: 09:30).</summary>
    public TimeOnly Open { get; set; } = new(9, 30);

    /// <summary>Regular session close, exchange-local (NYSE/Nasdaq: 16:00).</summary>
    public TimeOnly Close { get; set; } = new(16, 0);

    /// <summary>
    /// Comma-separated full-closure dates (yyyy-MM-dd), exchange-local. Exchange holidays move
    /// year to year, so these are configuration rather than code — update them annually.
    /// </summary>
    public string Holidays { get; set; } = string.Join(",", DefaultHolidays);

    /// <summary>
    /// Comma-separated early-close days as "yyyy-MM-dd=HH:mm" (exchange-local). Days listed here
    /// close at the given time instead of <see cref="Close"/>.
    /// </summary>
    public string EarlyCloses { get; set; } = string.Join(",", DefaultEarlyCloses);

    /// <summary>NYSE full closures for 2026-2027. Extend before January 2028.</summary>
    private static readonly string[] DefaultHolidays =
    [
        // 2026
        "2026-01-01", "2026-01-19", "2026-02-16", "2026-04-03", "2026-05-25",
        "2026-06-19", "2026-07-03", "2026-09-07", "2026-11-26", "2026-12-25",
        // 2027
        "2027-01-01", "2027-01-18", "2027-02-15", "2027-03-26", "2027-05-31",
        "2027-06-18", "2027-07-05", "2027-09-06", "2027-11-25", "2027-12-24"
    ];

    /// <summary>NYSE 13:00 ET early closes for 2026-2027.</summary>
    private static readonly string[] DefaultEarlyCloses =
    [
        "2026-11-27=13:00", "2026-12-24=13:00",
        "2027-11-26=13:00"
    ];

    public IReadOnlySet<DateOnly> GetHolidays()
    {
        var holidays = new HashSet<DateOnly>();

        foreach (var entry in Split(Holidays))
        {
            if (DateOnly.TryParseExact(entry, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                holidays.Add(date);
            }
            else
            {
                throw new InvalidOperationException(
                    $"Invalid entry '{entry}' in MarketHours:Holidays. Expected format 'yyyy-MM-dd'.");
            }
        }

        return holidays;
    }

    public IReadOnlyDictionary<DateOnly, TimeOnly> GetEarlyCloses()
    {
        var earlyCloses = new Dictionary<DateOnly, TimeOnly>();

        foreach (var entry in Split(EarlyCloses))
        {
            var parts = entry.Split('=', 2, StringSplitOptions.TrimEntries);

            if (parts.Length == 2
                && DateOnly.TryParseExact(parts[0], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                && TimeOnly.TryParseExact(parts[1], "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
            {
                earlyCloses[date] = time;
            }
            else
            {
                throw new InvalidOperationException(
                    $"Invalid entry '{entry}' in MarketHours:EarlyCloses. Expected format 'yyyy-MM-dd=HH:mm'.");
            }
        }

        return earlyCloses;
    }

    private static IEnumerable<string> Split(string? value) =>
        (value ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
