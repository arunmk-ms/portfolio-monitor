using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PortfolioApp.Data;
using PortfolioApp.Services;

namespace PortfolioApp.Functions;

/// <summary>
/// Write APIs for positions entered by hand, for people who want to track a few holdings without
/// exporting a file from their broker.
/// </summary>
public sealed class HoldingsApiFunction
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IManualHoldingService _holdings;
    private readonly PortfolioDbContext _db;
    private readonly ILogger<HoldingsApiFunction> _logger;

    public HoldingsApiFunction(
        IManualHoldingService holdings,
        PortfolioDbContext db,
        ILogger<HoldingsApiFunction> logger)
    {
        _holdings = holdings;
        _db = db;
        _logger = logger;
    }

    /// <summary>Adds a position, or updates the shares on one that already exists.</summary>
    [Function(nameof(CreateHolding))]
    public async Task<IActionResult> CreateHolding(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "holdings")] HttpRequest request,
        CancellationToken cancellationToken)
    {
        HoldingRequest? body;
        try
        {
            body = await JsonSerializer
                .DeserializeAsync<HoldingRequest>(request.Body, JsonOptions, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (JsonException ex)
        {
            return new BadRequestObjectResult(new { error = $"Malformed JSON: {ex.Message}" });
        }

        if (body is null)
        {
            return new BadRequestObjectResult(new { error = "A holding body is required." });
        }

        try
        {
            var result = await _holdings
                .UpsertAsync(body.Symbol, body.Quantity, body.AverageCost, body.Account, body.Description, cancellationToken)
                .ConfigureAwait(false);

            return new ObjectResult(new
            {
                holdingId = result.HoldingId,
                symbol = result.Symbol,
                account = result.AccountName,
                created = result.Created,
                addedToWatchlist = result.AddedToWatchlist
            })
            {
                StatusCode = result.Created ? (int)HttpStatusCode.Created : (int)HttpStatusCode.OK
            };
        }
        catch (ManualHoldingValidationException ex)
        {
            // Expected user error, not a fault: report it verbatim so the form can show it.
            _logger.LogInformation("Rejected manual holding: {Reason}", ex.Message);
            return new BadRequestObjectResult(new { error = ex.Message });
        }
    }

    /// <summary>Removes a position, so a mistyped entry can be corrected.</summary>
    [Function(nameof(DeleteHolding))]
    public async Task<IActionResult> DeleteHolding(
        [HttpTrigger(AuthorizationLevel.Function, "delete", Route = "holdings/{id:int}")] HttpRequest request,
        int id,
        CancellationToken cancellationToken)
    {
        var removed = await _holdings.DeleteAsync(id, cancellationToken).ConfigureAwait(false);

        return removed
            ? new OkObjectResult(new { holdingId = id, removed = true })
            : new NotFoundObjectResult(new { error = $"No holding with id {id}." });
    }

    /// <summary>
    /// Lists positions with enough detail to manage them, including whether each was imported or
    /// typed in.
    /// </summary>
    [Function(nameof(ListManageableHoldings))]
    public async Task<IActionResult> ListManageableHoldings(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "holdings/manage")] HttpRequest request,
        CancellationToken cancellationToken)
    {
        var rows = await _db.Holdings
            .AsNoTracking()
            .Include(h => h.Account)
            .OrderBy(h => h.Account!.Name).ThenBy(h => h.Symbol)
            .Select(h => new
            {
                h.Id,
                h.Symbol,
                h.Description,
                Account = h.Account!.Name,
                h.Quantity,
                AverageCost = h.AverageCostBasis,
                PositionType = h.PositionType.ToString(),
                h.IsMonitorable,
                IsManual = h.LastImportId == null,
                SourceFile = h.LastImport!.FileName,
                h.UpdatedAt
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new OkObjectResult(rows);
    }

    private sealed record HoldingRequest(
        string? Symbol,
        decimal? Quantity,
        decimal? AverageCost,
        string? Account,
        string? Description);
}
