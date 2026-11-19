using JobPortal.Domain.Common;
using JobPortal.Domain.Enums;

namespace JobPortal.Domain.Entities;

public sealed class EmployerHiringPipeline : BaseEntity
{
    public Guid JobApplicationId { get; set; }

    public JobApplication JobApplication { get; set; } = null!;

    public EmployerPipelineStage Stage { get; set; }
        = EmployerPipelineStage.Screening;

    public EmployerScreeningStatus ScreeningStatus { get; set; }
        = EmployerScreeningStatus.NotStarted;

    public string? ScreeningNotes { get; set; }

    public decimal? ScreeningScore { get; set; }

    public DateTime? ScreeningStartedAtUtc { get; set; }

    public DateTime? ScreenedAtUtc { get; set; }

    public Guid? ScreenedByUserId { get; set; }

    public User? ScreenedByUser { get; set; }

  

    public DateTime? HiredAtUtc { get; set; }
}
