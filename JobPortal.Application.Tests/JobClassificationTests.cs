using System.Text.Json;
using JobPortal.Application.Abstractions.Jobs;
using JobPortal.Application.Features.Jobs;
using JobPortal.Application.Features.PublicJobs;
using JobPortal.Application.Services;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Maintenance;
using JobPortal.Persistence.Context;
using JobPortal.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class JobClassificationTests
{
    private static JobClassificationResult Analyze(string? description, string? location = null) =>
        new JobClassificationService().Analyze(new("Engineer", description, location));
    [Theory]
    [InlineData("Full-time opportunity", EmploymentType.FullTime)]
    [InlineData("Part-time position", EmploymentType.PartTime)]
    [InlineData("6-month contract", EmploymentType.Contract)]
    [InlineData("Summer internship", EmploymentType.Internship)]
    [InlineData("Freelance developer", EmploymentType.Freelance)]
    [InlineData("Temporary position", EmploymentType.Temporary)]
    [InlineData("15 years full time education", EmploymentType.Unknown)]
    [InlineData("Manage client contracts", EmploymentType.Unknown)]
    [InlineData("", EmploymentType.Unknown)]
    public void EmploymentIsEvidenceBased(string text, EmploymentType expected) => Assert.Equal(expected, Analyze(text).EmploymentType);
    [Theory]
    [InlineData("Work from office", WorkplaceType.OnSite)]
    [InlineData("WFO", WorkplaceType.OnSite)]
    [InlineData("Onsite position", WorkplaceType.OnSite)]
    [InlineData("Office-based", WorkplaceType.OnSite)]
    [InlineData("Mandatory office attendance", WorkplaceType.OnSite)]
    [InlineData("Five days working from office", WorkplaceType.OnSite)]
    [InlineData("Work from home", WorkplaceType.Remote)]
    [InlineData("WFH", WorkplaceType.Remote)]
    [InlineData("Fully remote", WorkplaceType.Remote)]
    [InlineData("100% remote", WorkplaceType.Remote)]
    [InlineData("Remote within India", WorkplaceType.Remote)]
    [InlineData("Hybrid work model", WorkplaceType.Hybrid)]
    [InlineData("Three days office and two days remote", WorkplaceType.Hybrid)]
    [InlineData("Hyderabad office", WorkplaceType.Unknown)]
    [InlineData("This position is based at our Hyderabad office", WorkplaceType.Unknown)]
    [InlineData("Not fully remote", WorkplaceType.Unknown)]
    public void WorkModeNeedsExplicitAttendanceEvidence(string text, WorkplaceType expected) => Assert.Equal(expected, Analyze(text).WorkplaceType);
    [Theory]
    [InlineData("Minimum 3 years experience", 3, null)]
    [InlineData("Minimum 3 year(s) of experience is required", 3, null)]
    [InlineData("3–5 years experience", 3, 5)]
    [InlineData("3 to 5 years experience", 3, 5)]
    [InlineData("At least 5 years", 5, null)]
    [InlineData("5+ years experience", 5, null)]
    [InlineData("More than 5 years experience", 6, null)]
    [InlineData("0–1 years experience", 0, 1)]
    [InlineData("Freshers can apply", 0, null)]
    [InlineData("Freshers welcome", 0, null)]
    [InlineData("No experience required", 0, null)]
    [InlineData("15 years full time education. Established 20 years ago", null, null)]
    [InlineData("5 years experience preferred", null, null)]
    [InlineData("Minimum 3 years experience. 5 years experience preferred", 3, null)]
    public void RequiredExperienceIsExact(string text, int? minimum, int? maximum)
    { var r = Analyze(text); Assert.Equal(minimum, r.MinimumExperienceYears); Assert.Equal(maximum, r.MaximumExperienceYears); }
    [Theory]
    [InlineData("₹8–12 LPA", 800000, 1200000, "INR", SalaryPeriod.Yearly)]
    [InlineData("₹50,000 per month", 50000, null, "INR", SalaryPeriod.Monthly)]
    [InlineData("$90,000–120,000 annually", 90000, 120000, "USD", SalaryPeriod.Yearly)]
    [InlineData("₹8.5–12.5 LPA", 850000, 1250000, "INR", SalaryPeriod.Yearly)]
    [InlineData("GBP 40 per hour", 40, null, "GBP", SalaryPeriod.Hourly)]
    public void SalaryUsesOnlyDisclosedAmounts(string text, decimal min, int? max, string currency, SalaryPeriod period)
    { var r = Analyze(text); Assert.Equal(min, r.MinimumSalary); Assert.Equal(max, r.MaximumSalary); Assert.Equal(currency, r.CurrencyCode); Assert.Equal(period, r.SalaryPeriod); }
    [Theory]
    [InlineData(null)]
    [InlineData("Competitive salary in India")]
    [InlineData("Signing bonus $5,000 annually")]
    [InlineData("Benefits ₹50,000 per month")]
    [InlineData("We employ 90,000 people")]
    [InlineData("<script>₹8–12 LPA</script><p>No salary disclosed</p>")]
    [InlineData("<script>₹8–12 LPA")]
    public void UndisclosedSalaryAndUnsafeHtmlDoNotInventValues(string? text)
    { var r = Analyze(text); Assert.Null(r.MinimumSalary); Assert.Null(r.MaximumSalary); Assert.Null(r.CurrencyCode); Assert.Null(r.SalaryPeriod); }
    [Fact]
    public void AccentureSampleExcludesEducationAndPreservesApplicationIdentity()
    {
        var job = new Job { Title = "Packaged/SaaS Application Engineer", Location = "Bengaluru",
            Description = "Minimum 3 year(s) of experience is required.\nThe candidate should have minimum 3 years of experience in ServiceNow.\nThis position is based at our Hyderabad office.\n15 years full time education.",
            ApplicationUrl = "https://accenture.wd3.myworkdayjobs.com/job/verified", FingerprintHash = "original", JobSourceId = Guid.NewGuid(), ExternalJobId = "R123", Status = JobStatus.Published };
        var result = JobClassificationPolicy.Analyze(job); JobClassificationPolicy.Apply(job, result);
        Assert.Equal(3, job.MinimumExperienceYears); Assert.Null(job.MaximumExperienceYears);
        Assert.Equal("Hyderabad", result.DescriptionLocation); Assert.Contains("Location", result.ReviewFields);
        Assert.Equal("Bengaluru", job.Location); Assert.Equal(WorkplaceType.Unknown, job.WorkplaceType);
        Assert.Equal(EmploymentType.Unknown, job.EmploymentType); Assert.Null(job.CurrencyCode); Assert.Null(job.MinimumSalary);
        Assert.Equal("https://accenture.wd3.myworkdayjobs.com/job/verified", job.ApplicationUrl); Assert.Equal("original", job.FingerprintHash);
        Assert.Equal("R123", job.ExternalJobId); Assert.Equal(JobStatus.Published, job.Status);
    }
    [Fact]
    public void ConflictingModesRequireReviewAndMetadataHasPrecedenceInSuggestions()
    {
        var r = new JobClassificationService().Analyze(new("Engineer", "Fully remote. Work from office", EmploymentType: EmploymentType.Contract, WorkplaceType: WorkplaceType.Hybrid));
        Assert.Equal(WorkplaceType.Hybrid, r.WorkplaceType); Assert.Equal(EmploymentType.Contract, r.EmploymentType);
        Assert.Contains("WorkplaceType", r.ReviewFields); Assert.NotEmpty(r.Conflicts);
    }
    [Fact]
    public void ManualOverridesIncludingExplicitNullsSurviveReprocessing()
    {
        var job = new Job { Description = "Minimum 5 years experience. Fully remote. ₹8–12 LPA", WorkplaceType = WorkplaceType.OnSite,
            MinimumExperienceYears = 2, CurrencyCode = null, ManualClassificationOverrides = "WorkplaceType,Experience,Salary" };
        JobClassificationPolicy.Apply(job, JobClassificationPolicy.Analyze(job));
        Assert.Equal(WorkplaceType.OnSite, job.WorkplaceType); Assert.Equal(2, job.MinimumExperienceYears); Assert.Null(job.MinimumSalary); Assert.Null(job.CurrencyCode);
    }
    [Fact]
    public void HistoricalDefaultsRemainUncertainAcrossRepeatedPasses()
    {
        var job = new Job { Description = "Fully remote. Minimum 3 years experience", WorkplaceType = WorkplaceType.OnSite, EmploymentType = EmploymentType.FullTime, CurrencyCode = "USD" };
        JobClassificationPolicy.Apply(job, JobClassificationPolicy.Analyze(job), historical: true);
        var metadata = job.ClassificationJson;
        JobClassificationPolicy.Apply(job, JobClassificationPolicy.Analyze(job), historical: true);
        Assert.Equal(metadata, job.ClassificationJson); Assert.Equal(WorkplaceType.OnSite, job.WorkplaceType);
        Assert.Equal("USD", job.CurrencyCode); Assert.Equal(3, job.MinimumExperienceYears);
        var r = JsonSerializer.Deserialize<JobClassificationResult>(job.ClassificationJson!)!;
        Assert.Contains("Salary", r.ReviewFields); Assert.Contains("WorkplaceType", r.ReviewFields); Assert.Contains("EmploymentType", r.ReviewFields);
    }
    [Fact]
    public void HistoricalSalaryPeriodDisagreementRequiresReviewWithoutChangingAcceptedValues()
    {
        var job = new Job { Description = "Salary INR 50000 per year", MinimumSalary = 50000,
            CurrencyCode = "INR", SalaryPeriod = SalaryPeriod.Monthly };
        JobClassificationPolicy.Apply(job, JobClassificationPolicy.Analyze(job), historical: true);
        Assert.Equal(SalaryPeriod.Monthly, job.SalaryPeriod);
        Assert.Equal(50000, job.MinimumSalary);
        Assert.Contains("Salary", JsonSerializer.Deserialize<JobClassificationResult>(job.ClassificationJson!)!.ReviewFields);
        var metadata = job.ClassificationJson;
        JobClassificationPolicy.Apply(job, JobClassificationPolicy.Analyze(job), historical: true);
        Assert.Equal(metadata, job.ClassificationJson);
    }
    [Fact]
    public void AutomaticValuesCanBeCorrectedAndClearedWithoutInventedDefaults()
    {
        var job = new Job { Description = "Fully remote. Minimum 3 years experience. ₹8–12 LPA" };
        JobClassificationPolicy.Apply(job, JobClassificationPolicy.Analyze(job));
        job.Description = "Hybrid schedule. Minimum 5 years experience";
        JobClassificationPolicy.Apply(job, JobClassificationPolicy.Analyze(job));
        Assert.Equal(WorkplaceType.Hybrid, job.WorkplaceType); Assert.Equal(5, job.MinimumExperienceYears); Assert.Null(job.CurrencyCode); Assert.Null(job.MinimumSalary);
    }
    [Fact]
    public void AdminMappingReturnsSuggestionsAndKeepsExplicitCorrections()
    {
        var job = new Job { Company = new Company { Name = "Acme" } };
        job.Apply(new("Engineer", "Minimum 5 years experience. Fully remote", Guid.NewGuid(), null, "https://example.test/job/1",
            null, null, null, "Pune", null, null, null, EmploymentType.Unknown, WorkplaceType.OnSite, ExperienceLevel.Mid, null, MinimumExperienceYears: 2));
        Assert.Equal(2, job.MinimumExperienceYears); Assert.Equal(WorkplaceType.OnSite, job.WorkplaceType);
        Assert.NotNull(job.ToResponse().ClassificationJson); Assert.Null(job.ToResponse().CurrencyCode);
    }
    [Theory]
    [InlineData(AtsType.Workday)]
    [InlineData(AtsType.Greenhouse)]
    [InlineData(AtsType.Lever)]
    [InlineData(AtsType.Ashby)]
    [InlineData(AtsType.SuccessFactors)]
    [InlineData(AtsType.Custom)]
    public async Task SharedPersistenceClassifiesAllProviderIdentitiesAndRefreshIsIdempotent(AtsType type)
    {
        using var f = new JobSourceFixture(); f.Source.AtsType = type;
        var repo = new JobRepository(f.Context); var fingerprints = new JobFingerprintService();
        var service = new JobIngestionService(repo, new CompanyManagementRepository(f.Context), new CategoryManagementRepository(f.Context),
            new JobDeduplicationService(repo, fingerprints, new UrlCanonicalizer()), fingerprints, new UnitOfWork(f.Context), TimeProvider.System, f.Locks, new UrlCanonicalizer());
        var raw = new RawExternalJob { Title = "Software Engineer", CompanyId = f.Company.Id, CompanyName = "Acme", JobSourceId = f.Source.Id,
            ExternalId = "123", Location = "Pune", ApplicationUrl = "https://example.test/jobs/123", Description = "Minimum 3 years experience. Fully remote. Full-time opportunity. ₹8–12 LPA" };
        Assert.Equal(JobIngestionOutcome.Created, (await service.IngestAsync(raw)).Outcome);
        var job = Assert.Single(f.Context.Jobs); Assert.Equal(3, job.MinimumExperienceYears); Assert.Null(job.MaximumExperienceYears);
        Assert.Equal("INR", job.CurrencyCode); Assert.Equal(SalaryPeriod.Yearly, job.SalaryPeriod); Assert.Equal(WorkplaceType.Remote, job.WorkplaceType);
        var metadata = job.ClassificationJson;
        Assert.Equal(JobIngestionOutcome.Unchanged, (await service.IngestAsync(raw)).Outcome);
        Assert.Equal(metadata, job.ClassificationJson); Assert.Equal(raw.ApplicationUrl, job.ApplicationUrl);
        job.ManualClassificationOverrides = "Experience,Salary,WorkplaceType,EmploymentType,Location";
        job.MinimumExperienceYears = 1; job.MinimumSalary = 2; job.WorkplaceType = WorkplaceType.Hybrid; job.EmploymentType = EmploymentType.PartTime; job.Location = "Hyderabad";
        await f.Context.SaveChangesAsync();
        await service.IngestAsync(raw with { Description = "Minimum 10 years experience. Work from office", Location = "Mumbai" });
        Assert.Equal(1, job.MinimumExperienceYears); Assert.Equal(2, job.MinimumSalary); Assert.Equal(WorkplaceType.Hybrid, job.WorkplaceType); Assert.Equal("Hyderabad", job.Location);
        Assert.Equal(EmploymentType.PartTime, job.EmploymentType);
        Assert.Equal("INR", job.CurrencyCode); Assert.Equal(SalaryPeriod.Yearly, job.SalaryPeriod);
        job.MinimumSalary = null; job.MaximumSalary = null; job.CurrencyCode = null; job.SalaryPeriod = null;
        job.MinimumExperienceYears = null; job.MaximumExperienceYears = null;
        await f.Context.SaveChangesAsync();
        await service.IngestAsync(raw);
        Assert.Null(job.MinimumSalary); Assert.Null(job.CurrencyCode); Assert.Null(job.SalaryPeriod);
        Assert.Null(job.MinimumExperienceYears); Assert.Null(job.MaximumExperienceYears);
    }
}

