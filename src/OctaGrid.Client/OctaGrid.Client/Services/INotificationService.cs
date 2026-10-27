namespace OctaGrid.Client.Services;

public interface INotificationService
{
    /// <summary>Binds the service to the window/root that will host notifications. Idempotent.</summary>
    void Attach(Avalonia.Controls.TopLevel topLevel);

    void ShowError(string title, string message);

    void ShowInfo(string title, string message);

    void ShowSuccess(string title, string message);

    void ShowWarning(string title, string message);
}
