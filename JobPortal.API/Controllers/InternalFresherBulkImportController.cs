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
    IOptions<FresherAcquisitionOptions> options) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType(typeof(FresherBulkImportResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<FresherBulkImportResponse>> Import(
        [FromBody] FresherBulkImportRequest request, CancellationToken cancellationToken)
    {
        var maximum = Math.Clamp(options.Value.MaximumBatchSize, 1, 1000);
        if (request.Jobs.Count is < 1 || request.Jobs.Count > maximum)
            return StatusCode(StatusCodes.Status413PayloadTooLarge, new { code = "BATCH_SIZE_EXCEEDED", maximumBatchSize = maximum });
        try { return Ok(await importer.ImportAsync(request, cancellationToken)); }
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
