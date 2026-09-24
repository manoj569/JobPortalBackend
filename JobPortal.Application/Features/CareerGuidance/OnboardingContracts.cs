using System.Text.Json;
using System.Text.Json.Serialization;
using JobPortal.Domain.Entities;

namespace JobPortal.Application.Features.CareerGuidance;

// Default(struct) means omitted; explicit JSON null is a specified clear operation.
[JsonConverter(typeof(OnboardingFieldConverterFactory))]
public readonly record struct OnboardingField<T>(T Value, bool IsSpecified = true);

public sealed class OnboardingFieldConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) => typeToConvert.IsGenericType && typeToConvert.GetGenericTypeDefinition() == typeof(OnboardingField<>);
    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
        (JsonConverter)Activator.CreateInstance(typeof(FieldConverter<>).MakeGenericType(typeToConvert.GetGenericArguments()))!;
    private sealed class FieldConverter<T> : JsonConverter<OnboardingField<T>>
    {
        public override bool HandleNull => true;
        public override OnboardingField<T> Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) =>
            new(JsonSerializer.Deserialize<T>(ref reader, options)!);
        public override void Write(Utf8JsonWriter writer, OnboardingField<T> value, JsonSerializerOptions options) =>
            JsonSerializer.Serialize(writer, value.Value, options);
    }
}

public sealed record OnboardingBasicRequest(Guid Revision,
    OnboardingField<string?> DisplayName = default, OnboardingField<string?> ProfessionalHeadline = default,
    OnboardingField<string?> Bio = default, OnboardingField<string?> ProfileImageUrl = default,
    OnboardingField<string?> Location = default, OnboardingField<string?> ProfessionalEmail = default);
public sealed record OnboardingProfessionalRequest(Guid Revision,
    OnboardingField<Guid?> CompanyId = default, OnboardingField<string?> CompanyName = default,
    OnboardingField<string?> CurrentRole = default, OnboardingField<decimal?> YearsOfExperience = default,
    OnboardingField<CareerProfessionalType?> ProfessionalType = default, OnboardingField<string?> LinkedInUrl = default,
    OnboardingField<string?> Industry = default, OnboardingField<string?> FunctionalArea = default);
public sealed record OnboardingExpertiseRequest(Guid Revision, OnboardingField<string[]?> Languages = default,
    OnboardingField<string[]?> Expertise = default);
public sealed record ConsultantEducationItem(string Qualification, string Institution, string? FieldOfStudy = null,
    int? StartYear = null, int? EndYear = null, bool IsCurrentlyStudying = false, int DisplayOrder = 0);
public sealed record ConsultantExperienceItem(string JobTitle, string CompanyName, DateOnly StartDate,
    DateOnly? EndDate = null, bool IsCurrent = false, string? Description = null, int DisplayOrder = 0);
public sealed record OnboardingEducationRequest(Guid Revision, ConsultantEducationItem[] Items);
public sealed record OnboardingExperienceRequest(Guid Revision, ConsultantExperienceItem[] Items);
public sealed record EducationImportOption(Guid Id, ConsultantEducationItem Education);
public sealed record ExperienceImportOption(Guid Id, ConsultantExperienceItem Experience);
public sealed record OnboardingImportOptions(string? ProfileImageUrl, string? Location,
    IReadOnlyList<EducationImportOption> Education, IReadOnlyList<ExperienceImportOption> WorkExperience);
// History imports explicitly replace the corresponding collection, never append implicitly.
public sealed record OnboardingImportRequest(Guid Revision, bool ImportPhoto = false, bool ImportLocation = false,
    Guid[]? EducationIds = null, Guid[]? ExperienceIds = null);
public sealed record OnboardingSubmitRequest(Guid Revision, string PolicyVersion,
    bool AcceptIndependentGuidancePolicy, bool AcceptPublicProfileConsent);
public sealed record OnboardingResponse(Guid Id, Guid Revision, ConsultantVerificationStatus Status,
    string DisplayName, string ProfessionalHeadline, string Bio, string? ProfileImageUrl, string? Location,
    string? ProfessionalEmail, string AccountEmail, Guid? CompanyId, string CompanyName, string CurrentRole,
    decimal? YearsOfExperience, CareerProfessionalType? ProfessionalType, string LinkedInUrl,
    string? Industry, string? FunctionalArea, string[] Languages, string[] Expertise,
    IReadOnlyList<ConsultantEducationItem> Education, IReadOnlyList<ConsultantExperienceItem> WorkExperience,
    IReadOnlyList<ConsultantServiceResponse> Services, AvailabilityResponse Availability,
    DateTime? SubmittedAtUtc, DateTime? PublicProfileConsentAtUtc, DateTime? TermsAcceptedAtUtc,
    string PolicyVersion, string? RejectionFeedback, IReadOnlyList<string> MissingRequirements);
