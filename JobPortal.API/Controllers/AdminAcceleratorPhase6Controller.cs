using JobPortal.API.Extensions;
using JobPortal.Application.Features.CareerAccelerator;
using JobPortal.Application.Abstractions.Payments;
using JobPortal.Application.Features.Payments;
using JobPortal.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace JobPortal.API.Controllers;

[ApiController, Authorize(Roles = "Administrator")]
[Route("api/admin/accelerator/v1/operations")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[EnableRateLimiting("CareerAccelerator")]
public sealed class AdminAcceleratorPhase6Controller(AcceleratorPhase6OperationsService operations,
    IPaymentService payments) : ControllerBase
{
    [HttpGet("overview")]
    public async Task<ActionResult<ApiResponse<AcceleratorOperationalOverview>>> Overview(CancellationToken ct) =>
        Ok(new ApiResponse<AcceleratorOperationalOverview>(await operations.OverviewAsync(User.GetRequiredUserId(), ct)));

    [HttpGet("subscriptions")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<AcceleratorSubscriptionOperational>>>> Subscriptions(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        Ok(new ApiResponse<IReadOnlyList<AcceleratorSubscriptionOperational>>(
            await operations.SubscriptionsAsync(User.GetRequiredUserId(), page, pageSize, ct)));

    [HttpGet("subscriptions/{id:guid}")]
    public async Task<ActionResult<ApiResponse<AcceleratorSubscriptionOperational?>>> Subscription(
        Guid id, CancellationToken ct) =>
        Ok(new ApiResponse<AcceleratorSubscriptionOperational?>(
            await operations.SubscriptionAsync(User.GetRequiredUserId(), id, ct)));

    [HttpGet("payments")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<AcceleratorPaymentOperational>>>> Payments(
        [FromQuery] bool unresolvedOnly = false, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        Ok(new ApiResponse<IReadOnlyList<AcceleratorPaymentOperational>>(
            await operations.PaymentsAsync(User.GetRequiredUserId(), unresolvedOnly, page, pageSize, ct)));

    [HttpGet("ai/costs")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<AcceleratorAiCostOperational>>>> AiCosts(CancellationToken ct) =>
        Ok(new ApiResponse<IReadOnlyList<AcceleratorAiCostOperational>>(
            await operations.AiCostsAsync(User.GetRequiredUserId(), ct)));

    [HttpGet("ai/dispatches")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<AcceleratorDispatchOperational>>>> Dispatches(
        [FromQuery] bool unresolvedOnly = false, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        Ok(new ApiResponse<IReadOnlyList<AcceleratorDispatchOperational>>(
            await operations.DispatchesAsync(User.GetRequiredUserId(), unresolvedOnly, page, pageSize, ct)));

    [HttpGet("configuration")]
    public async Task<ActionResult<ApiResponse<AcceleratorConfigurationOperational>>> Configuration(CancellationToken ct) =>
        Ok(new ApiResponse<AcceleratorConfigurationOperational>(
            await operations.ConfigurationAsync(User.GetRequiredUserId(), ct)));

    [HttpPost("payments/{merchantOrderId}/reconcile")]
    public async Task<ActionResult<ApiResponse<PaymentResponse>>> ReconcilePayment(string merchantOrderId,
        AdminPhonePeReconcileRequest request, CancellationToken ct) =>
        Ok(new ApiResponse<PaymentResponse>(await payments.ReconcileAcceleratorPhonePeForAdminAsync(
            User.GetRequiredUserId(), merchantOrderId, request, ct)));

    [HttpPost("payments/{merchantOrderId}/refund-review")]
    public async Task<ActionResult<ApiResponse<PaymentResponse>>> FlagRefundReview(string merchantOrderId,
        AdminAcceleratorRefundReviewRequest request, CancellationToken ct) =>
        Ok(new ApiResponse<PaymentResponse>(await payments.FlagAcceleratorRefundReviewAsync(
            User.GetRequiredUserId(), merchantOrderId, request, ct)));
}
