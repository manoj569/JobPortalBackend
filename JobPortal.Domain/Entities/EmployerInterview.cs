using JobPortal.Domain.Common;
using JobPortal.Domain.Enums;

namespace JobPortal.Domain.Entities;

public sealed class EmployerInterview : BaseEntity
{
    public Guid JobApplicationId { get; set; }

    public JobApplication JobApplication { get; set; } = null!;

    public Guid PipelineId { get; set; }

    public EmployerHiringPipeline Pipeline { get; set; } = null!;

    public int RoundNumber { get; set; }

    public InterviewRoundType RoundType { get; set; }

    public string? Title { get; set; }

    public DateTime ScheduledAtUtc { get; set; }

    public int DurationMinutes { get; set; } = 60;

    public InterviewFormat InterviewFormat { get; set; }

    public string? MeetingUrl { get; set; }

    public string? Location { get; set; }

    public string? InterviewerName { get; set; }

    public string? InterviewerEmail { get; set; }

    public EmployerInterviewStatus Status { get; set; }
        = EmployerInterviewStatus.Scheduled;

    public EmployerInterviewResult Result { get; set; }
        = EmployerInterviewResult.Pending;

    public decimal? Score { get; set; }

    public string? Feedback { get; set; }

    public string? InternalNotes { get; set; }

    public DateTime? CompletedAtUtc { get; set; }

    public DateTime? CancelledAtUtc { get; set; }

    public Guid CreatedByUserId { get; set; }

    public User CreatedByUser { get; set; } = null!;
}
