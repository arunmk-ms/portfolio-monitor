using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PortfolioApp.Data;
using PortfolioApp.Models;

namespace PortfolioApp.Services;

/// <param name="SignalsRaised">New signals created this run.</param>
/// <param name="SignalsSuppressed">Still-true conditions whose signal was already open.</param>
/// <param name="SignalsResolved">Conditions that cleared, re-arming their rule.</param>
/// <param name="AlertsSent">Alert deliveries that succeeded.</param>
public readonly record struct AnalysisResult(
    int SignalsRaised,
    int SignalsSuppressed,
    int SignalsResolved,
    int AlertsSent);

public interface IPortfolioAnalysisService
{
    Task<AnalysisResult> AnalyseAsync(
        IReadOnlyCollection<StockQuote> quotes,
        CancellationToken cancellationToken);
}

/// <summary>
/// Runs the decision-support half of a monitoring pass: builds each symbol's indicator and
/// allocation context, evaluates the versioned rules, persists resulting signals, and dispatches
/// alerts for newly raised ones.
/// </summary>
public sealed class PortfolioAnalysisService : IPortfolioAnalysisService
{
    /// <summary>
    /// Upper bound on snapshots loaded per symbol. Comfortably covers a 200-period moving
    /// average while keeping the query bounded as history grows.
    /// </summary>
    private const int MaxHistoryPoints = 250;

    private readonly PortfolioDbContext _db;
    private readonly IRuleEngine _ruleEngine;
    private readonly ISignalStore _signalStore;
    private readonly IEnumerable<INotificationChannel> _channels;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<PortfolioAnalysisService> _logger;

    public PortfolioAnalysisService(
        PortfolioDbContext db,
        IRuleEngine ruleEngine,
        ISignalStore signalStore,
        IEnumerable<INotificationChannel> channels,
        TimeProvider timeProvider,
        ILogger<PortfolioAnalysisService> logger)
    {
        _db = db;
        _ruleEngine = ruleEngine;
        _signalStore = signalStore;
        _channels = channels;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<AnalysisResult> AnalyseAsync(
        IReadOnlyCollection<StockQuote> quotes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(quotes);

        if (quotes.Count == 0)
        {
            return default;
        }

        var rules = await _db.WatchRules
            .Where(r => r.IsEnabled)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (rules.Count == 0)
        {
            _logger.LogDebug("No enabled watch rules; skipping analysis.");
            return default;
        }

        var latestPrices = quotes.ToDictionary(q => q.Symbol, q => q.Price, StringComparer.Ordinal);

        // Aggregate duplicate positions: the same ticker can be held in several accounts.
        var holdings = await _db.Holdings
            .Where(h => h.IsMonitorable)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var positions = holdings
            .GroupBy(h => h.Symbol, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => new Position(
                    Quantity: group.Sum(h => h.Quantity ?? 0m),
                    AverageCost: WeightedAverageCost(group)),
                StringComparer.Ordinal);

        var totalPortfolioValue = positions.Sum(entry =>
            latestPrices.TryGetValue(entry.Key, out var price) ? entry.Value.Quantity * price : 0m);

        var raised = 0;
        var suppressed = 0;
        var resolved = 0;
        var alertsSent = 0;

        foreach (var quote in quotes)
        {
            var history = await LoadPriceHistoryAsync(quote.Symbol, cancellationToken).ConfigureAwait(false);

            positions.TryGetValue(quote.Symbol, out var position);

            decimal? marketValue = position.Quantity > 0m ? position.Quantity * quote.Price : null;

            var context = new SymbolEvaluationContext(
                Symbol: quote.Symbol,
                Price: quote.Price,
                AverageCost: position.AverageCost,
                MarketValue: marketValue,
                AllocationPercent: IndicatorCalculator.AllocationPercent(marketValue, totalPortfolioValue),
                PriceHistory: history);

            var evaluations = _ruleEngine.Evaluate(context, rules);
            if (evaluations.Count == 0)
            {
                continue;
            }

            var result = await _signalStore
                .RecordAsync(quote.Symbol, evaluations, _timeProvider.GetUtcNow(), cancellationToken)
                .ConfigureAwait(false);

            raised += result.Created.Count;
            suppressed += result.Suppressed;
            resolved += result.Resolved;

            foreach (var signal in result.Created)
            {
                var rule = rules.First(r => r.Id == signal.WatchRuleId);
                alertsSent += await DispatchAsync(signal, rule, cancellationToken).ConfigureAwait(false);
            }
        }

        if (raised > 0 || resolved > 0)
        {
            _logger.LogInformation(
                "Analysis complete: {Raised} signal(s) raised, {Suppressed} suppressed as already open, {Resolved} resolved.",
                raised,
                suppressed,
                resolved);
        }

        return new AnalysisResult(raised, suppressed, resolved, alertsSent);
    }

    /// <returns>Number of channels that delivered successfully.</returns>
    private async Task<int> DispatchAsync(Signal signal, WatchRule rule, CancellationToken cancellationToken)
    {
        var sent = 0;

        foreach (var channel in _channels)
        {
            var alert = new Alert { SignalId = signal.Id, Channel = channel.Channel };

            try
            {
                await channel.SendAsync(signal, rule, cancellationToken).ConfigureAwait(false);
                alert.SentAt = _timeProvider.GetUtcNow();
                sent++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A delivery failure must not discard the signal itself, so record the reason
                // and continue with the remaining channels.
                alert.FailureReason = ex.Message;
                _logger.LogError(
                    ex,
                    "Failed to deliver alert for signal {SignalId} over {Channel}.",
                    signal.Id,
                    channel.Channel);
            }

            _db.Alerts.Add(alert);
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return sent;
    }

    private async Task<IReadOnlyList<decimal>> LoadPriceHistoryAsync(
        string symbol,
        CancellationToken cancellationToken)
    {
        // Ordered by identity rather than CapturedAt: it is the insertion order the dedup logic
        // already relies on, and unlike DateTimeOffset it sorts on every provider.
        var descending = await _db.PriceSnapshots
            .Where(s => s.Symbol == symbol)
            .OrderByDescending(s => s.Id)
            .Take(MaxHistoryPoints)
            .Select(s => s.Price)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // Indicators expect oldest to newest.
        descending.Reverse();
        return descending;
    }

    /// <summary>
    /// Quantity-weighted average cost across accounts. A plain mean would misstate the basis
    /// whenever the same ticker is held in different sizes.
    /// </summary>
    private static decimal? WeightedAverageCost(IEnumerable<Holding> holdings)
    {
        decimal totalQuantity = 0m;
        decimal totalCost = 0m;

        foreach (var holding in holdings)
        {
            if (holding.Quantity is not { } quantity || quantity <= 0m || holding.AverageCostBasis is not { } cost)
            {
                continue;
            }

            totalQuantity += quantity;
            totalCost += quantity * cost;
        }

        return totalQuantity > 0m ? totalCost / totalQuantity : null;
    }

    private readonly record struct Position(decimal Quantity, decimal? AverageCost);
}
