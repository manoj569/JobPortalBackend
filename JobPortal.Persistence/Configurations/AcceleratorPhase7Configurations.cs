using JobPortal.Domain.Entities;
using JobPortal.Domain.Entities.CareerAccelerator;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobPortal.Persistence.Configurations;

public sealed class TalentShareLinkConfiguration : IEntityTypeConfiguration<TalentShareLink>
{
    public void Configure(EntityTypeBuilder<TalentShareLink> builder)
    {
        builder.ToTable("TalentShareLinks"); builder.ConfigureBaseEntity();
        builder.Property(x => x.TokenHash).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Purpose).HasMaxLength(50).IsRequired();
        builder.HasIndex(x => x.TokenHash).IsUnique();
        builder.HasIndex(x => new { x.CandidateId, x.ExpiresAtUtc });
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.CandidateId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AcceleratorCareerGoal>().WithMany().HasForeignKey(x => x.GoalId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Job>().WithMany().HasForeignKey(x => x.JobId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Company>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class TalentSubmissionConfiguration : IEntityTypeConfiguration<TalentSubmission>
{
    public void Configure(EntityTypeBuilder<TalentSubmission> builder)
    {
        builder.ToTable("TalentSubmissions"); builder.ConfigureBaseEntity();
        builder.Property(x => x.Revision).IsConcurrencyToken();
        builder.Property(x => x.InterviewMessage).HasMaxLength(2000);
        builder.HasIndex(x => new { x.CandidateId, x.JobId }).IsUnique().HasFilter("\"IsDeleted\" = FALSE");
        builder.HasIndex(x => new { x.CompanyId, x.Status, x.SubmittedAtUtc });
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.CandidateId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Job>().WithMany().HasForeignKey(x => x.JobId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Company>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AcceleratorCareerGoal>().WithMany().HasForeignKey(x => x.GoalId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<TalentShareLink>().WithMany().HasForeignKey(x => x.ShareLinkId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class TalentSubmissionEventConfiguration : IEntityTypeConfiguration<TalentSubmissionEvent>
{
    public void Configure(EntityTypeBuilder<TalentSubmissionEvent> builder)
    {
        builder.ToTable("TalentSubmissionEvents"); builder.ConfigureBaseEntity();
        builder.HasIndex(x => new { x.SubmissionId, x.OccurredAtUtc });
        builder.HasOne<TalentSubmission>().WithMany().HasForeignKey(x => x.SubmissionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.ActorId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class EmployerContactConfiguration : IEntityTypeConfiguration<EmployerContact>
{
    public void Configure(EntityTypeBuilder<EmployerContact> builder)
    {
        builder.ToTable("EmployerContacts"); builder.ConfigureBaseEntity();
        builder.Property(x => x.FullName).HasMaxLength(160).IsRequired();
        builder.Property(x => x.Title).HasMaxLength(160).IsRequired();
        builder.Property(x => x.Department).HasMaxLength(100).IsRequired();
        builder.Property(x => x.ProfessionalEmail).HasMaxLength(254).IsRequired();
        builder.Property(x => x.NormalizedEmail).HasMaxLength(254).IsRequired();
        builder.Property(x => x.Source).HasMaxLength(60).IsRequired();
        builder.Property(x => x.VerificationEvidence).HasMaxLength(500);
        builder.HasIndex(x => new { x.CompanyId, x.NormalizedEmail }).IsUnique().HasFilter("\"IsDeleted\" = FALSE");
        builder.HasIndex(x => new { x.CompanyId, x.IsSuppressed, x.Verification });
        builder.HasOne<Company>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class TalentOutreachDraftConfiguration : IEntityTypeConfiguration<TalentOutreachDraft>
{
    public void Configure(EntityTypeBuilder<TalentOutreachDraft> builder)
    {
        builder.ToTable("TalentOutreachDrafts"); builder.ConfigureBaseEntity();
        builder.Property(x => x.Subject).HasMaxLength(180).IsRequired();
        builder.Property(x => x.Body).HasMaxLength(10000).IsRequired();
        builder.Property(x => x.SendIdempotencyKey).HasMaxLength(100).IsRequired();
        builder.Property(x => x.ProviderMessageId).HasMaxLength(300);
        builder.Property(x => x.FailureEvidenceReference).HasMaxLength(500);
        builder.Property(x => x.Revision).IsConcurrencyToken();
        builder.HasIndex(x => new { x.CandidateId, x.JobId, x.Status });
        builder.HasIndex(x => new { x.CandidateId, x.SendIdempotencyKey }).IsUnique()
            .HasFilter("\"SendIdempotencyKey\" <> '' AND \"IsDeleted\" = FALSE");
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.CandidateId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Job>().WithMany().HasForeignKey(x => x.JobId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Company>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<TalentShareLink>().WithMany().HasForeignKey(x => x.ShareLinkId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<CandidateMailboxConnection>().WithMany().HasForeignKey(x => x.MailboxConnectionId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AcceleratorUsageReservation>().WithMany().HasForeignKey(x => x.UsageReservationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class TalentOutreachRecipientConfiguration : IEntityTypeConfiguration<TalentOutreachRecipient>
{
    public void Configure(EntityTypeBuilder<TalentOutreachRecipient> builder)
    {
        builder.ToTable("TalentOutreachRecipients"); builder.ConfigureBaseEntity();
        builder.Property(x => x.EmailSnapshot).HasMaxLength(254).IsRequired();
        builder.HasIndex(x => new { x.DraftId, x.ContactId }).IsUnique();
        builder.HasOne<TalentOutreachDraft>().WithMany().HasForeignKey(x => x.DraftId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<EmployerContact>().WithMany().HasForeignKey(x => x.ContactId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class CandidateMailboxConnectionConfiguration : IEntityTypeConfiguration<CandidateMailboxConnection>
{
    public void Configure(EntityTypeBuilder<CandidateMailboxConnection> builder)
    {
        builder.ToTable("CandidateMailboxConnections"); builder.ConfigureBaseEntity();
        builder.Property(x => x.Provider).HasMaxLength(30).IsRequired();
        builder.Property(x => x.EmailAddress).HasMaxLength(254).IsRequired();
        builder.Property(x => x.ProviderAccountIdentifier).HasMaxLength(200).IsRequired();
        builder.Property(x => x.GrantedScopes).HasMaxLength(500).IsRequired();
        builder.Property(x => x.TokenReference).HasMaxLength(300).IsRequired();
        builder.HasIndex(x => new { x.CandidateId, x.Provider, x.ProviderAccountIdentifier }).IsUnique();
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.CandidateId).OnDelete(DeleteBehavior.Restrict);
    }
}

