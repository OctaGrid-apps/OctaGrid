using System.ComponentModel.DataAnnotations;
using CommunityToolkit.Mvvm.ComponentModel;
using OctaGrid.Client.Models;

namespace OctaGrid.Client.ViewModels;

public partial class WorkspaceViewModel : ViewModelValidatorBase
{
    public WorkspaceViewModel(Workspace workspace)
    {
        Id = workspace.Id;
        Name = workspace.Name;
    }

    [ObservableProperty]
    public partial Guid? Id { get; private set; }

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [Required, Length(2, 100)]
    public partial string? Name { get; set; }

    public Workspace ToWorkspace() => new()
    {
        Id = Id ?? Guid.Empty,
        Name = Name ?? ""
    };
}
