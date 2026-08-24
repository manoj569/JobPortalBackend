using JobPortal.Application.Features.InterviewInsights;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Shared.Models;

namespace JobPortal.Application.Abstractions.InterviewInsights;

public interface IInterviewInsightService
{
    Task<InterviewInsightResponse> CreateAsync(Guid candidateId, CreateInterviewInsightRequest request, CancellationToken ct = default);
    Task<PagedResponse<InterviewInsightCardResponse>> SearchAsync(Guid candidateId, InterviewInsightQuery query, CancellationToken ct = default);
    Task<IReadOnlyCollection<InterviewInsightCompanyResponse>> SearchCompaniesAsync(Guid candidateId, string query, int limit, CancellationToken ct = default);
    Task<InterviewInsightResponse> GetAsync(Guid candidateId, Guid id, CancellationToken ct = default);
    Task<InterviewInsightResponse> UpdateAsync(Guid candidateId, Guid id, UpdateInterviewInsightRequest request, CancellationToken ct = default);
    Task DeleteAsync(Guid candidateId, Guid id, CancellationToken ct = default);
    Task<InterviewScheduleResponse> CreateScheduleAsync(Guid candidateId, CreateInterviewScheduleRequest request, CancellationToken ct = default);
    Task<IReadOnlyCollection<InterviewScheduleResponse>> GetSchedulesAsync(Guid candidateId, CancellationToken ct = default);
    Task<InterviewScheduleResponse> UpdateScheduleAsync(Guid candidateId, Guid id, UpdateInterviewScheduleRequest request, CancellationToken ct = default);
    Task<InsightFeedbackResponse> AddFeedbackAsync(Guid candidateId, Guid insightId, CreateInsightFeedbackRequest request, CancellationToken ct = default);
    Task<InsightReportResponse> ReportAsync(Guid candidateId, Guid insightId, CreateInsightReportRequest request, CancellationToken ct = default);
    Task<MyInterviewContributionsResponse> ContributionsAsync(Guid candidateId, CancellationToken ct = default);
    Task<CompanyInterviewInsightSummaryResponse> CompanySummaryAsync(Guid candidateId, Guid companyId, CancellationToken ct = default);
}

public interface IAdminInterviewInsightService
{
    Task<PagedResponse<InterviewInsightResponse>> SearchAsync(AdminInterviewInsightQuery query, CancellationToken ct = default);
    Task<InterviewInsightResponse> ModerateAsync(Guid administratorId, Guid id, ModerateInterviewInsightRequest request, CancellationToken ct = default);
    Task<PagedResponse<AdminInsightReportResponse>> ReportsAsync(AdminInsightReportQuery query, CancellationToken ct = default);
    Task<AdminInsightReportResponse> ModerateReportAsync(Guid administratorId, Guid id, ModerateInsightReportRequest request, CancellationToken ct = default);
}

public interface IInterviewInsightRepository
{
    Task<bool> IsCandidateAsync(Guid candidateId, CancellationToken ct);
    Task<bool> CompanyExistsAsync(Guid companyId, CancellationToken ct);
    Task<bool> JobBelongsToCompanyAsync(Guid jobId, Guid companyId, CancellationToken ct);
    Task<bool> HasApplicationAtCompanyAsync(Guid candidateId, Guid companyId, CancellationToken ct);
    Task<bool> HasScheduleAtCompanyAsync(Guid candidateId, Guid companyId, CancellationToken ct);
    Task<bool> HasPastScheduleAtCompanyAsync(Guid candidateId, Guid companyId, DateTime now, CancellationToken ct);
    Task<int> CountInsightsSinceAsync(Guid candidateId, DateTime since, CancellationToken ct);
    Task<int> CountFeedbackSinceAsync(Guid candidateId, DateTime since, CancellationToken ct);
    Task AddInsightAsync(InterviewInsight insight, CancellationToken ct);
    Task<InterviewInsight?> GetInsightAsync(Guid id, bool tracking, CancellationToken ct);
    Task<(IReadOnlyCollection<InterviewInsightCardResponse> Items, int Total)> SearchPublishedAsync(Guid candidateId, InterviewInsightQuery query, DateOnly? fromMonth, CancellationToken ct);
    Task<IReadOnlyCollection<InterviewInsightCompanyResponse>> SearchCompaniesAsync(string query, int limit, CancellationToken ct);
    Task<(IReadOnlyCollection<InterviewInsight> Items, int Total)> SearchAdminAsync(AdminInterviewInsightQuery query, CancellationToken ct);
    Task AddScheduleAsync(CandidateInterviewSchedule schedule, CancellationToken ct);
    Task<CandidateInterviewSchedule?> GetScheduleAsync(Guid candidateId, Guid id, bool tracking, CancellationToken ct);
    Task<IReadOnlyCollection<CandidateInterviewSchedule>> GetSchedulesAsync(Guid candidateId, CancellationToken ct);
    Task<bool> FeedbackExistsAsync(Guid candidateId, Guid insightId, CancellationToken ct);
    Task AddFeedbackAsync(InsightHelpfulnessFeedback feedback, CancellationToken ct);
    Task<bool> ReportExistsAsync(Guid candidateId, Guid insightId, CancellationToken ct);
    Task AddReportAsync(InsightReport report, CancellationToken ct);
    Task<(int Published, int Pending, int NeedsChanges, int Helped, int Score, IReadOnlyCollection<MyInterviewContributionCardResponse> Items)> ContributionsAsync(Guid candidateId, CancellationToken ct);
    Task<(string Name, int Published, int Helped)> CompanySummaryAsync(Guid companyId, CancellationToken ct);
    Task<(IReadOnlyCollection<InsightReport> Items, int Total)> SearchReportsAsync(AdminInsightReportQuery query, CancellationToken ct);
    Task<InsightReport?> GetReportAsync(Guid id, CancellationToken ct);
    Task AddNotificationAsync(Notification notification, CancellationToken ct);
    Task<int> CreateDueScheduleNotificationsAsync(DateTime nowUtc, CancellationToken ct);
    Task SaveAsync(CancellationToken ct);
}
