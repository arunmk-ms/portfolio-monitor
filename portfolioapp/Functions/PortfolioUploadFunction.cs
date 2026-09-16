using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using PortfolioApp.Services;

namespace PortfolioApp.Functions;

/// <summary>
/// Accepts a broker positions export from the web UI and turns it into accounts, holdings, and
/// monitored symbols.
/// </summary>
public sealed class PortfolioUploadFunction
{
    /// <summary>Guards against oversized uploads; a positions export is a few KB at most.</summary>
    private const long MaxUploadBytes = 5 * 1024 * 1024;

    private readonly IPortfolioImportService _importService;
    private readonly ILogger<PortfolioUploadFunction> _logger;

    public PortfolioUploadFunction(
        IPortfolioImportService importService,
        ILogger<PortfolioUploadFunction> logger)
    {
        _importService = importService;
        _logger = logger;
    }

    [Function(nameof(PortfolioUploadFunction))]
    public async Task<IActionResult> RunAsync(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "portfolio/import")] HttpRequest request,
        CancellationToken cancellationToken)
    {
        var (content, fileName) = await ResolveUploadAsync(request).ConfigureAwait(false);

        if (content is null)
        {
            return new BadRequestObjectResult(new
            {
                error = "Upload a CSV positions export as multipart form-data ('file') or as the raw request body."
            });
        }

        if (content.Length > MaxUploadBytes)
        {
            return new ObjectResult(new { error = "The uploaded file is too large." })
            {
                StatusCode = (int)HttpStatusCode.RequestEntityTooLarge
            };
        }

        try
        {
            var result = await _importService
                .ImportAsync(content, fileName, cancellationToken)
                .ConfigureAwait(false);

            return new OkObjectResult(new
            {
                importId = result.ImportId,
                positionsImported = result.PositionsImported,
                cashRowsSkipped = result.CashRowsSkipped,
                symbolsAddedToWatchlist = result.SymbolsAddedToWatchlist
            });
        }
        catch (InvalidDataException ex)
        {
            // The file was readable but isn't a positions export: a client error, not a fault.
            _logger.LogWarning(ex, "Rejected portfolio upload {FileName}: unrecognised format.", fileName);
            return new BadRequestObjectResult(new { error = ex.Message });
        }
        finally
        {
            await content.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Buffers the upload from either a multipart form field or the raw body. Buffering to memory
    /// keeps the parser able to seek, and the size cap above bounds the cost.
    /// </summary>
    private static async Task<(Stream? Content, string FileName)> ResolveUploadAsync(HttpRequest request)
    {
        if (request.HasFormContentType)
        {
            var form = await request.ReadFormAsync().ConfigureAwait(false);
            var file = form.Files.GetFile("file") ?? form.Files.FirstOrDefault();

            if (file is not null && file.Length > 0)
            {
                var buffer = new MemoryStream();
                await file.CopyToAsync(buffer).ConfigureAwait(false);
                buffer.Position = 0;
                return (buffer, file.FileName);
            }

            return (null, string.Empty);
        }

        var body = new MemoryStream();
        await request.Body.CopyToAsync(body).ConfigureAwait(false);

        if (body.Length == 0)
        {
            await body.DisposeAsync().ConfigureAwait(false);
            return (null, string.Empty);
        }

        body.Position = 0;
        var name = request.Query["fileName"].FirstOrDefault() ?? "positions.csv";
        return (body, name);
    }
}
