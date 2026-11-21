using Microsoft.EntityFrameworkCore;
using OctaGrid.Data.Models;

namespace OctaGrid.Data;

public class OctaGridContext : DbContext
{
    public OctaGridContext(DbContextOptions<OctaGridContext> options) : base(options)
    {
    }

    public DbSet<Agent> Agents => Set<Agent>();
    public DbSet<Dashboard> Dashboards => Set<Dashboard>();
    public DbSet<DashboardWidget> DashboardWidgets => Set<DashboardWidget>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<PairingCode> PairingCodes => Set<PairingCode>();
    public DbSet<Printer> Printers => Set<Printer>();
    public DbSet<PrintJob> PrintJobs => Set<PrintJob>();
    public DbSet<User> Users => Set<User>();
    public DbSet<WorkspaceMember> WorkspaceMembers => Set<WorkspaceMember>();
    public DbSet<Workspace> Workspaces => Set<Workspace>();

    public override int SaveChanges()
    {
        ApplyEntityConventions();
        StageDomainEvents();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ApplyEntityConventions();
        StageDomainEvents();
        return base.SaveChangesAsync(cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(OctaGridContext).Assembly);
    }

    /// <summary>
    /// Moves the events tracked entities raised into the outbox, so they are saved in the same transaction as the
    /// change. They leave the entities here: if the save fails, the outbox rows stay tracked and go with the retry.
    /// </summary>
    private void StageDomainEvents()
    {
        var raisers = ChangeTracker.Entries<Entity>()
            .Select(e => e.Entity)
            .Where(e => e.DomainEvents.Count > 0)
            .ToList();

        foreach (var entity in raisers)
        {
            OutboxMessages.AddRange(entity.DomainEvents.Select(OutboxMessage.From));
            entity.ClearDomainEvents();
        }
    }

    /// <summary>
    /// Marks a loaded <see cref="Workspace"/> as changed when one of its dashboards or members is added, changed or
    /// removed, so its version rotates too. Its rules span all of them (always a dashboard, exactly one default), and
    /// the version check on the workspace row is what stops two such changes made at the same time from both saving.
    /// A child changed without its workspace loaded (renaming a dashboard) touches no rule and leaves it alone.
    /// </summary>
    private void TouchChangedAggregateRoots()
    {
        var changed = ChangeTracker.Entries()
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .Select(e => e.Entity switch
            {
                Dashboard d => d.WorkspaceId,
                WorkspaceMember m => m.WorkspaceId,
                _ => Guid.Empty,
            })
            .Where(id => id != Guid.Empty)
            .ToHashSet();

        if (changed.Count == 0)
        {
            return;
        }

        foreach (var root in ChangeTracker.Entries<Workspace>().Where(w => w.State == EntityState.Unchanged && changed.Contains(w.Entity.Id)))
        {
            root.State = EntityState.Modified;
        }
    }

    /// <summary>
    /// Assigns the GUIDv7 id, audit timestamps, and rotates the concurrency token in app code —
    /// never relying on the database provider for any of them — so behavior is identical across
    /// SQLite and Postgres.
    /// </summary>
    private void ApplyEntityConventions()
    {
        TouchChangedAggregateRoots();
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
