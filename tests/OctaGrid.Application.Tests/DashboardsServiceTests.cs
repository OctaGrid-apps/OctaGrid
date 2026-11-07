using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OctaGrid.Application.Services;
using OctaGrid.Data.Abstractions;
using OctaGrid.Data.Abstractions.Models;
using OctaGrid.Protocol.Exceptions;
using OctaGrid.Protocol.Workspaces;

namespace OctaGrid.Application.Tests;

/// <summary>Runs the dashboards service against a real (in-memory) SQLite database.</summary>
public sealed class DashboardsServiceTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _workspaceId = Guid.NewGuid();

    public DashboardsServiceTests()
    {
        _connection.Open();
        using var context = NewContext();
        context.Database.EnsureCreated();

        context.Users.Add(new User { Id = _userId, Name = "Test", Email = "test@example.com", PasswordHash = "x" });
        context.Workspaces.Add(new Workspace { Id = _workspaceId, Name = "Test workspace" });
        context.WorkspaceMembers.Add(new WorkspaceMember
        {
            Id = Guid.NewGuid(),
            WorkspaceId = _workspaceId,
            UserId = _userId,
            Role = Role.Owner,
            JoinedAt = DateTimeOffset.UtcNow,
        });
        context.SaveChanges();
    }

    public void Dispose() => _connection.Dispose();

    private OctaGridContext NewContext() =>
        new(new DbContextOptionsBuilder<OctaGridContext>().UseSqlite(_connection).Options);

    private async Task<DashboardResponse> Create(string name, DashboardType type = DashboardType.List)
    {
        await using var context = NewContext();
        return await new DashboardsService(context).Create(_userId, _workspaceId, new CreateDashboardRequest(name, type), CancellationToken.None);
    }

    private async Task<List<DashboardResponse>> List()
    {
        await using var context = NewContext();
        return await new DashboardsService(context).GetList(_userId, _workspaceId, CancellationToken.None);
    }

    private async Task Delete(Guid id)
    {
        await using var context = NewContext();
        await new DashboardsService(context).Delete(_userId, id, CancellationToken.None);
    }

    [Fact]
    public async Task The_first_dashboard_of_a_workspace_is_the_default_and_the_next_ones_are_not()
    {
        var first = await Create("First");
        var second = await Create("Second");

        Assert.True(first.IsDefault);
        Assert.False(second.IsDefault);
    }

    [Fact]
    public async Task The_type_is_kept_as_the_enum_and_an_unknown_one_is_refused()
    {
        var created = await Create("Main", DashboardType.Layout);
        Assert.Equal(DashboardType.Layout, created.Type);

        await Assert.ThrowsAsync<ValidationException>(() => Create("Broken", (DashboardType)999));
    }

    [Fact]
    public async Task The_list_is_sorted_by_name_regardless_of_case()
    {
        await Create("workshop");
        await Create("Attic");
        await Create("basement");

        Assert.Equal(["Attic", "basement", "workshop"], (await List()).Select(d => d.Name));
    }

    [Fact]
    public async Task Making_a_dashboard_the_default_moves_the_flag_and_leaves_exactly_one()
    {
        var first = await Create("First");
        var second = await Create("Second");

        await using (var context = NewContext())
        {
            var response = await new DashboardsService(context).SetDefault(_userId, second.Id, CancellationToken.None);
            Assert.True(response.IsDefault);
        }

        var list = await List();
        Assert.Single(list, d => d.IsDefault);
        Assert.True(list.Single(d => d.Id == second.Id).IsDefault);
        Assert.False(list.Single(d => d.Id == first.Id).IsDefault);
    }

    [Fact]
    public async Task The_last_dashboard_cannot_be_deleted()
    {
        var only = await Create("Only");

        await Assert.ThrowsAsync<ValidationException>(() => Delete(only.Id));

        Assert.Single(await List());
    }

    [Fact]
    public async Task Deleting_the_default_dashboard_makes_the_first_remaining_one_by_name_the_default()
    {
        var first = await Create("Main");
        await Create("Zeta");
        await Create("Alpha");

        await Delete(first.Id);

        var list = await List();
        Assert.Equal(["Alpha", "Zeta"], list.Select(d => d.Name));
        Assert.Equal("Alpha", Assert.Single(list, d => d.IsDefault).Name);
    }

    [Fact]
    public async Task Deleting_another_dashboard_keeps_the_default()
    {
        var main = await Create("Main");
        var other = await Create("Other");

        await Delete(other.Id);

        var left = Assert.Single(await List());
        Assert.Equal(main.Id, left.Id);
        Assert.True(left.IsDefault);
    }

    [Fact]
    public async Task Deleting_a_dashboard_removes_its_widgets_but_not_the_printer()
    {
        await Create("Main");
        var other = await Create("Other");
        var printerId = Guid.NewGuid();

        await using (var context = NewContext())
        {
            context.Printers.Add(new Printer { Id = printerId, Name = "Voron", WorkspaceId = _workspaceId, ConnectorId = "x", Enabled = true });
            context.DashboardWidgets.Add(new DashboardWidget
            {
                Id = Guid.NewGuid(),
                DashboardId = other.Id,
                PrinterId = printerId,
                WidgetType = WidgetType.PrinterCard,
                ConfigJson = "{}",
            });
            await context.SaveChangesAsync();
        }

        await Delete(other.Id);

        await using var check = NewContext();
        Assert.Empty(await check.DashboardWidgets.ToListAsync());
        Assert.Single(await check.Printers.ToListAsync());
    }

    [Fact]
    public async Task Someone_outside_the_workspace_cannot_delete_or_change_a_dashboard()
    {
        await Create("Main");
        var other = await Create("Other");
        var stranger = Guid.NewGuid();

        await using var context = NewContext();
        var service = new DashboardsService(context);

        await Assert.ThrowsAsync<AuthenticationException>(() => service.Delete(stranger, other.Id, CancellationToken.None));
        await Assert.ThrowsAsync<AuthenticationException>(() => service.SetDefault(stranger, other.Id, CancellationToken.None));
        await Assert.ThrowsAsync<NotFoundException>(() => service.Delete(_userId, Guid.NewGuid(), CancellationToken.None));
    }
}
