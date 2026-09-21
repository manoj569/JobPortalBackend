using JobPortal.API.Extensions;
using JobPortal.Application.Features.CareerGuidance;
using JobPortal.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JobPortal.API.Controllers;

[ApiController, Authorize(Roles = "Candidate"), Route("api/career-guidance")]
public sealed class CareerPaymentsController(ICareerFinanceService service) : ControllerBase
{
    [HttpPost("bookings/{bookingId:guid}/payment/order")]
    public async Task<ActionResult<ApiResponse<CareerCheckout>>> Order(Guid bookingId, CancellationToken ct) => Ok(new ApiResponse<CareerCheckout>(await service.OrderAsync(User.GetRequiredUserId(), bookingId, ct)));
    [HttpPost("bookings/{bookingId:guid}/payment/verify")]
    public async Task<ActionResult<ApiResponse<CareerPaymentResponse>>> Verify(Guid bookingId, CareerVerifyRequest request, CancellationToken ct) => Ok(new ApiResponse<CareerPaymentResponse>(await service.VerifyAsync(User.GetRequiredUserId(), bookingId, request, ct)));
    [HttpGet("bookings/{bookingId:guid}/payment")]
    public async Task<ActionResult<ApiResponse<CareerPaymentResponse>>> Get(Guid bookingId, CancellationToken ct) => Ok(new ApiResponse<CareerPaymentResponse>(await service.GetAsync(User.GetRequiredUserId(), bookingId, false, ct)));
    [HttpPost("bookings/{bookingId:guid}/payment/reconcile")]
    public async Task<ActionResult<ApiResponse<CareerPaymentResponse>>> Reconcile(Guid bookingId, CancellationToken ct) => Ok(new ApiResponse<CareerPaymentResponse>(await service.ReconcileAsync(User.GetRequiredUserId(), bookingId, ct)));
    [HttpGet("payments/mine")]
    public async Task<ActionResult<ApiResponse<PagedResponse<CareerPaymentResponse>>>> Payments([FromQuery] FinanceQuery query, CancellationToken ct) => Ok(new ApiResponse<PagedResponse<CareerPaymentResponse>>(await service.PaymentsAsync(User.GetRequiredUserId(), false, query, ct)));
    [HttpGet("refunds/mine")]
    public async Task<ActionResult<ApiResponse<PagedResponse<CareerRefundResponse>>>> Refunds([FromQuery] FinanceQuery query, CancellationToken ct) => Ok(new ApiResponse<PagedResponse<CareerRefundResponse>>(await service.RefundsAsync(User.GetRequiredUserId(), false, query, ct)));
}

[ApiController, Authorize, Route("api/career-guidance/me/earnings")]
public sealed class CareerEarningsController(ICareerFinanceService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResponse<CareerEarningResponse>>>> Get([FromQuery] FinanceQuery query, CancellationToken ct) => Ok(new ApiResponse<PagedResponse<CareerEarningResponse>>(await service.EarningsAsync(User.GetRequiredUserId(), false, query, ct)));
    [HttpGet("summary")]
    public async Task<ActionResult<ApiResponse<IReadOnlyCollection<CareerEarningSummary>>>> Summary(CancellationToken ct) => Ok(new ApiResponse<IReadOnlyCollection<CareerEarningSummary>>(await service.SummaryAsync(User.GetRequiredUserId(), ct)));
}

[ApiController, Authorize(Roles = "Administrator"), Route("api/admin/career-guidance")]
public sealed class AdminCareerFinanceController(ICareerFinanceService service) : ControllerBase
{
    [HttpGet("payments")]
    public async Task<ActionResult<ApiResponse<PagedResponse<CareerPaymentResponse>>>> Payments([FromQuery] FinanceQuery query, CancellationToken ct) => Ok(new ApiResponse<PagedResponse<CareerPaymentResponse>>(await service.PaymentsAsync(User.GetRequiredUserId(), true, query, ct)));
    [HttpGet("payments/{id:guid}")]
    public async Task<ActionResult<ApiResponse<CareerPaymentResponse>>> Payment(Guid id, CancellationToken ct) => Ok(new ApiResponse<CareerPaymentResponse>(await service.GetAsync(User.GetRequiredUserId(), id, true, ct)));
    [HttpGet("refunds")]
    public async Task<ActionResult<ApiResponse<PagedResponse<CareerRefundResponse>>>> Refunds([FromQuery] FinanceQuery query, CancellationToken ct) => Ok(new ApiResponse<PagedResponse<CareerRefundResponse>>(await service.RefundsAsync(User.GetRequiredUserId(), true, query, ct)));
    [HttpGet("refunds/{id:guid}")]
    public async Task<ActionResult<ApiResponse<CareerRefundResponse>>> Refund(Guid id, CancellationToken ct) => Ok(new ApiResponse<CareerRefundResponse>(await service.RefundDetailAsync(User.GetRequiredUserId(), id, ct)));
    [HttpPost("refunds/{paymentId:guid}")]
    public async Task<ActionResult<ApiResponse<CareerRefundResponse>>> Refund(Guid paymentId, CareerRefundRequest request, CancellationToken ct) => Ok(new ApiResponse<CareerRefundResponse>(await service.RefundAsync(User.GetRequiredUserId(), paymentId, request, ct)));
    [HttpGet("earnings")]
    public async Task<ActionResult<ApiResponse<PagedResponse<CareerEarningResponse>>>> Earnings([FromQuery] FinanceQuery query, CancellationToken ct) => Ok(new ApiResponse<PagedResponse<CareerEarningResponse>>(await service.EarningsAsync(User.GetRequiredUserId(), true, query, ct)));
}
