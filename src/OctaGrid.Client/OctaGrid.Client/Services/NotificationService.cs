using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Controls.Primitives;
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
    private TopLevel? _topLevel;
    private Action? _detach;

    public NotificationService(ILogger<NotificationService>? logger = null)
    {
        _logger = logger ?? NullLogger<NotificationService>.Instance;
    }

    public void Attach(TopLevel topLevel)
    {
        if (ReferenceEquals(_topLevel, topLevel))
        {
            return;
        }

        // A new window replaces the old one, so toasts follow the window the user is looking at.
        _detach?.Invoke();
        _detach = null;
        _topLevel = topLevel;

        // The default manager lives in the adorner layer, which is drawn below the overlay layer that hosts
        // dialogs, so a toast raised from a dialog would be hidden behind it. Host it in the overlay layer instead.
        var overlayLayer = OverlayLayer.GetOverlayLayer(topLevel);
        if (overlayLayer is null)
        {
            _manager = new WindowNotificationManager(topLevel) { Position = NotificationPosition.TopRight };
            _logger.LogDebug("WindowNotificationManager attached to TopLevel");
            return;
        }

        // No background and no hit testing of its own: only the toasts take input, the page below keeps working.
        var manager = new WindowNotificationManager { Position = NotificationPosition.TopRight, ZIndex = int.MaxValue, Background = null };

        // The overlay layer is a Canvas: it does not stretch its children, so follow the window size by hand.
        void Resize()
        {
            manager.Width = topLevel.Bounds.Width;
            manager.Height = topLevel.Bounds.Height;
        }

        void OnPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property == Visual.BoundsProperty)
            {
                Resize();
            }
        }

        topLevel.PropertyChanged += OnPropertyChanged;
        Resize();

        overlayLayer.Children.Add(manager);
        _manager = manager;
        _detach = () =>
        {
            topLevel.PropertyChanged -= OnPropertyChanged;
            overlayLayer.Children.Remove(manager);
        };
        _logger.LogDebug("WindowNotificationManager attached to the overlay layer");
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
