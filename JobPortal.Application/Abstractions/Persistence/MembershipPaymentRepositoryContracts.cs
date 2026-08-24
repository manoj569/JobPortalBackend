using JobPortal.Application.Features.Memberships;
using JobPortal.Application.Features.Payments;
using JobPortal.Domain.Entities;

namespace JobPortal.Application.Abstractions.Persistence;

public interface IMembershipRepository
{
    Task<AvailableJobAccess?> GetAvailableJobAsync(string slug, CancellationToken cancellationToken = default);
    Task<Membership?> GetActiveForUserAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<Membership?> GetPortalMembershipForUserAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<Membership?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task AddAsync(Membership membership, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<MembershipResponse>> GetMembershipsForUserAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<(IReadOnlyCollection<MembershipHistoryResponse> Items, int TotalCount)> GetHistoryAsync(Guid userId, HistoryQuery query, CancellationToken cancellationToken = default);
    Task RecordApplicationAsync(Guid userId, Guid jobId, CancellationToken cancellationToken = default);
}

public sealed record AvailableJobAccess(Guid JobId, string ApplicationUrl);

public interface IPaymentRepository
{
    Task AddAsync(Payment payment, CancellationToken cancellationToken = default);
    Task<Payment?> GetOwnedAsync(Guid id, Guid userId, CancellationToken cancellationToken = default);
    Task<Payment?> GetByProviderOrderIdAsync(string providerOrderId, CancellationToken cancellationToken = default);
    Task<Payment?> GetOwnedByProviderOrderIdAsync(string providerOrderId, Guid userId, CancellationToken cancellationToken = default);
    Task<Payment?> GetLatestForUserAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<bool> HasProcessedProviderEventAsync(string providerEventId, CancellationToken cancellationToken = default);
    Task<(IReadOnlyCollection<PaymentResponse> Items, int TotalCount)> GetForUserAsync(Guid userId, HistoryQuery query, CancellationToken cancellationToken = default);
    Task<(IReadOnlyCollection<PaymentHistoryResponse> Items, int TotalCount)> GetHistoryAsync(Guid userId, HistoryQuery query, CancellationToken cancellationToken = default);
}
