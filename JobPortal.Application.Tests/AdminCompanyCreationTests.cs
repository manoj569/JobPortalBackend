using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using FluentValidation;
using JobPortal.API.Controllers;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Application.Features.AdminManagement;
using JobPortal.Application.Features.JobAggregation;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Persistence.Repositories;
using JobPortal.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JobPortal.Application.Tests;

// Uses the production controllers, services and repositories with an isolated in-memory database.
public sealed class AdminCompanyCreationTests
{
    [Fact]
    public async Task MinimalAdminRequestCreatesTrimmedCompanyAndReturnedIdWorksForJobSource()
    {
        using var f = new JobSourceFixture();
        var administrator = Guid.NewGuid();
        var controller = Controller(f, administrator);
        var request = JsonSerializer.Deserialize<CreateCompanyRequest>(
            """{"name":"  Razorpay  ","websiteUrl":"https://razorpay.com"}""",
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

        var action = await controller.Create(request, default);
        var created = Assert.IsType<CreatedAtActionResult>(action.Result);
        Assert.Equal(201, created.StatusCode);
        Assert.Equal(nameof(AdminCompaniesController.Get), created.ActionName);
        var envelope = Assert.IsType<ApiResponse<CompanyResponse>>(created.Value);
        var company = envelope.Data;
        Assert.Equal(company.Id, created.RouteValues!["id"]);
        Assert.Equal("Razorpay", company.Name);
        Assert.Equal("https://razorpay.com", company.WebsiteUrl);
        Assert.Null(company.LogoUrl);
        Assert.False(company.IsVerified);
        var stored = await f.Context.Companies.AsNoTracking().SingleAsync(x => x.Id == company.Id);
        Assert.Equal(administrator, stored.OwnerUserId);
        Assert.Equal("Razorpay", stored.Name);
        Assert.Equal("razorpay", stored.NormalizedName);

        var optionsAction = await controller.Options(default);
        var options = Assert.IsType<ApiResponse<IReadOnlyCollection<AdminOptionResponse>>>(
            Assert.IsType<OkObjectResult>(optionsAction.Result).Value).Data;
        Assert.Contains(options, x => x.Id == company.Id && x.Name == "Razorpay");
        Assert.Contains(options, x => x.Id == f.Company.Id); // Existing selections remain available.

        var sources = new AdminJobSourcesController(f.Service);
        var sourceAction = await sources.Create(new SaveJobSourceRequest(
            company.Id, "https://job-boards.greenhouse.io/razorpay", AtsType.Greenhouse, "razorpay"), default);
        var source = Assert.IsType<ApiResponse<JobSourceResponse>>(
            Assert.IsType<CreatedAtActionResult>(sourceAction.Result).Value).Data;
        Assert.Equal(company.Id, source.CompanyId);
        Assert.Equal("Razorpay", source.CompanyName);
        Assert.True(source.IsActive);
        Assert.Equal(company.Id, (await f.Context.JobSources.AsNoTracking().SingleAsync(x => x.Id == source.Id)).CompanyId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\r\n")]
    public async Task BlankNameIsRejectedBeforeAnythingIsInserted(string? name)
    {
        using var f = new JobSourceFixture();
        var before = await f.Context.Companies.CountAsync();
        await Assert.ThrowsAsync<ValidationException>(() => Service(f).CreateAsync(Guid.NewGuid(), new(name!)));
        Assert.Equal(before, await f.Context.Companies.CountAsync());
    }

    [Theory]
    [InlineData("razorpay")]
    [InlineData("RAZORPAY")]
    [InlineData("  RaZoRpAy  ")]
    public async Task MatchingNormalizedNameConflictsEvenWithDifferentExplicitSlug(string name)
    {
        using var f = new JobSourceFixture();
        var service = Service(f);
        var first = await service.CreateAsync(Guid.NewGuid(), new("Razorpay", Slug: "original-company-slug"));
        var error = await Assert.ThrowsAsync<ConflictException>(() => service.CreateAsync(
            Guid.NewGuid(), new(name, Slug: "different-company-slug")));
        Assert.Equal(409, error.StatusCode);
        Assert.Equal("duplicate_company_name", error.Code);
        Assert.Single(await f.Context.Companies.Where(x => x.NormalizedName == "razorpay").ToArrayAsync());
        Assert.Equal(first.Id, (await f.Context.Companies.SingleAsync(x => x.NormalizedName == "razorpay")).Id);
    }

    [Fact]
    public async Task ExistingWhitespaceNormalizedNameAlsoConflicts()
    {
        using var f = new JobSourceFixture();
        f.Context.Companies.Add(new Company { Name = "Razorpay   Software", Slug = "original" });
        await f.Context.SaveChangesAsync();
        var error = await Assert.ThrowsAsync<ConflictException>(() => Service(f).CreateAsync(
            Guid.NewGuid(), new("  RAZORPAY Software ", Slug: "different")));
        Assert.Equal("duplicate_company_name", error.Code);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "  ")]
    [InlineData("https://razorpay.com", null)]
    [InlineData(null, "https://assets.example.test/logo.png")]
    public async Task WebsiteAndLogoAreOptionalAndOnlySuppliedValuesAreStored(string? website, string? logo)
    {
        using var f = new JobSourceFixture();
        var response = await Service(f).CreateAsync(Guid.NewGuid(), new("Razorpay", WebsiteUrl: website, LogoUrl: logo));
        Assert.Equal(string.IsNullOrWhiteSpace(website) ? null : website, response.WebsiteUrl);
        Assert.Equal(string.IsNullOrWhiteSpace(logo) ? null : logo, response.LogoUrl);
    }

    [Theory]
    [InlineData("javascript:alert(1)", null)]
    [InlineData("not-a-url", null)]
    [InlineData(null, "file:///logo.png")]
    public async Task SuppliedInvalidUrlsAreRejected(string? website, string? logo)
    {
        using var f = new JobSourceFixture();
        await Assert.ThrowsAsync<ValidationException>(() => Service(f).CreateAsync(
            Guid.NewGuid(), new("Razorpay", WebsiteUrl: website, LogoUrl: logo)));
        Assert.DoesNotContain(f.Context.Companies, x => x.Name == "Razorpay");
    }

    [Fact]
    public async Task RenameCannotCreateDuplicateAndUnchangedNameStillWorks()
    {
        using var f = new JobSourceFixture();
        var service = Service(f);
        var first = await service.CreateAsync(Guid.NewGuid(), new("Razorpay"));
        var second = await service.CreateAsync(Guid.NewGuid(), new("Another Company"));
        var own = await service.UpdateAsync(first.Id, new("RAZORPAY", null, null, null, null, null, null, null, false));
        Assert.Equal(first.Id, own.Id);
        var error = await Assert.ThrowsAsync<ConflictException>(() => service.UpdateAsync(second.Id,
            new("Razorpay", "different-slug", null, null, null, null, null, null, false)));
        Assert.Equal("duplicate_company_name", error.Code);
    }

    [Fact]
    public void ExistingCompanyEndpointsRemainAdministratorOnlyAndUseExistingRoutes()
    {
        var type = typeof(AdminCompaniesController);
        Assert.Equal("Administrator", Assert.Single(type.GetCustomAttributes<AuthorizeAttribute>()).Roles);
        Assert.Empty(type.GetCustomAttributes<AllowAnonymousAttribute>());
        Assert.Equal("api/admin/companies", type.GetCustomAttribute<RouteAttribute>()!.Template);
        var create = type.GetMethod(nameof(AdminCompaniesController.Create))!;
        Assert.NotNull(create.GetCustomAttribute<HttpPostAttribute>());
        Assert.Empty(create.GetCustomAttributes<AllowAnonymousAttribute>());
        var options = type.GetMethod(nameof(AdminCompaniesController.Options))!;
        Assert.Equal("options", options.GetCustomAttribute<HttpGetAttribute>()!.Template);
        Assert.Empty(options.GetCustomAttributes<AllowAnonymousAttribute>());
    }

    [Fact]
    public void ExistingSchemaProtectsConcurrentDuplicateNormalizedNames()
    {
        using var f = new JobSourceFixture();
        var index = Assert.Single(f.Context.Model.FindEntityType(typeof(Company))!.GetIndexes(),
            x => x.Properties.Select(p => p.Name).SequenceEqual([nameof(Company.NormalizedName)]));
        Assert.True(index.IsUnique);
        Assert.Equal("\"IsDeleted\" = FALSE", index.GetFilter());
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("Candidate", false)]
    [InlineData("Employer", false)]
    [InlineData("Administrator", true)]
    public async Task CompanyControllerAuthorizationPolicyAllowsOnlyAdministrator(string? role, bool allowed)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorization();
        using var provider = services.BuildServiceProvider();
        var policy = (await AuthorizationPolicy.CombineAsync(provider.GetRequiredService<IAuthorizationPolicyProvider>(),
            typeof(AdminCompaniesController).GetCustomAttributes<AuthorizeAttribute>()))!;
        var identity = role is null ? new ClaimsIdentity() : new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()), new Claim(ClaimTypes.Role, role)], "test");
        var result = await provider.GetRequiredService<IAuthorizationService>()
            .AuthorizeAsync(new ClaimsPrincipal(identity), null, policy);
        Assert.Equal(allowed, result.Succeeded);
    }

    private static CompanyManagementService Service(JobSourceFixture f) => new(
        new CompanyManagementRepository(f.Context), new UnitOfWork(f.Context), f.Audit,
        new CreateCompanyRequestValidator(), new UpdateCompanyRequestValidator(), new CompanySearchQueryValidator());

    private static AdminCompaniesController Controller(JobSourceFixture f, Guid administrator) => new(Service(f))
    {
        ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([
                    new Claim(ClaimTypes.NameIdentifier, administrator.ToString()),
                    new Claim(ClaimTypes.Role, "Administrator")], "test"))
            }
        }
    };
}
