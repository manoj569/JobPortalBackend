using JobPortal.API.Extensions;
using JobPortal.Application.Abstractions.InterviewInsights;
using JobPortal.Application.Features.InterviewInsights;
using JobPortal.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JobPortal.API.Controllers;

[ApiController]
[Authorize(Roles = "Administrator")]
[Route("api/admin")]
[Produces("application/json")]
public sealed class AdminInterviewInsightsController(IAdminInterviewInsightService insights) : ControllerBase
{
    [HttpGet("interview-insights")]
    public async Task<ActionResult<ApiResponse<PagedResponse<InterviewInsightResponse>>>> Search([FromQuery] AdminInterviewInsightQuery query, CancellationToken ct) =>
        Ok(new ApiResponse<PagedResponse<InterviewInsightResponse>>(await insights.SearchAsync(query, ct)));

    [HttpPatch("interview-insights/{id:guid}/moderation")]
    public async Task<ActionResult<ApiResponse<InterviewInsightResponse>>> Moderate(Guid id, ModerateInterviewInsightRequest request, CancellationToken ct) =>
        Ok(new ApiResponse<InterviewInsightResponse>(await insights.ModerateAsync(User.GetRequiredUserId(), id, request, ct)));

    [HttpGet("interview-insight-reports")]
    public async Task<ActionResult<ApiResponse<PagedResponse<AdminInsightReportResponse>>>> Reports([FromQuery] AdminInsightReportQuery query, CancellationToken ct) =>
        Ok(new ApiResponse<PagedResponse<AdminInsightReportResponse>>(await insights.ReportsAsync(query, ct)));

    [HttpPatch("interview-insight-reports/{id:guid}")]
    public async Task<ActionResult<ApiResponse<AdminInsightReportResponse>>> ModerateReport(Guid id, ModerateInsightReportRequest request, CancellationToken ct) =>
        Ok(new ApiResponse<AdminInsightReportResponse>(await insights.ModerateReportAsync(User.GetRequiredUserId(), id, request, ct)));
}
