namespace JobPortal.Application.Features.HiringMessaging;

public sealed record HiringConversationResponse(
    Guid Id,
    Guid ApplicationId,
    Guid CandidateUserId,
    string CandidateName,
    Guid EmployerUserId,
    string EmployerName,
    Guid JobId,
    string JobTitle,
    string? LastMessage,
    DateTime? LastMessageAtUtc,
    int UnreadCount);

public sealed record HiringMessageResponse(
    Guid Id,
    Guid ConversationId,
    Guid SenderUserId,
    string SenderName,
    string Body,
    DateTime SentAtUtc,
    bool IsMine);

public sealed record HiringConversationDetailResponse(
    HiringConversationResponse Conversation,
    IReadOnlyCollection<HiringMessageResponse> Messages);

public sealed record HiringConversationQuery(
    int PageNumber = 1,
    int PageSize = 20);

public sealed record CreateHiringConversationRequest(
    Guid ApplicationId,
    string? InitialMessage);

public sealed record SendHiringMessageRequest(
    string Body);
