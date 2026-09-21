using JobPortal.API.Extensions;
using JobPortal.Application.Features.CareerGuidance;
using JobPortal.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JobPortal.API.Controllers;

[ApiController, Authorize(Roles = "Candidate"), Route("api/career-guidance/bookings/{bookingId:guid}/session")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class CareerCandidateSessionsController(CareerSessionService service) : ControllerBase
{
    [HttpPost("provision")]
    public async Task<ActionResult<ApiResponse<CareerSessionResponse>>> Provision(Guid bookingId, CancellationToken ct) =>
        Ok(new ApiResponse<CareerSessionResponse>(await service.ProvisionAsync(User.GetRequiredUserId(), bookingId, CareerSessionAudience.Candidate, ct)));
    [HttpGet]
    public async Task<ActionResult<ApiResponse<CareerSessionResponse>>> Get(Guid bookingId, CancellationToken ct) =>
        Ok(new ApiResponse<CareerSessionResponse>(await service.GetAsync(User.GetRequiredUserId(), bookingId, CareerSessionAudience.Candidate, true, ct)));
    [HttpGet("join")]
    public async Task<ActionResult<ApiResponse<CareerSessionJoinResponse>>> Join(Guid bookingId, CancellationToken ct) =>
        Ok(new ApiResponse<CareerSessionJoinResponse>(await service.JoinAsync(User.GetRequiredUserId(), bookingId, CareerSessionAudience.Candidate, true, ct)));
    [HttpPost("consultant-no-show-report")]
    public async Task<ActionResult<ApiResponse<CareerSessionResponse>>> Report(Guid bookingId, CareerSessionAction request, CancellationToken ct) =>
        Ok(new ApiResponse<CareerSessionResponse>(await service.ReportAsync(User.GetRequiredUserId(), bookingId, request, ct)));
}

[ApiController, Authorize, Route("api/career-guidance/me/sessions")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class CareerConsultantSessionsController(CareerSessionService service) : ControllerBase
{
    [HttpPost("bookings/{bookingId:guid}/provision")]
    public async Task<ActionResult<ApiResponse<CareerSessionResponse>>> Provision(Guid bookingId, CancellationToken ct) =>
        Ok(new ApiResponse<CareerSessionResponse>(await service.ProvisionAsync(User.GetRequiredUserId(), bookingId, CareerSessionAudience.Consultant, ct)));
    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResponse<CareerSessionResponse>>>> List([FromQuery] FinanceQuery query, CancellationToken ct) =>
        Ok(new ApiResponse<PagedResponse<CareerSessionResponse>>(await service.ListAsync(User.GetRequiredUserId(), false, query, ct)));
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ApiResponse<CareerSessionResponse>>> Get(Guid id, CancellationToken ct) =>
        Ok(new ApiResponse<CareerSessionResponse>(await service.GetAsync(User.GetRequiredUserId(), id, CareerSessionAudience.Consultant, false, ct)));
    [HttpGet("{id:guid}/join")]
    public async Task<ActionResult<ApiResponse<CareerSessionJoinResponse>>> Join(Guid id, CancellationToken ct) =>
        Ok(new ApiResponse<CareerSessionJoinResponse>(await service.JoinAsync(User.GetRequiredUserId(), id, CareerSessionAudience.Consultant, false, ct)));
    [HttpPost("{id:guid}/start")]
    public async Task<ActionResult<ApiResponse<CareerSessionResponse>>> Start(Guid id, CareerSessionAction request, CancellationToken ct) =>
        Ok(new ApiResponse<CareerSessionResponse>(await service.StartAsync(User.GetRequiredUserId(), id, request, ct)));
    [HttpPost("{id:guid}/complete")]
    public async Task<ActionResult<ApiResponse<CareerSessionResponse>>> Complete(Guid id, CareerSessionAction request, CancellationToken ct) =>
        Ok(new ApiResponse<CareerSessionResponse>(await service.CompleteAsync(User.GetRequiredUserId(), id, request, ct)));
    [HttpPost("{id:guid}/candidate-no-show")]
    public async Task<ActionResult<ApiResponse<CareerSessionResponse>>> NoShow(Guid id, CareerSessionAction request, CancellationToken ct) =>
        Ok(new ApiResponse<CareerSessionResponse>(await service.NoShowAsync(User.GetRequiredUserId(), id, request, false, ct)));
}

[ApiController, Authorize(Roles = "Administrator"), Route("api/admin/career-guidance/sessions")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AdminCareerSessionsController(CareerSessionService service) : ControllerBase
{
    [HttpPost("bookings/{bookingId:guid}/provision")]
    public async Task<ActionResult<ApiResponse<CareerSessionResponse>>> Provision(Guid bookingId, CancellationToken ct) =>
        Ok(new ApiResponse<CareerSessionResponse>(await service.ProvisionAsync(User.GetRequiredUserId(), bookingId, CareerSessionAudience.Administrator, ct)));
    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResponse<CareerSessionResponse>>>> List([FromQuery] FinanceQuery query, CancellationToken ct) =>
        Ok(new ApiResponse<PagedResponse<CareerSessionResponse>>(await service.ListAsync(User.GetRequiredUserId(), true, query, ct)));
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ApiResponse<CareerSessionResponse>>> Get(Guid id, CancellationToken ct) =>
        Ok(new ApiResponse<CareerSessionResponse>(await service.GetAsync(User.GetRequiredUserId(), id, CareerSessionAudience.Administrator, false, ct)));
    [HttpPost("{id:guid}/manual-meeting")]
    public async Task<ActionResult<ApiResponse<CareerSessionResponse>>> Configure(Guid id, CareerManualMeetingRequest request, CancellationToken ct) =>
        Ok(new ApiResponse<CareerSessionResponse>(await service.ConfigureAsync(User.GetRequiredUserId(), id, request, ct)));
    [HttpPost("{id:guid}/reconcile")]
    public async Task<ActionResult<ApiResponse<CareerSessionResponse>>> Reconcile(Guid id, CareerSessionAction request, CancellationToken ct) =>
        Ok(new ApiResponse<CareerSessionResponse>(await service.ReconcileAsync(User.GetRequiredUserId(), id, request, ct)));
    [HttpPost("{id:guid}/consultant-no-show")]
    public async Task<ActionResult<ApiResponse<CareerSessionResponse>>> NoShow(Guid id, CareerSessionAction request, CancellationToken ct) =>
        Ok(new ApiResponse<CareerSessionResponse>(await service.NoShowAsync(User.GetRequiredUserId(), id, request, true, ct)));
}
