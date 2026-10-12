using JobPortal.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobPortal.Persistence.Configurations;

public sealed class JobSourceRunItemConfiguration : IEntityTypeConfiguration<JobSourceRunItem>
{
    public void Configure(EntityTypeBuilder<JobSourceRunItem> builder)
    {
        builder.ToTable("JobSourceRunItems", table =>
            table.HasCheckConstraint("CK_JobSourceRunItems_Ordinal", "\"Ordinal\" BETWEEN 0 AND 9999"));
        builder.HasKey(x => new { x.RunId, x.Ordinal });
        builder.Property(x => x.ExternalId).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ValidationReason).HasMaxLength(64);
        builder.Property(x => x.PublicationReasonsJson).HasColumnType("jsonb");
        builder.HasOne<JobSourceRun>().WithMany().HasForeignKey(x => x.RunId).OnDelete(DeleteBehavior.Restrict);
    }
}
