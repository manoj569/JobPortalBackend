using JobPortal.Application.Abstractions.Jobs;
using JobPortal.Application.Abstractions.Persistence;
using JobPortal.Application.Features.JobAggregation;
using JobPortal.Domain.Entities;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;

namespace JobPortal.Application.Services;

public sealed class JobSourceCategoryResolver(
    IOptionsMonitor<JobAggregationOptions> options,
    ICategoryManagementRepository categories,
    IExternalJobCategoryClassifier? classifier = null,
    ILogger<JobSourceCategoryResolver>? logger = null) : IJobSourceCategoryResolver, IJobSourceCategoryRunCache
{
    private static readonly Action<ILogger, Guid, string, Exception?> CategoryMissing =
        LoggerMessage.Define<Guid, string>(LogLevel.Information, new EventId(4385, nameof(CategoryMissing)),
            "JobSourceCategoryResolution Source={JobSourceId} ReasonCode={ReasonCode}");
    private static readonly Action<ILogger, Guid, string, int, Exception?> CategorySummary =
        LoggerMessage.Define<Guid, string, int>(LogLevel.Information, new EventId(4388, nameof(CategorySummary)),
            "JobSourceCategorySummary Source={JobSourceId} ReasonCode={ReasonCode} Count={Count}");
    private readonly Dictionary<(Guid Source, string Reason), int> _missing = [];
    private bool _running;
    private readonly HashSet<Guid> _validCategories = [];
    public void BeginRun() { _missing.Clear(); _running = true; _validCategories.Clear(); _categoryOptions = null; }
    public void EndRun()
    {
        if (logger is not null)
            foreach (var (key, count) in _missing) CategorySummary(logger, key.Source, key.Reason, count, null);
        _missing.Clear(); _running = false; _validCategories.Clear(); _categoryOptions = null;
    }
    private async Task<bool> ExistsAsync(Guid id, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (_running && _validCategories.Contains(id)) return true;
        var exists = await categories.ExistsAsync(id, token);
        if (_running && exists) _validCategories.Add(id);
        return exists;
    }
    private IReadOnlyCollection<JobPortal.Application.Features.AdminManagement.AdminOptionResponse>? _categoryOptions;
    public async Task<Guid?> ResolveCategoryIdAsync(JobSource source, RawExternalJob rawJob, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(rawJob);
        cancellationToken.ThrowIfCancellationRequested();
        if (rawJob.CategoryId is { } explicitId && explicitId != Guid.Empty &&
            await ExistsAsync(explicitId, cancellationToken)) return explicitId;

        var key = ExternalJobNormalizer.NormalizeText(rawJob.ExternalCategory);
        if (key is not null)
        {
            // Conflicting keys after normalization are ambiguous: fail closed to source fallback.
            var values = options.CurrentValue.CategoryMappings
                .Where(x => string.Equals(ExternalJobNormalizer.NormalizeText(x.Key), key, StringComparison.OrdinalIgnoreCase))
                .Select(x => x.Value).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (values.Length == 1 && Guid.TryParse(values[0], out var mappedId) && mappedId != Guid.Empty &&
                await ExistsAsync(mappedId, cancellationToken)) return mappedId;
        }
        var classified = classifier?.Classify(rawJob);
        if (classified is not null)
        {
            _categoryOptions ??= await categories.GetOptionsAsync(cancellationToken);
            var matches = _categoryOptions.Where(c => ExternalJobCategoryClassifier.MatchesCategory(classified, c.Slug, c.Name)).ToArray();
            // Prefer the canonical slug; otherwise reuse a unique existing equivalent category.
            var exact = matches.Where(c => string.Equals(c.Slug, classified, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (exact.Length == 1) return exact[0].Id;
            // Reuse an unambiguous taxonomy match; an unknown category stays absent.
            if (matches.Length == 1) return matches[0].Id;
            MissingCategory(source, matches.Length == 0 ? "ClassifiedCategoryMissing" : "ClassifiedCategoryAmbiguous");
            return null;
        }
        return await ResolveCategoryIdAsync(source, cancellationToken);
    }

    public async Task<Guid?> ResolveCategoryIdAsync(JobSource source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        cancellationToken.ThrowIfCancellationRequested();
        if (source.Id == Guid.Empty) return null;
        if (!options.CurrentValue.SourceCategories.TryGetValue(source.Id.ToString("D"), out var value))
        {
            MissingCategory(source, "SourceMappingMissing");
            return null;
        }
        if (!Guid.TryParse(value, out var categoryId) || categoryId == Guid.Empty)
        {
            MissingCategory(source, "SourceMappingInvalid");
            return null;
        }
        if (await ExistsAsync(categoryId, cancellationToken)) return categoryId;
        MissingCategory(source, "SourceCategoryNotFound");
        return null;
    }

    private void MissingCategory(JobSource source, string reason)
    {
        if (_running) _missing[(source.Id, reason)] = _missing.GetValueOrDefault((source.Id, reason)) + 1;
        else if (logger is not null) CategoryMissing(logger, source.Id, reason, null);
    }
}
