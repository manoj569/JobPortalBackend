using System.Reflection;
using System.Security.Claims;
using System.Text;
using JobPortal.API.Controllers;
using JobPortal.Application.Abstractions.Auditing;
using JobPortal.Application.Abstractions.Persistence;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Application.Features.AdminImports;
using JobPortal.Application.Features.AdminManagement;
using JobPortal.Application.Features.Jobs;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class AdminImportTests
{
    private static readonly DateTime Now =
        new(2026, 8, 2, 8, 0, 0, DateTimeKind.Utc);
    private static readonly string[] ExpectedAuditKeys =
    [
        "duplicateRows", "importType", "importedRows", "invalidRows",
        "skippedRows", "totalRows", "validRows"
    ];

    [Fact]
    public async Task ControllerRequiresExactAdministratorRole()
    {
        var authorization = typeof(AdminImportsController)
            .GetCustomAttribute<AuthorizeAttribute>();

        Assert.Equal("Administrator", authorization?.Roles);
        var requirement = new RolesAuthorizationRequirement([authorization!.Roles!]);
        var candidate = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.Role, "Candidate")],
            "test"));
        var candidateContext = new AuthorizationHandlerContext(
            [requirement],
            candidate,
            resource: null);
        await requirement.HandleAsync(candidateContext);
        Assert.False(candidateContext.HasSucceeded);

        var administrator = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.Role, "Administrator")],
            "test"));
        var administratorContext = new AuthorizationHandlerContext(
            [requirement],
            administrator,
            resource: null);
        await requirement.HandleAsync(administratorContext);
        Assert.True(administratorContext.HasSucceeded);
    }

    [Fact]
    public async Task AdministratorCanPreviewCompanyWithoutWriting()
    {
        var fixture = CreateFixture();

        var result = await fixture.Service.PreviewCompaniesAsync(CompanyFile(
            "Example Labs,https://example.invalid,Technology,Pune,25,Example,false"));

        Assert.Equal(1, result.TotalRows);
        Assert.Equal(1, result.ValidRows);
        Assert.True(result.CanCommit);
        Assert.Equal("Valid", Assert.Single(result.Rows).Status);
        Assert.Empty(fixture.Repository.AddedCompanies);
        Assert.Equal(0, fixture.UnitOfWork.SaveCount);
        Assert.Empty(fixture.Audit.Events);
    }

    [Fact]
    public async Task EmptyOversizedWrongExtensionAndMalformedFilesAreRejected()
    {
        var fixture = CreateFixture();
        await Assert.ThrowsAsync<BadRequestException>(() =>
            fixture.Service.PreviewCompaniesAsync(File("empty.csv", string.Empty)));
        await Assert.ThrowsAsync<BadRequestException>(() =>
            fixture.Service.PreviewCompaniesAsync(new(
                "large.csv",
                AdminImportLimits.MaximumFileSizeBytes + 1,
                new MemoryStream([1]))));
        await Assert.ThrowsAsync<BadRequestException>(() =>
            fixture.Service.PreviewCompaniesAsync(File(
                "companies.txt",
                CompanyHeader + "\r\nExample,,,,,,false")));
        await Assert.ThrowsAsync<BadRequestException>(() =>
            fixture.Service.PreviewCompaniesAsync(File(
                "malformed.csv",
                CompanyHeader + "\r\n\"unterminated")));
    }

    [Theory]
    [InlineData("name,websiteUrl,industry,location,employeeCount,description")]
    [InlineData("name,name,websiteUrl,industry,location,employeeCount,description,isVerified")]
    [InlineData("name,websiteUrl,industry,location,employeeCount,description,isVerified,secret")]
    public async Task MissingDuplicateAndUnknownHeadersAreRejected(string header)
    {
        var fixture = CreateFixture();
        var file = File("companies.csv", header + "\r\nExample,,,,,,false");

        await Assert.ThrowsAsync<BadRequestException>(() =>
            fixture.Service.PreviewCompaniesAsync(file));
    }

    [Fact]
    public async Task MoreThanFiveHundredDataRowsAreRejected()
    {
        var fixture = CreateFixture();
        var rows = string.Join(
            "\r\n",
            Enumerable.Range(1, AdminImportLimits.MaximumDataRows + 1)
                .Select(index => $"Example {index},,,,,,false"));

        await Assert.ThrowsAsync<BadRequestException>(() =>
            fixture.Service.PreviewCompaniesAsync(CompanyFile(rows)));
    }

    [Fact]
    public async Task InvalidCompanyRowReportsRowAndFieldErrors()
    {
        var fixture = CreateFixture();

        var result = await fixture.Service.PreviewCompaniesAsync(CompanyFile(
            ",not-a-url,Technology,Pune,not-a-number,Example,maybe"));

        var row = Assert.Single(result.Rows);
        Assert.Equal(2, row.RowNumber);
        Assert.Equal("Invalid", row.Status);
        Assert.Contains(row.Errors, error => error.Field == "name");
        Assert.Contains(row.Errors, error => error.Field == "websiteUrl");
        Assert.Contains(row.Errors, error => error.Field == "employeeCount");
        Assert.Contains(row.Errors, error => error.Field == "isVerified");
    }

    [Fact]
    public async Task ExistingCompanyIsPreviewedAndSafelyUpdatedWithoutVerificationChange()
    {
        var fixture = CreateFixture();
        var existing = NewCompany("Example Labs", isVerified: true);
        existing.WebsiteUrl = "https://old.example.invalid";
        fixture.Repository.Companies.Add(existing);
        var fileContent =
            "Example Labs,https://new.example.invalid,Technology,Pune,50,Updated,true";

        var preview = await fixture.Service.PreviewCompaniesAsync(
            CompanyFile(fileContent));
        var committed = await fixture.Service.CommitCompaniesAsync(
            Guid.NewGuid(),
            CompanyFile(fileContent));

        Assert.Equal("Update existing", Assert.Single(preview.Rows).Status);
        Assert.Equal("Updated", Assert.Single(committed.Rows).Status);
        Assert.True(existing.IsVerified);
        Assert.Equal("https://new.example.invalid", existing.WebsiteUrl);
        Assert.Empty(fixture.Repository.AddedCompanies);
        Assert.Equal(1, fixture.UnitOfWork.SaveCount);
    }

    [Fact]
    public async Task NewCompanyCannotBeVerifiedByCsv()
    {
        var fixture = CreateFixture();

        var result = await fixture.Service.CommitCompaniesAsync(
            Guid.NewGuid(),
            CompanyFile("New Company,https://example.invalid,Tech,Pune,10,Example,true"));

        Assert.Equal(1, result.ImportedRows);
        Assert.False(Assert.Single(fixture.Repository.AddedCompanies).IsVerified);
    }

    [Fact]
    public async Task JobPreviewReportsReferencesUrlSalaryEnumsAndExpiryErrors()
    {
        var fixture = CreateFixture();
        var expired = Now.AddMinutes(-1).ToString("O");
        var row =
            $"Example Role,Missing Company,Missing Category,Description,not-a-url,Unknown,Somewhere,None,Pune,200,100,INR,{expired},Responsibilities,Requirements,Benefits,false";

        var result = await fixture.Service.PreviewJobsAsync(JobFile(row));

        var invalid = Assert.Single(result.Rows);
        Assert.Equal(2, invalid.RowNumber);
        Assert.Contains(invalid.Errors, error => error.Field == "applicationUrl");
        Assert.Contains(invalid.Errors, error => error.Field == "maxSalary");
        Assert.Contains(invalid.Errors, error => error.Field == "employmentType");
        Assert.Contains(invalid.Errors, error => error.Field == "workplaceType");
        Assert.Contains(invalid.Errors, error => error.Field == "experienceLevel");
        Assert.Contains(invalid.Errors, error => error.Field == "expiresAtUtc");
    }

    [Fact]
    public async Task ExistingAndInFileDuplicateJobsAreSkipped()
    {
        var fixture = CreateFixtureWithReferences();
        fixture.Repository.ExistingJobs.Add(new(
            fixture.Repository.Companies[0].Id,
            "Existing Role",
            "https://jobs.example.invalid/existing"));
        var rows = string.Join("\r\n",
            ValidJobRow("Existing Role", "https://jobs.example.invalid/existing"),
            ValidJobRow("New Role", "https://jobs.example.invalid/new"),
            ValidJobRow("New Role", "https://jobs.example.invalid/new"));

        var result = await fixture.Service.PreviewJobsAsync(JobFile(rows));

        Assert.Equal(3, result.TotalRows);
        Assert.Equal(1, result.ValidRows);
        Assert.Equal(2, result.DuplicateRows);
        Assert.Equal(2, result.SkippedRows);
        Assert.Equal(2, result.Rows.Count(row => row.Status == "Skip duplicate"));
    }

    [Fact]
    public async Task ValidCommitCreatesOnlyVisibleUnfeaturedDraftJobs()
    {
        var fixture = CreateFixtureWithReferences();
        var expiresAt = Now.AddDays(30).ToString("O");
        var row =
            $"Imported Role,Example Company,Technology,Description,https://jobs.example.invalid/imported,FullTime,Remote,Mid,Pune,100,200,INR,{expiresAt},Responsibilities,Requirements,Benefits,true";

        var result = await fixture.Service.CommitJobsAsync(JobFile(row));

        Assert.Equal(1, result.ImportedRows);
        var job = Assert.Single(fixture.Repository.AddedJobs);
        Assert.Equal(JobStatus.Draft, job.Status);
        Assert.False(job.IsHidden);
        Assert.False(job.IsFeatured);
        Assert.Null(job.PublishedAtUtc);
        Assert.Null(job.RecruiterContact);
        Assert.Equal(1, fixture.UnitOfWork.SaveCount);
    }

    [Fact]
    public async Task UnifiedJobCsvCreatesAndReusesRelationsAndImportsOptionalRecruiterContact()
    {
        var fixture = CreateFixture();
        var rows = string.Join("\r\n",
            UnifiedJobRow("Role One", " New Company ", " New Category ",
                recruiterName: "Jane Recruiter", recruiterRole: "Talent Partner",
                recruiterEmail: "jane@example.test", recruiterPhone: "+91 99999 99999",
                sharingApproved: "true"),
            UnifiedJobRow("Role Two", "new company", "new category"));

        var preview = await fixture.Service.PreviewJobsAsync(
            File("jobs.csv", UnifiedJobHeader + "\r\n" + rows));
        var committed = await fixture.Service.CommitJobsAsync(
            Guid.NewGuid(), File("jobs.csv", UnifiedJobHeader + "\r\n" + rows));

        Assert.Equal(2, preview.ValidRows);
        Assert.Equal("Create", preview.Rows.First().CompanyResolution);
        Assert.Equal("Reuse", preview.Rows.Last().CompanyResolution);
        Assert.Equal(2, committed.ImportedRows);
        Assert.Equal(2, fixture.Repository.AddedJobs.Count);
        Assert.Same(fixture.Repository.AddedJobs[0].Company, fixture.Repository.AddedJobs[1].Company);
        Assert.Same(fixture.Repository.AddedJobs[0].Category, fixture.Repository.AddedJobs[1].Category);
        var contact = Assert.IsType<JobRecruiterContact>(fixture.Repository.AddedJobs[0].RecruiterContact);
        Assert.Equal("jane@example.test", contact.Email);
        Assert.True(contact.IsSharingApproved);
        Assert.Null(fixture.Repository.AddedJobs[1].RecruiterContact);
        Assert.Null(fixture.Repository.AddedJobs[1].Location);
        Assert.Null(fixture.Repository.AddedJobs[1].MinimumSalary);
        Assert.Null(fixture.Repository.AddedJobs[1].MaximumSalary);
        Assert.Null(fixture.Repository.AddedJobs[1].ExpiresAtUtc);
        Assert.Null(fixture.Repository.AddedJobs[1].Responsibilities);
        Assert.Equal(JobStatus.Draft, fixture.Repository.AddedJobs[0].Status);
    }

    [Fact]
    public async Task UnifiedJobCsvRejectsInvalidRecruiterAndLeavesNoAggregateChanges()
    {
        var fixture = CreateFixture();
        var row = UnifiedJobRow("Role", "Rollback Company", "Rollback Category",
            recruiterName: "Recruiter", recruiterRole: "Role",
            recruiterEmail: "not-an-email", recruiterPhone: "invalid",
            sharingApproved: "true");

        var result = await fixture.Service.CommitJobsAsync(Guid.NewGuid(),
            File("jobs.csv", UnifiedJobHeader + "\r\n" + row));

        var invalid = Assert.Single(result.Rows);
        Assert.Contains(invalid.Errors, error => error.Field == "recruiterEmail" &&
            error.SubmittedValue == "not-an-email");
        Assert.Empty(fixture.Repository.AddedJobs);
        Assert.Equal(0, fixture.UnitOfWork.SaveCount);
        Assert.Empty(fixture.Audit.Events);
    }

    [Fact]
    public async Task OptionalSearchFieldsAreImportedWithoutBreakingLegacyCsv()
    {
        var fixture = CreateFixtureWithReferences();
        var companyResult = await fixture.Service.CommitCompaniesAsync(
            Guid.NewGuid(),
            File(
                "companies.csv",
                CompanyTemplateHeader + "\r\n" +
                "Filter Company,https://example.invalid,Technology,Pune,25,Example,false,Startup"));
        var row =
            "Filtered Intern,Example Company,Technology,Description,https://jobs.example.invalid/filtered,Internship,Hybrid,Entry,Pune,100,200,INR,,Responsibilities,Requirements,Benefits,true,0,2,3,false,Engineering,Software Development,B.Tech/B.E.,Company";

        var jobResult = await fixture.Service.CommitJobsAsync(
            File("jobs.csv", LegacyExtendedJobHeader + "\r\n" + row));

        Assert.Equal(1, companyResult.ImportedRows);
        Assert.Equal(CompanyType.Startup, Assert.Single(fixture.Repository.AddedCompanies).CompanyType);
        Assert.Equal(1, jobResult.ImportedRows);
        var job = Assert.Single(fixture.Repository.AddedJobs);
        Assert.Equal(0, job.MinimumExperienceYears);
        Assert.Equal(2, job.MaximumExperienceYears);
        Assert.Equal(3, job.InternshipDurationMonths);
        Assert.False(job.IsFlexibleDuration);
        Assert.Equal("Engineering", job.Department);
        Assert.Equal("Software Development", job.RoleCategory);
        Assert.Equal("B.Tech/B.E.", job.EducationRequirement);
        Assert.Equal(PostedByType.Company, job.PostedByType);
        Assert.Equal(JobStatus.Draft, job.Status);
        Assert.False(job.IsFeatured);
        Assert.Null(job.PublishedAtUtc);
    }

    [Fact]
    public async Task InvalidOptionalSearchFieldsRejectTheWholeCsv()
    {
        var fixture = CreateFixtureWithReferences();
        var row =
            "Invalid Intern,Example Company,Technology,Description,https://jobs.example.invalid/invalid,Internship,Hybrid,Entry,Pune,,,INR,,Responsibilities,Requirements,Benefits,false,5,2,4,false,Engineering,Software Development,Graduate,Unknown";

        var result = await fixture.Service.CommitJobsAsync(
            File("jobs.csv", LegacyExtendedJobHeader + "\r\n" + row));

        Assert.Equal(1, result.InvalidRows);
        var errors = Assert.Single(result.Rows).Errors;
        Assert.Contains(errors, error => error.Field == "maxExperienceYears");
        Assert.Contains(errors, error => error.Field == "internshipDurationMonths");
        Assert.Contains(errors, error => error.Field == "postedByType");
        Assert.Empty(fixture.Repository.AddedJobs);
        Assert.Empty(fixture.Audit.Events);
    }

    [Fact]
    public async Task InvalidCommitMakesNoPartialChangesAndNoAuditEntry()
    {
        var fixture = CreateFixtureWithReferences();
        var rows = string.Join("\r\n",
            ValidJobRow("Valid Role", "https://jobs.example.invalid/valid"),
            "Invalid Role,Example Company,Technology,Description,invalid,FullTime,Remote,Mid,Pune,,,INR,,Responsibilities,Requirements,Benefits,false");

        var result = await fixture.Service.CommitJobsAsync(JobFile(rows));

        Assert.Equal(1, result.InvalidRows);
        Assert.False(result.CanCommit);
        Assert.Equal(0, result.ImportedRows);
        Assert.Empty(fixture.Repository.AddedJobs);
        Assert.Equal(0, fixture.UnitOfWork.SaveCount);
        Assert.Empty(fixture.Audit.Events);
    }

    [Fact]
    public async Task CommitAuditContainsCountsOnly()
    {
        var fixture = CreateFixtureWithReferences();
        const string sensitiveMarker = "private-row-description@example.invalid";
        var row =
            $"Audited Role,Example Company,Technology,{sensitiveMarker},https://jobs.example.invalid/audited,FullTime,Remote,Mid,Pune,,,INR,,Responsibilities,Requirements,Benefits,false";

        await fixture.Service.CommitJobsAsync(JobFile(row));

        var audit = Assert.Single(fixture.Audit.Events);
        Assert.Equal(AuditAction.Upload, audit.Action);
        Assert.Equal("AdminCsvImport", audit.EntityType);
        Assert.Equal(
            ExpectedAuditKeys,
            audit.Metadata!.Keys.OrderBy(key => key, StringComparer.Ordinal));
        Assert.DoesNotContain(
            sensitiveMarker,
            string.Join("|", audit.Metadata.Values),
            StringComparison.Ordinal);
    }

    [Fact]
    public void TemplatesContainExactHeadersAndFictionalRows()
    {
        var fixture = CreateFixture();

        var companies = Encoding.UTF8.GetString(
            fixture.Service.GetCompaniesTemplate().Content);
        var jobs = Encoding.UTF8.GetString(
            fixture.Service.GetJobsTemplate().Content);

        Assert.StartsWith(CompanyTemplateHeader + "\r\n", companies, StringComparison.Ordinal);
        Assert.StartsWith(JobTemplateHeader + "\r\n", jobs, StringComparison.Ordinal);
        Assert.Contains("example.invalid", companies, StringComparison.Ordinal);
        Assert.Contains("example.invalid", jobs, StringComparison.Ordinal);
    }

    private static Fixture CreateFixture()
    {
        var repository = new ImportRepositoryFake();
        var unitOfWork = new UnitOfWorkFake();
        var audit = new AuditWriterTestDouble();
        var service = new AdminImportService(
            repository,
            unitOfWork,
            audit,
            new CreateCompanyRequestValidator(),
            new FixedTimeProvider(Now));
        return new(service, repository, unitOfWork, audit);
    }

    private static Fixture CreateFixtureWithReferences()
    {
        var fixture = CreateFixture();
        fixture.Repository.Companies.Add(NewCompany("Example Company"));
        fixture.Repository.Categories.Add(new Category
        {
            Id = Guid.NewGuid(),
            Name = "Technology",
            Slug = "technology"
        });
        return fixture;
    }

    private static Company NewCompany(
        string name,
        bool isVerified = false) => new()
        {
            Id = Guid.NewGuid(),
            Name = name,
            Slug = name.ToLowerInvariant().Replace(' ', '-'),
            IsVerified = isVerified
        };

    private static CsvImportFile CompanyFile(string rows) =>
        File("companies.csv", CompanyHeader + "\r\n" + rows);

    private static CsvImportFile JobFile(string rows) =>
        File("jobs.csv", JobHeader + "\r\n" + rows);

    private static CsvImportFile File(string fileName, string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        return new(fileName, bytes.Length, new MemoryStream(bytes));
    }

    private static string ValidJobRow(string title, string applicationUrl) =>
        $"{title},Example Company,Technology,Description,{applicationUrl},FullTime,Remote,Mid,Pune,,,INR,,Responsibilities,Requirements,Benefits,false";

    private static string UnifiedJobRow(string title, string company, string category,
        string recruiterName = "", string recruiterRole = "", string recruiterEmail = "",
        string recruiterPhone = "", string sharingApproved = "") => string.Join(',', new[]
        {
            title, company, category, "", "", "", "", "", "", "", "", "", "", "", "", "",
            "https://company.example.invalid", "", "Technology", "Pune", "Company description", "42", "true",
            "Category description", "3", recruiterName, recruiterRole, recruiterEmail,
            recruiterPhone, sharingApproved
        });

    private const string CompanyHeader =
        "name,websiteUrl,industry,location,employeeCount,description,isVerified";

    private const string JobHeader =
        "title,companyName,categoryName,description,applicationUrl,employmentType,workplaceType,experienceLevel,location,minSalary,maxSalary,currencyCode,expiresAtUtc,responsibilities,requirements,benefits,isFeatured";

    private const string CompanyTemplateHeader = CompanyHeader + ",companyType";

    private const string JobTemplateHeader =
        "title,companyName,categoryName,description,applicationUrl,employmentType,workplaceType,experienceLevel,location,minSalary,maxSalary,currencyCode,expiresAtUtc,responsibilities,requirements,benefits,companyWebsiteUrl,companyLogoUrl,companyIndustry,companyLocation,companyDescription,companyEmployeeCount,companyIsVerified,categoryDescription,categoryDisplayOrder,recruiterName,recruiterRole,recruiterEmail,recruiterPhoneNumber,recruiterContactSharingApproved";

    private const string UnifiedJobHeader = JobTemplateHeader;

    private const string LegacyExtendedJobHeader = JobHeader +
        ",minExperienceYears,maxExperienceYears,internshipDurationMonths,isFlexibleDuration,department,roleCategory,educationRequirement,postedByType";

    private sealed record Fixture(
        AdminImportService Service,
        ImportRepositoryFake Repository,
        UnitOfWorkFake UnitOfWork,
        AuditWriterTestDouble Audit);

    private sealed class ImportRepositoryFake : IAdminImportRepository
    {
        public List<Company> Companies { get; } = [];
        public List<Category> Categories { get; } = [];
        public List<ExistingJobImportIdentity> ExistingJobs { get; } = [];
        public List<Company> AddedCompanies { get; } = [];
        public List<Job> AddedJobs { get; } = [];

        public Task<IReadOnlyCollection<Company>> FindCompaniesAsync(
            IReadOnlyCollection<string> slugs,
            IReadOnlyCollection<string> normalizedNames,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyCollection<Company>>(Companies);

        public Task<IReadOnlyCollection<Category>> FindCategoriesAsync(
            IReadOnlyCollection<string> slugs,
            IReadOnlyCollection<string> normalizedNames,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyCollection<Category>>(Categories);

        public Task<IReadOnlyCollection<ExistingJobImportIdentity>> FindJobIdentitiesAsync(
            IReadOnlyCollection<Guid> companyIds,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyCollection<ExistingJobImportIdentity>>(
                ExistingJobs);

        public Task AddCompaniesAsync(
            IReadOnlyCollection<Company> companies,
            CancellationToken cancellationToken = default)
        {
            AddedCompanies.AddRange(companies);
            return Task.CompletedTask;
        }

        public void UpdateCompany(Company company)
        {
        }

        public Task AddJobsAsync(
            IReadOnlyCollection<Job> jobs,
            CancellationToken cancellationToken = default)
        {
            AddedJobs.AddRange(jobs);
            return Task.CompletedTask;
        }
    }

    private sealed class UnitOfWorkFake : IUnitOfWork
    {
        public int SaveCount { get; private set; }

        public Task<int> SaveChangesAsync(
            CancellationToken cancellationToken = default)
        {
            SaveCount++;
            return Task.FromResult(1);
        }
    }

    private sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow);
    }
}
