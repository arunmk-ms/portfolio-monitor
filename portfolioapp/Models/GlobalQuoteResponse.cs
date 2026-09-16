using System.Text.Json.Serialization;

namespace PortfolioApp.Models;

/// <summary>
/// Raw shape of the Alpha Vantage GLOBAL_QUOTE endpoint. The service answers with HTTP 200
/// even for failures, signalling them through the Error Message / Note / Information fields.
/// </summary>
internal sealed class GlobalQuoteResponse
{
    [JsonPropertyName("Global Quote")]
    public GlobalQuotePayload? GlobalQuote { get; set; }

    [JsonPropertyName("Error Message")]
    public string? ErrorMessage { get; set; }

    /// <summary>Legacy throttling message (calls per minute).</summary>
    [JsonPropertyName("Note")]
    public string? Note { get; set; }

    /// <summary>Current throttling / plan-limit message (calls per day).</summary>
    [JsonPropertyName("Information")]
    public string? Information { get; set; }
}

internal sealed class GlobalQuotePayload
{
    [JsonPropertyName("01. symbol")]
    public string? Symbol { get; set; }

    [JsonPropertyName("02. open")]
    public string? Open { get; set; }

    [JsonPropertyName("03. high")]
    public string? High { get; set; }

    [JsonPropertyName("04. low")]
    public string? Low { get; set; }

    [JsonPropertyName("05. price")]
    public string? Price { get; set; }

    [JsonPropertyName("06. volume")]
    public string? Volume { get; set; }

    [JsonPropertyName("07. latest trading day")]
    public string? LatestTradingDay { get; set; }

    [JsonPropertyName("08. previous close")]
    public string? PreviousClose { get; set; }

    [JsonPropertyName("09. change")]
    public string? Change { get; set; }

    [JsonPropertyName("10. change percent")]
    public string? ChangePercent { get; set; }
}
