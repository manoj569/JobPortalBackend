using JobPortal.API.Extensions;
using JobPortal.Application.Abstractions.HiringMessaging;
using JobPortal.Application.Features.HiringMessaging;
using JobPortal.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JobPortal.API.Controllers;

[ApiController]
[Authorize(Roles = "Candidate")]
[Route("api/candidate/messages")]
[Produces("application/json")]
public sealed class CandidateMessagesController(
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
            await messaging.SearchCandidateAsync(
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
            await messaging.GetCandidateAsync(
                UserId,
                conversationId,
                cancellationToken);

        return Ok(
            new ApiResponse<HiringConversationDetailResponse>(
                result));
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
            await messaging.SendCandidateMessageAsync(
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
        await messaging.MarkCandidateReadAsync(
            UserId,
            conversationId,
            cancellationToken);

        return Ok(
            new ApiResponse<object>(
                new { },
                "Conversation marked as read."));
    }
}
