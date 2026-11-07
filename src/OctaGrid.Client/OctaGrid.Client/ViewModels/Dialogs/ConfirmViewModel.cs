using CommunityToolkit.Mvvm.Input;
using OctaGrid.Client.Services;

namespace OctaGrid.Client.ViewModels;

/// <summary>A question with a confirm button and a cancel button. The result is true when the user confirms.</summary>
public partial class ConfirmViewModel(string message, string confirmText) : ViewModelBase, IDialogParticipant<bool>
{
    public string Message => message;

    public string ConfirmText => confirmText;

    [RelayCommand]
    private void Confirm() => this.ReturnResultFromOverlayDialog(true);

    [RelayCommand]
    private void Cancel() => this.ReturnResultFromOverlayDialog(false);
}
