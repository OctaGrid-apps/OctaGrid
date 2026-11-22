using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OctaGrid.Data.Models;

namespace OctaGrid.Data.Configurations;

internal sealed class PrinterStatusConfiguration : IEntityTypeConfiguration<PrinterStatus>
{
    public void Configure(EntityTypeBuilder<PrinterStatus> builder)
    {
        builder.HasKey(x => x.PrinterId);

        // Null until the agent reports a state.
        builder.Property(u => u.State)
            .HasConversion<string>()
            .HasMaxLength(50);

        builder.HasOne(s => s.Printer)
            .WithOne(s => s.Status)
            .HasForeignKey<PrinterStatus>(x => x.PrinterId)
            .IsRequired();
    }
}
