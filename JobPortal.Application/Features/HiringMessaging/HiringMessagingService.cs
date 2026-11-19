using FluentValidation;
using JobPortal.Application.Abstractions.HiringMessaging;
using JobPortal.Application.Abstractions.Persistence;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Application.Features.Notifications;
using JobPortal.Domain.Entities;
using JobPortal.Shared.Models;

namespace JobPortal.Application.Features.HiringMessaging;

public sealed class HiringMessagingService(
    IHiringMessagingRepository repository,
    IUnitOfWork unitOfWork,
    IValidator<HiringConversationQuery> queryValidator,
    IValidator<CreateHiringConversationRequest> createValidator,
    IValidator<SendHiringMessageRequest> messageValidator,
    NotificationOutbox notificationOutbox,
    TimeProvider timeProvider)
    : IHiringMessagingService
{
    public async Task<PagedResponse<HiringConversationResponse>>
        SearchEmployerAsync(
            Guid employerUserId,
            HiringConversationQuery query,
            CancellationToken cancellationToken = default)
    {
        await queryValidator.ValidateAndThrowAsync(
            query,
            cancellationToken);

        var result =
            await repository.SearchEmployerAsync(
                employerUserId,
                query.PageNumber,
                query.PageSize,
                cancellationToken);

        return new(
            result.Items
                .Select(x =>
                    MapConversation(x, employerUserId, true))
                .ToArray(),
            query.PageNumber,
            query.PageSize,
            result.TotalCount);
    }

    public async Task<PagedResponse<HiringConversationResponse>>
        SearchCandidateAsync(
            Guid candidateUserId,
            HiringConversationQuery query,
            CancellationToken cancellationToken = default)
    {
        await queryValidator.ValidateAndThrowAsync(
            query,
            cancellationToken);

        var result =
            await repository.SearchCandidateAsync(
                candidateUserId,
                query.PageNumber,
                query.PageSize,
                cancellationToken);

        return new(
            result.Items
                .Select(x =>
                    MapConversation(x, candidateUserId, false))
                .ToArray(),
            query.PageNumber,
            query.PageSize,
            result.TotalCount);
    }

    public async Task<HiringConversationDetailResponse>
        GetEmployerAsync(
            Guid employerUserId,
            Guid conversationId,
            CancellationToken cancellationToken = default)
    {
        var conversation =
            await repository.GetForEmployerAsync(
                employerUserId,
                conversationId,
                cancellationToken)
            ?? throw new NotFoundException(
                "Conversation was not found.");

        return MapDetail(
            conversation,
            employerUserId,
            true);
    }

    public async Task<HiringConversationDetailResponse>
        GetCandidateAsync(
            Guid candidateUserId,
            Guid conversationId,
            CancellationToken cancellationToken = default)
    {
        var conversation =
            await repository.GetForCandidateAsync(
                candidateUserId,
                conversationId,
                cancellationToken)
            ?? throw new NotFoundException(
                "Conversation was not found.");

        return MapDetail(
            conversation,
            candidateUserId,
            false);
    }

    public async Task<HiringConversationDetailResponse>
        CreateEmployerAsync(
            Guid employerUserId,
            CreateHiringConversationRequest request,
            CancellationToken cancellationToken = default)
    {
        await createValidator.ValidateAndThrowAsync(
            request,
            cancellationToken);

        var application =
            await repository.GetOwnedApplicationAsync(
                employerUserId,
                request.ApplicationId,
                cancellationToken)
            ?? throw new NotFoundException(
                "Application was not found.");

        var existing =
            await repository.GetByApplicationAsync(
                request.ApplicationId,
                cancellationToken);

        if (existing is not null)
        {
            return MapDetail(
                existing,
                employerUserId,
                true);
        }

        var now = UtcNow;

        var conversation =
            new HiringConversation
            {
                JobApplicationId = application.Id,
                JobApplication = application,

                CandidateUserId = application.UserId,
                CandidateUser = application.User,

                EmployerUserId = employerUserId,

                CandidateLastReadAtUtc = null,
                EmployerLastReadAtUtc = now
            };

        await repository.AddConversationAsync(
            conversation,
            cancellationToken);

        if (!string.IsNullOrWhiteSpace(
                request.InitialMessage))
        {
            var body =
                request.InitialMessage.Trim();

            var message =
                new HiringMessage
                {
                    ConversationId =
                        conversation.Id,

                    Conversation =
                        conversation,

                    SenderUserId =
                        employerUserId,

                    Body =
                        body,

                    SentAtUtc =
                        now
                };

            conversation.Messages.Add(message);
            conversation.LastMessageAtUtc = now;


            await repository.AddMessageAsync(
                message,
                cancellationToken);
        }

        await unitOfWork.SaveChangesAsync(
            cancellationToken);

        /*
         * EmployerUser navigation is not guaranteed to be
         * loaded on a newly created object.
         * Reload the persisted conversation.
         */
        var saved =
            await repository.GetForEmployerAsync(
                employerUserId,
                conversation.Id,
                cancellationToken)
            ?? throw new NotFoundException(
                "Conversation was not found after creation.");

        return MapDetail(
            saved,
            employerUserId,
            true);
    }

    public Task<HiringMessageResponse>
        SendEmployerMessageAsync(
            Guid employerUserId,
            Guid conversationId,
            SendHiringMessageRequest request,
            CancellationToken cancellationToken = default)
    {
        return SendAsync(
            employerUserId,
            conversationId,
            request,
            true,
            cancellationToken);
    }

    public Task<HiringMessageResponse>
        SendCandidateMessageAsync(
            Guid candidateUserId,
            Guid conversationId,
            SendHiringMessageRequest request,
            CancellationToken cancellationToken = default)
    {
        return SendAsync(
            candidateUserId,
            conversationId,
            request,
            false,
            cancellationToken);
    }

    public async Task MarkEmployerReadAsync(
        Guid employerUserId,
        Guid conversationId,
        CancellationToken cancellationToken = default)
    {
        var conversation =
            await repository.GetForEmployerAsync(
                employerUserId,
                conversationId,
                cancellationToken)
            ?? throw new NotFoundException(
                "Conversation was not found.");

        conversation.EmployerLastReadAtUtc =
            UtcNow;

        await unitOfWork.SaveChangesAsync(
            cancellationToken);
    }

    public async Task MarkCandidateReadAsync(
        Guid candidateUserId,
        Guid conversationId,
        CancellationToken cancellationToken = default)
    {
        var conversation =
            await repository.GetForCandidateAsync(
                candidateUserId,
                conversationId,
                cancellationToken)
            ?? throw new NotFoundException(
                "Conversation was not found.");

        conversation.CandidateLastReadAtUtc =
            UtcNow;

        await unitOfWork.SaveChangesAsync(
            cancellationToken);
    }

    private async Task<HiringMessageResponse> SendAsync(
        Guid userId,
        Guid conversationId,
        SendHiringMessageRequest request,
        bool employer,
        CancellationToken cancellationToken)
    {
        await messageValidator.ValidateAndThrowAsync(
            request,
            cancellationToken);

        var conversation =
            employer
                ? await repository.GetForEmployerAsync(
                    userId,
                    conversationId,
                    cancellationToken)
                : await repository.GetForCandidateAsync(
                    userId,
                    conversationId,
                    cancellationToken);

        if (conversation is null)
        {
            throw new NotFoundException(
                "Conversation was not found.");
        }

        var now = UtcNow;

        var message =
            new HiringMessage
            {
                ConversationId =
                    conversation.Id,

                Conversation =
                    conversation,

                SenderUserId =
                    userId,

                Body =
                    request.Body.Trim(),

                SentAtUtc =
                    now
            };

        await repository.AddMessageAsync(
            message,
            cancellationToken);

        conversation.LastMessageAtUtc =
            now;
        var recipientUserId =
    employer
        ? conversation.CandidateUserId
        : conversation.EmployerUserId;

        var recipientActionUrl =
            employer
                ? "/dashboard/messages"
                : "/employers/messages";

        var senderName =
            employer
                ? FullName(conversation.EmployerUser)
                : FullName(conversation.CandidateUser);

        notificationOutbox.Enqueue(
            NotificationSource.HiringMessage,
            message.Id,
            message.Id,
            recipientUserId,
            $"hiring-message:{message.Id:D}",
            "New message",
            $"You have a new message from {senderName}.",
            recipientActionUrl);
        if (employer)
        {
            conversation.EmployerLastReadAtUtc =
                now;
        }
        else
        {
            conversation.CandidateLastReadAtUtc =
                now;
        }

        await unitOfWork.SaveChangesAsync(
            cancellationToken);

        

        return new(
            message.Id,
            conversation.Id,
            userId,
            senderName,
            message.Body,
            message.SentAtUtc,
            true);
    }

    private static HiringConversationDetailResponse MapDetail(
        HiringConversation conversation,
        Guid currentUserId,
        bool employer)
    {
        return new(
            MapConversation(
                conversation,
                currentUserId,
                employer),

            conversation.Messages
                .OrderBy(x => x.SentAtUtc)
                .Select(x =>
                    new HiringMessageResponse(
                        x.Id,
                        x.ConversationId,
                        x.SenderUserId,
                        FullName(x.SenderUser),
                        x.Body,
                        x.SentAtUtc,
                        x.SenderUserId ==
                        currentUserId))
                .ToArray());
    }

    private static HiringConversationResponse MapConversation(
        HiringConversation conversation,
        Guid currentUserId,
        bool employer)
    {
        var lastReadAt =
            employer
                ? conversation.EmployerLastReadAtUtc
                : conversation.CandidateLastReadAtUtc;

        var unreadCount =
            conversation.Messages.Count(x =>
                x.SenderUserId != currentUserId &&
                (!lastReadAt.HasValue ||
                 x.SentAtUtc > lastReadAt.Value));

        var lastMessage =
            conversation.Messages
                .OrderByDescending(x => x.SentAtUtc)
                .FirstOrDefault();

        return new(
            conversation.Id,
            conversation.JobApplicationId,
            conversation.CandidateUserId,
            FullName(conversation.CandidateUser),
            conversation.EmployerUserId,
            FullName(conversation.EmployerUser),
            conversation.JobApplication.JobId,
            conversation.JobApplication.Job.Title,
            lastMessage?.Body,
            conversation.LastMessageAtUtc,
            unreadCount);
    }

    private static string FullName(User user)
    {
        return string.Join(
            " ",
            new[]
            {
                user.FirstName,
                user.LastName
            }.Where(x =>
                !string.IsNullOrWhiteSpace(x)));
    }

    private DateTime UtcNow =>
        timeProvider
            .GetUtcNow()
            .UtcDateTime;
}
