using JobPortal.Application.Features.AdminImports;

namespace JobPortal.Application.Abstractions.AdminImports;

public interface IAdminImportService
{
    Task<CsvImportResult> PreviewCompaniesAsync(
        CsvImportFile file,
        CancellationToken cancellationToken = default);

    Task<CsvImportResult> CommitCompaniesAsync(
        Guid administratorUserId,
        CsvImportFile file,
        CancellationToken cancellationToken = default);

    Task<CsvImportResult> PreviewJobsAsync(
        CsvImportFile file,
        CancellationToken cancellationToken = default);

    Task<CsvImportResult> CommitJobsAsync(
        Guid administratorUserId,
        CsvImportFile file,
        CancellationToken cancellationToken = default);

    CsvImportTemplate GetCompaniesTemplate();

    CsvImportTemplate GetJobsTemplate();
}
