using JobPortal.Domain.Common;
using JobPortal.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobPortal.Persistence.Configurations;

internal static class AIResumeMapping
{
    internal static void Base<T>(EntityTypeBuilder<T> builder, string table) where T : BaseEntity
    { builder.ToTable(table); builder.ConfigureBaseEntity(); }
    internal static void User<T>(EntityTypeBuilder<T> builder) where T : BaseEntity =>
        builder.HasOne<User>().WithMany().HasForeignKey("UserId").OnDelete(DeleteBehavior.Restrict);
}
public sealed class AIResumeSessionConfiguration : IEntityTypeConfiguration<AIResumeSession>
{
    public void Configure(EntityTypeBuilder<AIResumeSession> builder)
    {
        AIResumeMapping.Base(builder, "AIResumeSessions"); AIResumeMapping.User(builder);
        builder.HasOne<CandidateResumeProfile>().WithMany().HasForeignKey(x => x.SourceResumeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Job>().WithMany().HasForeignKey(x => x.JobId).OnDelete(DeleteBehavior.Restrict);
        builder.Property(x => x.SourceType).HasMaxLength(32);
        builder.Property(x => x.JobTitle).HasMaxLength(256); builder.Property(x => x.CompanyName).HasMaxLength(256);
        builder.Property(x => x.JobDescription).HasMaxLength(20000);
        builder.Property(x => x.SourceJson).HasColumnType("jsonb"); builder.Property(x => x.EvidenceJson).HasColumnType("jsonb");
        builder.Property(x => x.AnalysisJson).HasColumnType("jsonb"); builder.Property(x => x.AnalysisModel).HasMaxLength(100);
        builder.HasIndex(x => new { x.UserId, x.CreatedAtUtc });
        builder.ToTable(t => t.HasCheckConstraint("CK_AIResumeSessions_Status", "\"Status\" BETWEEN 1 AND 6"));
    }
}
public sealed class AIResumeWalletConfiguration : IEntityTypeConfiguration<AIResumeCreditWallet>
{
    public void Configure(EntityTypeBuilder<AIResumeCreditWallet> builder)
    {
        AIResumeMapping.Base(builder, "AIResumeCreditWallets"); AIResumeMapping.User(builder);
        builder.HasIndex(x => x.UserId).IsUnique();
        builder.ToTable(t => t.HasCheckConstraint("CK_AIResumeWallet_Nonnegative", "\"Balance\" >= 0 AND \"Reserved\" >= 0 AND \"LifetimePurchased\" >= 0 AND \"LifetimeConsumed\" >= 0 AND \"LifetimePurchased\" = \"Balance\" + \"Reserved\" + \"LifetimeConsumed\""));
    }
}
public sealed class AIResumePurchaseConfiguration : IEntityTypeConfiguration<AIResumePurchase>
{
    public void Configure(EntityTypeBuilder<AIResumePurchase> builder)
    {
        AIResumeMapping.Base(builder, "AIResumePurchases"); AIResumeMapping.User(builder);
        builder.HasOne<AIResumeSession>().WithMany().HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.Restrict);
        builder.Property(x => x.PackageCode).HasMaxLength(50); builder.Property(x => x.CurrencyCode).HasMaxLength(3);
        builder.Property(x => x.Amount).HasPrecision(18, 2); builder.Property(x => x.MerchantOrderId).HasMaxLength(100);
        builder.Property(x => x.ProviderPaymentId).HasMaxLength(200); builder.Property(x => x.RedirectUrl).HasMaxLength(2048);
        builder.HasIndex(x => new { x.UserId, x.RequestKey }).IsUnique(); builder.HasIndex(x => x.MerchantOrderId).IsUnique();
        builder.ToTable(t => t.HasCheckConstraint("CK_AIResumePurchase_Snapshot", "\"Credits\" > 0 AND \"Amount\" > 0 AND \"CurrencyCode\" = 'INR'"));
    }
}
public sealed class AIResumeGenerationConfiguration : IEntityTypeConfiguration<AIResumeGeneration>
{
    public void Configure(EntityTypeBuilder<AIResumeGeneration> builder)
    {
        AIResumeMapping.Base(builder, "AIResumeGenerations"); AIResumeMapping.User(builder);
        builder.HasOne<AIResumeSession>().WithMany().HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.Restrict);
        builder.Property(x => x.FailureCode).HasMaxLength(100);
        builder.HasIndex(x => new { x.UserId, x.RequestKey }).IsUnique();
        builder.HasIndex(x => x.SessionId).IsUnique().HasFilter("\"Status\" = 1");
        builder.HasIndex(x => new { x.Status, x.LeaseUntilUtc });
        builder.ToTable(t => t.HasCheckConstraint("CK_AIResumeGeneration_Status", "\"Status\" BETWEEN 1 AND 3"));
    }
}
public sealed class TailoredResumeConfiguration : IEntityTypeConfiguration<TailoredResume>
{
    public void Configure(EntityTypeBuilder<TailoredResume> builder)
    {
        AIResumeMapping.Base(builder, "TailoredResumes"); AIResumeMapping.User(builder);
        builder.HasOne<AIResumeSession>().WithMany().HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AIResumeGeneration>().WithMany().HasForeignKey(x => x.GenerationId).OnDelete(DeleteBehavior.Restrict);
        builder.Property(x => x.ContentJson).HasColumnType("jsonb"); builder.Property(x => x.TemplateCode).HasMaxLength(50);
        builder.Property(x => x.GenerationModel).HasMaxLength(100);
        builder.HasIndex(x => x.GenerationId).IsUnique(); builder.HasIndex(x => new { x.SessionId, x.Version }).IsUnique();
        builder.HasIndex(x => new { x.UserId, x.CreatedAtUtc });
        builder.ToTable(t => t.HasCheckConstraint("CK_TailoredResume_Version", "\"Version\" > 0 AND \"EditRevision\" >= 0"));
    }
}
public sealed class TailoredResumeEditConfiguration : IEntityTypeConfiguration<TailoredResumeEdit>
{
    public void Configure(EntityTypeBuilder<TailoredResumeEdit> builder)
    {
        AIResumeMapping.Base(builder, "TailoredResumeEdits");
        builder.HasOne<TailoredResume>().WithMany().HasForeignKey(x => x.ResumeId).OnDelete(DeleteBehavior.Restrict);
        builder.Property(x => x.ContentJson).HasColumnType("jsonb");
        builder.HasIndex(x => new { x.ResumeId, x.Revision }).IsUnique();
    }
}
public sealed class AIResumeLedgerConfiguration : IEntityTypeConfiguration<AIResumeCreditTransaction>
{
    public void Configure(EntityTypeBuilder<AIResumeCreditTransaction> builder)
    {
        AIResumeMapping.Base(builder, "AIResumeCreditTransactions"); AIResumeMapping.User(builder);
        builder.HasOne<AIResumePurchase>().WithMany().HasForeignKey(x => x.PurchaseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AIResumeGeneration>().WithMany().HasForeignKey(x => x.GenerationId).OnDelete(DeleteBehavior.Restrict);
        builder.Property(x => x.IdempotencyKey).HasMaxLength(150); builder.HasIndex(x => x.IdempotencyKey).IsUnique();
        builder.HasIndex(x => new { x.PurchaseId, x.Kind }).IsUnique().HasFilter("\"PurchaseId\" IS NOT NULL");
        // A generation may be retried after a released reservation. The ledger key is unique per attempt.
        builder.HasIndex(x => new { x.GenerationId, x.Kind }).HasFilter("\"GenerationId\" IS NOT NULL");
        builder.HasIndex(x => new { x.UserId, x.CreatedAtUtc });
        builder.ToTable(t => t.HasCheckConstraint("CK_AIResumeLedger_Nonnegative", "\"BalanceAfter\" >= 0 AND \"ReservedAfter\" >= 0"));
    }
}
