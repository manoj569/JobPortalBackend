using JobPortal.Application.Abstractions.Jobs;
using JobPortal.Application.Abstractions.Persistence;
using JobPortal.Application.Features.JobAggregation;
using JobPortal.Application.Features.Jobs;
using JobPortal.Application.Services;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Shared.Models;
using Microsoft.Extensions.Options;
using Xunit;

namespace JobPortal.Application.Tests;

public class JobAutoPublishServiceTests
{
    private static readonly DateTime Now =
        new(2026, 9, 30, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task TryPublishAsync_Disabled_DoesNotLoadOrPublish()
    {
        var repository = new JobRepositoryFake
        {
            Job = CreateValidJob()
        };

        var jobService = new JobServiceFake();

        var service = CreateService(
            repository,
            jobService,
            enabled: false);

        var result = await service.TryPublishAsync(
            repository.Job!.Id);

        Assert.Equal(
            JobAutoPublishOutcome.Disabled,
            result.Outcome);

        Assert.Equal(0, repository.GetByIdCalls);
        Assert.Equal(0, jobService.PublishCalls);
    }

    [Fact]
    public async Task TryPublishAsync_EligibleJob_PublishesExactlyOnce()
    {
        var job = CreateValidJob();

        var repository = new JobRepositoryFake
        {
            Job = job
        };

        var jobService = new JobServiceFake();

        var service = CreateService(
            repository,
            jobService,
            enabled: true);

        var result = await service.TryPublishAsync(job.Id);

        Assert.Equal(
            JobAutoPublishOutcome.Published,
            result.Outcome);

        Assert.True(result.Published);
        Assert.Equal(1, repository.GetByIdCalls);
        Assert.Equal(1, jobService.PublishCalls);
        Assert.Equal(job.Id, jobService.LastPublishedJobId);
    }

    [Fact]
    public async Task TryPublishAsync_MissingLocation_NeedsReviewAndDoesNotPublish()
    {
        var job = CreateValidJob();
        job.Location = string.Empty;

        var repository = new JobRepositoryFake
        {
            Job = job
        };

        var jobService = new JobServiceFake();

        var service = CreateService(
            repository,
            jobService,
            enabled: true);

        var result = await service.TryPublishAsync(job.Id);

        Assert.Equal(
            JobAutoPublishOutcome.NeedsReview,
            result.Outcome);

        Assert.Contains(
            JobQualityReasonCode.MissingLocation,
            result.Reasons);

        Assert.Equal(0, jobService.PublishCalls);
    }

    [Fact]
    public async Task TryPublishAsync_MissingExpiry_NeedsReviewAndDoesNotPublish()
    {
        var job = CreateValidJob();
        job.ExpiresAtUtc = null;

        var repository = new JobRepositoryFake
        {
            Job = job
        };

        var jobService = new JobServiceFake();

        var service = CreateService(
            repository,
            jobService,
            enabled: true);

        var result = await service.TryPublishAsync(job.Id);

        Assert.Equal(
            JobAutoPublishOutcome.NeedsReview,
            result.Outcome);

        Assert.Contains(
            JobQualityReasonCode.MissingExpiry,
            result.Reasons);

        Assert.Equal(0, jobService.PublishCalls);
    }

    [Fact]
    public async Task TryPublishAsync_InvalidUrl_RejectsAndDoesNotPublish()
    {
        var job = CreateValidJob();
        job.ApplicationUrl = "not-a-valid-url";

        var repository = new JobRepositoryFake
        {
            Job = job
        };

        var jobService = new JobServiceFake();

        var service = CreateService(
            repository,
            jobService,
            enabled: true);

        var result = await service.TryPublishAsync(job.Id);

        Assert.Equal(
            JobAutoPublishOutcome.Rejected,
            result.Outcome);

        Assert.Contains(
            JobQualityReasonCode.InvalidApplicationUrl,
            result.Reasons);

        Assert.Equal(0, jobService.PublishCalls);
    }

    [Fact]
    public async Task TryPublishAsync_ExpiredJob_RejectsAndDoesNotPublish()
    {
        var job = CreateValidJob();
        job.ExpiresAtUtc = Now.AddMinutes(-1);

        var repository = new JobRepositoryFake
        {
            Job = job
        };

        var jobService = new JobServiceFake();

        var service = CreateService(
            repository,
            jobService,
            enabled: true);

        var result = await service.TryPublishAsync(job.Id);

        Assert.Equal(
            JobAutoPublishOutcome.Rejected,
            result.Outcome);

        Assert.Contains(
            JobQualityReasonCode.Expired,
            result.Reasons);

        Assert.Equal(0, jobService.PublishCalls);
    }

    [Fact]
    public async Task TryPublishAsync_JobNotFound_DoesNotPublish()
    {
        var repository = new JobRepositoryFake();

        var jobService = new JobServiceFake();

        var service = CreateService(
            repository,
            jobService,
            enabled: true);

        var result = await service.TryPublishAsync(
            Guid.NewGuid());

        Assert.Equal(
            JobAutoPublishOutcome.JobNotFound,
            result.Outcome);

        Assert.Equal(1, repository.GetByIdCalls);
        Assert.Equal(0, jobService.PublishCalls);
    }

    private static JobAutoPublishService CreateService(
        JobRepositoryFake repository,
        JobServiceFake jobService,
        bool enabled)
    {
        var options = Options.Create(
            new JobAggregationOptions
            {
                AutoPublishEnabled = enabled
            });

        return new JobAutoPublishService(
            repository,
            new JobQualityGate(),
            jobService,
            options,
            new FixedTimeProvider(Now));
    }

    private static Job CreateValidJob()
    {
        return new Job
        {
            Id = Guid.NewGuid(),
            Title = "Senior .NET Developer",
            Description =
                "Build and maintain production web applications.",
            ApplicationUrl =
                "https://example.com/jobs/dotnet-developer",
            Location = "Pune, Maharashtra",
            CompanyId = Guid.NewGuid(),
            CategoryId = Guid.NewGuid(),
            EmploymentType = EmploymentType.FullTime,
            WorkplaceType = WorkplaceType.Hybrid,
            ExperienceLevel = ExperienceLevel.Mid,
            MinimumExperienceYears = 2,
            MaximumExperienceYears = 4,
            ExpiresAtUtc = Now.AddDays(30),
            Status = JobStatus.Draft
        };
    }

    private sealed class FixedTimeProvider(
        DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() =>
            new(utcNow);
    }

    private sealed class JobRepositoryFake : IJobRepository
    {
        public Job? Job { get; init; }

        public int GetByIdCalls { get; private set; }

        public Task<Job?> GetByIdAsync(
            Guid id,
            bool includeDeleted = false,
            CancellationToken cancellationToken = default)
        {
            GetByIdCalls++;

            return Task.FromResult(
                Job?.Id == id ? Job : null);
        }

        public Task<(
            IReadOnlyCollection<Job> Items,
            int TotalCount)> SearchAsync(
            JobSearchQuery query,
            CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<bool> CompanyExistsAsync(
            Guid companyId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task<bool> CategoryExistsAsync(
            Guid categoryId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task<int> ExpireOverduePublishedAsync(
            DateTime utcNow,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(0);

        public Task AddAsync(
            Job job,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public void Update(Job job)
        {
        }

        public void Remove(Job job)
        {
        }

        public Task DeletePermanentlyAsync(
            Guid id,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<Job?> FindByExternalUrlAsync(
            string externalUrl,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<Job?>(null);

        public Task<Job?> FindByFingerprintHashAsync(
            string fingerprintHash,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<Job?>(null);

        public Task<IReadOnlyList<Job>>
            FindCandidatesForFuzzyMatchAsync(
                Guid companyId,
                string title,
                string location,
                int maxResults,
                CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Job>>(
                Array.Empty<Job>());
    }

    private sealed class JobServiceFake : IJobService
    {
        public int PublishCalls { get; private set; }

        public Guid? LastPublishedJobId { get; private set; }

        public Task<JobResponse> PublishAsync(
            Guid id,
            CancellationToken cancellationToken = default)
        {
            PublishCalls++;
            LastPublishedJobId = id;

            return Task.FromResult<JobResponse>(null!);
        }

        public Task<ComposeJobResponse> ComposeAsync(
            Guid administratorUserId,
            ComposeJobRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<JobResponse> CreateAsync(
            CreateJobRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<JobResponse> UpdateAsync(
            Guid id,
            UpdateJobRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task SoftDeleteAsync(
            Guid id,
            CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task DeletePermanentlyAsync(
            Guid id,
            CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<JobResponse> GetByIdAsync(
            Guid id,
            CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<JobResponse> UnpublishAsync(
            Guid id,
            CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<JobResponse> CloseAsync(
            Guid id,
            CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<JobResponse> ArchiveAsync(
            Guid id,
            CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<JobResponse> SetFeaturedAsync(
            Guid id,
            bool isFeatured,
            CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<JobResponse> SetHiddenAsync(
            Guid id,
            bool isHidden,
            CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<PagedResponse<JobResponse>> SearchAsync(
            JobSearchQuery query,
            CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<AdminRecruiterContactResponse>
            GetRecruiterContactAsync(
                Guid jobId,
                CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<AdminRecruiterContactResponse>
            UpdateRecruiterContactAsync(
                Guid jobId,
                UpdateRecruiterContactRequest request,
                CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
    }
}
