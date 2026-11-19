using JobPortal.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobPortal.Persistence.Configurations;

public sealed class HiringConversationConfiguration
    : IEntityTypeConfiguration<HiringConversation>
{
    public void Configure(
        EntityTypeBuilder<HiringConversation> builder)
    {
        builder.ToTable("HiringConversations");

        builder.ConfigureBaseEntity();

        builder.HasIndex(x => x.JobApplicationId)
            .IsUnique()
            .HasFilter("\"IsDeleted\" = FALSE");

        builder.HasIndex(x => new
        {
            x.EmployerUserId,
            x.LastMessageAtUtc
        });

        builder.HasIndex(x => new
        {
            x.CandidateUserId,
            x.LastMessageAtUtc
        });

        builder.HasOne(x => x.JobApplication)
            .WithMany()
            .HasForeignKey(x => x.JobApplicationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.CandidateUser)
            .WithMany()
            .HasForeignKey(x => x.CandidateUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.EmployerUser)
            .WithMany()
            .HasForeignKey(x => x.EmployerUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
