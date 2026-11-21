using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OctaGrid.Data.Models;

namespace OctaGrid.Data.Configurations;

internal sealed class WorkspaceMemberConfiguration : EntityConfiguration<WorkspaceMember>
{
    protected override void ConfigureEntity(EntityTypeBuilder<WorkspaceMember> builder)
    {
        builder.Property(wm => wm.Role)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(50);

        builder.Property(wm => wm.JoinedAt)
            .IsRequired();

        builder.HasIndex(wm => new { wm.WorkspaceId, wm.UserId })
            .IsUnique();

        builder.HasOne(wm => wm.Workspace)
            .WithMany(w => w.Members)
            .HasForeignKey(wm => wm.WorkspaceId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);

        // By id only: a user is another aggregate.
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(wm => wm.UserId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);
    }
}
