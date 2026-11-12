using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OctaGrid.Data.Models;

namespace OctaGrid.Data.Configurations;

internal sealed class DashboardWidgetConfiguration : EntityConfiguration<DashboardWidget>
{
    protected override void ConfigureEntity(EntityTypeBuilder<DashboardWidget> builder)
    {
        builder.Property(u => u.WidgetType)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(50);

        builder.Property(u => u.GridX)
            .IsRequired();

        builder.Property(u => u.GridY)
            .IsRequired();

        builder.Property(u => u.GridWidth)
            .IsRequired();

        builder.Property(u => u.GridHeight)
            .IsRequired();

        builder.Property(u => u.ConfigJson)
            .IsRequired()
            .HasMaxLength(4000);

        builder.HasOne(s => s.Dashboard)
            .WithMany(w => w.Widgets)
            .HasForeignKey(s => s.DashboardId)
            .IsRequired();

        builder.HasOne(s => s.Printer)
            .WithMany()
            .HasForeignKey(s => s.PrinterId)
            .IsRequired();
    }
}
