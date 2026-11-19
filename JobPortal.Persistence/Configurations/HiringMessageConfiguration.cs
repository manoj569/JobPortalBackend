using JobPortal.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobPortal.Persistence.Configurations;

public sealed class HiringMessageConfiguration
    : IEntityTypeConfiguration<HiringMessage>
{
    public void Configure(
        EntityTypeBuilder<HiringMessage> builder)
    {
        builder.ToTable("HiringMessages");

        builder.ConfigureBaseEntity();

        builder.Property(x => x.Body)
            .HasMaxLength(5000)
            .IsRequired();

        builder.HasIndex(x => new
        {
            x.ConversationId,
            x.SentAtUtc
        });

        builder.HasOne(x => x.Conversation)
            .WithMany(x => x.Messages)
            .HasForeignKey(x => x.ConversationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.SenderUser)
            .WithMany()
            .HasForeignKey(x => x.SenderUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
