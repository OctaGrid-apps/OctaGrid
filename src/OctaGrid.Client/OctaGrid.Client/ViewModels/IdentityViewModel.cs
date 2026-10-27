using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OctaGrid.Client.Contracts;
using OctaGrid.Protocol.Exceptions;
using OctaGrid.Protocol.Users;

namespace OctaGrid.Client.ViewModels;

public partial class IdentityViewModel : ViewModelBase
{
    private readonly IAuthClient _client;

    public IdentityViewModel(IAuthClient client)
    {
        _client = client;
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
            await _client.SignIn(new SignInRequest(Email, Password), cancellationToken);
            SignedIn?.Invoke();
        }
        catch (AuthenticationException ex)
        {
            ErrorMessage = ex.Message;
        }
        catch (Exception)
        {
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
            await _client.CreateUser(new CreateUserRequest(Name, Email, Password), cancellationToken);
            SignedUp?.Invoke();
        }
        catch (ValidationException ex)
        {
            ErrorMessage = ex.Message;
        }
        catch (Exception)
        {
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
