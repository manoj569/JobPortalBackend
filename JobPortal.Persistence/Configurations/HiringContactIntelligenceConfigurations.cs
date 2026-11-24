using JobPortal.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobPortal.Persistence.Configurations;

public sealed class HiringContactIntelligenceConfiguration : IEntityTypeConfiguration<HiringContactIntelligence>
{
    public void Configure(EntityTypeBuilder<HiringContactIntelligence> builder)
    {
        builder.ToTable("HiringContactIntelligence"); builder.ConfigureBaseEntity();
        builder.HasOne<Company>().WithMany().HasForeignKey(x=>x.CompanyId).OnDelete(DeleteBehavior.Restrict);
        // Unfiltered: a soft delete must never allow a duplicate normalized identity.
        builder.HasIndex(x=>new {x.CompanyDomain,x.EmailHash}).IsUnique();
        builder.HasIndex(x=>new {x.CompanyId,x.CompanyDomain,x.IsActive,x.ExpiresAtUtc});
        builder.HasIndex(x=>new {x.CompanyDomain,x.JobFamily,x.Location});
        builder.Property(x=>x.CompanyDomain).HasMaxLength(253); builder.Property(x=>x.EmailHash).HasMaxLength(64);
        builder.Property(x=>x.Title).HasMaxLength(200); builder.Property(x=>x.Department).HasMaxLength(200);
        builder.Property(x=>x.Location).HasMaxLength(200); builder.Property(x=>x.JobFamily).HasMaxLength(40);
        builder.Property(x=>x.ContactType).HasMaxLength(40); builder.Property(x=>x.Verification).HasMaxLength(30);
        builder.Property(x=>x.Provider).HasMaxLength(30); builder.Property(x=>x.Source).HasMaxLength(40);
        builder.Property(x=>x.Revision).IsConcurrencyToken();
    }
}
public sealed class HiringContactDiscoveryLeaseConfiguration : IEntityTypeConfiguration<HiringContactDiscoveryLease>
{
    public void Configure(EntityTypeBuilder<HiringContactDiscoveryLease> builder)
    {
        builder.ToTable("HiringContactDiscoveryLeases"); builder.ConfigureBaseEntity();
        builder.Property(x=>x.ScopeHash).HasMaxLength(64); builder.HasIndex(x=>x.ScopeHash).IsUnique();
        builder.HasIndex(x=>x.ExpiresAtUtc); builder.Property(x=>x.Revision).IsConcurrencyToken();
        builder.HasOne<HiringOutreachSession>().WithMany().HasForeignKey(x=>x.OwnerSessionId).OnDelete(DeleteBehavior.Restrict);
    }
}
