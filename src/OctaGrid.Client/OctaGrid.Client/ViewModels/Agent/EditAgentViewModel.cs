using CommunityToolkit.Mvvm.Input;
using OctaGrid.Client.Models;
using OctaGrid.Client.Services;

namespace OctaGrid.Client.ViewModels;

public partial class EditAgentViewModel : ViewModelBase, IDialogParticipant<Agent>
{
    public EditAgentViewModel(AgentViewModel agent)
    {
        Item = agent;
    }

    public AgentViewModel Item { get; }

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
            this.ReturnResultFromOverlayDialog(Item.ToAgent());
        }
    }
}
