using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace OctaGrid.Client.Services;

public class NotificationService : INotificationService
{
    private readonly ILogger<NotificationService> _logger;

    // WindowNotificationManager needs a live TopLevel, which doesn't exist when the container is built.
    // The first view that has one calls Attach; until then notifications are dropped.
    private WindowNotificationManager? _manager;

    public NotificationService(ILogger<NotificationService>? logger = null)
    {
        _logger = logger ?? NullLogger<NotificationService>.Instance;
    }

    public void Attach(TopLevel topLevel)
    {
        if (_manager is null)
        {
            _manager = new WindowNotificationManager(topLevel) { Position = NotificationPosition.TopRight };
            _logger.LogDebug("WindowNotificationManager attached to TopLevel");
        }
    }

    public void ShowError(string title, string message) => Show(title, message, NotificationType.Error);

    public void ShowInfo(string title, string message) => Show(title, message, NotificationType.Information);

    public void ShowSuccess(string title, string message) => Show(title, message, NotificationType.Success);

    public void ShowWarning(string title, string message) => Show(title, message, NotificationType.Warning);

    private void Show(string title, string message, NotificationType type)
    {
        if (_manager is null)
        {
            _logger.LogWarning("Dropped {NotificationType} notification '{Title}': WindowNotificationManager is not attached yet", type, title);
            return;
        }

        Dispatcher.UIThread.Post(() => _manager?.Show(new Notification(title, message, type)));
    }
}
