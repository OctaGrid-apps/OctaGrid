namespace OctaGrid.Client.Services;

/// <summary>For hosts and tests without a backdrop effect: the window is always solid.</summary>
public sealed class NullBackdropCapabilities : IBackdropCapabilities
{
    public bool IsTransparencySupported => false;
}
