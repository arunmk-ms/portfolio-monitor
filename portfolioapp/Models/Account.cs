namespace PortfolioApp.Models;

/// <summary>
/// A brokerage account holding positions.
/// </summary>
/// <remarks>
/// The broker's account *number* is deliberately never read or stored. Accounts are identified
/// by their display name only, so an exported file's sensitive identifiers never reach the
/// database, logs, or telemetry.
/// </remarks>
public sealed class Account
{
    public int Id { get; set; }

    /// <summary>Display name from the export, for example "Individual - TOD".</summary>
    public string Name { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public ICollection<Holding> Holdings { get; set; } = new List<Holding>();
}
