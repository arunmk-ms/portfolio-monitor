using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PortfolioApp.Data;
using PortfolioApp.Models;
using PortfolioApp.Services;
using Xunit;

namespace PortfolioApp.Tests;

/// <summary>
/// Store tests run against SQLite in-memory rather than the EF InMemory provider, so real
/// relational behaviour (keys, ordering, round-tripping) is exercised.
/// </summary>
public sealed class SqlQuoteStoreTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<PortfolioDbContext> _options;

    public SqlQuoteStoreTests()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();

        _options = new DbContextOptionsBuilder<PortfolioDbContext>()
            .UseSqlite(_connection)
            .Options;

        using var context = new PortfolioDbContext(_options);
        context.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private PortfolioDbContext NewContext() => new(_options);

    private static StockQuote Quote(
        string symbol = "MSFT",
        decimal price = 496.82m,
        long volume = 15_336_074L,
        string tradingDay = "2026-09-02",
        int minuteOffset = 0) =>
        new(
            Symbol: symbol,
            Price: price,
            Open: 499.85m,
            High: 500.27m,
            Low: 493.81m,
            PreviousClose: 501.02m,
            Change: -4.20m,
            ChangePercent: -0.8383m,
            Volume: volume,
            LatestTradingDay: DateOnly.Parse(tradingDay),
            RetrievedAt: new DateTimeOffset(2026, 9, 2, 20, minuteOffset, 0, TimeSpan.Zero));

    private async Task<SnapshotWriteResult> SaveAsync(params StockQuote[] quotes)
    {
        await using var context = NewContext();
        var store = new SqlQuoteStore(context, NullLogger<SqlQuoteStore>.Instance);
        return await store.SaveQuotesAsync(quotes, CancellationToken.None);
    }

    [Fact]
    public async Task SaveQuotesAsync_PersistsNewQuotes()
    {
        var result = await SaveAsync(Quote("MSFT"), Quote("NVDA", price: 170.76m, volume: 148_225_317L));

        Assert.Equal(2, result.Saved);
        Assert.Equal(0, result.SkippedUnchanged);

        await using var context = NewContext();
        Assert.Equal(2, await context.PriceSnapshots.CountAsync());
    }

    [Fact]
    public async Task SaveQuotesAsync_RoundTripsAllFields()
    {
        await SaveAsync(Quote());

        await using var context = NewContext();
        var stored = await context.PriceSnapshots.SingleAsync();

        Assert.Equal("MSFT", stored.Symbol);
        Assert.Equal(496.82m, stored.Price);
        Assert.Equal(499.85m, stored.Open);
        Assert.Equal(500.27m, stored.High);
        Assert.Equal(493.81m, stored.Low);
        Assert.Equal(501.02m, stored.PreviousClose);
        Assert.Equal(-4.20m, stored.Change);
        Assert.Equal(-0.8383m, stored.ChangePercent);
        Assert.Equal(15_336_074L, stored.Volume);
        Assert.Equal(new DateOnly(2026, 9, 2), stored.LatestTradingDay);
    }

    [Fact]
    public async Task SaveQuotesAsync_SkipsQuoteIdenticalToPreviousSnapshot()
    {
        await SaveAsync(Quote(minuteOffset: 0));

        // Same delayed quote returned by the next poll five minutes later.
        var result = await SaveAsync(Quote(minuteOffset: 5));

        Assert.Equal(0, result.Saved);
        Assert.Equal(1, result.SkippedUnchanged);

        await using var context = NewContext();
        Assert.Equal(1, await context.PriceSnapshots.CountAsync());
    }

    [Fact]
    public async Task SaveQuotesAsync_StoresQuoteWhenPriceChanges()
    {
        await SaveAsync(Quote(price: 496.82m, minuteOffset: 0));

        var result = await SaveAsync(Quote(price: 497.10m, minuteOffset: 5));

        Assert.Equal(1, result.Saved);
        Assert.Equal(0, result.SkippedUnchanged);
        await using var context = NewContext();
        Assert.Equal(2, await context.PriceSnapshots.CountAsync());
    }

    [Fact]
    public async Task SaveQuotesAsync_StoresQuoteWhenOnlyVolumeChanges()
    {
        await SaveAsync(Quote(volume: 15_336_074L, minuteOffset: 0));

        var result = await SaveAsync(Quote(volume: 15_400_000L, minuteOffset: 5));

        Assert.Equal(1, result.Saved);
    }

    [Fact]
    public async Task SaveQuotesAsync_StoresQuoteWhenTradingDayRollsForward()
    {
        await SaveAsync(Quote(tradingDay: "2026-09-02", minuteOffset: 0));

        var result = await SaveAsync(Quote(tradingDay: "2026-09-03", minuteOffset: 5));

        Assert.Equal(1, result.Saved);
    }

    [Fact]
    public async Task SaveQuotesAsync_TracksSymbolsIndependently()
    {
        await SaveAsync(Quote("MSFT"), Quote("NVDA", price: 170.76m, volume: 148_225_317L));

        // MSFT unchanged, NVDA moved.
        var result = await SaveAsync(
            Quote("MSFT", minuteOffset: 5),
            Quote("NVDA", price: 172.00m, volume: 148_225_317L, minuteOffset: 5));

        Assert.Equal(1, result.Saved);
        Assert.Equal(1, result.SkippedUnchanged);

        await using var context = NewContext();
        Assert.Equal(2, await context.PriceSnapshots.CountAsync(s => s.Symbol == "NVDA"));
        Assert.Equal(1, await context.PriceSnapshots.CountAsync(s => s.Symbol == "MSFT"));
    }

    [Fact]
    public async Task SaveQuotesAsync_ReturnsZeroesForEmptyBatch()
    {
        var result = await SaveAsync();

        Assert.Equal(0, result.Saved);
        Assert.Equal(0, result.SkippedUnchanged);
    }

    [Fact]
    public async Task SaveQuotesAsync_ComparesAgainstMostRecentSnapshotNotOldest()
    {
        await SaveAsync(Quote(price: 100m, minuteOffset: 0));
        await SaveAsync(Quote(price: 200m, minuteOffset: 5));

        // Matches the oldest snapshot but not the newest, so it must be stored.
        var result = await SaveAsync(Quote(price: 100m, minuteOffset: 10));

        Assert.Equal(1, result.Saved);
        await using var context = NewContext();
        Assert.Equal(3, await context.PriceSnapshots.CountAsync());
    }
}
