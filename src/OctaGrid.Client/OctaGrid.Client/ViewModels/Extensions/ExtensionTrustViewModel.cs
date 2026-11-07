using CommunityToolkit.Mvvm.Input;
using OctaGrid.Client.Services;
using OctaGrid.Protocol.Extensions;

namespace OctaGrid.Client.ViewModels;

/// <summary>
/// Asks the user whether to run an extension they installed. It names the publisher and what the extension says it
/// needs; nothing from the extension has been loaded at this point.
/// </summary>
public partial class ExtensionTrustViewModel(ExtensionInfo info) : ViewModelBase, IDialogParticipant<bool>
{
    public string Name => info.Name;

    public string Publisher => string.IsNullOrWhiteSpace(info.Publisher) ? "Unknown publisher" : info.Publisher;

    public string Details => $"{info.Id} · v{info.Version}";

    public string PermissionsText => info.Permissions is { Count: > 0 }
        ? "It says it needs: " + string.Join(", ", info.Permissions) + "."
        : "It does not say what it needs.";

    [RelayCommand]
    private void Allow() => this.ReturnResultFromOverlayDialog(true);

    [RelayCommand]
    private void Cancel() => this.ReturnResultFromOverlayDialog(false);
}
