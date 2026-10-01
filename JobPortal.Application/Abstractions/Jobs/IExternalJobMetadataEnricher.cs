namespace JobPortal.Application.Abstractions.Jobs;

public interface IExternalJobMetadataEnricher
{
    RawExternalJob Enrich(RawExternalJob normalizedJob);
}

public interface IExternalJobCategoryClassifier
{
    // Returns a taxonomy slug, never an invented database ID.
    string? Classify(RawExternalJob normalizedJob);
}
