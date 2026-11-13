using OctaGrid.Client.Services;
using OctaGrid.Extensions.Models.Exceptions;

namespace OctaGrid.Client.Tests;

/// <summary>What the user is told when an exception escapes the UI thread, instead of the app closing.</summary>
public class UnhandledErrorReporterTests
{
    [Fact]
    public void An_AppException_is_handled_and_shown_with_its_own_message()
    {
        var notifications = new PrinterPageTabsTests.RecordingNotifications();
        var reporter = new UnhandledErrorReporter(notifications);

        var handled = reporter.Report(new NotFoundException("The printer is not connected."));

        Assert.True(handled);
        Assert.Equal([(UnhandledErrorReporter.Title, "The printer is not connected.")], notifications.Errors);
    }

    [Fact]
    public void Any_other_exception_is_handled_without_showing_its_internal_message()
    {
        var notifications = new PrinterPageTabsTests.RecordingNotifications();
        var reporter = new UnhandledErrorReporter(notifications);

        var handled = reporter.Report(new InvalidOperationException("Not connected to the printer."));

        Assert.True(handled);
        Assert.Equal([(UnhandledErrorReporter.Title, UnhandledErrorReporter.GenericMessage)], notifications.Errors);
    }

    [Fact]
    public void A_cancelled_operation_is_handled_without_bothering_the_user()
    {
        var notifications = new PrinterPageTabsTests.RecordingNotifications();
        var reporter = new UnhandledErrorReporter(notifications);

        var handled = reporter.Report(new OperationCanceledException());

        Assert.True(handled);
        Assert.Empty(notifications.Errors);
    }
}
