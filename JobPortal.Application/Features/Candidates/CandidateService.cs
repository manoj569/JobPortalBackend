using System.Text.Json;
using System.Text.RegularExpressions;
using System.Buffers.Binary;
using FluentValidation;
using JobPortal.Application.Abstractions.Auditing;
using JobPortal.Application.Abstractions.Candidates;
using JobPortal.Application.Abstractions.Persistence;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Application.Common.Text;
using JobPortal.Application.Features.Dashboard;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Shared.Models;
using Microsoft.Extensions.Logging;

#pragma warning disable CA1848

namespace JobPortal.Application.Features.Candidates;

public sealed class CandidateService(
    ICandidateRepository candidates,
    IDashboardRepository dashboard,
    IResumeStorage resumeStorage,
    IProfilePhotoStorage profilePhotoStorage,
    IUnitOfWork unitOfWork,
    IAuditWriter auditWriter,
    IValidator<UpdateCandidateProfileRequest> profileValidator,
    IValidator<UpdateCandidateAboutRequest> aboutValidator,
    IValidator<UpsertCandidateSkillRequest> skillValidator,
    IValidator<UpdateCandidateOnboardingRequest> onboardingValidator,
    IValidator<UpdateCandidateBasicDetailsRequest> basicDetailsValidator,
    IValidator<UpdateCandidateCareerPreferencesRequest> careerPreferencesValidator,
    IValidator<CandidatePageQuery> pageValidator,
    IValidator<JobApplicationQuery> applicationQueryValidator,
    IValidator<CreateJobApplicationRequest> applicationValidator,
    TimeProvider timeProvider,
    IResumeTextExtractor? resumeTextExtractor = null,
    ILogger<CandidateService>? logger = null) : ICandidateService
{
    private const long MaximumResumeBytes = 5 * 1024 * 1024;
    private const int MaximumProfilePhotoBytes = 1024 * 1024;
    private const int FreeMonthlyApplicationLimit = 10;
    private const int PremiumDailyApplicationLimit = 35;
    private static readonly Dictionary<string, string[]> AllowedResumeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = ["application/pdf"],
        [".doc"] = ["application/msword"],
        [".docx"] = ["application/vnd.openxmlformats-officedocument.wordprocessingml.document"]
    };

    public async Task<CandidateProfileResponse> GetProfileAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await RequiredCandidateAsync(userId, cancellationToken);
        return MapProfile(user, await profilePhotoStorage.GetAsync(userId, cancellationToken),
            CalculateTotalExperience(await candidates.GetEmploymentPeriodsAsync(userId, cancellationToken),
                DateOnly.FromDateTime(UtcNow)));
    }

    public async Task<CandidateProfileResponse> UpdateProfileAsync(
        Guid userId, UpdateCandidateProfileRequest request, CancellationToken cancellationToken = default)
    {
        await profileValidator.ValidateAndThrowAsync(request, cancellationToken);
        var user = await RequiredCandidateAsync(userId, cancellationToken);
        user.Headline = TextNormalizer.TrimOrNull(request.Headline);
        user.Bio = TextNormalizer.TrimOrNull(request.Bio);
        user.Location = TextNormalizer.TrimOrNull(request.Location);
        user.LinkedInUrl = TextNormalizer.TrimOrNull(request.LinkedInUrl);
        user.PortfolioUrl = TextNormalizer.TrimOrNull(request.PortfolioUrl);
        user.SkillsJson = SerializeStrings(request.Skills);
        user.EducationJson = SerializeStrings(request.Education);
        user.ExperienceJson = SerializeStrings(request.Experience);
        user.PreferredJobTypesJson = JsonSerializer.Serialize(request.PreferredJobTypes.Distinct());
        if (user.OnboardingCompletedAtUtc.HasValue &&
            (string.IsNullOrWhiteSpace(user.Location) ||
             request.Skills.Count == 0))
            user.OnboardingCompletedAtUtc = null;
        await auditWriter.AppendAsync(new(
            AuditAction.Update,
            "CandidateProfile",
            user.Id.ToString(),
            Actor: new(userId, "Candidate")), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return MapProfile(user, await profilePhotoStorage.GetAsync(userId, cancellationToken),
            CalculateTotalExperience(await candidates.GetEmploymentPeriodsAsync(userId, cancellationToken),
                DateOnly.FromDateTime(UtcNow)));
    }

    public async Task<CandidateBasicDetailsResponse> GetBasicDetailsAsync(
        Guid userId, CancellationToken cancellationToken = default) =>
        MapBasicDetails(await RequiredCandidateAsync(userId, cancellationToken));

    public async Task<CandidateBasicDetailsResponse> UpdateBasicDetailsAsync(
        Guid userId, UpdateCandidateBasicDetailsRequest request,
        CancellationToken cancellationToken = default)
    {
        await basicDetailsValidator.ValidateAndThrowAsync(request, cancellationToken);
        var user = await RequiredCandidateAsync(userId, cancellationToken);
        user.WorkStatus = request.WorkStatus;
        user.IsOutsideIndia = request.IsOutsideIndia;
        user.CurrentCountry = request.CurrentCountry.Trim();
        user.CurrentCity = request.CurrentCity.Trim();
        user.CurrentArea = TextNormalizer.TrimOrNull(request.CurrentArea);
        user.Location = user.CurrentCity;
        user.AvailabilityToJoin = request.AvailabilityToJoin;
        user.CurrentAnnualSalary = request.WorkStatus == CandidateWorkStatus.Experienced ? request.CurrentAnnualSalary : null;
        user.CurrentFixedAnnualSalary = request.WorkStatus == CandidateWorkStatus.Experienced ? request.CurrentFixedAnnualSalary : null;
        user.CurrentVariableAnnualSalary = request.WorkStatus == CandidateWorkStatus.Experienced ? request.CurrentVariableAnnualSalary : null;
        await auditWriter.AppendAsync(new(AuditAction.Update, "CandidateBasicDetails",
            user.Id.ToString(), Actor: new(userId, "Candidate")), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return MapBasicDetails(user);
    }

    public async Task<CandidateCareerPreferencesResponse> GetCareerPreferencesAsync(
        Guid userId, CancellationToken cancellationToken = default) =>
        MapCareerPreferences(await RequiredCandidateAsync(userId, cancellationToken));

    public async Task<CandidateCareerPreferencesResponse> UpdateCareerPreferencesAsync(
        Guid userId, UpdateCandidateCareerPreferencesRequest request,
        CancellationToken cancellationToken = default)
    {
        await careerPreferencesValidator.ValidateAndThrowAsync(request, cancellationToken);
        var user = await RequiredCandidateAsync(userId, cancellationToken);
        user.PreferredJobRolesJson = SerializeStrings(request.PreferredJobRoles);
        user.PreferredCitiesJson = SerializeStrings(request.PreferredCities);
        user.ExpectedAnnualSalary = request.ExpectedAnnualSalary;
        user.CandidateJobTypesJson = JsonSerializer.Serialize(request.JobTypes.Distinct());
        user.CandidateEmploymentTypesJson = JsonSerializer.Serialize(request.EmploymentTypes.Distinct());
        user.PreferredShiftsJson = JsonSerializer.Serialize(request.PreferredShifts.Distinct());
        await auditWriter.AppendAsync(new(AuditAction.Update, "CandidateCareerPreferences",
            user.Id.ToString(), Actor: new(userId, "Candidate")), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return MapCareerPreferences(user);
    }

    public async Task<ProfilePhotoMetadata> UploadProfilePhotoAsync(
        Guid userId, ProfilePhotoUpload upload, CancellationToken cancellationToken = default)
    {
        await RequiredCandidateAsync(userId, cancellationToken);
        if (upload.Length is <= 0 or > MaximumProfilePhotoBytes)
            throw new BadRequestException("Profile photo must be between 1 byte and 1 MB.", "invalid_profile_photo");
        await using var buffer = new MemoryStream((int)upload.Length);
        var chunk = new byte[81920];
        while (true)
        {
            var read = await upload.Content.ReadAsync(chunk, cancellationToken);
            if (read == 0) break;
            if (buffer.Length + read > MaximumProfilePhotoBytes)
                throw new BadRequestException("Profile photo must not exceed 1 MB.", "invalid_profile_photo");
            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
        }
        if (buffer.Length != upload.Length)
            throw new BadRequestException("Profile photo size is invalid.", "invalid_profile_photo");
        var bytes = buffer.ToArray();
        var contentType = DetectImageType(bytes) ?? throw new BadRequestException(
            "Profile photo must be a valid JPEG, PNG, or WebP image.", "invalid_profile_photo");
        var version = await profilePhotoStorage.StoreAsync(userId, bytes, contentType, cancellationToken);
        await auditWriter.AppendAsync(new(AuditAction.Update, "CandidateProfilePhoto",
            userId.ToString(), Actor: new(userId, "Candidate")), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new(true, version.ToString("N"));
    }

    public async Task<ProfilePhotoDownload> DownloadProfilePhotoAsync(
        Guid userId, CancellationToken cancellationToken = default)
    {
        await RequiredCandidateAsync(userId, cancellationToken);
        var photo = await profilePhotoStorage.GetAsync(userId, cancellationToken)
            ?? throw new NotFoundException("Profile photo was not found.");
        return new(new MemoryStream(photo.Content, writable: false), photo.ContentType,
            photo.SizeBytes, photo.Version.ToString("N"));
    }

    public async Task DeleteProfilePhotoAsync(
        Guid userId, CancellationToken cancellationToken = default)
    {
        await RequiredCandidateAsync(userId, cancellationToken);
        if (!await profilePhotoStorage.DeleteAsync(userId, cancellationToken))
            throw new NotFoundException("Profile photo was not found.");
        await auditWriter.AppendAsync(new(AuditAction.Delete, "CandidateProfilePhoto",
            userId.ToString(), Actor: new(userId, "Candidate")), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task<CandidateAboutResponse> UpdateAboutAsync(
        Guid userId, UpdateCandidateAboutRequest request,
        CancellationToken cancellationToken = default)
    {
        await aboutValidator.ValidateAndThrowAsync(request, cancellationToken);
        var user = await RequiredCandidateAsync(userId, cancellationToken);
        user.Headline = TextNormalizer.TrimOrNull(request.ResumeHeadline);
        user.Bio = TextNormalizer.TrimOrNull(request.ProfileSummary);
        await auditWriter.AppendAsync(new(AuditAction.Update, "CandidateProfileAbout",
            user.Id.ToString(), Actor: new(userId, "Candidate")), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new(user.Headline, user.Bio);
    }

    public async Task<IReadOnlyCollection<CandidateSkillResponse>> GetSkillsAsync(
        Guid userId, CancellationToken cancellationToken = default)
    {
        await RequiredCandidateAsync(userId, cancellationToken);
        return (await candidates.GetSkillsAsync(userId, cancellationToken))
            .Select(MapSkill).ToArray();
    }

    public async Task<CandidateSkillResponse> AddSkillAsync(
        Guid userId, UpsertCandidateSkillRequest request,
        CancellationToken cancellationToken = default)
    {
        await skillValidator.ValidateAndThrowAsync(request, cancellationToken);
        await RequiredCandidateAsync(userId, cancellationToken);
        var name = request.Name.Trim();
        var normalizedName = NormalizeSkillName(name);
        if (await candidates.SkillNameExistsAsync(userId, normalizedName, null, cancellationToken))
            throw new ConflictException("This skill already exists in your profile.");
        var skill = new CandidateSkill
        {
            UserId = userId,
            Name = name,
            NormalizedName = normalizedName,
            Proficiency = request.Proficiency,
            YearsOfExperience = request.YearsOfExperience
        };
        await candidates.AddSkillAsync(skill, cancellationToken);
        await auditWriter.AppendAsync(new(AuditAction.Create, "CandidateSkill",
            skill.Id.ToString(), Actor: new(userId, "Candidate")), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return MapSkill(skill);
    }

    public async Task<CandidateSkillResponse> UpdateSkillAsync(
        Guid userId, Guid skillId, UpsertCandidateSkillRequest request,
        CancellationToken cancellationToken = default)
    {
        await skillValidator.ValidateAndThrowAsync(request, cancellationToken);
        await RequiredCandidateAsync(userId, cancellationToken);
        var skill = await candidates.GetSkillAsync(userId, skillId, cancellationToken)
            ?? throw new NotFoundException("Skill was not found.");
        var name = request.Name.Trim();
        var normalizedName = NormalizeSkillName(name);
        if (await candidates.SkillNameExistsAsync(userId, normalizedName, skillId, cancellationToken))
            throw new ConflictException("This skill already exists in your profile.");
        skill.Name = name;
        skill.NormalizedName = normalizedName;
        skill.Proficiency = request.Proficiency;
        skill.YearsOfExperience = request.YearsOfExperience;
        await auditWriter.AppendAsync(new(AuditAction.Update, "CandidateSkill",
            skill.Id.ToString(), Actor: new(userId, "Candidate")), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return MapSkill(skill);
    }

    public async Task DeleteSkillAsync(
        Guid userId, Guid skillId, CancellationToken cancellationToken = default)
    {
        await RequiredCandidateAsync(userId, cancellationToken);
        var skill = await candidates.GetSkillAsync(userId, skillId, cancellationToken)
            ?? throw new NotFoundException("Skill was not found.");
        candidates.RemoveSkill(skill);
        await auditWriter.AppendAsync(new(AuditAction.Delete, "CandidateSkill",
            skill.Id.ToString(), Actor: new(userId, "Candidate")), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task<CandidateProfileCompletionResponse> GetProfileCompletionAsync(
        Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await RequiredCandidateAsync(userId, cancellationToken);
        var hasSkills = (await candidates.GetSkillsAsync(userId, cancellationToken)).Count > 0 ||
            Deserialize<string>(user.SkillsJson).Length > 0;
        if (!user.WorkStatus.HasValue)
        {
            var legacySections = new[]
            {
                Section("basicDetails", 15, !string.IsNullOrWhiteSpace(user.FirstName) &&
                    !string.IsNullOrWhiteSpace(user.LastName) && user.EmailConfirmed),
                Section("resumeHeadline", 10, !string.IsNullOrWhiteSpace(user.Headline)),
                Section("profileSummary", 15, !string.IsNullOrWhiteSpace(user.Bio)),
                Section("skills", 20, hasSkills),
                Section("resume", 20, !string.IsNullOrWhiteSpace(user.ResumeStorageKey)),
                Section("careerPreferences", 20, CareerPreferencesComplete(user))
            };
            return Completion(legacySections);
        }
        var records = await candidates.GetProfileRecordPresenceAsync(userId, cancellationToken);
        var experienced = user.WorkStatus == CandidateWorkStatus.Experienced ||
            !user.WorkStatus.HasValue && user.CareerStage == CareerStage.Experienced;
        var sections = new[]
        {
            Section("overview", experienced ? 15 : 25, BasicDetailsComplete(user)),
            Section("about", 15, !string.IsNullOrWhiteSpace(user.Headline) && !string.IsNullOrWhiteSpace(user.Bio)),
            Section("skills", 15, hasSkills),
            Section("resume", 15, !string.IsNullOrWhiteSpace(user.ResumeStorageKey)),
            Section("careerPreferences", 20, CareerPreferencesComplete(user)),
            Section("education", 10, records.HasEducation),
            Section("employment", experienced ? 10 : 0, !experienced || records.HasEmployment)
        };
        return Completion(sections);
    }

    public async Task<CandidateOnboardingResponse> GetOnboardingAsync(
        Guid userId, CancellationToken cancellationToken = default) =>
        MapOnboarding(await RequiredCandidateAsync(userId, cancellationToken));

    public async Task<CandidateOnboardingResponse> UpdateOnboardingAsync(
        Guid userId,
        UpdateCandidateOnboardingRequest request,
        CancellationToken cancellationToken = default)
    {
        await onboardingValidator.ValidateAndThrowAsync(request, cancellationToken);
        var user = await RequiredCandidateAsync(userId, cancellationToken);
        user.CareerStage = request.CareerStage;
        user.DesiredOpportunitiesJson = JsonSerializer.Serialize(
            request.DesiredOpportunities.Distinct());
        user.Location = request.City.Trim();
        user.SkillsJson = SerializeStrings(request.Skills);
        user.WorkPreferencesJson = JsonSerializer.Serialize(
            request.WorkPreferences.Distinct());
        user.College = TextNormalizer.TrimOrNull(request.College);
        user.Degree = TextNormalizer.TrimOrNull(request.Degree);
        user.GraduationYear = request.GraduationYear;
        user.YearsOfExperience = request.YearsOfExperience;
        user.OnboardingCompletedAtUtc ??= UtcNow;
        await auditWriter.AppendAsync(new(
            AuditAction.Update,
            "CandidateOnboarding",
            user.Id.ToString(),
            new Dictionary<string, string?>
            {
                ["changedFields"] =
                    "careerStage,desiredOpportunities,city,skills,workPreferences," +
                    "college,degree,graduationYear,yearsOfExperience",
                ["completed"] = bool.TrueString
            },
            new(userId, "Candidate")), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return MapOnboarding(user);
    }

    public async Task<ResumeResponse> UploadResumeAsync(
        Guid userId, ResumeUpload upload, CancellationToken cancellationToken = default)
    {
        var user = await RequiredCandidateAsync(userId, cancellationToken);
        var (extension, validatedContent) = await ValidateResumeAsync(upload, cancellationToken);
        var oldKey = user.ResumeStorageKey;
        await using var content = validatedContent;
        var storageKey = await resumeStorage.StoreAsync(content, extension, cancellationToken);
        user.ResumeStorageKey = storageKey;
        user.ResumeFileName = $"resume{extension}";
        user.ResumeContentType = upload.ContentType;
        user.ResumeSizeBytes = content.Length;
        user.ResumeUploadedAtUtc = UtcNow;
        var profile = await candidates.GetResumeProfileAsync(userId, true, cancellationToken);
        if (profile is null)
        {
            profile = new CandidateResumeProfile { UserId = userId };
            await candidates.AddResumeProfileAsync(profile, cancellationToken);
        }
        profile.ExtractionStatus = ResumeExtractionStatus.Processing;
        profile.ExtractionError = null;
        profile.ExtractedAtUtc = null;
        if (resumeTextExtractor is not null)
        {
            try
            {
                content.Position = 0;
                ApplyExtractedProfile(profile, await resumeTextExtractor.ExtractAsync(content, extension, cancellationToken));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                profile.ExtractionStatus = ResumeExtractionStatus.Failed;
                profile.ExtractionError = "Resume text extraction failed.";
                profile.ExtractedAtUtc = UtcNow;
                logger?.LogWarning(ex, "Resume extraction failed for candidate {CandidateId}", userId);
            }
        }
        await auditWriter.AppendAsync(new(
            AuditAction.Upload,
            "Resume",
            user.Id.ToString(),
            new Dictionary<string, string?>
            {
                ["fileType"] = extension,
                ["sizeBytes"] = content.Length.ToString(
                    System.Globalization.CultureInfo.InvariantCulture)
            },
            new(userId, "Candidate")), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await DeleteIfUnreferencedAsync(oldKey, cancellationToken);
        return new ResumeResponse(user.ResumeFileName, user.ResumeContentType, user.ResumeSizeBytes.Value,
            user.ResumeUploadedAtUtc.Value, profile.ExtractionStatus);
    }

    public async Task<ResumeDownload> DownloadResumeAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await RequiredCandidateAsync(userId, cancellationToken);
        if (user.ResumeStorageKey is null || user.ResumeFileName is null || user.ResumeContentType is null)
            throw new NotFoundException("Resume was not found.");
        var content = await resumeStorage.OpenReadAsync(user.ResumeStorageKey, cancellationToken)
            ?? throw new NotFoundException("Resume was not found.");
        return new(content, user.ResumeFileName, user.ResumeContentType);
    }

    public async Task DeleteResumeAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await RequiredCandidateAsync(userId, cancellationToken);
        var storageKey = user.ResumeStorageKey;
        if (storageKey is null) return;
        user.ResumeStorageKey = null;
        user.ResumeFileName = null;
        user.ResumeContentType = null;
        user.ResumeSizeBytes = null;
        user.ResumeUploadedAtUtc = null;
        var profile = await candidates.GetResumeProfileAsync(userId, true, cancellationToken);
        if (profile is not null)
        {
            profile.ExtractionStatus = ResumeExtractionStatus.NotStarted;
            profile.SkillsJson = profile.RoleKeywordsJson = profile.EducationKeywordsJson = profile.LocationsJson = "[]";
            profile.YearsOfExperience = null; profile.ExtractedAtUtc = null; profile.ExtractionError = null;
        }
        await auditWriter.AppendAsync(new(
            AuditAction.Delete,
            "Resume",
            user.Id.ToString(),
            Actor: new(userId, "Candidate")), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await DeleteIfUnreferencedAsync(storageKey, cancellationToken);
    }

    public async Task<ResumeStatusResponse> GetResumeStatusAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await RequiredCandidateAsync(userId, cancellationToken);
        if (user.ResumeStorageKey is null) return new(false, ResumeExtractionStatus.NotStarted, null, "Upload a resume to enable personalized job recommendations.");
        var profile = await candidates.GetResumeProfileAsync(userId, false, cancellationToken);
        var status = profile?.ExtractionStatus ?? ResumeExtractionStatus.NotStarted;
        return new(true, status, profile?.ExtractedAtUtc, StatusMessage(status));
    }

    public async Task<RecommendedJobsResponse> GetRecommendedJobsAsync(Guid userId, CandidatePageQuery query, CancellationToken cancellationToken = default)
    {
        await RequiredCandidateAsync(userId, cancellationToken);
        await pageValidator.ValidateAndThrowAsync(query, cancellationToken);
        var profile = await candidates.GetResumeProfileAsync(userId, false, cancellationToken);
        if (profile?.ExtractionStatus != ResumeExtractionStatus.Ready)
        {
            var status = profile?.ExtractionStatus ?? ResumeExtractionStatus.NotStarted;
            return new(status, StatusMessage(status), [], query.PageNumber, query.PageSize, 0);
        }
        var skills = Deserialize<string>(profile.SkillsJson).ToArray();
        var roles = Deserialize<string>(profile.RoleKeywordsJson).ToArray();
        var education = Deserialize<string>(profile.EducationKeywordsJson).ToArray();
        var locations = Deserialize<string>(profile.LocationsJson).ToArray();
        var scored = (await candidates.GetRecommendationCandidatesAsync(userId, cancellationToken))
            .Select(job => Score(job, skills, roles, education, locations)).Where(x => x.MatchScore > 0)
            .OrderByDescending(x => x.MatchScore).ThenByDescending(x => x.PublishedAtUtc).ThenBy(x => x.Id).ToArray();
        return new(ResumeExtractionStatus.Ready, "Personalized recommendations based on your resume.",
            scored.Skip((query.PageNumber - 1) * query.PageSize).Take(query.PageSize).ToArray(),
            query.PageNumber, query.PageSize, scored.Length);
    }

    public async Task<CandidateBrowseJobsResponse> GetBrowseJobsAsync(Guid userId, CandidatePageQuery query, CancellationToken cancellationToken = default)
    {
        await RequiredCandidateAsync(userId, cancellationToken);
        await pageValidator.ValidateAndThrowAsync(query, cancellationToken);
        var result = await candidates.GetCandidateBrowseJobsAsync(userId, query, cancellationToken);
        return new(result.Items.Select(x => new CandidateBrowseJobResponse(x.Id, x.ReferenceNumber, x.Title,
            x.Slug, x.CompanyId, x.CompanyName, x.CompanySlug, x.CompanyLogoUrl, x.CategoryId,
            x.CategoryName, x.Location, x.EmploymentType, x.WorkplaceType, x.ExperienceLevel,
            x.IsFeatured, x.PublishedAtUtc, x.ExpiresAtUtc)).ToArray(), query.PageNumber,
            query.PageSize, result.TotalCount);
    }

    public async Task<PagedResponse<CandidateSavedJobResponse>> GetSavedJobsAsync(
        Guid userId, CandidatePageQuery query, CancellationToken cancellationToken = default)
    {
        await RequiredCandidateAsync(userId, cancellationToken);
        await pageValidator.ValidateAndThrowAsync(query, cancellationToken);
        var dashboardQuery = new DashboardQuery(query.PageNumber, query.PageSize);
        var result = await dashboard.GetSavedJobsAsync(userId, dashboardQuery, cancellationToken);
        return new(result.Items.Select(x => new CandidateSavedJobResponse(
            x.SavedJobId, x.SavedAtUtc, x.Job.Id, x.Job.Title, x.Job.Slug, x.Job.CompanyName)).ToArray(),
            query.PageNumber, query.PageSize, result.TotalCount);
    }

    public async Task SaveJobAsync(Guid userId, Guid jobId, CancellationToken cancellationToken = default)
    {
        await RequiredCandidateAsync(userId, cancellationToken);
        if (!await dashboard.IsAvailableJobAsync(jobId, cancellationToken))
            throw new NotFoundException("Job was not found.");
        if (await dashboard.IsJobSavedAsync(userId, jobId, cancellationToken)) return;
        var savedJob = new SavedJob { UserId = userId, JobId = jobId };
        await dashboard.AddSavedJobAsync(savedJob, cancellationToken);

        // 🚀 ADD NOTIFICATION HERE
        await CreateNotificationAsync(
            userId,
            "Job Saved",
            "You have successfully saved this job to your list.",
            NotificationType.Profile,
            "/dashboard/saved-jobs",
            cancellationToken
        );

        await auditWriter.AppendAsync(new(
            AuditAction.Create,
            "SavedJob",
            savedJob.Id.ToString(),
            new Dictionary<string, string?> { ["jobId"] = jobId.ToString() },
            new(userId, "Candidate")), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
    public async Task RemoveSavedJobAsync(Guid userId, Guid jobId, CancellationToken cancellationToken = default)
    {
        await RequiredCandidateAsync(userId, cancellationToken);
        var saved = await dashboard.GetSavedJobAsync(userId, jobId, cancellationToken);
        if (saved is null) return;
        dashboard.RemoveSavedJob(saved);
        await auditWriter.AppendAsync(new(
            AuditAction.Delete,
            "SavedJob",
            saved.Id.ToString(),
            new Dictionary<string, string?> { ["jobId"] = jobId.ToString() },
            new(userId, "Candidate")), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
    public async Task<RecruiterContactResponse> GetRecruiterContactAsync(
    Guid userId,
    Guid jobId,
    CancellationToken cancellationToken = default)
    {
        await RequiredCandidateAsync(userId, cancellationToken);

        if (!await candidates.HasActiveMembershipAsync(userId, cancellationToken))
        {
            throw new ConflictException(
                "An active portal membership is required to view recruiter contact details.");
        }

        var contact = await candidates.GetApprovedRecruiterContactForAvailableJobAsync(
            jobId,
            cancellationToken)
            ?? throw new NotFoundException(
                "Recruiter contact details are not available for this job.");

        await auditWriter.AppendAsync(new(
            AuditAction.View,
            "RecruiterContact",
            jobId.ToString(),
            new Dictionary<string, string?>
            {
                ["jobId"] = jobId.ToString(),
                ["access"] = "membership"
            },
            new(userId, "Candidate")),
            cancellationToken);

        return new RecruiterContactResponse(
            contact.JobId,
            contact.JobTitle,
            contact.JobSlug,
            contact.CompanyName,
            contact.ContactName,
            contact.ContactRole,
            contact.Email,
            contact.PhoneNumber);
    }

    public async Task<ApplicationQuotaResponse> GetApplicationQuotaAsync(
    Guid userId,
    CancellationToken cancellationToken = default)
    {
        await RequiredCandidateAsync(userId, cancellationToken);

        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var hasPremiumMembership = await candidates.HasActiveMembershipAsync(
            userId,
            cancellationToken);

        var quota = GetApplicationQuotaWindow(nowUtc, hasPremiumMembership);

        var usage = await candidates.GetQuotaUsageAsync(
            userId,
            quota.Period,
            quota.StartsAtUtc,
            cancellationToken);

        var usedApplications = usage?.UsedApplications ?? 0;

        return new ApplicationQuotaResponse(
            hasPremiumMembership ? "Premium" : "Free",
            hasPremiumMembership,
            quota.Limit,
            usedApplications,
            Math.Max(0, quota.Limit - usedApplications),
            quota.EndsAtUtc);
    }
    public async Task<JobApplicationResponse> ApplyAsync(
        Guid userId, Guid jobId, CreateJobApplicationRequest request, CancellationToken cancellationToken = default)
    {
        await applicationValidator.ValidateAndThrowAsync(request, cancellationToken);
        var user = await RequiredCandidateAsync(userId, cancellationToken);
        var existing = await candidates.GetApplicationByJobAsync(userId, jobId, cancellationToken);
        if (existing is not null)
            return MapApplication(existing, new CandidateJob(existing.JobId, existing.Job.Title,
                existing.Job.Slug, existing.Job.Company.Name, existing.Job.ApplicationUrl));
        var job = await candidates.GetAvailableJobAsync(jobId, cancellationToken)
            ?? throw new NotFoundException("Job was not found.");
        if (request.ApplicationMethod == ApplicationMethod.External && string.IsNullOrWhiteSpace(job.ApplicationUrl))
            throw new BadRequestException("This job does not support external applications.", "invalid_application_method");

        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var hasPremiumMembership = await candidates.HasActiveMembershipAsync(userId, cancellationToken);

        var quota = GetApplicationQuotaWindow(nowUtc, hasPremiumMembership);

        var usage = await candidates.GetQuotaUsageAsync(
            userId,
            quota.Period,
            quota.StartsAtUtc,
            cancellationToken);

        if (usage is null)
        {
            usage = new ApplicationQuotaUsage
            {
                UserId = userId,
                Period = quota.Period,
                PeriodStartsAtUtc = quota.StartsAtUtc,
                PeriodEndsAtUtc = quota.EndsAtUtc,
                UsedApplications = 0
            };

            await candidates.AddQuotaUsageAsync(usage, cancellationToken);
        }

        if (usage.UsedApplications >= quota.Limit)
        {
            throw new ApplicationQuotaExceededException(
                quota.LimitExceededCode,
                quota.ExhaustedMessage,
                quota.RedirectToMembership);
        }

        usage.UsedApplications++;
        var application = new JobApplication
        {
            UserId = userId,
            JobId = jobId,
            Status = request.ApplicationMethod == ApplicationMethod.External
                ? JobApplicationStatus.ExternalApplicationStarted : JobApplicationStatus.Submitted,
            ApplicationMethod = request.ApplicationMethod,
            CoverLetter = TextNormalizer.TrimOrNull(request.CoverLetter),
            ResumeStorageKey = user.ResumeStorageKey,
            ResumeFileName = user.ResumeFileName,
            ResumeContentType = user.ResumeContentType,
            SubmittedAtUtc = UtcNow
        };
        application.StatusHistory.Add(new JobApplicationStatusHistory
        {
            Application = application,
            ActorUserId = userId,
            PreviousStatus = null,
            NewStatus = JobApplicationStatus.Submitted,
            ChangedAtUtc = UtcNow
        });
        await candidates.AddApplicationAsync(application, cancellationToken);
        await CreateNotificationAsync(userId,
            request.ApplicationMethod == ApplicationMethod.External ? "External application started" : "Application submitted",
            request.ApplicationMethod == ApplicationMethod.External
                ? $"We saved {job.Title} in your Applied jobs. Complete the employer application on their website."
                : $"Your application for {job.Title} has been submitted.",
            NotificationType.Application, "/dashboard/applied-jobs", cancellationToken);
        await auditWriter.AppendAsync(new(
            AuditAction.Submit,
            "JobApplication",
            application.Id.ToString(),
            new Dictionary<string, string?>
            {
                ["jobId"] = jobId.ToString(),
                ["status"] = JobApplicationStatus.Submitted.ToString()
            },
            new(userId, "Candidate")), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return MapApplication(application, job);
    }

    public async Task<ApplyJobResponse> ApplyJobAsync(Guid userId, Guid jobId,
        CreateJobApplicationRequest request, CancellationToken cancellationToken = default)
    {
        var application = await ApplyAsync(userId, jobId, request, cancellationToken);
        return new(application.Id, application.JobId, application.Status,
            application.ApplicationMethod, application.SubmittedAtUtc);
    }

    public async Task<PagedResponse<JobApplicationResponse>> GetApplicationsAsync(
        Guid userId, JobApplicationQuery query, CancellationToken cancellationToken = default)
    {
        await RequiredCandidateAsync(userId, cancellationToken);
        await applicationQueryValidator.ValidateAndThrowAsync(query, cancellationToken);
        var result = await candidates.GetApplicationsAsync(userId, query, cancellationToken);
        return new(result.Items, query.PageNumber, query.PageSize, result.TotalCount);
    }

    public async Task<JobApplicationResponse> GetApplicationAsync(
        Guid userId, Guid applicationId, CancellationToken cancellationToken = default)
    {
        await RequiredCandidateAsync(userId, cancellationToken);
        var application = await candidates.GetApplicationAsync(userId, applicationId, cancellationToken)
            ?? throw new NotFoundException("Application was not found.");
        return MapApplication(application,
            new CandidateJob(application.JobId, application.Job.Title, application.Job.Slug, application.Job.Company.Name, application.Job.ApplicationUrl));
    }

    public async Task<JobApplicationResponse> WithdrawAsync(
        Guid userId, Guid applicationId, CancellationToken cancellationToken = default)
    {
        await RequiredCandidateAsync(userId, cancellationToken);
        var application = await candidates.GetApplicationAsync(userId, applicationId, cancellationToken)
            ?? throw new NotFoundException("Application was not found.");
        if (application.Status != JobApplicationStatus.Submitted)
            throw new ConflictException("Only a Submitted application can be withdrawn.");
        var previous = application.Status;
        application.Status = JobApplicationStatus.Withdrawn;
        application.WithdrawnAtUtc = UtcNow;
        application.StatusHistory.Add(new JobApplicationStatusHistory
        {
            ApplicationId = application.Id,
            ActorUserId = userId,
            PreviousStatus = previous,
            NewStatus = JobApplicationStatus.Withdrawn,
            ChangedAtUtc = UtcNow
        });
        await auditWriter.AppendAsync(new(
            AuditAction.Withdraw,
            "JobApplication",
            application.Id.ToString(),
            new Dictionary<string, string?>
            {
                ["previousStatus"] = previous.ToString(),
                ["newStatus"] = JobApplicationStatus.Withdrawn.ToString()
            },
            new(userId, "Candidate")), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return MapApplication(application,
            new CandidateJob(application.JobId, application.Job.Title, application.Job.Slug, application.Job.Company.Name, application.Job.ApplicationUrl));
    }

    private async Task<User> RequiredCandidateAsync(Guid userId, CancellationToken cancellationToken) =>
        await candidates.GetCandidateAsync(userId, cancellationToken)
        ?? throw new UnauthorizedException("An active Candidate account is required.");

    private static async Task<(string Extension, MemoryStream Content)> ValidateResumeAsync(
        ResumeUpload upload, CancellationToken cancellationToken)
    {
        if (upload.Length is <= 0 or > MaximumResumeBytes)
            throw new BadRequestException("Resume size must be between 1 byte and 5 MB.", "invalid_resume");
        var extension = Path.GetExtension(upload.FileName).ToLowerInvariant();
        if (!AllowedResumeTypes.TryGetValue(extension, out var types) ||
            !types.Contains(upload.ContentType, StringComparer.OrdinalIgnoreCase))
            throw new BadRequestException("Resume must be a PDF, DOC, or DOCX file.", "invalid_resume");
        var content = new MemoryStream();
        var buffer = new byte[81920];
        while (true)
        {
            var readCount = await upload.Content.ReadAsync(buffer, cancellationToken);
            if (readCount == 0) break;
            if (content.Length + readCount > MaximumResumeBytes)
            {
                await content.DisposeAsync();
                throw new BadRequestException("Resume size must be between 1 byte and 5 MB.", "invalid_resume");
            }
            await content.WriteAsync(buffer.AsMemory(0, readCount), cancellationToken);
        }
        if (content.Length == 0)
        {
            await content.DisposeAsync();
            throw new BadRequestException("Resume size must be between 1 byte and 5 MB.", "invalid_resume");
        }
        var signature = content.GetBuffer().AsSpan(0, (int)Math.Min(8, content.Length));
        var valid = extension switch
        {
            ".pdf" => signature.Length >= 5 && signature[..5].SequenceEqual("%PDF-"u8),
            ".doc" => signature.Length >= 8 &&
                signature[..8].SequenceEqual(new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 }),
            ".docx" => signature.Length >= 4 &&
                signature[..4].SequenceEqual(new byte[] { 0x50, 0x4B, 0x03, 0x04 }),
            _ => false
        };
        if (!valid)
        {
            await content.DisposeAsync();
            throw new BadRequestException("Resume content does not match its file type.", "invalid_resume");
        }
        content.Position = 0;
        return (extension, content);
    }

    private static CandidateProfileResponse MapProfile(
        User user, StoredProfilePhoto? photo, decimal totalExperienceYears) => new(
        user.Id, user.Email, user.FirstName, user.LastName, user.Headline, user.Bio, user.Location,
        Deserialize<string>(user.SkillsJson), Deserialize<string>(user.EducationJson),
        Deserialize<string>(user.ExperienceJson), user.LinkedInUrl, user.PortfolioUrl,
        Deserialize<EmploymentType>(user.PreferredJobTypesJson), MapResume(user), user.PhoneNumber,
        photo is not null, photo?.Version.ToString("N"), MapBasicDetails(user),
        MapCareerPreferences(user), totalExperienceYears);
    private static CandidateBasicDetailsResponse MapBasicDetails(User user) => new(
        user.Email, user.PhoneNumber, user.WorkStatus, user.IsOutsideIndia, user.CurrentCountry,
        user.CurrentCity ?? user.Location, user.CurrentArea, user.AvailabilityToJoin,
        user.CurrentAnnualSalary, user.CurrentFixedAnnualSalary, user.CurrentVariableAnnualSalary);
    private static CandidateCareerPreferencesResponse MapCareerPreferences(User user) => new(
        Deserialize<string>(user.PreferredJobRolesJson), Deserialize<string>(user.PreferredCitiesJson),
        user.ExpectedAnnualSalary, Deserialize<CandidateJobType>(user.CandidateJobTypesJson),
        Deserialize<CandidateEmploymentPreference>(user.CandidateEmploymentTypesJson),
        Deserialize<CandidateShiftPreference>(user.PreferredShiftsJson));
    private static CandidateOnboardingResponse MapOnboarding(User user) => new(
        user.CareerStage,
        Deserialize<DesiredOpportunity>(user.DesiredOpportunitiesJson),
        user.Location,
        Deserialize<string>(user.SkillsJson),
        Deserialize<WorkPreference>(user.WorkPreferencesJson),
        user.College,
        user.Degree,
        user.GraduationYear,
        user.YearsOfExperience,
        user.OnboardingCompletedAtUtc);
    private static ResumeResponse? MapResume(User user) =>
        user.ResumeFileName is not null && user.ResumeContentType is not null &&
        user.ResumeSizeBytes.HasValue && user.ResumeUploadedAtUtc.HasValue
            ? new(user.ResumeFileName, user.ResumeContentType, user.ResumeSizeBytes.Value, user.ResumeUploadedAtUtc.Value)
            : null;
    private static JobApplicationResponse MapApplication(JobApplication application, CandidateJob job) => new(
        application.Id, application.JobId, job.Title, job.Slug, job.CompanyName,
        application.Status, application.CoverLetter, application.ResumeFileName,
        application.SubmittedAtUtc, application.WithdrawnAtUtc, application.ApplicationMethod);
    private static string SerializeStrings(IEnumerable<string> values) =>
        JsonSerializer.Serialize(values.Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase));
    private static T[] Deserialize<T>(string json) =>
        string.IsNullOrWhiteSpace(json) ? [] : JsonSerializer.Deserialize<T[]>(json) ?? [];

    private static string? DetectImageType(byte[] content)
    {
        var bytes = content.AsSpan();
        if (IsValidJpeg(bytes)) return "image/jpeg";
        if (IsValidPng(bytes)) return "image/png";
        if (bytes.Length >= 20 && bytes[..4].SequenceEqual("RIFF"u8) &&
            bytes[8..12].SequenceEqual("WEBP"u8) &&
            BinaryPrimitives.ReadUInt32LittleEndian(bytes[4..8]) + 8 == bytes.Length &&
            bytes[12..16] is var chunk &&
            (chunk.SequenceEqual("VP8 "u8) || chunk.SequenceEqual("VP8L"u8) ||
             chunk.SequenceEqual("VP8X"u8))) return "image/webp";
        return null;
    }

    private static bool IsValidPng(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 45 ||
            !bytes[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) return false;
        var offset = 8;
        var first = true;
        while (offset + 12 <= bytes.Length)
        {
            var length = BinaryPrimitives.ReadUInt32BigEndian(bytes[offset..(offset + 4)]);
            if (length > MaximumProfilePhotoBytes || offset + 12L + length > bytes.Length) return false;
            var type = bytes[(offset + 4)..(offset + 8)];
            if (first && (!type.SequenceEqual("IHDR"u8) || length != 13)) return false;
            if (type.SequenceEqual("IEND"u8)) return length == 0 && offset + 12 == bytes.Length;
            first = false;
            offset += checked((int)length + 12);
        }
        return false;
    }

    private static bool IsValidJpeg(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 12 || !bytes[..2].SequenceEqual(new byte[] { 0xFF, 0xD8 }) ||
            !bytes[^2..].SequenceEqual(new byte[] { 0xFF, 0xD9 })) return false;
        var offset = 2;
        var hasStartOfFrame = false;
        while (offset + 4 <= bytes.Length - 2)
        {
            if (bytes[offset++] != 0xFF) return false;
            while (offset < bytes.Length && bytes[offset] == 0xFF) offset++;
            if (offset >= bytes.Length) return false;
            var marker = bytes[offset++];
            if (marker == 0xDA) return hasStartOfFrame;
            if (marker is 0x01 or >= 0xD0 and <= 0xD7) continue;
            if (offset + 2 > bytes.Length) return false;
            var length = BinaryPrimitives.ReadUInt16BigEndian(bytes[offset..(offset + 2)]);
            if (length < 2 || offset + length > bytes.Length - 2) return false;
            if (marker is >= 0xC0 and <= 0xC3 or >= 0xC5 and <= 0xC7 or
                >= 0xC9 and <= 0xCB or >= 0xCD and <= 0xCF) hasStartOfFrame = true;
            offset += length;
        }
        return false;
    }

    internal static decimal CalculateTotalExperience(
        IReadOnlyCollection<CandidateEmploymentPeriod> periods, DateOnly today)
    {
        var ranges = periods.Select(x => (Start: x.StartDate,
                End: x.IsCurrent ? today : x.EndDate ?? x.StartDate))
            .Where(x => x.End >= x.Start).OrderBy(x => x.Start).ToArray();
        if (ranges.Length == 0) return 0;
        var days = 0;
        var start = ranges[0].Start;
        var end = ranges[0].End;
        foreach (var range in ranges.Skip(1))
        {
            if (range.Start <= end.AddDays(1))
            {
                if (range.End > end) end = range.End;
                continue;
            }
            days += end.DayNumber - start.DayNumber + 1;
            start = range.Start;
            end = range.End;
        }
        days += end.DayNumber - start.DayNumber + 1;
        return Math.Round(days / 365.2425m, 1, MidpointRounding.AwayFromZero);
    }
    private void ApplyExtractedProfile(CandidateResumeProfile profile, string text)
    {
        var normalized = " " + Normalize(text) + " ";
        string[] skillVocabulary = ["c#", ".net", "asp.net", "sql", "javascript", "typescript", "react", "angular", "python", "java", "azure", "aws", "docker", "kubernetes", "git", "html", "css", "machine learning", "data analysis", "excel", "power bi"];
        string[] roleVocabulary = ["software engineer", "software developer", "backend developer", "frontend developer", "full stack developer", "data analyst", "data scientist", "product manager", "business analyst", "devops engineer", "quality assurance", "accountant", "recruiter"];
        string[] educationVocabulary = ["bachelor", "b.tech", "b.e", "master", "m.tech", "mba", "bca", "mca", "computer science", "engineering", "diploma"];
        string[] locationVocabulary = ["bengaluru", "bangalore", "pune", "mumbai", "delhi", "new delhi", "hyderabad", "chennai", "kolkata", "gurugram", "gurgaon", "noida", "ahmedabad", "remote"];
        profile.SkillsJson = BoundedMatches(normalized, skillVocabulary, 50, 100);
        profile.RoleKeywordsJson = BoundedMatches(normalized, roleVocabulary, 20, 100);
        profile.EducationKeywordsJson = BoundedMatches(normalized, educationVocabulary, 20, 100);
        profile.LocationsJson = BoundedMatches(normalized, locationVocabulary, 20, 100);
        var experience = Regex.Match(normalized, @"\b(?<years>\d{1,2}(?:\.\d)?)\+?\s+years?\s+(?:of\s+)?experience\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        profile.YearsOfExperience = experience.Success && decimal.TryParse(experience.Groups["years"].Value,
            System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var years) && years is >= 0 and <= 50 ? years : null;
        profile.ExtractionStatus = ResumeExtractionStatus.Ready;
        profile.ExtractionError = null;
        profile.ExtractedAtUtc = UtcNow;
    }

    private static string BoundedMatches(string text, IEnumerable<string> vocabulary, int count, int length) =>
        JsonSerializer.Serialize(vocabulary.Where(x => text.Contains(" " + Normalize(x) + " ", StringComparison.Ordinal))
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(count).Select(x => x[..Math.Min(x.Length, length)]));
    private static string Normalize(string value) => string.Join(' ', new string(value.ToLowerInvariant()
        .Select(c => char.IsLetterOrDigit(c) || c is '#' or '+' or '.' ? c : ' ').ToArray())
        .Split(' ', StringSplitOptions.RemoveEmptyEntries));
    private static string StatusMessage(ResumeExtractionStatus status) => status switch
    {
        ResumeExtractionStatus.Ready => "Your resume is ready for personalized job recommendations.",
        ResumeExtractionStatus.Processing => "Your resume is being processed for recommendations.",
        ResumeExtractionStatus.Failed => "Your resume was saved, but recommendations are not available yet.",
        _ => "Upload a resume to enable personalized job recommendations."
    };
    internal static RecommendedJobResponse Score(RecommendationJobCandidate job, string[] skills, string[] roles,
        string[] education, string[] locations)
    {
        var title = Normalize(job.Title); var category = Normalize(job.CategoryName);
        var body = Normalize(string.Join(' ', job.Description, job.Requirements, job.Responsibilities, job.CompanyIndustry));
        var matchedSkills = skills.Where(x => (" " + title + " " + body + " ").Contains(" " + Normalize(x) + " ", StringComparison.Ordinal)).Take(5).ToArray();
        var matchedRoles = roles.Where(x => (" " + title + " " + category + " ").Contains(Normalize(x), StringComparison.Ordinal)).Take(3).ToArray();
        var matchedEducation = education.Where(x => body.Contains(Normalize(x), StringComparison.Ordinal)).Take(2).ToArray();
        var matchedLocations = locations.Where(x => Normalize(job.Location ?? "").Contains(Normalize(x), StringComparison.Ordinal)).Take(2).ToArray();
        var score = Math.Min(100, matchedSkills.Length * 12 + (matchedRoles.Length > 0 ? 25 : 0) +
            (matchedEducation.Length > 0 ? 5 : 0) + (matchedLocations.Length > 0 ? 10 : 0));
        var reasons = new List<string>(3);
        if (matchedSkills.Length > 0) reasons.Add("Matches skills: " + string.Join(", ", matchedSkills));
        if (matchedRoles.Length > 0) reasons.Add("Matches your " + matchedRoles[0] + " profile");
        if (matchedLocations.Length > 0) reasons.Add("Matches your preferred location: " + matchedLocations[0]);
        if (reasons.Count < 3 && matchedEducation.Length > 0) reasons.Add("Matches education: " + string.Join(", ", matchedEducation));
        return new(job.Id, job.ReferenceNumber, job.Title, job.Slug, job.CompanyId, job.CompanyName,
            job.CompanySlug, job.CompanyLogoUrl, job.CategoryId, job.CategoryName, job.Location,
            job.EmploymentType, job.WorkplaceType, job.ExperienceLevel, job.IsFeatured,
            job.PublishedAtUtc, job.ExpiresAtUtc, score, reasons.Take(3).ToArray());
    }
    private async Task DeleteIfUnreferencedAsync(string? storageKey, CancellationToken cancellationToken)
    {
        if (storageKey is not null &&
            !await candidates.IsResumeReferencedAsync(storageKey, cancellationToken))
            await resumeStorage.DeleteAsync(storageKey, cancellationToken);
    }
    private DateTime UtcNow => timeProvider.GetUtcNow().UtcDateTime;

    private static ApplicationQuotaWindow GetApplicationQuotaWindow(
    DateTime nowUtc,
    bool hasPremiumMembership)
    {
        var indiaTimeZone = GetIndiaTimeZone();
        var indiaNow = TimeZoneInfo.ConvertTimeFromUtc(
            DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc),
            indiaTimeZone);

        DateTime startsIndia;
        DateTime endsIndia;
        ApplicationQuotaPeriod period;
        int limit;
        string exhaustedMessage;

        if (hasPremiumMembership)
        {
            startsIndia = indiaNow.Date;
            endsIndia = startsIndia.AddDays(1);
            period = ApplicationQuotaPeriod.PremiumDaily;
            limit = PremiumDailyApplicationLimit;
            exhaustedMessage =
                $"You've reached today's application limit of {PremiumDailyApplicationLimit} jobs. " +
                "Please try again tomorrow.";
        }
        else
        {
            startsIndia = new DateTime(indiaNow.Year, indiaNow.Month, 1);
            endsIndia = startsIndia.AddMonths(1);
            period = ApplicationQuotaPeriod.FreeMonthly;
            limit = FreeMonthlyApplicationLimit;
            exhaustedMessage =
                $"You've reached your monthly application limit of {FreeMonthlyApplicationLimit} jobs. " +
                "Upgrade to Premium for more applications.";
        }

        return new ApplicationQuotaWindow(
            period,
            TimeZoneInfo.ConvertTimeToUtc(startsIndia, indiaTimeZone),
            TimeZoneInfo.ConvertTimeToUtc(endsIndia, indiaTimeZone),
            limit,
            hasPremiumMembership ? "DAILY_JOB_LIMIT_REACHED" : "MONTHLY_JOB_LIMIT_REACHED",
            exhaustedMessage,
            !hasPremiumMembership);
    }

    private static TimeZoneInfo GetIndiaTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("India Standard Time");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata");
        }
    }
    // 🚀 ADD THIS HELPER METHOD
    private async Task CreateNotificationAsync(
        Guid userId,
        string title,
        string message,
        NotificationType type,
        string? actionUrl = null,
        CancellationToken cancellationToken = default)
    {
        var notification = new Notification
        {
            UserId = userId,
            Title = title,
            Message = message,
            Type = type,
            ActionUrl = actionUrl,
            IsRead = false,
            CreatedAtUtc = UtcNow
        };

        await dashboard.AddNotificationAsync(notification, cancellationToken);
    }

    private static string NormalizeSkillName(string value) =>
        value.Trim().ToUpperInvariant();

    private static CandidateSkillResponse MapSkill(CandidateSkill skill) =>
        new(skill.Id, skill.Name, skill.Proficiency, skill.YearsOfExperience,
            skill.CreatedAtUtc, skill.UpdatedAtUtc);

    private static ProfileSectionCompletionResponse Section(
        string section, int weight, bool completed) =>
        new(section, weight, completed);

    private static CandidateProfileCompletionResponse Completion(
        IReadOnlyCollection<ProfileSectionCompletionResponse> sections) =>
        new(sections.Where(x => x.IsCompleted).Sum(x => x.Weight),
            sections.Where(x => x.IsCompleted).Select(x => x.Section).ToArray(),
            sections.Where(x => !x.IsCompleted && x.Weight > 0).Select(x => x.Section).ToArray(), sections);

    private static bool CareerPreferencesComplete(User user) =>
        Deserialize<string>(user.PreferredJobRolesJson).Length > 0 &&
        Deserialize<string>(user.PreferredCitiesJson).Length > 0 &&
        Deserialize<CandidateEmploymentPreference>(user.CandidateEmploymentTypesJson).Length > 0 ||
        user.OnboardingCompletedAtUtc.HasValue && user.CareerStage.HasValue &&
        !string.IsNullOrWhiteSpace(user.Location) &&
        Deserialize<DesiredOpportunity>(user.DesiredOpportunitiesJson).Length > 0 &&
        Deserialize<WorkPreference>(user.WorkPreferencesJson).Length > 0;

    private static bool BasicDetailsComplete(User user) =>
        !string.IsNullOrWhiteSpace(user.FirstName) && !string.IsNullOrWhiteSpace(user.LastName) &&
        user.EmailConfirmed && user.WorkStatus.HasValue &&
        !string.IsNullOrWhiteSpace(user.CurrentCountry) &&
        !string.IsNullOrWhiteSpace(user.CurrentCity ?? user.Location);

    private sealed record ApplicationQuotaWindow(
        ApplicationQuotaPeriod Period,
        DateTime StartsAtUtc,
        DateTime EndsAtUtc,
        int Limit,
        string LimitExceededCode,
        string ExhaustedMessage,
        bool RedirectToMembership);
}
