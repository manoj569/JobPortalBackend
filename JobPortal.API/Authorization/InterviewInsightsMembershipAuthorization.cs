using System.Security.Claims;
using JobPortal.Application.Abstractions.Persistence;
using JobPortal.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;

namespace JobPortal.API.Authorization;

public static class InterviewInsightsMembershipPolicy
{
    public const string Name = "ActiveInterviewInsightsMembership";
    public const string ErrorCode = "membership_required";
    public const string ErrorMessage = "An active membership is required to use Interview Insights.";
}

public sealed class ActiveInterviewInsightsMembershipRequirement : IAuthorizationRequirement;

public sealed class ActiveInterviewInsightsMembershipHandler(IMembershipRepository memberships) :
    AuthorizationHandler<ActiveInterviewInsightsMembershipRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ActiveInterviewInsightsMembershipRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated != true || !context.User.IsInRole("Candidate"))
        {
            context.Succeed(requirement);
            return;
        }

        var value = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        var cancellationToken = context.Resource is HttpContext httpContext
            ? httpContext.RequestAborted
            : CancellationToken.None;
        if (Guid.TryParse(value, out var userId) &&
            await memberships.GetActiveForUserAsync(userId, cancellationToken) is not null)
            context.Succeed(requirement);
    }
}

public sealed class InterviewInsightsAuthorizationResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler fallback = new();

    public async Task HandleAsync(
        RequestDelegate next,
        HttpContext context,
        AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        if (authorizeResult.Forbidden && context.User.IsInRole("Candidate") &&
            authorizeResult.AuthorizationFailure?.FailedRequirements
                .OfType<ActiveInterviewInsightsMembershipRequirement>().Any() == true)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(new ApiError(
                InterviewInsightsMembershipPolicy.ErrorCode,
                InterviewInsightsMembershipPolicy.ErrorMessage), context.RequestAborted);
            return;
        }

        await fallback.HandleAsync(next, context, policy, authorizeResult);
    }
}
