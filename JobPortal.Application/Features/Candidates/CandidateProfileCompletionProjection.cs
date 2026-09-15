using System.Text.Json;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;

namespace JobPortal.Application.Features.Candidates;

public static class CandidateProfileCompletionProjection
{
    public static CandidateProfileCompletionResponse Create(
        User user, bool hasSkills, bool hasEducation, bool hasEmployment)
    {
        var experienced = user.WorkStatus == CandidateWorkStatus.Experienced ||
            !user.WorkStatus.HasValue && (user.CareerStage == CareerStage.Experienced || hasEmployment);
        var sections = new[]
        {
            Step("BasicDetails", experienced ? 15 : 25,
                !string.IsNullOrWhiteSpace(user.FirstName) &&
                !string.IsNullOrWhiteSpace(user.LastName) &&
                !string.IsNullOrWhiteSpace(user.Headline) &&
                user.WorkStatus.HasValue && !string.IsNullOrWhiteSpace(user.CurrentCountry) &&
                !string.IsNullOrWhiteSpace(user.CurrentCity ?? user.Location)),
            Step("ProfileSummary", 15, !string.IsNullOrWhiteSpace(user.Bio)),
            Step("Skills", 15, hasSkills),
            Step("CareerPreferences", 20, CareerPreferencesComplete(user)),
            Step("Education", 10, hasEducation),
            Step("Employment", experienced ? 10 : 0, hasEmployment),
            Step("Resume", 15, !string.IsNullOrWhiteSpace(user.ResumeStorageKey))
        };
        var applicable = sections.Where(x => x.Weight > 0).ToArray();
        var missing = applicable.Where(x => !x.IsCompleted).ToArray();
        return new(applicable.Where(x => x.IsCompleted).Sum(x => x.Weight),
            applicable.Where(x => x.IsCompleted).Select(x => x.Section).ToArray(),
            missing.Select(x => x.Section).ToArray(), sections,
            missing.FirstOrDefault()?.Section);
    }

    private static ProfileSectionCompletionResponse Step(string name, int weight, bool completed) =>
        new(name, weight, completed);

    private static bool CareerPreferencesComplete(User user) =>
        Values<string>(user.PreferredJobRolesJson).Length > 0 &&
        Values<string>(user.PreferredCitiesJson).Length > 0 ||
        user.OnboardingCompletedAtUtc.HasValue && user.CareerStage.HasValue &&
        !string.IsNullOrWhiteSpace(user.Location) &&
        Values<DesiredOpportunity>(user.DesiredOpportunitiesJson).Length > 0 &&
        Values<WorkPreference>(user.WorkPreferencesJson).Length > 0;

    private static T[] Values<T>(string json)
    {
        try { return JsonSerializer.Deserialize<T[]>(json) ?? []; }
        catch (JsonException) { return []; }
    }
}
