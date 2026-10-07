using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PortfolioApp.Configuration;
using PortfolioApp.Data;
using PortfolioApp.Models;
using PortfolioApp.Services;
using System.Text.Json;

namespace PortfolioApp.Functions;

/// <summary>
/// One composed read model for the dashboard: market status, quotes, holdings, watchlist,
/// signals, and alert history.
/// </summary>
/// <remarks>
/// A dashboard shows several panels that must agree with each other. Assembling them from
/// separate round trips lets one panel render against newer data than the next, so totals and
/// allocations silently disagree. This returns a single snapshot taken at one instant instead.
/// <para>
/// Every price carries the source it came from, and <c>marketStatus.dataQuality</c> reports the
/// weakest source present, because DESIGN.md requires delayed and end-of-day data to be labelled
/// rather than presented as live.
/// </para>
/// </remarks>
public sealed class DashboardApiFunction
{
    /// <summary>Indicator lookbacks. Returned as null until enough history exists.</summary>
    private const int RsiPeriod = 14;
    private const int ShortMaPeriod = 50;
    private const int LongMaPeriod = 200;

    /// <summary>Bounded so a long-running deployment cannot return an unbounded payload.</summary>
    private const int MaxAlerts = 100;

    private static readonly JsonSerializerOptions FactsOptions = new(JsonSerializerDefaults.Web);

    private readonly PortfolioDbContext _db;
    private readonly IMarketCalendar _marketCalendar;
    private readonly MarketHoursOptions _marketHours;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<DashboardApiFunction> _logger;

    public DashboardApiFunction(
        PortfolioDbContext db,
        IMarketCalendar marketCalendar,
        IOptions<MarketHoursOptions> marketHours,
        TimeProvider timeProvider,
        ILogger<DashboardApiFunction> logger)
    {
        _db = db;
        _marketCalendar = marketCalendar;
        _marketHours = marketHours.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    [Function(nameof(GetDashboard))]
    public async Task<IActionResult> GetDashboard(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "dashboard")] HttpRequest request,
        CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();

