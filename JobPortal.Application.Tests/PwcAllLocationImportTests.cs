using JobPortal.Application.Abstractions.Jobs;
using JobPortal.Application.Features.JobAggregation;
using JobPortal.Application.Services;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Infrastructure.Services;
using JobPortal.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class PwcAllLocationImportTests
{
    [Theory]
    [InlineData(AtsType.Workday, "pwc/Global_Experienced_Careers", "https://pwc.wd3.myworkdayjobs.com/Global_Experienced_Careers", true)]
    [InlineData(AtsType.Workday, " PWC/Global_Experienced_Careers ", "https://pwc.wd3.myworkdayjobs.com/en-US/Global_Experienced_Careers/", true)]
    [InlineData(AtsType.Greenhouse, "pwc/Global_Experienced_Careers", "https://pwc.wd3.myworkdayjobs.com/Global_Experienced_Careers", false)]
    [InlineData(AtsType.Workday, "pwc/Other", "https://pwc.wd3.myworkdayjobs.com/Other", false)]
    [InlineData(AtsType.Workday, "pwc/Global_Experienced_Careers", "https://other.wd3.myworkdayjobs.com/Global_Experienced_Careers", false)]
    [InlineData(AtsType.Workday, "pwc/Global_Experienced_Careers", "https://pwc.wd3.myworkdayjobs.com.evil.test/Global_Experienced_Careers", false)]
    [InlineData(AtsType.Workday, "pwc/Global_Experienced_Careers", "http://pwc.wd3.myworkdayjobs.com/Global_Experienced_Careers", false)]
    [InlineData(AtsType.Workday, "pwc/Global_Experienced_Careers", "https://pwc.wd3.myworkdayjobs.com/Global_Experienced_Careers?x=1", false)]
    public void OnlyVerifiedPwcBoardUsesAllLocationPolicy(AtsType ats, string identifier, string board, bool expected)
    {
        var source = new JobSource { AtsType = ats, AtsIdentifier = identifier, CareerPageUrl = board,
            Company = new Company { Name = "PwC" } };
        Assert.Equal(expected, PwcWorkdayImportPolicy.AppliesTo(source));
        var job = Vacancy(1) with { CountryCodes = ["US"], Location = "New York" };
        var selected = Policy(new()).Select(source, new([job], 0, true));
        Assert.Equal(expected ? 1 : 0, selected.Jobs.Count);
    }

    [Fact]
    public void AllLocationsKeepProviderFactsAndIncompleteOrLimitedSnapshotsStayIncomplete()
    {
        var source = PwcSource();
        var jobs = new[]
        {
            Vacancy(0) with { CountryCodes = ["IN"], Location = "Pune, India" },
            Vacancy(1) with { CountryCodes = ["US"], Location = "New York, United States" },
            Vacancy(2) with { Location = "Remote", WorkplaceTypeText = "Remote", CountryCodes = [] },
            Vacancy(3) with { Location = "London", CountryCodes = ["GB"], WorkplaceTypeText = "Hybrid", AdditionalLocations = ["Paris"] },
            Vacancy(4) with { Location = null, CountryCodes = [] }
        };
        var selected = Policy(new()).Select(source, new(jobs, 0, true));
        Assert.True(selected.IsComplete);
        Assert.Equal(jobs, selected.Jobs);
        Assert.Null(selected.SelectionReasonCounts);
        Assert.False(Policy(new()).Select(source, new(jobs, 0, false)).IsComplete);
        var settings = new JobAggregationOptions();
        settings.SourceApprovals[source.Id.ToString("D")] = new() { IndiaOnly = true, TestImportLimit = 2 };
        var limited = Policy(settings).Select(source, new(jobs, 0, true));
        Assert.False(limited.IsComplete);
        Assert.Equal(2, limited.Jobs.Count);
        Assert.Equal(3, limited.SelectionReasonCounts!["TestImportLimit"]);
        Assert.Equal(2, Policy(new()).Select(source, new(jobs, 2, false)).Skipped);
    }

    [Fact]
    public void ProviderPreservesInternationalRemoteCountryAndAdditionalLocations()
    {
        var target = WorkdayJobSourceProvider.ValidateSource(PwcSource());
        var listing = new WorkdayJobSourceProvider.ListingEntry("Associate", "/job/London/Associate_REQ1", "London", null, "REQ1");
        var raw = WorkdayJobSourceProvider.ParseDetail("""
            {"jobPostingInfo":{"title":"Associate","jobReqId":"REQ1","jobDescription":"<p>Actual vacancy description.</p>",
            "location":"London, United Kingdom","additionalLocations":["Paris, France"],"country":{"alpha2Code":"GB"},"remoteType":"Hybrid"}}
            """, target, PwcSource(), listing);
        Assert.NotNull(raw);
        Assert.Equal("GB", Assert.Single(raw.CountryCodes));
        Assert.Equal("Paris, France", Assert.Single(raw.AdditionalLocations));
        Assert.Equal("London, United Kingdom", raw.Location);
        Assert.Equal(WorkplaceType.Hybrid, raw.WorkplaceType);
        Assert.Equal("REQ1", raw.ExternalId);
        Assert.Equal("https://pwc.wd3.myworkdayjobs.com/en-US/Global_Experienced_Careers/job/London/Associate_REQ1", raw.ApplicationUrl);
    }

    [Fact]
    public async Task ValidFallbackIsScopedAndCannotBeReplacedByAnUnrelatedSourceDefault()
    {
        using var f = new JobSourceFixture();
        ConfigurePwc(f);
        f.Map(f.Category.Id.ToString("D"));
        var general = new Category { Name = "General", Slug = "general" };
        f.Context.Categories.Add(general);
        await f.Context.SaveChangesAsync();
        Assert.Equal(general.Id, await f.Resolver.ResolveCategoryIdAsync(f.Source, Vacancy(1)));
        var classifierResolver = new JobSourceCategoryResolver(new Monitor(), new CategoryManagementRepository(f.Context), new ExternalJobCategoryClassifier());
        Assert.Equal(general.Id, await classifierResolver.ResolveCategoryIdAsync(f.Source, Vacancy(1) with { Title = "Software Engineer" }));
        var other = new Category { Name = "Other", Slug = "other" };
        f.Context.Categories.Add(other);
        await f.Context.SaveChangesAsync();
        f.Resolver.BeginRun();
        Assert.Equal(other.Id, await f.Resolver.ResolveCategoryIdAsync(f.Source, Vacancy(1)));
        f.Resolver.EndRun();
        f.Source.AtsIdentifier = "other/Global_Experienced_Careers";
        Assert.Equal(f.Category.Id, await f.Resolver.ResolveCategoryIdAsync(f.Source, Vacancy(1)));
    }

    [Fact]
    public async Task MissingValidFallbackStillFailsClosed()
    {
        using var f = new JobSourceFixture();
        ConfigurePwc(f);
        var resolver = new JobSourceCategoryResolver(new Monitor(), new CategoryManagementRepository(f.Context), new ExternalJobCategoryClassifier());
        Assert.Null(await resolver.ResolveCategoryIdAsync(f.Source, Vacancy(1)));
    }

    [Fact]
    public async Task InternationalUnknownAndRemoteJobsPersistOnceAndIncompleteScanNeverClosesJobs()
    {
        using var f = new JobSourceFixture();
        ConfigurePwc(f);
        var general = new Category { Name = "General", Slug = "general" };
        var software = new Category { Name = "Software Engineering", Slug = "software-engineering" };
        f.Context.AddRange(general, software);
        await f.Context.SaveChangesAsync();
        var provider = new SnapshotProvider([
            Vacancy(0) with { Title = "Software Engineer", CountryCodes = ["IN"], Location = "Pune, India" },
            Vacancy(1) with { Location = "New York, United States", CountryCodes = ["US"] },
            Vacancy(2) with { Location = "Remote", WorkplaceTypeText = "Remote", CountryCodes = [] },
            Vacancy(3) with { Location = "London, United Kingdom", CountryCodes = ["GB"], WorkplaceTypeText = "Hybrid" },
            Vacancy(4) with { Location = null, CountryCodes = [] },
            Vacancy(5) with { Title = "Tax and Cybersecurity Advisor" }
        ]);
        var runner = Runner(f, provider);
        AssertCounts(await runner.RunAsync(f.Source.Id), 6, 0, 0, 0, 0);
        var saved = await f.Context.Jobs.AsNoTracking().OrderBy(x => x.ExternalJobId).ToArrayAsync();
        Assert.Equal(software.Id, saved[0].CategoryId);
        Assert.All(saved.Skip(1), x => Assert.Equal(general.Id, x.CategoryId));
        Assert.Equal(provider.Jobs.Select(x => x.Location), saved.Select(x => x.Location));
        Assert.Equal(WorkplaceType.Remote, saved[2].WorkplaceType);
        Assert.Equal(WorkplaceType.Hybrid, saved[3].WorkplaceType);
        Assert.All(saved, x => { Assert.Equal(JobStatus.Draft, x.Status); Assert.Null(x.PublishedAtUtc); });
        AssertCounts(await runner.RunAsync(f.Source.Id), 0, 6, 0, 0, 0);
        Assert.Equal(saved.Select(x => x.Id), await f.Context.Jobs.AsNoTracking().OrderBy(x => x.ExternalJobId).Select(x => x.Id).ToArrayAsync());
        provider.Jobs = [provider.Jobs[0], Vacancy(6)];
        provider.Complete = false;
        var partial = await runner.RunAsync(f.Source.Id);
        AssertCounts(partial, 1, 1, 0, 0, 0);
        Assert.False(partial.Succeeded);
        Assert.Equal(0, partial.Closed);
        Assert.Equal(7, await f.Context.Jobs.CountAsync());
        provider.Jobs = [Vacancy(7) with { Title = " " }, Vacancy(8) with { ApplicationUrl = "ftp://example.test/job" }];
        provider.Complete = true;
        provider.Skipped = 1;
        var invalid = await runner.RunAsync(f.Source.Id);
        AssertCounts(invalid, 0, 0, 2, 0, 3);
        Assert.Equal(1, invalid.ProviderSkipped);
        Assert.Equal(1, invalid.ValidationReasonCounts!["MissingTitle"]);
        Assert.Equal(1, invalid.ValidationReasonCounts["InvalidApplicationUrl"]);
        Assert.Equal(0, invalid.Closed);
    }

    [Fact]
    public async Task Complete3879VacancySnapshotWithFourExistingCreatesExactly3875()
    {
        using var f = new JobSourceFixture();
        ConfigurePwc(f);
        f.Context.Categories.Add(new Category { Name = "General", Slug = "general" });
        await f.Context.SaveChangesAsync();
        var provider = new SnapshotProvider(Enumerable.Range(0, 4).Select(Vacancy).ToArray()) { Complete = false };
        var runner = Runner(f, provider);
        AssertCounts(await runner.RunAsync(f.Source.Id), 4, 0, 0, 0, 0);
        provider.Jobs = Enumerable.Range(0, 3879).Select(Vacancy).ToArray();
        provider.Complete = true;
        var full = await runner.RunAsync(f.Source.Id);
        Assert.True(full.Succeeded);
        AssertCounts(full, 3875, 4, 0, 0, 0);
        Assert.Equal(3879, await f.Context.Jobs.CountAsync());
        AssertCounts(await runner.RunAsync(f.Source.Id), 0, 3879, 0, 0, 0);
        Assert.Equal(3879, await f.Context.Jobs.Select(x => x.ExternalJobId).Distinct().CountAsync());
    }

    private static void AssertCounts(JobSourceRunResult result, int created, int existing, int rejected, int failed, int skipped)
    {
        Assert.Equal(created, result.Created);
        Assert.Equal(existing, result.Existing);
        Assert.Equal(rejected, result.IngestionRejected);
        Assert.Equal(failed, result.Failed);
        Assert.Equal(skipped, result.Skipped);
        Assert.Equal(0, result.SelectionSkipped);
    }

    private static RawExternalJob Vacancy(int index) => new()
    {
        ExternalId = $"REQ{index:D5}", Title = $"Associate vacancy {index}", CompanyName = "PwC",
        Description = "Actual vacancy description.", Location = "London, United Kingdom", CountryCodes = ["GB"],
        ApplicationUrl = $"https://pwc.wd3.myworkdayjobs.com/en-US/Global_Experienced_Careers/job/London/Associate_REQ{index:D5}"
    };
    private static JobSource PwcSource() => new() { AtsType = AtsType.Workday, Company = new Company { Name = "PwC" },
        AtsIdentifier = "pwc/Global_Experienced_Careers", CareerPageUrl = "https://pwc.wd3.myworkdayjobs.com/Global_Experienced_Careers" };
    private static void ConfigurePwc(JobSourceFixture f)
    {
        var source = PwcSource();
        f.Company.Name = "PwC";
        f.Source.AtsType = source.AtsType;
        f.Source.AtsIdentifier = source.AtsIdentifier;
        f.Source.CareerPageUrl = source.CareerPageUrl;
    }
    private static JobSourcePublicationPolicy Policy(JobAggregationOptions settings) => new(Options.Create(settings), TimeProvider.System);
    private static JobSourceRunner Runner(JobSourceFixture f, SnapshotProvider provider)
    {
        var jobs = new JobRepository(f.Context);
        var unit = new UnitOfWork(f.Context);
        var fingerprints = new JobFingerprintService();
        var canonical = new UrlCanonicalizer();
        var ingestion = new JobIngestionService(jobs, new CompanyManagementRepository(f.Context), new CategoryManagementRepository(f.Context),
            new JobDeduplicationService(jobs, fingerprints, canonical), fingerprints, unit, TimeProvider.System, f.Locks, canonical);
        var resolver = new JobSourceCategoryResolver(new Monitor(), new CategoryManagementRepository(f.Context), new ExternalJobCategoryClassifier());
        return new(f.Repository, [provider], ingestion, unit, TimeProvider.System, resolver,
            new ExternalJobNormalizer(), jobRepository: jobs, publicationPolicy: Policy(new()));
    }
    private sealed class Monitor : IOptionsMonitor<JobAggregationOptions>
    {
        public JobAggregationOptions CurrentValue { get; } = new();
        public JobAggregationOptions Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<JobAggregationOptions, string?> listener) => null;
    }
    private sealed class SnapshotProvider(RawExternalJob[] jobs) : IBatchedExternalJobProvider
    {
        public RawExternalJob[] Jobs { get; set; } = jobs;
        public bool Complete { get; set; } = true;
        public int Skipped { get; set; }
        public AtsType AtsType => AtsType.Workday;
        public Task<IReadOnlyCollection<RawExternalJob>> FetchJobsAsync(JobSource source, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyCollection<RawExternalJob>>(Jobs);
        public Task<ExternalJobSourceSnapshot> FetchSnapshotAsync(JobSource source, CancellationToken cancellationToken = default) => Task.FromResult(new ExternalJobSourceSnapshot(Jobs, Skipped, Complete));
        public async IAsyncEnumerable<ExternalJobSourceSnapshot> FetchBatchesAsync(JobSource source,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (Jobs.Length == 0) yield return new([], Skipped, Complete);
            for (var offset = 0; offset < Jobs.Length; offset += 100)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Yield();
                var final = offset + 100 >= Jobs.Length;
                yield return new(Jobs.Skip(offset).Take(100).ToArray(), final ? Skipped : 0, final && Complete);
            }
        }
    }
}
