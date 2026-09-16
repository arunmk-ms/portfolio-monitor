using PortfolioApp.Models;
using PortfolioApp.Services;
using Xunit;

namespace PortfolioApp.Tests;

public class BrokerageValueParserTests
{
    [Theory]
    [InlineData("$6.48 ", 6.48)]
    [InlineData("$78.36 ", 78.36)]
    [InlineData("($0.09)", -0.09)]
    [InlineData("$3,567.50 ", 3567.50)]
    [InlineData("($39,573.87)", -39573.87)]
    [InlineData("$600.75 ", 600.75)]
    [InlineData("$43,141.37 ", 43141.37)]
    [InlineData("-12.5", -12.5)]
    [InlineData("1234567.89", 1234567.89)]
    public void ParseMoney_HandlesBrokerFormatting(string input, double expected)
    {
        Assert.Equal((decimal)expected, BrokerageValueParser.ParseMoney(input));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("--")]
    [InlineData("n/a")]
    public void ParseMoney_ReturnsNullForBlankOrUnparsableValues(string? input)
    {
        Assert.Null(BrokerageValueParser.ParseMoney(input));
    }

    [Theory]
    [InlineData("1.67%", 1.67)]
    [InlineData("-1.37%", -1.37)]
    [InlineData("-91.74%", -91.74)]
    [InlineData("(1.37%)", -1.37)]
    public void ParsePercent_StripsSuffixAndHonoursSign(string input, double expected)
    {
        Assert.Equal((decimal)expected, BrokerageValueParser.ParsePercent(input));
    }

    [Fact]
    public void ParseQuantity_SupportsFractionalShares()
    {
        Assert.Equal(550.541m, BrokerageValueParser.ParseQuantity("550.541"));
    }

    [Fact]
    public void ParseDownloadedAt_ReadsFidelityFooterStamp()
    {
        var parsed = BrokerageValueParser.ParseDownloadedAt("Date downloaded Sep-09-2026 11:41 p.m ET");

        Assert.NotNull(parsed);
        Assert.Equal(new DateTime(2026, 9, 9, 23, 41, 0), parsed!.Value.DateTime);
        // September is EDT, i.e. UTC-4.
        Assert.Equal(TimeSpan.FromHours(-4), parsed.Value.Offset);
    }

    [Fact]
    public void ParseDownloadedAt_AppliesWinterOffset()
    {
        var parsed = BrokerageValueParser.ParseDownloadedAt("Date downloaded Jan-15-2026 09:00 a.m ET");

        Assert.NotNull(parsed);
        Assert.Equal(TimeSpan.FromHours(-5), parsed!.Value.Offset);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("Brokerage services are provided by Fidelity Brokerage Services LLC")]
    [InlineData("Date downloaded not-a-real-date")]
    public void ParseDownloadedAt_ReturnsNullForNonFooterRows(string? input)
    {
        Assert.Null(BrokerageValueParser.ParseDownloadedAt(input));
    }
}

public class SymbolClassifierTests
{
    [Theory]
    [InlineData("XYZ", "XYZ INC")]
    [InlineData("ABC", "ABC INC")]
    [InlineData("MSFT", "MICROSOFT CORP")]
    [InlineData("BRK.B", "BERKSHIRE HATHAWAY")]
    public void Classify_TreatsPlainTickersAsEquities(string symbol, string description)
    {
        Assert.Equal(PositionType.Equity, SymbolClassifier.Classify(symbol, description));
        Assert.True(SymbolClassifier.IsMonitorable(SymbolClassifier.Classify(symbol, description)));
    }

    [Theory]
    // Fidelity marks money-market/cash sweep positions with a trailing "**".
    [InlineData("FCASH**", "HELD IN FCASH")]
    [InlineData("SPAXX**", "FIDELITY GOVERNMENT MONEY MARKET")]
    public void Classify_TreatsMoneyMarketRowsAsCash(string symbol, string description)
    {
        var type = SymbolClassifier.Classify(symbol, description);

        Assert.Equal(PositionType.Cash, type);
        Assert.False(SymbolClassifier.IsMonitorable(type));
    }

    [Fact]
    public void Classify_ExcludesPendingActivityRows()
    {
        var type = SymbolClassifier.Classify("Pending Activity", null);

        Assert.False(SymbolClassifier.IsMonitorable(type));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Classify_TreatsEmptySymbolAsOther(string? symbol)
    {
        Assert.Equal(PositionType.Other, SymbolClassifier.Classify(symbol, null));
    }

    [Fact]
    public void Normalize_UpperCasesAndTrims()
    {
        Assert.Equal("MSFT", SymbolClassifier.Normalize("  msft "));
    }
}
