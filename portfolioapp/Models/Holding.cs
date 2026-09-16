namespace PortfolioApp.Models;

/// <summary>What a row in a positions export represents.</summary>
public enum PositionType
{
    /// <summary>A tradable security with a quotable ticker.</summary>
    Equity = 0,

    /// <summary>Cash or a money-market sweep (for example Fidelity's "FCASH**", "SPAXX**").</summary>
    Cash = 1,

    /// <summary>Unsettled or informational rows such as "Pending Activity".</summary>
    Other = 2
}

/// <summary>
/// A current position in an account — the `Holding` entity from DESIGN.md. Rows are upserted on
/// each import, keyed by (account, symbol), so the table always reflects the latest upload.
/// </summary>
public sealed class Holding
{
    public int Id { get; set; }

    public int AccountId { get; set; }

    public Account? Account { get; set; }

    public string Symbol { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Share count. Brokers report fractional shares, so this is decimal, not integer.</summary>
    public decimal? Quantity { get; set; }

    public decimal? AverageCostBasis { get; set; }

    public decimal? CostBasisTotal { get; set; }

    public decimal? CurrentValue { get; set; }

    public decimal? LastPrice { get; set; }

    public PositionType PositionType { get; set; }

    /// <summary>
    /// False for cash and informational rows. The monitoring run must never send these to the
    /// quote provider: they have no ticker and would waste API quota on guaranteed errors.
    /// </summary>
    public bool IsMonitorable { get; set; }

    public DateTimeOffset FirstSeenAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>The import that last touched this row.</summary>
    public int LastImportId { get; set; }

    public PortfolioImport? LastImport { get; set; }
}
