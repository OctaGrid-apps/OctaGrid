using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OctaGrid.Data.Abstractions.Models;

namespace OctaGrid.Data.Abstractions.Configurations;

internal sealed class AgentConfiguration : EntityConfiguration<Agent>
{
    protected override void ConfigureEntity(EntityTypeBuilder<Agent> builder)
    {
        builder.Property(u => u.Name)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(u => u.TokenPrefix)
            .HasMaxLength(32);

        builder.Property(u => u.TokenHash)
            .HasMaxLength(256);

        builder.Property(u => u.PublicKey)
            .HasMaxLength(256);

        builder.HasIndex(u => u.TokenPrefix);

        builder.Property(u => u.LastSeenAt);

        builder.HasOne(s => s.Workspace)
            .WithMany(w => w.Agents)
            .HasForeignKey(s => s.WorkspaceId)
            .IsRequired();
    }
}
