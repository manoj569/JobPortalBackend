using System.Security.Claims;
using System.Text.Json;
using JobPortal.API.Authorization;
using JobPortal.API.Controllers;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Persistence.Context;
using JobPortal.Persistence.Repositories;
using JobPortal.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class InterviewInsightsMembershipAuthorizationTests
{
    private static readonly DateTime Now = new(2026, 8, 24, 12, 0, 0, DateTimeKind.Utc);
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task ActivePaidNonExpiredMemberIsAuthorized()
    {
        var userId = Guid.NewGuid();
        await using var db = Db();
        var membership = Membership(userId, MembershipStatus.Active, Now.AddDays(-1), Now.AddDays(29));
        db.Add(membership);
        db.Add(new Payment
        {
            UserId = userId, MembershipId = membership.Id, Status = PaymentStatus.Paid,
            Amount = 99m, CurrencyCode = "INR", PaidAtUtc = Now.AddDays(-1)
        });
        await db.SaveChangesAsync();

        var context = AuthorizationContext(userId);
        await new ActiveInterviewInsightsMembershipHandler(new MembershipRepository(db, new FixedTimeProvider()))
            .HandleAsync(context);
        Assert.True(context.HasSucceeded);
    }

    [Theory]
    [InlineData(null, null, null)]
    [InlineData(MembershipStatus.Active, -30, 0)]
    [InlineData(MembershipStatus.Expired, -30, -1)]
    [InlineData(MembershipStatus.Cancelled, -1, 29)]
    [InlineData(MembershipStatus.Pending, -1, 29)]
    [InlineData(MembershipStatus.Suspended, -1, 29)]
    public async Task CandidateWithoutActiveAccessGetsExactForbiddenContract(
        MembershipStatus? status, int? startOffset, int? endOffset)
    {
        var userId = Guid.NewGuid();
        await using var db = Db();
        if (status.HasValue)
        {
            var membership = Membership(userId, status.Value, Now.AddDays(startOffset!.Value), Now.AddDays(endOffset!.Value));
            db.Add(membership);
            db.Add(new Payment
            {
                UserId = userId, MembershipId = membership.Id,
                Status = status == MembershipStatus.Pending ? PaymentStatus.Failed : PaymentStatus.Paid,
                Amount = 99m, CurrencyCode = "INR"
            });
            await db.SaveChangesAsync();
        }

        var authorization = AuthorizationContext(userId);
        await new ActiveInterviewInsightsMembershipHandler(new MembershipRepository(db, new FixedTimeProvider()))
            .HandleAsync(authorization);
        Assert.False(authorization.HasSucceeded);

        var http = new DefaultHttpContext();
        http.User = Candidate(userId);
        http.Response.Body = new MemoryStream();
        var failure = AuthorizationFailure.Failed([new ActiveInterviewInsightsMembershipRequirement()]);
        await new InterviewInsightsAuthorizationResultHandler().HandleAsync(
            _ => Task.CompletedTask, http, new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build(),
            PolicyAuthorizationResult.Forbid(failure));
        Assert.Equal(StatusCodes.Status403Forbidden, http.Response.StatusCode);
        http.Response.Body.Position = 0;
        var error = await JsonSerializer.DeserializeAsync<ApiError>(http.Response.Body, WebJson);
        Assert.Equal("membership_required", error?.Code);
        Assert.Equal(InterviewInsightsMembershipPolicy.ErrorMessage, error?.Message);
        var json = JsonSerializer.Serialize(error);
        Assert.DoesNotContain("membershipId", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("payment", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(userId.ToString(), json, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("Search", false, true)]
    [InlineData("Create", false, true)]
    [InlineData("Companies", false, true)]
    [InlineData("Get", false, false)]
    [InlineData("Search", true, true)]
    [InlineData("Create", true, true)]
    [InlineData("Get", true, true)]
    public async Task InsightActionsEnforceMembershipOnlyWhereRequired(string action, bool premium, bool allowed)
    {
        var userId = Guid.NewGuid();
        await using var db = Db();
        if (premium)
        {
            var membership = Membership(userId, MembershipStatus.Active, Now.AddDays(-1), Now.AddDays(29));
            db.Add(membership);
            db.Add(new Payment { UserId = userId, MembershipId = membership.Id, Status = PaymentStatus.Paid,
                Amount = 99m, CurrencyCode = "INR", PaidAtUtc = Now.AddDays(-1) });
            await db.SaveChangesAsync();
        }
        var policy = await ActionPolicy(typeof(CandidateInterviewInsightsController), action);
        var context = new AuthorizationHandlerContext(policy.Requirements, Candidate(userId), new DefaultHttpContext());
        await new Microsoft.AspNetCore.Authorization.Infrastructure.PassThroughAuthorizationHandler().HandleAsync(context);
        await new ActiveInterviewInsightsMembershipHandler(new MembershipRepository(db, new FixedTimeProvider())).HandleAsync(context);
        Assert.Equal(allowed, context.HasSucceeded);
    }

    [Theory]
    [InlineData(typeof(CandidateInterviewInsightsController), "Create")]
    [InlineData(typeof(CandidateInterviewInsightsController), "Search")]
    [InlineData(typeof(CandidateCompaniesController), "Search")]
    [InlineData(typeof(CandidateCompaniesController), "Create")]
    public async Task FreeActionsStillRequireAuthenticatedCandidate(Type controller, string action)
    {
        var policy = await ActionPolicy(controller, action);
        Assert.DoesNotContain(policy.Requirements, r => r is ActiveInterviewInsightsMembershipRequirement);
        foreach (var user in new[] { new ClaimsPrincipal(new ClaimsIdentity()),
            new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, "Employer")], "test")) })
        {
            var context = new AuthorizationHandlerContext(policy.Requirements, user, null);
            await new Microsoft.AspNetCore.Authorization.Infrastructure.PassThroughAuthorizationHandler().HandleAsync(context);
            Assert.False(context.HasSucceeded);
        }
    }

    [Theory]
    [InlineData("Get")]
    [InlineData("Update")]
    [InlineData("Delete")]
    [InlineData("CreateSchedule")]
    [InlineData("Schedules")]
    [InlineData("UpdateSchedule")]
    [InlineData("Feedback")]
    [InlineData("Report")]
    [InlineData("Contributions")]
    [InlineData("CompanySummary")]
    public async Task OtherActionsRetainMembershipRequirement(string action)
    {
        var policy = await ActionPolicy(typeof(CandidateInterviewInsightsController), action);
        Assert.Contains(policy.Requirements, r => r is ActiveInterviewInsightsMembershipRequirement);
    }

    private static async Task<AuthorizationPolicy> ActionPolicy(Type controller, string action)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorization(options => options.AddPolicy(InterviewInsightsMembershipPolicy.Name,
            policy => policy.RequireAuthenticatedUser().RequireRole("Candidate")
                .AddRequirements(new ActiveInterviewInsightsMembershipRequirement())));
        using var provider = services.BuildServiceProvider();
        var method = controller.GetMethod(action)!;
        Assert.Empty(method.GetCustomAttributes(typeof(AllowAnonymousAttribute), true));
        var attributes = controller.GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Concat(method.GetCustomAttributes(typeof(AuthorizeAttribute), true)).Cast<AuthorizeAttribute>();
        return (await AuthorizationPolicy.CombineAsync(provider.GetRequiredService<IAuthorizationPolicyProvider>(), attributes))!;
    }

    [Fact]
    public void AdministratorInsightRoutesRetainAdministratorOnlyAuthorization()
    {
        var attribute = Assert.Single(typeof(AdminInterviewInsightsController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>());
        Assert.Equal("Administrator", attribute.Roles);
        Assert.Null(attribute.Policy);
    }

    private static AuthorizationHandlerContext AuthorizationContext(Guid userId) => new(
        [new ActiveInterviewInsightsMembershipRequirement()], Candidate(userId), new DefaultHttpContext());

    private static ClaimsPrincipal Candidate(Guid userId) => new(new ClaimsIdentity(
        [new Claim(ClaimTypes.NameIdentifier, userId.ToString()), new Claim(ClaimTypes.Role, "Candidate")], "test"));

    private static Membership Membership(Guid userId, MembershipStatus status, DateTime starts, DateTime ends) => new()
    {
        UserId = userId,
        PlanCode = InterviewInsightsMembershipPolicy.RequiredPlanCode,
        PlanName = "Job Application Access",
        Status = status,
        StartsAtUtc = starts,
        EndsAtUtc = ends
    };
    private static JobPortalDbContext Db() => new(new DbContextOptionsBuilder<JobPortalDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(Now);
    }
}
