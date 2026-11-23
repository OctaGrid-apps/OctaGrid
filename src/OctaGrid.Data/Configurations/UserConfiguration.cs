using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OctaGrid.Data.Models;

namespace OctaGrid.Data.Configurations;

internal sealed class UserConfiguration : EntityConfiguration<User>
{
    protected override void ConfigureEntity(EntityTypeBuilder<User> builder)
    {
        builder.Property(u => u.Name)
            .IsRequired()
            .HasMaxLength(User.MaxNameLength);

        // Not unique: two providers may report the same address.
        builder.Property(u => u.Email)
            .IsRequired()
            .HasMaxLength(User.MaxEmailLength);
    }
}
