using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OctaGrid.Data;
using OctaGrid.Data.Models;
using OctaGrid.Extensions.Models.Exceptions;

namespace OctaGrid.Application.Tests;

/// <summary>
/// The workspace aggregate: what a new workspace starts with, the rules about its dashboard list, and the workspace
/// version that keeps two changes to the list made at the same time from both being saved.
/// </summary>
public sealed class WorkspaceAggregateTests : IDisposable
{
    private static readonly DateTimeOffset At = new(2026, 11, 21, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid OwnerId = Guid.NewGuid();

    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public void Dispose() => _connection.Dispose();

    private static Workspace WithDashboards(params string[] names)
    {
        var workspace = Workspace.Create("Lab", OwnerId, At);
        foreach (var name in names)
        {
            workspace.AddDashboard(name, DashboardKind.List);
        }

        return workspace;
    }

    private static Dashboard Named(Workspace workspace, string name) => workspace.Dashboards.Single(d => d.Name == name);

    [Fact]
    public void A_new_workspace_has_its_creator_as_owner_and_one_default_dashboard()
    {
        var workspace = Workspace.Create("Lab", OwnerId, At);

        var owner = Assert.Single(workspace.Members);
        Assert.Equal(OwnerId, owner.UserId);
        Assert.Equal(Role.Owner, owner.Role);
        Assert.Equal(At, owner.JoinedAt);
        var dashboard = Assert.Single(workspace.Dashboards);
        Assert.Equal(Workspace.DefaultDashboardName, dashboard.Name);
        Assert.True(dashboard.IsDefault);
    }

    [Fact]
    public void A_user_cannot_be_added_twice()
    {
        var workspace = Workspace.Create("Lab", OwnerId, At);

        Assert.Throws<ValidationException>(() => workspace.AddMember(OwnerId, Role.Reader, At));
    }

    [Fact]
    public void Dashboards_added_later_are_not_the_default()
    {
        var workspace = WithDashboards("Second");

        Assert.False(Named(workspace, "Second").IsDefault);
        Assert.True(Named(workspace, Workspace.DefaultDashboardName).IsDefault);
    }

    [Fact]
    public void An_unknown_dashboard_type_is_refused()
    {
        var workspace = Workspace.Create("Lab", OwnerId, At);

        Assert.Throws<ValidationException>(() => workspace.AddDashboard("Odd", (DashboardKind)99));
    }

    [Fact]
    public void Choosing_the_default_leaves_exactly_one()
    {
        var workspace = WithDashboards("Second", "Third");

        workspace.SetDefaultDashboard(Named(workspace, "Third").Id);

        Assert.Equal("Third", Assert.Single(workspace.Dashboards, d => d.IsDefault).Name);
    }

    [Fact]
    public void A_dashboard_of_another_workspace_cannot_be_chosen_or_removed()
    {
        var workspace = WithDashboards("Second");

        Assert.Throws<NotFoundException>(() => workspace.SetDefaultDashboard(Guid.NewGuid()));
        Assert.Throws<NotFoundException>(() => workspace.RemoveDashboard(Guid.NewGuid()));
    }

    [Fact]
    public void The_last_dashboard_cannot_be_removed()
    {
        var workspace = WithDashboards();

        Assert.Throws<ValidationException>(() => workspace.RemoveDashboard(workspace.Dashboards.Single().Id));
        Assert.Single(workspace.Dashboards);
    }

    [Fact]
    public void Removing_the_default_makes_the_first_remaining_one_by_name_the_default()
    {
        var workspace = WithDashboards("zeta", "Alpha");

        workspace.RemoveDashboard(Named(workspace, Workspace.DefaultDashboardName).Id);

        Assert.Equal("Alpha", Assert.Single(workspace.Dashboards, d => d.IsDefault).Name);
    }

    [Fact]
    public void A_workspace_without_dashboards_gets_its_default_one_and_only_once()
    {
        var workspace = WithDashboards("Second");
        workspace.RemoveDashboard(Named(workspace, "Second").Id);

        Assert.False(workspace.EnsureDashboard());
        Assert.Single(workspace.Dashboards);
    }

    [Fact]
    public async Task Two_changes_to_the_dashboard_list_made_at_the_same_time_cannot_both_be_saved()
    {
        var workspaceId = await SeedAsync("First", "Second");
        await using var first = NewContext();
        await using var second = NewContext();
        var seenByFirst = await LoadAsync(first, workspaceId);
        var seenBySecond = await LoadAsync(second, workspaceId);

        // Each one alone would leave a dashboard; both together would leave none.
        seenByFirst.RemoveDashboard(Named(seenByFirst, "First").Id);
        seenBySecond.RemoveDashboard(Named(seenBySecond, "Second").Id);
        await first.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Reported as an AppException, so the UI can show it and a server can answer 409.
        var conflict = await Assert.ThrowsAsync<ConflictException>(() => second.SaveChangesAsync(TestContext.Current.CancellationToken));
        Assert.Equal("/errors/conflict", conflict.TypeUri);
        Assert.IsType<DbUpdateConcurrencyException>(conflict.InnerException);
        await using var check = NewContext();
        Assert.NotEmpty(await check.Dashboards.Where(d => d.WorkspaceId == workspaceId).ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Renaming_a_dashboard_without_its_workspace_leaves_the_workspace_version_alone()
    {
        var workspaceId = await SeedAsync("First");
        long before;
        await using (var context = NewContext())
        {
            before = (await context.Workspaces.AsNoTracking().SingleAsync(w => w.Id == workspaceId, TestContext.Current.CancellationToken)).Version;
            (await context.Dashboards.FirstAsync(d => d.Name == "First", TestContext.Current.CancellationToken)).Rename("Renamed");
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var check = NewContext();
        Assert.Equal(before, (await check.Workspaces.SingleAsync(w => w.Id == workspaceId, TestContext.Current.CancellationToken)).Version);
    }

    private OctaGridContext NewContext() =>
        new(new DbContextOptionsBuilder<OctaGridContext>().UseSqlite(_connection).Options);

    private async Task<Guid> SeedAsync(params string[] dashboards)
    {
        _connection.Open();
        await using var context = NewContext();
        await context.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        context.Users.Add(new User { Id = OwnerId, Name = "Owner", Email = "owner@example.com", PasswordHash = "x" });
        var workspace = Workspace.Create("Lab", OwnerId, At);
        foreach (var name in dashboards)
        {
            workspace.AddDashboard(name, DashboardKind.List);
        }

        context.Workspaces.Add(workspace);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        return workspace.Id;
    }

    private static Task<Workspace> LoadAsync(OctaGridContext context, Guid workspaceId) =>
        context.Workspaces.Include(w => w.Dashboards).SingleAsync(w => w.Id == workspaceId, TestContext.Current.CancellationToken);
}
