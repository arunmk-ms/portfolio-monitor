using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PortfolioApp.Data;
using PortfolioApp.Models;

namespace PortfolioApp.Services;

/// <param name="ImportId">Identifier of the audit row created for this upload.</param>
/// <param name="PositionsImported">Monitorable positions written or updated.</param>
/// <param name="CashRowsSkipped">Cash and informational rows recorded but not monitored.</param>
/// <param name="SymbolsAddedToWatchlist">Newly monitored tickers.</param>
public readonly record struct PortfolioImportResult(
    int ImportId,
    int PositionsImported,
    int CashRowsSkipped,
    int SymbolsAddedToWatchlist);

public interface IPortfolioImportService
{
    Task<PortfolioImportResult> ImportAsync(Stream csv, string fileName, CancellationToken cancellationToken);
}

/// <summary>
/// Turns an uploaded positions file into accounts, holdings, and monitored symbols.
/// </summary>
public sealed class PortfolioImportService : IPortfolioImportService
{
    private readonly PortfolioDbContext _db;
    private readonly IPositionsCsvParser _parser;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<PortfolioImportService> _logger;

    public PortfolioImportService(
        PortfolioDbContext db,
        IPositionsCsvParser parser,
        TimeProvider timeProvider,
        ILogger<PortfolioImportService> logger)
    {
        _db = db;
        _parser = parser;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<PortfolioImportResult> ImportAsync(
        Stream csv,
        string fileName,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(csv);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        var parsed = _parser.Parse(csv);
        var now = _timeProvider.GetUtcNow();

        var import = new PortfolioImport
        {
            // Store only the file name, never a full path: paths can carry user identifiers.
            FileName = Path.GetFileName(fileName),
            SourceDownloadedAt = parsed.DownloadedAt,
            ImportedAt = now,
            RowsParsed = parsed.Rows.Count
        };

        _db.PortfolioImports.Add(import);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var imported = 0;
        var cashSkipped = 0;
        var watchlistAdded = 0;

        foreach (var row in parsed.Rows)
        {
            var account = await GetOrCreateAccountAsync(row.AccountName, now, cancellationToken).ConfigureAwait(false);
            var positionType = SymbolClassifier.Classify(row.Symbol, row.Description);
            var monitorable = SymbolClassifier.IsMonitorable(positionType);

            var holding = await _db.Holdings
                .FirstOrDefaultAsync(h => h.AccountId == account.Id && h.Symbol == row.Symbol, cancellationToken)
                .ConfigureAwait(false);

            if (holding is null)
            {
                holding = new Holding
                {
                    AccountId = account.Id,
                    Symbol = row.Symbol,
                    FirstSeenAt = now
                };
                _db.Holdings.Add(holding);
            }

            holding.Description = row.Description;
            holding.Quantity = row.Quantity;
            holding.LastPrice = row.LastPrice;
            holding.CurrentValue = row.CurrentValue;
            holding.CostBasisTotal = row.CostBasisTotal;
            holding.AverageCostBasis = row.AverageCostBasis;
            holding.PositionType = positionType;
            holding.IsMonitorable = monitorable;
            holding.UpdatedAt = now;
            holding.LastImportId = import.Id;

            if (monitorable)
            {
                imported++;
                if (await EnsureWatchlistedAsync(row.Symbol, now, cancellationToken).ConfigureAwait(false))
                {
                    watchlistAdded++;
                }
            }
            else
            {
                cashSkipped++;
                _logger.LogDebug(
                    "{Symbol} classified as {PositionType}; stored as a position but excluded from quote monitoring.",
                    row.Symbol,
                    positionType);
            }
        }

        import.PositionsImported = imported;
        import.RowsSkipped = parsed.SkippedRowCount + cashSkipped;

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Imported {FileName}: {Imported} monitorable position(s), {CashSkipped} cash/other row(s), {WatchlistAdded} new watchlist symbol(s).",
            import.FileName,
            imported,
            cashSkipped,
            watchlistAdded);

        return new PortfolioImportResult(import.Id, imported, cashSkipped, watchlistAdded);
    }

    private async Task<Account> GetOrCreateAccountAsync(
        string name,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var account = await _db.Accounts
            .FirstOrDefaultAsync(a => a.Name == name, cancellationToken)
            .ConfigureAwait(false);

        if (account is not null)
        {
            return account;
        }

        account = new Account { Name = name, CreatedAt = now };
        _db.Accounts.Add(account);

        // Saved immediately so the generated key is available for holdings in this same import.
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return account;
    }

    /// <returns>True when the symbol was newly added to the watchlist.</returns>
    private async Task<bool> EnsureWatchlistedAsync(
        string symbol,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var existing = await _db.WatchlistSymbols
            .FirstOrDefaultAsync(w => w.Symbol == symbol, cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            existing.UpdatedAt = now;
            return false;
        }

        _db.WatchlistSymbols.Add(new WatchlistSymbol
        {
            Symbol = symbol,
            Source = WatchlistSource.Import,
            IsEnabled = true,
            AddedAt = now,
            UpdatedAt = now
        });

        return true;
    }
}
