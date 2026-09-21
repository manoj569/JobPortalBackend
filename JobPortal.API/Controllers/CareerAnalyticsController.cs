using JobPortal.API.Extensions;
using JobPortal.Application.Abstractions.Persistence;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Application.Features.CareerGuidance;
using JobPortal.Domain.Enums;
using JobPortal.Persistence.Repositories;
using JobPortal.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JobPortal.API.Controllers;

[ApiController, Authorize(Roles = "Administrator"), Route("api/admin/career-guidance/analytics")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AdminCareerAnalyticsController(CareerAnalyticsRepository analytics, TimeProvider clock, IUserRepository users) : ControllerBase
{
    [HttpGet("overview")]
    public async Task<ActionResult<ApiResponse<CareerGuidanceAdminAnalytics>>> Overview([FromQuery] CareerAnalyticsQuery query, CancellationToken ct)
    {
        await CareerAnalyticsAccess.RequireAsync(users, User.GetRequiredUserId(), "Administrator", ct);
        var (from, to) = CareerAnalyticsAccess.Range(query, clock);
        return Ok(new ApiResponse<CareerGuidanceAdminAnalytics>(await analytics.AdminAsync(from, to, ct)));
    }
}

[ApiController, Authorize, Route("api/career-guidance/me")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class CareerOwnerAnalyticsController(CareerAnalyticsRepository analytics, TimeProvider clock, IUserRepository users) : ControllerBase
{
    [HttpGet("analytics")]
    public async Task<ActionResult<ApiResponse<CareerGuidanceConsultantAnalytics>>> Consultant([FromQuery] CareerAnalyticsQuery query, CancellationToken ct)
    {
        var actor = User.GetRequiredUserId();
        await CareerAnalyticsAccess.RequireAsync(users, actor, null, ct);
        var (from, to) = CareerAnalyticsAccess.Range(query, clock);
        var result = await analytics.ConsultantAsync(actor, from, to, ct);
        return result is null ? NotFound() : Ok(new ApiResponse<CareerGuidanceConsultantAnalytics>(result));
    }
    [HttpGet("summary")]
    [Authorize(Roles = "Candidate")]
    public async Task<ActionResult<ApiResponse<CareerGuidanceCandidateSummary>>> Summary(CancellationToken ct)
    {
        var actor = User.GetRequiredUserId();
        await CareerAnalyticsAccess.RequireAsync(users, actor, "Candidate", ct);
        return Ok(new ApiResponse<CareerGuidanceCandidateSummary>(await analytics.CandidateAsync(actor, ct)));
    }
}

internal static class CareerAnalyticsAccess
{
    internal static async Task RequireAsync(IUserRepository users, Guid actor, string? role, CancellationToken ct)
    {
        var user = await users.GetByIdWithRoleAsync(actor, ct);
        if (user is null || user.IsDeleted || user.Status != UserStatus.Active) throw new UnauthorizedException();
        if (role is not null && user.Role.Name != role) throw new AppException("Access denied.", 403, "forbidden");
    }
    internal static (DateTime From, DateTime To) Range(CareerAnalyticsQuery query, TimeProvider clock)
    {
        try { return query.Normalize(clock.GetUtcNow().UtcDateTime); }
        catch (ArgumentException) { throw new BadRequestException("Analytics UTC range must be positive and no longer than 366 days."); }
    }
}
