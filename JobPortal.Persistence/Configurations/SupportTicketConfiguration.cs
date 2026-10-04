using JobPortal.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobPortal.Persistence.Configurations;

public sealed class SupportTicketConfiguration : IEntityTypeConfiguration<SupportTicket>
{
    public void Configure(EntityTypeBuilder<SupportTicket> builder)
    {
        builder.ToTable("SupportTickets", table =>
        {
            table.HasCheckConstraint("CK_SupportTickets_Category", "\"Category\" BETWEEN 1 AND 10");
            table.HasCheckConstraint("CK_SupportTickets_Status", "\"Status\" BETWEEN 1 AND 4");
            table.HasCheckConstraint("CK_SupportTickets_ResolvedAtUtc",
                "(\"Status\" = 3 AND \"ResolvedAtUtc\" IS NOT NULL) OR (\"Status\" <> 3 AND \"ResolvedAtUtc\" IS NULL)");
        });
        builder.ConfigureBaseEntity();
        builder.Property(x => x.TicketNumber).HasMaxLength(40).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(201).IsRequired();
        builder.Property(x => x.Email).HasMaxLength(256).IsRequired();
        builder.Property(x => x.Subject).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(5000).IsRequired();
        builder.Property(x => x.ScreenshotPath).HasMaxLength(40);
        builder.Property(x => x.AdminNotes).HasMaxLength(5000);
        builder.Property(x => x.Revision).IsConcurrencyToken();
        builder.HasIndex(x => x.TicketNumber).IsUnique();
        builder.HasIndex(x => new { x.UserId, x.CreatedAtUtc });
        builder.HasIndex(x => new { x.Status, x.CreatedAtUtc });
        builder.HasIndex(x => new { x.Category, x.CreatedAtUtc });
        builder.HasIndex(x => x.CreatedAtUtc);
        builder.HasIndex(x => x.Email);
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}
