namespace PortfolioApp.Models;

/// <summary>
/// Audit record for one uploaded positions file. Keeping this separate from
/// <see cref="Holding"/> answers "where did this position come from, and when" after the fact,
/// and makes a bad import traceable.
/// </summary>
public sealed class PortfolioImport
{
    public int Id { get; set; }

    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// The broker's own "Date downloaded" stamp from the file footer, when present. This is the
    /// as-of time for the position data, which can be materially older than <see cref="ImportedAt"/>.
    /// </summary>
    public DateTimeOffset? SourceDownloadedAt { get; set; }

    public DateTimeOffset ImportedAt { get; set; }

    /// <summary>Data rows read from the file, excluding headers, blanks, and disclaimer text.</summary>
    public int RowsParsed { get; set; }

    public int PositionsImported { get; set; }

    /// <summary>Rows recognised but deliberately not stored as monitorable positions (e.g. cash).</summary>
    public int RowsSkipped { get; set; }

    public ICollection<Holding> Holdings { get; set; } = new List<Holding>();
}
