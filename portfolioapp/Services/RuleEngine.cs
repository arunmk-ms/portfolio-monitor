using PortfolioApp.Models;

namespace PortfolioApp.Services;

/// <summary>Everything a rule needs to evaluate one symbol at one instant.</summary>
/// <param name="Symbol">Ticker being evaluated.</param>
/// <param name="Price">Latest observed price.</param>
/// <param name="AverageCost">Average cost basis, when the symbol is actually held.</param>
/// <param name="MarketValue">Quantity x price, when the symbol is held.</param>
/// <param name="AllocationPercent">Share of total portfolio value.</param>
/// <param name="PriceHistory">Closing prices oldest to newest, including <paramref name="Price"/>.</param>
public sealed record SymbolEvaluationContext(
    string Symbol,
    decimal Price,
    decimal? AverageCost,
    decimal? MarketValue,
    decimal? AllocationPercent,
    IReadOnlyList<decimal> PriceHistory);

/// <summary>Why a rule produced no verdict.</summary>
public enum RuleOutcome
{
    /// <summary>The condition holds.</summary>
    Triggered = 0,

    /// <summary>The condition does not hold, so any open signal for this rule can be resolved.</summary>
    NotTriggered = 1,

    /// <summary>
    /// The rule could not be evaluated — missing cost basis, or not enough price history for the
    /// indicator. Distinct from <see cref="NotTriggered"/> because absence of data is not
    /// evidence that a condition cleared, and must never resolve an open signal.
    /// </summary>
    Skipped = 2
}

/// <param name="Score">Normalised breach magnitude, 0-1, for ranking competing signals.</param>
/// <param name="Facts">Structured values behind the verdict, persisted onto the signal.</param>
public sealed record RuleEvaluation(
    WatchRule Rule,
    RuleOutcome Outcome,
    decimal Score,
    IReadOnlyDictionary<string, object?> Facts,
    string? SkipReason = null);

public interface IRuleEngine
{
    IReadOnlyList<RuleEvaluation> Evaluate(SymbolEvaluationContext context, IEnumerable<WatchRule> rules);
}

/// <summary>
/// Evaluates watch rules deterministically: no clock, no I/O, no randomness, so the same inputs
/// always produce the same verdict and a stored signal can be reproduced later from its facts.
/// </summary>
public sealed class RuleEngine : IRuleEngine
{
    public IReadOnlyList<RuleEvaluation> Evaluate(SymbolEvaluationContext context, IEnumerable<WatchRule> rules)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(rules);

        var results = new List<RuleEvaluation>();

