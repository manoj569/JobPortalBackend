using JobPortal.Domain.Entities;
using JobPortal.Domain.Entities.CareerAccelerator;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobPortal.Persistence.Configurations;

public sealed class HiringOutreachSessionConfiguration : IEntityTypeConfiguration<HiringOutreachSession>
{
    public void Configure(EntityTypeBuilder<HiringOutreachSession> builder)
    {
        builder.ToTable("HiringOutreachSessions"); builder.ConfigureBaseEntity();
        builder.HasIndex(x=>new {x.CandidateId,x.RequestKey}).IsUnique();
        builder.HasIndex(x=>new {x.CandidateId,x.JobId,x.ExpiresAtUtc});
        builder.HasIndex(x=>new {x.CandidateId,x.DispatchedAtUtc});
        builder.Property(x=>x.Revision).IsConcurrencyToken();
        builder.Property(x=>x.CompanyName).HasMaxLength(300); builder.Property(x=>x.CompanyDomain).HasMaxLength(253);
        builder.Property(x=>x.JobTitle).HasMaxLength(300); builder.Property(x=>x.Location).HasMaxLength(500);
        builder.Property(x=>x.Department).HasMaxLength(200); builder.Property(x=>x.Provider).HasMaxLength(30);
        builder.Property(x=>x.AllowanceSource).HasMaxLength(80);
        builder.Property(x=>x.State).HasMaxLength(40); builder.Property(x=>x.FailureCode).HasMaxLength(80); builder.Property(x=>x.UsageState).HasMaxLength(30);
        builder.HasOne<User>().WithMany().HasForeignKey(x=>x.CandidateId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Job>().WithMany().HasForeignKey(x=>x.JobId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Company>().WithMany().HasForeignKey(x=>x.CompanyId).OnDelete(DeleteBehavior.Restrict);
    }
}
public sealed class HiringOutreachContactConfiguration : IEntityTypeConfiguration<HiringOutreachContact>
{
    public void Configure(EntityTypeBuilder<HiringOutreachContact> builder)
    {
        builder.ToTable("HiringOutreachContacts"); builder.ConfigureBaseEntity();
        builder.HasIndex(x=>new {x.SessionId,x.Email}).IsUnique();
        builder.Property(x=>x.Email).HasMaxLength(254); builder.Property(x=>x.FullName).HasMaxLength(200);
        builder.Property(x=>x.FirstName).HasMaxLength(100); builder.Property(x=>x.Title).HasMaxLength(200);
        builder.Property(x=>x.Department).HasMaxLength(200); builder.Property(x=>x.Type).HasMaxLength(40);
        builder.Property(x=>x.Verification).HasMaxLength(30); builder.Property(x=>x.LinkedInUrl).HasMaxLength(500);
        builder.Property(x=>x.ProviderReference).HasMaxLength(200);
        builder.HasOne<HiringOutreachSession>().WithMany().HasForeignKey(x=>x.SessionId).OnDelete(DeleteBehavior.Restrict);
    }
}
public sealed class HiringOutreachDiscoveryRequestConfiguration : IEntityTypeConfiguration<HiringOutreachDiscoveryRequest>
{
    public void Configure(EntityTypeBuilder<HiringOutreachDiscoveryRequest> builder)
    {
        builder.ToTable("HiringOutreachDiscoveryRequests"); builder.ConfigureBaseEntity();
        builder.HasIndex(x=>new { x.CandidateId,x.RequestKey }).IsUnique();
        builder.HasOne<User>().WithMany().HasForeignKey(x=>x.CandidateId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Job>().WithMany().HasForeignKey(x=>x.JobId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<HiringOutreachSession>().WithMany().HasForeignKey(x=>x.SessionId).OnDelete(DeleteBehavior.Restrict);
    }
}
public sealed class HiringOutreachDraftConfiguration : IEntityTypeConfiguration<HiringOutreachDraft>
{
    public void Configure(EntityTypeBuilder<HiringOutreachDraft> builder)
    {
        builder.ToTable("HiringOutreachDrafts"); builder.ConfigureBaseEntity();
        builder.Property(x=>x.RecipientIdsJson).HasColumnType("jsonb"); builder.Property(x=>x.Subject).HasMaxLength(200);
        builder.Property(x=>x.Body).HasMaxLength(10000); builder.Property(x=>x.Source).HasMaxLength(30);
        builder.HasOne<HiringOutreachSession>().WithMany().HasForeignKey(x=>x.SessionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ResumeDocumentBlob>().WithMany().HasForeignKey(x=>x.ResumeId).OnDelete(DeleteBehavior.Restrict);
    }
}
public sealed class HiringOutreachSendConfiguration : IEntityTypeConfiguration<HiringOutreachSend>
{
    public void Configure(EntityTypeBuilder<HiringOutreachSend> builder)
    {
        builder.ToTable("HiringOutreachSends"); builder.ConfigureBaseEntity();
        builder.HasIndex(x=>new {x.CandidateId,x.SessionId,x.ContactId,x.RequestKey}).IsUnique();
        builder.HasIndex(x=>new {x.CandidateId,x.JobId,x.EmailSnapshot});
        builder.HasIndex(x=>new {x.CandidateId,x.CreatedAtUtc}); builder.Property(x=>x.Revision).IsConcurrencyToken();
        builder.Property(x=>x.PayloadHash).HasMaxLength(64); builder.Property(x=>x.EmailSnapshot).HasMaxLength(254);
        builder.Property(x=>x.Subject).HasMaxLength(200); builder.Property(x=>x.Body).HasMaxLength(10000);
        builder.Property(x=>x.State).HasMaxLength(40); builder.Property(x=>x.FailureCode).HasMaxLength(80);
        builder.Property(x=>x.ProviderMessageId).HasMaxLength(300); builder.Property(x=>x.ProviderThreadId).HasMaxLength(300);
        builder.HasOne<User>().WithMany().HasForeignKey(x=>x.CandidateId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Job>().WithMany().HasForeignKey(x=>x.JobId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<HiringOutreachSession>().WithMany().HasForeignKey(x=>x.SessionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<HiringOutreachContact>().WithMany().HasForeignKey(x=>x.ContactId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<HiringOutreachDraft>().WithMany().HasForeignKey(x=>x.DraftId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ResumeDocumentBlob>().WithMany().HasForeignKey(x=>x.ResumeId).OnDelete(DeleteBehavior.Restrict);
    }
}
public sealed class HiringMailboxSecretConfiguration : IEntityTypeConfiguration<HiringMailboxSecret>
{
    public void Configure(EntityTypeBuilder<HiringMailboxSecret> builder)
    {
        builder.ToTable("HiringMailboxSecrets"); builder.ConfigureBaseEntity(); builder.HasIndex(x=>x.MailboxId).IsUnique();
        builder.HasOne<CandidateMailboxConnection>().WithMany().HasForeignKey(x=>x.MailboxId).OnDelete(DeleteBehavior.Restrict);
    }
}
public sealed class HiringMailboxAuthorizationConfiguration : IEntityTypeConfiguration<HiringMailboxAuthorization>
{
    public void Configure(EntityTypeBuilder<HiringMailboxAuthorization> builder)
    {
        builder.ToTable("HiringMailboxAuthorizations"); builder.ConfigureBaseEntity(); builder.HasIndex(x=>x.StateHash).IsUnique();
        builder.Property(x=>x.StateHash).HasMaxLength(64); builder.Property(x=>x.BrowserHash).HasMaxLength(64);
        builder.HasOne<User>().WithMany().HasForeignKey(x=>x.CandidateId).OnDelete(DeleteBehavior.Restrict);
    }
}
