using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PortfolioApp.Configuration;
using PortfolioApp.Models;
using PortfolioApp.Services;

namespace PortfolioApp.Functions;

/// <summary>
/// Timer-triggered monitoring run. Fetches the latest quote for every configured symbol on the
/// configured schedule, skipping ticks that fall outside the trading session. Persisting
/// snapshots and evaluating rules are follow-up steps described in DESIGN.md; this function is
/// the data-acquisition stage.
/// </summary>
public sealed class QuoteMonitorFunction
{
    /// <summary>
    /// NCRONTAB schedule (second minute hour day month day-of-week), read from the
    /// `QuoteMonitorSchedule` application setting so the cadence can be tuned per environment
    /// without a rebuild. The cron window is a coarse filter evaluated in the host's time zone;
    /// <see cref="IMarketCalendar"/> makes the precise open/close/holiday decision.
    /// </summary>
    public const string Schedule = "%QuoteMonitorSchedule%";

    private readonly IAlphaVantageClient _client;
    private readonly IQuoteStore _store;
    private readonly IMarketCalendar _marketCalendar;
    private readonly IWatchlistProvider _watchlist;
    private readonly IPortfolioAnalysisService _analysis;
    private readonly AlphaVantageOptions _options;
    private readonly MarketHoursOptions _marketHours;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<QuoteMonitorFunction> _logger;

    public QuoteMonitorFunction(
        IAlphaVantageClient client,
        IQuoteStore store,
        IMarketCalendar marketCalendar,
        IWatchlistProvider watchlist,
        IPortfolioAnalysisService analysis,
        IOptions<AlphaVantageOptions> options,
        IOptions<MarketHoursOptions> marketHours,
        TimeProvider timeProvider,
        ILogger<QuoteMonitorFunction> logger)
    {
        _client = client;
        _store = store;
        _marketCalendar = marketCalendar;
        _watchlist = watchlist;
        _analysis = analysis;
        _options = options.Value;
        _marketHours = marketHours.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    [Function(nameof(QuoteMonitorFunction))]
    // A transient provider or database blip would otherwise discard the entire interval's data.
    // Retries are deliberately spaced: the provider throttles per minute, so retrying instantly
    // would just fail again.
    [ExponentialBackoffRetry(maxRetryCount: 3, minimumInterval: "00:00:10", maximumInterval: "00:01:00")]
    public async Task RunAsync(
        [TimerTrigger(Schedule)] TimerInfo timerInfo,
        CancellationToken cancellationToken)
    {
        var symbols = await _watchlist.GetSymbolsAsync(cancellationToken).ConfigureAwait(false);
        if (symbols.Count == 0)
        {
            _logger.LogWarning(
                "No symbols to monitor. Upload a positions file, or set the 'AlphaVantage__Symbols' application setting.");
            return;
        }

        // Check the session before spending any API quota: outside market hours the provider
        // returns the same stale quote every time, so the call is pure waste against the
        // request budget.
        if (_marketHours.Enabled)
        {
            var status = _marketCalendar.GetStatus(_timeProvider.GetUtcNow());
            if (!status.IsOpen)
            {
                _logger.LogInformation(
                    "Market is closed ({Reason}) at {LocalTime:yyyy-MM-dd HH:mm} exchange-local; skipping this run.",
                    status.Reason,
                    status.LocalTime);
                return;
            }
        }

        if (timerInfo.IsPastDue)
        {
            _logger.LogWarning("Monitoring run started late; the previous occurrence was missed.");
        }

        _logger.LogInformation("Starting monitoring run for {SymbolCount} symbol(s): {Symbols}.", symbols.Count, string.Join(", ", symbols));

        var quotes = new List<StockQuote>(symbols.Count);
        var failures = 0;
        var retryableFailures = 0;

        for (var i = 0; i < symbols.Count; i++)
        {
            var symbol = symbols[i];

            // Spread requests out: Alpha Vantage throttles bursts on a per-minute window.
            if (i > 0 && _options.DelayBetweenRequestsMilliseconds > 0)
            {
                await Task.Delay(_options.DelayBetweenRequestsMilliseconds, cancellationToken).ConfigureAwait(false);
            }

            try
            {
                var quote = await _client.GetQuoteAsync(symbol, cancellationToken).ConfigureAwait(false);
                quotes.Add(quote);

                _logger.LogInformation(
                    "{Symbol}: {Price:F2} ({Change:+0.00;-0.00;0.00} / {ChangePercent:+0.00;-0.00;0.00}%) volume {Volume:N0}, latest trading day {LatestTradingDay}.",
                    quote.Symbol,
                    quote.Price,
                    quote.Change,
                    quote.ChangePercent,
                    quote.Volume,
                    quote.LatestTradingDay?.ToString("yyyy-MM-dd") ?? "unknown");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning("Monitoring run cancelled by the host before {Symbol} completed.", symbol);
                throw;
            }
            catch (AlphaVantageException ex)
            {
                failures++;
                if (ex.IsThrottled)
                {
                    // Not retryable: the budget is already spent, and retrying spends more of it.
                    _logger.LogWarning(ex, "Skipping {Symbol}: provider rate limit reached.", symbol);
                }
                else
                {
                    _logger.LogError(ex, "Skipping {Symbol}: provider returned an error.", symbol);
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                failures++;
                retryableFailures++;
                _logger.LogError(ex, "Skipping {Symbol}: the request to Alpha Vantage failed.", symbol);
            }
        }

        // Persist before reporting success: a run that fetched quotes but failed to store them
        // has accomplished nothing, and the exception below must not mask a write failure.
        var writeResult = await _store.SaveQuotesAsync(quotes, cancellationToken).ConfigureAwait(false);

        // Evaluate rules against the freshly stored quotes. Analysis runs after persistence so
        // indicators see the current observation, and is guarded so a rule-engine defect degrades
        // the app to plain collection rather than failing the whole run and losing the data.
        var analysis = default(AnalysisResult);
        try
        {
            analysis = await _analysis.AnalyseAsync(quotes, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Rule evaluation failed; quotes were still stored for this run.");
        }

        _logger.LogInformation(
            "Monitoring run finished. Retrieved {SuccessCount} of {SymbolCount} quote(s); stored {SavedCount}, skipped {SkippedCount} unchanged; raised {SignalsRaised} signal(s), resolved {SignalsResolved}. Next run at {NextRun}.",
            quotes.Count,
            symbols.Count,
            writeResult.Saved,
            writeResult.SkippedUnchanged,
            analysis.SignalsRaised,
            analysis.SignalsResolved,
            timerInfo.ScheduleStatus?.Next.ToString("O") ?? "unknown");

        // Every symbol failed. Throw only when at least one failure was retryable: a run that
        // failed purely on rate limits cannot succeed on retry, and re-running would consume
        // more of the daily budget. Those are logged as errors instead.
        if (failures == symbols.Count && failures > 0)
        {
            if (retryableFailures > 0)
            {
                throw new InvalidOperationException(
                    $"Monitoring run failed: no quotes could be retrieved for any of the {symbols.Count} monitored symbol(s).");
            }

            _logger.LogError(
                "Monitoring run retrieved no quotes for any of the {SymbolCount} monitored symbol(s); all failures were provider rejections or rate limits, so this run will not be retried.",
                symbols.Count);
        }
    }
}
