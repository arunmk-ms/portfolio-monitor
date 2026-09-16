using Microsoft.Extensions.Options;
using PortfolioApp.Configuration;

namespace PortfolioApp.Services;

/// <summary>
/// US equity trading calendar: weekday sessions in the exchange's local time zone, minus
/// full-closure holidays, with support for early-close days.
/// </summary>
public sealed class UsEquityMarketCalendar : IMarketCalendar
{
    private readonly MarketHoursOptions _options;
    private readonly TimeZoneInfo _timeZone;
    private readonly IReadOnlySet<DateOnly> _holidays;
    private readonly IReadOnlyDictionary<DateOnly, TimeOnly> _earlyCloses;

    public UsEquityMarketCalendar(IOptions<MarketHoursOptions> options)
    {
        _options = options.Value;
        _timeZone = ResolveTimeZone(_options.TimeZone);
        _holidays = _options.GetHolidays();
        _earlyCloses = _options.GetEarlyCloses();
    }

    public MarketStatus GetStatus(DateTimeOffset instant)
    {
        var local = TimeZoneInfo.ConvertTime(instant, _timeZone).DateTime;
        var date = DateOnly.FromDateTime(local);
        var time = TimeOnly.FromDateTime(local);

        if (local.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
        {
            return new MarketStatus(false, MarketClosedReason.Weekend, local);
        }

        if (_holidays.Contains(date))
        {
            return new MarketStatus(false, MarketClosedReason.Holiday, local);
        }

        if (time < _options.Open)
        {
            return new MarketStatus(false, MarketClosedReason.BeforeOpen, local);
        }

        // An early-close day ends the session sooner than the regular close.
        var close = _earlyCloses.TryGetValue(date, out var earlyClose) ? earlyClose : _options.Close;

        return time >= close
            ? new MarketStatus(false, MarketClosedReason.AfterClose, local)
            : new MarketStatus(true, MarketClosedReason.None, local);
    }

    /// <summary>
    /// Accepts either an IANA id (Linux/macOS, and Windows via ICU) or a Windows id, so the same
    /// configuration works whether the app runs on a Windows or Linux Functions plan.
    /// </summary>
    private static TimeZoneInfo ResolveTimeZone(string id)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            if (TimeZoneInfo.TryConvertIanaIdToWindowsId(id, out var windowsId))
            {
                return TimeZoneInfo.FindSystemTimeZoneById(windowsId);
            }

            if (TimeZoneInfo.TryConvertWindowsIdToIanaId(id, out var ianaId))
            {
                return TimeZoneInfo.FindSystemTimeZoneById(ianaId);
            }

            throw new InvalidOperationException(
                $"Unknown time zone '{id}' in MarketHours:TimeZone. Use an IANA id such as 'America/New_York'.", ex);
        }
    }
}
