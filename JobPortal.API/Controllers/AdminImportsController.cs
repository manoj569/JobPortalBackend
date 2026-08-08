using System.ComponentModel.DataAnnotations;
using JobPortal.API.Extensions;
using JobPortal.Application.Abstractions.AdminImports;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Application.Features.AdminImports;
using JobPortal.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JobPortal.API.Controllers;

[ApiController]
[Authorize(Roles = "Administrator")]
[Route("api/admin/imports")]
[Produces("application/json")]
public sealed class AdminImportsController(IAdminImportService imports) :
    ControllerBase
{
    private const long MaximumRequestSize =
        AdminImportLimits.MaximumFileSizeBytes + (1024 * 1024);

    [HttpPost("companies/preview")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaximumRequestSize)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaximumRequestSize)]
    public async Task<ActionResult<ApiResponse<CsvImportResult>>> PreviewCompanies(
        [FromForm] CsvUploadForm upload,
        CancellationToken cancellationToken) =>
        Ok(new ApiResponse<CsvImportResult>(
            await WithFileAsync(
                upload,
                imports.PreviewCompaniesAsync,
                cancellationToken)));

    [HttpPost("companies/commit")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaximumRequestSize)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaximumRequestSize)]
    public async Task<ActionResult<ApiResponse<CsvImportResult>>> CommitCompanies(
        [FromForm] CsvUploadForm upload,
        CancellationToken cancellationToken)
    {
        var administratorUserId = User.GetRequiredUserId();
        var result = await WithFileAsync(
            upload,
            (file, token) => imports.CommitCompaniesAsync(
                administratorUserId,
                file,
                token),
            cancellationToken);
        var response = new ApiResponse<CsvImportResult>(
            result,
            result.InvalidRows == 0
                ? "Company CSV import completed."
                : "Company CSV import rejected; correct all invalid rows and upload the file again.");
        return result.InvalidRows == 0 ? Ok(response) : BadRequest(response);
    }

    [HttpPost("jobs/preview")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaximumRequestSize)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaximumRequestSize)]
    public async Task<ActionResult<ApiResponse<CsvImportResult>>> PreviewJobs(
        [FromForm] CsvUploadForm upload,
        CancellationToken cancellationToken) =>
        Ok(new ApiResponse<CsvImportResult>(
            await WithFileAsync(
                upload,
                imports.PreviewJobsAsync,
                cancellationToken)));

    [HttpPost("jobs/commit")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaximumRequestSize)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaximumRequestSize)]
    public async Task<ActionResult<ApiResponse<CsvImportResult>>> CommitJobs(
        [FromForm] CsvUploadForm upload,
        CancellationToken cancellationToken)
    {
        var administratorUserId = User.GetRequiredUserId();
        var result = await WithFileAsync(
            upload,
            (file, token) => imports.CommitJobsAsync(administratorUserId, file, token),
            cancellationToken);
        var response = new ApiResponse<CsvImportResult>(
            result,
            result.InvalidRows == 0
                ? "Job CSV import completed."
                : "Job CSV import rejected; correct all invalid rows and upload the file again.");
        return result.InvalidRows == 0 ? Ok(response) : BadRequest(response);
    }

    [HttpGet("templates/companies")]
    [Produces("text/csv")]
    public FileContentResult CompaniesTemplate()
    {
        var template = imports.GetCompaniesTemplate();
        return File(template.Content, template.ContentType, template.FileName);
    }

    [HttpGet("templates/jobs")]
    [Produces("text/csv")]
    public FileContentResult JobsTemplate()
    {
        var template = imports.GetJobsTemplate();
        return File(template.Content, template.ContentType, template.FileName);
    }

    private static async Task<CsvImportResult> WithFileAsync(
        CsvUploadForm upload,
        Func<CsvImportFile, CancellationToken, Task<CsvImportResult>> operation,
        CancellationToken cancellationToken)
    {
        if (upload.File is null)
            throw new BadRequestException("A CSV file is required.", "invalid_csv");
        await using var stream = upload.File.OpenReadStream();
        return await operation(
            new(upload.File.FileName, upload.File.Length, stream),
            cancellationToken);
    }
}

public sealed class CsvUploadForm
{
    [Required]
    public IFormFile? File { get; init; }
}
