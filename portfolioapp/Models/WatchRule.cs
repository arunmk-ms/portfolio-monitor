namespace PortfolioApp.Models;

/// <summary>What a triggered rule is recommending. These are prompts for review, never orders.</summary>
public enum SignalDirection
{
    Informational = 0,
    BuyCandidate = 1,
    SellCandidate = 2,
    Rebalance = 3
}

/// <summary>
/// The condition a rule tests. A closed set of typed conditions rather than a free-form
/// expression language: rules must be deterministic and explainable after the fact, and a
/// stored expression string is neither easy to validate nor safe to evaluate.
/// </summary>
public enum RuleType
{
    /// <summary>Price fell below <see cref="WatchRule.Threshold"/> (an absolute price).</summary>
    PriceBelow = 0,

    /// <summary>Price rose above <see cref="WatchRule.Threshold"/> (an absolute price).</summary>
    PriceAbove = 1,

    /// <summary>Price is at least <see cref="WatchRule.Threshold"/> percent below average cost.</summary>
    PercentDropFromAverageCost = 2,

    /// <summary>Price is at least <see cref="WatchRule.Threshold"/> percent above average cost.</summary>
    PercentGainFromAverageCost = 3,

    /// <summary>RSI fell below <see cref="WatchRule.Threshold"/> (oversold).</summary>
    RsiBelow = 4,

    /// <summary>RSI rose above <see cref="WatchRule.Threshold"/> (overbought).</summary>
    RsiAbove = 5,

    /// <summary>Price is below its <see cref="WatchRule.Period"/>-period moving average.</summary>
    PriceBelowMovingAverage = 6,

    /// <summary>Price is above its <see cref="WatchRule.Period"/>-period moving average.</summary>
    PriceAboveMovingAverage = 7,

    /// <summary>Position exceeds <see cref="WatchRule.Threshold"/> percent of portfolio value.</summary>
    AllocationAbove = 8,

    /// <summary>Position is under <see cref="WatchRule.Threshold"/> percent of portfolio value.</summary>
    AllocationBelow = 9
}

/// <summary>
/// A versioned, deterministic rule — the `WatchRule` entity from DESIGN.md.
/// </summary>
/// <remarks>
/// <see cref="Version"/> is copied onto every signal the rule produces, so a historical alert
/// remains explainable even after the rule's threshold is later changed.
/// </remarks>
public sealed class WatchRule
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>The ticker this rule applies to, or <c>null</c> to apply it to every monitored symbol.</summary>
    public string? Symbol { get; set; }

    public RuleType RuleType { get; set; }

    public decimal Threshold { get; set; }

    /// <summary>Lookback length for indicator rules (RSI, moving average); ignored otherwise.</summary>
    public int? Period { get; set; }

    public SignalDirection Direction { get; set; }

    public bool IsEnabled { get; set; } = true;

    /// <summary>Incremented whenever the rule's evaluated behaviour changes.</summary>
    public int Version { get; set; } = 1;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>True when the rule needs indicator history rather than just the latest quote.</summary>
    public bool RequiresHistory =>
        RuleType is RuleType.RsiBelow
            or RuleType.RsiAbove
            or RuleType.PriceBelowMovingAverage
            or RuleType.PriceAboveMovingAverage;
}