        foreach (var rule in rules)
        {
            if (!rule.IsEnabled)
            {
                continue;
            }

            // A null Symbol means the rule applies to every monitored symbol.
            if (rule.Symbol is not null &&
                !string.Equals(rule.Symbol, context.Symbol, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            results.Add(EvaluateRule(context, rule));
        }

        return results;
    }

    private static RuleEvaluation EvaluateRule(SymbolEvaluationContext context, WatchRule rule) =>
        rule.RuleType switch
        {
            RuleType.PriceBelow => Compare(rule, context.Price, rule.Threshold, below: true, "price"),
            RuleType.PriceAbove => Compare(rule, context.Price, rule.Threshold, below: false, "price"),
            RuleType.PercentDropFromAverageCost => EvaluateCostBasisMove(context, rule, drop: true),
            RuleType.PercentGainFromAverageCost => EvaluateCostBasisMove(context, rule, drop: false),
            RuleType.RsiBelow => EvaluateRsi(context, rule, below: true),
            RuleType.RsiAbove => EvaluateRsi(context, rule, below: false),
            RuleType.PriceBelowMovingAverage => EvaluateMovingAverage(context, rule, below: true),
            RuleType.PriceAboveMovingAverage => EvaluateMovingAverage(context, rule, below: false),
            RuleType.AllocationAbove => EvaluateAllocation(context, rule, below: false),
            RuleType.AllocationBelow => EvaluateAllocation(context, rule, below: true),
            _ => Skip(rule, $"Unsupported rule type '{rule.RuleType}'.")
        };

    private static RuleEvaluation EvaluateCostBasisMove(SymbolEvaluationContext context, WatchRule rule, bool drop)
    {
        if (context.AverageCost is null or 0m)
        {
            return Skip(rule, "No average cost basis for this symbol; it is watched but not held.");
        }

        var changePercent = IndicatorCalculator.PercentChange(context.AverageCost, context.Price);
        if (changePercent is null)
        {
            return Skip(rule, "Could not compute change from average cost.");
        }

        // A "drop" rule uses a positive threshold expressed as a magnitude, e.g. 10 => -10%.
        var magnitude = drop ? -changePercent.Value : changePercent.Value;
        var triggered = magnitude >= rule.Threshold;

        var facts = Facts(context, rule,
            ("averageCost", context.AverageCost),
            ("changeFromCostPercent", Round(changePercent.Value)),
            ("thresholdPercent", rule.Threshold));

        return Result(rule, triggered, NormalisedScore(magnitude, rule.Threshold), facts);
    }

    private static RuleEvaluation EvaluateRsi(SymbolEvaluationContext context, WatchRule rule, bool below)
    {
        var period = rule.Period ?? 14;
        var rsi = IndicatorCalculator.RelativeStrengthIndex(context.PriceHistory, period);

        if (rsi is null)
        {
            return Skip(rule,
                $"Need {period + 1} price points for a {period}-period RSI; have {context.PriceHistory.Count}.");
        }

        var triggered = below ? rsi.Value < rule.Threshold : rsi.Value > rule.Threshold;
        var magnitude = below ? rule.Threshold - rsi.Value : rsi.Value - rule.Threshold;

        var facts = Facts(context, rule,
            ("rsi", Round(rsi.Value)),
            ("rsiPeriod", period),
            ("threshold", rule.Threshold));

        // RSI is bounded 0-100, so scale the breach against the remaining range.
        var range = below ? Math.Max(rule.Threshold, 1m) : Math.Max(100m - rule.Threshold, 1m);
        return Result(rule, triggered, Clamp(magnitude / range), facts);
    }

    private static RuleEvaluation EvaluateMovingAverage(SymbolEvaluationContext context, WatchRule rule, bool below)
    {
        var period = rule.Period ?? 20;
        var movingAverage = IndicatorCalculator.SimpleMovingAverage(context.PriceHistory, period);

        if (movingAverage is null)
        {
            return Skip(rule,
                $"Need {period} price points for a {period}-period moving average; have {context.PriceHistory.Count}.");
        }

        var triggered = below ? context.Price < movingAverage.Value : context.Price > movingAverage.Value;
        var deviation = IndicatorCalculator.PercentChange(movingAverage, context.Price) ?? 0m;

        var facts = Facts(context, rule,
            ("movingAverage", Round(movingAverage.Value)),
            ("movingAveragePeriod", period),
            ("deviationPercent", Round(deviation)));

        return Result(rule, triggered, Clamp(Math.Abs(deviation) / 100m), facts);
    }

    private static RuleEvaluation EvaluateAllocation(SymbolEvaluationContext context, WatchRule rule, bool below)
    {
        if (context.AllocationPercent is null)
        {
            return Skip(rule, "Portfolio allocation is unavailable; the symbol is not held or has no market value.");
        }

        var allocation = context.AllocationPercent.Value;
        var triggered = below ? allocation < rule.Threshold : allocation > rule.Threshold;
        var magnitude = below ? rule.Threshold - allocation : allocation - rule.Threshold;

        var facts = Facts(context, rule,
            ("allocationPercent", Round(allocation)),
            ("thresholdPercent", rule.Threshold),
            ("marketValue", context.MarketValue));

        return Result(rule, triggered, NormalisedScore(magnitude, rule.Threshold), facts);
    }

    private static RuleEvaluation Compare(
        WatchRule rule,
        decimal actual,
        decimal threshold,
        bool below,
        string factName)
    {
        var triggered = below ? actual < threshold : actual > threshold;
        var magnitude = below ? threshold - actual : actual - threshold;

        var facts = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [factName] = actual,
            ["threshold"] = threshold,
            ["rule"] = rule.Name
        };

        return Result(rule, triggered, NormalisedScore(magnitude, threshold), facts);
    }

    private static RuleEvaluation Result(
        WatchRule rule,
        bool triggered,
        decimal score,
        IReadOnlyDictionary<string, object?> facts) =>
        new(rule, triggered ? RuleOutcome.Triggered : RuleOutcome.NotTriggered, triggered ? score : 0m, facts);

    private static RuleEvaluation Skip(WatchRule rule, string reason) =>
        new(rule, RuleOutcome.Skipped, 0m,
            new Dictionary<string, object?>(StringComparer.Ordinal) { ["skipped"] = reason }, reason);

    private static Dictionary<string, object?> Facts(
        SymbolEvaluationContext context,
        WatchRule rule,
        params (string Key, object? Value)[] extra)
    {
        var facts = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["symbol"] = context.Symbol,
            ["price"] = context.Price,
            ["rule"] = rule.Name
        };

        foreach (var (key, value) in extra)
        {
            facts[key] = value;
        }

        return facts;
    }

    /// <summary>Breach magnitude relative to the threshold, clamped to 0-1.</summary>
    private static decimal NormalisedScore(decimal magnitude, decimal threshold)
    {
        if (magnitude <= 0m)
        {
            return 0m;
        }

        var denominator = Math.Abs(threshold);
        return denominator == 0m ? 1m : Clamp(magnitude / denominator);
    }

    private static decimal Clamp(decimal value) => Math.Clamp(value, 0m, 1m);

    private static decimal Round(decimal value) => Math.Round(value, 4, MidpointRounding.AwayFromZero);
}
