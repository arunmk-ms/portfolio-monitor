using PortfolioApp.Models;

namespace PortfolioApp.Services;

public interface IAlphaVantageClient
{
    /// <summary>Fetches the latest GLOBAL_QUOTE for a single ticker.</summary>
    /// <exception cref="AlphaVantageException">The provider returned an error, a throttle notice, or an empty quote.</exception>
    Task<StockQuote> GetQuoteAsync(string symbol, CancellationToken cancellationToken);
}
