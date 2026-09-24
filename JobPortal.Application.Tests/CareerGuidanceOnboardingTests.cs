using JobPortal.Application.Abstractions.Auditing;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Application.Features.Auditing;
using JobPortal.Application.Features.CareerGuidance;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Persistence.Context;
using JobPortal.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class CareerGuidanceOnboardingTests
{
    [Fact]
    public async Task StartOnboardingCreatesDraftAndResumeIsIdempotent()
    {
        using var f = new Fixture();

        var first = await f.Service.StartOnboardingAsync(f.Owner.Id, default);
        var second = await f.Service.StartOnboardingAsync(f.Owner.Id, default);

        Assert.Equal(ConsultantVerificationStatus.Draft, first.Status);
        Assert.Equal(first.Id, second.Id);
        Assert.Single(await f.Db.CareerConsultants.ToArrayAsync());
    }

    [Fact]
    public async Task IncompleteDraftCannotBeSubmitted()
    {
        using var f = new Fixture();

        var draft = await f.Service.StartOnboardingAsync(f.Owner.Id, default);

        var exception = await Assert.ThrowsAsync<BadRequestException>(() =>
            f.Service.SubmitOnboardingAsync(
                f.Owner.Id,
                new OnboardingSubmitRequest(
                    draft.Revision,
                    CareerGuidanceService.PolicyVersion,
                    true,
                    true),
                default));

        Assert.Contains("Complete these requirements", exception.Message);

        var stored = await f.Db.CareerConsultants.SingleAsync();

        Assert.Equal(
            ConsultantVerificationStatus.Draft,
            stored.VerificationStatus);

        Assert.Null(stored.SubmittedAtUtc);
        Assert.Null(stored.PublicProfileConsentAtUtc);
    }

    [Fact]
    public async Task SubmissionRequiresCurrentPolicyAndExplicitPublicConsent()
    {
        using var f = new Fixture();

        var draft = await f.Service.StartOnboardingAsync(f.Owner.Id, default);

        await Assert.ThrowsAsync<BadRequestException>(() =>
            f.Service.SubmitOnboardingAsync(
                f.Owner.Id,
                new OnboardingSubmitRequest(
                    draft.Revision,
                    "wrong-policy",
                    true,
                    true),
                default));

        draft = await f.Service.OnboardingAsync(f.Owner.Id, default);

        await Assert.ThrowsAsync<BadRequestException>(() =>
            f.Service.SubmitOnboardingAsync(
                f.Owner.Id,
                new OnboardingSubmitRequest(
                    draft.Revision,
                    CareerGuidanceService.PolicyVersion,
                    true,
                    false),
                default));
    }

    [Fact]
    public async Task NewServicesAllowOnlyLaunchDurationsAndInr()
    {
        using var f = new Fixture();

        var draft = await f.Service.StartOnboardingAsync(f.Owner.Id, default);

        await Assert.ThrowsAsync<BadRequestException>(() =>
            f.Service.SaveServiceAsync(
                f.Owner.Id,
                null,
                new ConsultantServiceRequest(
                    "Custom session",
                    "Custom session",
                    "Custom session",
                    45,
                    1000,
                    "INR",
                    true,
                    draft.Revision),
                default));

        draft = await f.Service.OnboardingAsync(f.Owner.Id, default);

        await Assert.ThrowsAsync<BadRequestException>(() =>
            f.Service.SaveServiceAsync(
                f.Owner.Id,
                null,
                new ConsultantServiceRequest(
                    "Mock interview",
                    "Mock interview",
                    "Mock interview",
                    30,
                    100,
                    "USD",
                    true,
                    draft.Revision),
                default));

        draft = await f.Service.OnboardingAsync(f.Owner.Id, default);

        var saved = await f.Service.SaveServiceAsync(
            f.Owner.Id,
            null,
            new ConsultantServiceRequest(
                "Mock interview",
                "Mock interview",
                "Practice interview",
                30,
                1000,
                "INR",
                true,
                draft.Revision),
            default);

        var service = Assert.Single(saved.Services);

        Assert.Equal(30, service.DurationMinutes);
        Assert.Equal("INR", service.Currency);
    }

    [Fact]
    public async Task DraftCanConfigureServiceBeforeApproval()
    {
        using var f = new Fixture();

        var draft = await f.Service.StartOnboardingAsync(f.Owner.Id, default);

        var result = await f.Service.SaveServiceAsync(
            f.Owner.Id,
            null,
            new ConsultantServiceRequest(
                "Career consultation",
                "Career consultation",
                "One to one career guidance",
                15,
                500,
                "INR",
                true,
                draft.Revision),
            default);

        Assert.Single(result.Services);

        var stored = await f.Db.CareerConsultants.SingleAsync();

        Assert.Equal(
            ConsultantVerificationStatus.Draft,
            stored.VerificationStatus);
    }

    [Fact]
    public async Task MaterialEditOfRejectedProfileReturnsItToDraft()
    {
        using var f = new Fixture();

        await f.Service.StartOnboardingAsync(f.Owner.Id, default);

        var profile = await f.Db.CareerConsultants.SingleAsync();

        profile.VerificationStatus =
            ConsultantVerificationStatus.Rejected;

        profile.VerificationReason =
            "Please improve your profile.";

        await f.Db.SaveChangesAsync();

        var current =
            await f.Service.OnboardingAsync(f.Owner.Id, default);

        var updated = await f.Service.SaveBasicAsync(
            f.Owner.Id,
            new OnboardingBasicRequest(
                current.Revision,
                DisplayName:
                    new OnboardingField<string?>("Updated Name")),
            default);

        Assert.Equal(
            ConsultantVerificationStatus.Draft,
            updated.Status);

        Assert.Equal(
            "Please improve your profile.",
            updated.RejectionFeedback);
    }

    [Fact]
    public async Task NonMaterialProfessionalEmailEditDoesNotResetStatus()
    {
        using var f = new Fixture();

        await f.Service.StartOnboardingAsync(f.Owner.Id, default);

        var profile = await f.Db.CareerConsultants.SingleAsync();

        profile.VerificationStatus =
            ConsultantVerificationStatus.Pending;

        await f.Db.SaveChangesAsync();

        var current =
            await f.Service.OnboardingAsync(f.Owner.Id, default);

        var updated = await f.Service.SaveBasicAsync(
            f.Owner.Id,
            new OnboardingBasicRequest(
                current.Revision,
                ProfessionalEmail:
                    new OnboardingField<string?>(
                        "professional@example.com")),
            default);

        Assert.Equal(
            ConsultantVerificationStatus.Pending,
            updated.Status);

        Assert.Equal(
            "professional@example.com",
            updated.ProfessionalEmail);
    }

    [Fact]
    public async Task OmittedBasicFieldIsPreservedAndExplicitNullClearsIt()
    {
        using var f = new Fixture();

        var draft =
            await f.Service.StartOnboardingAsync(f.Owner.Id, default);

        var saved = await f.Service.SaveBasicAsync(
            f.Owner.Id,
            new OnboardingBasicRequest(
                draft.Revision,
                Location:
                    new OnboardingField<string?>("Pune")),
            default);

        Assert.Equal("Pune", saved.Location);

        saved = await f.Service.SaveBasicAsync(
            f.Owner.Id,
            new OnboardingBasicRequest(
                saved.Revision,
                DisplayName:
                    new OnboardingField<string?>("Consultant")),
            default);

        Assert.Equal("Pune", saved.Location);

        saved = await f.Service.SaveBasicAsync(
            f.Owner.Id,
            new OnboardingBasicRequest(
                saved.Revision,
                Location:
                    new OnboardingField<string?>(null)),
            default);

        Assert.Null(saved.Location);
    }

    [Fact]
    public async Task CompleteOnboardingCanBeSubmittedAndBecomesPending()
    {
        using var f = new Fixture();

        // 1. Start draft
        var draft = await f.Service.StartOnboardingAsync(
            f.Owner.Id,
            default);

        Assert.Equal(
            ConsultantVerificationStatus.Draft,
            draft.Status);

        // 2. Basic information
        draft = await f.Service.SaveBasicAsync(
            f.Owner.Id,
            new OnboardingBasicRequest(
                draft.Revision,
                DisplayName:
                    new OnboardingField<string?>("Manoj Consultant"),
                ProfessionalHeadline:
                    new OnboardingField<string?>(
                        "Software Engineer & Career Mentor"),
                Bio:
                    new OnboardingField<string?>(
                        "I help candidates prepare for technical interviews."),
                Location:
                    new OnboardingField<string?>("Pune"),
                ProfessionalEmail:
                    new OnboardingField<string?>(
                        "mentor@example.com")),
            default);

        // 3. Professional information
        draft = await f.Service.SaveProfessionalAsync(
            f.Owner.Id,
            new OnboardingProfessionalRequest(
                draft.Revision,
                CompanyId:
                    new OnboardingField<Guid?>(f.Company.Id),
                CurrentRole:
                    new OnboardingField<string?>(
                        "Software Engineer"),
                YearsOfExperience:
                    new OnboardingField<decimal?>(5m),
                ProfessionalType:
                    new OnboardingField<CareerProfessionalType?>(
                        CareerProfessionalType.CurrentEmployee),
                LinkedInUrl:
                    new OnboardingField<string?>(
                        "https://www.linkedin.com/in/example"),
                Industry:
                    new OnboardingField<string?>("Technology"),
                FunctionalArea:
                    new OnboardingField<string?>(
                        "Software Engineering")),
            default);

        // 4. Languages and expertise
        draft = await f.Service.SaveExpertiseAsync(
            f.Owner.Id,
            new OnboardingExpertiseRequest(
                draft.Revision,
                Languages:
                    new OnboardingField<string[]?>(
                        ["English", "Hindi"]),
                Expertise:
                    new OnboardingField<string[]?>(
                        [
                            "Interview Preparation",
                        "Career Guidance"
                        ])),
            default);

        // 5. Add launch service
        var privateProfile = await f.Service.SaveServiceAsync(
            f.Owner.Id,
            null,
            new ConsultantServiceRequest(
                "30 Minute Career Guidance",
                "Career Guidance",
                "One to one career guidance session",
                30,
                1000,
                "INR",
                true,
                draft.Revision),
            default);

        // Service mutation changes the revision, so reload onboarding.
        draft = await f.Service.OnboardingAsync(
            f.Owner.Id,
            default);

        Assert.Single(privateProfile.Services);

        // 6. Configure availability before approval.
        //
        // Draft consultant is allowed to prepare availability,
        // but IsAcceptingBookings must remain false.
        var scheduling = new CareerSchedulingService(
    new CareerSchedulingRepository(f.Db),
    new UserRepository(f.Db),
    new AuditWriter(
        new AuditLogRepository(f.Db),
        new ActorContext(f.Admin.Id)),
    TimeProvider.System,
    Microsoft.Extensions.Options.Options.Create(
        new CareerGuidanceSchedulingOptions()),
    new SaveAvailabilityRequestValidator(),
    new AvailabilityExceptionRequestValidator(),
    new CreateCareerBookingRequestValidator());

        var availability = await scheduling.SaveAvailabilityAsync(
            f.Owner.Id,
            new SaveAvailabilityRequest(
                "Asia/Kolkata",
                true,
                [
                    new WeeklyWindow(
                    DayOfWeek.Monday,
                    new TimeOnly(9, 0),
                    new TimeOnly(12, 0))
                ],
                draft.Revision),
            default);

        Assert.False(availability.IsAcceptingBookings);
        Assert.Single(availability.Windows);

        // Availability mutation changes revision.
        draft = await f.Service.OnboardingAsync(
            f.Owner.Id,
            default);

        // All business requirements should now be complete except
        // policy/public consent, which are accepted during submit.
        Assert.DoesNotContain(
            "DisplayName",
            draft.MissingRequirements);

        Assert.DoesNotContain(
            "LaunchService",
            draft.MissingRequirements);

        Assert.DoesNotContain(
            "UsableAvailability",
            draft.MissingRequirements);

        // 7. Submit
        var submitted =
            await f.Service.SubmitOnboardingAsync(
                f.Owner.Id,
                new OnboardingSubmitRequest(
                    draft.Revision,
                    CareerGuidanceService.PolicyVersion,
                    true,
                    true),
                default);

        // 8. Verify lifecycle
        Assert.Equal(
            ConsultantVerificationStatus.Pending,
            submitted.Status);

        Assert.NotNull(submitted.SubmittedAtUtc);
        Assert.NotNull(submitted.TermsAcceptedAtUtc);
        Assert.NotNull(submitted.PublicProfileConsentAtUtc);

        Assert.Equal(
            CareerGuidanceService.PolicyVersion,
            submitted.PolicyVersion);

        Assert.False(
            submitted.Availability.IsAcceptingBookings);

        Assert.Empty(submitted.MissingRequirements);

        // 9. Verify persisted state
        var stored =
            await f.Db.CareerConsultants.SingleAsync();

        Assert.Equal(
            ConsultantVerificationStatus.Pending,
            stored.VerificationStatus);

        Assert.False(stored.IsAcceptingBookings);
        Assert.NotNull(stored.SubmittedAtUtc);
        Assert.NotNull(stored.TermsAcceptedAtUtc);
        Assert.NotNull(stored.PublicProfileConsentAtUtc);
    }

    [Fact]
    public async Task ExplicitImportCopiesSelectedCandidateDataWithoutLiveSync()
    {
        using var f = new Fixture();

        f.Owner.ProfileImageUrl = "https://example.com/profile.jpg";
        f.Owner.Location = "Pune";

        var education = new CandidateEducation
        {
            UserId = f.Owner.Id,
            User = f.Owner,
            Qualification = "B.E.",
            Institution = "Example University",
            FieldOfStudy = "Mechanical Engineering",
            StartYear = 2016,
            EndYear = 2020,
            DisplayOrder = 0
        };

        var experience = new CandidateExperience
        {
            UserId = f.Owner.Id,
            User = f.Owner,
            JobTitle = "Software Engineer",
            CompanyName = "Example Technologies",
            StartDate = new DateOnly(2024, 2, 1),
            EndDate = new DateOnly(2025, 7, 31),
            IsCurrent = false,
            Description = "Backend development",
            DisplayOrder = 0
        };

        f.Db.AddRange(education, experience);
        await f.Db.SaveChangesAsync();

        var draft = await f.Service.StartOnboardingAsync(
            f.Owner.Id,
            default);

        var options = await f.Service.ImportOptionsAsync(
            f.Owner.Id,
            default);

        Assert.Equal(
            "https://example.com/profile.jpg",
            options.ProfileImageUrl);

        Assert.Equal("Pune", options.Location);

        var educationOption =
            Assert.Single(options.Education);

        var experienceOption =
            Assert.Single(options.WorkExperience);

        var imported = await f.Service.ImportAsync(
            f.Owner.Id,
            new OnboardingImportRequest(
                draft.Revision,
                ImportPhoto: true,
                ImportLocation: true,
                EducationIds: [educationOption.Id],
                ExperienceIds: [experienceOption.Id]),
            default);

        Assert.Equal(
            "https://example.com/profile.jpg",
            imported.ProfileImageUrl);

        Assert.Equal("Pune", imported.Location);

        var importedEducation =
            Assert.Single(imported.Education);

        Assert.Equal(
            "B.E.",
            importedEducation.Qualification);

        Assert.Equal(
            "Example University",
            importedEducation.Institution);

        var importedExperience =
            Assert.Single(imported.WorkExperience);

        Assert.Equal(
            "Software Engineer",
            importedExperience.JobTitle);

        Assert.Equal(
            "Example Technologies",
            importedExperience.CompanyName);

        // Change the original candidate portfolio after import.
        f.Owner.ProfileImageUrl =
            "https://example.com/changed.jpg";

        f.Owner.Location = "Mumbai";

        education.Qualification = "Changed qualification";
        education.Institution = "Changed university";

        experience.JobTitle = "Changed role";
        experience.CompanyName = "Changed company";

        await f.Db.SaveChangesAsync();

        // Consultant onboarding must retain its own copied values.
        var consultant = await f.Service.OnboardingAsync(
            f.Owner.Id,
            default);

        Assert.Equal(
            "https://example.com/profile.jpg",
            consultant.ProfileImageUrl);

        Assert.Equal("Pune", consultant.Location);

        var consultantEducation =
            Assert.Single(consultant.Education);

        Assert.Equal(
            "B.E.",
            consultantEducation.Qualification);

        Assert.Equal(
            "Example University",
            consultantEducation.Institution);

        var consultantExperience =
            Assert.Single(consultant.WorkExperience);

        Assert.Equal(
            "Software Engineer",
            consultantExperience.JobTitle);

        Assert.Equal(
            "Example Technologies",
            consultantExperience.CompanyName);
    }

    [Fact]
    public async Task MaterialEditIsBlockedWhenVerifiedConsultantHasFutureConfirmedPaidBooking()
    {
        using var f = new Fixture();

        // 1. Create consultant draft.
        await f.Service.StartOnboardingAsync(
            f.Owner.Id,
            default);

        var profile =
            await f.Db.CareerConsultants.SingleAsync();

        // 2. Simulate an already approved consultant.
        profile.VerificationStatus =
            ConsultantVerificationStatus.Verified;

        profile.PublicProfileConsentAtUtc =
            DateTime.UtcNow;

        await f.Db.SaveChangesAsync();

        // 3. Create an active launch-compatible service.
        await f.Service.SaveServiceAsync(
            f.Owner.Id,
            null,
            new ConsultantServiceRequest(
                "INTERVIEW",
                "Mock Interview",
                "Interview preparation",
                30,
                1000m,
                "INR",
                true,
                profile.Revision),
            default);

        // SaveServiceAsync returns ConsultantPrivateResponse,
        // so load the actual service entity from the database.
        var offering =
            await f.Db.CareerConsultantServices.SingleAsync();

        // 4. Create a separate candidate for the booking.
        var candidate = new User
        {
            FirstName = "Candidate",
            Email = "candidate@example.com",
            Status = UserStatus.Active,
            Role = f.Owner.Role,
            RoleId = f.Owner.RoleId
        };

        f.Db.Add(candidate);
        await f.Db.SaveChangesAsync();

        var now = DateTime.UtcNow;

        // 5. Create a future confirmed booking.
        var booking = new CareerGuidanceBooking
        {
            RequiresPayment = true,

            CandidateUserId = candidate.Id,
            Candidate = candidate,

            ConsultantId = profile.Id,
            Consultant = profile,

            ConsultantServiceId = offering.Id,
            Service = offering,

            StartUtc = now.AddDays(2),
            EndUtc = now.AddDays(2).AddMinutes(30),

            ConsultantTimeZoneSnapshot = "Asia/Kolkata",
            ServiceTitleSnapshot = "Mock Interview",
            ServiceTypeSnapshot = "INTERVIEW",
            DurationMinutesSnapshot = 30,
            PriceSnapshot = 1000m,
            CurrencySnapshot = "INR",

            Status = CareerBookingStatus.Confirmed,
            SessionGoal = "Interview preparation"
        };

        f.Db.Add(booking);
        await f.Db.SaveChangesAsync();

        // 6. Mark the booking as paid.
        var payment = new CareerGuidancePayment
        {
            BookingId = booking.Id,
            Booking = booking,

            CandidateUserId = candidate.Id,

            ConsultantId = profile.Id,
            Consultant = profile,

            AmountGross = 1000m,
            Currency = "INR",

            PlatformCommissionPercentSnapshot = 10m,
            PlatformCommissionAmount = 100m,
            ConsultantNetAmount = 900m,

            Status = CareerPaymentStatus.Captured,
            PaidAtUtc = now
        };

        f.Db.Add(payment);
        await f.Db.SaveChangesAsync();

        // 7. Get current onboarding revision.
        var onboarding =
            await f.Service.OnboardingAsync(
                f.Owner.Id,
                default);

        // 8. Attempt a material public-profile change.
        var exception =
    await Assert.ThrowsAsync<ConflictException>(
        () => f.Service.SaveBasicAsync(
            f.Owner.Id,
            new OnboardingBasicRequest(
                onboarding.Revision,
                ProfessionalHeadline:
                    new OnboardingField<string?>(
                        "Changed professional headline")),
            default));

        // 9. Material edit must be blocked.
        Assert.Equal(
            409,
            exception.StatusCode);

        // 10. Consultant must remain Verified.
        var unchanged =
            await f.Service.OnboardingAsync(
                f.Owner.Id,
                default);

        Assert.Equal(
            ConsultantVerificationStatus.Verified,
            unchanged.Status);

        // 11. Attempted change must not be persisted.
        Assert.NotEqual(
            "Changed professional headline",
            unchanged.ProfessionalHeadline);
    }


    [Fact]
    public async Task PublicProfileExposesConsentedFieldsButNotPrivateMetadata()
    {
        using var f = new Fixture();

        await f.Service.StartOnboardingAsync(
            f.Owner.Id,
            default);

        var profile =
            await f.Db.CareerConsultants.SingleAsync();

        profile.DisplayName = "Career Mentor";
        profile.ProfessionalHeadline = "Senior Software Engineer";
        profile.Bio = "Career guidance profile";

        profile.ProfileImageUrl =
            "https://example.com/profile.jpg";

        profile.Location = "Pune";
        profile.Industry = "Technology";
        profile.FunctionalArea = "Engineering";

        profile.ProfessionalEmail =
            "private-professional@example.com";

        profile.LinkedInUrl =
            "https://www.linkedin.com/in/private-profile";

        profile.VerificationStatus =
            ConsultantVerificationStatus.Verified;

        profile.PublicProfileConsentAtUtc =
            DateTime.UtcNow;

        profile.VerificationMethod =
            "ManualLinkedInReview";

        profile.VerificationReason =
            "Admin-only review reason";

        profile.ReviewedByUserId =
            f.Admin.Id;

        profile.ReviewedAtUtc =
            DateTime.UtcNow;

        await f.Db.SaveChangesAsync();

        var result =
            await f.Service.GetAsync(
                profile.Id,
                default);

        // Public, consented fields.
        Assert.Equal(
            "https://example.com/profile.jpg",
            result.ProfileImageUrl);

        Assert.Equal(
            "Pune",
            result.Location);

        Assert.Equal(
            "Technology",
            result.Industry);

        Assert.Equal(
            "Engineering",
            result.FunctionalArea);

        Assert.Equal(
            "Approved by CareerHarbor",
            result.ApprovalLabel);

        // Public DTO must never expose private/admin metadata.
        var publicProperties =
            typeof(ConsultantPublicResponse)
                .GetProperties()
                .Select(x => x.Name)
                .ToArray();

        Assert.DoesNotContain(
            "ProfessionalEmail",
            publicProperties);

        Assert.DoesNotContain(
            "LinkedInUrl",
            publicProperties);

        Assert.DoesNotContain(
            "VerificationMethod",
            publicProperties);

        Assert.DoesNotContain(
            "VerificationReason",
            publicProperties);

        Assert.DoesNotContain(
            "ReviewedByUserId",
            publicProperties);

        Assert.DoesNotContain(
            "ReviewedAtUtc",
            publicProperties);

        Assert.DoesNotContain(
            "UserId",
            publicProperties);
    }

    [Fact]
    public async Task DraftConsultantIsExcludedFromAdminApplicationAnalytics()
    {
        using var f = new Fixture();

        // 1. Start onboarding but do not submit it.
        var draft = await f.Service.StartOnboardingAsync(
            f.Owner.Id,
            default);

        Assert.Equal(
            ConsultantVerificationStatus.Draft,
            draft.Status);

        // 2. Confirm the draft exists in the database.
        var stored =
            await f.Db.CareerConsultants.SingleAsync();

        Assert.Equal(
            ConsultantVerificationStatus.Draft,
            stored.VerificationStatus);

        // 3. Query admin analytics across a wide window.
        var analyticsRepository =
            new CareerAnalyticsRepository(
                f.Db,
                TimeProvider.System);

        var from =
            DateTime.UtcNow.AddDays(-30);

        var to =
            DateTime.UtcNow.AddDays(30);

        var analytics =
            await analyticsRepository.AdminAsync(
                from,
                to,
                default);

        // 4. An unsubmitted Draft is not an application.
        Assert.Equal(
            0,
            analytics.Applications);

        Assert.Equal(
            0,
            analytics.PendingVerification);

        Assert.Equal(
            0,
            analytics.VerifiedConsultants);

        Assert.Equal(
            0,
            analytics.RejectedConsultants);

        Assert.Equal(
            0,
            analytics.SuspendedConsultants);
    }
    private sealed class Fixture : IDisposable
    {
        public DbContextOptions<JobPortalDbContext> Options { get; }

        public JobPortalDbContext Db { get; }

        public User Owner { get; } = new()
        {
            FirstName = "Owner",
            Email = "owner@example.com",
            Status = UserStatus.Active
        };

        public User Admin { get; } = new()
        {
            FirstName = "Admin",
            Email = "admin@example.com",
            Status = UserStatus.Active
        };

        public Company Company { get; } = new()
        {
            Name = "Example",
            Slug = "example"
        };

        public CareerGuidanceService Service { get; }

        public Fixture()
        {
            Options =
                new DbContextOptionsBuilder<JobPortalDbContext>()
                    .UseInMemoryDatabase(
                        Guid.NewGuid().ToString())
                    .Options;

            Db = new JobPortalDbContext(Options);

            var candidateRole = new Role
            {
                Name = "Candidate",
                NormalizedName = "CANDIDATE"
            };

            var adminRole = new Role
            {
                Name = "Administrator",
                NormalizedName = "ADMINISTRATOR"
            };

            Owner.Role = candidateRole;
            Owner.RoleId = candidateRole.Id;

            Admin.Role = adminRole;
            Admin.RoleId = adminRole.Id;

            Company.OwnerUserId = Admin.Id;

            Db.AddRange(
                candidateRole,
                adminRole,
                Owner,
                Admin,
                Company);

            Db.SaveChanges();

            Service = new CareerGuidanceService(
                new CareerGuidanceRepository(Db),
                new UserRepository(Db),
                new CompanyManagementRepository(Db),
                new UnitOfWork(Db),
                new AuditWriter(
                    new AuditLogRepository(Db),
                    new ActorContext(Admin.Id)),
                TimeProvider.System,
                new ConsultantProfileRequestValidator(),
                new ConsultantServiceRequestValidator(),
                new ConsultantSearchQueryValidator(),
                new ConsultantReviewRequestValidator(),
                new ConsultantAdminQueryValidator());
        }

        public void Dispose()
        {
            Db.Dispose();
        }
    }

    private sealed class ActorContext(Guid actor)
        : IAuditContextAccessor
    {
        public Guid? ActorUserId => actor;

        public string? ActorRole => "Administrator";

        public string? CorrelationId =>
            "career-guidance-onboarding-test";
    }
}
