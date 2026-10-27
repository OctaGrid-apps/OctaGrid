using CommunityToolkit.Mvvm.Input;
using OctaGrid.Client.Models;
using OctaGrid.Client.Services;

namespace OctaGrid.Client.ViewModels;

public partial class EditWorkspaceViewModel : ViewModelBase, IDialogParticipant<Workspace>
{
    public EditWorkspaceViewModel(WorkspaceViewModel workspace)
    {
        Item = workspace;
    }

    public WorkspaceViewModel Item { get; }

    [RelayCommand]
    private void Cancel()
    {
        this.ReturnResultFromOverlayDialog(null);
    }

    [RelayCommand]
    private void Confirm()
    {
        Item.Validate();
        if (!Item.HasErrors)
        {
            this.ReturnResultFromOverlayDialog(Item.ToWorkspace());
        }
    }
}
