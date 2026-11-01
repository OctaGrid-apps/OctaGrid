using System.Diagnostics;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OctaGrid.Client.Models;
using OctaGrid.Client.Services;
using Windows.Services.Store;
using WinRT.Interop;

namespace OctaGrid.Client.Desktop.Services;

public sealed class StoreUpdateService : IUpdateService
{
    public const string StoreProductId = "9NWQ15C73TXL";
    private const string StoreProductUri = $"ms-windows-store://pdp/?ProductId={StoreProductId}";
    private const string DisplayCatalogUri = $"https://displaycatalog.mp.microsoft.com/v7/products/{StoreProductId}?market=US&languages=en-US";

    private static readonly HttpClient HttpClient = new()
    {
        DefaultRequestHeaders = { { "User-Agent", "OctaGrid-Desktop" } }
    };

    private readonly ILogger<StoreUpdateService> _logger;

    public StoreUpdateService(ILogger<StoreUpdateService>? logger = null)
    {
        _logger = logger ?? NullLogger<StoreUpdateService>.Instance;
    }

    public UpdateChannel Channel => UpdateChannel.MicrosoftStore;

    public bool IsUpdateSupported => true;

    public async Task<AppUpdateInfo> CheckForUpdatesAsync(CancellationToken cancellationToken)
    {
        var currentPackageVersion = PackageEnvironment.GetCurrentPackageVersion();
        var currentVersion = currentPackageVersion?.ToString() ?? Helper.GetAppVersion();

        _logger.LogInformation("Checking for Microsoft Store updates (current version: {Version})", currentVersion);

        try
        {
            var storeContext = StoreContext.GetDefault();
            InitializeStoreContextWithWindow(storeContext);

            var updates = await storeContext.GetAppAndOptionalStorePackageUpdatesAsync().AsTask(cancellationToken);
            if (updates != null && updates.Count > 0)
            {
                var targetVer = updates[0].Package.Id.Version;
                var targetVersionStr = $"{targetVer.Major}.{targetVer.Minor}.{targetVer.Build}.{targetVer.Revision}";

                _logger.LogInformation("StoreContext found {Count} update(s). Target version: {Version}", updates.Count, targetVersionStr);
                return new AppUpdateInfo(
                    true,
                    currentVersion,
                    targetVersionStr,
                    $"A new version ({targetVersionStr}) is available in the Microsoft Store.",
                    updates);
            }

            _logger.LogInformation("StoreContext reported application is up to date");
            return new AppUpdateInfo(false, currentVersion, currentVersion, null, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // StoreContext throws on sideloaded/dev builds without a Store license (0x803F6107)
            _logger.LogWarning(ex, "StoreContext query failed (likely sideloaded/dev build). Falling back to Display Catalog check");
        }

        try
        {
            using var response = await HttpClient.GetAsync(DisplayCatalogUri, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

                Version? latestStoreVersion = null;
                string? latestPackageFullName = null;

                if (doc.RootElement.TryGetProperty("Product", out var product) &&
                    product.TryGetProperty("DisplaySkuAvailabilities", out var skus))
                {
                    foreach (var skuItem in skus.EnumerateArray())
                    {
                        if (skuItem.TryGetProperty("Sku", out var sku) &&
                            sku.TryGetProperty("Properties", out var props) &&
                            props.TryGetProperty("Packages", out var packages))
                        {
                            foreach (var pkg in packages.EnumerateArray())
                            {
                                var pkgVersion = ExtractPackageVersion(pkg, out var pkgFullName);
                                if (pkgVersion is not null)
                                {
                                    if (latestStoreVersion is null || pkgVersion > latestStoreVersion)
                                    {
                                        latestStoreVersion = pkgVersion;
                                        latestPackageFullName = pkgFullName;
                                    }
                                }
                            }
                        }
                    }
                }

                if (latestStoreVersion is not null)
                {
                    var targetVersionStr = latestStoreVersion.ToString();
                    var isNewer = IsNewerVersion(currentVersion, targetVersionStr);

                    return new AppUpdateInfo(
                        isNewer,
                        currentVersion,
                        targetVersionStr,
                        $"A new version ({targetVersionStr}) is available in the Microsoft Store.",
                        latestPackageFullName);
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Display Catalog fallback check failed");
        }

        return new AppUpdateInfo(false, currentVersion, currentVersion, null, null);
    }

    public async Task DownloadAndApplyUpdateAsync(AppUpdateInfo updateInfo, Action<int>? progressCallback, CancellationToken cancellationToken)
    {
        // If updates were returned by native StoreContext, trigger the small OS update modal dialog
        if (updateInfo.RawUpdate is IReadOnlyList<StorePackageUpdate> storeUpdates && storeUpdates.Count > 0)
        {
            _logger.LogInformation("Triggering native Windows Store update modal dialog for {Count} package(s)", storeUpdates.Count);

            try
            {
                var storeContext = StoreContext.GetDefault();
                InitializeStoreContextWithWindow(storeContext);

                // This triggers the native Windows small modal dialog with progress and auto-relaunch
                StorePackageUpdateResult result;
                if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
                {
                    result = await storeContext.RequestDownloadAndInstallStorePackageUpdatesAsync(storeUpdates).AsTask(cancellationToken);
                }
                else
                {
                    result = await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
                        await storeContext.RequestDownloadAndInstallStorePackageUpdatesAsync(storeUpdates).AsTask(cancellationToken));
                }

                _logger.LogInformation("Native Store package update result: {State}", result.OverallState);
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Failed to launch native Store update dialog; falling back to Store URI");
            }
        }

        // Fallback: Launch MS Store product page and shut down application
        _logger.LogInformation("Launching Microsoft Store page for {ProductId} and closing application", StoreProductId);
        await OpenStoreListingAsync();
        await Task.Delay(500, cancellationToken);

        if (App.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
        else
        {
            Environment.Exit(0);
        }
    }

    public Task OpenStoreListingAsync()
    {
        try
        {
            Process.Start(new ProcessStartInfo(StoreProductUri)
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to launch Microsoft Store via Process.Start, falling back to TopLevel launcher");

            if (App.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                var topLevel = TopLevel.GetTopLevel(desktop.MainWindow);
                if (topLevel?.Launcher != null)
                {
                    return topLevel.Launcher.LaunchUriAsync(new Uri(StoreProductUri));
                }
            }
        }

        return Task.CompletedTask;
    }

    private static void InitializeStoreContextWithWindow(StoreContext storeContext)
    {
        void BindHwnd()
        {
            if (App.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop &&
                desktop.MainWindow is { } window)
            {
                var platformHandle = window.TryGetPlatformHandle();
                if (platformHandle != null)
                {
                    InitializeWithWindow.Initialize(storeContext, platformHandle.Handle);
                }
            }
        }

        if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        {
            BindHwnd();
        }
        else
        {
            Avalonia.Threading.Dispatcher.UIThread.Invoke(BindHwnd);
        }
    }

    private static Version? ExtractPackageVersion(JsonElement pkg, out string? packageFullName)
    {
        packageFullName = null;

        if (pkg.TryGetProperty("PlatformDependencyXmlBlob", out var blobElem) &&
            blobElem.GetString() is { } blobJson)
        {
            try
            {
                using var blobDoc = JsonDocument.Parse(blobJson);
                if (blobDoc.RootElement.TryGetProperty("content.bundledPackages", out var bundledPackages))
                {
                    Version? maxBundledVersion = null;
                    string? bestFullName = null;

                    foreach (var bundledPkg in bundledPackages.EnumerateArray())
                    {
                        if (bundledPkg.GetString() is { } bName)
                        {
                            var parts = bName.Split('_');
                            if (parts.Length > 1 && Version.TryParse(parts[1], out var bVer))
                            {
                                if (maxBundledVersion is null || bVer > maxBundledVersion)
                                {
                                    maxBundledVersion = bVer;
                                    bestFullName = bName;
                                }
                            }
                        }
                    }

                    if (maxBundledVersion is not null)
                    {
                        packageFullName = bestFullName;
                        return maxBundledVersion;
                    }
                }
            }
            catch
            {
            }
        }

        if (pkg.TryGetProperty("PackageFullName", out var fullNameElem) &&
            fullNameElem.GetString() is { } fullName)
        {
            var parts = fullName.Split('_');
            if (parts.Length > 1 && Version.TryParse(parts[1], out var parsedVer))
            {
                packageFullName = fullName;
                return parsedVer;
            }
        }

        return null;
    }

    private static bool IsNewerVersion(string currentVersion, string latestVersion)
    {
        var cleanCurrent = currentVersion.TrimStart('v', 'V');
        var cleanLatest = latestVersion.TrimStart('v', 'V');

        var dashCur = cleanCurrent.IndexOf('-');
        if (dashCur > 0)
        {
            cleanCurrent = cleanCurrent[..dashCur];
        }

        var plusCur = cleanCurrent.IndexOf('+');
        if (plusCur > 0)
        {
            cleanCurrent = cleanCurrent[..plusCur];
        }

        var dashLat = cleanLatest.IndexOf('-');
        if (dashLat > 0)
        {
            cleanLatest = cleanLatest[..dashLat];
        }

        var plusLat = cleanLatest.IndexOf('+');
        if (plusLat > 0)
        {
            cleanLatest = cleanLatest[..plusLat];
        }

        return Version.TryParse(cleanCurrent, out var cur) &&
               Version.TryParse(cleanLatest, out var lat) &&
               lat > cur;
    }
}
