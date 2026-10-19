using JobPortal.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobPortal.Persistence.Configurations;

public sealed class JobSourceVacancyConfiguration : IEntityTypeConfiguration<JobSourceVacancy>
{
    public void Configure(EntityTypeBuilder<JobSourceVacancy> builder)
    {
        builder.ToTable("JobSourceVacancies");
        builder.HasKey(x => new { x.JobSourceId, x.ExternalJobId });
        builder.Property(x => x.ExternalJobId).HasMaxLength(255);
        builder.HasIndex(x => x.JobId);
        builder.HasOne<JobSource>().WithMany().HasForeignKey(x => x.JobSourceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Job>().WithMany().HasForeignKey(x => x.JobId).OnDelete(DeleteBehavior.Restrict);
    }
}
