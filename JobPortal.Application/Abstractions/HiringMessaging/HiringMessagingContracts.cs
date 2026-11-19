using JobPortal.Application.Features.HiringMessaging;
using JobPortal.Shared.Models;

namespace JobPortal.Application.Abstractions.HiringMessaging;

public interface IHiringMessagingService
{
    Task<PagedResponse<HiringConversationResponse>> SearchEmployerAsync(
        Guid employerUserId,
        HiringConversationQuery query,
        CancellationToken cancellationToken = default);

    Task<PagedResponse<HiringConversationResponse>> SearchCandidateAsync(
        Guid candidateUserId,
        HiringConversationQuery query,
        CancellationToken cancellationToken = default);

    Task<HiringConversationDetailResponse> GetEmployerAsync(
        Guid employerUserId,
        Guid conversationId,
        CancellationToken cancellationToken = default);

    Task<HiringConversationDetailResponse> GetCandidateAsync(
        Guid candidateUserId,
        Guid conversationId,
        CancellationToken cancellationToken = default);

    Task<HiringConversationDetailResponse> CreateEmployerAsync(
        Guid employerUserId,
        CreateHiringConversationRequest request,
        CancellationToken cancellationToken = default);

    Task<HiringMessageResponse> SendEmployerMessageAsync(
        Guid employerUserId,
        Guid conversationId,
        SendHiringMessageRequest request,
        CancellationToken cancellationToken = default);

    Task<HiringMessageResponse> SendCandidateMessageAsync(
        Guid candidateUserId,
        Guid conversationId,
        SendHiringMessageRequest request,
        CancellationToken cancellationToken = default);

    Task MarkEmployerReadAsync(
        Guid employerUserId,
        Guid conversationId,
        CancellationToken cancellationToken = default);

    Task MarkCandidateReadAsync(
        Guid candidateUserId,
        Guid conversationId,
        CancellationToken cancellationToken = default);
}
