using Microsoft.Azure.Functions.Worker;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PortfolioApp.Configuration;
using PortfolioApp.Data;
using PortfolioApp.Functions;
using PortfolioApp.Models;
using PortfolioApp.Services;
using Xunit;

namespace PortfolioApp.Tests;

/// <summary>
/// Covers the monitoring run's orchestration: the market-hours guard, partial-failure handling,
/// and which failures are allowed to bubble up and trigger a host retry.
/// </summary>
public sealed class QuoteMonitorFunctionTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<PortfolioDbContext> _options;

    // A Wednesday, 12:00 ET - a regular open session.
    private static readonly DateTimeOffset MarketOpenInstant = new(2026, 9, 9, 16, 0, 0, TimeSpan.Zero);

    // A Saturday.
    private static readonly DateTimeOffset MarketClosedInstant = new(2026, 9, 12, 16, 0, 0, TimeSpan.Zero);

    public QuoteMonitorFunctionTests()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<PortfolioDbContext>().UseSqlite(_connection).Options;

        using var context = new PortfolioDbContext(_options);
        context.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private PortfolioDbContext NewContext() => new(_options);

    private static QuoteMonitorFunction Build(
        PortfolioDbContext context,
        IAlphaVantageClient client,
        DateTimeOffset now,
        bool marketHoursEnabled = true,
        string symbols = "MSFT,NVDA")
    {
        var alphaVantage = Options.Create(new AlphaVantageOptions
        {
            ApiKey = "test-key",
            Symbols = symbols,
            DelayBetweenRequestsMilliseconds = 0
        });

        var marketHours = Options.Create(new MarketHoursOptions { Enabled = marketHoursEnabled });
        var timeProvider = new FixedTimeProvider(now);

        return new QuoteMonitorFunction(
            client,
            new SqlQuoteStore(context, NullLogger<SqlQuoteStore>.Instance),
            new UsEquityMarketCalendar(marketHours),
            new WatchlistProvider(context, alphaVantage, NullLogger<WatchlistProvider>.Instance),
            new NoOpAnalysisService(),
            alphaVantage,
            marketHours,
            timeProvider,
            NullLogger<QuoteMonitorFunction>.Instance);
    }

    /// <summary>These tests cover fetch orchestration; rule evaluation is covered separately.</summary>
    private sealed class NoOpAnalysisService : IPortfolioAnalysisService
    {
        public Task<AnalysisResult> AnalyseAsync(
            IReadOnlyCollection<StockQuote> quotes,
            CancellationToken cancellationToken) => Task.FromResult(default(AnalysisResult));
    }

    private static TimerInfo Timer() => new();

    [Fact]
    public async Task Run_StoresQuotesDuringMarketHours()
    {
        await using var context = NewContext();
        var client = new StubClient();

        await Build(context, client, MarketOpenInstant).RunAsync(Timer(), CancellationToken.None);

        Assert.Equal(new[] { "MSFT", "NVDA" }, client.RequestedSymbols.ToArray());
        await using var verify = NewContext();
        Assert.Equal(2, await verify.PriceSnapshots.CountAsync());
    }

    [Fact]
    public async Task Run_MakesNoProviderCallsWhenMarketIsClosed()
    {
        await using var context = NewContext();
        var client = new StubClient();

        await Build(context, client, MarketClosedInstant).RunAsync(Timer(), CancellationToken.None);

        // The guard must short-circuit before spending any API quota.
        Assert.Empty(client.RequestedSymbols);
        await using var verify = NewContext();
        Assert.Equal(0, await verify.PriceSnapshots.CountAsync());
    }

    [Fact]
    public async Task Run_IgnoresClosedMarketWhenGuardDisabled()
    {
        await using var context = NewContext();
        var client = new StubClient();

        await Build(context, client, MarketClosedInstant, marketHoursEnabled: false)
            .RunAsync(Timer(), CancellationToken.None);

        Assert.Equal(2, client.RequestedSymbols.Count);
    }

    [Fact]
    public async Task Run_ContinuesAfterASingleSymbolFails()
    {
        await using var context = NewContext();
        var client = new StubClient
        {
            FailuresBySymbol = { ["MSFT"] = new AlphaVantageException("MSFT", "boom") }
        };

        await Build(context, client, MarketOpenInstant).RunAsync(Timer(), CancellationToken.None);

        await using var verify = NewContext();
        var stored = await verify.PriceSnapshots.Select(s => s.Symbol).ToListAsync();
        Assert.Equal(new[] { "NVDA" }, stored.ToArray());
    }

    [Fact]
    public async Task Run_ThrowsWhenEverySymbolFailsForARetryableReason()
    {
        await using var context = NewContext();
        var client = new StubClient
        {
            FailuresBySymbol =
            {
                ["MSFT"] = new HttpRequestException("network down"),
                ["NVDA"] = new HttpRequestException("network down")
            }
        };

        // A transport failure can succeed on retry, so it must surface to the host.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Build(context, client, MarketOpenInstant).RunAsync(Timer(), CancellationToken.None));
    }

    [Fact]
    public async Task Run_DoesNotThrowWhenEveryFailureIsRateLimiting()
    {
        await using var context = NewContext();
        var client = new StubClient
        {
            FailuresBySymbol =
            {
                ["MSFT"] = new AlphaVantageException("MSFT", "limit", isThrottled: true),
                ["NVDA"] = new AlphaVantageException("NVDA", "limit", isThrottled: true)
            }
        };

        // Retrying a rate-limited run cannot succeed and would spend more of the daily budget.
        await Build(context, client, MarketOpenInstant).RunAsync(Timer(), CancellationToken.None);

        await using var verify = NewContext();
        Assert.Equal(0, await verify.PriceSnapshots.CountAsync());
    }

    [Fact]
    public async Task Run_DoesNothingWhenNoSymbolsAreConfigured()
    {
        await using var context = NewContext();
        var client = new StubClient();

        await Build(context, client, MarketOpenInstant, symbols: " , ")
            .RunAsync(Timer(), CancellationToken.None);

        Assert.Empty(client.RequestedSymbols);
    }

    [Fact]
    public async Task Run_PrefersImportedWatchlistOverConfiguredSymbols()
    {
        await using (var seed = NewContext())
        {
            seed.WatchlistSymbols.Add(new WatchlistSymbol
            {
                Symbol = "TSLA",
                Source = WatchlistSource.Import,
                IsEnabled = true,
                AddedAt = MarketOpenInstant,
                UpdatedAt = MarketOpenInstant
            });
            await seed.SaveChangesAsync();
        }

        await using var context = NewContext();
        var client = new StubClient();

        await Build(context, client, MarketOpenInstant).RunAsync(Timer(), CancellationToken.None);

        Assert.Equal(new[] { "TSLA" }, client.RequestedSymbols.ToArray());
    }

    private sealed class StubClient : IAlphaVantageClient
    {
        public List<string> RequestedSymbols { get; } = new();

        public Dictionary<string, Exception> FailuresBySymbol { get; } = new(StringComparer.Ordinal);

        public Task<StockQuote> GetQuoteAsync(string symbol, CancellationToken cancellationToken)
        {
            RequestedSymbols.Add(symbol);

            if (FailuresBySymbol.TryGetValue(symbol, out var failure))
            {
                return Task.FromException<StockQuote>(failure);
            }

            return Task.FromResult(new StockQuote(
                symbol, 100m, 99m, 101m, 98m, 99m, 1m, 1m, 1_000L,
                new DateOnly(2026, 9, 9), MarketOpenInstant));
        }
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;

        public FixedTimeProvider(DateTimeOffset now) => _now = now;

        public override DateTimeOffset GetUtcNow() => _now;
    }
}
