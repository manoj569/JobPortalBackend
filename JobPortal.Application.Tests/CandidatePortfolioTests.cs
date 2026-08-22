using System.Text.Json;
using FluentValidation;
using JobPortal.Application.Abstractions.Persistence;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Application.Features.Portfolios;
using JobPortal.Domain.Common;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class CandidatePortfolioTests
{
    private static readonly DateTime Now = new(2026, 8, 12, 0, 0, 0, DateTimeKind.Utc);
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task CreateProducesOnePrivateDraftAndIsIdempotent()
    {
        var fixture = CreateFixture();
        var first = await fixture.Service.CreateAsync(fixture.User.Id,
            new(null, CandidatePortfolioTemplate.Professional));
        var second = await fixture.Service.CreateAsync(fixture.User.Id,
            new("ignored-slug", CandidatePortfolioTemplate.Developer));

        Assert.True(first.IsCreated);
        Assert.Equal(CandidatePortfolioStatus.Draft, first.Status);
        Assert.Equal("manoj-shekapure", first.Slug);
        Assert.Equal(first.Id, second.Id);
        await Assert.ThrowsAsync<NotFoundException>(() =>
            fixture.Service.GetPublicAsync(first.Slug!));
    }

    [Fact]
    public async Task GeneratedSlugUsesCollisionSafeSuffix()
    {
        var fixture = CreateFixture();
        fixture.Repository.UsedSlugs.Add("manoj-shekapure");

        var result = await fixture.Service.CreateAsync(fixture.User.Id,
            new(null, CandidatePortfolioTemplate.Professional));

        Assert.Equal("manoj-shekapure-2", result.Slug);
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("bad--slug")]
    [InlineData("-leading")]
    [InlineData("ab")]
    [InlineData("spaces are unsafe")]
    public async Task ReservedOrMalformedSlugIsRejected(string slug)
    {
        var fixture = CreateFixture();
        await Assert.ThrowsAsync<BadRequestException>(() => fixture.Service.CreateAsync(
            fixture.User.Id, new(slug, CandidatePortfolioTemplate.Professional)));
    }

    [Fact]
    public async Task RequestedSlugIsTrimmedNormalizedAndMustBeUnique()
    {
        var fixture = CreateFixture();
        var created = await fixture.Service.CreateAsync(fixture.User.Id,
            new("  Manoj-Shekapure  ", CandidatePortfolioTemplate.Developer));
        Assert.Equal("manoj-shekapure", created.Slug);

        var other = CreateFixture();
        other.Repository.UsedSlugs.Add("taken-slug");
        await Assert.ThrowsAsync<ConflictException>(() => other.Service.CreateAsync(
            other.User.Id, new("taken-slug", CandidatePortfolioTemplate.Professional)));
    }

    [Fact]
    public async Task PublishReportsMissingRequirementsWithoutPublishing()
    {
        var fixture = CreateFixture(completeProfile: false);
        await fixture.Service.CreateAsync(fixture.User.Id, new(null, CandidatePortfolioTemplate.Professional));

        var result = await fixture.Service.PublishAsync(fixture.User.Id);

        Assert.False(result.Published);
        Assert.Equal(["resumeHeadline", "profileSummary", "skills"], result.MissingRequirements);
        Assert.Equal(CandidatePortfolioStatus.Draft, result.Portfolio.Status);
    }

    [Fact]
    public async Task PublishMakesAllowlistedPublicDtoReadableAndUnpublishRemovesIt()
    {
        var fixture = CreateFixture();
        fixture.User.CurrentCity = "Private City";
        fixture.User.CurrentArea = "Private Area";
        await fixture.Service.CreateAsync(fixture.User.Id, new(null, CandidatePortfolioTemplate.Professional));
        var published = await fixture.Service.PublishAsync(fixture.User.Id);
        var publicResult = await fixture.Service.GetPublicAsync(published.Portfolio.Slug!);
        var json = JsonSerializer.Serialize(publicResult, WebJson);

        Assert.True(published.Published);
        Assert.Equal(Now, publicResult.PublishedAtUtc);
        Assert.DoesNotContain("email", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("phone", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("userId", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("storage", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("career", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("candidatePortfolio", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Private City", json, StringComparison.Ordinal);
        Assert.DoesNotContain("Private Area", json, StringComparison.Ordinal);

        await fixture.Service.UnpublishAsync(fixture.User.Id);
        await fixture.Service.UnpublishAsync(fixture.User.Id);
        await Assert.ThrowsAsync<NotFoundException>(() => fixture.Service.GetPublicAsync(publicResult.Slug));
    }

    [Fact]
    public async Task HiddenSectionsAreOmittedAndOrderingIsDeterministic()
    {
        var fixture = CreateFixture();
        var portfolio = await fixture.Service.CreateAsync(fixture.User.Id, new(null, CandidatePortfolioTemplate.Professional));
        var settings = portfolio.SectionSettings.Select(x => new PortfolioSectionSettingRequest(
            x.SectionType, x.SectionType is not (PortfolioSectionType.Skills or PortfolioSectionType.Resume),
            100 - x.DisplayOrder)).ToArray();
        await fixture.Service.UpdateSettingsAsync(fixture.User.Id,
            new(portfolio.Slug!, CandidatePortfolioTemplate.Professional, settings));
        fixture.Experiences.Add(new CandidateExperience { UserId = fixture.User.Id, JobTitle = "Second", CompanyName = "B", StartDate = new(2020, 1, 1), DisplayOrder = 2, AnnualSalary = 999999, NoticePeriod = CandidateAvailability.OneMonth });
        fixture.Experiences.Add(new CandidateExperience { UserId = fixture.User.Id, JobTitle = "First", CompanyName = "A", StartDate = new(2019, 1, 1), DisplayOrder = 1 });
        await fixture.Service.PublishAsync(fixture.User.Id);

        var result = await fixture.Service.GetPublicAsync(portfolio.Slug!);
        var json = JsonSerializer.Serialize(result, WebJson);
        Assert.Null(result.Sections.Skills);
        Assert.Null(result.Sections.ResumeAvailable);
        Assert.Equal(settings.Where(x => x.IsVisible).OrderBy(x => x.DisplayOrder)
            .Select(x => x.SectionType), result.SectionOrder);
        Assert.Equal(["First", "Second"], result.Sections.Experience!.Select(x => x.JobTitle));
        Assert.DoesNotContain("\"skills\"", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("resumeAvailable", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("annualSalary", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("noticePeriod", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CandidateCannotMutateAnotherCandidatesContent()
    {
        var fixture = CreateFixture();
        fixture.Experiences.Add(new CandidateExperience
        { UserId = Guid.NewGuid(), JobTitle = "Hidden", CompanyName = "Private", StartDate = new(2020, 1, 1) });

        await Assert.ThrowsAsync<NotFoundException>(() => fixture.Service.UpdateExperienceAsync(
            fixture.User.Id, fixture.Experiences[0].Id,
            new("Changed", "Changed", null, null, new(2020, 1, 1), null, true, null, 0)));
        await Assert.ThrowsAsync<UnauthorizedException>(() => fixture.Service.GetAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task StructuredCrudTrimsDataAndSoftDeletedRecordsAreNotPublic()
    {
        var fixture = CreateFixture();
        var experience = await fixture.Service.AddExperienceAsync(fixture.User.Id,
            new("  Engineer ", " Career Harbor ", null, EmploymentType.FullTime,
                new(2024, 1, 1), null, true, " Builds APIs ", 2));
        Assert.Equal("Engineer", experience.JobTitle);
        await fixture.Service.DeleteExperienceAsync(fixture.User.Id, experience.Id);
        Assert.True(fixture.Experiences.Single().IsDeleted);
    }

    [Fact]
    public async Task EducationCourseTypeCanonicalStringCreatesUpdatesAndRoundTrips()
    {
        var fixture = CreateFixture();
        var request = JsonSerializer.Deserialize<EducationRequest>(
            """{"qualification":"B.Tech","institution":"University","fieldOfStudy":"Computer Science","startYear":2020,"endYear":2024,"grade":"8.5","description":null,"displayOrder":1,"courseType":"FullTime","isCurrentlyStudying":false,"gradingSystem":"CGPA"}""",
            WebJson)!;
        var created = await fixture.Service.AddEducationAsync(fixture.User.Id, request);
        var updated = await fixture.Service.UpdateEducationAsync(fixture.User.Id, created.Id,
            request with { CourseType = EducationCourseType.CorrespondenceOrDistance });
        Assert.Equal(EducationCourseType.FullTime, created.CourseType);
        Assert.Equal(EducationCourseType.CorrespondenceOrDistance, updated.CourseType);
        Assert.Contains("\"CorrespondenceOrDistance\"",
            JsonSerializer.Serialize(updated, WebJson), StringComparison.Ordinal);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<EducationRequest>(
            """{"qualification":"B.Tech","institution":"University","displayOrder":1,"courseType":"CorrespondenceDistanceLearning"}""",
            WebJson));
    }

    [Fact]
    public async Task ValidatorsEnforceDatesYearsHttpsExpiryAndPlainText()
    {
        var fixture = CreateFixture();
        await Assert.ThrowsAsync<ValidationException>(() => fixture.Service.AddExperienceAsync(fixture.User.Id,
            new("Role", "Company", null, null, new(2025, 1, 1), new(2024, 1, 1), false, null, 0)));
        await Assert.ThrowsAsync<ValidationException>(() => fixture.Service.AddExperienceAsync(fixture.User.Id,
            new("Role", "Company", null, EmploymentType.FullTime, new(2025, 1, 1), null, false, null, 0)));
        await Assert.ThrowsAsync<ValidationException>(() => fixture.Service.AddEducationAsync(fixture.User.Id,
            new("Degree", "School", null, 2025, 2020, null, null, 0)));
        await Assert.ThrowsAsync<ValidationException>(() => fixture.Service.AddProjectAsync(fixture.User.Id,
            new("Project", null, "Description", [], "http://unsafe.test", null, null, null, 0)));
        await Assert.ThrowsAsync<ValidationException>(() => fixture.Service.AddLinkAsync(fixture.User.Id,
            new(ProfessionalLinkType.Website, null, "javascript:alert(1)", 0)));
        await Assert.ThrowsAsync<ValidationException>(() => fixture.Service.AddCertificationAsync(fixture.User.Id,
            new("Cert", null, new(2025, 1, 1), new(2024, 1, 1), false, null, null, 0)));
        await Assert.ThrowsAsync<ValidationException>(() => fixture.Service.AddCustomSectionAsync(fixture.User.Id,
            new("<script>alert(1)</script>", 0)));
    }

    [Fact]
    public async Task CustomSectionAndItemLimitsAreEnforced()
    {
        var fixture = CreateFixture();
        for (var index = 0; index < 5; index++) fixture.CustomSections.Add(new PortfolioCustomSection
        { UserId = fixture.User.Id, Title = $"Section {index}" });
        await Assert.ThrowsAsync<ConflictException>(() => fixture.Service.AddCustomSectionAsync(
            fixture.User.Id, new("One too many", 0)));
        var section = fixture.CustomSections[0];
        for (var index = 0; index < 10; index++) section.Items.Add(new PortfolioCustomItem { Title = $"Item {index}" });
        await Assert.ThrowsAsync<ConflictException>(() => fixture.Service.AddCustomItemAsync(
            fixture.User.Id, section.Id, new("One too many", null, null, null, 0)));
    }

    private static Fixture CreateFixture(bool completeProfile = true)
    {
        var user = new User
        {
            Id = Guid.NewGuid(), RoleId = SystemRoleIds.Candidate, Status = UserStatus.Active,
            FirstName = "Manoj", LastName = "Shekapure", Email = "private@example.test",
            PhoneNumber = "+919999999999", Headline = completeProfile ? ".NET Developer" : null,
            Bio = completeProfile ? "Builds secure backend systems." : null,
            SkillsJson = completeProfile ? "[\"C#\"]" : "[]", ResumeStorageKey = "private/resume.pdf"
        };
        var experiences = new List<CandidateExperience>();
        var education = new List<CandidateEducation>();
        var projects = new List<CandidateProject>();
        var certifications = new List<CandidateCertification>();
        var links = new List<CandidateProfessionalLink>();
        var custom = new List<PortfolioCustomSection>();
        var repository = new FakeRepository(user, [], experiences, education, projects, certifications, links, custom);
        var service = new CandidatePortfolioService(repository, new FakeUnitOfWork(), new AuditWriterTestDouble(),
            new CreatePortfolioRequestValidator(), new UpdatePortfolioSettingsRequestValidator(),
            new ExperienceRequestValidator(), new EducationRequestValidator(new FixedTimeProvider(Now)),
            new ProjectRequestValidator(), new CertificationRequestValidator(),
            new ProfessionalLinkRequestValidator(), new CustomSectionRequestValidator(),
            new CustomItemRequestValidator(), new FixedTimeProvider(Now));
        return new(service, repository, user, experiences, custom);
    }

    private sealed record Fixture(CandidatePortfolioService Service, FakeRepository Repository,
        User User, List<CandidateExperience> Experiences, List<PortfolioCustomSection> CustomSections);

    private sealed class FakeRepository(
        User user, List<CandidateSkill> skills, List<CandidateExperience> experiences,
        List<CandidateEducation> education, List<CandidateProject> projects,
        List<CandidateCertification> certifications, List<CandidateProfessionalLink> links,
        List<PortfolioCustomSection> customSections) : ICandidatePortfolioRepository
    {
        public HashSet<string> UsedSlugs { get; } = new(StringComparer.OrdinalIgnoreCase);
        private CandidatePortfolio? Portfolio { get; set; }
        public Task<CandidatePortfolioData?> GetCandidateDataAsync(Guid userId, bool tracking, CancellationToken cancellationToken = default) =>
            Task.FromResult(userId == user.Id ? Data() : null);
        public Task<CandidatePortfolioData?> GetPublishedDataAsync(string normalizedSlug, CancellationToken cancellationToken = default) =>
            Task.FromResult(Portfolio?.Status == CandidatePortfolioStatus.Published && Portfolio.NormalizedSlug == normalizedSlug ? Data() : null);
        public Task<bool> SlugExistsAsync(string normalizedSlug, Guid? excludingPortfolioId, CancellationToken cancellationToken = default) =>
            Task.FromResult(UsedSlugs.Contains(normalizedSlug) || Portfolio is not null && Portfolio.NormalizedSlug == normalizedSlug && Portfolio.Id != excludingPortfolioId);
        public Task AddPortfolioAsync(CandidatePortfolio portfolio, CancellationToken cancellationToken = default)
        { Portfolio = portfolio; return Task.CompletedTask; }
        public Task AddAsync<TEntity>(TEntity entity, CancellationToken cancellationToken = default) where TEntity : BaseEntity
        {
            switch (entity)
            {
                case CandidateExperience x: experiences.Add(x); break;
                case CandidateEducation x: education.Add(x); break;
                case CandidateProject x: projects.Add(x); break;
                case CandidateCertification x: certifications.Add(x); break;
                case CandidateProfessionalLink x: links.Add(x); break;
                case PortfolioCustomSection x: customSections.Add(x); break;
                case PortfolioCustomItem x: customSections.Single(y => y.Id == x.SectionId).Items.Add(x); break;
            }
            return Task.CompletedTask;
        }
        public void Remove<TEntity>(TEntity entity) where TEntity : BaseEntity => entity.IsDeleted = true;
        private CandidatePortfolioData Data() => new(user, Portfolio, skills.Where(x => !x.IsDeleted).ToArray(),
            experiences.Where(x => !x.IsDeleted && x.UserId == user.Id).OrderBy(x => x.DisplayOrder).ThenBy(x => x.Id).ToArray(),
            education.Where(x => !x.IsDeleted && x.UserId == user.Id).OrderBy(x => x.DisplayOrder).ThenBy(x => x.Id).ToArray(),
            projects.Where(x => !x.IsDeleted && x.UserId == user.Id).OrderBy(x => x.DisplayOrder).ThenBy(x => x.Id).ToArray(),
            certifications.Where(x => !x.IsDeleted && x.UserId == user.Id).OrderBy(x => x.DisplayOrder).ThenBy(x => x.Id).ToArray(),
            links.Where(x => !x.IsDeleted && x.UserId == user.Id).OrderBy(x => x.DisplayOrder).ThenBy(x => x.Id).ToArray(),
            customSections.Where(x => !x.IsDeleted && x.UserId == user.Id).OrderBy(x => x.DisplayOrder).ThenBy(x => x.Id).ToArray());
    }
    private sealed class FakeUnitOfWork : IUnitOfWork { public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(1); }
    private sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider { public override DateTimeOffset GetUtcNow() => new(utcNow); }
}
