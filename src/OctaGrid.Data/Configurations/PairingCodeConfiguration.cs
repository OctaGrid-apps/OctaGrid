using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OctaGrid.Data.Models;

namespace OctaGrid.Data.Configurations;

public class PairingCodeConfiguration : IEntityTypeConfiguration<PairingCode>
{
    public void Configure(EntityTypeBuilder<PairingCode> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .ValueGeneratedNever();

        builder.Property(x => x.CodeHash)
            .IsRequired();

        builder.HasIndex(x => x.CodeHash)
            .IsUnique();

        builder.Property(x => x.AccountId)
            .IsRequired();

        builder.HasIndex(x => new { x.AccountId, x.Consumed });

        builder.Property(x => x.DeviceId);

        builder.Property(x => x.Consumed)
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(x => x.AttemptCount)
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(x => x.ExpiresAt)
            .IsRequired();

        builder.Property(x => x.CreatedAt)
            .IsRequired();

        builder.HasIndex(x => x.ExpiresAt);
    }
}
