using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OctaGrid.Data.Abstractions.Models;

namespace OctaGrid.Data.Abstractions.Configurations;

internal sealed class DashboardConfiguration : EntityConfiguration<Dashboard>
{
    protected override void ConfigureEntity(EntityTypeBuilder<Dashboard> builder)
    {
        builder.Property(u => u.Name)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(u => u.IsDefault);

        builder.Property(u => u.Type)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.HasOne(s => s.Workspace)
            .WithMany(w => w.Dashboards)
            .HasForeignKey(s => s.WorkspaceId)
            .IsRequired();
    }
}
