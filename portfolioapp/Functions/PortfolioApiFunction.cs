using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PortfolioApp.Data;
using PortfolioApp.Models;

namespace PortfolioApp.Functions;

/// <summary>
/// Read APIs backing the web UI, plus rule management.
/// </summary>
/// <remarks>
/// DESIGN.md requires the UI to read through this API and never call the market-data provider
/// directly, so every view the dashboard needs is served from stored data here.
/// </remarks>
public sealed class PortfolioApiFunction
{
    private static readonly JsonSerializerOptions FactsOptions = new(JsonSerializerDefaults.Web);

    private readonly PortfolioDbContext _db;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<PortfolioApiFunction> _logger;

    public PortfolioApiFunction(
        PortfolioDbContext db,
        TimeProvider timeProvider,
        ILogger<PortfolioApiFunction> logger)
    {
        _db = db;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>Open signals with the facts that produced them, newest first.</summary>
    [Function(nameof(GetSignals))]
    public async Task<IActionResult> GetSignals(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "signals")] HttpRequest request,
        CancellationToken cancellationToken)
    {
        var includeAll = string.Equals(request.Query["all"], "true", StringComparison.OrdinalIgnoreCase);

        var query = _db.Signals.AsNoTracking();
        if (!includeAll)
        {
            query = query.Where(s => s.Status == SignalStatus.Open);
        }

        var signals = await query
            .OrderByDescending(s => s.Id)
            .Take(200)
            .Select(s => new
            {
                s.Id,
                s.Symbol,
                Direction = s.Direction.ToString(),
                Status = s.Status.ToString(),
                s.Score,
                s.RuleVersion,
                RuleName = s.WatchRule!.Name,
                s.CreatedAt,
                s.ResolvedAt,
                s.FactsJson
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // Facts are stored as a JSON string; re-hydrate so clients get an object, not a string.
        var payload = signals.Select(s => new
        {
            s.Id,
            s.Symbol,
            s.Direction,
            s.Status,
            s.Score,
            s.RuleVersion,
            s.RuleName,
            s.CreatedAt,
            s.ResolvedAt,
            Facts = ParseFacts(s.FactsJson)
        });

        return new OkObjectResult(payload);
    }

    /// <summary>Holdings with their latest observed price and allocation.</summary>
    [Function(nameof(GetHoldings))]
    public async Task<IActionResult> GetHoldings(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "holdings")] HttpRequest request,
        CancellationToken cancellationToken)
    {
        var holdings = await _db.Holdings
            .AsNoTracking()
            .Include(h => h.Account)
            .OrderBy(h => h.Symbol)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var symbols = holdings.Select(h => h.Symbol).Distinct(StringComparer.Ordinal).ToList();

        // Latest snapshot per symbol, by insertion order (portable across providers).
        var latest = await _db.PriceSnapshots
            .AsNoTracking()
            .Where(s => symbols.Contains(s.Symbol))
            .GroupBy(s => s.Symbol)
            .Select(g => new { Symbol = g.Key, Id = g.Max(s => s.Id) })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var latestIds = latest.Select(l => l.Id).ToList();
        var prices = await _db.PriceSnapshots
            .AsNoTracking()
            .Where(s => latestIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Symbol, s => s, StringComparer.Ordinal, cancellationToken)
            .ConfigureAwait(false);

        var rows = holdings.Select(h =>
        {
            prices.TryGetValue(h.Symbol, out var snapshot);
            var price = snapshot?.Price ?? h.LastPrice;
            var marketValue = h.Quantity.HasValue && price.HasValue ? h.Quantity * price : h.CurrentValue;

            return new
            {
                h.Symbol,
                h.Description,
                Account = h.Account?.Name,
                h.Quantity,
                h.AverageCostBasis,
                LatestPrice = price,
                MarketValue = marketValue,
                PositionType = h.PositionType.ToString(),
                h.IsMonitorable,
                PriceAsOf = snapshot?.CapturedAt,
                LatestTradingDay = snapshot?.LatestTradingDay
            };
        }).ToList();

        var total = rows.Sum(r => r.MarketValue ?? 0m);

        return new OkObjectResult(new
        {
            totalMarketValue = total,
            holdings = rows.Select(r => new
            {
                r.Symbol,
                r.Description,
                r.Account,
                r.Quantity,
                r.AverageCostBasis,
                r.LatestPrice,
                r.MarketValue,
                r.PositionType,
                r.IsMonitorable,
                r.PriceAsOf,
                r.LatestTradingDay,
                AllocationPercent = total > 0m && r.MarketValue.HasValue
                    ? Math.Round(r.MarketValue.Value / total * 100m, 4)
                    : (decimal?)null
            })
        });
    }

    [Function(nameof(GetRules))]
    public async Task<IActionResult> GetRules(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "rules")] HttpRequest request,
        CancellationToken cancellationToken)
    {
        var rules = await _db.WatchRules
            .AsNoTracking()
            .OrderBy(r => r.Symbol).ThenBy(r => r.Name)
            .Select(r => new
            {
                r.Id,
                r.Name,
                r.Symbol,
                RuleType = r.RuleType.ToString(),
                r.Threshold,
                r.Period,
                Direction = r.Direction.ToString(),
                r.IsEnabled,
                r.Version,
                r.UpdatedAt
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new OkObjectResult(rules);
    }

    /// <summary>Creates a watch rule, or updates one in place when <c>id</c> is supplied.</summary>
    [Function(nameof(SaveRule))]
    public async Task<IActionResult> SaveRule(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "rules")] HttpRequest request,
        CancellationToken cancellationToken)
    {
        RuleRequest? body;
        try
        {
            body = await JsonSerializer
                .DeserializeAsync<RuleRequest>(request.Body, FactsOptions, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (JsonException ex)
        {
            return new BadRequestObjectResult(new { error = $"Malformed JSON: {ex.Message}" });
        }

        if (body is null)
        {
            return new BadRequestObjectResult(new { error = "A rule body is required." });
        }

        if (string.IsNullOrWhiteSpace(body.Name))
        {
            return new BadRequestObjectResult(new { error = "'name' is required." });
        }

        if (!Enum.TryParse<RuleType>(body.RuleType, ignoreCase: true, out var ruleType))
        {
            return new BadRequestObjectResult(new
            {
                error = $"Unknown 'ruleType'. Valid values: {string.Join(", ", Enum.GetNames<RuleType>())}."
            });
        }

        if (!Enum.TryParse<SignalDirection>(body.Direction, ignoreCase: true, out var direction))
        {
            return new BadRequestObjectResult(new
            {
                error = $"Unknown 'direction'. Valid values: {string.Join(", ", Enum.GetNames<SignalDirection>())}."
            });
        }

        var now = _timeProvider.GetUtcNow();
        WatchRule rule;

        if (body.Id is { } id and > 0)
        {
            var existing = await _db.WatchRules
                .FirstOrDefaultAsync(r => r.Id == id, cancellationToken)
                .ConfigureAwait(false);

            if (existing is null)
            {
                return new NotFoundObjectResult(new { error = $"No rule with id {id}." });
            }

            rule = existing;

            // Bump the version whenever evaluated behaviour changes, so already-raised signals
            // stay attributable to the thresholds that actually produced them.
            var behaviourChanged = rule.RuleType != ruleType
                || rule.Threshold != body.Threshold
                || rule.Period != body.Period
                || rule.Symbol != body.Symbol;

            if (behaviourChanged)
            {
                rule.Version++;
            }
        }
        else
        {
            rule = new WatchRule { CreatedAt = now, Version = 1 };
            _db.WatchRules.Add(rule);
        }

        rule.Name = body.Name.Trim();
        rule.Symbol = string.IsNullOrWhiteSpace(body.Symbol) ? null : body.Symbol.Trim().ToUpperInvariant();
        rule.RuleType = ruleType;
        rule.Threshold = body.Threshold;
        rule.Period = body.Period;
        rule.Direction = direction;
        rule.IsEnabled = body.IsEnabled ?? true;
        rule.UpdatedAt = now;

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Saved watch rule {RuleId} '{Name}' v{Version} ({RuleType} {Threshold}).",
            rule.Id,
            rule.Name,
            rule.Version,
            rule.RuleType,
            rule.Threshold);

        return new ObjectResult(new
        {
            rule.Id,
            rule.Name,
            rule.Symbol,
            RuleType = rule.RuleType.ToString(),
            rule.Threshold,
            rule.Period,
            Direction = rule.Direction.ToString(),
            rule.IsEnabled,
            rule.Version
        })
        {
            StatusCode = body.Id is > 0 ? (int)HttpStatusCode.OK : (int)HttpStatusCode.Created
        };
    }

    /// <summary>Acknowledges or ignores a signal, the review actions DESIGN.md calls for.</summary>
    [Function(nameof(UpdateSignalStatus))]
    public async Task<IActionResult> UpdateSignalStatus(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "signals/{id:int}/status")] HttpRequest request,
        int id,
        CancellationToken cancellationToken)
    {
        var requested = request.Query["status"].FirstOrDefault();

        if (!Enum.TryParse<SignalStatus>(requested, ignoreCase: true, out var status)
            || status is not (SignalStatus.Acknowledged or SignalStatus.Ignored))
        {
            return new BadRequestObjectResult(new
            {
                error = "Provide ?status=Acknowledged or ?status=Ignored. Open and Resolved are set by the engine."
            });
        }

        var signal = await _db.Signals
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken)
            .ConfigureAwait(false);

        if (signal is null)
        {
            return new NotFoundObjectResult(new { error = $"No signal with id {id}." });
        }

        signal.Status = status;

        var now = _timeProvider.GetUtcNow();
        var alerts = await _db.Alerts
            .Where(a => a.SignalId == id && a.AcknowledgedAt == null)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var alert in alerts)
        {
            alert.AcknowledgedAt = now;
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new OkObjectResult(new { signal.Id, Status = signal.Status.ToString() });
    }

    private static JsonElement? ParseFacts(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<JsonElement>(json, FactsOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record RuleRequest(
        int? Id,
        string? Name,
        string? Symbol,
        string? RuleType,
        decimal Threshold,
        int? Period,
        string? Direction,
        bool? IsEnabled);
}
