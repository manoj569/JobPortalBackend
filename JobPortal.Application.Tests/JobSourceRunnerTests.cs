using JobPortal.Application.Abstractions.Jobs;
using JobPortal.Application.Abstractions.Persistence;
using JobPortal.Application.Services;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using Xunit;

namespace JobPortal.Application.Tests;

public class JobSourceRunnerTests
{
    private readonly TestJobSourceRepository _sources = new();
    private readonly TestExternalJobProvider _provider = new();
    private readonly TestIngestionService _ingestion = new();
    private readonly TestUnitOfWork _unitOfWork = new();
    private readonly JobSourceRunner _runner;

    private readonly JobSource _source = new()
    {
        Id = Guid.NewGuid(),
        CompanyId = Guid.NewGuid(),
        Company = new Company
        {
            Id = Guid.NewGuid(),
            Name = "Acme Corp"
        },
        CareerPageUrl = "https://example.com/careers",
        AtsType = AtsType.Greenhouse,
        AtsIdentifier = "acme",
        IsActive = true,
        ScanIntervalMinutes = 720
    };

    public JobSourceRunnerTests()
    {
        _sources.Source = _source;
        _provider.ProviderAtsType = AtsType.Greenhouse;

        _runner = new JobSourceRunner(
            _sources,
            [_provider],
            _ingestion,
            _unitOfWork,
            TimeProvider.System,
            new UnmappedCategoryResolver(), new ExternalJobNormalizer());
    }

    [Fact]
    public async Task RunAsync_SuccessfulRun_UpdatesSourceAndCounters()
    {
        _source.ConsecutiveFailures = 3;
        _source.LastError = "Previous failure";

        _provider.Jobs =
        [
            CreateRawJob("Job 1"),
            CreateRawJob("Job 2"),
            CreateRawJob("Job 3"),
            CreateRawJob("Job 4")
        ];

        _ingestion.Results.Enqueue(new JobIngestionResult
        {
            Outcome = JobIngestionOutcome.Created
        });

        _ingestion.Results.Enqueue(new JobIngestionResult
        {
            Outcome = JobIngestionOutcome.MatchedByUrl
        });

        _ingestion.Results.Enqueue(new JobIngestionResult
        {
            Outcome = JobIngestionOutcome.Invalid
        });

        _ingestion.Results.Enqueue(new JobIngestionResult
        {
            Outcome = JobIngestionOutcome.Failed
        });

        var result = await _runner.RunAsync(_source.Id);

        Assert.True(result.Succeeded);
        Assert.Equal(4, result.TotalReceived);
        Assert.Equal(1, result.Created);
        Assert.Equal(1, result.Matched);
        Assert.Equal(1, result.Skipped);
        Assert.Equal(1, result.Failed);

        Assert.NotNull(_source.LastRunAtUtc);
        Assert.NotNull(_source.LastSuccessfulRunAtUtc);
        Assert.Null(_source.LastError);
        Assert.Equal(0, _source.ConsecutiveFailures);

        Assert.Equal(1, _sources.UpdateCalls);
        Assert.Equal(1, _unitOfWork.SaveCalls);
    }

    [Fact]
    public async Task RunAsync_AllMatchTypes_CountAsMatched()
    {
        _provider.Jobs =
        [
            CreateRawJob("Job 1"),
            CreateRawJob("Job 2"),
            CreateRawJob("Job 3")
        ];

        _ingestion.Results.Enqueue(new JobIngestionResult
        {
            Outcome = JobIngestionOutcome.MatchedByUrl
        });

        _ingestion.Results.Enqueue(new JobIngestionResult
        {
            Outcome = JobIngestionOutcome.MatchedByFingerprint
        });

        _ingestion.Results.Enqueue(new JobIngestionResult
        {
            Outcome = JobIngestionOutcome.MatchedByFuzzy
        });

        var result = await _runner.RunAsync(_source.Id);

        Assert.True(result.Succeeded);
        Assert.Equal(3, result.Matched);
        Assert.Equal(0, result.Created);
        Assert.Equal(0, result.Skipped);
        Assert.Equal(0, result.Failed);
    }

    [Fact]
    public async Task RunAsync_IndividualRecordFailure_ContinuesRemainingJobs()
    {
        _provider.Jobs =
        [
            CreateRawJob("Job 1"),
            CreateRawJob("Job 2"),
            CreateRawJob("Job 3")
        ];

        _ingestion.ThrowOnCall = 2;

        _ingestion.Results.Enqueue(new JobIngestionResult
        {
            Outcome = JobIngestionOutcome.Created
        });

        _ingestion.Results.Enqueue(new JobIngestionResult
        {
            Outcome = JobIngestionOutcome.Created
        });

        var result = await _runner.RunAsync(_source.Id);

        Assert.True(result.Succeeded);
        Assert.Equal(3, result.TotalReceived);
        Assert.Equal(2, result.Created);
        Assert.Equal(1, result.Failed);

        Assert.Equal(3, _ingestion.CallCount);
        Assert.Equal(1, _unitOfWork.ResetCalls);

        Assert.NotNull(_source.LastSuccessfulRunAtUtc);
    }

