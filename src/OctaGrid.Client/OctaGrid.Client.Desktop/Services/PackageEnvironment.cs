using System.Runtime.InteropServices;
using System.Text;

namespace OctaGrid.Client.Desktop.Services;

public static class PackageEnvironment
{
    private const int AppModelErrorNoPackage = 15700;
    private const int ErrorInsufficientBuffer = 122;

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1838:Avoid 'StringBuilder' parameters for P/Invokes")]
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int GetCurrentPackageFullName(ref int packageFullNameLength, StringBuilder? packageFullName);

    public static bool IsRunningAsPackaged()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        var length = 0;
        var result = GetCurrentPackageFullName(ref length, null);
        return result != AppModelErrorNoPackage;
    }

    public static string? GetCurrentPackageFullName()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        var length = 0;
        var result = GetCurrentPackageFullName(ref length, null);
        if (result != ErrorInsufficientBuffer || length == 0)
        {
            return null;
        }

        var sb = new StringBuilder(length);
        result = GetCurrentPackageFullName(ref length, sb);
        return result == 0 ? sb.ToString() : null;
    }

    public static Version? GetCurrentPackageVersion()
    {
        var fullName = GetCurrentPackageFullName();
        if (string.IsNullOrEmpty(fullName))
        {
            return null;
        }

        var parts = fullName.Split('_');
        if (parts.Length > 1 && Version.TryParse(parts[1], out var version))
        {
            return version;
        }

        return null;
    }
}
