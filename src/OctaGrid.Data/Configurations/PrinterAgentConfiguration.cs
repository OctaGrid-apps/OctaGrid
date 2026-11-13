using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OctaGrid.Data.Models;

namespace OctaGrid.Data.Configurations;

internal sealed class PrinterAgentConfiguration : EntityConfiguration<PrinterAgent>
{
    protected override void ConfigureEntity(EntityTypeBuilder<PrinterAgent> builder)
    {
        builder.HasIndex(pa => new { pa.PrinterId, pa.AgentId })
            .IsUnique();

        builder.HasOne(pa => pa.Printer)
            .WithMany(p => p.PrinterAgents)
            .HasForeignKey(pa => pa.PrinterId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(pa => pa.Agent)
            .WithMany(a => a.PrinterAgents)
            .HasForeignKey(pa => pa.AgentId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);
    }
}
