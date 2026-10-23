using JobPortal.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobPortal.Persistence.Configurations;

public sealed class JobPromotionConfiguration : IEntityTypeConfiguration<JobPromotion>
{
    public void Configure(EntityTypeBuilder<JobPromotion> builder)
    {
        builder.ToTable("JobPromotions", table =>
        {
            table.HasCheckConstraint("CK_JobPromotions_Dates", "\"EndsAtUtc\" IS NULL OR \"StartsAtUtc\" IS NULL OR \"EndsAtUtc\" > \"StartsAtUtc\"");
            table.HasCheckConstraint("CK_JobPromotions_DisplayPriority", "\"DisplayPriority\" >= 0");
            table.HasCheckConstraint("CK_JobPromotions_Banner", "(\"BannerImageContent\" IS NULL AND \"BannerContentType\" IS NULL AND \"BannerImageUrl\" IS NULL) OR (\"BannerImageContent\" IS NOT NULL AND \"BannerContentType\" IN ('image/jpeg', 'image/png', 'image/webp') AND \"BannerImageUrl\" IS NOT NULL)");
            table.HasCheckConstraint("CK_JobPromotions_BannerSize", "\"BannerImageContent\" IS NULL OR octet_length(\"BannerImageContent\") BETWEEN 1 AND 5242880");
        });
        builder.ConfigureBaseEntity();
        builder.Property(x => x.BannerImageUrl).HasMaxLength(2048);
        builder.Property(x => x.BannerImageContent).HasColumnType("bytea");
        builder.Property(x => x.BannerContentType).HasMaxLength(100);
        builder.HasIndex(x => x.JobId).IsUnique();
        builder.HasIndex(x => new { x.IsEnabled, x.StartsAtUtc, x.EndsAtUtc, x.DisplayPriority })
            .HasFilter("\"IsDeleted\" = FALSE");
        builder.HasOne(x => x.Job).WithMany().HasForeignKey(x => x.JobId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.UpdatedByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
