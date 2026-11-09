using OctaGrid.Client.Services;

namespace OctaGrid.Client.Desktop.Services;

/// <summary>Mica needs Windows 11 (build 22000).</summary>
public sealed class WindowsBackdropCapabilities : IBackdropCapabilities
{
    public bool IsTransparencySupported => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000);
}
