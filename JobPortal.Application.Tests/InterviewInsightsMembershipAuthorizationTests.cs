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
        Assert.Equal("An active membership is required to use Interview Insights.", error?.Message);
        var json = JsonSerializer.Serialize(error);
        Assert.DoesNotContain("membershipId", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("payment", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(userId.ToString(), json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EveryCandidateInsightScheduleAndCompanyActionUsesSharedPolicy()
    {
        AssertPolicy(typeof(CandidateInterviewInsightsController));
        AssertPolicy(typeof(CandidateCompaniesController));
    }

    [Fact]
    public void AdministratorInsightRoutesRetainAdministratorOnlyAuthorization()
    {
        var attribute = Assert.Single(typeof(AdminInterviewInsightsController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>());
        Assert.Equal("Administrator", attribute.Roles);
        Assert.Null(attribute.Policy);
    }

    private static void AssertPolicy(Type controller)
    {
        var attribute = Assert.Single(controller.GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<AuthorizeAttribute>());
        Assert.Equal(InterviewInsightsMembershipPolicy.Name, attribute.Policy);
        Assert.Null(attribute.Roles);
        Assert.All(controller.GetMethods().Where(x => x.IsPublic && x.DeclaringType == controller &&
            x.GetCustomAttributes(typeof(HttpMethodAttribute), true).Length > 0),
            action => Assert.Empty(action.GetCustomAttributes(typeof(AllowAnonymousAttribute), true)));
    }

    private static AuthorizationHandlerContext AuthorizationContext(Guid userId) => new(
        [new ActiveInterviewInsightsMembershipRequirement()], Candidate(userId), new DefaultHttpContext());

    private static ClaimsPrincipal Candidate(Guid userId) => new(new ClaimsIdentity(
        [new Claim(ClaimTypes.NameIdentifier, userId.ToString()), new Claim(ClaimTypes.Role, "Candidate")], "test"));

    private static Membership Membership(Guid userId, MembershipStatus status, DateTime starts, DateTime ends) => new()
    {
        UserId = userId, PlanName = "Career Harbor 30-Day Access", Status = status,
        StartsAtUtc = starts, EndsAtUtc = ends
    };

    private static JobPortalDbContext Db() => new(new DbContextOptionsBuilder<JobPortalDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(Now);
    }
}
