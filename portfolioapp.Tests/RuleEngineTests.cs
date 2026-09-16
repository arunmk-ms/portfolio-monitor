using PortfolioApp.Models;
using PortfolioApp.Services;
using Xunit;

namespace PortfolioApp.Tests;

public class IndicatorCalculatorTests
{
    [Fact]
    public void SimpleMovingAverage_AveragesTheMostRecentWindowOnly()
    {
        var prices = new[] { 1m, 2m, 3m, 10m, 20m, 30m };

        // Last three only: (10 + 20 + 30) / 3.
        Assert.Equal(20m, IndicatorCalculator.SimpleMovingAverage(prices, 3));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    public void SimpleMovingAverage_ReturnsNullWithoutEnoughHistory(int period)
    {
        var prices = new[] { 1m, 2m, 3m, 4m, 5m, 6m };

        Assert.Null(IndicatorCalculator.SimpleMovingAverage(prices, period));
    }

    [Fact]
    public void RelativeStrengthIndex_ReturnsNullUntilPeriodPlusOnePricesExist()
    {
        var prices = Enumerable.Range(1, 14).Select(i => (decimal)i).ToArray();

        // A 14-period RSI needs 15 prices, because the first value yields no change.
        Assert.Null(IndicatorCalculator.RelativeStrengthIndex(prices, 14));
        Assert.NotNull(IndicatorCalculator.RelativeStrengthIndex(prices.Append(15m).ToArray(), 14));
    }

    [Fact]
    public void RelativeStrengthIndex_ReturnsMaximumForUninterruptedGains()
    {
        var prices = Enumerable.Range(1, 20).Select(i => (decimal)i).ToArray();

        Assert.Equal(100m, IndicatorCalculator.RelativeStrengthIndex(prices, 14));
    }

    [Fact]
    public void RelativeStrengthIndex_ReturnsNeutralForAFlatSeries()
    {
        var prices = Enumerable.Repeat(50m, 20).ToArray();

        // No movement in either direction is neutral, not overbought.
        Assert.Equal(50m, IndicatorCalculator.RelativeStrengthIndex(prices, 14));
    }

    [Fact]
    public void RelativeStrengthIndex_IsLowAfterSustainedDecline()
    {
        var prices = Enumerable.Range(0, 20).Select(i => 100m - i).ToArray();

        var rsi = IndicatorCalculator.RelativeStrengthIndex(prices, 14);

        Assert.NotNull(rsi);
        Assert.Equal(0m, rsi!.Value);
    }

    [Fact]
    public void RelativeStrengthIndex_StaysWithinBounds()
    {
        var prices = new[] { 44m, 44.34m, 44.09m, 44.15m, 43.61m, 44.33m, 44.83m, 45.10m,
            45.42m, 45.84m, 46.08m, 45.89m, 46.03m, 45.61m, 46.28m, 46.28m };

        var rsi = IndicatorCalculator.RelativeStrengthIndex(prices, 14);

        Assert.NotNull(rsi);
        Assert.InRange(rsi!.Value, 0m, 100m);
        // A mostly rising series should read bullish.
        Assert.True(rsi.Value > 50m, $"Expected RSI above 50 for a rising series but got {rsi.Value}.");
    }

    [Theory]
    [InlineData(100, 110, 10)]
    [InlineData(100, 90, -10)]
    [InlineData(50, 50, 0)]
    public void PercentChange_ComputesSignedPercentage(double from, double to, double expected)
    {
        Assert.Equal((decimal)expected, IndicatorCalculator.PercentChange((decimal)from, (decimal)to));
    }

    [Fact]
    public void PercentChange_ReturnsNullWhenBaseIsZeroOrMissing()
    {
        Assert.Null(IndicatorCalculator.PercentChange(0m, 10m));
        Assert.Null(IndicatorCalculator.PercentChange(null, 10m));
        Assert.Null(IndicatorCalculator.PercentChange(10m, null));
    }

    [Fact]
    public void AllocationPercent_ComputesShareOfPortfolio()
    {
        Assert.Equal(25m, IndicatorCalculator.AllocationPercent(250m, 1000m));
    }

    [Fact]
    public void AllocationPercent_ReturnsNullWhenPortfolioHasNoValue()
    {
        Assert.Null(IndicatorCalculator.AllocationPercent(250m, 0m));
        Assert.Null(IndicatorCalculator.AllocationPercent(null, 1000m));
    }
}

public class RuleEngineTests
{
    private static readonly RuleEngine Engine = new();

    private static WatchRule Rule(
        RuleType type,
        decimal threshold,
        string? symbol = "MSFT",
        int? period = null,
        SignalDirection direction = SignalDirection.Informational) =>
        new()
        {
            Id = 1,
            Name = $"{type} {threshold}",
            Symbol = symbol,
            RuleType = type,
            Threshold = threshold,
            Period = period,
            Direction = direction,
            IsEnabled = true,
            Version = 1
        };

    private static SymbolEvaluationContext Context(
        decimal price = 100m,
        decimal? averageCost = null,
        decimal? marketValue = null,
        decimal? allocation = null,
        IReadOnlyList<decimal>? history = null) =>
        new("MSFT", price, averageCost, marketValue, allocation, history ?? new[] { price });

    [Fact]
    public void Evaluate_TriggersWhenPriceFallsBelowThreshold()
    {
        var result = Engine.Evaluate(Context(price: 90m), new[] { Rule(RuleType.PriceBelow, 100m) }).Single();

        Assert.Equal(RuleOutcome.Triggered, result.Outcome);
        Assert.Equal(90m, result.Facts["price"]);
        Assert.Equal(100m, result.Facts["threshold"]);
    }

