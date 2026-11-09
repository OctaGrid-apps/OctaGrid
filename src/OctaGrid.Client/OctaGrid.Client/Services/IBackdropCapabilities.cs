namespace OctaGrid.Client.Services;

/// <summary>What the host platform can do behind the window; implemented by each host project.</summary>
public interface IBackdropCapabilities
{
    /// <summary>Whether the platform offers a transparency effect such as Mica (Windows 11).</summary>
    bool IsTransparencySupported { get; }
}
