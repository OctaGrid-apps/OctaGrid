using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OctaGrid.Data.Abstractions.Models;

namespace OctaGrid.Data.Abstractions.Configurations;

/// <summary>
/// Base configuration for every entity deriving from <see cref="Entity"/>: the primary key,
/// audit timestamps, and the optimistic concurrency token. <see cref="Entity.Id"/> and
/// <see cref="Entity.RowVersion"/> are generated in app code (see
/// <see cref="OctaGridContext"/>), never by the database provider, so behavior is identical
/// across SQLite and Postgres.
/// </summary>
internal abstract class EntityConfiguration<TEntity> : IEntityTypeConfiguration<TEntity>
    where TEntity : Entity
{
    public void Configure(EntityTypeBuilder<TEntity> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .ValueGeneratedNever();

        builder.Property(e => e.CreatedAt)
            .IsRequired();

        builder.Property(e => e.UpdatedAt)
            .IsRequired();

        builder.Property(e => e.Version)
            .IsRequired()
            .IsConcurrencyToken();

        ConfigureEntity(builder);
    }

    protected abstract void ConfigureEntity(EntityTypeBuilder<TEntity> builder);
}
