using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OctaGrid.Data.Abstractions.Models;

namespace OctaGrid.Data.Abstractions.Configurations;

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

        builder.Property(u => u.EndpointAddress)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(u => u.EncryptedApiKey)
            .HasMaxLength(1000);

        builder.HasOne(s => s.Workspace)
            .WithMany(w => w.Printers)
            .HasForeignKey(s => s.WorkspaceId)
            .IsRequired();

        // The Printer<->Agent many-to-many is configured entirely by PrinterAgentConfiguration,
        // since PrinterAgent is a first-class entity (its own Id/timestamps), not an implicit join
        // table. Access it via PrinterAgents, not a skip-navigation, to keep one source of truth.
    }
}
