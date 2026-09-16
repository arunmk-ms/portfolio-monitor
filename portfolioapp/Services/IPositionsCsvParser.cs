namespace PortfolioApp.Services;

/// <summary>One position row parsed from a broker export. Deliberately has no account-number field.</summary>
public sealed record PositionRow(
    string AccountName,
    string Symbol,
    string? Description,
    decimal? Quantity,
    decimal? LastPrice,
    decimal? CurrentValue,
    decimal? CostBasisTotal,
    decimal? AverageCostBasis);

/// <param name="Rows">Position rows recognised in the file.</param>
/// <param name="DownloadedAt">The broker's "Date downloaded" stamp, when the file carries one.</param>
/// <param name="SkippedRowCount">Blank, disclaimer, and otherwise unusable rows.</param>
public sealed record PositionsFile(
    IReadOnlyList<PositionRow> Rows,
    DateTimeOffset? DownloadedAt,
    int SkippedRowCount);

public interface IPositionsCsvParser
{
    PositionsFile Parse(Stream csv);
}
