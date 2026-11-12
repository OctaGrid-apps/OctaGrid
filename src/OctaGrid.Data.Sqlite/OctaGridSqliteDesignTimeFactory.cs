using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace OctaGrid.Data.Sqlite;

/// <summary>
/// Lets `dotnet ef migrations add` construct <see cref="OctaGridContext"/> against SQLite without
/// a running host: this project is a plain class library with no Main. Only used at design time;
/// the real connection string comes from <see cref="OctaGridSqlite.AddOctaGridSqlite"/> at runtime.
/// </summary>
public sealed class OctaGridSqliteDesignTimeFactory : IDesignTimeDbContextFactory<OctaGridContext>
{
    public OctaGridContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<OctaGridContext>()
            .UseSqlite("Data Source=octagrid.db", b => b.MigrationsAssembly(OctaGridSqlite.MigrationsAssembly))
            .Options;

        return new OctaGridContext(options);
    }
}
