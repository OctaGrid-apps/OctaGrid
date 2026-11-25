using Avalonia;
using Avalonia.Threading;

namespace OctaGrid.Client.Tests;

[Collection(UiCollection.Name)]
public class HeadlessUiTests
{
    // Whichever thread uses a dispatcher first becomes Avalonia's UI thread, so a post from a test's own thread must
    // not be able to take it from the headless application.
    [Fact]
    public async Task The_headless_application_keeps_the_ui_thread_when_a_test_posts_to_it_from_another_thread()
    {
        await Task.Run(() => Dispatcher.UIThread.Post(() => { }), TestContext.Current.CancellationToken);

        var ready = await HeadlessUi.Run(() => Dispatcher.UIThread.CheckAccess() && Application.Current is App);

        Assert.True(ready);
    }
}
