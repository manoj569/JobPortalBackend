namespace JobPortal.Application.Features.FresherAcquisition;

public interface IDurableFresherImportProcessor
{
    Task<FresherImportStatus> StageAsync(FresherBulkImportRequest request, CancellationToken cancellationToken);
    Task<bool> ProcessNextAsync(IFresherBulkImportService importer, CancellationToken cancellationToken);
}
