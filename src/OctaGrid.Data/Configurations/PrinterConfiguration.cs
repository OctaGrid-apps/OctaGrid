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

        // Workspace and agent by id only, as between aggregates.
        builder.HasOne<Workspace>()
            .WithMany()
            .HasForeignKey(s => s.WorkspaceId)
            .IsRequired();

        // Removing an agent keeps its printers (with their settings and secrets); they wait for another agent.
        builder.HasOne<Agent>()
            .WithMany()
            .HasForeignKey(p => p.AgentId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
