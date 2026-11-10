using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;

namespace OctaGrid.Client.Controls;

/// <summary>
/// The picture of a print file in a tile, with a cube in its place while the picture has not arrived (or there is none).
/// The picture is decoded once, here, and shared by the tile and its larger preview, and it is released when the tile
/// leaves the screen: a list of files is made again each time it is shown, and decoded pictures that nobody disposes add up.
/// </summary>
public partial class FileThumbnail : UserControl
{
    public static readonly StyledProperty<byte[]?> DataProperty =
        AvaloniaProperty.Register<FileThumbnail, byte[]?>(nameof(Data));

    public static readonly DirectProperty<FileThumbnail, bool> HasImageProperty =
        AvaloniaProperty.RegisterDirect<FileThumbnail, bool>(nameof(HasImage), o => o.HasImage);

    public static readonly DirectProperty<FileThumbnail, Bitmap?> PictureProperty =
        AvaloniaProperty.RegisterDirect<FileThumbnail, Bitmap?>(nameof(Picture), o => o.Picture);

    private bool _hasImage;
    private bool _isAttached;
    private Bitmap? _picture;

    public FileThumbnail()
    {
        InitializeComponent();
    }

    /// <summary>The encoded image; null or empty shows the placeholder.</summary>
    public byte[]? Data
    {
        get => GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    /// <summary>True when there is a picture to show, and not the placeholder.</summary>
    public bool HasImage
    {
        get => _hasImage;
        private set => SetAndRaise(HasImageProperty, ref _hasImage, value);
    }

    /// <summary>The decoded picture, shared by everything in the tile that shows it; null when there is none.</summary>
    public Bitmap? Picture
    {
        get => _picture;
        private set
        {
            var old = _picture;
            if (SetAndRaise(PictureProperty, ref _picture, value))
            {
                // After the bindings moved to the new picture, so nothing shows one that is already disposed.
                old?.Dispose();
            }
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _isAttached = true;
        Decode();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _isAttached = false;
        Release();
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        // Nothing is decoded for a tile that is not on screen; it is done when it arrives.
        if (change.Property == DataProperty && _isAttached)
        {
            Decode();
        }
    }

    private void Decode()
    {
        if (Data is not { Length: > 0 } bytes)
        {
            Release();
            return;
        }

        try
        {
            using var stream = new MemoryStream(bytes);
            Picture = new Bitmap(stream);
            HasImage = true;
        }
        catch (Exception)
        {
            // A picture the printer sent that cannot be read is the same to the user as no picture.
            Release();
        }
    }

    private void Release()
    {
        Picture = null;
        HasImage = false;
    }
}
