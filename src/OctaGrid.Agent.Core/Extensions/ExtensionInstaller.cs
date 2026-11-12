using System.IO.Compression;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OctaGrid.Extensions.Abstractions;
using OctaGrid.Protocol.Extensions;

namespace OctaGrid.Agent.Core.Extensions;

/// <summary>
/// Puts extension packages into the extensions folder and takes them out again. It only moves files: it never
/// loads extension code, so nothing from a package runs before the user trusts it.
/// </summary>
/// <remarks>
/// A package is a folder, or a <c>.octaext</c> (zip) file, that contains <c>octagrid-extension.json</c> at its root
/// (or inside a single top-level folder). It is installed as <c>&lt;root&gt;/&lt;id&gt;/&lt;version&gt;/</c>.
/// </remarks>
public sealed class ExtensionInstaller
{
    /// <summary>An empty file that marks a folder to be deleted the next time the host starts.</summary>
    public const string RemoveMarker = ".uninstall";

    /// <summary>The largest package, unpacked, that will be installed.</summary>
    public const long MaxUnpackedBytes = 256L * 1024 * 1024;

    private const int MaxEntries = 5000;

    private readonly string _root;
    private readonly ILogger _logger;

    public ExtensionInstaller(string root, ILogger? logger = null)
    {
        _root = Path.GetFullPath(root);
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>Installs a package folder or <c>.octaext</c>/<c>.zip</c> file.</summary>
    /// <param name="sourcePath">The package.</param>
    /// <param name="installedFolder">The folder the package was installed into, when it worked.</param>
    public ExtensionInstallResult Install(string sourcePath, out string? installedFolder)
    {
        installedFolder = null;
        var staging = Path.Combine(_root, ".staging-" + Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(staging);

            if (Directory.Exists(sourcePath))
            {
                var error = CopyDirectory(Path.GetFullPath(sourcePath), staging);
                if (error is not null)
                {
                    return ExtensionInstallResult.Fail(error);
                }
            }
            else if (File.Exists(sourcePath))
            {
                var error = Unpack(sourcePath, staging);
                if (error is not null)
                {
                    return ExtensionInstallResult.Fail(error);
                }
            }
            else
            {
                return ExtensionInstallResult.Fail("The package was not found.");
            }

            var packageRoot = FindPackageRoot(staging);
            if (packageRoot is null)
            {
                return ExtensionInstallResult.Fail($"The package has no {ExtensionManifest.FileName}.");
            }

            ExtensionManifest manifest;
            try
            {
                manifest = ExtensionManifest.Parse(File.ReadAllText(Path.Combine(packageRoot, ExtensionManifest.FileName)));
            }
            catch (JsonException ex)
            {
                return ExtensionInstallResult.Fail($"{ExtensionManifest.FileName} is not valid JSON: {ex.Message}");
            }

            var errors = manifest.Validate();
            if (errors.Count > 0)
            {
                return ExtensionInstallResult.Fail("The manifest is not valid: " + string.Join(" ", errors), manifest.Id);
            }

            var target = Path.Combine(_root, manifest.Id, manifest.Version);
            if (Directory.Exists(target) && File.Exists(Path.Combine(target, RemoveMarker)))
            {
                // The user removed this version, but the folder is still there because its files were locked,
                // for example by a second OctaGrid window that still has the extension loaded. It is not installed.
                // Rename it first: a rename fails as a whole while any file is open, whereas a recursive delete
                // would remove the unlocked files (the marker too) and leave a broken folder behind.
                var aside = Path.Combine(_root, ".staging-" + Guid.NewGuid().ToString("N"));
                try
                {
                    Directory.Move(target, aside);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    _logger.LogWarning(ex, "Extension folder {Folder} is marked for removal but is still locked", target);
                    return ExtensionInstallResult.Fail(
                        $"{manifest.Name} {manifest.Version} was removed but its files are still in use. Close other OctaGrid windows and try again.", manifest.Id);
                }

                // The marker moved with it, so a folder that cannot be deleted now is retried at the next start.
                TryDelete(aside);
            }

            if (Directory.Exists(target))
            {
                return ExtensionInstallResult.Fail($"{manifest.Name} {manifest.Version} is already installed.", manifest.Id);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            Directory.Move(packageRoot, target);

            installedFolder = target;
            _logger.LogInformation("Installed extension {ExtensionId} {Version} into {Folder}", manifest.Id, manifest.Version, target);
            return ExtensionInstallResult.Ok(manifest.Id, $"Installed {manifest.Name} {manifest.Version}.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            _logger.LogWarning(ex, "Could not install the extension package {Source}", sourcePath);
            return ExtensionInstallResult.Fail($"Could not install the package: {ex.Message}");
        }
        finally
        {
            TryDelete(staging);
        }
    }

    /// <summary>
    /// Removes an installed extension folder. Files of an extension that is loaded are locked on Windows, so such a
    /// folder is only marked and deleted by <see cref="RemoveMarkedFolders"/> the next time the host starts.
    /// </summary>
    /// <returns>True when the folder is gone; false when it was marked for removal.</returns>
    public bool Remove(string folder, bool isLoaded)
    {
        if (!isLoaded && TryDelete(folder))
        {
            RemoveEmptyParent(folder);
            return true;
        }

        File.WriteAllText(Path.Combine(folder, RemoveMarker), string.Empty);
        return false;
    }

    /// <summary>Deletes the folders that were marked for removal. Call it before extensions are loaded.</summary>
    public void RemoveMarkedFolders()
    {
        if (!Directory.Exists(_root))
        {
            return;
        }

        foreach (var folder in Directory.EnumerateDirectories(_root, "*", SearchOption.AllDirectories).ToList())
        {
            if (Directory.Exists(folder) && File.Exists(Path.Combine(folder, RemoveMarker)))
            {
                if (TryDelete(folder))
                {
                    _logger.LogInformation("Removed extension folder {Folder}", folder);
                    RemoveEmptyParent(folder);
                }
                else
                {
                    _logger.LogWarning("Could not remove extension folder {Folder}; it will be tried again at the next start", folder);
                }
            }
        }
    }

    private static string? FindPackageRoot(string staging)
    {
        if (File.Exists(Path.Combine(staging, ExtensionManifest.FileName)))
        {
            return staging;
        }

        // A zip made from a folder keeps that folder as its only top-level entry.
        var children = Directory.GetDirectories(staging);
        return children.Length == 1 && File.Exists(Path.Combine(children[0], ExtensionManifest.FileName)) ? children[0] : null;
    }

    private static string? CopyDirectory(string source, string destination)
    {
        long total = 0;
        var count = 0;
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            total += new FileInfo(file).Length;
            if (++count > MaxEntries || total > MaxUnpackedBytes)
            {
                return "The package is too large.";
            }

            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }

        return null;
    }

    private static string? Unpack(string file, string destination)
    {
        using var archive = ZipFile.OpenRead(file);
        if (archive.Entries.Count > MaxEntries || archive.Entries.Sum(e => e.Length) > MaxUnpackedBytes)
        {
            return "The package is too large.";
        }

        var root = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;
        foreach (var entry in archive.Entries)
        {
            var target = Path.GetFullPath(Path.Combine(destination, entry.FullName));

            // A name such as "../x" would write outside the folder.
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                return "The package contains a file outside its folder.";
            }

            if (entry.Name.Length == 0)
            {
                Directory.CreateDirectory(target);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target);
        }

        return null;
    }

    private static bool TryDelete(string folder)
    {
        try
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    // <root>/<id>/<version>: drop <id> when its last version is gone.
    private void RemoveEmptyParent(string folder)
    {
        var parent = Path.GetDirectoryName(Path.GetFullPath(folder));
        if (parent is not null
            && !string.Equals(parent, _root, StringComparison.OrdinalIgnoreCase)
            && Directory.Exists(parent)
            && !Directory.EnumerateFileSystemEntries(parent).Any())
        {
            TryDelete(parent);
        }
    }
}
