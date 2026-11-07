using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OctaGrid.Client.Contracts;
using OctaGrid.Client.Dialogs;
using OctaGrid.Client.DesignTime;
using OctaGrid.Client.Models;
using OctaGrid.Client.Services;
using OctaGrid.Client.ViewModels;
using OctaGrid.Protocol.Workspaces;

namespace OctaGrid.Client.Tests;

[Collection(UiCollection.Name)]
public class DashboardManagementTests
{
    /// <summary>Keeps the dashboards in memory and follows the same rules as the real service.</summary>
    private sealed class FakeDashboardsClient : IDashboardsClient
    {
        private readonly List<DashboardResponse> _items = [];

        public FakeDashboardsClient(params string[] names)
        {
            foreach (var name in names)
            {
                _items.Add(new DashboardResponse(Guid.NewGuid(), name, DashboardType.List, _items.Count == 0, 1));
            }
        }

        public List<DashboardResponse> Items => _items;

        /// <summary>When set, renaming throws it, as a failing server or database would.</summary>
        public string? FailWith { get; set; }

        public DashboardResponse this[string name] => _items.Single(d => d.Name == name);

        public Task<DashboardResponse> CreateDashboard(Guid workspaceId, CreateDashboardRequest request, CancellationToken cancellationToken)
        {
            var created = new DashboardResponse(Guid.NewGuid(), request.Name, request.Type, _items.Count == 0, 1);
            _items.Add(created);
            return Task.FromResult(created);
        }

        public Task DeleteDashboard(Guid dashboardId, CancellationToken cancellationToken)
        {
            if (_items.Count == 1)
            {
                throw new InvalidOperationException("A workspace needs at least one dashboard.");
            }

            var deleted = _items.Single(d => d.Id == dashboardId);
            _items.Remove(deleted);
            if (deleted.IsDefault)
            {
                var next = _items.OrderBy(d => d.Name).First();
                _items[_items.IndexOf(next)] = next with { IsDefault = true };
            }

            return Task.CompletedTask;
        }

        public Task<List<DashboardResponse>> GetDashboardList(Guid workspaceId, CancellationToken cancellationToken) =>
            Task.FromResult(_items.OrderBy(d => d.Name).ToList());

        public Task<DashboardResponse> SetDefaultDashboard(Guid dashboardId, CancellationToken cancellationToken)
        {
            for (var i = 0; i < _items.Count; i++)
            {
                _items[i] = _items[i] with { IsDefault = _items[i].Id == dashboardId };
            }

            return Task.FromResult(_items.Single(d => d.Id == dashboardId));
        }

        public Task<DashboardResponse> UpdateDashboard(Guid dashboardId, UpdateDashboardRequest request, CancellationToken cancellationToken)
        {
            if (FailWith is not null)
            {
                throw new InvalidOperationException(FailWith);
            }

            var index = _items.FindIndex(d => d.Id == dashboardId);
            _items[index] = _items[index] with { Name = request.Name };
            return Task.FromResult(_items[index]);
        }
    }

    private static ManageDashboardsViewModel Manage(FakeDashboardsClient client) =>
        new(client, DesignOctaGridClient.DefaultWorkspaceId, client.Items.OrderBy(d => d.Name));

    private static DashboardsViewModel Page(FakeDashboardsClient client)
    {
        var state = new ApplicationState(new InMemorySettingsStore())
        {
            SelectedWorkspace = new Workspace { Id = DesignOctaGridClient.DefaultWorkspaceId, Name = "Default" },
        };
        return new DashboardsViewModel(client, new DesignOctaGridClient(), state, new DesignAgentCommands(), new ByteArrayImageCache());
    }

    [Fact]
    public void The_dialog_lists_the_dashboards_and_marks_the_default()
    {
        var viewModel = Manage(new FakeDashboardsClient("Main", "Workshop"));

        Assert.Equal(["Main", "Workshop"], viewModel.Items.Select(i => i.Name));
        Assert.True(viewModel.Items[0].IsDefault);
        Assert.False(viewModel.Items[0].CanMakeDefault);
        Assert.True(viewModel.Items[1].CanMakeDefault);
        Assert.Equal("List of printers", viewModel.Items[0].TypeText);
    }

    [Fact]
    public void The_only_dashboard_cannot_be_deleted_and_says_why()
    {
        var viewModel = Manage(new FakeDashboardsClient("Main"));
        var only = viewModel.Items.Single();

        Assert.False(only.CanDelete);
        Assert.False(only.DeleteCommand.CanExecute(null));
        Assert.Contains("at least one", only.DeleteTip);
    }

