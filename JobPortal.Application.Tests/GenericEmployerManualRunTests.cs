using JobPortal.Application.Abstractions.Persistence;
using JobPortal.Application.Features.JobAggregation;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using Xunit;
using static JobPortal.Application.Tests.EmployerDirectIntegrationTests;

namespace JobPortal.Application.Tests;

public sealed class GenericEmployerManualRunTests
{
    [Theory]
    [InlineData(GenericEmployerContract.Identifier)]
    [InlineData(GenericEmployerContract.LegacyIdentifier)]
    public async Task ActiveGenericSourceWithNoPreviousRunCanBeManuallyQueued(string identifier)
    {
        var source = new JobSource { AtsType = AtsType.Custom, AtsIdentifier = identifier,
            IsActive = true, CareerPageUrl = "https://example.com/careers" };
        var row = new JobSourceRun { JobSourceId = source.Id, Status = JobSourceRunStatus.Queued };
        var sources = Stub.Create<IJobSourceRepository>((method, args) =>
        {
            Assert.Equal("GetByIdAsync", method);
            Assert.Equal(source.Id, args![0]);
            return Task.FromResult<JobSource?>(source);
        });
        var calls = 0;
        var store = Stub.Create<IJobSourceRunStore>((method, args) =>
        {
            Assert.Equal("EnqueueAsync", method);
            Assert.Equal(source.Id, args![0]);
            calls++;
            return Task.FromResult(row);
        });
        var service = new JobSourceRunService(sources, store, TimeProvider.System);
        Assert.Null(source.LastRunAtUtc);
        Assert.Equal(0, calls);
        var response = await service.EnqueueAsync(source.Id, null, default);
        Assert.Equal(1, calls);
        Assert.Equal(source.Id, response.JobSourceId);
        Assert.Equal(row.Id, response.RunId);
        Assert.Equal("Queued", response.Status);
    }
}
