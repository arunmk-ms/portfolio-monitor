using System.Globalization;
using System.Text.Json;
using PortfolioApp.Models;
using PortfolioApp.Services;
using Xunit;

namespace PortfolioApp.Tests;

public class AlphaVantageClientTests
{
    private static readonly DateTimeOffset RetrievedAt = new(2026, 9, 2, 20, 0, 0, TimeSpan.Zero);

    private static GlobalQuoteResponse Deserialize(string json) =>
        JsonSerializer.Deserialize<GlobalQuoteResponse>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

    /// <summary>Verbatim GLOBAL_QUOTE response shape returned by Alpha Vantage.</summary>
    private const string MsftQuoteJson = """
    {
        "Global Quote": {
            "01. symbol": "MSFT",
            "02. open": "499.8500",
            "03. high": "500.2700",
            "04. low": "493.8100",
            "05. price": "496.8200",
            "06. volume": "15336074",
            "07. latest trading day": "2026-09-02",
            "08. previous close": "501.0200",
            "09. change": "-4.2000",
            "10. change percent": "-0.8383%"
        }
    }
    """;

    [Fact]
    public void MapToQuote_ParsesAllFields()
    {
        var quote = AlphaVantageClient.MapToQuote("MSFT", Deserialize(MsftQuoteJson), RetrievedAt);

        Assert.Equal("MSFT", quote.Symbol);
        Assert.Equal(496.82m, quote.Price);
        Assert.Equal(499.85m, quote.Open);
        Assert.Equal(500.27m, quote.High);
        Assert.Equal(493.81m, quote.Low);
        Assert.Equal(501.02m, quote.PreviousClose);
        Assert.Equal(-4.20m, quote.Change);
        Assert.Equal(15_336_074L, quote.Volume);
        Assert.Equal(new DateOnly(2026, 9, 2), quote.LatestTradingDay);
        Assert.Equal(RetrievedAt, quote.RetrievedAt);
    }

    [Fact]
    public void MapToQuote_StripsPercentSuffixFromChangePercent()
    {
        var quote = AlphaVantageClient.MapToQuote("MSFT", Deserialize(MsftQuoteJson), RetrievedAt);

        Assert.Equal(-0.8383m, quote.ChangePercent);
    }

    [Fact]
    public void MapToQuote_UsesInvariantCulture_WhenAmbientCultureUsesCommaDecimalSeparator()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            // de-DE treats '.' as a thousands separator; parsing must stay invariant.
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");

            var quote = AlphaVantageClient.MapToQuote("MSFT", Deserialize(MsftQuoteJson), RetrievedAt);

            Assert.Equal(496.82m, quote.Price);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void MapToQuote_NormalizesSymbolToUpperCase()
    {
        var json = MsftQuoteJson.Replace("\"01. symbol\": \"MSFT\"", "\"01. symbol\": \"nvda\"");

        var quote = AlphaVantageClient.MapToQuote("nvda", Deserialize(json), RetrievedAt);

        Assert.Equal("NVDA", quote.Symbol);
    }

    [Fact]
    public void MapToQuote_Throws_WhenProviderReturnsErrorMessage()
    {
        const string json = """{ "Error Message": "Invalid API call." }""";

        var ex = Assert.Throws<AlphaVantageException>(
            () => AlphaVantageClient.MapToQuote("BOGUS", Deserialize(json), RetrievedAt));

        Assert.Equal("BOGUS", ex.Symbol);
        Assert.False(ex.IsThrottled);
        Assert.Contains("Invalid API call.", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MapToQuote_ThrowsThrottled_WhenProviderReturnsNote()
    {
        const string json = """{ "Note": "Our standard API call frequency is 5 calls per minute." }""";

        var ex = Assert.Throws<AlphaVantageException>(
            () => AlphaVantageClient.MapToQuote("NVDA", Deserialize(json), RetrievedAt));

        Assert.True(ex.IsThrottled);
    }

    [Fact]
    public void MapToQuote_ThrowsThrottled_WhenProviderReturnsInformation()
    {
        const string json = """{ "Information": "The **demo** API key is for demo purposes only." }""";

        var ex = Assert.Throws<AlphaVantageException>(
            () => AlphaVantageClient.MapToQuote("NVDA", Deserialize(json), RetrievedAt));

        Assert.True(ex.IsThrottled);
    }

    [Fact]
    public void MapToQuote_Throws_WhenQuoteIsEmpty()
    {
        const string json = """{ "Global Quote": {} }""";

        var ex = Assert.Throws<AlphaVantageException>(
            () => AlphaVantageClient.MapToQuote("ZZZZ", Deserialize(json), RetrievedAt));

        Assert.False(ex.IsThrottled);
        Assert.Contains("ZZZZ", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MapToQuote_LeavesTradingDayNull_WhenDateIsMissingOrMalformed()
    {
        var json = MsftQuoteJson.Replace("\"2026-09-02\"", "\"not-a-date\"");

        var quote = AlphaVantageClient.MapToQuote("MSFT", Deserialize(json), RetrievedAt);

        Assert.Null(quote.LatestTradingDay);
    }
}