    [Fact]
    public void With_several_dashboards_each_one_can_be_deleted()
    {
        var viewModel = Manage(new FakeDashboardsClient("Main", "Workshop"));

        Assert.All(viewModel.Items, item => Assert.True(item.DeleteCommand.CanExecute(null)));
    }

    [Fact]
    public async Task A_new_dashboard_is_named_in_its_own_row_and_created_on_save()
    {
        var client = new FakeDashboardsClient("Main");
        var viewModel = Manage(client);

        viewModel.AddCommand.Execute(null);
        var row = viewModel.Items[0];
        Assert.True(row.IsNew);
        Assert.True(row.IsEditing);
        Assert.False(row.SaveCommand.CanExecute(null), "An empty name cannot be saved.");
        Assert.False(viewModel.AddCommand.CanExecute(null), "Only one new dashboard at a time.");

        row.EditName = " Attic ";
        Assert.True(row.SaveCommand.CanExecute(null));
        await row.SaveCommand.ExecuteAsync(null);

        Assert.Equal(["Attic", "Main"], client.Items.Select(d => d.Name).Order());
        Assert.Equal(DashboardType.List, client["Attic"].Type);
        Assert.False(viewModel.HasNewItem);
        Assert.True(viewModel.AddCommand.CanExecute(null));
        Assert.Equal(["Attic", "Main"], viewModel.Items.Select(i => i.Name));
    }

    [Fact]
    public void Cancelling_a_new_dashboard_drops_its_row()
    {
        var viewModel = Manage(new FakeDashboardsClient("Main"));

        viewModel.AddCommand.Execute(null);
        viewModel.Items[0].CancelEditCommand.Execute(null);

        Assert.Equal(["Main"], viewModel.Items.Select(i => i.Name));
        Assert.True(viewModel.AddCommand.CanExecute(null));
    }

    [Theory]
    [InlineData("")]
    [InlineData("a")]
    [InlineData("   ")]
    public void A_name_that_is_too_short_cannot_be_saved(string name)
    {
        var viewModel = Manage(new FakeDashboardsClient("Main"));
        viewModel.AddCommand.Execute(null);

        viewModel.Items[0].EditName = name;

        Assert.False(viewModel.Items[0].SaveCommand.CanExecute(null));
    }

    [Fact]
    public async Task Renaming_edits_in_the_row_and_cancelling_keeps_the_old_name()
    {
        var client = new FakeDashboardsClient("Main", "Workshop");
        var viewModel = Manage(client);
        var row = viewModel.Items.Single(i => i.Name == "Workshop");

        row.RenameCommand.Execute(null);
        Assert.True(row.IsEditing);
        Assert.Equal("Workshop", row.EditName);

        row.EditName = "Garage";
        row.CancelEditCommand.Execute(null);
        Assert.False(row.IsEditing);
        Assert.Equal("Workshop", client.Items.Single(d => !d.IsDefault).Name);

        row.RenameCommand.Execute(null);
        row.EditName = "Garage";
        await row.SaveCommand.ExecuteAsync(null);

        Assert.Equal(["Garage", "Main"], viewModel.Items.Select(i => i.Name));
        Assert.All(viewModel.Items, i => Assert.False(i.IsEditing));
    }

    [Fact]
    public async Task Making_a_dashboard_the_default_moves_the_badge()
    {
        var client = new FakeDashboardsClient("Main", "Workshop");
        var viewModel = Manage(client);

        await viewModel.Items.Single(i => i.Name == "Workshop").MakeDefaultCommand.ExecuteAsync(null);

        Assert.Equal("Workshop", Assert.Single(viewModel.Items, i => i.IsDefault).Name);
        Assert.Equal("Main", Assert.Single(viewModel.Items, i => i.CanMakeDefault).Name);
    }

    [Fact]
    public async Task A_failed_change_is_reported_and_the_list_shows_what_is_stored()
    {
        var client = new FakeDashboardsClient("Main", "Workshop") { FailWith = "The database is locked." };
        var notifications = new RecordingNotifications();
        var viewModel = new ManageDashboardsViewModel(client, DesignOctaGridClient.DefaultWorkspaceId, client.Items, notifications);
        var row = viewModel.Items.Single(i => i.Name == "Workshop");

        row.RenameCommand.Execute(null);
        row.EditName = "Garage";
        await row.SaveCommand.ExecuteAsync(null);

        var error = Assert.Single(notifications.Errors);
        Assert.Contains("Could not rename the dashboard", error);
        Assert.Contains("The database is locked.", error);
        Assert.Equal(["Main", "Workshop"], viewModel.Items.Select(i => i.Name));
        Assert.True(viewModel.CanEdit);
    }

