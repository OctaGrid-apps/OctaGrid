using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OctaGrid.Data.Models;

namespace OctaGrid.Data.Configurations;

internal sealed class AgentStatusConfiguration : IEntityTypeConfiguration<AgentStatus>
{
    public void Configure(EntityTypeBuilder<AgentStatus> builder)
    {
        builder.HasKey(x => x.AgentId);

        builder.Property(u => u.State)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.HasOne(s => s.Agent)
            .WithOne(s => s.Status)
            .HasForeignKey<AgentStatus>(x => x.AgentId)
            .IsRequired();
    }
}
