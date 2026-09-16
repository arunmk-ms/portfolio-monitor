namespace PortfolioApp.Models;

/// <summary>Where an alert was delivered.</summary>
public enum AlertChannel
{
    /// <summary>Written to logs/telemetry only. The default until a real channel is configured.</summary>
    Log = 0,

    Email = 1,

    Push = 2
}

/// <summary>
/// A delivery attempt for a signal — the `Alert` entity from DESIGN.md.
/// </summary>
/// <remarks>
/// Kept separate from <see cref="Signal"/> so one signal can fan out to several channels and so
/// a delivery failure never destroys the underlying finding.
/// </remarks>
public sealed class Alert
{
    public int Id { get; set; }

    public int SignalId { get; set; }

    public Signal? Signal { get; set; }

    public AlertChannel Channel { get; set; }

    public DateTimeOffset? SentAt { get; set; }

    public DateTimeOffset? AcknowledgedAt { get; set; }

    /// <summary>Set when delivery failed, so a failed send is visible rather than silently lost.</summary>
    public string? FailureReason { get; set; }
}