public sealed class ClassificationBackfillTests
{
    [Fact]
    public async Task RecoveredAnalysisAndCommittedWritesRetainAccurateCountersAfterAuditFailures()
    {
        var job = new Job { Title = null!, Description = "Minimum 3 years experience" };
        var store = new Store(job); var progress = new ClassificationProgress();
        await ClassificationBackfill.RunAsync(store, new(true, 1, 1, "unused"), progress, _ => Task.CompletedTask, () => Task.CompletedTask, default);
        Assert.Single(progress.RetryIds); Assert.Equal(0, progress.Proposed);
        store.Jobs[job.Id].Title = "Engineer";
        var failedOutcome = false;
        await ClassificationBackfill.RunAsync(store, new(true, 1, 1, "unused"), progress, item =>
        {
            var entry = JsonSerializer.SerializeToElement(item);
            if (!failedOutcome && entry.TryGetProperty("Phase", out var phase) && phase.GetString() == "Outcome")
            { failedOutcome = true; throw new IOException("Outcome unavailable"); }
            return Task.CompletedTask;
        }, () => Task.CompletedTask, default);
        Assert.Equal(1, progress.Proposed); Assert.Equal(1, progress.Applied); Assert.Single(progress.RetryIds);
        await ClassificationBackfill.RunAsync(store, new(true, 1, 1, "unused"), progress, _ => Task.CompletedTask, () => Task.CompletedTask, default);
        Assert.Empty(progress.RetryIds); Assert.Empty(progress.UncountedRetryAnalyses); Assert.Equal(0, progress.Remaining);
        Assert.Equal(1, progress.Applied); Assert.Equal(1, store.Writes);
    }
    [Fact]
    public async Task ExistingDuplicateUrlsRemainIndependentRecords()
    {
        var first = new Job { Description = "Minimum 3 years experience", ApplicationUrl = "https://example.test/duplicate", Status = JobStatus.Published, ExternalJobId = "first" };
        var second = new Job { Description = "Minimum 5 years experience", ApplicationUrl = first.ApplicationUrl, Status = JobStatus.Draft, ExternalJobId = "second" };
        var store = new Store(first, second);
        await ClassificationBackfill.RunAsync(store, new(true, 1, 2, "unused"), new(), _ => Task.CompletedTask, () => Task.CompletedTask, default);
        Assert.Equal(2, store.Jobs.Count); Assert.Equal(2, store.Writes);
        Assert.Equal(3, store.Jobs[first.Id].MinimumExperienceYears); Assert.Equal(5, store.Jobs[second.Id].MinimumExperienceYears);
        Assert.Equal(first.ApplicationUrl, store.Jobs[second.Id].ApplicationUrl);
        Assert.Equal("first", store.Jobs[first.Id].ExternalJobId); Assert.Equal("second", store.Jobs[second.Id].ExternalJobId);
        Assert.Equal(JobStatus.Published, store.Jobs[first.Id].Status); Assert.Equal(JobStatus.Draft, store.Jobs[second.Id].Status);
    }
    [Fact]
    public async Task UnavailableAuditPreventsDatabaseWrites()
    {
        var store = new Store(new Job { Description = "Minimum 3 years experience" });
        await Assert.ThrowsAsync<IOException>(() => ClassificationBackfill.RunAsync(store, new(true, 1, 1, "unused"), new(),
            _ => throw new IOException("Audit unavailable"), () => Task.CompletedTask, default));
        Assert.Equal(0, store.Writes);
    }

