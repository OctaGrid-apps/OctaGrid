using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OctaGrid.Client.Services;

namespace OctaGrid.Client.ViewModels;

public partial class SafetyDisclaimerViewModel : ViewModelBase, IDialogParticipant<bool>
{
    private readonly ILogger<SafetyDisclaimerViewModel> _logger;

    public SafetyDisclaimerViewModel() : this(
        App.Services.GetService<ILogger<SafetyDisclaimerViewModel>>() ?? NullLogger<SafetyDisclaimerViewModel>.Instance)
    {
    }

    public SafetyDisclaimerViewModel(ILogger<SafetyDisclaimerViewModel> logger)
    {
        _logger = logger;
        _logger.LogDebug("SafetyDisclaimerViewModel initialized");
    }

    [RelayCommand]
    private void Accept()
    {
        _logger.LogInformation("Safety disclaimer acknowledged by user");
        this.ReturnResultFromOverlayDialog(true);
    }
}
