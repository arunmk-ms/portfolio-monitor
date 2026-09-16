using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PortfolioApp.Configuration;
using PortfolioApp.Data;

namespace PortfolioApp.Services;

/// <summary>Supplies the tickers the monitoring run should fetch.</summary>
public interface IWatchlistProvider
{
    Task<IReadOnlyList<string>> GetSymbolsAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Resolves monitored symbols from the imported watchlist, falling back to the configured
/// symbol list while the portfolio is still empty (fresh deployment, before any upload).
/// </summary>
public sealed class WatchlistProvider : IWatchlistProvider
{
    private readonly PortfolioDbContext _db;
    private readonly AlphaVantageOptions _options;
    private readonly ILogger<WatchlistProvider> _logger;

    public WatchlistProvider(
        PortfolioDbContext db,
        IOptions<AlphaVantageOptions> options,
        ILogger<WatchlistProvider> logger)
    {
        _db = db;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<string>> GetSymbolsAsync(CancellationToken cancellationToken)
    {
        var symbols = await _db.WatchlistSymbols
            .Where(w => w.IsEnabled)
            .OrderBy(w => w.Symbol)
            .Select(w => w.Symbol)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (symbols.Count > 0)
        {
            return symbols;
        }

        var configured = _options.GetSymbols();
        _logger.LogInformation(
            "Watchlist is empty; falling back to the {Count} configured symbol(s). Upload a positions file to monitor your portfolio instead.",
            configured.Count);

        return configured;
    }
}
