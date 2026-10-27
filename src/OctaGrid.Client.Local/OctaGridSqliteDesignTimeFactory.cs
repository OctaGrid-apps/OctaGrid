using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using OctaGrid.Data.Abstractions;

namespace OctaGrid.Client.Local;

/// <summary>
/// Lets `dotnet ef migrations add` construct <see cref="OctaGridContext"/> against SQLite without
/// a running host — Client.Local is a plain class library with no Main. Only used at design time;
/// the real connection string comes from AddOctoGridClientLocal at runtime.
/// </summary>
public sealed class OctaGridSqliteDesignTimeFactory : IDesignTimeDbContextFactory<OctaGridContext>
{
    public OctaGridContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<OctaGridContext>()
            .UseSqlite("Data Source=octagrid.db", b => b.MigrationsAssembly("OctaGrid.Client.Local"))
            .Options;

        return new OctaGridContext(options);
    }
}