    [Fact]
    public async Task AuditRecordsIntentBeforeWriteAndIncludesHistoricalReviewFields()
    {
        var job = new Job { Description = "Minimum 3 years experience", MinimumExperienceYears = 8 };
        var store = new Store(job); var entries = new List<JsonElement>();
        await ClassificationBackfill.RunAsync(store, new(true, 1, 1, "unused"), new(), item =>
        {
            var entry = JsonSerializer.SerializeToElement(item);
            if (entry.GetProperty("Phase").GetString() == "Intent") Assert.Equal(0, store.Writes);
            entries.Add(entry); return Task.CompletedTask;
        }, () => Task.CompletedTask, default);
        Assert.Equal(2, entries.Count);
        Assert.Equal(entries[0].GetProperty("AttemptId").GetGuid(), entries[1].GetProperty("AttemptId").GetGuid());
        Assert.Contains(entries[0].GetProperty("ReviewFields").EnumerateArray(), field => field.GetString() == "Experience");
        Assert.Equal(8, store.Jobs[job.Id].MinimumExperienceYears);
    }
    private sealed class Store(params Job[] jobs) : IClassificationBackfillStore
    {
        public Dictionary<Guid, Job> Jobs { get; } = jobs.ToDictionary(x => x.Id);
        public HashSet<Guid> FailOnce { get; } = [];
        public int Writes { get; private set; }
        public Task<(long Total, Guid? Upper)> InventoryAsync(CancellationToken token) => Task.FromResult(((long)Jobs.Count, (Guid?)Jobs.Keys.Max()));
        private ClassificationRow Copy(Guid id) => new(JsonSerializer.Deserialize<Job>(JsonSerializer.Serialize(Jobs[id]))!, "revision");
        public Task<IReadOnlyList<ClassificationRow>> ReadAsync(Guid? after, Guid upper, int size, CancellationToken token) =>
            Task.FromResult<IReadOnlyList<ClassificationRow>>(Jobs.Keys.Where(id => (!after.HasValue || id.CompareTo(after.Value) > 0) && id.CompareTo(upper) <= 0).Order().Take(size).Select(Copy).ToArray());
        public Task<ClassificationRow?> ReadOneAsync(Guid id, CancellationToken token) => Task.FromResult<ClassificationRow?>(Copy(id));
        public Task<bool> UpdateAsync(ClassificationRow row, CancellationToken token)
        {
            if (FailOnce.Remove(row.Job.Id)) return Task.FromResult(false);
            Writes++; Jobs[row.Job.Id] = row.Job; return Task.FromResult(true);
        }
    }
    [Fact]
    public async Task BoundedDryRunResumesAndApplyIsIdempotentWithFailureIsolation()
    {
        var jobs = Enumerable.Range(1, 3).Select(i => new Job { Id = new Guid(i, 0, 0, new byte[8]), Description = "Minimum 3 years experience", ApplicationUrl = "https://example.test/" + i, Status = JobStatus.Published }).ToArray();
        var store = new Store(jobs); var progress = new ClassificationProgress();
        var options = new ClassificationBackfillOptions(false, 1, 1, "unused");
        await ClassificationBackfill.RunAsync(store, options, progress, _ => Task.CompletedTask, () => Task.CompletedTask, default);
        Assert.Equal(1, progress.Analyzed); Assert.Equal(2, progress.Remaining); Assert.Equal(0, store.Writes); Assert.Null(jobs[0].MinimumExperienceYears);
        await ClassificationBackfill.RunAsync(store, options with { MaxBatches = 2 }, progress, _ => Task.CompletedTask, () => Task.CompletedTask, default);
        Assert.Equal(3, progress.Proposed); Assert.Equal(0, progress.Remaining);
        progress = new(); store.FailOnce.Add(jobs[0].Id);
        await ClassificationBackfill.RunAsync(store, options with { Apply = true, MaxBatches = 3 }, progress, _ => Task.CompletedTask, () => Task.CompletedTask, default);
        Assert.Single(progress.RetryIds); Assert.Equal(2, store.Writes);
        await ClassificationBackfill.RunAsync(store, options with { Apply = true }, progress, _ => Task.CompletedTask, () => Task.CompletedTask, default);
        Assert.Empty(progress.RetryIds); Assert.Equal(3, store.Writes);
        foreach (var job in store.Jobs.Values) { Assert.Equal(3, job.MinimumExperienceYears); Assert.Equal(JobStatus.Published, job.Status); Assert.StartsWith("https://example.test/", job.ApplicationUrl, StringComparison.Ordinal); }
        await ClassificationBackfill.RunAsync(store, options with { Apply = true, MaxBatches = 3 }, new(), _ => Task.CompletedTask, () => Task.CompletedTask, default);
        Assert.Equal(3, store.Writes);
    }
    [Fact]
    public async Task CancellationAfterCheckpointResumesWithoutLosingJobs()
    {
        var store = new Store(new Job { Description = "Minimum 3 years experience" }, new Job { Description = "Minimum 5 years experience" });
        var progress = new ClassificationProgress(); using var cancel = new CancellationTokenSource(); var saves = 0;
        await Assert.ThrowsAsync<OperationCanceledException>(() => ClassificationBackfill.RunAsync(store, new(false, 2, 1, "unused"), progress,
            _ => Task.CompletedTask, () => { if (++saves == 2) cancel.Cancel(); return Task.CompletedTask; }, cancel.Token));
        Assert.Equal(1, progress.Analyzed);
        await ClassificationBackfill.RunAsync(store, new(false, 2, 1, "unused"), progress, _ => Task.CompletedTask, () => Task.CompletedTask, default);
        Assert.Equal(2, progress.Analyzed); Assert.Equal(0, progress.Remaining);
    }
}

