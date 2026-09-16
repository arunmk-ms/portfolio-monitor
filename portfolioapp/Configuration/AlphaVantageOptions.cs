using System.ComponentModel.DataAnnotations;

namespace PortfolioApp.Configuration;

/// <summary>
/// Settings for the Alpha Vantage market-data provider.
/// Bound from configuration section "AlphaVantage" (app settings use "AlphaVantage__ApiKey" etc.).
/// </summary>
public sealed class AlphaVantageOptions
{
    public const string SectionName = "AlphaVantage";

    [Required(AllowEmptyStrings = false, ErrorMessage = "Set the 'AlphaVantage__ApiKey' application setting.")]
    public string ApiKey { get; set; } = string.Empty;

    [Required]
    [Url]
    public string BaseUrl { get; set; } = "https://www.alphavantage.co/";

    /// <summary>Comma-separated tickers to monitor, for example "MSFT,NVDA".</summary>
    [Required(AllowEmptyStrings = false)]
    public string Symbols { get; set; } = "MSFT,NVDA";

    [Range(1, 300)]
    public int RequestTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Pause between symbol requests. Alpha Vantage throttles per minute, so quotes are
    /// fetched sequentially rather than in a burst.
    /// </summary>
    [Range(0, 60_000)]
    public int DelayBetweenRequestsMilliseconds { get; set; } = 1_000;

    public IReadOnlyList<string> GetSymbols() =>
        Symbols.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(symbol => symbol.ToUpperInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
}
