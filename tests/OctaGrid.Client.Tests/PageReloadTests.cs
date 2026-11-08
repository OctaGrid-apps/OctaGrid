using Avalonia.Controls;
using Avalonia.Threading;

namespace OctaGrid.Client.Tests;

public class PageReloadTests
{
    [Fact]
    public async Task A_cached_page_raises_Loaded_again_each_time_it_is_shown()
    {
        // The printer and dashboard pages reload their data on Loaded, which is how they notice that a
        // connector extension was turned on or off while the user was on the Settings page.
        var loaded = 0;
        await HeadlessUi.Run(() =>
        {
            var page = new ContentPage();
            page.Loaded += (_, _) => loaded++;
            var host = new ContentControl { Content = page };
            var window = new Window { Content = host };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            host.Content = new TextBlock();
            Dispatcher.UIThread.RunJobs();
            host.Content = page;
            Dispatcher.UIThread.RunJobs();
            window.Close();
        });

        Assert.Equal(2, loaded);
    }
}
