using JobPortal.Application.Features.Support;
using JobPortal.Domain.Entities;
using JobPortal.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace JobPortal.Persistence.Repositories;

public sealed class SupportTicketRepository(JobPortalDbContext context) : ISupportTicketRepository
{
    public async Task AddAsync(SupportTicket ticket, CancellationToken cancellationToken = default) =>
        await context.SupportTickets.AddAsync(ticket, cancellationToken);

    public Task<SupportTicket?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        context.SupportTickets.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

    public Task<SupportTicket?> GetOwnedAsync(Guid userId, string ticketNumber, CancellationToken cancellationToken = default) =>
        context.SupportTickets.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == userId && x.TicketNumber == ticketNumber, cancellationToken);

    public Task<(IReadOnlyCollection<SupportTicket> Items, int Total)> GetMineAsync(Guid userId,
        SupportTicketPageQuery query, CancellationToken cancellationToken = default) =>
        PageAsync(context.SupportTickets.AsNoTracking().Where(x => x.UserId == userId), query.PageNumber, query.PageSize, cancellationToken);

    public Task<(IReadOnlyCollection<SupportTicket> Items, int Total)> SearchAsync(
        AdminSupportTicketQuery query, CancellationToken cancellationToken = default)
    {
        var tickets = context.SupportTickets.AsNoTracking().AsQueryable();
        if (query.Status is { } status) tickets = tickets.Where(x => x.Status == status);
        if (query.Category is { } category) tickets = tickets.Where(x => x.Category == category);
        if (!string.IsNullOrWhiteSpace(query.Email))
        {
            var email = query.Email.Trim().ToLowerInvariant();
            tickets = tickets.Where(x => x.Email == email);
        }
        if (!string.IsNullOrWhiteSpace(query.TicketNumber))
        {
            var number = query.TicketNumber.Trim().ToUpperInvariant();
            tickets = tickets.Where(x => x.TicketNumber == number);
        }
        if (query.FromUtc is { } from) tickets = tickets.Where(x => x.CreatedAtUtc >= from);
        if (query.ToUtc is { } to) tickets = tickets.Where(x => x.CreatedAtUtc < to);
        return PageAsync(tickets, query.PageNumber, query.PageSize, cancellationToken);
    }

    private static async Task<(IReadOnlyCollection<SupportTicket> Items, int Total)> PageAsync(
        IQueryable<SupportTicket> tickets, int pageNumber, int pageSize, CancellationToken cancellationToken)
    {
        var total = await tickets.CountAsync(cancellationToken);
        var items = await tickets.OrderByDescending(x => x.CreatedAtUtc).ThenByDescending(x => x.Id)
            .Skip(checked((pageNumber - 1) * pageSize)).Take(pageSize).ToArrayAsync(cancellationToken);
        return (items, total);
    }
}
