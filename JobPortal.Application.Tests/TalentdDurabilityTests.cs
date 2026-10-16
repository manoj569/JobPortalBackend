using System.Text.Json.Nodes;
using JobPortal.Application.Abstractions.Jobs;
using JobPortal.Application.Features.FresherAcquisition;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed partial class TalentdDatabaseValidationTests
{
    private static FresherBulkImportRequest Many(int count)
    {
        var fixture = Fixture();
        return Request(fixture.ImportId, Enumerable.Range(0, count).Select(i =>
            Change(fixture.Jobs[0], "externalJobId", JsonValue.Create(fixture.Jobs[0].ExternalJobId + ":" + i))).ToArray());
    }

    [TalentdPostgresFact]
    public async Task DurableAcceptanceSurvivesDisconnectedRequestAndFreshProcessorWithTwentyItems()
    {
        var request = Many(20);
        await using (var db = Database())
        {
            await db.Database.MigrateAsync(); await SeedOwner(db);
            var (_, queue) = Services(db, new TimingLogger());
            using var http = new CancellationTokenSource();
            var timer = System.Diagnostics.Stopwatch.StartNew();
            var accepted = await queue.StageAsync(request, http.Token);
            output.WriteLine("DurableAcceptance20Ms={0:F3}", timer.Elapsed.TotalMilliseconds);
            http.Cancel();
            Assert.Equal("IN_PROGRESS", accepted.Status); Assert.Equal(20, accepted.ReceivedCount);
            Assert.Equal(0, await db.AcquisitionImportItems.CountAsync(x => x.AcquisitionImportRunId == accepted.RunId));
            Assert.NotNull((await db.AcquisitionImportRuns.SingleAsync(x => x.Id == accepted.RunId)).StagedRequestJson);
            Assert.Equal(accepted.RunId, (await queue.StageAsync(request, default)).RunId);
            var changed = Request(request.ImportId, Change(request.Jobs[0], "title", JsonValue.Create("Changed content")));
            await Assert.ThrowsAsync<ImportIdPayloadConflictException>(() => queue.StageAsync(changed, default));
        }
        await using (var db = Database())
        {
            var (importer, queue) = Services(db, new TimingLogger());
            Assert.True(await queue.ProcessNextAsync(importer, default));
            var status = (await queue.GetImportStatusAsync("talentd", request.ImportId, default))!;
            Assert.Equal("COMPLETED", status.Status); Assert.Equal(20, status.CreatedCount); Assert.Equal(20, status.PublishedCount);
            Assert.Equal(20, status.Receipt!.Items.Count); Assert.Equal(0, status.FailedCount);
            var run = await db.AcquisitionImportRuns.AsNoTracking().SingleAsync(x => x.Id == status.RunId);
            Assert.NotNull(run.CompletedAtUtc);
            var replay = await queue.StageAsync(request, default);
            Assert.Equal(status.RunId, replay.RunId);
            Assert.Equal(1, await db.AcquisitionImportRuns.CountAsync(x => x.BatchId == request.ImportId));
            Assert.Equal(20, await db.AcquisitionImportItems.CountAsync(x => x.AcquisitionImportRunId == run.Id));
            Assert.Equal(20, status.Receipt.Items.Select(x => x.JobId).Distinct().Count());
        }
    }

    [TalentdPostgresFact]
    public async Task LegacyFiveWithOnePublishedHydratesSameRunAndNeverDuplicatesFirstJob()
    {
        var request = Many(5); Guid runId; Guid? originalJob;
        await using (var db = Database())
        {
            await db.Database.MigrateAsync(); await SeedOwner(db);
            var (importer, queue) = Services(db, new TimingLogger());
            var first = await importer.ImportAsync(Request(request.ImportId, request.Jobs[0]));
            originalJob = first.Items[0].JobId; runId = first.RunId!.Value;
            var run = await db.AcquisitionImportRuns.SingleAsync(x => x.Id == runId);
            run.Status = "Running"; run.CompletedAtUtc = null; run.ReceivedCount = 5;
            run.RequestFingerprint = FresherCandidateIdentity.PayloadFingerprint(request);
            run.CreatedCount = 0; run.PublishedCount = 0; await db.SaveChangesAsync();
            var before = await queue.GetImportStatusAsync("talentd", request.ImportId, default);
            Assert.Equal(1, before!.CreatedCount); Assert.Equal(1, before.PublishedCount); Assert.Null(before.Receipt);
            Assert.Equal(runId, (await queue.StageAsync(request, default)).RunId);
            db.ChangeTracker.Clear();
            var reconciled = await db.AcquisitionImportRuns.AsNoTracking().SingleAsync(x => x.Id == runId);
            Assert.Equal(1, reconciled.CreatedCount);
            Assert.Equal(1, reconciled.PublishedCount);
        }
        await using (var db = Database())
        {
            var (importer, queue) = Services(db, new TimingLogger());
            Assert.True(await queue.ProcessNextAsync(importer, default));
            var result = (await queue.GetImportStatusAsync("talentd", request.ImportId, default))!;
            Assert.Equal("COMPLETED", result.Status); Assert.Equal(runId, result.RunId);
            Assert.Equal(5, result.CreatedCount); Assert.Equal(5, result.PublishedCount);
            Assert.Equal(originalJob, result.Receipt!.Items[0].JobId);
            Assert.Equal(1, await db.Jobs.CountAsync(x => x.Id == originalJob));
        }
    }

    [TalentdPostgresFact]
    public async Task CrashAfterFirstPublicationRestartsFromReceiptAndStatusIsReadOnly()
    {
        var request = Many(5); Guid? firstJob;
        await using (var db = Database())
        {
            await db.Database.MigrateAsync(); await SeedOwner(db);
            using var shutdown = new CancellationTokenSource();
            var logger = new TimingLogger { OnPhase = phase => { if (phase == "Publication") shutdown.Cancel(); } };
            var (importer, queue) = Services(db, logger);
            var staged = await queue.StageAsync(request, default);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queue.ProcessNextAsync(importer, shutdown.Token));
            db.ChangeTracker.Clear();
            var partial = (await queue.GetImportStatusAsync("talentd", request.ImportId, default))!;
            Assert.Equal(5, partial.ReceivedCount); Assert.Equal(1, partial.CreatedCount); Assert.Equal(1, partial.PublishedCount);
            var parent = await db.AcquisitionImportRuns.AsNoTracking().SingleAsync(x => x.Id == staged.RunId);
            Assert.Equal(1, parent.CreatedCount); Assert.Equal(1, parent.PublishedCount);
            firstJob = (await db.AcquisitionImportItems.SingleAsync(x => x.AcquisitionImportRunId == staged.RunId)).JobId;
            var tracked = db.ChangeTracker.Entries().Count();
            Assert.Equal(partial, await queue.GetImportStatusAsync("talentd", request.ImportId, default));
            Assert.Equal(tracked, db.ChangeTracker.Entries().Count());
            await db.AcquisitionImportRuns.Where(x => x.Id == staged.RunId).ExecuteUpdateAsync(x => x.SetProperty(r => r.NextAttemptAtUtc, DateTime.UtcNow.AddMinutes(-1)));
        }
        await using (var db = Database())
        {
            var (importer, queue) = Services(db, new TimingLogger());
            Assert.True(await queue.ProcessNextAsync(importer, default));
            var status = (await queue.GetImportStatusAsync("talentd", request.ImportId, default))!;
            Assert.Equal("COMPLETED", status.Status); Assert.Equal(5, status.CreatedCount); Assert.Equal(5, status.PublishedCount);
            Assert.Equal(firstJob, status.Receipt!.Items[0].JobId);
        }
    }

    [TalentdPostgresFact]
    public async Task SessionOwnershipExcludesAnotherProcessorAndRecoversOnSessionClose()
    {
        var request = Many(1);
        await using var db = Database(); await db.Database.MigrateAsync(); await SeedOwner(db);
        var (_, queue) = Services(db, new TimingLogger()); var staged = await queue.StageAsync(request, default);
        var sourceId = (await db.AcquisitionImportRuns.SingleAsync(x => x.Id == staged.RunId)).AcquisitionSourceId;
        await using (await queue.AcquireRunLockAsync(sourceId, request.ImportId, default))
        {
            await using var other = Database(); var (importer, contender) = Services(other, new TimingLogger());
            Assert.False(await contender.ProcessNextAsync(importer, default));
        }
        await using var fresh = Database(); var (resumed, store) = Services(fresh, new TimingLogger());
        Assert.True(await store.ProcessNextAsync(resumed, default));
        Assert.Equal("COMPLETED", (await store.GetImportStatusAsync("talentd", request.ImportId, default))!.Status);
    }

    [TalentdPostgresFact]
    public async Task DurableRetriesAreBoundedAndFailedStatusHasDiagnostic()
    {
        var request = Many(1);
        await using var db = Database(); await db.Database.MigrateAsync(); await SeedOwner(db);
        var (_, queue) = Services(db, new TimingLogger()); var staged = await queue.StageAsync(request, default);
        for (var i = 0; i < 5; i++)
        {
            await db.AcquisitionImportRuns.Where(x => x.Id == staged.RunId).ExecuteUpdateAsync(x => x.SetProperty(r => r.NextAttemptAtUtc, DateTime.UtcNow.AddMinutes(-1)));
            Assert.True(await queue.ProcessNextAsync(new BrokenImporter(), default));
        }
        var status = (await queue.GetImportStatusAsync("talentd", request.ImportId, default))!;
        Assert.Equal("FAILED", status.Status); Assert.Contains("TimeoutException", status.FailureSummary);
        Assert.Null(status.Receipt); Assert.False(await queue.ProcessNextAsync(new BrokenImporter(), default));
        Assert.NotNull((await db.AcquisitionImportRuns.AsNoTracking().SingleAsync(x => x.Id == staged.RunId)).CompletedAtUtc);
    }

    [TalentdPostgresFact]
    public async Task PublicationPendingDoesNotDuplicateJobAndIsRetriedToCompletion()
    {
        var request = Many(1);
        await using var db = Database(); await db.Database.MigrateAsync(); await SeedOwner(db);
        var calls = 0;
        var (importer, queue) = Services(db, new TimingLogger(), inner => new FirstPublicationPending(inner, () => ++calls));
        var staged = await queue.StageAsync(request, default);
        Assert.True(await queue.ProcessNextAsync(importer, default));
        var pending = (await queue.GetImportStatusAsync("talentd", request.ImportId, default))!;
        Assert.Equal("IN_PROGRESS", pending.Status);
        Assert.Equal(1, pending.CreatedCount); Assert.Equal(0, pending.PublishedCount);
        Assert.Equal(1, await db.AcquisitionImportItems.CountAsync(x => x.AcquisitionImportRunId == staged.RunId));
        await db.AcquisitionImportRuns.Where(x => x.Id == staged.RunId)
            .ExecuteUpdateAsync(x => x.SetProperty(r => r.NextAttemptAtUtc, DateTime.UtcNow.AddMinutes(-1)));

        Assert.True(await queue.ProcessNextAsync(importer, default));
        var completed = (await queue.GetImportStatusAsync("talentd", request.ImportId, default))!;
        Assert.Equal("COMPLETED", completed.Status); Assert.Equal(1, completed.CreatedCount); Assert.Equal(1, completed.PublishedCount);
        Assert.Equal(1, await db.Jobs.CountAsync(x => x.AcquisitionExternalId ==
            FresherCandidateIdentity.Normalize(request.Jobs[0].ExternalJobId)));
        Assert.True(calls >= 2);
    }

    private sealed class BrokenImporter : IFresherBulkImportService
    {
        public Task<FresherBulkImportResponse> ImportAsync(FresherBulkImportRequest request, CancellationToken cancellationToken = default) => throw new TimeoutException();
        public Task<FresherBulkImportResponse> ProcessStagedAsync(FresherBulkImportRequest request, CancellationToken cancellationToken) => throw new TimeoutException();
    }

    private sealed class FirstPublicationPending(IJobAutoPublishService inner, Func<int> attempt) : IJobAutoPublishService
    {
        public Task<JobAutoPublishResult> TryPublishAsync(Guid jobId, CancellationToken cancellationToken = default) =>
            attempt() == 1
                ? Task.FromResult(new JobAutoPublishResult { Outcome = JobAutoPublishOutcome.NeedsReview })
                : inner.TryPublishAsync(jobId, cancellationToken);
    }
}
