using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PortfolioApp.Data;
using PortfolioApp.Models;

namespace PortfolioApp.Services;

/// <param name="Symbol">Normalised ticker.</param>
/// <param name="Created">True when a new position was added, false when an existing one changed.</param>
/// <param name="AddedToWatchlist">True when the symbol became monitored as a result.</param>
public readonly record struct ManualHoldingResult(
    int HoldingId,
    string Symbol,
    string AccountName,
    bool Created,
    bool AddedToWatchlist);

/// <summary>Raised when a manual entry is not usable; the message is safe to show the user.</summary>
public sealed class ManualHoldingValidationException : Exception
{
    public ManualHoldingValidationException(string message) : base(message)
    {
    }
}

public interface IManualHoldingService
{
    Task<ManualHoldingResult> UpsertAsync(
        string? symbol,
        decimal? quantity,
        decimal? averageCost,
        string? accountName,
        string? description,
        CancellationToken cancellationToken);

    Task<bool> DeleteAsync(int holdingId, CancellationToken cancellationToken);
}

/// <summary>
/// Adds and removes positions entered by hand.
/// </summary>
/// <remarks>
/// Deliberately mirrors <see cref="PortfolioImportService"/>: same (account, symbol) upsert key,
/// same symbol classification, same watchlist side effect. A position typed in must behave
/// identically to one imported, or the dashboard and the monitoring run would treat them
/// differently for no reason the user could see.
/// <para>
/// Manual rows are stored with a null <see cref="Holding.LastImportId"/> rather than a synthetic
/// import record, so the import audit trail keeps meaning only what it says.
/// </para>
/// </remarks>
public sealed class ManualHoldingService : IManualHoldingService
{
    /// <summary>Fallback account so the user is not forced to name one for a simple entry.</summary>
    public const string DefaultAccountName = "Manual";

    private const int MaxAccountNameLength = 128;
    private const int MaxDescriptionLength = 256;

    private readonly PortfolioDbContext _db;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ManualHoldingService> _logger;

    public ManualHoldingService(
        PortfolioDbContext db,
        TimeProvider timeProvider,
        ILogger<ManualHoldingService> logger)
    {
        _db = db;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<ManualHoldingResult> UpsertAsync(
        string? symbol,
        decimal? quantity,
        decimal? averageCost,
        string? accountName,
        string? description,
        CancellationToken cancellationToken)
    {
        var ticker = SymbolClassifier.Normalize(symbol);

        if (ticker.Length == 0)
        {
            throw new ManualHoldingValidationException("A stock symbol is required.");
        }

        var positionType = SymbolClassifier.Classify(ticker, description);
        if (positionType != PositionType.Equity)
        {
            // Cash sweeps have no quotable ticker; accepting one by hand would add a row the
            // monitoring run can never price.
            throw new ManualHoldingValidationException(
                $"'{ticker}' is not a tradable ticker. Enter an exchange symbol such as MSFT.");
        }

        if (quantity is not { } shares)
        {
            throw new ManualHoldingValidationException("Number of shares is required.");
        }

        if (shares <= 0m)
        {
            throw new ManualHoldingValidationException("Number of shares must be greater than zero.");
        }

        if (averageCost is < 0m)
        {
            throw new ManualHoldingValidationException("Average cost cannot be negative.");
        }

        var name = string.IsNullOrWhiteSpace(accountName) ? DefaultAccountName : accountName.Trim();
        if (name.Length > MaxAccountNameLength)
        {
            throw new ManualHoldingValidationException(
                $"Account name is longer than {MaxAccountNameLength} characters.");
        }

        var trimmedDescription = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        if (trimmedDescription is { Length: > MaxDescriptionLength })
        {
            trimmedDescription = trimmedDescription[..MaxDescriptionLength];
        }

        var now = _timeProvider.GetUtcNow();
        var account = await GetOrCreateAccountAsync(name, now, cancellationToken).ConfigureAwait(false);

        var holding = await _db.Holdings
            .FirstOrDefaultAsync(h => h.AccountId == account.Id && h.Symbol == ticker, cancellationToken)
            .ConfigureAwait(false);

        var created = holding is null;

        if (holding is null)
        {
            holding = new Holding
            {
                AccountId = account.Id,
                Symbol = ticker,
                FirstSeenAt = now
            };
            _db.Holdings.Add(holding);
        }

        holding.Quantity = shares;

        // A blank average cost means "not tracked", not "erase what I told you earlier". Someone
        // correcting a share count would otherwise silently destroy their own cost basis.
        if (averageCost.HasValue)
        {
            holding.AverageCostBasis = averageCost;
        }

        holding.CostBasisTotal = holding.AverageCostBasis.HasValue
            ? holding.AverageCostBasis.Value * shares
            : null;

        holding.PositionType = positionType;
        holding.IsMonitorable = SymbolClassifier.IsMonitorable(positionType);
        holding.UpdatedAt = now;

        // Entering a position by hand supersedes whatever an earlier file said about it, so the
        // row stops being attributed to that import.
        holding.LastImportId = null;

        if (trimmedDescription is not null)
        {
            holding.Description = trimmedDescription;
        }

        // A manually entered position has no market price until the monitoring run fetches one.
        // Leave LastPrice and CurrentValue untouched rather than deriving a value from cost,
        // which would show a fabricated market value on the dashboard.

        var addedToWatchlist = await EnsureWatchlistedAsync(ticker, now, cancellationToken).ConfigureAwait(false);

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "{Action} manual holding {Symbol} ({Shares} share(s)) in account '{Account}'.",
            created ? "Created" : "Updated",
            ticker,
            shares,
            name);

        return new ManualHoldingResult(holding.Id, ticker, name, created, addedToWatchlist);
    }

    public async Task<bool> DeleteAsync(int holdingId, CancellationToken cancellationToken)
    {
        var holding = await _db.Holdings
            .FirstOrDefaultAsync(h => h.Id == holdingId, cancellationToken)
            .ConfigureAwait(false);

        if (holding is null)
        {
            return false;
        }

        _db.Holdings.Remove(holding);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // The watchlist entry is intentionally left in place: selling out of a position is not a
        // reason to stop watching the symbol, and the user can disable it explicitly.
        _logger.LogInformation("Removed holding {HoldingId} ({Symbol}).", holdingId, holding.Symbol);
        return true;
    }

    private async Task<Account> GetOrCreateAccountAsync(
        string name,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var account = await _db.Accounts
            .FirstOrDefaultAsync(a => a.Name == name, cancellationToken)
            .ConfigureAwait(false);

        if (account is not null)
        {
            return account;
        }

        account = new Account { Name = name, CreatedAt = now };
        _db.Accounts.Add(account);

        // Saved immediately so the generated key is available for the holding below.
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return account;
    }

    /// <returns>True when the symbol was newly added to the watchlist.</returns>
    private async Task<bool> EnsureWatchlistedAsync(
        string symbol,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var existing = await _db.WatchlistSymbols
            .FirstOrDefaultAsync(w => w.Symbol == symbol, cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            existing.UpdatedAt = now;
            return false;
        }

        _db.WatchlistSymbols.Add(new WatchlistSymbol
        {
            Symbol = symbol,
            Source = WatchlistSource.Manual,
            IsEnabled = true,
            AddedAt = now,
            UpdatedAt = now
        });

        return true;
    }
}