public sealed class ClassificationSearchContractTests
{
    [Fact]
    public async Task CombinedFiltersCountBeforePaginationAndKeepCurrencyAndPeriodSeparate()
    {
        await using var db = new JobPortalDbContext(new DbContextOptionsBuilder<JobPortalDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var company = new Company { Name = "Acme", Slug = "acme" }; db.Add(company);
        Job Job(int min, int? max, string currency = "INR", SalaryPeriod period = SalaryPeriod.Yearly) => new()
        { Title = "Engineer", Slug = Guid.NewGuid().ToString(), Company = company, CompanyId = company.Id, Location = "Hyderabad", Status = JobStatus.Published,
            PublishedAtUtc = DateTime.UtcNow.AddDays(-1), MinimumExperienceYears = min, MaximumExperienceYears = max,
            WorkplaceType = WorkplaceType.Remote, EmploymentType = EmploymentType.FullTime, MinimumSalary = 800000, MaximumSalary = 1200000, CurrencyCode = currency, SalaryPeriod = period };
        db.AddRange(Job(3, null), Job(3, 5), Job(6, null), Job(3, null, "USD"), Job(3, null, period: SalaryPeriod.Monthly)); await db.SaveChangesAsync();
        var service = new PublicJobService(new PublicJobRepository(db, TimeProvider.System), new PublicJobQueryValidator());
        var query = new PublicJobQuery(PageSize: 1, MinExperienceYears: 3, MaxExperienceYears: 5, WorkplaceType: WorkplaceType.Remote,
            EmploymentType: EmploymentType.FullTime, Location: "Hyderabad", MinAmount: 900000, CurrencyCode: "INR", SalaryPeriod: SalaryPeriod.Yearly);
        var response = await service.SearchAsync(query); Assert.Equal(2, response.TotalCount); Assert.Single(response.Items);
        Assert.Equal(SalaryPeriod.Yearly, response.Items.Single().SalaryPeriod);
        var minimumOnlyMatches = await service.SearchAsync(query with { MinExperienceYears = 10, MaxExperienceYears = 12 });
        Assert.Equal(2, minimumOnlyMatches.TotalCount); // Both 3+ and 6+ are open intervals.
    }
    [Fact]
    public void SalaryFiltersAcceptLegacyPairOmissionRejectPartialScopeAndTranslateToPostgres()
    {
        var validator = new PublicJobQueryValidator(); Assert.True(validator.Validate(new PublicJobQuery(MinAmount: 1)).IsValid);
        Assert.False(validator.Validate(new PublicJobQuery(MinAmount: 1, CurrencyCode: "USD")).IsValid);
        Assert.False(validator.Validate(new PublicJobQuery(MinAmount: 1, SalaryPeriod: SalaryPeriod.Monthly)).IsValid);
        Assert.True(validator.Validate(new PublicJobQuery(MinAmount: 1, CurrencyCode: "INR", SalaryPeriod: SalaryPeriod.Yearly)).IsValid);
        using var db = new JobPortalDbContext(new DbContextOptionsBuilder<JobPortalDbContext>().UseNpgsql("Host=localhost;Database=translation_only;Username=postgres").Options);
        var sql = new PublicJobRepository(db, TimeProvider.System).FilteredJobsQuery(new(MinExperienceYears: 3, MaxExperienceYears: 5, MinAmount: 1, CurrencyCode: "INR", SalaryPeriod: SalaryPeriod.Yearly)).Select(PublicJobProjections.Summary).ToQueryString();
        Assert.Contains("CurrencyCode", sql, StringComparison.Ordinal); Assert.Contains("SalaryPeriod", sql, StringComparison.Ordinal); Assert.Contains("IS NULL", sql, StringComparison.Ordinal);
    }
}




public sealed class ClassificationAdminContractTests
{
    [Fact]
    public void LegacyAdminEditPreservesPeriodOnlyForUnchangedSalary()
    {
        var job = new Job { MinimumSalary = 800000, CurrencyCode = "INR", SalaryPeriod = SalaryPeriod.Yearly };
        var request = new UpdateJobRequest("Engineer", "Minimum 3 years experience", Guid.NewGuid(), null,
            "https://example.test/job", null, null, null, "Pune", 800000, null, "INR",
            EmploymentType.Unknown, WorkplaceType.Unknown, ExperienceLevel.Mid, null);
        job.Apply(request);
        Assert.Equal(SalaryPeriod.Yearly, job.SalaryPeriod);
        job.Apply(request with { MinimumSalary = 900000 });
        Assert.Null(job.SalaryPeriod);
        job.SalaryPeriod = SalaryPeriod.Yearly;
        job.Apply(request with { MinimumSalary = 900000, ManualOverrideFields = ["Salary"] });
        Assert.Null(job.SalaryPeriod);
    }
    [Fact]
    public void AnalysisIsAdminOnlyAndReturnsSuggestionsWithoutWriting()
    {
        var authorization = Assert.Single(typeof(JobPortal.API.Controllers.JobsController).GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), true));
        Assert.Equal("Administrator", ((Microsoft.AspNetCore.Authorization.AuthorizeAttribute)authorization).Roles);
        var controller = new JobPortal.API.Controllers.JobsController(null!, null!);
        var response = controller.AnalyzeDescription(new("Engineer", "Minimum 3 years experience"), new JobClassificationService());
        var ok = Assert.IsType<Microsoft.AspNetCore.Mvc.OkObjectResult>(response.Result);
        var body = Assert.IsType<JobPortal.Shared.Models.ApiResponse<JobClassificationResult>>(ok.Value);
        Assert.Equal(3, body.Data.MinimumExperienceYears); Assert.Null(body.Data.MaximumExperienceYears);
        Assert.NotEmpty(body.Data.Evidence);
    }
    [Fact]
    public async Task AdminCreateAndComposeClassifyUnspecifiedFieldsAndPreserveSelections()
    {
        using var f = new JobSourceFixture(); var repository = new JobRepository(f.Context);
        var service = new JobService(repository, new UnitOfWork(f.Context), f.Audit, new CreateJobRequestValidator(), new UpdateJobRequestValidator(),
            new JobSearchQueryValidator.UpdateRecruiterContactRequestValidator(), new JobSearchQueryValidator(), TimeProvider.System,
            new CompanyManagementRepository(f.Context), new CategoryManagementRepository(f.Context), f.Repository, f.Locks);
        var result = await service.CreateAsync(new("Engineer", "Minimum 3 years experience. Fully remote", f.Company.Id, null, "https://example.test/jobs/manual",
            null, null, null, "Pune", null, null, "USD", EmploymentType.Unknown, WorkplaceType.Unknown, ExperienceLevel.Mid, null));
        Assert.Equal(3, result.MinimumExperienceYears); Assert.Null(result.MaximumExperienceYears); Assert.Equal(WorkplaceType.Remote, result.WorkplaceType); Assert.Null(result.CurrencyCode);
        var composed = await service.ComposeAsync(Guid.NewGuid(), new(new ComposeJobDraftRequest("Other Engineer", Description: "Minimum 5 years experience. Work from office", ApplicationUrl: "https://example.test/jobs/composed", Location: "Pune", WorkplaceType: WorkplaceType.Hybrid),
            Company: new ComposeRelationRequest<CreateInlineCompanyRequest>(f.Company.Id)));
        var stored = await f.Context.Jobs.SingleAsync(x => x.Id == composed.Id);
        Assert.Equal(5, stored.MinimumExperienceYears); Assert.Equal(WorkplaceType.Hybrid, stored.WorkplaceType); Assert.Null(stored.CurrencyCode);
    }
    [Theory]
    [InlineData("Preferred Qualifications\n5 years experience\nRequired Qualifications\nMinimum 3 years experience", 3)]
    [InlineData("2.5 years experience", null)]
    [InlineData("100 years experience", null)]
    [InlineData("More than 60 years experience", null)]
    public void PreferredSectionsAndUnsupportedFractionsAreNotFabricated(string text, int? expected) =>
        Assert.Equal(expected, new JobClassificationService().Analyze(new("Engineer", text)).MinimumExperienceYears);
}

