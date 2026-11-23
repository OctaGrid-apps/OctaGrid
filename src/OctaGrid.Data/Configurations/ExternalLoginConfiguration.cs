using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OctaGrid.Data.Models;

namespace OctaGrid.Data.Configurations;

internal sealed class ExternalLoginConfiguration : EntityConfiguration<ExternalLogin>
{
    protected override void ConfigureEntity(EntityTypeBuilder<ExternalLogin> builder)
    {
        builder.Property(l => l.Issuer)
            .IsRequired()
            .HasMaxLength(ExternalLogin.MaxLength);

        builder.Property(l => l.Subject)
            .IsRequired()
            .HasMaxLength(ExternalLogin.MaxLength);

        // One provider account signs in as one user, even when two first sign-ins race.
        builder.HasIndex(l => new { l.Issuer, l.Subject })
            .IsUnique();

        builder.HasOne<User>()
            .WithMany(u => u.Logins)
            .HasForeignKey(l => l.UserId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);
    }
}
