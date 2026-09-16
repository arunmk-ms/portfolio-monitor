using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PortfolioApp.Data;
using PortfolioApp.Models;

namespace PortfolioApp.Services;

/// <summary>
/// Persists price snapshots to the relational store via EF Core.
/// </summary>
public sealed class SqlQuoteStore : IQuoteStore
{
    private readonly PortfolioDbContext _db;
    private readonly ILogger<SqlQuoteStore> _logger;

    public SqlQuoteStore(PortfolioDbContext db, ILogger<SqlQuoteStore> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<SnapshotWriteResult> SaveQuotesAsync(
        IReadOnlyCollection<StockQuote> quotes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(quotes);

        if (quotes.Count == 0)
        {
            return new SnapshotWriteResult(0, 0);
        }

        var saved = 0;
        var skipped = 0;

        foreach (var quote in quotes)
        {
            // One indexed lookup per symbol. The symbol count is small (single digits), so this
            // stays cheaper and far simpler than a window-function query over the whole table.
            // Ordering by the identity column gives "the row most recently written for this
            // symbol", which is exactly what the dedup check needs, and unlike ordering by
            // CapturedAt it translates on every provider (SQLite can't sort a DateTimeOffset).
            var previous = await _db.PriceSnapshots
                .Where(s => s.Symbol == quote.Symbol)
                .OrderByDescending(s => s.Id)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            if (IsUnchanged(previous, quote))
            {
                skipped++;
                _logger.LogDebug(
                    "{Symbol}: quote unchanged since {PreviousCapturedAt:O}; not storing a duplicate snapshot.",
                    quote.Symbol,
                    previous!.CapturedAt);
                continue;
            }

            _db.PriceSnapshots.Add(PriceSnapshot.FromQuote(quote));
            saved++;
        }

        if (saved > 0)
        {
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return new SnapshotWriteResult(saved, skipped);
    }

    /// <summary>
    /// Alpha Vantage's free tier serves delayed (often end-of-day) data, so polling every five
    /// minutes returns the identical quote repeatedly. Storing those duplicates would bloat the
    /// table and, worse, corrupt indicator math — a 20-period moving average over 20 copies of
    /// the same price is meaningless. Price, volume, and trading day together identify a quote.
    /// </summary>
    internal static bool IsUnchanged(PriceSnapshot? previous, StockQuote quote) =>
        previous is not null
        && previous.Price == quote.Price
        && previous.Volume == quote.Volume
        && previous.LatestTradingDay == quote.LatestTradingDay;
}
