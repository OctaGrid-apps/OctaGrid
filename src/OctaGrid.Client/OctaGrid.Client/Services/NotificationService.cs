using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Threading;

namespace OctaGrid.Client.Services;

public class NotificationService : INotificationService
{
    // WindowNotificationManager needs a live TopLevel, which doesn't exist when the container is built.
    // The first view that has one calls Attach; until then notifications are dropped.
    private WindowNotificationManager? _manager;

    public void Attach(TopLevel topLevel)
    {
        _manager ??= new WindowNotificationManager(topLevel) { Position = NotificationPosition.TopRight };
    }

    public void ShowError(string title, string message) => Show(title, message, NotificationType.Error);

    public void ShowInfo(string title, string message) => Show(title, message, NotificationType.Information);

    public void ShowSuccess(string title, string message) => Show(title, message, NotificationType.Success);

    public void ShowWarning(string title, string message) => Show(title, message, NotificationType.Warning);

    private void Show(string title, string message, NotificationType type) =>
        Dispatcher.UIThread.Post(() => _manager?.Show(new Notification(title, message, type)));
}
