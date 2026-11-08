using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OctaGrid.Client.DesignTime;
using OctaGrid.Client.Dialogs;
using OctaGrid.Client.Services;
using OctaGrid.Client.ViewModels;
using OctaGrid.Protocol.Extensions;

namespace OctaGrid.Client.Tests;

[Collection(UiCollection.Name)]
public class ExtensionsUiTests
{
    private static async Task<ExtensionsViewModel> LoadedViewModel()
    {
        var viewModel = new ExtensionsViewModel(new DesignExtensionsClient());
        await viewModel.LoadCommand.ExecuteAsync(null);
        return viewModel;
    }

    [Fact]
    public async Task An_installed_extension_that_waits_for_approval_can_be_reviewed_and_removed_but_not_toggled()
    {
        var viewModel = await LoadedViewModel();

        var pending = viewModel.Items.Single(i => i.Id == "acme.prusalink");

        Assert.Equal("Needs approval", pending.StateText);
        Assert.True(pending.NeedsApproval);
        Assert.True(pending.CanUninstall);
        Assert.False(pending.CanToggle);
        Assert.False(pending.IsEnabled);
    }

    [Fact]
    public async Task Built_in_extensions_can_be_turned_off_but_not_removed()
    {
        var viewModel = await LoadedViewModel();

        var bundled = viewModel.Items.Single(i => i.Id == "octagrid.moonraker");

        Assert.True(bundled.CanToggle);
        Assert.False(bundled.CanUninstall);
        Assert.False(bundled.NeedsApproval);
    }

    [Fact]
    public void The_trust_prompt_names_the_publisher_and_what_the_extension_says_it_needs()
    {
        var info = new ExtensionInfo("acme.prusalink", "PrusaLink", "1.2.0", "Acme", "connector", ExtensionSource.UserFolder,
            ExtensionState.NeedsApproval, null, ConnectorCapabilities.None, CanUninstall: true, Permissions: ["network", "files"]);

        var viewModel = new ExtensionTrustViewModel(info);

        Assert.Equal("Acme", viewModel.Publisher);
        Assert.Equal("acme.prusalink · v1.2.0", viewModel.Details);
        Assert.Equal("It says it needs: network, files.", viewModel.PermissionsText);
    }

    [Fact]
    public void The_trust_prompt_says_so_when_an_extension_declares_nothing()
    {
        var info = new ExtensionInfo("acme.x", "X", "1.0.0", "", "connector", ExtensionSource.UserFolder,
            ExtensionState.NeedsApproval, null, ConnectorCapabilities.None, CanUninstall: true);

        var viewModel = new ExtensionTrustViewModel(info);

        Assert.Equal("Unknown publisher", viewModel.Publisher);
        Assert.Equal("It does not say what it needs.", viewModel.PermissionsText);
    }

    [Fact]
    public async Task Choosing_a_developer_folder_turns_developer_mode_on_and_remembers_it()
    {
        var settings = new InMemorySettingsStore();
        var viewModel = new ExtensionsViewModel(new DesignExtensionsClient(), settings: settings);
        await viewModel.LoadCommand.ExecuteAsync(null);
        Assert.False(viewModel.IsDeveloperMode);

        await viewModel.SetDeveloperFolderAsync(@"C:\work\MyConnector\bin");

        Assert.True(viewModel.IsDeveloperMode);
        Assert.Contains(@"C:\work\MyConnector\bin", viewModel.DeveloperStatus);
        Assert.Equal(@"C:\work\MyConnector\bin", settings.ClientSettings.DeveloperExtensionsFolder);
    }

