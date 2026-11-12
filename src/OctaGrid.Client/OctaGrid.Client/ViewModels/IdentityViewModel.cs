using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OctaGrid.Client.Contracts;
using OctaGrid.Extensions.Models.Exceptions;
using OctaGrid.Protocol.Users;

namespace OctaGrid.Client.ViewModels;

public partial class IdentityViewModel : ViewModelBase
{
    private readonly IAuthClient _client;
    private readonly ILogger<IdentityViewModel> _logger;

    public IdentityViewModel(IAuthClient client, ILogger<IdentityViewModel>? logger = null)
    {
        _client = client;
        _logger = logger ?? NullLogger<IdentityViewModel>.Instance;
    }

    public event Action? SignedIn;

    public event Action? SignedUp;

    public event Action? SwitchToSignInRequested;

    public event Action? SwitchToSignUpRequested;

    public bool CanSubmit => !IsBusy;

    [ObservableProperty]
    public partial string Email { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? ErrorMessage { get; set; }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSubmit))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string Name { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Password { get; set; } = string.Empty;

    [RelayCommand(CanExecute = nameof(CanSubmit))]
    private async Task SignIn(CancellationToken cancellationToken)
    {
        ErrorMessage = null;
        IsBusy = true;
        try
        {
            _logger.LogInformation("Attempting sign-in for '{Email}'", Email);
            await _client.SignIn(new SignInRequest(Email, Password), cancellationToken);
            _logger.LogInformation("Sign-in succeeded for '{Email}'", Email);
            SignedIn?.Invoke();
        }
        catch (AuthenticationException ex)
        {
            _logger.LogWarning("Sign-in failed for '{Email}': {Message}", Email, ex.Message);
            ErrorMessage = ex.Message;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Unexpected error during sign-in for '{Email}'", Email);
            ErrorMessage = "Something went wrong. Please try again.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanSubmit))]
    private async Task SignUp(CancellationToken cancellationToken)
    {
        ErrorMessage = null;
        IsBusy = true;
        try
        {
            _logger.LogInformation("Attempting sign-up for '{Email}'", Email);
            await _client.CreateUser(new CreateUserRequest(Name, Email, Password), cancellationToken);
            _logger.LogInformation("Sign-up succeeded for '{Email}'", Email);
            SignedUp?.Invoke();
        }
        catch (ValidationException ex)
        {
            _logger.LogWarning("Sign-up validation failed for '{Email}': {Message}", Email, ex.Message);
            ErrorMessage = ex.Message;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Unexpected error during sign-up for '{Email}'", Email);
            ErrorMessage = "Something went wrong. Please try again.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void SwitchToSignIn()
    {
        SwitchToSignInRequested?.Invoke();
    }

    [RelayCommand]
    private void SwitchToSignUp()
    {
        SwitchToSignUpRequested?.Invoke();
    }
}
