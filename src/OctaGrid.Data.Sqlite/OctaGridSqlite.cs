using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace OctaGrid.Data.Sqlite;

/// <summary>Registers and prepares <see cref="OctaGridContext"/> on SQLite.</summary>
public static class OctaGridSqlite
{
    /// <summary>The assembly that holds the SQLite migrations.</summary>
    internal static readonly string MigrationsAssembly = typeof(OctaGridSqlite).Assembly.GetName().Name!;

    /// <summary>Registers <see cref="OctaGridContext"/> (scoped) on a SQLite file.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="databasePath">
    /// Returns the path of the database file. It is called when the first context is created, not during
    /// registration, so registering touches no files.
    /// </param>
    public static IServiceCollection AddOctaGridSqlite(this IServiceCollection services, Func<string> databasePath)
    {
        ArgumentNullException.ThrowIfNull(databasePath);

        return services.AddDbContext<OctaGridContext>(options =>
        {
            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = databasePath(),
                Pooling = true,
                DefaultTimeout = 30
            }.ToString();

            options.UseSqlite(connectionString, b => b.MigrationsAssembly(MigrationsAssembly));
        });
    }

    /// <summary>Applies the pending migrations and the pragmas the app runs SQLite with.</summary>
    public static void MigrateSqlite(this OctaGridContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.Database.Migrate();

        // SQLite-only settings, which is why raw SQL is acceptable here and nowhere in the services.
        context.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA temp_store=MEMORY;");
    }
}
