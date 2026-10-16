using JobPortal.API.Controllers;
using JobPortal.Application.Features.FresherAcquisition;
using JobPortal.Domain.Entities;
using JobPortal.Persistence.Context;
using JobPortal.Persistence.Repositories;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class TalentdStatusTests
{
    [Fact]
    public async Task TalentdPostReturnsDurableAcceptanceWithoutCallingSynchronousImporter()
    {
        var status = new FresherImportStatus("same-id", "same-id", Guid.NewGuid(), "IN_PROGRESS",
            1, 0, 0, 0, 0, 0, 0, 0, null, null);
        var processor = new AcceptingProcessor(status);
        var controller = new InternalFresherBulkImportController(null!,
            Options.Create(new FresherAcquisitionOptions()), processor);
        using var requestLifetime = new CancellationTokenSource();
        var response = await controller.Import(new FresherBulkImportRequest
        {
            ImportId = "same-id", SourceType = "talentd", SourceName = "Talentd",
            Jobs = [new FresherImportCandidate { ExternalJobId = "TALENTD:ID:123", Title = "Example" }]
        }, requestLifetime.Token);
        requestLifetime.Cancel();
        var accepted = Assert.IsType<AcceptedResult>(response);
        Assert.Same(status, accepted.Value);
        Assert.Equal(requestLifetime.Token, processor.AcceptanceToken);
        Assert.Equal("same-id", processor.LastRequest!.ImportId);
        Assert.Equal(1, processor.AcceptanceCount);
    }

    [Theory]
    [InlineData("Running", "IN_PROGRESS")]
    [InlineData("AwaitingPublication", "IN_PROGRESS")]
    [InlineData("Completed", "COMPLETED")]
    [InlineData("Failed", "FAILED")]
    public async Task DurableStatusIsReadOnlyAndSourceScoped(string stored, string expected)
    {
        using var db = new JobPortalDbContext(new DbContextOptionsBuilder<JobPortalDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var source = new AcquisitionSource { ProviderKey = "talentd", Name = "Talentd", IsEnabled = true, AutoPublishEnabled = true };
        var run = new AcquisitionImportRun { AcquisitionSource = source, AcquisitionSourceId = source.Id, BatchId = "test", Status = stored,
            ReceivedCount = 1, EligibleCount = 1, CreatedCount = 1, PublishedCount = 1 };
        db.AddRange(source, run);
        db.AcquisitionImportItems.Add(new AcquisitionImportItem { AcquisitionImportRunId = run.Id, AcquisitionSourceId = source.Id,
            CandidateIdentityKey = "identity", Outcome = "Created", ReasonCode = "CREATED", PublicationStatus = "Published" });
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var store = new FresherAcquisitionStore(db);
        Assert.Null(await store.GetImportStatusAsync("talentd", "missing", default));
        Assert.Null(await store.GetImportStatusAsync("website-crawler", "test", default));
        var result = await store.GetImportStatusAsync("talentd", "test", default);
        Assert.Equal(expected, result!.Status);
        Assert.Equal(run.Id, result.RunId);
        Assert.Equal(1, result.PublishedCount);
        if (stored == "Completed") Assert.Single(result.Receipt!.Items);
        else Assert.Null(result.Receipt);
        Assert.Empty(db.ChangeTracker.Entries());
        var controller = new InternalFresherBulkImportController(null!, Options.Create(new FresherAcquisitionOptions()), null!);
        Assert.IsType<NotFoundObjectResult>(await controller.Status("talentd", "missing", store, default));
        Assert.IsType<OkObjectResult>(await controller.Status("talentd", "test", store, default));
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("wrong", false)]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", true)]
    public async Task StatusUsesBulkImportServiceToken(string? token, bool authorized)
    {
        var filter = new FresherServiceTokenFilter(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["FresherAcquisition:ServiceToken"] = new string('a', 32) }).Build());
        var http = new DefaultHttpContext();
        if (token is not null) http.Request.Headers.Authorization = "Bearer " + token;
        var context = new AuthorizationFilterContext(new ActionContext(http, new RouteData(), new ActionDescriptor()), []);
        await filter.OnAuthorizationAsync(context);
        Assert.Equal(authorized, context.Result is null);
        Assert.Contains(typeof(InternalFresherBulkImportController).GetCustomAttributes(typeof(ServiceFilterAttribute), true),
            attribute => ((ServiceFilterAttribute)attribute).ServiceType == typeof(FresherServiceTokenFilter));
    }

    private sealed class AcceptingProcessor(FresherImportStatus status) : IDurableFresherImportProcessor
    {
        public FresherBulkImportRequest? LastRequest { get; private set; }
        public CancellationToken AcceptanceToken { get; private set; }
        public int AcceptanceCount { get; private set; }

        public Task<FresherImportStatus> StageAsync(FresherBulkImportRequest request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            AcceptanceToken = cancellationToken;
            AcceptanceCount++;
            return Task.FromResult(status);
        }

        public Task<bool> ProcessNextAsync(IFresherBulkImportService importer, CancellationToken cancellationToken) =>
            Task.FromResult(false);
    }
}
