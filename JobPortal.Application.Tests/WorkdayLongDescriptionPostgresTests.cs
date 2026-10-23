using System.Net;
using System.Text.Json;
using JobPortal.Application.Abstractions.Jobs;
using JobPortal.Application.Services;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Infrastructure.Services;
using JobPortal.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;
using Xunit.Abstractions;

namespace JobPortal.Application.Tests;

public sealed class WorkdayLongDescriptionPostgresTests(WorkdayCheckpointDatabase database, ITestOutputHelper output)
    : IClassFixture<WorkdayCheckpointDatabase>
{
    [LocalPostgresTheory]
    [InlineData(20001)]
    [InlineData(100)]
    public async Task WorkdayDescriptionAndReceiptRoundTripWithoutTruncation(int length)
    {
        await using var db = database.Open();
        var email = Guid.NewGuid().ToString("N") + "@fixture.test";
        var owner = new User { Email = email, NormalizedEmail = email.ToUpperInvariant(), FirstName = "Fixture", LastName = "Owner",
            RoleId = await db.Roles.Select(x => x.Id).FirstAsync() };
        db.Add(owner);
        var company = new Company { Name = "Long description fixture " + Guid.NewGuid().ToString("N"),
            Slug = Guid.NewGuid().ToString("N"), OwnerUserId = owner.Id };
        var source = new JobSource { Company = company, CompanyId = company.Id, AtsType = AtsType.Workday,
            AtsIdentifier = "fixture/Careers", CareerPageUrl = "https://fixture.wd1.myworkdayjobs.com/Careers" };
        db.Add(source);
        await db.SaveChangesAsync();
        var run = new JobSourceRun { JobSourceId = source.Id, Status = JobSourceRunStatus.Succeeded,
            QueuedAtUtc = DateTime.UtcNow, NextAttemptAtUtc = DateTime.UtcNow };
        db.Add(run);
        await db.SaveChangesAsync();
        var raw = await FetchAsync(source, "<p>" + new string('x', length) + "</p>");
        db.Add(new JobSourceRunItem { RunId = run.Id, Ordinal = 0, ExternalId = raw.ExternalId!, CommittedAtUtc = DateTime.UtcNow });
        var normalized = new ExternalJobNormalizer().Normalize(raw) with { JobSourceId = source.Id, CompanyId = company.Id };
        output.WriteLine($"Raw HTML length={raw.Description!.Length}; normalized Description length={normalized.Description!.Length}");
        Assert.Equal(length, normalized.Description.Length);
        Assert.Null(normalized.Requirements);
        Assert.Null(normalized.Responsibilities);
        var jobs = new JobRepository(db);
        var fingerprint = new JobFingerprintService();
        var canonical = new UrlCanonicalizer();
        var ingestion = new JobIngestionService(jobs, new CompanyManagementRepository(db), new CategoryManagementRepository(db),
            new JobDeduplicationService(jobs, fingerprint, canonical), fingerprint, new UnitOfWork(db), TimeProvider.System,
            new TestAggregationLocks(), canonical);
        var result = await ingestion.IngestAsync(normalized);
        Assert.Equal(JobIngestionOutcome.Created, result.Outcome);
        var saved = await db.Jobs.AsNoTracking().SingleAsync(x => x.Id == result.JobId);
        Assert.Equal(normalized.Description, saved.Description);
        Assert.True(await db.Set<JobSourceRunItem>().AnyAsync(x => x.RunId == run.Id && x.Ordinal == 0));
    }

    [LocalPostgresFact]
    public async Task BoundedFieldViolationReportsOnlySafeLengthsAndAllowsFollowingItem()
    {
        await using var db = database.Open();
        // Each test must be independent of execution order.
        var owner = new User { Email = Guid.NewGuid().ToString("N") + "@fixture.test", FirstName = "Test", LastName = "Owner",
            RoleId = await db.Roles.Select(x => x.Id).FirstAsync() };
        owner.NormalizedEmail = owner.Email.ToUpperInvariant();
        var company = new Company { Name = "Diagnostics fixture", Slug = Guid.NewGuid().ToString("N"), OwnerUserId = owner.Id };
        db.AddRange(owner, company);
        await db.SaveChangesAsync();
        var job = new Job { CompanyId = company.Id, Title = "Software Engineer", ReferenceNumber = Guid.NewGuid().ToString("N"),
            Slug = Guid.NewGuid().ToString("N"), Description = "Normal description", ApplicationUrl = "https://fixture.test/jobs/1",
            EducationRequirement = new string('s', 201) };
        db.Add(job);
        var logger = new SafeLogger();
        var unit = new UnitOfWork(db, logger);
        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => unit.SaveChangesAsync());
        Assert.Equal("22001", Assert.IsType<PostgresException>(exception.InnerException).SqlState);
        Assert.Equal("Jobs.EducationRequirement (Job.EducationRequirement): 201/200", exception.Data["PersistenceLengthViolations"]);
        var message = Assert.Single(logger.Messages);
        Assert.Contains("Property=EducationRequirement", message);
        Assert.Contains("ObservedLength=201", message);
        Assert.DoesNotContain(job.EducationRequirement, message);
        Assert.DoesNotContain(job.ApplicationUrl, message);
        unit.ResetAfterFailure();
        job.EducationRequirement = "Graduate";
        db.Entry(job).State = EntityState.Added;
        await unit.SaveChangesAsync();
        Assert.True(await db.Jobs.AsNoTracking().AnyAsync(x => x.Id == job.Id));
    }

    private sealed class SafeLogger : Microsoft.Extensions.Logging.ILogger<UnitOfWork>
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel level) => true;
        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel level, Microsoft.Extensions.Logging.EventId id,
            TState state, Exception? exception, Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));
    }

    internal static async Task<RawExternalJob> FetchAsync(JobSource source, string html)
    {
        using var http = new HttpClient(new DetailHandler(html, source.Id.ToString("N")));
        var provider = new WorkdayJobSourceProvider(new Factory(http));
        var snapshot = await provider.FetchSnapshotAsync(source);
        return Assert.Single(snapshot.Jobs);
    }

    private sealed class DetailHandler(string html, string identity) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var json = request.Method == HttpMethod.Post
                ? JsonSerializer.Serialize(new { total = 1, jobPostings = new[] { new { title = "Software Engineer",
                    externalPath = "/job/Pune/Engineer_" + identity, locationsText = "Pune, India" } } })
                : JsonSerializer.Serialize(new { jobPostingInfo = new { title = "Software Engineer", jobReqId = "REQ-" + identity,
                    jobDescription = html, location = "Pune, India" } });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") });
        }
    }
    private sealed class Factory(HttpClient client) : IHttpClientFactory { public HttpClient CreateClient(string name) => client; }
    private sealed class LocalPostgresFactAttribute : FactAttribute
    {
        public LocalPostgresFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WORKDAY_CHECKPOINT_TEST_POSTGRES")))
                Skip = "Requires disposable localhost careerharbor_workday_test PostgreSQL.";
        }
    }
    private sealed class LocalPostgresTheoryAttribute : TheoryAttribute
    {
        public LocalPostgresTheoryAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WORKDAY_CHECKPOINT_TEST_POSTGRES")))
                Skip = "Requires disposable localhost careerharbor_workday_test PostgreSQL.";
        }
    }
}
