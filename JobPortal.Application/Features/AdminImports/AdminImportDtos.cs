namespace JobPortal.Application.Features.AdminImports;

public sealed record CsvImportFile(
    string FileName,
    long Length,
    Stream Content);

public sealed record CsvImportFieldError(
    string Field,
    string Message,
    string? SubmittedValue = null);

public sealed record CsvImportRowResult(
    int RowNumber,
    string Status,
    IReadOnlyCollection<CsvImportFieldError> Errors,
    string? CompanyResolution = null,
    string? CategoryResolution = null);

public sealed record CsvImportResult(
    int TotalRows,
    int ValidRows,
    int InvalidRows,
    int DuplicateRows,
    int ImportedRows,
    int SkippedRows,
    bool CanCommit,
    IReadOnlyCollection<CsvImportRowResult> Rows);

public sealed record CsvImportTemplate(
    string FileName,
    string ContentType,
    byte[] Content);
