using JobPortal.API.Extensions;
using JobPortal.API.Authorization;
using JobPortal.Application.Abstractions.InterviewInsights;
using JobPortal.Application.Abstractions.CandidateCompanies;
using JobPortal.Application.Features.CandidateCompanies;
using JobPortal.Application.Features.InterviewInsights;
using JobPortal.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JobPortal.API.Controllers;

[ApiController]
[Authorize(Policy = InterviewInsightsMembershipPolicy.Name)]
[Route("api/candidate")]
[Produces("application/json")]
public sealed class CandidateInterviewInsightsController(IInterviewInsightService insights, ICandidateCompanyService companies) : ControllerBase
{
    [HttpPost("interview-insights")]
    public async Task<ActionResult<ApiResponse<InterviewInsightResponse>>> Create(CreateInterviewInsightRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, new ApiResponse<InterviewInsightResponse>(
            await insights.CreateAsync(User.GetRequiredUserId(), request, ct), "Your experience is pending administrator review."));

    [HttpGet("interview-insights")]
    public async Task<ActionResult<ApiResponse<PagedResponse<InterviewInsightCardResponse>>>> Search([FromQuery] InterviewInsightQuery query, CancellationToken ct) =>
        Ok(new ApiResponse<PagedResponse<InterviewInsightCardResponse>>(await insights.SearchAsync(User.GetRequiredUserId(), query, ct)));

    [HttpGet("interview-insights/companies")]
    public async Task<ActionResult<ApiResponse<IReadOnlyCollection<CompanyOption>>>> Companies(
        [FromQuery] string query, [FromQuery] int limit = 10, CancellationToken ct = default) =>
        Ok(new ApiResponse<IReadOnlyCollection<CompanyOption>>(
            await companies.SearchAsync(User.GetRequiredUserId(), query, limit, ct)));

    [HttpGet("interview-insights/{id:guid}")]
    public async Task<ActionResult<ApiResponse<InterviewInsightResponse>>> Get(Guid id, CancellationToken ct) =>
        Ok(new ApiResponse<InterviewInsightResponse>(await insights.GetAsync(User.GetRequiredUserId(), id, ct)));

    [HttpPatch("interview-insights/{id:guid}")]
    public async Task<ActionResult<ApiResponse<InterviewInsightResponse>>> Update(Guid id, UpdateInterviewInsightRequest request, CancellationToken ct) =>
        Ok(new ApiResponse<InterviewInsightResponse>(await insights.UpdateAsync(User.GetRequiredUserId(), id, request, ct),
            "Your updated experience is pending administrator review."));

    [HttpDelete("interview-insights/{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await insights.DeleteAsync(User.GetRequiredUserId(), id, ct);
        return NoContent();
    }

    [HttpPost("interview-schedules")]
    public async Task<ActionResult<ApiResponse<InterviewScheduleResponse>>> CreateSchedule(CreateInterviewScheduleRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, new ApiResponse<InterviewScheduleResponse>(await insights.CreateScheduleAsync(User.GetRequiredUserId(), request, ct)));

    [HttpGet("interview-schedules")]
    public async Task<ActionResult<ApiResponse<IReadOnlyCollection<InterviewScheduleResponse>>>> Schedules(CancellationToken ct) =>
        Ok(new ApiResponse<IReadOnlyCollection<InterviewScheduleResponse>>(await insights.GetSchedulesAsync(User.GetRequiredUserId(), ct)));

    [HttpPatch("interview-schedules/{id:guid}")]
    public async Task<ActionResult<ApiResponse<InterviewScheduleResponse>>> UpdateSchedule(Guid id, UpdateInterviewScheduleRequest request, CancellationToken ct) =>
        Ok(new ApiResponse<InterviewScheduleResponse>(await insights.UpdateScheduleAsync(User.GetRequiredUserId(), id, request, ct)));

    [HttpPost("interview-insights/{id:guid}/feedback")]
    public async Task<ActionResult<ApiResponse<InsightFeedbackResponse>>> Feedback(Guid id, CreateInsightFeedbackRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, new ApiResponse<InsightFeedbackResponse>(await insights.AddFeedbackAsync(User.GetRequiredUserId(), id, request, ct),
            "Thanks. Your feedback improves the quality of Interview Insights."));

    [HttpPost("interview-insights/{id:guid}/report")]
    public async Task<ActionResult<ApiResponse<InsightReportResponse>>> Report(Guid id, CreateInsightReportRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, new ApiResponse<InsightReportResponse>(await insights.ReportAsync(User.GetRequiredUserId(), id, request, ct)));

    [HttpGet("interview-insights/my-contributions")]
    public async Task<ActionResult<ApiResponse<MyInterviewContributionsResponse>>> Contributions(CancellationToken ct) =>
        Ok(new ApiResponse<MyInterviewContributionsResponse>(await insights.ContributionsAsync(User.GetRequiredUserId(), ct)));

    [HttpGet("interview-insights/company/{companyId:guid}/summary")]
    public async Task<ActionResult<ApiResponse<CompanyInterviewInsightSummaryResponse>>> CompanySummary(Guid companyId, CancellationToken ct) =>
        Ok(new ApiResponse<CompanyInterviewInsightSummaryResponse>(await insights.CompanySummaryAsync(User.GetRequiredUserId(), companyId, ct)));
}
