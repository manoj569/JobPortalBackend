using JobPortal.Domain.Common;

namespace JobPortal.Domain.Entities;

public sealed class JobSkill : BaseEntity
{
    public const byte MinimumProficiencyLevel = 1;
    public const byte MaximumProficiencyLevel = 5;
    // Skill required; proficiency is not specifically stated by the job creator.
    public const byte DefaultProficiencyLevel = MinimumProficiencyLevel;

    private byte _proficiencyLevel = DefaultProficiencyLevel;
    public Guid JobId { get; set; }
    public Job Job { get; set; } = null!;
    public Guid SkillId { get; set; }
    public Skill Skill { get; set; } = null!;
    public bool IsRequired { get; set; }
    public byte ProficiencyLevel
    {
        get => _proficiencyLevel;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, MinimumProficiencyLevel);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, MaximumProficiencyLevel);
            _proficiencyLevel = value;
        }
    }
}
