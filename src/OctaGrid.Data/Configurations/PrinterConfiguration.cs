using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OctaGrid.Data.Models;

namespace OctaGrid.Data.Configurations;

internal sealed class PrinterConfiguration : EntityConfiguration<Printer>
{
    protected override void ConfigureEntity(EntityTypeBuilder<Printer> builder)
    {
        builder.Property(u => u.Name)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(u => u.ReportedModel)
            .HasMaxLength(256);

        builder.Property(u => u.Enabled)
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(u => u.ConnectorId)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(u => u.Settings)
            .IsRequired()
            .HasConversion(DictionaryJson.Converter, DictionaryJson.Comparer);

        builder.HasOne(s => s.Workspace)
            .WithMany(w => w.Printers)
            .HasForeignKey(s => s.WorkspaceId)
            .IsRequired();

        // Removing an agent keeps its printers (with their settings and secrets); they wait for another agent.
        builder.HasOne(p => p.Agent)
            .WithMany(a => a.Printers)
            .HasForeignKey(p => p.AgentId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
