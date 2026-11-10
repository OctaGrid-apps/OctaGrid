using OctaGrid.Client.Models;
using OctaGrid.Client.Services;
using OctaGrid.Client.ViewModels;

namespace OctaGrid.Client.DesignTime;

/// <summary>
/// Ready-made view models with sample data for the designer (<c>&lt;Design.DataContext&gt;</c>) and for tests.
/// They are built directly from in-memory parts, so the designer needs no service container.
/// </summary>
internal static class DesignData
{
    private static readonly DesignOctaGridClient Client = new();
    // The disclaimer is marked as accepted: otherwise the main view would open the modal disclaimer
    // every time the designer (re)loads it, for example when the preview is zoomed.
    private static readonly InMemorySettingsStore Settings = new(new ClientSettings { IsSafetyDisclaimerAccepted = true });
    private static readonly DesignAgentCommands AgentCommands = new();
    private static readonly DesignExtensionsClient Extensions = new();
    private static readonly NullUpdateService Updates = new();
    private static readonly NotificationService Notifications = new();
    private static readonly ByteArrayImageCache Images = new();

    /// <summary>The application state with the sample workspace selected.</summary>
    public static ApplicationState State { get; } = new(Settings)
    {
        SelectedWorkspace = new Workspace
        {
            Id = DesignOctaGridClient.DefaultWorkspaceId,
            Name = "Default Workspace",
        },
    };

    public static AgentListViewModel Agents => new(Client, State);

    public static DashboardsViewModel Dashboards => new(Client, Client, State, AgentCommands, Images, Settings, Notifications);

    public static MainViewModel Main => new(
        Client, Client, Client, Updates, Settings, State, Notifications,
        Dashboards, Printers, Agents, Workspaces, SettingsPage);

    public static PrinterPageViewModel PrinterPage => new(
        new PrinterDashboardCardViewModel(
            new PrinterStatusWidget
            {
                Id = Guid.NewGuid(),
                Name = "Voron 2.4",
                NozzleTemperature = 215.3,
                NozzleTargetTemperature = 215,
                BedTemperature = 59.8,
                BedTargetTemperature = 60,
                PrintFileName = "benchy.gcode",
                Progress = 0.42,
                CurrentLayer = 84,
                TotalLayerCount = 200,
                State = OctaGrid.Protocol.Messaging.UniversalPrinterState.Printing,
            },
            AgentCommands, Images),
        () => { });

    public static PrinterListViewModel Printers => new(Client, Notifications, State, AgentCommands);

    public static SettingsViewModel SettingsPage => new(Settings, State, Updates, extensionsClient: Extensions, notifications: Notifications);

    public static WorkspaceListViewModel Workspaces => new(Client, State);
}
