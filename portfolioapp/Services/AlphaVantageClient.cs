using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PortfolioApp.Configuration;
using PortfolioApp.Models;

namespace PortfolioApp.Services;

/// <summary>
/// Typed <see cref="HttpClient"/> over the Alpha Vantage GLOBAL_QUOTE endpoint.
/// </summary>
public sealed class AlphaVantageClient : IAlphaVantageClient
{
    /// <summary>Named-client key, also used to build the HttpClient logging category names.</summary>
    public const string HttpClientName = "AlphaVantage";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly AlphaVantageOptions _options;
    private readonly ILogger<AlphaVantageClient> _logger;
    private readonly TimeProvider _timeProvider;

    public AlphaVantageClient(
        HttpClient httpClient,
        IOptions<AlphaVantageOptions> options,
        ILogger<AlphaVantageClient> logger,
        TimeProvider timeProvider)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
        _timeProvider = timeProvider;
    }

    public async Task<StockQuote> GetQuoteAsync(string symbol, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);

        // The API key is only ever placed in the query string, never logged.
        var requestUri = $"query?function=GLOBAL_QUOTE&symbol={Uri.EscapeDataString(symbol)}&apikey={Uri.EscapeDataString(_options.ApiKey)}";

        _logger.LogDebug("Requesting GLOBAL_QUOTE for {Symbol}.", symbol);

        using var response = await _httpClient.GetAsync(requestUri, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content
            .ReadFromJsonAsync<GlobalQuoteResponse>(SerializerOptions, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new AlphaVantageException(symbol, "Alpha Vantage returned an empty response body.");

        return MapToQuote(symbol, payload, _timeProvider.GetUtcNow());
    }

    internal static StockQuote MapToQuote(string symbol, GlobalQuoteResponse payload, DateTimeOffset retrievedAt)
    {
        if (!string.IsNullOrWhiteSpace(payload.ErrorMessage))
        {
            throw new AlphaVantageException(symbol, $"Alpha Vantage rejected the request: {payload.ErrorMessage}");
        }

        // Throttling is reported as a plain informational message with HTTP 200.
        var throttleMessage = FirstNonEmpty(payload.Note, payload.Information);
        if (throttleMessage is not null)
        {
            throw new AlphaVantageException(symbol, $"Alpha Vantage rate limit reached: {throttleMessage}", isThrottled: true);
        }

        var quote = payload.GlobalQuote;
        if (quote is null || string.IsNullOrWhiteSpace(quote.Symbol))
        {
            throw new AlphaVantageException(symbol, $"Alpha Vantage returned no quote for '{symbol}'. The ticker may be unsupported.");
        }

        return new StockQuote(
            Symbol: quote.Symbol.Trim().ToUpperInvariant(),
            Price: ParseDecimal(quote.Price),
            Open: ParseDecimal(quote.Open),
            High: ParseDecimal(quote.High),
            Low: ParseDecimal(quote.Low),
            PreviousClose: ParseDecimal(quote.PreviousClose),
            Change: ParseDecimal(quote.Change),
            ChangePercent: ParseDecimal(quote.ChangePercent?.TrimEnd('%', ' ')),
            Volume: ParseLong(quote.Volume),
            LatestTradingDay: ParseDate(quote.LatestTradingDay),
            RetrievedAt: retrievedAt);
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private static decimal ParseDecimal(string? value) =>
        decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0m;

    private static long ParseLong(string? value) =>
        long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0L;

    private static DateOnly? ParseDate(string? value) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed
            : null;
}
