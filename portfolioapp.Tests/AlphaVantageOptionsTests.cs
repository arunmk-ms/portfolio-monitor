using PortfolioApp.Configuration;
using Xunit;

namespace PortfolioApp.Tests;

public class AlphaVantageOptionsTests
{
    [Fact]
    public void GetSymbols_DefaultsToMsftAndNvda()
    {
        var options = new AlphaVantageOptions();

        Assert.Equal(new[] { "MSFT", "NVDA" }, options.GetSymbols());
    }

    [Fact]
    public void GetSymbols_TrimsUpperCasesAndDeduplicates()
    {
        var options = new AlphaVantageOptions { Symbols = " msft , NVDA,, msft ," };

        Assert.Equal(new[] { "MSFT", "NVDA" }, options.GetSymbols());
    }

    [Fact]
    public void GetSymbols_ReturnsEmpty_WhenOnlySeparatorsAreConfigured()
    {
        var options = new AlphaVantageOptions { Symbols = " , , " };

        Assert.Empty(options.GetSymbols());
    }
}