public sealed class ClassificationPostgresTests(WorkdayCheckpointDatabase database) : IClassFixture<WorkdayCheckpointDatabase>
{
    [ClassificationLocalPostgresFact]
    public async Task BackfillReadsNativeJsonUpdatesOnlyClassificationAndRejectsConcurrentAdminEdits()
    {
        await using var db = database.Open();
        var owner = new User { Email = "classification-fixture@example.test", NormalizedEmail = "CLASSIFICATION-FIXTURE@EXAMPLE.TEST",
            FirstName = "Fixture", LastName = "Owner", RoleId = await db.Roles.Select(role => role.Id).FirstAsync() };
        db.Add(owner);
        var company = new Company { Name = "Classification Fixture", NormalizedName = "CLASSIFICATION FIXTURE", Slug = "classification-fixture", OwnerUser = owner, OwnerUserId = owner.Id };
        var job = new Job { Title = "Engineer", ReferenceNumber = "CLASS-1", Slug = "class-1", Company = company, Description = "Minimum 3 years experience", Location = "Pune",
            SourceMetadataJson = "{\"location\":\"Pune\"}", ApplicationUrl = "https://example.test/verified", Status = JobStatus.Published, PublishedAtUtc = DateTime.UtcNow };
        db.Add(job); await db.SaveChangesAsync();
        await using var connection = new Npgsql.NpgsqlConnection(database.Connection); await connection.OpenAsync();
        var store = new PostgresClassificationBackfillStore(connection, DateTime.UtcNow);
        var row = (await store.ReadOneAsync(job.Id, default))!;
        // PostgreSQL stores microseconds; compare persisted values rather than pre-save .NET ticks.
        var createdAtUtc = row.Job.CreatedAtUtc.ToUniversalTime();
        var updatedAtUtc = row.Job.UpdatedAtUtc?.ToUniversalTime();
        Assert.NotNull(row.Job.SourceMetadataJson);
        JobClassificationPolicy.Apply(row.Job, JobClassificationPolicy.Analyze(row.Job), historical: true);
        Assert.True(await store.UpdateAsync(row, default));
        db.ChangeTracker.Clear(); var stored = await db.Jobs.SingleAsync(x => x.Id == job.Id);
        Assert.Equal(3, stored.MinimumExperienceYears); Assert.Equal("https://example.test/verified", stored.ApplicationUrl); Assert.Equal(JobStatus.Published, stored.Status);
        Assert.Equal("Pune", stored.Location); Assert.Equal(createdAtUtc, stored.CreatedAtUtc); Assert.Equal(updatedAtUtc, stored.UpdatedAtUtc);
        row = (await store.ReadOneAsync(job.Id, default))!; row.Job.MinimumExperienceYears = 9;
        stored.MinimumExperienceYears = 2; await db.SaveChangesAsync();
        Assert.False(await store.UpdateAsync(row, default));
    }
    private sealed class ClassificationLocalPostgresFactAttribute : FactAttribute
    {
        public ClassificationLocalPostgresFactAttribute()
        { if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WORKDAY_CHECKPOINT_TEST_POSTGRES"))) Skip = "Requires explicit disposable localhost PostgreSQL fixture; never uses production configuration."; }
    }
}
