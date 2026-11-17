using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using OctaGrid.Client.Models;
using OctaGrid.Protocol.Permissions;

namespace OctaGrid.Client.Services;

/// <summary>
/// What the user may do in the selected workspace. Only for deciding what to show: the backend refuses what is
/// not allowed whatever the client thinks. View models ask for a permission and never for a role, and views
/// hide what is not allowed instead of showing it disabled.
/// </summary>
public sealed class WorkspacePermissions : ObservableObject
{
    private readonly ApplicationState? _state;

    public WorkspacePermissions(ApplicationState state)
    {
        _state = state;
        _state.PropertyChanged += OnStateChanged;
    }

    private WorkspacePermissions()
    {
    }

    /// <summary>Allows everything. For the designer, tests and places that have no workspace to ask about.</summary>
    public static WorkspacePermissions Unrestricted { get; } = new();

    public bool CanControlPrint => Has(Permission.ControlPrint);

    public bool CanManageAgents => Has(Permission.ManageAgents);

    public bool CanManageDashboards => Has(Permission.ManageDashboards);

    public bool CanManageFiles => Has(Permission.ManageFiles);

    public bool CanManagePrinters => Has(Permission.ManagePrinters);

    public bool CanSendCommands => Has(Permission.SendCommands);

    /// <summary>Raised when another workspace is selected or the selected one is loaded again.</summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Whether the user may do this in the selected workspace. True while the backend has not said what is
    /// allowed (no workspace yet, or a backend that does not report permissions): nothing is hidden on a guess.
    /// </summary>
    public bool Has(Permission permission) => Allows(_state?.SelectedWorkspace?.Permissions, permission);

    /// <summary>The same rule for any list of granted permissions: a list that is not known withholds nothing.</summary>
    public static bool Allows(IReadOnlyList<Permission>? granted, Permission permission) =>
        granted is null || granted.Contains(permission);

    private void OnStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ApplicationState.SelectedWorkspace))
        {
            // Every Can* property may have changed.
            OnPropertyChanged(string.Empty);
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
