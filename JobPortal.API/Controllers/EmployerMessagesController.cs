using JobPortal.API.Extensions;
using JobPortal.Application.Abstractions.HiringMessaging;
using JobPortal.Application.Features.HiringMessaging;
using JobPortal.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JobPortal.API.Controllers;

[ApiController]
[Authorize(Roles = "Employer")]
[Route("api/employer/messages")]
[Produces("application/json")]
public sealed class EmployerMessagesController(
    IHiringMessagingService messaging)
    : ControllerBase
{
    private Guid UserId =>
        User.GetRequiredUserId();

    [HttpGet("conversations")]
    public async Task<ActionResult<
        ApiResponse<PagedResponse<HiringConversationResponse>>>>
        Search(
            [FromQuery] HiringConversationQuery query,
            CancellationToken cancellationToken)
    {
        var result =
            await messaging.SearchEmployerAsync(
                UserId,
                query,
                cancellationToken);

        return Ok(new ApiResponse<
            PagedResponse<HiringConversationResponse>>(
                result));
    }

    [HttpGet("conversations/{conversationId:guid}")]
    public async Task<ActionResult<
        ApiResponse<HiringConversationDetailResponse>>> Get(
        Guid conversationId,
        CancellationToken cancellationToken)
    {
        var result =
            await messaging.GetEmployerAsync(
                UserId,
                conversationId,
                cancellationToken);

        return Ok(
            new ApiResponse<HiringConversationDetailResponse>(
                result));
    }

    [HttpPost("conversations")]
    public async Task<ActionResult<
        ApiResponse<HiringConversationDetailResponse>>> Create(
        [FromBody] CreateHiringConversationRequest request,
        CancellationToken cancellationToken)
    {
        var result =
            await messaging.CreateEmployerAsync(
                UserId,
                request,
                cancellationToken);

        return Ok(
            new ApiResponse<HiringConversationDetailResponse>(
                result,
                "Conversation created successfully."));
    }

    [HttpPost(
        "conversations/{conversationId:guid}/messages")]
    public async Task<ActionResult<
        ApiResponse<HiringMessageResponse>>> Send(
        Guid conversationId,
        [FromBody] SendHiringMessageRequest request,
        CancellationToken cancellationToken)
    {
        var result =
            await messaging.SendEmployerMessageAsync(
                UserId,
                conversationId,
                request,
                cancellationToken);

        return Ok(
            new ApiResponse<HiringMessageResponse>(
                result,
                "Message sent successfully."));
    }

    [HttpPost(
        "conversations/{conversationId:guid}/read")]
    public async Task<ActionResult<ApiResponse<object>>> Read(
        Guid conversationId,
        CancellationToken cancellationToken)
    {
        await messaging.MarkEmployerReadAsync(
            UserId,
            conversationId,
            cancellationToken);

        return Ok(
            new ApiResponse<object>(
                new { },
                "Conversation marked as read."));
    }
}
