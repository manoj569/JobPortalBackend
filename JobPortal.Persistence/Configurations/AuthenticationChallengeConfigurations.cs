using JobPortal.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobPortal.Persistence.Configurations;

public sealed class PendingRegistrationConfiguration :
    IEntityTypeConfiguration<PendingRegistration>
{
    public void Configure(EntityTypeBuilder<PendingRegistration> builder)
    {
        builder.ToTable("PendingRegistrations");
        builder.ConfigureBaseEntity();
        builder.Property(x => x.Email).HasMaxLength(256).IsRequired();
        builder.Property(x => x.NormalizedEmail).HasMaxLength(256).IsRequired();
        builder.Property(x => x.PasswordHash).HasMaxLength(512);
        builder.Property(x => x.FirstName).HasMaxLength(100).IsRequired();
        builder.Property(x => x.LastName).HasMaxLength(100).IsRequired();
        builder.Property(x => x.NormalizedPhoneNumber).HasMaxLength(13).IsRequired();
        builder.Property(x => x.TermsAndPrivacyVersion).HasMaxLength(32).IsRequired();
        builder.HasIndex(x => x.NormalizedEmail)
            .IsUnique()
            .HasFilter("\"ClosedAtUtc\" IS NULL AND \"IsDeleted\" = FALSE");
        builder.HasIndex(x => x.NormalizedPhoneNumber)
            .IsUnique()
            .HasFilter("\"ClosedAtUtc\" IS NULL AND \"IsDeleted\" = FALSE");
        builder.HasIndex(x => new { x.ExpiresAtUtc, x.ClosedAtUtc });
        builder.HasOne(x => x.CompletedUser)
            .WithMany()
            .HasForeignKey(x => x.CompletedUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class OtpChallengeConfiguration :
    IEntityTypeConfiguration<OtpChallenge>
{
    public void Configure(EntityTypeBuilder<OtpChallenge> builder)
    {
        builder.ToTable(
            "OtpChallenges",
            table =>
            {
                table.HasCheckConstraint(
                    "CK_OtpChallenges_FailedAttemptCount",
                    "\"FailedAttemptCount\" BETWEEN 0 AND 5");
                table.HasCheckConstraint(
                    "CK_OtpChallenges_SendCount",
                    "\"SendCount\" >= 1");
            });
        builder.ConfigureBaseEntity();
        builder.Property(x => x.NormalizedPhoneNumber).HasMaxLength(13).IsRequired();
        builder.Property(x => x.OtpHash).HasMaxLength(64).IsRequired();
        builder.HasIndex(x => new
        {
            x.NormalizedPhoneNumber,
            x.Purpose,
            x.ConsumedAtUtc,
            x.ExpiresAtUtc
        });
        builder.HasIndex(x => new { x.Purpose, x.LastSentAtUtc });
        builder.HasIndex(x => x.PendingRegistrationId)
            .IsUnique()
            .HasFilter("\"PendingRegistrationId\" IS NOT NULL AND \"IsDeleted\" = FALSE");
        builder.HasOne(x => x.User)
            .WithMany(x => x.OtpChallenges)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.PendingRegistration)
            .WithMany(x => x.OtpChallenges)
            .HasForeignKey(x => x.PendingRegistrationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class RegistrationEmailRequestConfiguration :
    IEntityTypeConfiguration<RegistrationEmailRequest>
{
    public void Configure(EntityTypeBuilder<RegistrationEmailRequest> builder)
    {
        builder.ToTable("RegistrationEmailRequests", table =>
            table.HasCheckConstraint("CK_RegistrationEmailRequests_AttemptCount", "\"AttemptCount\" >= 0"));
        builder.ConfigureBaseEntity();
        builder.Property(x => x.VerificationToken).HasMaxLength(512).IsRequired();
        builder.HasIndex(x => new { x.SentAtUtc, x.NextAttemptAtUtc, x.LockedUntilUtc });
        builder.HasIndex(x => x.UserId).HasFilter("\"SentAtUtc\" IS NULL AND \"IsDeleted\" = FALSE");
        builder.HasOne(x => x.User).WithMany(x => x.RegistrationEmailRequests)
            .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.Property<uint>("xmin").HasColumnName("xmin").IsRowVersion();
    }
}
