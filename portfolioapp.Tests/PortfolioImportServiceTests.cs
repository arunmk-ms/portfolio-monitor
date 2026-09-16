using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PortfolioApp.Configuration;
using PortfolioApp.Data;
using PortfolioApp.Models;
using PortfolioApp.Services;
using Xunit;

namespace PortfolioApp.Tests;

/// <summary>
/// Exercises the import pipeline against the real Fidelity export format, including its
/// disclaimer rows, blank filler lines, accounting-style negatives, and cash sweep position.
/// </summary>
public sealed class PortfolioImportServiceTests : IDisposable
{
    private static readonly string SampleFile =
        Path.Combine(AppContext.BaseDirectory, "TestData", "Portfolio_Positions_Sep-09-2026.csv");

    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<PortfolioDbContext> _options;
    private readonly DateTimeOffset _now = new(2026, 9, 10, 12, 0, 0, TimeSpan.Zero);

    public PortfolioImportServiceTests()
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

    private static FidelityPositionsCsvParser Parser() =>
        new(NullLogger<FidelityPositionsCsvParser>.Instance);

    private PortfolioImportService Service(PortfolioDbContext context) =>
        new(context, Parser(), new FakeTimeProvider(_now), NullLogger<PortfolioImportService>.Instance);

    private async Task<PortfolioImportResult> ImportSampleAsync(string? content = null)
    {
        await using var context = NewContext();
        await using Stream stream = content is null
            ? File.OpenRead(SampleFile)
            : new MemoryStream(Encoding.UTF8.GetBytes(content));

        return await Service(context).ImportAsync(stream, "Portfolio_Positions_Sep-09-2026.csv", CancellationToken.None);
    }

    [Fact]
    public void SampleFileExists() => Assert.True(File.Exists(SampleFile), $"Missing test asset: {SampleFile}");

    [Fact]
    public async Task Import_StoresOnlyTradableSecuritiesAsMonitorable()
    {
        var result = await ImportSampleAsync();

        // XYZ and ABC are equities; FCASH** is a cash sweep and must not be monitored.
        Assert.Equal(2, result.PositionsImported);
        Assert.Equal(1, result.CashRowsSkipped);
        Assert.Equal(2, result.SymbolsAddedToWatchlist);
    }

    [Fact]
    public async Task Import_NeverSendsCashSweepToTheWatchlist()
    {
        await ImportSampleAsync();

        await using var context = NewContext();
        var watched = await context.WatchlistSymbols.Select(w => w.Symbol).ToListAsync();

        Assert.Equal(new[] { "ABC", "XYZ" }, watched.OrderBy(s => s).ToArray());
        Assert.DoesNotContain("FCASH**", watched);
    }

    [Fact]
    public async Task Import_RecordsCashPositionWithoutMonitoringIt()
    {
        await ImportSampleAsync();

        await using var context = NewContext();
        var cash = await context.Holdings.SingleAsync(h => h.Symbol == "FCASH**");

        Assert.Equal(PositionType.Cash, cash.PositionType);
        Assert.False(cash.IsMonitorable);
        Assert.Equal(600.75m, cash.CurrentValue);
        Assert.Null(cash.Quantity);
    }

    [Fact]
    public async Task Import_ParsesAccountingNegativesAndThousandsSeparators()
    {
        await ImportSampleAsync();

        await using var context = NewContext();
        var holding = await context.Holdings.SingleAsync(h => h.Symbol == "XYZ");

        Assert.Equal(550.541m, holding.Quantity);
        Assert.Equal(6.48m, holding.LastPrice);
        Assert.Equal(3567.50m, holding.CurrentValue);
        Assert.Equal(43141.37m, holding.CostBasisTotal);
        Assert.Equal(78.36m, holding.AverageCostBasis);
        Assert.Equal("XYZ INC", holding.Description);
    }

    [Fact]
    public async Task Import_CapturesBrokerDownloadTimestamp()
    {
        var result = await ImportSampleAsync();

        await using var context = NewContext();
        var import = await context.PortfolioImports.SingleAsync(i => i.Id == result.ImportId);

        Assert.NotNull(import.SourceDownloadedAt);
        Assert.Equal(new DateTime(2026, 9, 9, 23, 41, 0), import.SourceDownloadedAt!.Value.DateTime);
        Assert.Equal(_now, import.ImportedAt);
    }

    [Fact]
    public async Task Import_CreatesAccountFromNameOnly()
    {
        await ImportSampleAsync();

        await using var context = NewContext();
        var account = await context.Accounts.SingleAsync();

        Assert.Equal("Individual - TOD", account.Name);
    }

