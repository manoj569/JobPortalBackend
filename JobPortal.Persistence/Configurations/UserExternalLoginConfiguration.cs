using JobPortal.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobPortal.Persistence.Configurations;

public sealed class UserExternalLoginConfiguration : IEntityTypeConfiguration<UserExternalLogin>
{
    public void Configure(EntityTypeBuilder<UserExternalLogin> builder)
    {
        builder.ToTable("UserExternalLogins", table =>
            table.HasCheckConstraint("CK_UserExternalLogins_Provider", "\"Provider\" = 1"));
        builder.ConfigureBaseEntity();
        builder.Property(x => x.ProviderSubject).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ProviderEmail).HasMaxLength(256).IsRequired();
        builder.HasIndex(x => new { x.Provider, x.ProviderSubject })
            .IsUnique().HasFilter("\"IsDeleted\" = FALSE");
        builder.HasIndex(x => new { x.UserId, x.Provider })
            .IsUnique().HasFilter("\"IsDeleted\" = FALSE");
        builder.HasIndex(x => new { x.Provider, x.ProviderEmail });
        builder.HasOne(x => x.User).WithMany(x => x.ExternalLogins)
            .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
