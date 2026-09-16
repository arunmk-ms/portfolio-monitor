using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.Extensions.Logging;

namespace PortfolioApp.Services;

/// <summary>
/// Reads a Fidelity "Portfolio Positions" CSV export.
/// </summary>
/// <remarks>
/// The file is a display export, not a data feed: after the position rows it contains blank
/// filler rows, multi-line legal disclaimers, and a "Date downloaded" footer. Parsing therefore
/// scans for usable rows rather than assuming every line is a record.
/// <para>
/// The "Account number" column is never read. Only the account *name* is retained, so account
/// numbers cannot reach the database, logs, or telemetry.
/// </para>
/// </remarks>
public sealed class FidelityPositionsCsvParser : IPositionsCsvParser
{
    private const string AccountNameHeader = "Account name";
    private const string SymbolHeader = "Symbol";

    private readonly ILogger<FidelityPositionsCsvParser> _logger;

    public FidelityPositionsCsvParser(ILogger<FidelityPositionsCsvParser> logger)
    {
        _logger = logger;
    }

    public PositionsFile Parse(Stream csv)
    {
        ArgumentNullException.ThrowIfNull(csv);

        var configuration = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            // The trailing disclaimer rows have fewer populated columns than the header.
            MissingFieldFound = null,
            BadDataFound = null,
            HasHeaderRecord = true,
            TrimOptions = TrimOptions.Trim,
            DetectDelimiter = false
        };

        using var reader = new StreamReader(csv, leaveOpen: true);
        using var parser = new CsvReader(reader, configuration);

        if (!parser.Read() || !parser.ReadHeader())
        {
            throw new InvalidDataException("The positions file is empty or has no header row.");
        }

        var header = parser.HeaderRecord
            ?? throw new InvalidDataException("The positions file has no readable header row.");

        if (!header.Any(column => string.Equals(column?.Trim(), SymbolHeader, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException(
                $"The positions file has no '{SymbolHeader}' column. Columns found: {string.Join(", ", header)}.");
        }

        var rows = new List<PositionRow>();
        DateTimeOffset? downloadedAt = null;
        var skipped = 0;

        while (parser.Read())
        {
            var symbol = Field(parser, SymbolHeader);
            var accountName = Field(parser, AccountNameHeader);

            if (string.IsNullOrWhiteSpace(symbol) || string.IsNullOrWhiteSpace(accountName))
            {
                // Blank filler, disclaimer prose, or the download footer. The footer stamp is
                // useful metadata, so probe the first column for it before discarding the row.
                downloadedAt ??= BrokerageValueParser.ParseDownloadedAt(parser.GetField(0));
                skipped++;
                continue;
            }

            rows.Add(new PositionRow(
                AccountName: accountName.Trim(),
                Symbol: SymbolClassifier.Normalize(symbol),
                Description: Field(parser, "Description")?.Trim(),
                Quantity: BrokerageValueParser.ParseQuantity(Field(parser, "Quantity")),
                LastPrice: BrokerageValueParser.ParseMoney(Field(parser, "Last price")),
                CurrentValue: BrokerageValueParser.ParseMoney(Field(parser, "Current value")),
                CostBasisTotal: BrokerageValueParser.ParseMoney(Field(parser, "Cost basis total")),
                AverageCostBasis: BrokerageValueParser.ParseMoney(Field(parser, "Average cost basis"))));
        }

        _logger.LogInformation(
            "Parsed {RowCount} position row(s) from the positions file; skipped {SkippedCount} non-position row(s).",
            rows.Count,
            skipped);

        return new PositionsFile(rows, downloadedAt, skipped);
    }

    /// <summary>Reads a column by name, tolerating exports that omit optional columns.</summary>
    private static string? Field(IReaderRow row, string name) =>
        row.TryGetField<string>(name, out var value) ? value : null;
}
