using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PortfolioApp.Data;
using PortfolioApp.Models;
using PortfolioApp.Services;
using Xunit;

namespace PortfolioApp.Tests;

/// <summary>
/// Covers positions entered by hand: validation, the (account, symbol) upsert, and the watchlist
/// side effect that keeps a manual entry behaving exactly like an imported one.
/// </summary>
public sealed class ManualHoldingServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<PortfolioDbContext> _options;
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 9, 18, 12, 0, 0, TimeSpan.Zero));

    public ManualHoldingServiceTests()
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

    private ManualHoldingService Service(PortfolioDbContext db) =>
        new(db, _time, NullLogger<ManualHoldingService>.Instance);

    private Task<ManualHoldingResult> AddAsync(
        PortfolioDbContext db,
        string? symbol = "MSFT",
        decimal? quantity = 10m,
        decimal? averageCost = null,
        string? account = null,
        string? description = null) =>
        Service(db).UpsertAsync(symbol, quantity, averageCost, account, description, CancellationToken.None);

    [Fact]
    public async Task Upsert_CreatesHolding_WithNoOriginatingImport()
    {
        await using var db = NewContext();

        var result = await AddAsync(db, "msft", 12.5m, 390m);

        Assert.True(result.Created);
        Assert.Equal("MSFT", result.Symbol);

        var holding = await db.Holdings.SingleAsync();
        Assert.Equal("MSFT", holding.Symbol);
        Assert.Equal(12.5m, holding.Quantity);
        Assert.Equal(390m, holding.AverageCostBasis);
        Assert.Equal(4875m, holding.CostBasisTotal);
        Assert.True(holding.IsMonitorable);
        // The audit trail must not claim a file produced this row.
        Assert.Null(holding.LastImportId);
        Assert.True(holding.IsManual);
    }

    [Fact]
    public async Task Upsert_DoesNotInventAMarketPrice()
    {
        await using var db = NewContext();

        await AddAsync(db, "MSFT", 10m, 100m);

        var holding = await db.Holdings.SingleAsync();
        // Cost is known, market value is not: deriving one from the other would put a fabricated
        // number on the dashboard.
        Assert.Null(holding.LastPrice);
        Assert.Null(holding.CurrentValue);
    }

    [Fact]
    public async Task Upsert_DefaultsAccount_WhenNotSupplied()
    {
        await using var db = NewContext();

        var result = await AddAsync(db);

        Assert.Equal(ManualHoldingService.DefaultAccountName, result.AccountName);
        Assert.Equal(ManualHoldingService.DefaultAccountName, (await db.Accounts.SingleAsync()).Name);
    }

    [Fact]
    public async Task Upsert_AddsSymbolToWatchlist_AsManualSource()
    {
        await using var db = NewContext();

        var result = await AddAsync(db, "NVDA", 5m);

        Assert.True(result.AddedToWatchlist);
        var watched = await db.WatchlistSymbols.SingleAsync();
        Assert.Equal("NVDA", watched.Symbol);
        Assert.Equal(WatchlistSource.Manual, watched.Source);
        Assert.True(watched.IsEnabled);
    }

    [Fact]
    public async Task Upsert_UpdatesInPlace_ForSameAccountAndSymbol()
    {
        await using var db = NewContext();

        await AddAsync(db, "MSFT", 10m, 100m, "Brokerage");
        var second = await AddAsync(db, "msft", 25m, 120m, "Brokerage");

        Assert.False(second.Created);
        Assert.False(second.AddedToWatchlist);

        var holding = await db.Holdings.SingleAsync();
        Assert.Equal(25m, holding.Quantity);
        Assert.Equal(120m, holding.AverageCostBasis);
        Assert.Equal(3000m, holding.CostBasisTotal);
    }

    [Fact]
    public async Task Upsert_KeepsSameSymbolSeparate_AcrossAccounts()
    {
        await using var db = NewContext();

        await AddAsync(db, "MSFT", 10m, account: "Brokerage");
        await AddAsync(db, "MSFT", 4m, account: "Roth IRA");

        Assert.Equal(2, await db.Holdings.CountAsync());
        Assert.Equal(2, await db.Accounts.CountAsync());
        // One symbol is monitored once no matter how many accounts hold it.
        Assert.Equal(1, await db.WatchlistSymbols.CountAsync());
    }

    [Fact]
    public async Task Upsert_DetachesRowFromEarlierImport()
    {
        await using var db = NewContext();

        var import = new PortfolioImport { FileName = "positions.csv", ImportedAt = _time.GetUtcNow() };
        db.PortfolioImports.Add(import);
        var account = new Account { Name = "Brokerage", CreatedAt = _time.GetUtcNow() };
        db.Accounts.Add(account);
        await db.SaveChangesAsync();

        db.Holdings.Add(new Holding
        {
            AccountId = account.Id,
            Symbol = "MSFT",
            Quantity = 1m,
            PositionType = PositionType.Equity,
            IsMonitorable = true,
            FirstSeenAt = _time.GetUtcNow(),
            UpdatedAt = _time.GetUtcNow(),
            LastImportId = import.Id
        });
        await db.SaveChangesAsync();

        await AddAsync(db, "MSFT", 9m, account: "Brokerage");

        var holding = await db.Holdings.SingleAsync();
        Assert.Equal(9m, holding.Quantity);
        // A hand-edited position is no longer attributable to the file it came from.
        Assert.Null(holding.LastImportId);
    }

    [Theory]
    [InlineData(null, "symbol is required")]
    [InlineData("", "symbol is required")]
    [InlineData("   ", "symbol is required")]
    public async Task Upsert_RequiresSymbol(string? symbol, string expected)
    {
        await using var db = NewContext();

        var ex = await Assert.ThrowsAsync<ManualHoldingValidationException>(
            () => AddAsync(db, symbol));

        Assert.Contains(expected, ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(db.Holdings);
    }

    [Theory]
    [InlineData("FCASH**")]
    [InlineData("SPAXX**")]
    [InlineData("NOT A TICKER")]
    [InlineData("123456789012345")]
    public async Task Upsert_RejectsNonTradableSymbols(string symbol)
    {
        await using var db = NewContext();

        var ex = await Assert.ThrowsAsync<ManualHoldingValidationException>(
            () => AddAsync(db, symbol));

        Assert.Contains("not a tradable ticker", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(db.Holdings);
    }

    [Fact]
    public async Task Upsert_RequiresQuantity()
    {
        await using var db = NewContext();

        var ex = await Assert.ThrowsAsync<ManualHoldingValidationException>(
            () => AddAsync(db, "MSFT", null));

        Assert.Contains("shares is required", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-0.5)]
    public async Task Upsert_RejectsNonPositiveQuantity(decimal quantity)
    {
        await using var db = NewContext();

        var ex = await Assert.ThrowsAsync<ManualHoldingValidationException>(
            () => AddAsync(db, "MSFT", quantity));

        Assert.Contains("greater than zero", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(db.Holdings);
    }

    [Fact]
    public async Task Upsert_RejectsNegativeAverageCost()
    {
        await using var db = NewContext();

        var ex = await Assert.ThrowsAsync<ManualHoldingValidationException>(
            () => AddAsync(db, "MSFT", 10m, -5m));

        Assert.Contains("cannot be negative", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Upsert_AllowsFractionalShares()
    {
        await using var db = NewContext();

        await AddAsync(db, "MSFT", 0.4213m, 100m);

        Assert.Equal(0.4213m, (await db.Holdings.SingleAsync()).Quantity);
    }

    [Fact]
    public async Task Upsert_LeavesCostBasisUnknown_WhenAverageCostOmitted()
    {
        await using var db = NewContext();

        await AddAsync(db, "MSFT", 10m, averageCost: null);

        var holding = await db.Holdings.SingleAsync();
        Assert.Null(holding.AverageCostBasis);
        Assert.Null(holding.CostBasisTotal);
    }

    [Fact]
    public async Task Upsert_KeepsExistingCost_WhenUpdatingSharesOnly()
    {
        await using var db = NewContext();

        await AddAsync(db, "MSFT", 10m, 100m, "Brokerage");
        // Correcting the share count must not wipe a cost basis the user already entered.
        await AddAsync(db, "MSFT", 25m, averageCost: null, account: "Brokerage");

        var holding = await db.Holdings.SingleAsync();
        Assert.Equal(25m, holding.Quantity);
        Assert.Equal(100m, holding.AverageCostBasis);
        // Total is recalculated against the new share count.
        Assert.Equal(2500m, holding.CostBasisTotal);
    }

    [Fact]
    public async Task Upsert_OverwritesCost_WhenExplicitlySupplied()
    {
        await using var db = NewContext();

        await AddAsync(db, "MSFT", 10m, 100m, "Brokerage");
        await AddAsync(db, "MSFT", 10m, 150m, "Brokerage");

        var holding = await db.Holdings.SingleAsync();
        Assert.Equal(150m, holding.AverageCostBasis);
        Assert.Equal(1500m, holding.CostBasisTotal);
    }

    [Fact]
    public async Task Delete_RemovesHolding_ButKeepsWatchingTheSymbol()
    {
        await using var db = NewContext();

        var added = await AddAsync(db, "MSFT", 10m);
        var removed = await Service(db).DeleteAsync(added.HoldingId, CancellationToken.None);

        Assert.True(removed);
        Assert.Empty(db.Holdings);
        // Selling out of a position is not a reason to stop monitoring the ticker.
        Assert.Equal(1, await db.WatchlistSymbols.CountAsync());
    }

    [Fact]
    public async Task Delete_ReturnsFalse_ForUnknownId()
    {
        await using var db = NewContext();

        Assert.False(await Service(db).DeleteAsync(4242, CancellationToken.None));
    }

    private sealed class FakeTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
