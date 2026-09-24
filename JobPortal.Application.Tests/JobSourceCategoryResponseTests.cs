using System.Text.Json;
using JobPortal.API.Controllers;
using JobPortal.Application.Abstractions.Jobs;
using JobPortal.Application.Features.JobAggregation;
using JobPortal.Application.Services;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Infrastructure;
using JobPortal.Persistence.Repositories;
using JobPortal.Shared.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class JobSourceCategoryResponseTests
{
    [Theory]
    [InlineData("valid")]
    [InlineData("missing")]
    [InlineData("invalid")]
    [InlineData("nonexistent")]
    [InlineData("deleted")]
    public async Task EnvironmentMappingMatchesIngestionListAndDetailResponses(string scenario)
    {
        using var fixture = new JobSourceFixture();
        var source = new JobSource
        {
            Id = Guid.Parse("195d3823-6a47-4e1e-bf71-8830be0131ee"),
            CompanyId = fixture.Company.Id, Company = fixture.Company,
            AtsType = AtsType.Greenhouse, AtsIdentifier = "togetherai", CareerPageUrl = "https://example.test/careers"
        };
        var category = new Category
        {
            Id = Guid.Parse("df7f6710-bd28-41ea-aa97-7994c97f182a"), Name = "General", Slug = "general",
            IsDeleted = scenario == "deleted"
        };
        fixture.Context.Add(source);
        if (scenario != "nonexistent") fixture.Context.Add(category);
        await fixture.Context.SaveChangesAsync();

        // Prefix isolates this test from real process configuration; the suffix is the exact Render key.
        var prefix = $"JOB_SOURCE_RESPONSE_TEST_{Guid.NewGuid():N}_";
        var key = prefix + "JobAggregation__SourceCategories__195d3823-6a47-4e1e-bf71-8830be0131ee";
        try
        {
            if (scenario != "missing") Environment.SetEnvironmentVariable(key, scenario == "invalid" ? "not-a-guid" : category.Id.ToString("D"));
            var configuration = new ConfigurationBuilder().AddEnvironmentVariables(prefix).Build();
            using var configurationLifetime = (IDisposable)configuration;
            var services = new ServiceCollection();
            services.AddInfrastructure(configuration); // Actual production options binding, not a substitute.
            using var provider = services.BuildServiceProvider();
            var categories = new CategoryManagementRepository(fixture.Context);
            var resolver = new JobSourceCategoryResolver(provider.GetRequiredService<IOptionsMonitor<JobAggregationOptions>>(), categories);
            var service = new JobSourceManagementService(fixture.Repository, new CompanyManagementRepository(fixture.Context),
                categories, resolver, fixture.Runner, fixture.Guard, new UnitOfWork(fixture.Context), fixture.Audit,
                new SaveJobSourceRequestValidator(), new JobSourceSearchQueryValidator(), fixture.Locks);
            var controller = new AdminJobSourcesController(service);
            var listAction = await controller.Search(new(), default);
            var list = Assert.IsType<ApiResponse<PagedResponse<JobSourceResponse>>>(Assert.IsType<OkObjectResult>(listAction.Result).Value);
            var listed = Assert.Single(list.Data!.Items, x => x.Id == source.Id);
            var detailAction = await controller.Get(source.Id, default);
            var detail = Assert.IsType<ApiResponse<JobSourceResponse>>(Assert.IsType<OkObjectResult>(detailAction.Result).Value).Data!;
            Guid? expectedId = scenario == "valid" ? category.Id : null;
            Assert.Equal(expectedId, listed.CategoryId);
            Assert.Equal(scenario == "valid" ? "General" : null, listed.CategoryName);
            Assert.Equal(listed, detail);
            Assert.Equal(expectedId, await resolver.ResolveCategoryIdAsync(source, new RawExternalJob()));
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(detail, JsonSerializerOptions.Web));
            Assert.Equal(scenario == "valid" ? "General" : null, json.RootElement.GetProperty("categoryName").GetString());
            Assert.Null(typeof(SaveJobSourceRequest).GetProperty("CategoryId"));
            Assert.Null(typeof(SaveJobSourceRequest).GetProperty("CategoryName"));
        }
        finally { Environment.SetEnvironmentVariable(key, null); }
    }
}