        var holdings = await _db.Holdings
            .AsNoTracking()
            .Include(h => h.Account)
            .OrderBy(h => h.Symbol)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var watchlist = await _db.WatchlistSymbols
            .AsNoTracking()
            .OrderBy(w => w.Symbol)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var rules = await _db.WatchRules
            .AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var signals = await _db.Signals
            .AsNoTracking()
            .Include(s => s.WatchRule)
            .OrderByDescending(s => s.CreatedAt)
            .Take(MaxAlerts)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var alerts = await _db.Alerts
            .AsNoTracking()
            .Include(a => a.Signal)
            .OrderByDescending(a => a.Id)
            .Take(MaxAlerts)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var lastImport = await _db.PortfolioImports
            .AsNoTracking()
            .OrderByDescending(i => i.ImportedAt)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        // Every symbol the UI can show a price for: owned plus explicitly watched.
        var symbols = holdings.Select(h => h.Symbol)
            .Concat(watchlist.Select(w => w.Symbol))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var history = await LoadHistoryAsync(symbols, cancellationToken).ConfigureAwait(false);
        var holdingsBySymbol = holdings
            .GroupBy(h => h.Symbol, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var quotes = symbols
            .Select(symbol => BuildQuote(
                symbol,
                history.GetValueOrDefault(symbol) ?? [],
                holdingsBySymbol.GetValueOrDefault(symbol)))
            .Where(q => q is not null)
            .Select(q => q!)
            .OrderBy(q => q.Symbol, StringComparer.Ordinal)
            .ToList();

        var marketStatus = _marketCalendar.GetStatus(now);
        var openSignalSymbols = signals
            .Where(s => s.Status == SignalStatus.Open)
            .Select(s => s.Symbol)
            .ToHashSet(StringComparer.Ordinal);

        var body = new
        {
            capturedAt = now,
            marketStatus = new
            {
                state = marketStatus.IsOpen ? "OPEN" : "CLOSED",
                reason = marketStatus.Reason.ToString(),
                guardEnabled = _marketHours.Enabled,
                dataQuality = DescribeDataQuality(quotes),
                provider = DescribeProvider(quotes),
                exchangeLocalTime = marketStatus.LocalTime.ToString("yyyy-MM-dd HH:mm")
            },
            lastImport = lastImport is null
                ? null
                : new
                {
                    lastImport.FileName,
                    lastImport.ImportedAt,
                    lastImport.SourceDownloadedAt,
                    lastImport.PositionsImported
                },
            quotes,
            holdings = holdings.Select(h => new
            {
                h.Symbol,
                h.Description,
                Account = h.Account?.Name,
                h.Quantity,
                AverageCost = h.AverageCostBasis,
                h.CostBasisTotal,
                ImportedValue = h.CurrentValue,
                PositionType = h.PositionType.ToString(),
                h.IsMonitorable,
                h.UpdatedAt
            }),
            watchlist = watchlist.Select(w => BuildWatchlistEntry(w, rules, openSignalSymbols)),
            signals = signals.Select(s => new
            {
                s.Id,
                s.Symbol,
                Direction = s.Direction.ToString(),
                Status = s.Status.ToString(),
                // Stored 0-1; the UI shows 0-100 and should not have to know the scale.
                Score = Math.Round(s.Score * 100m, 1),
                s.RuleVersion,
                RuleId = s.WatchRuleId,
                RuleName = s.WatchRule?.Name,
                RuleType = s.WatchRule?.RuleType.ToString(),
                s.CreatedAt,
                s.ResolvedAt,
                Facts = ParseFacts(s.FactsJson)
            }),
            alerts = alerts.Select(a => new
            {
                a.Id,
                a.SignalId,
                Symbol = a.Signal?.Symbol,
                Channel = a.Channel.ToString(),
                a.SentAt,
                a.AcknowledgedAt,
                a.FailureReason
            }),
            counts = new
            {
                holdings = holdings.Count,
                monitoredSymbols = watchlist.Count(w => w.IsEnabled),
                enabledRules = rules.Count(r => r.IsEnabled),
                openSignals = openSignalSymbols.Count,
                priceSnapshots = history.Sum(h => h.Value.Count)
            }
        };

        _logger.LogInformation(
            "Dashboard read: {Holdings} holding(s), {Symbols} symbol(s), {Signals} signal(s).",
            holdings.Count,
            symbols.Count,
            signals.Count);

        return new OkObjectResult(body);
    }

