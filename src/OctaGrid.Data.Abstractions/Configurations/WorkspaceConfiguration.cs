using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OctaGrid.Data.Abstractions.Models;

namespace OctaGrid.Data.Abstractions.Configurations;

internal sealed class WorkspaceConfiguration : EntityConfiguration<Workspace>
{
    protected override void ConfigureEntity(EntityTypeBuilder<Workspace> builder)
    {
        builder.Property(u => u.Name)
            .IsRequired()
            .HasMaxLength(100);
    }
}
