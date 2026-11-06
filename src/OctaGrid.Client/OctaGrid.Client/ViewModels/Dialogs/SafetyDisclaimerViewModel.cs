using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OctaGrid.Client.Services;

namespace OctaGrid.Client.ViewModels;

public partial class SafetyDisclaimerViewModel : ViewModelBase, IDialogParticipant<bool>
{
    private readonly ILogger<SafetyDisclaimerViewModel> _logger;

    public SafetyDisclaimerViewModel(ILogger<SafetyDisclaimerViewModel>? logger = null)
    {
        _logger = logger ?? NullLogger<SafetyDisclaimerViewModel>.Instance;
        _logger.LogDebug("SafetyDisclaimerViewModel initialized");
    }

    [RelayCommand]
    private void Accept()
    {
        _logger.LogInformation("Safety disclaimer acknowledged by user");
        this.ReturnResultFromOverlayDialog(true);
    }
}
