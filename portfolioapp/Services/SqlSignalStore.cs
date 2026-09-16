using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PortfolioApp.Data;
using PortfolioApp.Models;

namespace PortfolioApp.Services;

/// <param name="Created">Signals newly raised on this run.</param>
/// <param name="Suppressed">Conditions still true whose signal is already open.</param>
/// <param name="Resolved">Open signals whose condition stopped holding, re-arming the rule.</param>
public readonly record struct SignalRecordResult(
    IReadOnlyList<Signal> Created,
    int Suppressed,
    int Resolved);

public interface ISignalStore
{
    Task<SignalRecordResult> RecordAsync(
        string symbol,
        IReadOnlyList<RuleEvaluation> evaluations,
        DateTimeOffset now,
        CancellationToken cancellationToken);
}

/// <summary>
/// Persists rule verdicts as signals, implementing the "suppress duplicate alerts until a
/// condition resets" requirement from DESIGN.md.
/// </summary>
/// <remarks>
/// A signal stays <see cref="SignalStatus.Open"/> for as long as its condition holds, and a
/// second evaluation of the same rule does not raise a duplicate. Only when the condition stops
/// holding is the signal resolved, which re-arms the rule. Without this a price sitting one cent
/// below a threshold would alert on every single run.
/// </remarks>
public sealed class SqlSignalStore : ISignalStore
{
    private static readonly JsonSerializerOptions FactsOptions = new(JsonSerializerDefaults.Web);

    private readonly PortfolioDbContext _db;
    private readonly ILogger<SqlSignalStore> _logger;

    public SqlSignalStore(PortfolioDbContext db, ILogger<SqlSignalStore> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<SignalRecordResult> RecordAsync(
        string symbol,
        IReadOnlyList<RuleEvaluation> evaluations,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        ArgumentNullException.ThrowIfNull(evaluations);

        var created = new List<Signal>();
        var suppressed = 0;
        var resolved = 0;

        foreach (var evaluation in evaluations)
        {
            var rule = evaluation.Rule;

            if (evaluation.Outcome == RuleOutcome.Skipped)
            {
                // Missing data is not evidence the condition cleared, so leave any open signal
                // alone rather than resolving it and re-alerting once data returns.
                _logger.LogDebug(
                    "{Symbol}: rule '{Rule}' skipped - {Reason}",
                    symbol,
                    rule.Name,
                    evaluation.SkipReason);
                continue;
            }

            var open = await _db.Signals
                .FirstOrDefaultAsync(
                    s => s.Symbol == symbol
                         && s.WatchRuleId == rule.Id
                         && s.Status == SignalStatus.Open,
                    cancellationToken)
                .ConfigureAwait(false);

            if (evaluation.Outcome == RuleOutcome.Triggered)
            {
                if (open is not null)
                {
                    suppressed++;
                    continue;
                }

                var signal = new Signal
                {
                    Symbol = symbol,
                    WatchRuleId = rule.Id,
                    // Snapshot the version so editing the rule later cannot rewrite history.
                    RuleVersion = rule.Version,
                    Direction = rule.Direction,
                    Score = evaluation.Score,
                    FactsJson = JsonSerializer.Serialize(evaluation.Facts, FactsOptions),
                    Status = SignalStatus.Open,
                    CreatedAt = now
                };

                _db.Signals.Add(signal);
                created.Add(signal);

                _logger.LogInformation(
                    "{Symbol}: rule '{Rule}' v{Version} triggered ({Direction}, score {Score:F2}).",
                    symbol,
                    rule.Name,
                    rule.Version,
                    rule.Direction,
                    evaluation.Score);

                continue;
            }

            // NotTriggered: the condition cleared, so re-arm the rule.
            if (open is not null)
            {
                open.Status = SignalStatus.Resolved;
                open.ResolvedAt = now;
                resolved++;

                _logger.LogInformation(
                    "{Symbol}: rule '{Rule}' condition cleared; signal {SignalId} resolved.",
                    symbol,
                    rule.Name,
                    open.Id);
            }
        }

        if (created.Count > 0 || resolved > 0)
        {
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return new SignalRecordResult(created, suppressed, resolved);
    }
}
