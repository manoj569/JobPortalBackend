using System.Text.Json;
using JobPortal.API.Middleware;
using JobPortal.API.Controllers;
using JobPortal.Application.Abstractions.Candidates;
using JobPortal.Application.Abstractions.Persistence;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Application.Features.Candidates;
using JobPortal.Application.Features.Dashboard;
using JobPortal.Domain.Common;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class CandidateModuleTests
{
    [Fact]
    public async Task ProfilePhotoCanBeUploadedReplacedRetrievedAndDeletedWithCandidateIsolation()
    {
        var fixture = CreateFixture();
        var first = MinimalPng();
        var uploaded = await fixture.Service.UploadProfilePhotoAsync(fixture.Candidate.Id,
            new(new MemoryStream(first), first.Length, "application/octet-stream"));
        var retrieved = await fixture.Service.DownloadProfilePhotoAsync(fixture.Candidate.Id);
        Assert.True(uploaded.HasProfilePhoto);
        Assert.Equal("image/png", retrieved.ContentType);

        var second = MinimalPng();
        var replaced = await fixture.Service.UploadProfilePhotoAsync(fixture.Candidate.Id,
            new(new MemoryStream(second), second.Length, "image/png"));
        Assert.NotEqual(uploaded.Version, replaced.Version);
        await Assert.ThrowsAsync<UnauthorizedException>(() => fixture.Service.DownloadProfilePhotoAsync(Guid.NewGuid()));

        await fixture.Service.DeleteProfilePhotoAsync(fixture.Candidate.Id);
        await Assert.ThrowsAsync<NotFoundException>(() => fixture.Service.DownloadProfilePhotoAsync(fixture.Candidate.Id));
    }

    [Fact]
    public async Task ProfilePhotoRejectsInvalidSignatureAndOversize()
    {
        var fixture = CreateFixture();
        var invalid = new byte[32];
        await Assert.ThrowsAsync<BadRequestException>(() => fixture.Service.UploadProfilePhotoAsync(
            fixture.Candidate.Id, new(new MemoryStream(invalid), invalid.Length, "image/png")));
        var oversized = new byte[1024 * 1024 + 1];
        await Assert.ThrowsAsync<BadRequestException>(() => fixture.Service.UploadProfilePhotoAsync(
            fixture.Candidate.Id, new(new MemoryStream(oversized), oversized.Length, "image/png")));
    }

    [Fact]
    public async Task ProfilePhotoResponseUsesPrivateAuthorizationVaryingCacheHeaders()
    {
        var fixture = CreateFixture();
        var content = MinimalPng();
        await fixture.Service.UploadProfilePhotoAsync(fixture.Candidate.Id,
            new(new MemoryStream(content), content.Length, "image/png"));
        var controller = new CandidateController(fixture.Service)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, fixture.Candidate.Id.ToString()),
                         new Claim(ClaimTypes.Role, "Candidate")], "test"))
                }
            }
        };

        var result = await controller.ProfilePhoto(default);

        Assert.IsType<FileStreamResult>(result);
        Assert.Equal("private, no-store", controller.Response.Headers.CacheControl);
        Assert.Equal("no-cache", controller.Response.Headers.Pragma);
        Assert.Equal("Authorization", controller.Response.Headers.Vary);
        Assert.StartsWith("\"private-photo-", controller.Response.Headers.ETag.ToString(), StringComparison.Ordinal);
        Assert.NotNull(typeof(CandidateController).GetCustomAttributes(
            typeof(AuthorizeAttribute), inherit: true).SingleOrDefault());
    }

    [Fact]
    public async Task BasicDetailsAndCareerPreferencesAreTrimmedValidatedAndKeepContactReadOnly()
    {
        var fixture = CreateFixture();
        fixture.Candidate.PhoneNumber = "+919876543210";
        var details = await fixture.Service.UpdateBasicDetailsAsync(fixture.Candidate.Id,
            new(CandidateWorkStatus.Experienced, false, " India ", " Pune ", " Baner ",
                CandidateAvailability.OneMonth, 1200000, 1000000, 200000));
        Assert.Equal("candidate@example.test", details.Email);
        Assert.Equal("+919876543210", details.Mobile);
        Assert.Equal("Pune", details.CurrentCity);

        var preferences = await fixture.Service.UpdateCareerPreferencesAsync(fixture.Candidate.Id,
            new([" Backend Engineer "], [" Pune "], 1500000,
                [CandidateJobType.Permanent], [CandidateEmploymentPreference.FullTime],
                [CandidateShiftPreference.Flexible]));
        Assert.Equal("Backend Engineer", Assert.Single(preferences.PreferredJobRoles));
        Assert.Equal("Pune", Assert.Single(preferences.PreferredCities));

        var invalid = new UpdateCandidateCareerPreferencesRequest(
            ["Engineer", " engineer "], [], -1, [], [], []);
        Assert.False((await new UpdateCandidateCareerPreferencesRequestValidator()
            .ValidateAsync(invalid)).IsValid);
    }

    [Fact]
    public async Task CareerPreferenceCanonicalStringsDeserializeSaveAndRoundTrip()
    {
        const string json =
            """{"preferredJobRoles":["Engineer"],"preferredCities":["Pune"],"expectedAnnualSalary":1200000,"jobTypes":["Permanent"],"employmentTypes":["FullTime"],"preferredShifts":["Flexible"]}""";
        var request = JsonSerializer.Deserialize<UpdateCandidateCareerPreferencesRequest>(json, WebJson)!;
        var fixture = CreateFixture();
        await fixture.Service.UpdateCareerPreferencesAsync(fixture.Candidate.Id, request);
        var response = await fixture.Service.GetCareerPreferencesAsync(fixture.Candidate.Id);
        var responseJson = JsonSerializer.Serialize(response, WebJson);
        Assert.Equal(CandidateEmploymentPreference.FullTime, Assert.Single(response.EmploymentTypes));
        Assert.Contains("\"FullTime\"", responseJson, StringComparison.Ordinal);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<UpdateCandidateCareerPreferencesRequest>(
            json.Replace("FullTime", "Full-time", StringComparison.Ordinal), WebJson));
    }

    [Fact]
    public async Task CandidateWithoutMobileCanAddUnverifiedNumberButExistingVerifiedNumberCannotBeReplaced()
    {
        var fixture = CreateFixture();
        fixture.Candidate.PhoneNumber = null;
        fixture.Candidate.NormalizedPhoneNumber = null;
        fixture.Candidate.PhoneConfirmed = false;
        var request = new UpdateCandidateBasicDetailsRequest(CandidateWorkStatus.Fresher,
            false, "India", "Pune", null, null, null, null, null, "9876543210");
        var added = await fixture.Service.UpdateBasicDetailsAsync(fixture.Candidate.Id, request);
        Assert.Equal("+919876543210", added.MobileNumber);
        Assert.False(added.MobileVerified);
        fixture.Candidate.PhoneConfirmed = true;
        var error = await Assert.ThrowsAsync<ConflictException>(() =>
            fixture.Service.UpdateBasicDetailsAsync(fixture.Candidate.Id,
                request with { MobileNumber = "9123456780" }));
        Assert.Equal("mobile_number_change_requires_verification", error.Code);
        Assert.Equal("+919876543210", fixture.Candidate.PhoneNumber);
    }

    [Fact]
    public async Task ExistingBasicDetailsArePreservedWhenAddingMobileOnly()
    {
        var fixture = CreateFixture();
        fixture.Candidate.WorkStatus = CandidateWorkStatus.Experienced;
        fixture.Candidate.IsOutsideIndia = false;
        fixture.Candidate.CurrentCountry = "India";
        fixture.Candidate.CurrentCity = "Pune";
        fixture.Candidate.CurrentArea = "Baner";
        fixture.Candidate.AvailabilityToJoin = CandidateAvailability.OneMonth;

        var result = await fixture.Service.UpdateBasicDetailsAsync(
            fixture.Candidate.Id, new(MobileNumber: "9876543210"));

        Assert.Equal("+919876543210", result.MobileNumber);
        Assert.Equal("India", result.CurrentCountry);
        Assert.Equal("Pune", result.CurrentCity);
        Assert.Equal("Baner", result.CurrentArea);
        Assert.Equal(CandidateAvailability.OneMonth, result.NoticePeriod);
    }

    [Fact]
    public async Task InvalidMobileProducesMobileNumberFieldError()
    {
        var request = new UpdateCandidateBasicDetailsRequest(
            CandidateWorkStatus.Fresher, false, "India", "Pune",
            MobileNumber: "12345");
        var result = await new UpdateCandidateBasicDetailsRequestValidator().ValidateAsync(request);

        var error = Assert.Single(result.Errors, x => x.PropertyName == "MobileNumber");
        Assert.Equal("Mobile number must be a valid 10-digit Indian mobile number.", error.ErrorMessage);
    }

    [Fact]
    public async Task NoticePeriodPersistsThroughPrivateBasicDetails()
    {
        var fixture = CreateFixture();
        var result = await fixture.Service.UpdateBasicDetailsAsync(fixture.Candidate.Id,
            new(CandidateWorkStatus.Fresher, false, "India", "Pune",
                NoticePeriod: CandidateAvailability.ImmediateJoiner));

        Assert.Equal(CandidateAvailability.ImmediateJoiner, fixture.Candidate.AvailabilityToJoin);
        Assert.Equal(CandidateAvailability.ImmediateJoiner, result.NoticePeriod);
        Assert.Contains("\"noticePeriod\":\"ImmediateJoiner\"",
            JsonSerializer.Serialize(result, WebJson), StringComparison.Ordinal);
    }

    [Fact]
    public async Task CompletionRequiresEmploymentOnlyForExperiencedCandidates()
    {
        var fixture = CreateFixture();
        fixture.Candidate.WorkStatus = CandidateWorkStatus.Fresher;
        fixture.Candidate.CurrentCountry = "India";
        fixture.Candidate.CurrentCity = "Pune";
        var fresher = await fixture.Service.GetProfileCompletionAsync(fixture.Candidate.Id);
        Assert.DoesNotContain("Employment", fresher.MissingSections);

        fixture.Candidate.WorkStatus = CandidateWorkStatus.Experienced;
        var experienced = await fixture.Service.GetProfileCompletionAsync(fixture.Candidate.Id);
        Assert.Contains("Employment", experienced.MissingSections);
        fixture.Repository.HasEmployment = true;
        var completedEmployment = await fixture.Service.GetProfileCompletionAsync(fixture.Candidate.Id);
        Assert.DoesNotContain("Employment", completedEmployment.MissingSections);
    }

    [Fact]
    public void TotalExperienceDoesNotDoubleCountOverlappingEmployment()
    {
        var periods = new CandidateEmploymentPeriod[]
        {
            new(new DateOnly(2020, 1, 1), new DateOnly(2021, 12, 31), false),
            new(new DateOnly(2021, 1, 1), new DateOnly(2022, 12, 31), false)
        };
        Assert.InRange(CandidateService.CalculateTotalExperience(periods, new DateOnly(2026, 1, 1)), 2.9m, 3.1m);
    }

    private static byte[] MinimalPng() => Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");
    private static readonly DateTime Now =
        new(2026, 7, 30, 10, 0, 0, DateTimeKind.Utc);
    private static readonly JsonSerializerOptions WebJson =
        new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task ProfileRequiresOwnedActiveCandidate()
    {
        var fixture = CreateFixture();

        await Assert.ThrowsAsync<UnauthorizedException>(
            () => fixture.Service.GetProfileAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task CandidateCanRetrieveOwnProfile()
    {
        var fixture = CreateFixture();

        var profile = await fixture.Service.GetProfileAsync(fixture.Candidate.Id);

        Assert.Equal(fixture.Candidate.Id, profile.Id);
        Assert.Equal(fixture.Candidate.Email, profile.Email);
        Assert.DoesNotContain("CandidateSkills", JsonSerializer.Serialize(profile, WebJson));
    }

    [Fact]
    public async Task AboutFieldsAreTrimmedAndValidated()
    {
        var fixture = CreateFixture();

        var result = await fixture.Service.UpdateAboutAsync(fixture.Candidate.Id,
            new("  Backend Engineer  ", "  Builds secure services.  "));

        Assert.Equal("Backend Engineer", result.ResumeHeadline);
        Assert.Equal("Builds secure services.", result.ProfileSummary);
        await Assert.ThrowsAsync<FluentValidation.ValidationException>(() =>
            fixture.Service.UpdateAboutAsync(fixture.Candidate.Id,
                new(new string('x', 181), null)));
    }

    [Fact]
    public async Task SkillCrudNormalizesAndRejectsCaseInsensitiveDuplicate()
    {
        var fixture = CreateFixture();
        var created = await fixture.Service.AddSkillAsync(fixture.Candidate.Id,
            new("  C#  ", CandidateSkillProficiency.Advanced, 4));

        Assert.Equal("C#", created.Name);
        await Assert.ThrowsAsync<ConflictException>(() => fixture.Service.AddSkillAsync(
            fixture.Candidate.Id, new("c#")));

        var updated = await fixture.Service.UpdateSkillAsync(fixture.Candidate.Id,
            created.Id, new("ASP.NET Core", CandidateSkillProficiency.Expert, 5));
        Assert.Equal("ASP.NET Core", updated.Name);
        Assert.Single(await fixture.Service.GetSkillsAsync(fixture.Candidate.Id));

        await fixture.Service.DeleteSkillAsync(fixture.Candidate.Id, created.Id);
        Assert.Empty(await fixture.Service.GetSkillsAsync(fixture.Candidate.Id));
    }

    [Fact]
    public async Task CandidateCannotDeleteAnotherCandidatesSkill()
    {
        var fixture = CreateFixture();
        fixture.Repository.Skills.Add(new CandidateSkill
        {
            UserId = Guid.NewGuid(), Name = "SQL", NormalizedName = "SQL"
        });

        await Assert.ThrowsAsync<NotFoundException>(() => fixture.Service.DeleteSkillAsync(
            fixture.Candidate.Id, fixture.Repository.Skills[0].Id));
    }

    [Fact]
    public async Task CompletionUsesExistingResumeAndCareerPreferencesDeterministically()
    {
        var fixture = CreateFixture();
        fixture.Candidate.Headline = "Engineer";
        fixture.Candidate.Bio = "Secure backend specialist";
        fixture.Candidate.ResumeStorageKey = "resume.pdf";
        fixture.Candidate.CareerStage = CareerStage.Experienced;
        fixture.Candidate.Location = "Pune";
        fixture.Candidate.DesiredOpportunitiesJson = "[3]";
        fixture.Candidate.WorkPreferencesJson = "[1]";
        fixture.Candidate.OnboardingCompletedAtUtc = Now;
        fixture.Candidate.WorkStatus = CandidateWorkStatus.Experienced;
        fixture.Candidate.CurrentCountry = "India";
        fixture.Candidate.CurrentCity = "Pune";
        fixture.Repository.HasEducation = true;
        fixture.Repository.HasEmployment = true;
        fixture.Repository.Skills.Add(new CandidateSkill
        {
            UserId = fixture.Candidate.Id, Name = "C#", NormalizedName = "C#"
        });

        var result = await fixture.Service.GetProfileCompletionAsync(fixture.Candidate.Id);

        Assert.Equal(100, result.CompletionPercentage);
        Assert.Empty(result.MissingSections);
        Assert.Equal(7, result.CompletedSections.Count);
        Assert.DoesNotContain("User", JsonSerializer.Serialize(result, WebJson));
    }

    [Fact]
    public async Task IncompleteProfileReturnsStableMissingSections()
    {
        var fixture = CreateFixture();

        var result = await fixture.Service.GetProfileCompletionAsync(fixture.Candidate.Id);

        Assert.Equal(0, result.CompletionPercentage);
        Assert.Equal(
            ["BasicDetails", "Skills", "CareerPreferences", "Education", "Resume", "ProfileSummary"],
            result.MissingSections);
        Assert.Equal("BasicDetails", result.NextRecommendedIncompleteStep);
    }

    [Fact]
    public async Task ActiveCandidateAccessDoesNotDependOnHistoricalEmailFlag()
    {
        var fixture = CreateFixture();
        fixture.Candidate.EmailConfirmed = false;

        var profile = await fixture.Service.GetProfileAsync(
            fixture.Candidate.Id);
        await fixture.Service.GetSavedJobsAsync(
            fixture.Candidate.Id,
            new());
        await fixture.Service.SaveJobAsync(
            fixture.Candidate.Id,
            fixture.Job.Id);
        var application = await fixture.Service.ApplyAsync(
            fixture.Candidate.Id,
            fixture.Job.Id,
            new(null));

        Assert.Equal(fixture.Candidate.Id, profile.Id);
        Assert.Single(fixture.Dashboard.Added);
        Assert.Equal(
            JobApplicationStatus.Submitted,
            application.Status);
    }

    [Fact]
    public async Task ApplicationQuotaReportsFreeMonthlyAllowance()
    {
        var fixture = CreateFixture();
        fixture.Repository.HasMembership = false;

        var response = await fixture.Service.GetApplicationQuotaAsync(fixture.Candidate.Id);

        Assert.Equal("Free", response.Plan);
        Assert.False(response.IsPremium);
        Assert.Equal(10, response.Limit);
        Assert.Equal(0, response.UsedApplications);
        Assert.Equal(10, response.RemainingApplications);
        Assert.True(response.ResetsAtUtc > Now);
    }

    [Fact]
    public async Task ApplicationQuotaReportsPremiumDailyAllowance()
    {
        var fixture = CreateFixture();
        fixture.Repository.HasMembership = true;

        var response = await fixture.Service.GetApplicationQuotaAsync(fixture.Candidate.Id);

        Assert.Equal("Premium", response.Plan);
        Assert.True(response.IsPremium);
        Assert.Equal(35, response.Limit);
        Assert.Equal(0, response.UsedApplications);
        Assert.Equal(35, response.RemainingApplications);
        Assert.True(response.ResetsAtUtc > Now);
    }

    [Theory]
    [InlineData(CareerStage.Student, DesiredOpportunity.Internship, null)]
    [InlineData(CareerStage.Fresher, DesiredOpportunity.FresherJob, null)]
    [InlineData(CareerStage.Experienced, DesiredOpportunity.ExperiencedJob, 4.5)]
    public async Task OnboardingSupportsEveryCareerStage(
        CareerStage careerStage,
        DesiredOpportunity desiredOpportunity,
        double? yearsOfExperience)
    {
        var fixture = CreateFixture();
        var request = new UpdateCandidateOnboardingRequest(
            careerStage,
            [desiredOpportunity],
            "  Pune  ",
            [" C# ", "SQL"],
            [WorkPreference.Remote, WorkPreference.Hybrid],
            careerStage == CareerStage.Experienced ? null : "Example Institute",
            careerStage == CareerStage.Experienced ? null : "B.Tech",
            careerStage == CareerStage.Experienced ? null : 2026,
            yearsOfExperience.HasValue ? (decimal)yearsOfExperience.Value : null);

        var response = await fixture.Service.UpdateOnboardingAsync(
            fixture.Candidate.Id, request);

        Assert.Equal(careerStage, response.CareerStage);
        Assert.Equal("Pune", response.City);
        Assert.Equal(["C#", "SQL"], response.Skills);
        Assert.Equal(Now, response.CompletedAtUtc);
        Assert.Equal(yearsOfExperience, (double?)response.YearsOfExperience);
        var audit = Assert.Single(fixture.Audit.Events);
        var auditJson = JsonSerializer.Serialize(audit);
        Assert.Equal("CandidateOnboarding", audit.EntityType);
        Assert.DoesNotContain("Pune", auditJson, StringComparison.Ordinal);
        Assert.DoesNotContain("C#", auditJson, StringComparison.Ordinal);
        Assert.DoesNotContain("Institute", auditJson, StringComparison.Ordinal);
        Assert.DoesNotContain(
            fixture.Candidate.Email, auditJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task OnboardingRejectsInvalidListsEnumsYearsAndOversizedSkills()
    {
        var validator = new UpdateCandidateOnboardingRequestValidator(
            new FixedTimeProvider(Now));
        var invalidRequests = new[]
        {
            ValidOnboarding() with { DesiredOpportunities = [] },
            ValidOnboarding() with
            {
                DesiredOpportunities =
                [
                    DesiredOpportunity.Internship,
                    DesiredOpportunity.Internship
                ]
            },
            ValidOnboarding() with { Skills = ["C#", " c# "] },
            ValidOnboarding() with { Skills = [""] },
            ValidOnboarding() with
            {
                Skills = Enumerable.Range(1, 21).Select(index => $"Skill {index}").ToArray()
            },
            ValidOnboarding() with { CareerStage = (CareerStage)99 },
            ValidOnboarding() with { WorkPreferences = [(WorkPreference)99] },
            ValidOnboarding() with { GraduationYear = Now.Year + 11 },
            ValidOnboarding() with
            {
                CareerStage = CareerStage.Experienced,
                YearsOfExperience = null
            },
            ValidOnboarding() with { YearsOfExperience = 51 }
        };

        foreach (var request in invalidRequests)
            Assert.False((await validator.ValidateAsync(request)).IsValid);
    }

    [Fact]
    public async Task OnboardingIsOwnerScopedAndRejectsAdministratorAccounts()
    {
        var fixture = CreateFixture();
        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            fixture.Service.GetOnboardingAsync(Guid.NewGuid()));

        fixture.Candidate.RoleId = SystemRoleIds.Administrator;
        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            fixture.Service.UpdateOnboardingAsync(
                fixture.Candidate.Id, ValidOnboarding()));
    }

    [Fact]
    public void OnboardingContractRejectsInternalOverPosting()
    {
        var json =
            """
            {
              "careerStage":1,
              "desiredOpportunities":[1],
              "city":"Pune",
              "skills":["C#"],
              "workPreferences":[1],
              "userId":"00000000-0000-0000-0000-000000000001",
              "role":"Administrator"
            }
            """;

        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<UpdateCandidateOnboardingRequest>(
                json, WebJson));
    }

    [Fact]
    public async Task FreeCandidateCanApplyWhenBelowMonthlyLimit()
    {
        var fixture = CreateFixture();
        fixture.Repository.HasMembership = false;

        await fixture.Service.ApplyAsync(
            fixture.Candidate.Id,
            fixture.Job.Id,
            new(null));

        Assert.Single(fixture.Repository.AddedApplications);

        fixture = CreateFixture();
        fixture.Repository.AvailableJob = null;

        await Assert.ThrowsAsync<NotFoundException>(() =>
            fixture.Service.ApplyAsync(
                fixture.Candidate.Id,
                fixture.Job.Id,
                new(null)));
    }
    [Fact]
    public async Task FreeCandidateAtMonthlyLimitCannotCreateAnEleventhApplication()
    {
        var fixture = CreateFixture();
        fixture.Repository.HasMembership = false;

        await fixture.Service.ApplyAsync(
            fixture.Candidate.Id,
            fixture.Job.Id,
            new("First application"));

        var usage = Assert.Single(fixture.Repository.AddedQuotaUsages);

        Assert.Equal(ApplicationQuotaPeriod.FreeMonthly, usage.Period);
        Assert.Equal(1, usage.UsedApplications);

        usage.UsedApplications = 9;

        await fixture.Service.ApplyAsync(
            fixture.Candidate.Id,
            fixture.Job.Id,
            new("Tenth application"));

        Assert.Equal(10, usage.UsedApplications);

        var exception = await Assert.ThrowsAsync<ApplicationQuotaExceededException>(() =>
            fixture.Service.ApplyAsync(
                fixture.Candidate.Id,
                fixture.Job.Id,
                new("Another application")));

        Assert.Equal("MONTHLY_JOB_LIMIT_REACHED", exception.Code);
        Assert.True(exception.RedirectToMembership);
        Assert.Equal(
            "You've reached your monthly application limit of 10 jobs. Upgrade to Premium for more applications.",
            exception.Message);
        Assert.Equal(2, fixture.Repository.AddedApplications.Count);
    }

    [Fact]
    public async Task PremiumCandidateCanApplyWhenBelowDailyLimit()
    {
        var fixture = CreateFixture();
        fixture.Repository.HasMembership = true;

        await fixture.Service.ApplyAsync(
            fixture.Candidate.Id,
            fixture.Job.Id,
            new("First application"));

        Assert.Single(fixture.Repository.AddedApplications);
        Assert.Equal(1, Assert.Single(fixture.Repository.AddedQuotaUsages).UsedApplications);
    }

    [Fact]
    public async Task PremiumCandidateAtDailyLimitCannotCreateAThirtySixthApplication()
    {
        var fixture = CreateFixture();
        fixture.Repository.HasMembership = true;

        await fixture.Service.ApplyAsync(
            fixture.Candidate.Id,
            fixture.Job.Id,
            new("First application"));

        var usage = Assert.Single(fixture.Repository.AddedQuotaUsages);

        Assert.Equal(ApplicationQuotaPeriod.PremiumDaily, usage.Period);
        Assert.Equal(1, usage.UsedApplications);

        usage.UsedApplications = 34;

        await fixture.Service.ApplyAsync(
            fixture.Candidate.Id,
            fixture.Job.Id,
            new("Thirty-fifth application"));

        Assert.Equal(35, usage.UsedApplications);

        var exception = await Assert.ThrowsAsync<ApplicationQuotaExceededException>(() =>
            fixture.Service.ApplyAsync(
                fixture.Candidate.Id,
                fixture.Job.Id,
                new("Another application")));

        Assert.Equal("DAILY_JOB_LIMIT_REACHED", exception.Code);
        Assert.False(exception.RedirectToMembership);
        Assert.Equal(
            "You've reached today's application limit of 35 jobs. Please try again tomorrow.",
            exception.Message);
        Assert.Equal(2, fixture.Repository.AddedApplications.Count);
    }

    [Fact]
    public async Task FreeMonthlyQuotaResetsForANewCalendarMonth()
    {
        var fixture = CreateFixture(new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc));
        fixture.Repository.HasMembership = false;
        fixture.Repository.AddedQuotaUsages.Add(new ApplicationQuotaUsage
        {
            UserId = fixture.Candidate.Id,
            Period = ApplicationQuotaPeriod.FreeMonthly,
            PeriodStartsAtUtc = new DateTime(2026, 6, 30, 18, 30, 0, DateTimeKind.Utc),
            PeriodEndsAtUtc = new DateTime(2026, 7, 31, 18, 30, 0, DateTimeKind.Utc),
            UsedApplications = 10
        });

        await fixture.Service.ApplyAsync(fixture.Candidate.Id, fixture.Job.Id, new(null));

        Assert.Single(fixture.Repository.AddedApplications);
        Assert.Equal(2, fixture.Repository.AddedQuotaUsages.Count);
        Assert.Equal(1, fixture.Repository.AddedQuotaUsages.Single(usage =>
            usage.PeriodStartsAtUtc == new DateTime(2026, 7, 31, 18, 30, 0, DateTimeKind.Utc))
            .UsedApplications);
    }

    [Fact]
    public async Task PremiumDailyQuotaResetsForANewCalendarDay()
    {
        var fixture = CreateFixture(new DateTime(2026, 8, 2, 18, 31, 0, DateTimeKind.Utc));
        fixture.Repository.HasMembership = true;
        fixture.Repository.AddedQuotaUsages.Add(new ApplicationQuotaUsage
        {
            UserId = fixture.Candidate.Id,
            Period = ApplicationQuotaPeriod.PremiumDaily,
            PeriodStartsAtUtc = new DateTime(2026, 8, 1, 18, 30, 0, DateTimeKind.Utc),
            PeriodEndsAtUtc = new DateTime(2026, 8, 2, 18, 30, 0, DateTimeKind.Utc),
            UsedApplications = 35
        });

        await fixture.Service.ApplyAsync(fixture.Candidate.Id, fixture.Job.Id, new(null));

        Assert.Single(fixture.Repository.AddedApplications);
        Assert.Equal(2, fixture.Repository.AddedQuotaUsages.Count);
        Assert.Equal(1, fixture.Repository.AddedQuotaUsages.Single(usage =>
            usage.PeriodStartsAtUtc == new DateTime(2026, 8, 2, 18, 30, 0, DateTimeKind.Utc))
            .UsedApplications);
    }

    [Fact]
    public async Task QuotaExceededReturnsTheRequiredForbiddenResponse()
    {
        var context = new DefaultHttpContext();
        await using var responseBody = new MemoryStream();
        context.Response.Body = responseBody;
        var middleware = new GlobalExceptionMiddleware(
            _ => Task.FromException(new ApplicationQuotaExceededException(
                "MONTHLY_JOB_LIMIT_REACHED",
                "You've reached your monthly application limit of 10 jobs. Upgrade to Premium for more applications.",
                true)),
            NullLogger<GlobalExceptionMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        responseBody.Position = 0;
        var response = await JsonSerializer.DeserializeAsync<ApplicationQuotaLimitErrorResponse>(
            responseBody,
            WebJson);

        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
        Assert.NotNull(response);
        Assert.False(response.Success);
        Assert.Equal("MONTHLY_JOB_LIMIT_REACHED", response.Code);
        Assert.True(response.RedirectToMembership);
    }

    [Fact]
    public async Task RecruiterContactRequiresMembershipAndDoesNotWritePrivateDataToAudit()
    {
        var fixture = CreateFixture();

        fixture.Repository.ApprovedRecruiterContact = new CandidateRecruiterContact(
            fixture.Job.Id,
            fixture.Job.Title,
            fixture.Job.Slug,
            "Example Co",
            "Priya Sharma",
            "Campus Recruiter",
            "priya.sharma@example.test",
            "+919876543210");

        fixture.Repository.HasMembership = false;

        await Assert.ThrowsAsync<ConflictException>(() =>
            fixture.Service.GetRecruiterContactAsync(
                fixture.Candidate.Id,
                fixture.Job.Id));

        fixture.Repository.HasMembership = true;

        var response = await fixture.Service.GetRecruiterContactAsync(
            fixture.Candidate.Id,
            fixture.Job.Id);

        Assert.Equal("Priya Sharma", response.ContactName);
        Assert.Equal("Campus Recruiter", response.ContactRole);
        Assert.Equal("priya.sharma@example.test", response.Email);
        Assert.Equal("+919876543210", response.PhoneNumber);

        var auditJson = JsonSerializer.Serialize(Assert.Single(fixture.Audit.Events));

        Assert.Contains("\"access\":\"membership\"", auditJson);
        Assert.DoesNotContain(response.Email, auditJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(response.PhoneNumber!, auditJson, StringComparison.Ordinal);
        Assert.DoesNotContain(response.ContactName, auditJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RecruiterContactIsUnavailableWhenNotApprovedOrNotPresent()
    {
        var fixture = CreateFixture();

        await Assert.ThrowsAsync<NotFoundException>(() =>
            fixture.Service.GetRecruiterContactAsync(
                fixture.Candidate.Id,
                fixture.Job.Id));
    }

    [Fact]
    public async Task DuplicateApplicationReturnsExistingApplication()
    {
        var fixture = CreateFixture();
        fixture.Repository.ExistingApplication = fixture.CreateApplication(JobApplicationStatus.Submitted);

        var response = await fixture.Service.ApplyAsync(fixture.Candidate.Id, fixture.Job.Id, new("Interested"));
        Assert.Equal(fixture.Repository.ExistingApplication.Id, response.Id);
        Assert.Empty(fixture.Repository.AddedApplications);
        Assert.Empty(fixture.Dashboard.Notifications);
    }

    [Fact]
    public async Task ApplicationUsesCurrentResumeAndCannotReadAnotherCandidatesApplication()
    {
        var fixture = CreateFixture();
        fixture.Candidate.ResumeStorageKey = "current.pdf";
        fixture.Candidate.ResumeFileName = "resume.pdf";

        var submitted = await fixture.Service.ApplyAsync(
            fixture.Candidate.Id, fixture.Job.Id, new("Interested"));

        Assert.Equal("resume.pdf", submitted.ResumeFileName);
        Assert.Equal("current.pdf", fixture.Repository.AddedApplications.Single().ResumeStorageKey);
        Assert.Contains(
            fixture.Audit.Events,
            audit => audit.Action == AuditAction.Submit &&
                audit.EntityId == fixture.Repository.AddedApplications.Single().Id.ToString());
        var history = Assert.Single(fixture.Repository.AddedApplications.Single().StatusHistory);
        Assert.Null(history.PreviousStatus);
        Assert.Equal(JobApplicationStatus.Submitted, history.NewStatus);
        Assert.Equal(fixture.Candidate.Id, history.ActorUserId);
        await Assert.ThrowsAsync<NotFoundException>(() =>
            fixture.Service.GetApplicationAsync(fixture.Candidate.Id, Guid.NewGuid()));
    }

    [Fact]
    public async Task OnlySubmittedApplicationCanBeWithdrawn()
    {
        var fixture = CreateFixture();
        var submitted = fixture.CreateApplication(JobApplicationStatus.Submitted);
        fixture.Repository.OwnedApplication = submitted;

        var response = await fixture.Service.WithdrawAsync(fixture.Candidate.Id, submitted.Id);

        Assert.Equal(JobApplicationStatus.Withdrawn, response.Status);
        Assert.NotNull(submitted.WithdrawnAtUtc);
        var history = Assert.Single(submitted.StatusHistory);
        Assert.Equal(JobApplicationStatus.Submitted, history.PreviousStatus);
        Assert.Equal(JobApplicationStatus.Withdrawn, history.NewStatus);
        Assert.Equal(fixture.Candidate.Id, history.ActorUserId);

        fixture.Repository.OwnedApplication = fixture.CreateApplication(JobApplicationStatus.Reviewed);
        await Assert.ThrowsAsync<ConflictException>(() =>
            fixture.Service.WithdrawAsync(fixture.Candidate.Id, fixture.Repository.OwnedApplication.Id));
    }

    [Theory]
    [InlineData("resume.exe", "application/pdf")]
    [InlineData("resume.pdf", "text/plain")]
    [InlineData("resume.pdf", "application/pdf")]
    public async Task ResumeRejectsInvalidExtensionContentTypeOrSignature(string name, string contentType)
    {
        var fixture = CreateFixture();
        await using var stream = new MemoryStream("not a resume"u8.ToArray());

        await Assert.ThrowsAsync<BadRequestException>(() => fixture.Service.UploadResumeAsync(
            fixture.Candidate.Id, new(stream, stream.Length, name, contentType)));
        Assert.Empty(fixture.Storage.Stored);
    }

    [Fact]
    public async Task ResumeReplacementUsesServerKeyAndPreservesApplicationSnapshot()
    {
        var fixture = CreateFixture();
        fixture.Candidate.ResumeStorageKey = "prior.pdf";
        fixture.Repository.ReferencedResumeKeys.Add("prior.pdf");
        await using var stream = new MemoryStream("%PDF-1.7 test"u8.ToArray());

        var response = await fixture.Service.UploadResumeAsync(fixture.Candidate.Id,
            new(stream, stream.Length, "../../unsafe.pdf", "application/pdf"));

        Assert.Equal("unsafe.pdf", response.FileName);
        Assert.Single(fixture.Storage.Stored);
        Assert.DoesNotContain("prior.pdf", fixture.Storage.Deleted);
        Assert.DoesNotContain("unsafe", fixture.Storage.Stored.Single(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            fixture.Audit.Events,
            audit => audit.Action == AuditAction.Upload &&
                !JsonSerializer.Serialize(audit).Contains(
                    "unsafe", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SavingAJobIsIdempotentAndUnavailableJobsAreRejected()
    {
        var fixture = CreateFixture();
        fixture.Dashboard.AlreadySaved = true;
        await fixture.Service.SaveJobAsync(fixture.Candidate.Id, fixture.Job.Id);
        Assert.Empty(fixture.Dashboard.Added);

        fixture.Dashboard.AlreadySaved = false;
        fixture.Dashboard.JobAvailable = false;
        await Assert.ThrowsAsync<NotFoundException>(
            () => fixture.Service.SaveJobAsync(fixture.Candidate.Id, fixture.Job.Id));
    }

    [Fact]
    public async Task ApplicationListEnforcesPaginationAndPassesOwnerScope()
    {
        var fixture = CreateFixture();
        await Assert.ThrowsAnyAsync<FluentValidation.ValidationException>(() =>
            fixture.Service.GetApplicationsAsync(fixture.Candidate.Id, new(0, 20)));

        await fixture.Service.GetApplicationsAsync(
            fixture.Candidate.Id, new(2, 10, JobApplicationStatus.Shortlisted));

        Assert.Equal(fixture.Candidate.Id, fixture.Repository.ListedForUserId);
        Assert.Equal(2, fixture.Repository.LastQuery!.PageNumber);
        Assert.Equal(10, fixture.Repository.LastQuery.PageSize);
    }

    [Fact]
    public async Task ResumePreservesOriginalUnicodeDisplayNameForProfileAndDownloadWithoutExposingStorageKey()
    {
        var fixture = CreateFixture();
        const string displayName = "My Resume FINAL résumé.pdf";
        await using var stream = new MemoryStream("%PDF-1.7 test"u8.ToArray());
        var uploaded = await fixture.Service.UploadResumeAsync(fixture.Candidate.Id,
            new(stream, stream.Length, displayName, "application/pdf"));
        var profile = await fixture.Service.GetProfileAsync(fixture.Candidate.Id);
        var downloaded = await fixture.Service.DownloadResumeAsync(fixture.Candidate.Id);
        var json = JsonSerializer.Serialize(profile, WebJson);
        Assert.Equal(displayName, uploaded.FileName);
        Assert.Equal(displayName, profile.Resume!.FileName);
        Assert.Equal(displayName, downloaded.FileName);
        Assert.DoesNotContain(fixture.Candidate.ResumeStorageKey!, json, StringComparison.Ordinal);
        var controller = new CandidateController(fixture.Service)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, fixture.Candidate.Id.ToString()),
                         new Claim(ClaimTypes.Role, "Candidate")], "test"))
                }
            }
        };
        var file = Assert.IsType<FileStreamResult>(await controller.DownloadResume(default));
        Assert.Equal(displayName, file.FileDownloadName);
        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            fixture.Service.DownloadResumeAsync(Guid.NewGuid()));
    }

    [Theory]
    [InlineData(@"C:\Users\Candidate\Manoj_Shekapure_Resume.pdf", "Manoj_Shekapure_Resume.pdf")]
    [InlineData("../../Manoj_Shekapure_Resume.pdf", "Manoj_Shekapure_Resume.pdf")]
    public async Task ResumePathLikeNamesAreReducedToSafeBasename(string supplied, string expected)
    {
        var fixture = CreateFixture();
        await using var stream = new MemoryStream("%PDF-1.7 test"u8.ToArray());
        var response = await fixture.Service.UploadResumeAsync(fixture.Candidate.Id,
            new(stream, stream.Length, supplied, "application/pdf"));
        Assert.Equal(expected, response.FileName);
        Assert.DoesNotContain(expected, fixture.Storage.Stored.Single(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LegacyResumeWithoutDisplayNameUsesSafeFallback()
    {
        var fixture = CreateFixture();
        fixture.Candidate.ResumeStorageKey = "generated-private-key";
        fixture.Candidate.ResumeFileName = null;
        fixture.Candidate.ResumeContentType = "application/pdf";
        fixture.Candidate.ResumeSizeBytes = 12;
        fixture.Candidate.ResumeUploadedAtUtc = Now;
        fixture.Storage.Content["generated-private-key"] = "%PDF-1.7 old"u8.ToArray();
        var profile = await fixture.Service.GetProfileAsync(fixture.Candidate.Id);
        var download = await fixture.Service.DownloadResumeAsync(fixture.Candidate.Id);
        Assert.Equal("resume.pdf", profile.Resume!.FileName);
        Assert.Equal("resume.pdf", download.FileName);
    }

    [Fact]
    public async Task PortalApplyCreatesApplicationAndNotificationAndRetryIsIdempotent()
    {
        var fixture = CreateFixture();
        var first = await fixture.Service.ApplyJobAsync(fixture.Candidate.Id, fixture.Job.Id,
            new(ApplicationMethod: ApplicationMethod.Portal));
        var created = Assert.Single(fixture.Repository.AddedApplications);
        created.Job = fixture.Job;
        fixture.Repository.ExistingApplication = created;
        var second = await fixture.Service.ApplyJobAsync(fixture.Candidate.Id, fixture.Job.Id,
            new(ApplicationMethod: ApplicationMethod.Portal));

        Assert.Equal(first.ApplicationId, second.ApplicationId);
        Assert.Equal(JobApplicationStatus.Submitted, first.ApplicationStatus);
        Assert.Equal(ApplicationMethod.Portal, first.ApplicationMethod);
        var notification = Assert.Single(fixture.Dashboard.Notifications);
        Assert.Equal("Application submitted", notification.Title);
    }

    [Fact]
    public async Task ExternalApplyRecordsIntentWithoutClaimingEmployerSubmission()
    {
        var fixture = CreateFixture();
        fixture.Repository.AvailableJob = fixture.Repository.AvailableJob! with
        {
            ApplicationUrl = "https://employer.example/apply"
        };

        var response = await fixture.Service.ApplyJobAsync(fixture.Candidate.Id, fixture.Job.Id,
            new(ApplicationMethod: ApplicationMethod.External));

        Assert.Equal(JobApplicationStatus.ExternalApplicationStarted, response.ApplicationStatus);
        Assert.Equal(ApplicationMethod.External, response.ApplicationMethod);
        Assert.Equal("External application started", Assert.Single(fixture.Dashboard.Notifications).Title);
    }

    [Fact]
    public async Task AppliedJobsAreExcludedFromCandidateBrowseAndRecommendations()
    {
        var fixture = CreateFixture();
        var candidateJob = new RecommendationJobCandidate(fixture.Job.Id, "JOB-1", "C# Engineer",
            fixture.Job.Slug, "C# SQL", null, null, fixture.Job.CompanyId, fixture.Job.Company.Name,
            fixture.Job.Company.Slug, null, "Technology", Guid.NewGuid(), "Engineering", "Pune",
            EmploymentType.FullTime, WorkplaceType.Hybrid, ExperienceLevel.Mid, false, Now, null);
        fixture.Repository.RecommendationJobs = [candidateJob];
        fixture.Repository.AppliedJobIds.Add(fixture.Job.Id);
        fixture.Repository.ResumeProfile = new CandidateResumeProfile
        {
            UserId = fixture.Candidate.Id,
            ExtractionStatus = ResumeExtractionStatus.Ready,
            SkillsJson = "[\"c#\"]"
        };

        var browse = await fixture.Service.GetBrowseJobsAsync(fixture.Candidate.Id, new());
        var recommended = await fixture.Service.GetRecommendedJobsAsync(fixture.Candidate.Id, new());

        Assert.Empty(browse.Items);
        Assert.Empty(recommended.Items);
    }

    private static Fixture CreateFixture(DateTime? nowUtc = null)
    {
        var candidate = new User
        {
            Id = Guid.NewGuid(),
            Email = "candidate@example.test",
            FirstName = "Casey",
            LastName = "Patel",
            RoleId = SystemRoleIds.Candidate,
            EmailConfirmed = true,
            Status = UserStatus.Active
        };
        var company = new Company { Id = Guid.NewGuid(), Name = "Example Co", Slug = "example" };
        var job = new Job
        {
            Id = Guid.NewGuid(),
            CompanyId = company.Id,
            Company = company,
            Title = "Engineer",
            Slug = "engineer",
            Status = JobStatus.Published
        };
        var repository = new FakeCandidateRepository
        {
            Candidate = candidate,
            AvailableJob = new(job.Id, job.Title, job.Slug, company.Name)
        };
        var dashboard = new FakeDashboardRepository();
        var storage = new FakeResumeStorage();
        var photoStorage = new FakeProfilePhotoStorage();
        var unitOfWork = new FakeUnitOfWork();
        var audit = new AuditWriterTestDouble();
        var timeProvider = new FixedTimeProvider(nowUtc ?? Now);
        var service = new CandidateService(
            repository, dashboard, storage, photoStorage, unitOfWork, audit,
            new UpdateCandidateProfileRequestValidator(),
            new UpdateCandidateAboutRequestValidator(),
            new UpsertCandidateSkillRequestValidator(),
            new UpdateCandidateOnboardingRequestValidator(timeProvider),
            new UpdateCandidateBasicDetailsRequestValidator(),
            new UpdateCandidateCareerPreferencesRequestValidator(),
            new CandidatePageQueryValidator(),
            new JobApplicationQueryValidator(), new CreateJobApplicationRequestValidator(),
            timeProvider);
        return new(service, repository, dashboard, storage, unitOfWork, audit, candidate, job);
    }

    private sealed record Fixture(
        CandidateService Service,
        FakeCandidateRepository Repository,
        FakeDashboardRepository Dashboard,
        FakeResumeStorage Storage,
        FakeUnitOfWork UnitOfWork,
        AuditWriterTestDouble Audit,
        User Candidate,
        Job Job)
    {
        public JobApplication CreateApplication(JobApplicationStatus status) => new()
        {
            UserId = Candidate.Id,
            JobId = Job.Id,
            Job = Job,
            Status = status,
            SubmittedAtUtc = DateTime.UtcNow
        };
    }

    private sealed class FakeCandidateRepository : ICandidateRepository
    {
        public User? Candidate { get; set; }
        public CandidateJob? AvailableJob { get; set; }
        public CandidateRecruiterContact? ApprovedRecruiterContact { get; set; }
        public bool HasMembership { get; set; } = true;
        public bool HasPriorApplication { get; set; }
        public JobApplication? OwnedApplication { get; set; }
        public List<JobApplication> AddedApplications { get; } = [];
        public HashSet<string> ReferencedResumeKeys { get; } = [];
        public Guid? ListedForUserId { get; private set; }
        public JobApplicationQuery? LastQuery { get; private set; }
        public List<ApplicationQuotaUsage> AddedQuotaUsages { get; } = [];
        public CandidateResumeProfile? ResumeProfile { get; set; }
        public IReadOnlyCollection<RecommendationJobCandidate> RecommendationJobs { get; set; } = [];
        public HashSet<Guid> AppliedJobIds { get; } = [];
        public JobApplication? ExistingApplication { get; set; }
        public List<CandidateSkill> Skills { get; } = [];
        public bool HasEducation { get; set; }
        public bool HasEmployment { get; set; }
        public IReadOnlyCollection<CandidateEmploymentPeriod> EmploymentPeriods { get; set; } = [];

        public Task<User?> GetCandidateAsync(Guid userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(
                Candidate?.Id == userId &&
                Candidate.RoleId == SystemRoleIds.Candidate &&
                Candidate.Status == UserStatus.Active
                    ? Candidate
                    : null);
        public Task<IReadOnlyCollection<CandidateSkill>> GetSkillsAsync(
            Guid userId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyCollection<CandidateSkill>>(
                Skills.Where(x => x.UserId == userId && !x.IsDeleted).ToArray());
        public Task<(bool HasEducation, bool HasEmployment)> GetProfileRecordPresenceAsync(
            Guid userId, CancellationToken cancellationToken = default) =>
            Task.FromResult((HasEducation, HasEmployment));
        public Task<IReadOnlyCollection<CandidateEmploymentPeriod>> GetEmploymentPeriodsAsync(
            Guid userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(EmploymentPeriods);
        public Task<CandidateSkill?> GetSkillAsync(
            Guid userId, Guid skillId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Skills.SingleOrDefault(x => x.UserId == userId &&
                x.Id == skillId && !x.IsDeleted));
        public Task<bool> SkillNameExistsAsync(
            Guid userId, string normalizedName, Guid? excludingSkillId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Skills.Any(x => x.UserId == userId && !x.IsDeleted &&
                x.NormalizedName == normalizedName &&
                (!excludingSkillId.HasValue || x.Id != excludingSkillId.Value)));
        public Task AddSkillAsync(
            CandidateSkill skill, CancellationToken cancellationToken = default)
        {
            Skills.Add(skill);
            return Task.CompletedTask;
        }
        public void RemoveSkill(CandidateSkill skill) => skill.IsDeleted = true;
        public Task<CandidateJob?> GetAvailableJobAsync(Guid jobId, CancellationToken cancellationToken = default) =>
            Task.FromResult(AvailableJob?.Id == jobId ? AvailableJob : null);
        public Task<CandidateResumeProfile?> GetResumeProfileAsync(Guid userId, bool tracking, CancellationToken cancellationToken = default) =>
            Task.FromResult(ResumeProfile?.UserId == userId ? ResumeProfile : null);
        public Task AddResumeProfileAsync(CandidateResumeProfile profile, CancellationToken cancellationToken = default)
        {
            ResumeProfile = profile;
            return Task.CompletedTask;
        }
        public Task<IReadOnlyCollection<RecommendationJobCandidate>> GetRecommendationCandidatesAsync(Guid userId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyCollection<RecommendationJobCandidate>>(RecommendationJobs.Where(x => !AppliedJobIds.Contains(x.Id)).ToArray());
        public Task<(IReadOnlyCollection<RecommendationJobCandidate> Items, int TotalCount)> GetCandidateBrowseJobsAsync(
            Guid userId, CandidatePageQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult(((IReadOnlyCollection<RecommendationJobCandidate>)RecommendationJobs.Where(x => !AppliedJobIds.Contains(x.Id)).ToArray(), RecommendationJobs.Count(x => !AppliedJobIds.Contains(x.Id))));
        public Task<CandidateRecruiterContact?> GetApprovedRecruiterContactForAvailableJobAsync(
    Guid jobId,
    CancellationToken cancellationToken = default) =>
    Task.FromResult(
        ApprovedRecruiterContact?.JobId == jobId
            ? ApprovedRecruiterContact
            : null);
        public Task<bool> HasActiveMembershipAsync(Guid userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(HasMembership);
        public Task<bool> IsResumeReferencedAsync(string storageKey, CancellationToken cancellationToken = default) =>
            Task.FromResult(ReferencedResumeKeys.Contains(storageKey));
        public Task<JobApplication?> GetApplicationAsync(
            Guid userId, Guid applicationId, CancellationToken cancellationToken = default) =>
            Task.FromResult(OwnedApplication?.UserId == userId && OwnedApplication.Id == applicationId
                ? OwnedApplication
                : null);
        public Task<bool> HasApplicationAsync(
            Guid userId, Guid jobId, CancellationToken cancellationToken = default) =>
            Task.FromResult(HasPriorApplication);
        public Task<JobApplication?> GetApplicationByJobAsync(Guid userId, Guid jobId, CancellationToken cancellationToken = default) =>
            Task.FromResult(ExistingApplication?.UserId == userId && ExistingApplication.JobId == jobId ? ExistingApplication : null);
        public Task<ApplicationQuotaUsage?> GetQuotaUsageAsync(
    Guid userId,
    ApplicationQuotaPeriod period,
    DateTime periodStartsAtUtc,
    CancellationToken cancellationToken = default) =>
    Task.FromResult(
        AddedQuotaUsages.SingleOrDefault(usage =>
            usage.UserId == userId &&
            usage.Period == period &&
            usage.PeriodStartsAtUtc == periodStartsAtUtc));

        public Task AddQuotaUsageAsync(
            ApplicationQuotaUsage quotaUsage,
            CancellationToken cancellationToken = default)
        {
            AddedQuotaUsages.Add(quotaUsage);
            return Task.CompletedTask;
        }
        public Task AddApplicationAsync(
            JobApplication application, CancellationToken cancellationToken = default)
        {
            AddedApplications.Add(application);
            return Task.CompletedTask;
        }
        public Task<(IReadOnlyCollection<JobApplicationResponse> Items, int TotalCount)> GetApplicationsAsync(
            Guid userId, JobApplicationQuery query, CancellationToken cancellationToken = default)
        {
            ListedForUserId = userId;
            LastQuery = query;
            return Task.FromResult(((IReadOnlyCollection<JobApplicationResponse>)[], 0));
        }
    }

    private sealed class FakeDashboardRepository : IDashboardRepository
    {
        public bool JobAvailable { get; set; } = true;
        public bool AlreadySaved { get; set; }
        public List<SavedJob> Added { get; } = [];
        public List<Notification> Notifications { get; } = [];
        public Task<User?> GetUserAsync(Guid userId, CancellationToken cancellationToken = default) =>
            Task.FromResult<User?>(null);
        public Task<(IReadOnlyCollection<SavedJobResponse> Items, int TotalCount)> GetSavedJobsAsync(
            Guid userId, DashboardQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult(((IReadOnlyCollection<SavedJobResponse>)[], 0));
        public Task<bool> IsAvailableJobAsync(Guid jobId, CancellationToken cancellationToken = default) =>
            Task.FromResult(JobAvailable);
        public Task<bool> IsJobSavedAsync(
            Guid userId, Guid jobId, CancellationToken cancellationToken = default) =>
            Task.FromResult(AlreadySaved);
        public Task AddSavedJobAsync(SavedJob savedJob, CancellationToken cancellationToken = default)
        {
            Added.Add(savedJob);
            return Task.CompletedTask;
        }
        public Task<SavedJob?> GetSavedJobAsync(
            Guid userId, Guid jobId, CancellationToken cancellationToken = default) =>
            Task.FromResult<SavedJob?>(null);
        public void RemoveSavedJob(SavedJob savedJob) { }
        public Task<(IReadOnlyCollection<AppliedJobHistoryResponse> Items, int TotalCount)> GetAppliedJobsAsync(
            Guid userId, DashboardQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult(((IReadOnlyCollection<AppliedJobHistoryResponse>)[], 0));
        public Task<(IReadOnlyCollection<NotificationResponse> Items, int TotalCount, int UnreadCount)> GetNotificationsAsync(
            Guid userId, DashboardQuery query, bool? isRead, CancellationToken cancellationToken = default) =>
            Task.FromResult(((IReadOnlyCollection<NotificationResponse>)[], 0, 0));
        public Task<Notification?> GetNotificationAsync(
            Guid userId, Guid notificationId, CancellationToken cancellationToken = default) =>
            Task.FromResult<Notification?>(null);
        public Task<int> MarkAllNotificationsReadAsync(
            Guid userId, DateTime readAtUtc, CancellationToken cancellationToken = default) =>
            Task.FromResult(0);

        // 👇 ADD THIS MISSING METHOD IMPLEMENTATION
        public Task AddNotificationAsync(Notification notification, CancellationToken cancellationToken = default)
        {
            Notifications.Add(notification);
            return Task.CompletedTask;
        }
    }
    private sealed class FakeResumeStorage : IResumeStorage
    {
        public List<string> Stored { get; } = [];
        public List<string> Deleted { get; } = [];
        public Dictionary<string, byte[]> Content { get; } = [];
        public Task<string> StoreAsync(
            Stream content, string extension, CancellationToken cancellationToken = default)
        {
            var key = $"{Guid.NewGuid():N}{extension}";
            Stored.Add(key);
            using var memory = new MemoryStream();
            content.CopyTo(memory);
            Content[key] = memory.ToArray();
            content.Position = 0;
            return Task.FromResult(key);
        }
        public Task<Stream?> OpenReadAsync(
            string storageKey, CancellationToken cancellationToken = default) =>
            Task.FromResult<Stream?>(Content.TryGetValue(storageKey, out var bytes)
                ? new MemoryStream(bytes, writable: false) : null);
        public Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default)
        {
            Deleted.Add(storageKey);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeProfilePhotoStorage : IProfilePhotoStorage
    {
        private readonly Dictionary<Guid, StoredProfilePhoto> _photos = [];
        public Task<StoredProfilePhoto?> GetAsync(Guid userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_photos.GetValueOrDefault(userId));
        public Task<Guid> StoreAsync(Guid userId, byte[] content, string contentType, CancellationToken cancellationToken = default)
        {
            var version = Guid.NewGuid();
            _photos[userId] = new(content, contentType, content.Length, version);
            return Task.FromResult(version);
        }
        public Task<bool> DeleteAsync(Guid userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_photos.Remove(userId));
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public int SaveCount { get; private set; }
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveCount++;
            return Task.FromResult(1);
        }
    }

    private static UpdateCandidateOnboardingRequest ValidOnboarding() =>
        new(
            CareerStage.Student,
            [DesiredOpportunity.Internship],
            "Pune",
            ["C#"],
            [WorkPreference.Remote],
            "Example Institute",
            "B.Tech",
            Now.Year,
            null);

    private sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow);
    }
}
