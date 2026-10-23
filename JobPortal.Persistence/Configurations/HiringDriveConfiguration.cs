using JobPortal.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobPortal.Persistence.Configurations;

public sealed class HiringDriveConfiguration : IEntityTypeConfiguration<HiringDrive>
{
    public void Configure(EntityTypeBuilder<HiringDrive> builder)
    {
        builder.ToTable("HiringDrives", table =>
        {
            table.HasCheckConstraint("CK_HiringDrives_Dates", "\"EndAtUtc\" IS NULL OR \"EndAtUtc\" > \"StartAtUtc\"");
            table.HasCheckConstraint("CK_HiringDrives_DisplayOrder", "\"DisplayOrder\" >= 0");
            table.HasCheckConstraint("CK_HiringDrives_Counts", "\"ClickCount\" >= 0 AND \"ImpressionCount\" >= 0");
            table.HasCheckConstraint("CK_HiringDrives_Destination", "\"JobId\" IS NOT NULL OR \"ApplyUrl\" IS NOT NULL");
        });
        builder.ConfigureBaseEntity();
        builder.Property(x => x.Title).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Subtitle).HasMaxLength(300);
        builder.Property(x => x.Description).HasMaxLength(10000);
        builder.Property(x => x.BadgeText).HasMaxLength(60);
        builder.Property(x => x.BadgeType).HasMaxLength(30);
        builder.Property(x => x.ImageUrl).HasMaxLength(2048);
        builder.Property(x => x.ApplyUrl).HasMaxLength(2048);
        builder.Property(x => x.LocationText).HasMaxLength(200);
        builder.Property(x => x.ExperienceText).HasMaxLength(100);
        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Job).WithMany().HasForeignKey(x => x.JobId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.UpdatedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.IsActive, x.DisplayOrder, x.CreatedAtUtc }).HasFilter("\"IsDeleted\" = FALSE");
        builder.HasIndex(x => new { x.IsActive, x.StartAtUtc, x.EndAtUtc });
    }
}
