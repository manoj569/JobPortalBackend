using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using JobPortal.Application.Abstractions.Jobs;
using JobPortal.Application.Features.FresherAcquisition;
using JobPortal.Domain.Common;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
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

    [TalentdPostgresFact]
    public async Task FailedSameImportIdRecoversOnlyLegacyAmbiguousItemsAndReusesSevenSuccesses()
    {
        var fixture = Fixture();
        var suffix = Guid.NewGuid().ToString("N");
        var domain = "shared-" + suffix + ".example.test";
        var targetName = "Recovery target " + suffix;
        var jobs = Enumerable.Range(0, 10).Select(index =>
        {
            var candidate = Change(fixture.Jobs[0], "externalJobId", JsonValue.Create(fixture.Jobs[0].ExternalJobId + ":recovery:" + index));
            if (index < 7)
            {
                candidate = Change(candidate, "companyName", JsonValue.Create("Independent employer " + suffix + " " + index));
                candidate = Change(candidate, "companyWebsiteUrl", null);
            }
            else
            {
                candidate = Change(candidate, "companyName", JsonValue.Create(targetName));
                candidate = Change(candidate, "companyWebsiteUrl", JsonValue.Create("https://" + domain));
                candidate = Change(candidate, "companyWebsiteVerified", JsonValue.Create(true));
            }
            return candidate;
        }).ToArray();
        var request = Request(fixture.ImportId, jobs);
        var publisherCalls = new ConcurrentDictionary<Guid, int>();
        Guid runId;
        Guid[] successfulJobIds;

        await using (var db = Database())
        {
            await db.Database.MigrateAsync(); await SeedOwner(db);
            var source = await db.AcquisitionSources.SingleAsync(x => x.ProviderKey == "talentd");
            var ownerId = await db.Users.Where(x => x.RoleId == SystemRoleIds.Administrator && x.Status == UserStatus.Active)
                .Select(x => x.Id).FirstAsync();
            db.Companies.AddRange(
                new Company { Name = "Existing brand A " + suffix, Slug = "existing-brand-a-" + suffix, WebsiteUrl = "https://" + domain, OwnerUserId = ownerId },
                new Company { Name = "Existing brand B " + suffix, Slug = "existing-brand-b-" + suffix, WebsiteUrl = "https://www." + domain, OwnerUserId = ownerId });
            await db.SaveChangesAsync();

            var (importer, queue) = Services(db, new TimingLogger(), inner => new CountingPublisher(inner, publisherCalls));
            var accepted = await queue.StageAsync(request, default);
            Assert.Equal("IN_PROGRESS", accepted.Status);
            await db.AcquisitionImportRuns.Where(x => x.Id == accepted.RunId)
                .ExecuteUpdateAsync(x => x.SetProperty(r => r.NextAttemptAtUtc, new DateTime(1, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
            Assert.True(await queue.ProcessNextAsync(importer, default));
            var partial = (await queue.GetImportStatusAsync("talentd", request.ImportId, default))!;
            Assert.True(partial.CreatedCount == 7,
                $"Expected seven initial successes; status={partial.Status}, failure={partial.FailureSummary}, received={partial.ReceivedCount}, failed={partial.FailedCount}, attempts={partial.ProcessingAttempts}.");
            Assert.Equal(7, partial.PublishedCount);
            Assert.Equal(0, partial.FailedCount); // New ambiguous items have no immutable terminal receipt.
            var durableSuccesses = await db.AcquisitionImportItems.AsNoTracking()
                .Where(x => x.AcquisitionImportRunId == accepted.RunId).OrderBy(x => x.ItemIndex).ToArrayAsync();
            Assert.Equal(7, durableSuccesses.Length);
            successfulJobIds = durableSuccesses.Select(x => x.JobId!.Value).ToArray();
            runId = accepted.RunId;

            // Recreate the pre-fix production representation: the old version wrote
            // three terminal COMPANY_DOMAIN_AMBIGUOUS receipts before failing the run.
            await using (var chunk = await queue.BeginChunkAsync([], default))
            {
                var legacyReceipts = jobs.Skip(7).Select((candidate, offset) =>
                {
                    var index = offset + 7;
                    return new FresherReceiptInput(index, candidate, FresherCandidateIdentity.Create(source.Id, candidate, true),
                        "ContentNeutral", new(index, "Failed", "COMPANY_DOMAIN_AMBIGUOUS"), DateTime.UtcNow);
                }).ToArray();
                await chunk.CommitAsync(runId, legacyReceipts, default);
            }
            await queue.CompleteRunAsync(runId, new(request.ImportId, 10, 7, 0, 0, 0, 0, 7, 0, 0, 3, []), default);
            Assert.Equal("FAILED", (await queue.GetImportStatusAsync("talentd", request.ImportId, default))!.Status);
            Assert.Equal(7, publisherCalls.Values.Count(x => x == 1));
        }

        async Task<FresherImportStatus> RetryFromFreshContext()
        {
            await using var db = Database();
            var (_, queue) = Services(db, new TimingLogger(), inner => new CountingPublisher(inner, publisherCalls));
            return await queue.StageAsync(request, default);
        }
        var concurrentRetries = await Task.WhenAll(RetryFromFreshContext(), RetryFromFreshContext());
        Assert.All(concurrentRetries, x => { Assert.Equal(runId, x.RunId); Assert.Equal("IN_PROGRESS", x.Status); });
        await using (var db = Database())
            await db.AcquisitionImportRuns.Where(x => x.Id == runId)
                .ExecuteUpdateAsync(x => x.SetProperty(r => r.NextAttemptAtUtc, new DateTime(1, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
        await using (var db = Database())
        {
            var (importer, queue) = Services(db, new TimingLogger(), inner => new CountingPublisher(inner, publisherCalls));
            Assert.True(await queue.ProcessNextAsync(importer, default));
        }

        await using (var db = Database())
        {
            var (_, queue) = Services(db, new TimingLogger());
            var stillAmbiguous = (await queue.GetImportStatusAsync("talentd", request.ImportId, default))!;
            Assert.Equal("IN_PROGRESS", stillAmbiguous.Status);
            Assert.Equal(7, stillAmbiguous.CreatedCount);
            Assert.Equal(7, stillAmbiguous.PublishedCount);
            Assert.Equal(3, stillAmbiguous.FailedCount);
            Assert.Equal(2, stillAmbiguous.ProcessingAttempts);
            Assert.Equal(7, await db.Jobs.CountAsync(x => successfulJobIds.Contains(x.Id)));

            var resolvedCompany = await db.Companies.Where(x => x.NormalizedWebsiteDomain == domain).OrderBy(x => x.Name).FirstAsync();
            resolvedCompany.Name = targetName;
            await db.SaveChangesAsync();
            await db.AcquisitionImportRuns.Where(x => x.Id == runId)
                .ExecuteUpdateAsync(x => x.SetProperty(r => r.NextAttemptAtUtc, new DateTime(1, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
            var (importer, retryQueue) = Services(db, new TimingLogger(), inner => new CountingPublisher(inner, publisherCalls));
            Assert.True(await retryQueue.ProcessNextAsync(importer, default));
        }

        await using (var db = Database())
        {
            var (_, queue) = Services(db, new TimingLogger(), inner => new CountingPublisher(inner, publisherCalls));
            var completed = (await queue.GetImportStatusAsync("talentd", request.ImportId, default))!;
            Assert.Equal("COMPLETED", completed.Status);
            Assert.Equal(runId, completed.RunId);
            Assert.Equal(10, completed.ReceivedCount);
            Assert.Equal(10, completed.CreatedCount);
            Assert.Equal(10, completed.PublishedCount);
            Assert.Equal(0, completed.FailedCount);
            Assert.Equal(3, completed.ProcessingAttempts);
            Assert.Equal(10, completed.Receipt!.Items.Count);
            Assert.Equal(10, completed.Receipt.Items.Select(x => x.JobId).Distinct().Count());
            var finalReceipts = await db.AcquisitionImportItems.AsNoTracking()
                .Where(x => x.AcquisitionImportRunId == runId).OrderBy(x => x.ItemIndex).ToArrayAsync();
            Assert.Equal(10, finalReceipts.Length);
            Assert.Equal(successfulJobIds, finalReceipts.Take(7).Select(x => x.JobId!.Value).ToArray());
            Assert.Equal(10, await db.Jobs.CountAsync(x => finalReceipts.Select(r => r.JobId).Contains(x.Id)));
            Assert.All(finalReceipts, x => { Assert.Equal("Created", x.Outcome); Assert.Equal("Published", x.PublicationStatus); });
            Assert.Equal(10, publisherCalls.Count);
            Assert.All(publisherCalls.Values, calls => Assert.Equal(1, calls));

            var stableReplay = await queue.StageAsync(request, default);
            Assert.Equal("COMPLETED", stableReplay.Status);
            Assert.Equal(runId, stableReplay.RunId);
            Assert.All(publisherCalls.Values, calls => Assert.Equal(1, calls));
            var changed = Change(jobs[0], "title", JsonValue.Create("Changed payload must conflict"));
            var changedPayload = new[] { changed }.Concat(jobs.Skip(1)).ToArray();
            await Assert.ThrowsAsync<ImportIdPayloadConflictException>(() => queue.StageAsync(Request(request.ImportId, changedPayload), default));
        }
    }

    [TalentdPostgresFact]
    public async Task RecoveryLeavesMixedTerminalFailureUntouched()
    {
        var request = Many(2);
        await using var db = Database();
        await db.Database.MigrateAsync(); await SeedOwner(db);
        var (importer, queue) = Services(db, new TimingLogger());
        var accepted = await queue.StageAsync(request, default);
        var sourceId = (await db.AcquisitionImportRuns.SingleAsync(x => x.Id == accepted.RunId)).AcquisitionSourceId;
        var failures = new[]
        {
            new FresherReceiptInput(0, request.Jobs[0], FresherCandidateIdentity.Create(sourceId, request.Jobs[0], true),
                "ContentNeutral", new(0, "Failed", "COMPANY_DOMAIN_AMBIGUOUS"), DateTime.UtcNow),
            new FresherReceiptInput(1, request.Jobs[1], FresherCandidateIdentity.Create(sourceId, request.Jobs[1], true),
                "ContentNeutral", new(1, "Failed", "COMPANY_NOT_RESOLVED"), DateTime.UtcNow)
        };
        await using (var chunk = await queue.BeginChunkAsync([], default))
            await chunk.CommitAsync(accepted.RunId, failures, default);
        await queue.CompleteRunAsync(accepted.RunId,
            new(request.ImportId, 2, 0, 0, 0, 0, 0, 0, 0, 0, 2, []), default);

        var reopened = await queue.StageAsync(request, default);
        Assert.Equal(accepted.RunId, reopened.RunId);
        Assert.Equal("IN_PROGRESS", reopened.Status);
        await db.AcquisitionImportRuns.Where(x => x.Id == accepted.RunId)
            .ExecuteUpdateAsync(x => x.SetProperty(r => r.NextAttemptAtUtc, new DateTime(1, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
        Assert.True(await queue.ProcessNextAsync(importer, default));

        var status = (await queue.GetImportStatusAsync("talentd", request.ImportId, default))!;
        Assert.Equal("FAILED", status.Status);
        Assert.Equal(1, status.CreatedCount);
        Assert.Equal(1, status.FailedCount);
        var receipts = await db.AcquisitionImportItems.AsNoTracking()
            .Where(x => x.AcquisitionImportRunId == accepted.RunId).OrderBy(x => x.ItemIndex).ToArrayAsync();
        Assert.Equal("Created", receipts[0].Outcome);
        Assert.Equal("COMPANY_NOT_RESOLVED", receipts[1].ReasonCode);
        Assert.Equal("Failed", receipts[1].Outcome);
        Assert.Null(receipts[1].JobId);
        Assert.Equal("FAILED", (await queue.StageAsync(request, default)).Status);
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

    private sealed class CountingPublisher(IJobAutoPublishService inner, ConcurrentDictionary<Guid, int> calls) : IJobAutoPublishService
    {
        public Task<JobAutoPublishResult> TryPublishAsync(Guid jobId, CancellationToken cancellationToken = default)
        {
            calls.AddOrUpdate(jobId, 1, (_, count) => count + 1);
            return inner.TryPublishAsync(jobId, cancellationToken);
        }
    }
}