    /// <summary>
    /// The account-number column must never be persisted. This asserts on every text column of
    /// every table so a future mapping change can't quietly reintroduce it.
    /// </summary>
    [Fact]
    public async Task Import_DoesNotPersistAccountNumberAnywhere()
    {
        const string accountNumber = "Z12345678";
        var csv = await File.ReadAllTextAsync(SampleFile);

        // Populate the account-number column, which is blank in the redacted sample.
        csv = csv.Replace("\n,Individual - TOD,", $"\n{accountNumber},Individual - TOD,", StringComparison.Ordinal);
        Assert.Contains(accountNumber, csv, StringComparison.Ordinal);

        await ImportSampleAsync(csv);

        await using var context = NewContext();

        var stored = new List<string?>();
        stored.AddRange(await context.Accounts.Select(a => a.Name).ToListAsync());
        stored.AddRange(await context.PortfolioImports.Select(i => i.FileName).ToListAsync());
        stored.AddRange(await context.Holdings.Select(h => h.Symbol).ToListAsync());
        stored.AddRange(await context.Holdings.Select(h => h.Description).ToListAsync());
        stored.AddRange(await context.WatchlistSymbols.Select(w => w.Symbol).ToListAsync());

        Assert.DoesNotContain(stored, value => value is not null
            && value.Contains(accountNumber, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Import_IsIdempotentAcrossRepeatedUploads()
    {
        await ImportSampleAsync();
        var second = await ImportSampleAsync();

        await using var context = NewContext();

        // Re-uploading updates positions in place instead of duplicating them.
        Assert.Equal(3, await context.Holdings.CountAsync());
        Assert.Equal(1, await context.Accounts.CountAsync());
        Assert.Equal(2, await context.WatchlistSymbols.CountAsync());
        Assert.Equal(0, second.SymbolsAddedToWatchlist);

        // ...but each upload is still recorded for audit.
        Assert.Equal(2, await context.PortfolioImports.CountAsync());
    }

    [Fact]
    public async Task Import_UpdatesQuantityOnReupload()
    {
        await ImportSampleAsync();

        var updated = (await File.ReadAllTextAsync(SampleFile))
            .Replace(",XYZ,XYZ INC,550.541,", ",XYZ,XYZ INC,600.000,", StringComparison.Ordinal);
        await ImportSampleAsync(updated);

        await using var context = NewContext();
        var holding = await context.Holdings.SingleAsync(h => h.Symbol == "XYZ");

        Assert.Equal(600.000m, holding.Quantity);
        Assert.Equal(_now, holding.UpdatedAt);
    }

    [Fact]
    public async Task Import_SkipsDisclaimerAndBlankRows()
    {
        var result = await ImportSampleAsync();

        await using var context = NewContext();
        var import = await context.PortfolioImports.SingleAsync(i => i.Id == result.ImportId);

        // Three real positions parsed; the blanks, disclaimers, and footer are all discarded.
        Assert.Equal(3, import.RowsParsed);
        Assert.Equal(3, await context.Holdings.CountAsync());
    }

    [Fact]
    public async Task Import_RejectsFileWithoutSymbolColumn()
    {
        const string csv = "Foo,Bar\n1,2\n";

        await using var context = NewContext();
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));

        await Assert.ThrowsAsync<InvalidDataException>(
            () => Service(context).ImportAsync(stream, "bogus.csv", CancellationToken.None));
    }

    [Fact]
    public async Task Import_StoresFileNameWithoutDirectoryPath()
    {
        await using var context = NewContext();
        await using var stream = File.OpenRead(SampleFile);

        var result = await Service(context)
            .ImportAsync(stream, @"C:\Users\someone\Downloads\Portfolio_Positions.csv", CancellationToken.None);

        await using var verify = NewContext();
        var import = await verify.PortfolioImports.SingleAsync(i => i.Id == result.ImportId);

        Assert.Equal("Portfolio_Positions.csv", import.FileName);
        Assert.DoesNotContain("someone", import.FileName, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WatchlistProvider_ReturnsImportedSymbols()
    {
        await ImportSampleAsync();

        await using var context = NewContext();
        var provider = new WatchlistProvider(
            context,
            Options.Create(new AlphaVantageOptions { ApiKey = "k", Symbols = "MSFT,NVDA" }),
            NullLogger<WatchlistProvider>.Instance);

        var symbols = await provider.GetSymbolsAsync(CancellationToken.None);

        Assert.Equal(new[] { "ABC", "XYZ" }, symbols.ToArray());
    }

    [Fact]
    public async Task WatchlistProvider_FallsBackToConfigurationWhenNothingImported()
    {
        await using var context = NewContext();
        var provider = new WatchlistProvider(
            context,
            Options.Create(new AlphaVantageOptions { ApiKey = "k", Symbols = "MSFT,NVDA" }),
            NullLogger<WatchlistProvider>.Instance);

        var symbols = await provider.GetSymbolsAsync(CancellationToken.None);

        Assert.Equal(new[] { "MSFT", "NVDA" }, symbols.ToArray());
    }

    [Fact]
    public async Task WatchlistProvider_ExcludesDisabledSymbols()
    {
        await ImportSampleAsync();

        await using (var context = NewContext())
        {
            var entry = await context.WatchlistSymbols.SingleAsync(w => w.Symbol == "XYZ");
            entry.IsEnabled = false;
            await context.SaveChangesAsync();
        }

        await using var verify = NewContext();
        var provider = new WatchlistProvider(
            verify,
            Options.Create(new AlphaVantageOptions { ApiKey = "k", Symbols = "MSFT" }),
            NullLogger<WatchlistProvider>.Instance);

        Assert.Equal(new[] { "ABC" }, (await provider.GetSymbolsAsync(CancellationToken.None)).ToArray());
    }

    private sealed class FakeTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;

        public FakeTimeProvider(DateTimeOffset now) => _now = now;

        public override DateTimeOffset GetUtcNow() => _now;
    }
}
