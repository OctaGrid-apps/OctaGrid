using Microsoft.EntityFrameworkCore;
using OctaGrid.Data.Abstractions.Models;

namespace OctaGrid.Data.Abstractions;

public class OctaGridContext : DbContext
{
    public OctaGridContext(DbContextOptions<OctaGridContext> options) : base(options)
    {
    }

    public DbSet<Agent> Agents => Set<Agent>();
    public DbSet<Dashboard> Dashboards => Set<Dashboard>();
    public DbSet<DashboardWidget> DashboardWidgets => Set<DashboardWidget>();
    public DbSet<PairingCode> PairingCodes => Set<PairingCode>();
    public DbSet<PrinterAgent> PrinterAgents => Set<PrinterAgent>();
    public DbSet<Printer> Printers => Set<Printer>();
    public DbSet<User> Users => Set<User>();
    public DbSet<WorkspaceMember> WorkspaceMembers => Set<WorkspaceMember>();
    public DbSet<Workspace> Workspaces => Set<Workspace>();

    public override int SaveChanges()
    {
        ApplyEntityConventions();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ApplyEntityConventions();
        return base.SaveChangesAsync(cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(OctaGridContext).Assembly);
    }

    /// <summary>
    /// Assigns the GUIDv7 id, audit timestamps, and rotates the concurrency token in app code —
    /// never relying on the database provider for any of them — so behavior is identical across
    /// SQLite and Postgres.
    /// </summary>
    private void ApplyEntityConventions()
    {
        var entries = ChangeTracker.Entries<Entity>();
        var utcNow = DateTimeOffset.UtcNow;

        foreach (var entry in entries)
        {
            if (entry.State == EntityState.Added)
            {
                if (entry.Entity.Id == Guid.Empty)
                {
                    entry.Entity.Id = Guid.CreateVersion7();
                }
                if (entry.Entity.CreatedAt == default)
                {
                    entry.Entity.CreatedAt = utcNow;
                }
                if (entry.Entity.UpdatedAt == default)
                {
                    entry.Entity.UpdatedAt = utcNow;
                }
                entry.Entity.Version = 0;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = utcNow;
                entry.Entity.Version++;
            }
        }
    }
}
