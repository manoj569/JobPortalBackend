using System.Security.Cryptography;
using System.Text;
using JobPortal.Application.Features.FresherAcquisition;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;

namespace JobPortal.API.Controllers;

[ApiController]
[Route("api/internal/v1/jobs/bulk-import")]
[ServiceFilter(typeof(FresherServiceTokenFilter))]
[ServiceFilter(typeof(FresherRequestLimitFilter))]
[EnableRateLimiting("FresherBulkImport")]
public sealed class InternalFresherBulkImportController(IFresherBulkImportService importer,
    IOptions<FresherAcquisitionOptions> options, IDurableFresherImportProcessor queue) : ControllerBase
{
    [HttpGet("status")]
    public async Task<IActionResult> Status([FromQuery] string sourceType, [FromQuery] string importId,
        [FromServices] IFresherAcquisitionStore store, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sourceType) || sourceType.Length > 80 || string.IsNullOrWhiteSpace(importId) || importId.Length > 100)
            return BadRequest(new { code = "INVALID_IMPORT_IDENTITY" });
        var status = await store.GetImportStatusAsync(sourceType.Trim().ToLowerInvariant(), importId, cancellationToken);
        return status is null ? NotFound(new { status = "NOT_FOUND", importId }) : Ok(status);
    }

    [HttpPost]
    [ProducesResponseType(typeof(FresherBulkImportResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(FresherImportStatus), StatusCodes.Status202Accepted)]
    public async Task<IActionResult> Import(
        [FromBody] FresherBulkImportRequest request, CancellationToken cancellationToken)
    {
        var maximum = Math.Clamp(options.Value.MaximumBatchSize, 1, 1000);
        if (request.Jobs.Count is < 1 || request.Jobs.Count > maximum)
            return StatusCode(StatusCodes.Status413PayloadTooLarge, new { code = "BATCH_SIZE_EXCEEDED", maximumBatchSize = maximum });
        try
        {
            if (!request.SourceType.Trim().Equals("talentd", StringComparison.OrdinalIgnoreCase))
                return Ok(await importer.ImportAsync(request, cancellationToken));
            if (request.Jobs.Count > 20)
                return StatusCode(StatusCodes.Status413PayloadTooLarge, new { code = "BATCH_SIZE_EXCEEDED", maximumBatchSize = 20 });
            var status = await queue.StageAsync(request, cancellationToken);
            return status.Status == "COMPLETED" ? Ok(status.Receipt) : Accepted(status);
        }
        catch (ArgumentException) { return BadRequest(new { code = "INVALID_DURABLE_BATCH" }); }
        catch (InvalidOperationException) { return StatusCode(503, new { code = "ACQUISITION_SOURCE_UNAVAILABLE" }); }
        catch (ImportIdPayloadConflictException)
        {
            return Conflict(new { code = "IMPORT_ID_PAYLOAD_CONFLICT" });
        }
        catch (ImportAlreadyRunningException)
        {
            return Conflict(new { code = "IMPORT_IN_PROGRESS" });
        }
    }
}

public sealed class FresherServiceTokenFilter(IConfiguration configuration) : IAsyncAuthorizationFilter
{
    public Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var configured = configuration["FresherAcquisition:ServiceToken"];
        if (string.IsNullOrWhiteSpace(configured) || Encoding.UTF8.GetByteCount(configured) < 32)
        {
            context.Result = new StatusCodeResult(StatusCodes.Status503ServiceUnavailable);
            return Task.CompletedTask;
        }
        var header = context.HttpContext.Request.Headers.Authorization.ToString();
        const string prefix = "Bearer ";
        if (!header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            context.Result = new UnauthorizedResult();
            return Task.CompletedTask;
        }
        var supplied = Encoding.UTF8.GetBytes(header[prefix.Length..].Trim());
        var expected = Encoding.UTF8.GetBytes(configured);
        var valid = supplied.Length == expected.Length && CryptographicOperations.FixedTimeEquals(supplied, expected);
        if (!valid) context.Result = new UnauthorizedResult();
        return Task.CompletedTask;
    }
}

public sealed class FresherRequestLimitFilter(IOptions<FresherAcquisitionOptions> options) : IAsyncResourceFilter
{
    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        var maxBytes = Math.Clamp(options.Value.MaximumRequestBytes, 64_000, 10_000_000);
        var feature = context.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (feature is { IsReadOnly: false }) feature.MaxRequestBodySize = maxBytes;
        if (context.HttpContext.Request.ContentLength is { } contentLength && contentLength > maxBytes)
        {
            context.Result = new ObjectResult(new { code = "REQUEST_TOO_LARGE", maximumBytes = maxBytes })
            { StatusCode = StatusCodes.Status413PayloadTooLarge };
            return;
        }
        await next();
    }
}
