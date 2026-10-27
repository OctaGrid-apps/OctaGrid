using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using OctaGrid.Client.Models;

namespace OctaGrid.Client;

public static class Extensions
{
    public static void ApplyTheme(this Window window, ClientSettings settings)
    {
        if (settings.TransparencyLevel == TransparencyLevel.None)
        {
            window.TransparencyLevelHint = [WindowTransparencyLevel.None];
        }
        else
        {
            window.TransparencyLevelHint = [WindowTransparencyLevel.Mica, WindowTransparencyLevel.None];
        }

        window.RequestedThemeVariant = settings.ElementTheme switch
        {
            ElementTheme.Dark => ThemeVariant.Dark,
            ElementTheme.Light => ThemeVariant.Light,
            _ => ThemeVariant.Default
        };
    }

    public static void Restart(this IApplicationLifetime appLifetime)
    {
        if (appLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            throw new NotSupportedException();
        }
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            RestartMacOS();
        }
        else
        {
            RestartStandard();
        }

        desktop.Shutdown();
    }

    private static void RestartMacOS()
    {
        var exePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exePath))
        {
            throw new InvalidOperationException("Could not determine process path.");
        }

        // Check if running inside a .app bundle (e.g., .../MyApp.app/Contents/MacOS/MyApp)
        var appIndex = exePath.IndexOf(".app", StringComparison.OrdinalIgnoreCase);
        if (appIndex != -1)
        {
            // Extract the path up to and including the ".app" folder
            var appBundlePath = exePath[..(appIndex + 4)];

            // Use the macOS 'open' command with -n (open a new instance)
            Process.Start(new ProcessStartInfo
            {
                FileName = "/usr/bin/open",
                Arguments = $"-n \"{appBundlePath}\"",
                UseShellExecute = false
            });
        }
        else
        {
            // Fallback for unpackaged dev runs ('dotnet run')
            Process.Start(new ProcessStartInfo(exePath)
            {
                UseShellExecute = false,
                WorkingDirectory = Environment.CurrentDirectory
            });
        }
    }

    private static void RestartStandard()
    {
        var exePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exePath))
        {
            throw new InvalidOperationException("Could not determine process path.");
        }

        Process.Start(new ProcessStartInfo(exePath)
        {
            UseShellExecute = true,
            WorkingDirectory = Environment.CurrentDirectory
        });
    }
}

public static class Helper
{
    private const string DefaultVersion = "1.0.0";

    public static string GetAppVersion()
    {
        var infoVersion = GetInfoVersion();

        var plusIndex = infoVersion.IndexOf('+');
        var cleanVersion = plusIndex > 0 ? infoVersion[..plusIndex] : infoVersion;
        return $"v{cleanVersion}";
    }

    public static string GetInfoVersion()
    {
        var assembly = Assembly.GetEntryAssembly() ?? typeof(Helper).Assembly;
        var infoVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        return string.IsNullOrWhiteSpace(infoVersion)
            ? $"v{assembly.GetName().Version?.ToString(3) ?? DefaultVersion}"
            : infoVersion;
    }

    public static string LocalDataFolder()
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.Create), "OctaGrid");
        Directory.CreateDirectory(directory);
        return directory;
    }
}
