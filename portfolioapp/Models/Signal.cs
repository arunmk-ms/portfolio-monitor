namespace PortfolioApp.Models;

/// <summary>Lifecycle of a signal.</summary>
public enum SignalStatus
{
    /// <summary>The condition is currently true. Suppresses duplicate signals for the same rule.</summary>
    Open = 0,

    /// <summary>The user has seen it.</summary>
    Acknowledged = 1,

    /// <summary>The user dismissed it.</summary>
    Ignored = 2,

    /// <summary>The condition stopped being true, so the rule is armed again.</summary>
    Resolved = 3
}

/// <summary>
/// A rule firing against a symbol — the `Signal` entity from DESIGN.md.
/// </summary>
public sealed class Signal
{
    public int Id { get; set; }

    public string Symbol { get; set; } = string.Empty;

    public int WatchRuleId { get; set; }

    public WatchRule? WatchRule { get; set; }

    /// <summary>
    /// The rule version at evaluation time. Copied rather than joined so editing a rule never
    /// rewrites the meaning of alerts that already fired.
    /// </summary>
    public int RuleVersion { get; set; }

    public SignalDirection Direction { get; set; }

    /// <summary>How far past the threshold the condition is, 0-1. Used to rank competing signals.</summary>
    public decimal Score { get; set; }

    /// <summary>
    /// The structured values behind the decision (price, threshold, RSI, moving average,
    /// allocation) as JSON, so the UI can explain *why* a signal fired without re-running it.
    /// </summary>
    public string FactsJson { get; set; } = "{}";

    public SignalStatus Status { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>When the condition stopped holding.</summary>
    public DateTimeOffset? ResolvedAt { get; set; }

    public ICollection<Alert> Alerts { get; set; } = new List<Alert>();
}
