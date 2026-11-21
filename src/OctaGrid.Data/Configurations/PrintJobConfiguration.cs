using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OctaGrid.Data.Models;

namespace OctaGrid.Data.Configurations;

internal sealed class PrintJobConfiguration : EntityConfiguration<PrintJob>
{
    protected override void ConfigureEntity(EntityTypeBuilder<PrintJob> builder)
    {
        builder.Property(j => j.WorkspaceId)
            .IsRequired();

        builder.Property(j => j.FileName)
            .HasMaxLength(512);

        builder.Property(j => j.StartedAt)
            .IsRequired();

        builder.Property(j => j.Outcome)
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Ignore(j => j.IsRunning);

        // By id only, as between aggregates; the jobs go with their printer.
        builder.HasOne<Printer>()
            .WithMany()
            .HasForeignKey(j => j.PrinterId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);

        // Finding the running job of a printer.
        builder.HasIndex(j => new { j.PrinterId, j.Outcome });

        builder.HasIndex(j => j.WorkspaceId);
    }
}
