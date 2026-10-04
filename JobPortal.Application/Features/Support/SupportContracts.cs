using System.Text.Json.Serialization;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Shared.Models;

namespace JobPortal.Application.Features.Support;

public sealed class SupportSettings
{
    public const string SectionName = "SupportSettings";
    public string? SupportEmail { get; set; }
    public string? ScreenshotRootPath { get; set; }
}

public sealed record CreateSupportTicketRequest(
    string? Name, string? Email, SupportCategory Category, string Subject, string Description);
public sealed record SupportScreenshotUpload(Stream Content, long Length, string FileName, string ContentType);
public sealed record SupportScreenshotDownload(Stream Content, string ContentType);
public sealed record SupportTicketCreatedResponse(string TicketNumber);
public sealed record SupportTicketResponse(
    string TicketNumber,
    [property: JsonConverter(typeof(JsonStringEnumConverter<SupportCategory>))] SupportCategory Category,
    string Subject, string Description,
    [property: JsonConverter(typeof(JsonStringEnumConverter<SupportTicketStatus>))] SupportTicketStatus Status,
    bool HasScreenshot, DateTime CreatedAtUtc, DateTime? UpdatedAtUtc, DateTime? ResolvedAtUtc);
public sealed record AdminSupportTicketResponse(
    Guid Id, Guid? UserId, string Name, string Email, string TicketNumber,
    [property: JsonConverter(typeof(JsonStringEnumConverter<SupportCategory>))] SupportCategory Category,
    string Subject, string Description,
    [property: JsonConverter(typeof(JsonStringEnumConverter<SupportTicketStatus>))] SupportTicketStatus Status,
    string? AdminNotes, bool HasScreenshot, DateTime CreatedAtUtc, DateTime? UpdatedAtUtc, DateTime? ResolvedAtUtc);
public sealed record UpdateSupportTicketStatusRequest(
    [property: JsonConverter(typeof(JsonStringEnumConverter<SupportTicketStatus>))] SupportTicketStatus Status);
public sealed record UpdateSupportTicketNotesRequest(string? AdminNotes);
public sealed record SupportTicketPageQuery(int PageNumber = 1, int PageSize = 20);
public sealed record AdminSupportTicketQuery(
    int PageNumber = 1, int PageSize = 20, SupportTicketStatus? Status = null,
    SupportCategory? Category = null, string? Email = null, string? TicketNumber = null,
    DateTime? FromUtc = null, DateTime? ToUtc = null);

public interface ISupportTicketService
{
    Task<SupportTicketCreatedResponse> CreateAsync(Guid? userId, CreateSupportTicketRequest request,
        SupportScreenshotUpload? screenshot = null, CancellationToken cancellationToken = default);
    Task<PagedResponse<SupportTicketResponse>> GetMineAsync(Guid userId, SupportTicketPageQuery query, CancellationToken cancellationToken = default);
    Task<SupportTicketResponse> GetMineAsync(Guid userId, string ticketNumber, CancellationToken cancellationToken = default);
    Task<SupportScreenshotDownload> GetMyScreenshotAsync(Guid userId, string ticketNumber, CancellationToken cancellationToken = default);
    Task<PagedResponse<AdminSupportTicketResponse>> SearchAsync(AdminSupportTicketQuery query, CancellationToken cancellationToken = default);
    Task<AdminSupportTicketResponse> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task<SupportScreenshotDownload> GetScreenshotAsync(Guid id, CancellationToken cancellationToken = default);
    Task<AdminSupportTicketResponse> UpdateStatusAsync(Guid actorId, Guid id, UpdateSupportTicketStatusRequest request, CancellationToken cancellationToken = default);
    Task<AdminSupportTicketResponse> UpdateNotesAsync(Guid actorId, Guid id, UpdateSupportTicketNotesRequest request, CancellationToken cancellationToken = default);
}

public interface ISupportTicketRepository
{
    Task AddAsync(SupportTicket ticket, CancellationToken cancellationToken = default);
    Task<SupportTicket?> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task<SupportTicket?> GetOwnedAsync(Guid userId, string ticketNumber, CancellationToken cancellationToken = default);
    Task<(IReadOnlyCollection<SupportTicket> Items, int Total)> GetMineAsync(Guid userId, SupportTicketPageQuery query, CancellationToken cancellationToken = default);
    Task<(IReadOnlyCollection<SupportTicket> Items, int Total)> SearchAsync(AdminSupportTicketQuery query, CancellationToken cancellationToken = default);
}

public interface ISupportScreenshotStorage
{
    Task<string> StoreAsync(Stream content, string extension, CancellationToken cancellationToken = default);
    Task<Stream?> OpenReadAsync(string storageKey, CancellationToken cancellationToken = default);
    Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default);
}
