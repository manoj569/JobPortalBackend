using JobPortal.API.Extensions;
using JobPortal.Application.Features.CareerGuidance;
using JobPortal.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JobPortal.API.Controllers;

[ApiController, Authorize, Route("api/career-guidance/me/availability")]
public sealed class CareerAvailabilityController(ICareerSchedulingService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse<AvailabilityResponse>>> Get(CancellationToken ct) => Ok(new ApiResponse<AvailabilityResponse>(await service.AvailabilityAsync(User.GetRequiredUserId(), ct)));
    [HttpPut]
    public async Task<ActionResult<ApiResponse<AvailabilityResponse>>> Save(SaveAvailabilityRequest request, CancellationToken ct) => Ok(new ApiResponse<AvailabilityResponse>(await service.SaveAvailabilityAsync(User.GetRequiredUserId(), request, ct)));
    [HttpGet("exceptions")]
    public async Task<ActionResult<ApiResponse<IReadOnlyCollection<AvailabilityExceptionResponse>>>> Exceptions([FromQuery] SlotQuery query, CancellationToken ct) => Ok(new ApiResponse<IReadOnlyCollection<AvailabilityExceptionResponse>>(await service.ExceptionsAsync(User.GetRequiredUserId(), query, ct)));
    [HttpPost("exceptions")]
    public async Task<ActionResult<ApiResponse<ExceptionChangeResponse>>> AddException(AvailabilityExceptionRequest request, CancellationToken ct) => StatusCode(201, new ApiResponse<ExceptionChangeResponse>(await service.AddExceptionAsync(User.GetRequiredUserId(), request, ct)));
    [HttpDelete("exceptions/{id:guid}")]
    public async Task<ActionResult<ApiResponse<ExceptionChangeResponse>>> DeleteException(Guid id, [FromQuery] Guid revision, CancellationToken ct) => Ok(new ApiResponse<ExceptionChangeResponse>(await service.DeleteExceptionAsync(User.GetRequiredUserId(), id, revision, ct)));
}

[ApiController, AllowAnonymous, Route("api/career-guidance/consultants/{consultantId:guid}/services/{serviceId:guid}/slots")]
public sealed class CareerSlotsController(ICareerSchedulingService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse<SlotResponse>>> Get(Guid consultantId, Guid serviceId, [FromQuery] SlotQuery query, CancellationToken ct) => Ok(new ApiResponse<SlotResponse>(await service.SlotsAsync(consultantId, serviceId, query, ct)));
}

[ApiController, Authorize, Route("api/career-guidance/bookings")]
public sealed class CareerBookingsController(ICareerSchedulingService service) : ControllerBase
{
    [HttpPost]
    [Authorize(Roles = "Candidate")]
    public async Task<ActionResult<ApiResponse<CareerBookingResponse>>> Create(CreateCareerBookingRequest request, CancellationToken ct) => StatusCode(201, new ApiResponse<CareerBookingResponse>(await service.CreateAsync(User.GetRequiredUserId(), request, ct)));
    [HttpGet("mine")]
    public async Task<ActionResult<ApiResponse<PagedResponse<CareerBookingResponse>>>> Mine([FromQuery] BookingQuery query, CancellationToken ct) => Ok(new ApiResponse<PagedResponse<CareerBookingResponse>>(await service.BookingsAsync(User.GetRequiredUserId(), false, false, query, ct)));
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ApiResponse<CareerBookingResponse>>> Get(Guid id, CancellationToken ct) => Ok(new ApiResponse<CareerBookingResponse>(await service.GetAsync(User.GetRequiredUserId(), id, false, false, ct)));
    [HttpPost("{id:guid}/cancel")]
    public async Task<ActionResult<ApiResponse<CareerBookingResponse>>> Cancel(Guid id, BookingActionRequest request, CancellationToken ct) => Ok(new ApiResponse<CareerBookingResponse>(await service.CancelAsync(User.GetRequiredUserId(), id, false, request, ct)));
}

[ApiController, Authorize, Route("api/career-guidance/me/bookings")]
public sealed class CareerConsultantBookingsController(ICareerSchedulingService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResponse<CareerBookingResponse>>>> Mine([FromQuery] BookingQuery query, CancellationToken ct) => Ok(new ApiResponse<PagedResponse<CareerBookingResponse>>(await service.BookingsAsync(User.GetRequiredUserId(), true, false, query, ct)));
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ApiResponse<CareerBookingResponse>>> Get(Guid id, CancellationToken ct) => Ok(new ApiResponse<CareerBookingResponse>(await service.GetAsync(User.GetRequiredUserId(), id, true, false, ct)));
    [HttpPost("{id:guid}/cancel")]
    public async Task<ActionResult<ApiResponse<CareerBookingResponse>>> Cancel(Guid id, BookingActionRequest request, CancellationToken ct) => Ok(new ApiResponse<CareerBookingResponse>(await service.CancelAsync(User.GetRequiredUserId(), id, true, request, ct)));
    [HttpPost("{id:guid}/status")]
    public async Task<ActionResult<ApiResponse<CareerBookingResponse>>> Status(Guid id, BookingStatusRequest request, CancellationToken ct) => Ok(new ApiResponse<CareerBookingResponse>(await service.SetStatusAsync(User.GetRequiredUserId(), id, request, ct)));
}

[ApiController, Authorize(Roles = "Administrator"), Route("api/admin/career-guidance/bookings")]
public sealed class AdminCareerBookingsController(ICareerSchedulingService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResponse<CareerBookingResponse>>>> Search([FromQuery] BookingQuery query, CancellationToken ct) => Ok(new ApiResponse<PagedResponse<CareerBookingResponse>>(await service.BookingsAsync(User.GetRequiredUserId(), false, true, query, ct)));
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ApiResponse<CareerBookingResponse>>> Get(Guid id, CancellationToken ct) => Ok(new ApiResponse<CareerBookingResponse>(await service.GetAsync(User.GetRequiredUserId(), id, false, true, ct)));
}
