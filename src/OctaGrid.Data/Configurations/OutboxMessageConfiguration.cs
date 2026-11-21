using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OctaGrid.Data.Models;

namespace OctaGrid.Data.Configurations;

internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.HasKey(m => m.Sequence);

        builder.Property(m => m.Sequence)
            .ValueGeneratedOnAdd();

        builder.Property(m => m.EventId)
            .IsRequired();

        builder.HasIndex(m => m.EventId)
            .IsUnique();

        builder.Property(m => m.Type)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(m => m.Payload)
            .IsRequired();

        builder.Property(m => m.OccurredAt)
            .IsRequired();

        builder.Property(m => m.Attempts)
            .IsRequired();

        builder.Property(m => m.LastError)
            .HasMaxLength(OutboxMessage.MaxErrorLength);

        // Finding what still waits.
        builder.HasIndex(m => m.ProcessedAt);
    }
}
