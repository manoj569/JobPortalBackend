using System.Text.Json;
using JobPortal.API.Controllers;
using JobPortal.Application.Features.AdminImports;
using JobPortal.Application.Features.JobDiscovery;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class JobDiscoveryResponseTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task RunDetailsEnvelopeSerializesWithoutEntityNavigationCycle()
    {
        var runId = Guid.NewGuid();
        var item = new JobDiscoveryItemResponse(Guid.NewGuid(), "Adzuna", "source-1", "Engineer",
            "Acme", "Engineering", "https://example.test/apply", "Pune", null, null,
            DateTime.UtcNow, "Candidate", null, null, null, DateTime.UtcNow);
        var details = new JobDiscoveryRunDetailsResponse(runId, "Manual", "Completed",
            DateTime.UtcNow, DateTime.UtcNow, 1, 0, 0, null, [item]);
        var controller = new JobDiscoveryController(new FakeService(details));

        var action = await controller.Details(runId, CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(action.Result);
        var json = JsonSerializer.Serialize(ok.Value, JsonOptions);

        Assert.Contains("\"items\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"run\"", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("runId", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RunHistoryEnvelopeContainsSummariesOnly()
    {
        var summary = new JobDiscoveryRunSummary(Guid.NewGuid(), "Scheduled", "Completed",
            DateTime.UtcNow, DateTime.UtcNow, 2, 1, 0, null);
        var controller = new JobDiscoveryController(new FakeService(null, [summary]));

        var envelope = await controller.Runs(25, CancellationToken.None);
        var json = JsonSerializer.Serialize(envelope, JsonOptions);

        Assert.DoesNotContain("\"items\"", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"run\"", json, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class FakeService(JobDiscoveryRunDetailsResponse? details,
        IReadOnlyCollection<JobDiscoveryRunSummary>? summaries = null) : IJobDiscoveryService
    {
        public Task<JobDiscoveryRunDetailsResponse?> GetAsync(Guid id, CancellationToken ct) => Task.FromResult<JobDiscoveryRunDetailsResponse?>(details);
        public Task<JobDiscoveryRunSummary> RunAsync(string trigger, JobDiscoveryCriteria? criteria, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<JobDiscoveryRunSummary>> ListAsync(int take, CancellationToken ct) =>
            Task.FromResult(summaries ?? (IReadOnlyCollection<JobDiscoveryRunSummary>)[]);
        public Task<CsvImportResult> PreviewAsync(Guid runId, IReadOnlyCollection<Guid> itemIds, CancellationToken ct) => throw new NotSupportedException();
        public Task<JobDiscoveryCommitResult> CommitAsync(Guid administratorId, Guid runId, IReadOnlyCollection<Guid> itemIds, CancellationToken ct) => throw new NotSupportedException();
    }
}
