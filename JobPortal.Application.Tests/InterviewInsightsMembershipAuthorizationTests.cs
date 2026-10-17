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
using Microsoft.AspNetCore.Authentication;
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
    public async Task RetainedMembershipPolicyRejectsMissingExpiredAndInvalidAccess(
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

    public static TheoryData<string, bool> InsightActionsWithMembership
    {
        get
        {
            var data = new TheoryData<string, bool>();
            foreach (var action in InsightActionNames)
            {
                data.Add(action, false);
                data.Add(action, true);
            }
            return data;
        }
    }

    public static TheoryData<string> InsightActions
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var action in InsightActionNames) data.Add(action);
            return data;
        }
    }

    private static readonly string[] InsightActionNames =
    [
        "Create", "Search", "Companies", "Get", "Update", "Delete", "CreateSchedule",
        "Schedules", "UpdateSchedule", "Feedback", "Report", "Contributions", "CompanySummary"
    ];

    [Fact]
    public void MatrixIncludesEveryCandidateInsightEndpoint()
    {
        var actual = typeof(CandidateInterviewInsightsController).GetMethods()
            .Where(m => m.GetCustomAttributes(typeof(HttpMethodAttribute), true).Length > 0)
            .Select(m => m.Name).Order().ToArray();
        Assert.Equal(13, actual.Length);
        Assert.Equal(InsightActionNames.Order(), actual);
    }

    [Theory]
    [MemberData(nameof(InsightActionsWithMembership))]
    public async Task EveryInsightActionAllowsCandidatesWithOrWithoutMembership(string action, bool member)
    {
        var userId = Guid.NewGuid();
        await using var db = Db();
        if (member)
        {
            var membership = Membership(userId, MembershipStatus.Active, Now.AddDays(-1), Now.AddDays(29));
            db.Add(membership);
            db.Add(new Payment { UserId = userId, MembershipId = membership.Id, Status = PaymentStatus.Paid,
                Amount = 99m, CurrencyCode = "INR", PaidAtUtc = Now.AddDays(-1) });
            await db.SaveChangesAsync();
        }
        var policy = await ActionPolicy(typeof(CandidateInterviewInsightsController), action);
        Assert.DoesNotContain(policy.Requirements, r => r is ActiveInterviewInsightsMembershipRequirement);
        var called = false;
        var http = await ExecutePolicyAsync(policy, Candidate(userId), db, _ =>
        {
            called = true;
            return Task.CompletedTask;
        });
        Assert.True(called);
        Assert.Equal(StatusCodes.Status200OK, http.Response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(InsightActions))]
    public async Task EveryInsightActionStillChallengesAnonymousAndForbidsWrongRoles(string action)
    {
        await using var db = Db();
        var policy = await ActionPolicy(typeof(CandidateInterviewInsightsController), action);
        foreach (var role in new[] { "", "Employer", "Administrator" })
        {
            var user = role.Length == 0 ? new ClaimsPrincipal(new ClaimsIdentity()) : new ClaimsPrincipal(
                new ClaimsIdentity([new Claim(ClaimTypes.Role, role), new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())], "test"));
            var http = await ExecutePolicyAsync(policy, user, db, _ => throw new InvalidOperationException("Unauthorized action ran."));
            Assert.Equal(role.Length == 0 ? StatusCodes.Status401Unauthorized : StatusCodes.Status403Forbidden, http.Response.StatusCode);
            http.Response.Body.Position = 0;
            Assert.DoesNotContain("membership_required", await new StreamReader(http.Response.Body).ReadToEndAsync());
        }
    }

    [Theory]
    [InlineData("Search")]
    [InlineData("Create")]
    public async Task CandidateCompanyActionsRetainAuthenticatedCandidateAuthorization(string action)
    {
        await using var db = Db();
        var policy = await ActionPolicy(typeof(CandidateCompaniesController), action);
        foreach (var role in new[] { "", "Employer", "Administrator", "Candidate" })
        {
            var user = role.Length == 0 ? new ClaimsPrincipal(new ClaimsIdentity()) : new ClaimsPrincipal(
                new ClaimsIdentity([new Claim(ClaimTypes.Role, role), new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())], "test"));
            var called = false;
            var http = await ExecutePolicyAsync(policy, user, db, _ => { called = true; return Task.CompletedTask; });
            Assert.Equal(role == "Candidate", called);
            Assert.Equal(role == "Candidate" ? StatusCodes.Status200OK : role.Length == 0
                ? StatusCodes.Status401Unauthorized : StatusCodes.Status403Forbidden, http.Response.StatusCode);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RetainedMembershipPolicyStillProtectsAnUnrelatedControlRoute(bool member)
    {
        var userId = Guid.NewGuid();
        await using var db = Db();
        if (member)
        {
            var membership = Membership(userId, MembershipStatus.Active, Now.AddDays(-1), Now.AddDays(29));
            db.Add(membership);
            db.Add(new Payment { UserId = userId, MembershipId = membership.Id, Status = PaymentStatus.Paid,
                Amount = 99m, CurrencyCode = "INR", PaidAtUtc = Now.AddDays(-1) });
            await db.SaveChangesAsync();
        }
        // A test-only route outside Insights exercises the unchanged registered requirement/result handler.
        // Real job application access uses its existing free/paid quotas, not this error contract.
        var policy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().RequireRole("Candidate")
            .AddRequirements(new ActiveInterviewInsightsMembershipRequirement()).Build();
        var called = false;
        var http = await ExecutePolicyAsync(policy, Candidate(userId), db, _ =>
        {
            called = true;
            return Task.CompletedTask;
        }, "/api/auth-test/job-application-access");
        Assert.Equal(member, called);
        Assert.Equal(member ? StatusCodes.Status200OK : StatusCodes.Status403Forbidden, http.Response.StatusCode);
        if (!member)
        {
            http.Response.Body.Position = 0;
            var error = await JsonSerializer.DeserializeAsync<ApiError>(http.Response.Body, WebJson);
            Assert.Equal("membership_required", error?.Code);
        }
    }

    internal static async Task<DefaultHttpContext> ExecuteActionAsync(string action, Guid userId, RequestDelegate next)
    {
        await using var db = Db();
        return await ExecutePolicyAsync(await ActionPolicy(typeof(CandidateInterviewInsightsController), action), Candidate(userId), db, next);
    }

    private static async Task<DefaultHttpContext> ExecutePolicyAsync(AuthorizationPolicy policy, ClaimsPrincipal user,
        JobPortalDbContext db, RequestDelegate next, string path = "/api/candidate/interview-insights")
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorization();
        services.AddSingleton<JobPortal.Application.Abstractions.Persistence.IMembershipRepository>(new MembershipRepository(db, new FixedTimeProvider()));
        services.AddSingleton<IAuthorizationHandler, ActiveInterviewInsightsMembershipHandler>();
        services.AddSingleton<IAuthenticationService, StatusAuthenticationService>();
        using var provider = services.BuildServiceProvider();
        var http = new DefaultHttpContext { User = user, RequestServices = provider };
        http.Request.Path = path;
        http.Response.Body = new MemoryStream();
        var authorization = await provider.GetRequiredService<IAuthorizationService>().AuthorizeAsync(user, http, policy);
        var result = authorization.Succeeded ? PolicyAuthorizationResult.Success() : user.Identity?.IsAuthenticated == true
            ? PolicyAuthorizationResult.Forbid(authorization.Failure!) : PolicyAuthorizationResult.Challenge();
        await new InterviewInsightsAuthorizationResultHandler().HandleAsync(next, http, policy, result);
        return http;
    }

    private sealed class StatusAuthenticationService : IAuthenticationService
    {
        public Task<AuthenticateResult> AuthenticateAsync(HttpContext context, string? scheme) => Task.FromResult(AuthenticateResult.NoResult());
        public Task ChallengeAsync(HttpContext context, string? scheme, AuthenticationProperties? properties)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        }
        public Task ForbidAsync(HttpContext context, string? scheme, AuthenticationProperties? properties)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        }
        public Task SignInAsync(HttpContext context, string? scheme, ClaimsPrincipal principal, AuthenticationProperties? properties) => throw new NotSupportedException();
        public Task SignOutAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) => throw new NotSupportedException();
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
