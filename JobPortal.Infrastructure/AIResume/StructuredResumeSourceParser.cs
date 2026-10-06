using System.Text.Json;
using JobPortal.Application.Abstractions.Candidates;
using JobPortal.Application.Features.AIResume;
using JobPortal.Domain.Entities;

namespace JobPortal.Infrastructure.AIResume;

public sealed class StructuredResumeSourceParser(IResumeStorage storage, IResumeTextExtractor textExtractor) : IAIResumeSourceParser
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<TailoredResumeContent> ParseAsync(User candidate, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (candidate.ResumeProfile is null || candidate.ResumeStorageKey is null || candidate.ResumeFileName is null)
            throw new InvalidDataException("Candidate resume source is unavailable.");
        await using var resume = await storage.OpenReadAsync(candidate.Id, candidate.ResumeStorageKey,
            candidate.ResumeProfile.Id, candidate.ResumeFileName, candidate.ResumeContentType, ct)
            ?? throw new ResumeStorageObjectNotFoundException();
        var extractedText = await textExtractor.ExtractAsync(resume, Path.GetExtension(candidate.ResumeFileName), ct);
        // The owned upload is authoritative. Profile keywords/tables are not a substitute for its facts.
        var source = UploadedResumeFactParser.Parse(extractedText);
        if (JsonSerializer.Serialize(source, Json).Length > 40_000)
            throw new InvalidDataException("Candidate resume source is too large.");
        return source;
    }
}