    [Fact]
    public void Evaluate_DoesNotTriggerWhenPriceIsExactlyAtThreshold()
    {
        // The boundary is exclusive: "below 100" must not fire at exactly 100.
        var result = Engine.Evaluate(Context(price: 100m), new[] { Rule(RuleType.PriceBelow, 100m) }).Single();

        Assert.Equal(RuleOutcome.NotTriggered, result.Outcome);
    }

    [Fact]
    public void Evaluate_ScoresDeeperBreachesHigher()
    {
        var mild = Engine.Evaluate(Context(price: 95m), new[] { Rule(RuleType.PriceBelow, 100m) }).Single();
        var severe = Engine.Evaluate(Context(price: 60m), new[] { Rule(RuleType.PriceBelow, 100m) }).Single();

        Assert.True(severe.Score > mild.Score);
        Assert.InRange(severe.Score, 0m, 1m);
    }

    [Fact]
    public void Evaluate_SkipsRulesForOtherSymbols()
    {
        var results = Engine.Evaluate(Context(), new[] { Rule(RuleType.PriceBelow, 200m, symbol: "NVDA") });

        Assert.Empty(results);
    }

    [Fact]
    public void Evaluate_AppliesPortfolioWideRulesToEverySymbol()
    {
        var results = Engine.Evaluate(Context(price: 50m), new[] { Rule(RuleType.PriceBelow, 100m, symbol: null) });

        Assert.Single(results);
        Assert.Equal(RuleOutcome.Triggered, results[0].Outcome);
    }

    [Fact]
    public void Evaluate_IgnoresDisabledRules()
    {
        var rule = Rule(RuleType.PriceBelow, 200m);
        rule.IsEnabled = false;

        Assert.Empty(Engine.Evaluate(Context(), new[] { rule }));
    }

    [Fact]
    public void Evaluate_SkipsCostBasisRuleWhenSymbolIsNotHeld()
    {
        var result = Engine
            .Evaluate(Context(averageCost: null), new[] { Rule(RuleType.PercentDropFromAverageCost, 10m) })
            .Single();

        // Watched-but-not-held must skip, never report "condition cleared".
        Assert.Equal(RuleOutcome.Skipped, result.Outcome);
        Assert.NotNull(result.SkipReason);
    }

    [Fact]
    public void Evaluate_TriggersOnPercentDropFromAverageCost()
    {
        var result = Engine
            .Evaluate(Context(price: 80m, averageCost: 100m), new[] { Rule(RuleType.PercentDropFromAverageCost, 10m) })
            .Single();

        Assert.Equal(RuleOutcome.Triggered, result.Outcome);
        Assert.Equal(-20m, result.Facts["changeFromCostPercent"]);
    }

    [Fact]
    public void Evaluate_DoesNotTriggerDropRuleOnAGain()
    {
        var result = Engine
            .Evaluate(Context(price: 120m, averageCost: 100m), new[] { Rule(RuleType.PercentDropFromAverageCost, 10m) })
            .Single();

        Assert.Equal(RuleOutcome.NotTriggered, result.Outcome);
    }

    [Fact]
    public void Evaluate_SkipsIndicatorRuleWithoutEnoughHistory()
    {
        var result = Engine
            .Evaluate(Context(history: new[] { 100m, 101m }), new[] { Rule(RuleType.RsiBelow, 30m, period: 14) })
            .Single();

        Assert.Equal(RuleOutcome.Skipped, result.Outcome);
        Assert.Contains("RSI", result.SkipReason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Evaluate_TriggersRsiBelowAfterSustainedDecline()
    {
        var history = Enumerable.Range(0, 20).Select(i => 100m - i).ToArray();

        var result = Engine
            .Evaluate(Context(price: history[^1], history: history), new[] { Rule(RuleType.RsiBelow, 30m, period: 14) })
            .Single();

        Assert.Equal(RuleOutcome.Triggered, result.Outcome);
        Assert.Equal(14, result.Facts["rsiPeriod"]);
    }

    [Fact]
    public void Evaluate_TriggersWhenPriceIsBelowMovingAverage()
    {
        var history = new[] { 100m, 100m, 100m, 100m, 80m };

        var result = Engine
            .Evaluate(Context(price: 80m, history: history), new[] { Rule(RuleType.PriceBelowMovingAverage, 0m, period: 5) })
            .Single();

        Assert.Equal(RuleOutcome.Triggered, result.Outcome);
        Assert.Equal(96m, result.Facts["movingAverage"]);
    }

    [Fact]
    public void Evaluate_TriggersAllocationAboveLimit()
    {
        var result = Engine
            .Evaluate(
                Context(marketValue: 6000m, allocation: 60m),
                new[] { Rule(RuleType.AllocationAbove, 25m, direction: SignalDirection.Rebalance) })
            .Single();

        Assert.Equal(RuleOutcome.Triggered, result.Outcome);
        Assert.Equal(60m, result.Facts["allocationPercent"]);
    }

    [Fact]
    public void Evaluate_SkipsAllocationRuleWhenAllocationIsUnknown()
    {
        var result = Engine
            .Evaluate(Context(allocation: null), new[] { Rule(RuleType.AllocationAbove, 25m) })
            .Single();

        Assert.Equal(RuleOutcome.Skipped, result.Outcome);
    }

    [Fact]
    public void Evaluate_IsDeterministic()
    {
        var context = Context(price: 90m);
        var rules = new[] { Rule(RuleType.PriceBelow, 100m) };

        var first = Engine.Evaluate(context, rules).Single();
        var second = Engine.Evaluate(context, rules).Single();

        Assert.Equal(first.Outcome, second.Outcome);
        Assert.Equal(first.Score, second.Score);
        Assert.Equal(first.Facts["price"], second.Facts["price"]);
    }
}
