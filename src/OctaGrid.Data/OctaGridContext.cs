using Microsoft.EntityFrameworkCore;
using OctaGrid.Data.Models;
using OctaGrid.Extensions.Models.Exceptions;

namespace OctaGrid.Data;

public class OctaGridContext : DbContext
{
    private readonly IOutboxSignal? _outboxSignal;

    /// <param name="options">The context options.</param>
    /// <param name="outboxSignal">Told after a save that wrote domain events to the outbox. Optional.</param>
    public OctaGridContext(DbContextOptions<OctaGridContext> options, IOutboxSignal? outboxSignal = null) : base(options)
    {
        _outboxSignal = outboxSignal;
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

    /// <exception cref="ConflictException">A row changed since it was read (its <see cref="Entity.Version"/> moved on).</exception>
    public override int SaveChanges()
    {
        ApplyEntityConventions();
        var hasEvents = StageDomainEvents();
        int saved;
        try
        {
            saved = base.SaveChanges();
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConflictException(ex);
        }

        SignalOutbox(hasEvents);
        return saved;
    }

    /// <exception cref="ConflictException">A row changed since it was read (its <see cref="Entity.Version"/> moved on).</exception>
    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ApplyEntityConventions();
        var hasEvents = StageDomainEvents();
        int saved;
        try
        {
            saved = await base.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            // Reported as an AppException so callers show it like any other expected failure.
            throw new ConflictException(ex);
        }

        SignalOutbox(hasEvents);
        return saved;
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(OctaGridContext).Assembly);
    }

    /// <summary>
    /// Moves the events tracked entities raised into the outbox, so they are saved in the same transaction as the
    /// change. They leave the entities here: if the save fails, the outbox rows stay tracked and go with the retry.
    /// Each event gets the entity's version as saved and, unless it has one, the save time.
    /// Runs after <see cref="ApplyEntityConventions"/>, which rotates the versions.
    /// </summary>
    /// <returns>True when the save writes outbox rows, including rows left from a failed save.</returns>
    private bool StageDomainEvents()
    {
        var utcNow = DateTimeOffset.UtcNow;
        var raisers = ChangeTracker.Entries<Entity>()
            .Select(e => e.Entity)
            .Where(e => e.DomainEvents.Count > 0)
            .ToList();

        foreach (var entity in raisers)
        {
            var events = entity.DomainEvents.Select(e => e with
            {
                OccurredAt = e.OccurredAt == default ? utcNow : e.OccurredAt,
                AggregateVersion = entity.Version,
            });
            OutboxMessages.AddRange(events.Select(OutboxMessage.From));
            entity.ClearDomainEvents();
        }

        return ChangeTracker.Entries<OutboxMessage>().Any(e => e.State == EntityState.Added);
    }

    private void SignalOutbox(bool hasEvents)
    {
        if (hasEvents)
        {
            _outboxSignal?.Signal();
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