    [Fact]
    public async Task The_page_opens_the_default_dashboard()
    {
        var client = new FakeDashboardsClient("Main", "Workshop");
        await client.SetDefaultDashboard(client["Workshop"].Id, CancellationToken.None);
        var page = Page(client);

        await page.LoadDataCommand.ExecuteAsync(null);

        Assert.Equal("Workshop", page.SelectedDashboard?.Name);
        Assert.Equal(2, page.Dashboards.Count);
    }

    [Fact]
    public async Task Reloading_keeps_the_dashboard_the_user_has_open_and_shows_its_new_name()
    {
        var client = new FakeDashboardsClient("Main", "Workshop");
        var page = Page(client);
        await page.LoadDataCommand.ExecuteAsync(null);
        page.SelectedDashboard = page.Dashboards.Single(d => d.Name == "Workshop");

        await client.UpdateDashboard(client["Workshop"].Id, new UpdateDashboardRequest("Garage"), CancellationToken.None);
        await page.LoadDataCommand.ExecuteAsync(null);

        Assert.Equal("Garage", page.SelectedDashboard?.Name);
    }

    [Fact]
    public async Task When_the_open_dashboard_is_deleted_the_page_falls_back_to_the_default()
    {
        var client = new FakeDashboardsClient("Main", "Workshop");
        var page = Page(client);
        await page.LoadDataCommand.ExecuteAsync(null);
        page.SelectedDashboard = page.Dashboards.Single(d => d.Name == "Workshop");

        await client.DeleteDashboard(client["Workshop"].Id, CancellationToken.None);
        await page.LoadDataCommand.ExecuteAsync(null);

        Assert.Equal("Main", page.SelectedDashboard?.Name);
    }

    [Fact]
    public async Task A_workspace_without_dashboards_gets_a_list_dashboard()
    {
        var client = new FakeDashboardsClient();
        var page = Page(client);

        await page.LoadDataCommand.ExecuteAsync(null);

        var created = Assert.Single(client.Items);
        Assert.Equal(DashboardType.List, created.Type);
        Assert.Equal(created.Id, page.SelectedDashboard?.Id);
    }

    [Fact]
    public async Task The_dialog_shows_a_row_per_dashboard_with_the_actions_and_nothing_overlapping()
    {
        await HeadlessUi.RunAsync(() =>
        {
            var viewModel = Manage(new FakeDashboardsClient("Main", "Workshop"));
            var window = new Window { Content = new ManageDashboardsView { DataContext = viewModel }, Width = 640, Height = 600 };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            var texts = window.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text).ToList();
            Assert.Contains("Main", texts);
            Assert.Contains("Workshop", texts);
            Assert.Contains("Default", texts);

            // Each row offers rename and delete; make-default only on the dashboard that is not the default.
            var tips = window.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible)
                .Select(b => ToolTip.GetTip(b)?.ToString()).Where(t => t is not null).ToList();
            Assert.Equal(2, tips.Count(t => t == "Rename"));
            Assert.Equal(2, tips.Count(t => t == "Delete dashboard"));
            Assert.Single(tips, t => t == "Make default");

            window.Close();
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task The_page_has_one_button_for_managing_instead_of_edit_and_new()
    {
        await HeadlessUi.RunAsync(() =>
        {
            var window = new Window { Content = new OctaGrid.Client.Views.DashboardsView { DataContext = DesignData.Dashboards }, Width = 900, Height = 600 };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var tips = window.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible)
                .Select(b => ToolTip.GetTip(b)?.ToString()).Where(t => t is not null).ToList();
            Assert.Contains("Manage dashboards", tips);
            Assert.DoesNotContain("Edit current dashboard", tips);
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "New Dashboard");

            window.Close();
            return Task.CompletedTask;
        });
    }

    private sealed class RecordingNotifications : INotificationService
    {
        public List<string> Errors { get; } = [];

        public void Attach(TopLevel topLevel) { }

        public void ShowInfo(string title, string message) { }

        public void ShowSuccess(string title, string message) { }

        public void ShowWarning(string title, string message) { }

        public void ShowError(string title, string message) => Errors.Add($"{title}: {message}");
    }
}
