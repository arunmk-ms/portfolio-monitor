using PortfolioApp.Models;

namespace PortfolioApp.Services;

/// <summary>Outcome of persisting one monitoring run's quotes.</summary>
/// <param name="Saved">Snapshots written to the store.</param>
/// <param name="SkippedUnchanged">
/// Quotes identical to the symbol's last stored snapshot, so they were not written.
/// </param>
public readonly record struct SnapshotWriteResult(int Saved, int SkippedUnchanged);

/// <summary>
/// Persists price snapshots. Abstracted so the backing store (currently Azure SQL) can be
/// swapped without touching the monitoring function.
/// </summary>
public interface IQuoteStore
{
    Task<SnapshotWriteResult> SaveQuotesAsync(
        IReadOnlyCollection<StockQuote> quotes,
        CancellationToken cancellationToken);
}
