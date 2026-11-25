using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using OctaGrid.Client.Controls;

namespace OctaGrid.Client.Tests;

[Collection(UiCollection.Name)]
public class FileThumbnailTests
{
    // A 1x1 PNG, the smallest picture a printer could send.
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    private static Window Show(Control content)
    {
        var window = new Window { Content = content, Width = 200, Height = 200 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static bool IsDisposed(Bitmap bitmap)
    {
        try
        {
            bitmap.Save(new MemoryStream(), PngBitmapEncoderOptions.Default);
            return false;
        }
        catch (ObjectDisposedException)
        {
            return true;
        }
    }

    [Fact]
    public async Task Data_on_screen_shows_a_picture()
    {
        await HeadlessUi.Run(() =>
        {
            var thumbnail = new FileThumbnail { Data = Png };
            var window = Show(thumbnail);

            Assert.True(thumbnail.HasImage);
            Assert.NotNull(thumbnail.Picture);
            window.Close();
        });
    }

    [Fact]
    public async Task New_data_replaces_the_picture_and_disposes_the_old_one()
    {
        await HeadlessUi.Run(() =>
        {
            var thumbnail = new FileThumbnail { Data = Png };
            var window = Show(thumbnail);
            var first = thumbnail.Picture!;

            thumbnail.Data = [.. Png];

            Assert.NotSame(first, thumbnail.Picture);
            Assert.True(IsDisposed(first), "The replaced picture was not disposed.");
            Assert.False(IsDisposed(thumbnail.Picture!));
            window.Close();
        });
    }

    [Fact]
    public async Task Leaving_the_screen_releases_the_picture()
    {
        await HeadlessUi.Run(() =>
        {
            var thumbnail = new FileThumbnail { Data = Png };
            var window = Show(thumbnail);
            var picture = thumbnail.Picture!;

            window.Content = null;
            Dispatcher.UIThread.RunJobs();

            Assert.Null(thumbnail.Picture);
            Assert.False(thumbnail.HasImage);
            Assert.True(IsDisposed(picture), "The released picture was not disposed.");
            window.Close();
        });
    }
}
