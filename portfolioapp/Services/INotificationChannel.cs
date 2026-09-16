using Microsoft.Extensions.Logging;
using PortfolioApp.Models;

namespace PortfolioApp.Services;

/// <summary>Delivers a signal to a user-facing channel.</summary>
public interface INotificationChannel
{
    AlertChannel Channel { get; }

    /// <summary>Delivers the signal. Implementations should throw on failure so it is recorded.</summary>
    Task SendAsync(Signal signal, WatchRule rule, CancellationToken cancellationToken);
}

/// <summary>
/// Writes alerts to the logging pipeline, which OpenTelemetry already exports.
/// </summary>
/// <remarks>
/// This is the default channel so the signal-to-alert path is complete and exercised end to end
/// without requiring email credentials. Swapping in Azure Communication Services or SendGrid is
/// a matter of registering another <see cref="INotificationChannel"/>; nothing upstream changes.
/// </remarks>
public sealed class LoggingNotificationChannel : INotificationChannel
{
    private readonly ILogger<LoggingNotificationChannel> _logger;

    public LoggingNotificationChannel(ILogger<LoggingNotificationChannel> logger)
    {
        _logger = logger;
    }

    public AlertChannel Channel => AlertChannel.Log;

    public Task SendAsync(Signal signal, WatchRule rule, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "ALERT {Direction} {Symbol}: {RuleName} (rule v{RuleVersion}, score {Score:F2}). Facts: {Facts}",
            signal.Direction,
            signal.Symbol,
            rule.Name,
            signal.RuleVersion,
            signal.Score,
            signal.FactsJson);

        return Task.CompletedTask;
    }
}
