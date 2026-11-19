using JobPortal.Domain.Entities;

namespace JobPortal.Application.Abstractions.Persistence;

public interface IHiringMessagingRepository
{
    Task<JobApplication?> GetOwnedApplicationAsync(
        Guid employerUserId,
        Guid applicationId,
        CancellationToken cancellationToken = default);

    Task<HiringConversation?> GetByApplicationAsync(
        Guid applicationId,
        CancellationToken cancellationToken = default);

    Task<HiringConversation?> GetForEmployerAsync(
        Guid employerUserId,
        Guid conversationId,
        CancellationToken cancellationToken = default);

    Task<HiringConversation?> GetForCandidateAsync(
        Guid candidateUserId,
        Guid conversationId,
        CancellationToken cancellationToken = default);

    Task<(IReadOnlyCollection<HiringConversation> Items, int TotalCount)>
        SearchEmployerAsync(
            Guid employerUserId,
            int pageNumber,
            int pageSize,
            CancellationToken cancellationToken = default);

    Task<(IReadOnlyCollection<HiringConversation> Items, int TotalCount)>
        SearchCandidateAsync(
            Guid candidateUserId,
            int pageNumber,
            int pageSize,
            CancellationToken cancellationToken = default);

    Task AddConversationAsync(
        HiringConversation conversation,
        CancellationToken cancellationToken = default);

    Task AddMessageAsync(
        HiringMessage message,
        CancellationToken cancellationToken = default);
}
