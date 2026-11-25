using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using OctaGrid.Client.Controls.Dialogs;

namespace OctaGrid.Client.Tests;

[Collection(UiCollection.Name)]
public class OverlayDialogTests
{
    // The overlay brush is not a compile-time constant, so it cannot be the generated property's default. Overriding
    // the default on the owner type throws in its type initializer, which broke every dialog; the theme sets it instead.
    [Fact]
    public async Task A_dialog_dims_the_window_behind_it_with_the_theme_scrim()
    {
        await HeadlessUi.Run(() =>
        {
            var dialog = new OverlayDialog { Content = "Body" };
            var window = new Window { Content = dialog, Width = 400, Height = 300 };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var scrim = Assert.IsAssignableFrom<ISolidColorBrush>(dialog.OverlayBrush);
            Assert.Equal(Color.Parse("#80000000"), scrim.Color);
            window.Close();
        });
    }
}
