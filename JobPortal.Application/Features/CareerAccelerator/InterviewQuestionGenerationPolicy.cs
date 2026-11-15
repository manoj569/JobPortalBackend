using System.Text.Json;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Domain.Entities.CareerAccelerator;

namespace JobPortal.Application.Features.CareerAccelerator;

public static class InterviewQuestionGenerationPolicy
{
    private static readonly HashSet<string> AllowedFields =
        ["QuestionText", "QuestionType", "Difficulty", "CompetencyIds", "FollowUpEligible", "GenerationMetadata"];

    public static GeneratedInterviewQuestionPayload Parse(string json, AcceleratorInterviewVersion version,
        AcceleratorInterviewSection section, AcceleratorInterviewQuestion source)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > 16000)
            throw Invalid("Generated question output is missing or too large.");
        GeneratedInterviewQuestionPayload? payload;
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                document.RootElement.EnumerateObject().Any(x => !AllowedFields.Contains(x.Name)) ||
                document.RootElement.EnumerateObject().Select(x => x.Name).Distinct().Count() != AllowedFields.Count)
                throw Invalid("Generated question output contains unknown or missing fields.");
            payload = JsonSerializer.Deserialize<GeneratedInterviewQuestionPayload>(json);
        }
        catch (JsonException) { throw Invalid("Generated question output is invalid JSON."); }
        if (payload is null || string.IsNullOrWhiteSpace(payload.QuestionText) || payload.QuestionText.Length > 12000 ||
            payload.QuestionType != source.Type || payload.Difficulty != version.Difficulty ||
            payload.CompetencyIds is null || payload.CompetencyIds.Count is < 1 or > 4 ||
            payload.CompetencyIds.Distinct().Count() != payload.CompetencyIds.Count ||
            payload.CompetencyIds.Any(x => !version.Competencies.Any(c => c.CompetencyId == x)) ||
            source.CompetencyId.HasValue && !payload.CompetencyIds.Contains(source.CompetencyId.Value) ||
            payload.FollowUpEligible && (section.MaxFollowUpsPerQuestion == 0 || string.IsNullOrWhiteSpace(source.FollowUpPrompt)) ||
            payload.GenerationMetadata is null || payload.GenerationMetadata.Length > 2000)
            throw Invalid("Generated question violates the published interview blueprint.");
        try
        {
            using var metadata = JsonDocument.Parse(payload.GenerationMetadata);
            if (metadata.RootElement.ValueKind != JsonValueKind.Object)
                throw Invalid("Generation metadata must be a JSON object.");
        }
        catch (JsonException) { throw Invalid("Generation metadata is invalid JSON."); }
        return payload;
    }

    private static ConflictException Invalid(string message) => new(message, "accelerator_generated_question_invalid");
}
