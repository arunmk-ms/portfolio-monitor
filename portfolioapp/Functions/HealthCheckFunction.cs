using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PortfolioApp.Configuration;
using PortfolioApp.Data;
using PortfolioApp.Services;

namespace PortfolioApp.Functions;

/// <summary>
/// Readiness probe. Without this the only way to confirm a deployment is wired correctly is to
/// wait for a timer tick, which can be an entire trading session away.
/// </summary>
/// <remarks>
/// Anonymous so platform probes can reach it. It reports only whether dependencies resolve —
/// never connection strings, keys, or other configuration values.
/// </remarks>
public sealed class HealthCheckFunction
{
    private readonly PortfolioDbContext _db;
    private readonly IMarketCalendar _marketCalendar;
    private readonly IWatchlistProvider _watchlist;
    private readonly MarketHoursOptions _marketHours;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<HealthCheckFunction> _logger;

    public HealthCheckFunction(
        PortfolioDbContext db,
        IMarketCalendar marketCalendar,
        IWatchlistProvider watchlist,
        IOptions<MarketHoursOptions> marketHours,
        TimeProvider timeProvider,
        ILogger<HealthCheckFunction> logger)
    {
        _db = db;
        _marketCalendar = marketCalendar;
        _watchlist = watchlist;
        _marketHours = marketHours.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    [Function(nameof(HealthCheckFunction))]
    public async Task<IActionResult> RunAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "health")] HttpRequest request,
        CancellationToken cancellationToken)
    {
        var databaseReachable = false;
        var pendingMigrations = Array.Empty<string>();
        string? databaseError = null;

        try
        {
            databaseReachable = await _db.Database.CanConnectAsync(cancellationToken).ConfigureAwait(false);

            if (databaseReachable)
            {
                // A deployed build whose migrations were never applied looks healthy until the
                // first write fails, so surface that here instead.
                pendingMigrations = (await _db.Database
                    .GetPendingMigrationsAsync(cancellationToken)
                    .ConfigureAwait(false)).ToArray();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Health check could not reach the database.");
            databaseError = ex.GetType().Name;
        }

        var status = _marketCalendar.GetStatus(_timeProvider.GetUtcNow());
        var healthy = databaseReachable && pendingMigrations.Length == 0;

        // Only query application tables once migrations are known to be applied. Querying them
        // on an un-migrated database throws, which would turn this probe into a 500 and hide the
        // very diagnosis it exists to report.
        int? monitoredSymbolCount = null;
        if (healthy)
        {
            try
            {
                monitoredSymbolCount = (await _watchlist.GetSymbolsAsync(cancellationToken).ConfigureAwait(false)).Count;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Health check could not read the watchlist.");
                healthy = false;
                databaseError ??= ex.GetType().Name;
            }
        }

        var body = new
        {
            status = healthy ? "healthy" : "unhealthy",
            database = new
            {
                reachable = databaseReachable,
                pendingMigrations,
                error = databaseError
            },
            market = new
            {
                guardEnabled = _marketHours.Enabled,
                isOpen = status.IsOpen,
                reason = status.Reason.ToString(),
                exchangeLocalTime = status.LocalTime.ToString("yyyy-MM-dd HH:mm")
            },
            monitoredSymbolCount
        };

        return new ObjectResult(body)
        {
            StatusCode = healthy ? (int)HttpStatusCode.OK : (int)HttpStatusCode.ServiceUnavailable
        };
    }
}
