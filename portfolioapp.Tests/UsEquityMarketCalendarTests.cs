using Microsoft.Extensions.Options;
using PortfolioApp.Configuration;
using PortfolioApp.Services;
using Xunit;

namespace PortfolioApp.Tests;

public class UsEquityMarketCalendarTests
{
    private static UsEquityMarketCalendar Calendar(MarketHoursOptions? options = null) =>
        new(Options.Create(options ?? new MarketHoursOptions()));

    /// <summary>Builds an instant from an exchange-local (America/New_York) wall-clock time.</summary>
    private static DateTimeOffset Eastern(int year, int month, int day, int hour, int minute)
    {
        var tz = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        var local = new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, tz.GetUtcOffset(local));
    }

    [Theory]
    // Wednesday 2026-09-09 is a regular session.
    [InlineData(9, 29, false)]  // one minute before the open
    [InlineData(9, 30, true)]   // the opening bell is in-session
    [InlineData(12, 0, true)]
    [InlineData(15, 59, true)]
    [InlineData(16, 0, false)]  // the close is exclusive
    [InlineData(20, 0, false)]
    public void GetStatus_AppliesRegularSessionBoundaries(int hour, int minute, bool expectedOpen)
    {
        var status = Calendar().GetStatus(Eastern(2026, 9, 9, hour, minute));

        Assert.Equal(expectedOpen, status.IsOpen);
    }

    [Theory]
    [InlineData(2026, 9, 12)] // Saturday
    [InlineData(2026, 9, 13)] // Sunday
    public void GetStatus_ClosedOnWeekends(int year, int month, int day)
    {
        var status = Calendar().GetStatus(Eastern(year, month, day, 12, 0));

        Assert.False(status.IsOpen);
        Assert.Equal(MarketClosedReason.Weekend, status.Reason);
    }

    [Theory]
    [InlineData(2026, 1, 1)]    // New Year's Day
    [InlineData(2026, 4, 3)]    // Good Friday
    [InlineData(2026, 7, 3)]    // Independence Day (observed)
    [InlineData(2026, 11, 26)]  // Thanksgiving
    [InlineData(2026, 12, 25)]  // Christmas
    [InlineData(2027, 1, 1)]
    public void GetStatus_ClosedOnHolidays(int year, int month, int day)
    {
        var status = Calendar().GetStatus(Eastern(year, month, day, 12, 0));

        Assert.False(status.IsOpen);
        Assert.Equal(MarketClosedReason.Holiday, status.Reason);
    }

    [Theory]
    [InlineData(12, 59, true)]   // still trading before the 13:00 early close
    [InlineData(13, 0, false)]   // early close reached
    [InlineData(15, 0, false)]   // would be open on a regular day
    public void GetStatus_HonoursEarlyCloseDays(int hour, int minute, bool expectedOpen)
    {
        // 2026-11-27 is the day after Thanksgiving: a 13:00 ET close.
        var status = Calendar().GetStatus(Eastern(2026, 11, 27, hour, minute));

        Assert.Equal(expectedOpen, status.IsOpen);
    }

    [Fact]
    public void GetStatus_ReportsBeforeOpenAndAfterCloseDistinctly()
    {
        Assert.Equal(MarketClosedReason.BeforeOpen, Calendar().GetStatus(Eastern(2026, 9, 9, 7, 0)).Reason);
        Assert.Equal(MarketClosedReason.AfterClose, Calendar().GetStatus(Eastern(2026, 9, 9, 18, 0)).Reason);
    }

    [Fact]
    public void GetStatus_TracksDaylightSavingTransitions()
    {
        // 14:00 UTC is 10:00 EDT (in session) in July, but 09:00 EST (pre-open) in January.
        // A fixed UTC offset would get one of these wrong.
        var summer = Calendar().GetStatus(new DateTimeOffset(2026, 7, 8, 14, 0, 0, TimeSpan.Zero));
        var winter = Calendar().GetStatus(new DateTimeOffset(2026, 1, 8, 14, 0, 0, TimeSpan.Zero));

        Assert.True(summer.IsOpen);
        Assert.False(winter.IsOpen);
        Assert.Equal(MarketClosedReason.BeforeOpen, winter.Reason);
    }

    [Fact]
    public void GetStatus_AcceptsWindowsTimeZoneIdentifier()
    {
        var calendar = Calendar(new MarketHoursOptions { TimeZone = "Eastern Standard Time" });

        Assert.True(calendar.GetStatus(Eastern(2026, 9, 9, 12, 0)).IsOpen);
    }

    [Fact]
    public void GetStatus_UsesConfiguredSessionWindow()
    {
        var calendar = Calendar(new MarketHoursOptions
        {
            Open = new TimeOnly(10, 0),
            Close = new TimeOnly(14, 0)
        });

        Assert.False(calendar.GetStatus(Eastern(2026, 9, 9, 9, 45)).IsOpen);
        Assert.True(calendar.GetStatus(Eastern(2026, 9, 9, 10, 0)).IsOpen);
        Assert.False(calendar.GetStatus(Eastern(2026, 9, 9, 14, 0)).IsOpen);
    }

    [Fact]
    public void Constructor_ThrowsOnUnknownTimeZone()
    {
        var options = new MarketHoursOptions { TimeZone = "Mars/Olympus_Mons" };

        var ex = Assert.Throws<InvalidOperationException>(() => Calendar(options));
        Assert.Contains("Mars/Olympus_Mons", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GetHolidays_ThrowsOnMalformedEntry()
    {
        var options = new MarketHoursOptions { Holidays = "2026-01-01,not-a-date" };

        Assert.Throws<InvalidOperationException>(() => options.GetHolidays());
    }

    [Fact]
    public void GetEarlyCloses_ThrowsOnMalformedEntry()
    {
        var options = new MarketHoursOptions { EarlyCloses = "2026-11-27=noon" };

        Assert.Throws<InvalidOperationException>(() => options.GetEarlyCloses());
    }

    [Fact]
    public void GetEarlyCloses_ParsesConfiguredDays()
    {
        var options = new MarketHoursOptions { EarlyCloses = "2026-11-27=13:00" };

        var earlyCloses = options.GetEarlyCloses();

        Assert.Equal(new TimeOnly(13, 0), earlyCloses[new DateOnly(2026, 11, 27)]);
    }
}
