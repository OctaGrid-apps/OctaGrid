using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OctaGrid.Protocol.Dashboards;

namespace OctaGrid.Client.ViewModels;

/// <summary>One row in the dashboard management dialog: a dashboard, or the new one that is being named.</summary>
public partial class DashboardItemViewModel : ObservableObject
{
    public const int MinNameLength = 2;
    public const int MaxNameLength = 100;

    private readonly ManageDashboardsViewModel _owner;

    public DashboardItemViewModel(ManageDashboardsViewModel owner, DashboardResponse? dashboard, DashboardType newType = DashboardType.List)
    {
        _owner = owner;
        if (dashboard is null)
        {
            IsNew = true;
            IsEditing = true;
            Type = newType;
            return;
        }

        Id = dashboard.Id;
        Name = dashboard.Name;
        Type = dashboard.Type;
        IsDefault = dashboard.IsDefault;
        EditName = dashboard.Name;
    }

    public Guid Id { get; }

    /// <summary>True for the row of a dashboard that is not created yet.</summary>
    public bool IsNew { get; }

    public string Name { get; } = string.Empty;

    public DashboardType Type { get; }

    public bool IsDefault { get; }

    public string TypeText => Type switch
    {
        DashboardType.List => "List of printers",
        DashboardType.Layout => "Widgets",
        _ => Type.ToString(),
    };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial string EditName { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotEditing))]
    public partial bool IsEditing { get; set; }

    public bool IsNotEditing => !IsEditing;

    public bool CanSave => EditName.Trim().Length is >= MinNameLength and <= MaxNameLength;

    /// <summary>The last dashboard of a workspace cannot be deleted.</summary>
    public bool CanDelete => !IsNew && _owner.CanDeleteAny;

    public string DeleteTip => CanDelete ? "Delete dashboard" : "A workspace needs at least one dashboard";

    public bool CanMakeDefault => !IsNew && !IsDefault;

    /// <summary>Raised by the owner when something that decides what the row allows has changed.</summary>
    public void Refresh()
    {
        OnPropertyChanged(nameof(CanDelete));
        OnPropertyChanged(nameof(DeleteTip));
        DeleteCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void Rename()
    {
        EditName = Name;
        IsEditing = true;
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private Task Save() => _owner.SaveAsync(this);

    [RelayCommand]
    private void CancelEdit()
    {
        if (IsNew)
        {
            _owner.Discard(this);
            return;
        }

        IsEditing = false;
    }

    [RelayCommand(CanExecute = nameof(CanMakeDefault))]
    private Task MakeDefault() => _owner.SetDefaultAsync(this);

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private Task Delete() => _owner.DeleteAsync(this);
}