    [Fact]
    public async Task Turning_developer_mode_off_clears_the_saved_folder()
    {
        var settings = new InMemorySettingsStore();
        var viewModel = new ExtensionsViewModel(new DesignExtensionsClient(), settings: settings);
        await viewModel.SetDeveloperFolderAsync(@"C:\work\MyConnector\bin");

        await viewModel.TurnOffDeveloperModeCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsDeveloperMode);
        Assert.Null(settings.ClientSettings.DeveloperExtensionsFolder);
    }

    [Fact]
    public async Task Extensions_from_the_developer_folder_say_where_they_come_from()
    {
        var client = new DesignExtensionsClient();
        await client.ApproveExtensionAsync("acme.prusalink", CancellationToken.None);
        var info = (await client.GetExtensionsAsync(CancellationToken.None)).Single(e => e.Id == "acme.prusalink") with
        {
            Source = ExtensionSource.Development,
            CanUninstall = false,
        };

        var item = new ExtensionItemViewModel(info, (_, _) => { }, _ => { }, _ => { });

        Assert.Contains("Developer folder", item.Details);
        Assert.False(item.CanUninstall);
    }

    [Theory]
    [InlineData("https://github.com/acme/prusalink", true)]
    [InlineData(null, false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("not a url", false)]
    public async Task The_project_link_is_shown_only_for_a_web_address(string? url, bool shown)
    {
        var client = new DesignExtensionsClient();
        var info = (await client.GetExtensionsAsync(CancellationToken.None)).First() with { ProjectUrl = url };

        var item = new ExtensionItemViewModel(info, (_, _) => { }, _ => { }, _ => { });

        Assert.Equal(shown, item.HasProjectUri);
        Assert.Equal(shown, item.ProjectUri is not null);
    }

    [Theory]
    [InlineData(new[] { "--extensions-dev", @"C:\dev\ext" }, null, null, @"C:\dev\ext")]
    [InlineData(new[] { @"--extensions-dev=C:\dev\ext" }, null, null, @"C:\dev\ext")]
    [InlineData(new[] { "--EXTENSIONS-DEV", @"C:\dev\ext" }, null, null, @"C:\dev\ext")]
    [InlineData(new[] { "--other" }, null, @"C:\env", @"C:\env")]
    [InlineData(new[] { "--extensions-dev", @"C:\cli" }, @"C:\saved", @"C:\env", @"C:\cli")]
    [InlineData(new string[0], @"C:\saved", @"C:\env", @"C:\env")]
    [InlineData(new string[0], @"C:\saved", null, @"C:\saved")]
    [InlineData(new string[0], "  ", null, null)]
    [InlineData(new[] { "--extensions-dev" }, null, null, null)]
    public void The_developer_folder_comes_from_the_command_line_then_the_environment_then_the_settings(
        string[] args, string? saved, string? environment, string? expected) =>
        Assert.Equal(expected, DeveloperMode.ResolveFolder(args, saved, _ => environment));

    [Fact]
    public void Without_any_arguments_there_is_no_developer_folder() =>
        Assert.Null(DeveloperMode.ResolveFolder(null, null, _ => null));

    [Fact]
    public async Task The_extension_dialogs_render_with_their_actions()
    {
        var viewModel = await LoadedViewModel();

        await HeadlessUi.Run(() =>
        {
            var window = new Window { Content = new ExtensionsView { DataContext = viewModel }, Width = 700, Height = 700 };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var buttons = window.GetVisualDescendants().OfType<Button>().Select(b => b.Content?.ToString()).ToList();
            Assert.Contains("Install from file…", buttons);
            Assert.Contains("Install from folder…", buttons);
            Assert.Contains("Review…", buttons);
            Assert.Contains("Remove", buttons);
            Assert.Contains("Choose folder…", buttons);
            window.Close();

            var trust = new Window
            {
                Content = new ExtensionTrustView { DataContext = new ExtensionTrustViewModel(viewModel.Items.Single(i => i.NeedsApproval).Info) },
            };
            trust.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.Contains("Trust and turn on", trust.GetVisualDescendants().OfType<Button>().Select(b => b.Content?.ToString()));
            trust.Close();

            var confirm = new Window { Content = new ConfirmView { DataContext = new ConfirmViewModel("Remove it?", "Remove") } };
            confirm.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.Contains("Remove", confirm.GetVisualDescendants().OfType<Button>().Select(b => b.Content?.ToString()));
            confirm.Close();
        });
    }
}
