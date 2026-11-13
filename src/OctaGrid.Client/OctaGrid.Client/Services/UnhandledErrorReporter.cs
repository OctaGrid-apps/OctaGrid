using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OctaGrid.Extensions.Models.Exceptions;

namespace OctaGrid.Client.Services;

/// <summary>
/// The last line of defence for an exception that escaped the UI thread (a button's command, a click handler). Commands
/// are meant to handle their own failures; one that does not must cost the user one action, not the whole app with no
/// word of what happened. The error is logged and shown as a toast, and the app keeps running.
/// </summary>
public sealed class UnhandledErrorReporter
{
    internal const string Title = "Something went wrong";
    internal const string GenericMessage = "The action could not be completed. The details are in the log.";

    private readonly ILogger<UnhandledErrorReporter> _logger;
    private readonly INotificationService? _notifications;

    public UnhandledErrorReporter(INotificationService? notifications, ILogger<UnhandledErrorReporter>? logger = null)
    {
        _notifications = notifications;
        _logger = logger ?? NullLogger<UnhandledErrorReporter>.Instance;
    }

    /// <summary>Logs and shows the exception. Returns whether it was handled, so the app does not end because of it.</summary>
    public bool Report(Exception exception)
    {
        // A cancelled operation is something the user or the app ended on purpose, not an error.
        if (exception is OperationCanceledException)
        {
            _logger.LogDebug(exception, "An operation on the UI thread was cancelled");
            return true;
        }

        _logger.LogError(exception, "Unhandled exception on the UI thread");

        // An AppException's message is written for the user; anything else is a bug whose message is not.
        var message = exception is AppException ? exception.Message : GenericMessage;
        _notifications?.ShowError(Title, message);
        return true;
    }
}