    [Fact]
    public async Task RunAsync_ProviderFailure_RecordsFailureState()
    {
        var previousSuccess = DateTime.UtcNow.AddDays(-1);

        _source.LastSuccessfulRunAtUtc = previousSuccess;
        _source.ConsecutiveFailures = 2;

        _provider.Exception =
            new InvalidOperationException("Provider failed\r\nsecret details");

        var result = await _runner.RunAsync(_source.Id);

        Assert.False(result.Succeeded);

        Assert.NotNull(_source.LastRunAtUtc);
        Assert.Equal(
            previousSuccess,
            _source.LastSuccessfulRunAtUtc);

        Assert.Equal(3, _source.ConsecutiveFailures);

        Assert.NotNull(_source.LastError);
        Assert.Equal("External job source run failed.", _source.LastError);
        Assert.DoesNotContain("secret details", _source.LastError);
        Assert.DoesNotContain("\r", _source.LastError);
        Assert.DoesNotContain("\n", _source.LastError);

        Assert.Equal(1, _unitOfWork.ResetCalls);
        Assert.Equal(1, _unitOfWork.SaveCalls);
        Assert.Equal(1, _sources.UpdateCalls);
    }

    [Fact]
    public async Task RunAsync_SourceNotFound_ReturnsFailure()
    {
        _sources.Source = null;

        var result = await _runner.RunAsync(Guid.NewGuid());

        Assert.False(result.Succeeded);
        Assert.Equal(
            "Job source was not found.",
            result.Error);

        Assert.Equal(0, _provider.FetchCalls);
        Assert.Equal(0, _unitOfWork.SaveCalls);
    }

    [Fact]
    public async Task RunAsync_InactiveSource_DoesNotCallProvider()
    {
        _source.IsActive = false;

        var result = await _runner.RunAsync(_source.Id);

        Assert.False(result.Succeeded);
        Assert.Equal(
            "Job source is inactive.",
            result.Error);

        Assert.Equal(0, _provider.FetchCalls);
        Assert.Equal(0, _unitOfWork.SaveCalls);
    }

    [Fact]
    public async Task RunAsync_NoMatchingProvider_ReturnsFailure()
    {
        _source.AtsType = AtsType.Lever;

        var result = await _runner.RunAsync(_source.Id);

        Assert.False(result.Succeeded);
        Assert.Contains(
            "No provider is registered",
            result.Error);

        Assert.Equal(0, _provider.FetchCalls);
        Assert.Equal(1, _unitOfWork.SaveCalls);
        Assert.NotNull(_source.LastRunAtUtc);
        Assert.Null(_source.LastSuccessfulRunAtUtc);
        Assert.Equal(1, _source.ConsecutiveFailures);
        Assert.Equal(result.Error, _source.LastError);
    }

    [Fact]
    public async Task RunAsync_Cancellation_Propagates()
    {
        using var cancellation =
            new CancellationTokenSource();

        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => _runner.RunAsync(
                _source.Id,
                cancellation.Token));

        Assert.Equal(0, _provider.FetchCalls);
        Assert.Equal(0, _unitOfWork.SaveCalls);
    }

    private static RawExternalJob CreateRawJob(string title) => new()
    {
        Title = title,
        CompanyName = "Acme Corp",
        ApplicationUrl = "https://example.com/" + title
    };

    private sealed class UnmappedCategoryResolver : IJobSourceCategoryResolver
    {
        public Task<Guid?> ResolveCategoryIdAsync(JobSource source, RawExternalJob rawJob, CancellationToken cancellationToken = default) =>
            Task.FromResult<Guid?>(null);
        public Task<Guid?> ResolveCategoryIdAsync(JobSource source, CancellationToken cancellationToken = default) =>
            Task.FromResult<Guid?>(null);
    }

    private sealed class TestJobSourceRepository :
        IJobSourceRepository
    {
        public Task<IReadOnlyCollection<JobSource>> GetDueSourcesAsync(
            DateTime nowUtc, int maxResults, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyCollection<JobSource>>([]);

        public JobSource? Source { get; set; }
        public int UpdateCalls { get; private set; }

        public Task<JobSource?> GetByIdAsync(
            Guid id,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(
                Source?.Id == id ? Source : null);

        public void Update(JobSource source)
        {
            UpdateCalls++;
        }
    }

    private sealed class TestExternalJobProvider :
        IExternalJobProvider
    {
        public AtsType ProviderAtsType { get; set; }
        public AtsType AtsType => ProviderAtsType;

        public IReadOnlyCollection<RawExternalJob> Jobs { get; set; } =
            Array.Empty<RawExternalJob>();

        public Exception? Exception { get; set; }

        public int FetchCalls { get; private set; }

        public Task<IReadOnlyCollection<RawExternalJob>> FetchJobsAsync(
            JobSource source,
            CancellationToken cancellationToken = default)
        {
            FetchCalls++;

            if (Exception is not null)
                throw Exception;

            return Task.FromResult(Jobs);
        }
    }

    private sealed class TestIngestionService :
        IJobIngestionService
    {
        public Queue<JobIngestionResult> Results { get; } = new();

        public int CallCount { get; private set; }
        public int? ThrowOnCall { get; set; }

        public Task<JobIngestionResult> IngestAsync(
            RawExternalJob rawJob,
            CancellationToken cancellationToken = default)
        {
            CallCount++;

            if (ThrowOnCall == CallCount)
                throw new InvalidOperationException(
                    "Individual record failed.");

            if (Results.Count == 0)
            {
                return Task.FromResult(new JobIngestionResult
                {
                    Outcome = JobIngestionOutcome.Invalid
                });
            }

            return Task.FromResult(Results.Dequeue());
        }
    }

    private sealed class TestUnitOfWork : IUnitOfWork
    {
        public int SaveCalls { get; private set; }
        public int ResetCalls { get; private set; }

        public Task<int> SaveChangesAsync(
            CancellationToken cancellationToken = default)
        {
            SaveCalls++;
            return Task.FromResult(1);
        }

        public void ResetAfterFailure()
        {
            ResetCalls++;
        }
    }
}