    /// <summary>
    /// Loads recent snapshots per symbol, oldest first, for indicator math.
    /// </summary>
    private async Task<Dictionary<string, List<PriceSnapshot>>> LoadHistoryAsync(
        IReadOnlyCollection<string> symbols,
        CancellationToken cancellationToken)
    {
        if (symbols.Count == 0)
        {
            return new Dictionary<string, List<PriceSnapshot>>(StringComparer.Ordinal);
        }

        // One query for all symbols, then group in memory: a per-symbol query would be N round
        // trips, and the row count here is bounded by the monitoring cadence.
        var rows = await _db.PriceSnapshots
            .AsNoTracking()
            .Where(s => symbols.Contains(s.Symbol))
            .OrderBy(s => s.CapturedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows
            .GroupBy(s => s.Symbol, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
    }

    /// <summary>
    /// Builds one quote, preferring a stored provider snapshot and falling back to the price the
    /// broker export carried. The fallback is what keeps the dashboard useful before the first
    /// monitoring run, but it is always labelled so it cannot be mistaken for a live quote.
    /// </summary>
    private static QuoteView? BuildQuote(string symbol, List<PriceSnapshot> history, Holding? holding)
    {
        var latest = history.Count > 0 ? history[^1] : null;

        if (latest is not null)
        {
            var closes = history.Select(h => h.Price).ToList();

            return new QuoteView(
                Symbol: symbol,
                Name: holding?.Description,
                Price: latest.Price,
                PreviousClose: latest.PreviousClose == 0m ? null : latest.PreviousClose,
                DayHigh: latest.High == 0m ? null : latest.High,
                DayLow: latest.Low == 0m ? null : latest.Low,
                Volume: latest.Volume == 0 ? null : latest.Volume,
                Rsi14: IndicatorCalculator.RelativeStrengthIndex(closes, RsiPeriod),
                Sma50: IndicatorCalculator.SimpleMovingAverage(closes, ShortMaPeriod),
                Sma200: IndicatorCalculator.SimpleMovingAverage(closes, LongMaPeriod),
                CapturedAt: latest.CapturedAt,
                LatestTradingDay: latest.LatestTradingDay?.ToString("yyyy-MM-dd"),
                Source: "provider",
                HistoryPoints: history.Count);
        }

        // No snapshot yet. A cash row has no meaningful price, so skip it rather than invent one.
        if (holding?.LastPrice is not { } importedPrice || !holding.IsMonitorable)
        {
            return null;
        }

        return new QuoteView(
            Symbol: symbol,
            Name: holding.Description,
            Price: importedPrice,
            PreviousClose: null,
            DayHigh: null,
            DayLow: null,
            Volume: null,
            Rsi14: null,
            Sma50: null,
            Sma200: null,
            CapturedAt: holding.UpdatedAt,
            LatestTradingDay: null,
            Source: "import",
            HistoryPoints: 0);
    }

    /// <summary>
    /// Projects a watched symbol together with the thresholds its rules imply, so the UI can show
    /// rule state without duplicating the rule engine's definitions.
    /// </summary>
    private static object BuildWatchlistEntry(
        WatchlistSymbol watched,
        IReadOnlyCollection<WatchRule> rules,
        IReadOnlySet<string> openSignalSymbols)
    {
        // A rule with no symbol applies to every monitored symbol.
        var applicable = rules
            .Where(r => r.Symbol is null || string.Equals(r.Symbol, watched.Symbol, StringComparison.Ordinal))
            .ToList();

        var enabled = applicable.Where(r => r.IsEnabled).ToList();

        decimal? Threshold(RuleType type) => enabled
            .Where(r => r.RuleType == type)
            .Select(r => (decimal?)r.Threshold)
            .DefaultIfEmpty(null)
            .Max();

        var state = openSignalSymbols.Contains(watched.Symbol)
            ? "TRIGGERED"
            : enabled.Count > 0
                ? "ARMED"
                : "NO_RULES";

        return new
        {
            watched.Symbol,
            Source = watched.Source.ToString(),
            watched.IsEnabled,
            RuleState = state,
            RuleCount = enabled.Count,
            BuyBelow = Threshold(RuleType.PriceBelow),
            SellAbove = Threshold(RuleType.PriceAbove),
            Rules = applicable.Select(r => new
            {
                r.Id,
                r.Name,
                RuleType = r.RuleType.ToString(),
                r.Threshold,
                r.Period,
                Direction = r.Direction.ToString(),
                r.IsEnabled,
                r.Version
            })
        };
    }

    /// <summary>Reports the weakest source present, so a mixed set is never oversold as live.</summary>
    private static string DescribeDataQuality(IReadOnlyCollection<QuoteView> quotes)
    {
        if (quotes.Count == 0)
        {
            return "NONE";
        }

        return quotes.Any(q => q.Source == "import") ? "IMPORTED" : "DELAYED_15M";
    }

    private static string DescribeProvider(IReadOnlyCollection<QuoteView> quotes)
    {
        var hasProvider = quotes.Any(q => q.Source == "provider");
        var hasImport = quotes.Any(q => q.Source == "import");

        return (hasProvider, hasImport) switch
        {
            (true, true) => "market data provider + broker import",
            (true, false) => "market data provider",
            (false, true) => "broker import",
            _ => "no price data"
        };
    }

    private static JsonElement? ParseFacts(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<JsonElement>(json, FactsOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <param name="Source">"provider" for a stored quote, "import" for a broker file price.</param>
    /// <param name="HistoryPoints">Snapshots available, so the UI can explain absent indicators.</param>
    private sealed record QuoteView(
        string Symbol,
        string? Name,
        decimal Price,
        decimal? PreviousClose,
        decimal? DayHigh,
        decimal? DayLow,
        long? Volume,
        decimal? Rsi14,
        decimal? Sma50,
        decimal? Sma200,
        DateTimeOffset CapturedAt,
        string? LatestTradingDay,
        string Source,
        int HistoryPoints);
}
