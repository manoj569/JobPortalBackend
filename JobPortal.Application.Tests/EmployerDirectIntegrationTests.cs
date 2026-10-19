using System.Reflection;
using System.Text.Json;
using JobPortal.Application.Abstractions.Jobs;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Application.Features.JobAggregation;
using JobPortal.Application.Services;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Persistence.Context;
using JobPortal.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class EmployerDirectIntegrationTests
{
    [Fact]
    public async Task TwoCompleteSnapshotsCloseAndReplayDoesNotIncrement()
    {
        using var f = new Fixture();
        var repository = new JobRepository(f.Context);
        var one = Guid.NewGuid();
        Assert.Equal(0, await repository.ReconcileSourceSnapshotAsync(f.Source.Id, [], one, f.Now, 2));
        await f.Context.SaveChangesAsync();
        Assert.Equal(JobStatus.Published, f.Job.Status);
        Assert.Equal(1, (await f.Context.Set<JobSourceVacancy>().SingleAsync()).MissingCompleteSnapshots);
        Assert.Equal(0, await repository.ReconcileSourceSnapshotAsync(f.Source.Id, [], one, f.Now, 2));
        Assert.Equal(1, (await f.Context.Set<JobSourceVacancy>().SingleAsync()).MissingCompleteSnapshots);
        Assert.Equal(1, await repository.ReconcileSourceSnapshotAsync(f.Source.Id, [], Guid.NewGuid(), f.Now.AddHours(12), 2));
        await f.Context.SaveChangesAsync();
        Assert.Equal(JobStatus.Closed, f.Job.Status);
        Assert.NotNull(f.Job.SourceClosedBySnapshotId);
    }

    [Fact]
    public async Task OlderCompleteObservationCannotIncrementMissingCounters()
    {
        using var f = new Fixture();
        var repository = new JobRepository(f.Context);
        await repository.ReconcileSourceSnapshotAsync(f.Source.Id, [], Guid.NewGuid(), f.Now, 2);
        await f.Context.SaveChangesAsync();
        Assert.Equal(0, await repository.ReconcileSourceSnapshotAsync(f.Source.Id, [], Guid.NewGuid(), f.Now.AddHours(-1), 2));
        Assert.Equal(1, (await f.Context.Set<JobSourceVacancy>().SingleAsync()).MissingCompleteSnapshots);
        Assert.Equal(JobStatus.Published, f.Job.Status);
    }

    [Fact]
    public async Task SuspiciousDropCannotCountAsAnotherMissingScan()
    {
        using var f = new Fixture();
        var repository = new JobRepository(f.Context);
        f.Source.LastCompleteSnapshotCount = 10;
        await f.Context.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.ReconcileSourceSnapshotAsync(f.Source.Id, [], Guid.NewGuid(), f.Now, 2));
        Assert.Empty(await f.Context.Set<JobSourceVacancy>().ToArrayAsync());
        Assert.Equal(JobStatus.Published, f.Job.Status);
    }

    [Fact]
    public async Task PartialInventoryPresenceResetsMissingBeforeGeographyFiltering()
    {
        using var f = new Fixture();
        var repository = new JobRepository(f.Context);
        await repository.ReconcileSourceSnapshotAsync(f.Source.Id, [], Guid.NewGuid(), f.Now, 2);
        await f.Context.SaveChangesAsync();
        await repository.ObserveSourceInventoryAsync(f.Source.Id, ["42"], f.Now.AddHours(1));
        var membership = await f.Context.Set<JobSourceVacancy>().SingleAsync();
        Assert.True(membership.IsCurrent);
        Assert.Equal(0, membership.MissingCompleteSnapshots);
        await repository.ObserveSourceInventoryAsync(f.Source.Id, [], f.Now.AddHours(2));
        Assert.Equal(0, membership.MissingCompleteSnapshots);
        Assert.Equal(JobStatus.Published, f.Job.Status);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task SuppressedOrNonOwnedPublicationNeverCloses(bool hidden, bool owns)
    {
        using var f = new Fixture();
        f.Job.IsHidden = hidden; f.Job.SourceOwnsPublication = owns;
        await f.Context.SaveChangesAsync();
        var repository = new JobRepository(f.Context);
        await repository.ReconcileSourceSnapshotAsync(f.Source.Id, [], Guid.NewGuid(), f.Now, 2);
        await f.Context.SaveChangesAsync();
        Assert.Equal(0, await repository.ReconcileSourceSnapshotAsync(f.Source.Id, [], Guid.NewGuid(), f.Now.AddHours(12), 2));
        Assert.Equal(JobStatus.Published, f.Job.Status);
        Assert.Null(f.Job.SourceClosedBySnapshotId);
    }

    [Fact]
    public async Task OtherActiveSourceProtectsTheSharedJob()
    {
        using var f = new Fixture();
        var second = new JobSource { Company = f.Company, CompanyId = f.Company.Id, CareerPageUrl = "https://example.com/other", IsActive = true };
        f.Context.Add(second); await f.Context.SaveChangesAsync();
        var repository = new JobRepository(f.Context);
        await repository.ObserveSourceVacancyAsync(second.Id, "other-42", f.Job.Id, f.Now);
        await repository.ReconcileSourceSnapshotAsync(f.Source.Id, [], Guid.NewGuid(), f.Now, 2);
        await f.Context.SaveChangesAsync();
        Assert.Equal(0, await repository.ReconcileSourceSnapshotAsync(f.Source.Id, [], Guid.NewGuid(), f.Now.AddHours(12), 2));
        Assert.Equal(JobStatus.Published, f.Job.Status);
    }

    [Fact]
    public async Task ReappearanceUsesRealIngestionAndTheSameJobIdentity()
    {
        using var f = new Fixture();
        var repository = new JobRepository(f.Context);
        await repository.ReconcileSourceSnapshotAsync(f.Source.Id, [], Guid.NewGuid(), f.Now, 2);
        await f.Context.SaveChangesAsync();
        await repository.ReconcileSourceSnapshotAsync(f.Source.Id, [], Guid.NewGuid(), f.Now.AddHours(12), 2);
        await f.Context.SaveChangesAsync();
        var id = f.Job.Id;
        var canonical = new UrlCanonicalizer(); var fingerprints = new JobFingerprintService();
        var ingestion = new JobIngestionService(repository, new CompanyManagementRepository(f.Context), new CategoryManagementRepository(f.Context),
            new JobDeduplicationService(repository, fingerprints, canonical), fingerprints, new UnitOfWork(f.Context), TimeProvider.System,
            new TestAggregationLocks(), canonical);
        var result = await ingestion.IngestAsync(new RawExternalJob { JobSourceId = f.Source.Id, ExternalId = "42", CompanyId = f.Company.Id,
            CompanyName = f.Company.Name, Title = "Updated Engineer", Location = "Pune, India", ApplicationUrl = "https://example.com/jobs/42" });
        Assert.Equal(id, result.JobId);
        var saved = await f.Context.Jobs.SingleAsync();
        Assert.Equal(JobStatus.Draft, saved.Status); // Existing AutoPublish service is the only publisher.
        Assert.Null(saved.SourceClosedBySnapshotId);
        await repository.ObserveSourceVacancyAsync(f.Source.Id, "42", id, f.Now.AddDays(1));
        Assert.Equal(0, (await f.Context.Set<JobSourceVacancy>().SingleAsync()).MissingCompleteSnapshots);
        Assert.Single(await f.Context.Jobs.ToArrayAsync());
    }

    [Fact]
    public async Task ManualClosedJobCannotBeReopenedAsSourceClosure()
    {
        using var f = new Fixture();
        f.Job.Status = JobStatus.Closed; f.Job.SourceClosedBySnapshotId = null;
        await f.Context.SaveChangesAsync();
        var repository = new JobRepository(f.Context);
        await repository.ObserveSourceVacancyAsync(f.Source.Id, "42", f.Job.Id, f.Now);
        Assert.Equal(JobStatus.Closed, f.Job.Status);
    }

    [Fact]
    public async Task GenericSubmissionQueuesExistingRunAndRejectsPayloadConflict()
    {
        using var f = new Fixture();
        f.Source.AtsType = AtsType.Custom; f.Source.AtsIdentifier = "employer-direct";
        await f.Context.SaveChangesAsync();
        JobSourceRun? retained = null;
        var store = Stub.Create<IJobSourceRunStore>((name, args) => name switch
        {
            "GetAsync" => (object)Task.FromResult<JobSourceRun?>(retained),
            "EnqueueSnapshotAsync" => Task.FromResult<JobSourceRun>(retained = new JobSourceRun { Id = (Guid)args[1]!, JobSourceId = f.Source.Id,
                CheckpointJson = (string)args[4]!, QueuedAtUtc = DateTime.UtcNow }),
            _ => throw new InvalidOperationException(name)
        });
        var service = new EmployerDirectSnapshotService(new JobSourceRepository(f.Context), store, TimeProvider.System);
        var body = new EmployerDirectSnapshotRequest(f.Company.Id, f.Source.CareerPageUrl, DateTimeOffset.UtcNow, false, "INCOMPLETE", []);
        var snapshotId = Guid.NewGuid();
        var first = await service.SubmitAsync(f.Source.Id, snapshotId, body, null, CancellationToken.None);
        var replay = await service.SubmitAsync(f.Source.Id, snapshotId, body, null, CancellationToken.None);
        Assert.Equal(first.RunId, replay.RunId);
        Assert.Equal(snapshotId, first.RunId);
        await Assert.ThrowsAsync<ConflictException>(() => service.SubmitAsync(f.Source.Id, snapshotId,
            body with { CompletionReason = "changed" }, null, CancellationToken.None));
        await Assert.ThrowsAsync<BadRequestException>(() => service.SubmitAsync(f.Source.Id, Guid.NewGuid(),
            body with { IsComplete = true }, null, CancellationToken.None));
    }

    [Fact]
    public async Task GenericProviderReusesSourceRunPayloadAndCannotClaimCompleteness()
    {
        using var f = new Fixture();
        f.Source.AtsType = AtsType.Custom; f.Source.AtsIdentifier = "employer-direct";
        var raw = new RawExternalJob { ExternalId = "new", Title = "Senior Engineer", ApplicationUrl = "https://example.com/jobs/new",
            Location = "India", SourcePostedAtUtc = DateTime.UtcNow.AddDays(-120) };
        var input = new EmployerDirectSnapshotRequest(f.Company.Id, f.Source.CareerPageUrl, DateTimeOffset.UtcNow, false, "UNPROVEN", [raw]);
        var run = new JobSourceRun { JobSourceId = f.Source.Id,
            CheckpointJson = JsonSerializer.Serialize(new EmployerDirectSnapshotEnvelope("employer-direct-v1", "hash", input), EmployerDirectSnapshotService.JsonOptions) };
        var store = Stub.Create<IJobSourceRunStore>((_, _) => Task.FromResult<JobSourceRun?>(run));
        var checkpoint = Stub.Create<IJobSourceRunCheckpoint>((name, _) => name == "get_RunId" ? (Guid?)run.Id : throw new InvalidOperationException(name));
        var provider = new EmployerDirectSnapshotProvider(store, checkpoint);
        var snapshot = await provider.FetchSnapshotAsync(f.Source);
        Assert.False(snapshot.IsComplete);
        Assert.Single(snapshot.Jobs);
        Assert.Equal(raw.ExternalId, snapshot.Jobs.Single().ExternalId);
        Assert.Equal(raw.SourcePostedAtUtc, snapshot.Jobs.Single().SourcePostedAtUtc);
        Assert.Equal(JobStatus.Published, f.Job.Status);
    }

    private sealed class Fixture : IDisposable
    {
        public JobPortalDbContext Context { get; } = new(new DbContextOptionsBuilder<JobPortalDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        public Company Company { get; } = new() { Name = "Example", NormalizedName = "EXAMPLE", Slug = "example" };
        public JobSource Source { get; }
        public Job Job { get; }
        public DateTime Now { get; } = DateTime.UtcNow;
        public Fixture()
        {
            Source = new() { Company = Company, CompanyId = Company.Id, CareerPageUrl = "https://example.com/careers", AtsType = AtsType.Greenhouse,
                AtsIdentifier = "example", IsActive = true, AutoPublishEnabled = true };
            Job = new() { JobSourceId = Source.Id, ExternalJobId = "42", Company = Company, CompanyId = Company.Id, Title = "Engineer",
                Description = "Build systems", Slug = "engineer", ApplicationUrl = "https://example.com/jobs/42", Location = "Pune, India",
                Status = JobStatus.Published, SourceOwnsPublication = true, LastSeenAtUtc = Now, ReferenceNumber = "TEST42" };
            Context.AddRange(Company, Source, Job); Context.SaveChanges();
        }
        public void Dispose() => Context.Dispose();
    }

    public class Stub : DispatchProxy
    {
        private Func<string, object?[], object?> invoke = null!;
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => invoke(targetMethod!.Name, args ?? []);
        public static T Create<T>(Func<string, object?[], object?> invoke) where T : class
        {
            var result = Create<T, Stub>(); ((Stub)(object)result).invoke = invoke; return result;
        }
    }
}


