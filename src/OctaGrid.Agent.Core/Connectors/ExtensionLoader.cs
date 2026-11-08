using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OctaGrid.Extensions.Abstractions;
using OctaGrid.Protocol.Extensions;

namespace OctaGrid.Agent.Core.Connectors;

/// <summary>What happened when the host tried to load one extension folder.</summary>
/// <param name="Folder">The extension's folder.</param>
/// <param name="Manifest">The manifest, when it could be read.</param>
/// <param name="State"><see cref="ExtensionState.Loaded"/>, <see cref="ExtensionState.Incompatible"/> or <see cref="ExtensionState.Failed"/>.</param>
/// <param name="Message">Why the extension was not loaded.</param>
/// <param name="Connector">The connector, when the extension was loaded.</param>
/// <param name="Handle">Lets the host unload an extension that was loaded in developer mode; null otherwise.</param>
public sealed record ExtensionLoadResult(
    string Folder,
    ExtensionManifest? Manifest,
    ExtensionState State,
    string? Message,
    IConnector? Connector,
    ExtensionHandle? Handle = null)
{
    /// <summary>The manifest id, or the folder name when there is no readable manifest.</summary>
    public string Id => Manifest?.Id ?? Path.GetFileName(Folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
}

/// <summary>
/// Owns the load context of an extension that was loaded from a shadow copy (developer mode). Unloading it lets the
/// runtime free the old version once nothing refers to it any more, so a rebuilt extension can be loaded in its place.
/// </summary>
public sealed class ExtensionHandle
{
    private AssemblyLoadContext? _context;
    private readonly string _shadowFolder;

    internal ExtensionHandle(AssemblyLoadContext context, string shadowFolder)
    {
        _context = context;
        _shadowFolder = shadowFolder;
        WeakContext = new WeakReference(context);
    }

    /// <summary>A reference that does not keep the load context alive; it is dead once the old version is gone.</summary>
    public WeakReference WeakContext { get; }

    /// <summary>Starts unloading the extension and, once the runtime lets go of its files, deletes the shadow copy.</summary>
    public void Unload()
    {
        var context = _context;
        _context = null;
        context?.Unload();

        // The files stay locked until the runtime has collected the context, which can take a few collections.
        var folder = _shadowFolder;
        _ = Task.Run(async () =>
        {
            for (var attempt = 0; attempt < 20; attempt++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                try
                {
                    if (Directory.Exists(folder))
                    {
                        Directory.Delete(folder, recursive: true);
                    }

                    return;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    await Task.Delay(500);
                }
            }
        });
    }
}

/// <summary>
/// Finds extensions in a folder and loads them in-process, each into its own <see cref="AssemblyLoadContext"/>.
/// One extension that is broken or does not fit this version never stops the others from loading.
/// </summary>
/// <remarks>
/// Layout: <c>&lt;root&gt;/&lt;name&gt;/octagrid-extension.json</c> (a developer's build output folder) or
/// <c>&lt;root&gt;/&lt;id&gt;/&lt;version&gt;/octagrid-extension.json</c> (installed packages; only the highest version is loaded).
/// </remarks>
public sealed class ExtensionLoader
{
    private readonly ExtensionVersion _host;
    private readonly ILoggerFactory? _loggerFactory;
    private readonly ILogger _logger;

    public ExtensionLoader(ExtensionVersion? hostVersion = null, ILoggerFactory? loggerFactory = null)
    {
        _host = hostVersion ?? ExtensionVersion.Host;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory?.CreateLogger<ExtensionLoader>() ?? NullLogger<ExtensionLoader>.Instance;
    }

    /// <summary>Loads every extension found under <paramref name="root"/>. A missing folder means there are none.</summary>
    /// <param name="root">The extensions folder.</param>
    /// <param name="isTrusted">
    /// Decides whether the user agreed to run an extension. The code of an extension that is not trusted is never
    /// loaded; it is reported as <see cref="ExtensionState.NeedsApproval"/>. Null trusts everything.
    /// </param>
    public IReadOnlyList<ExtensionLoadResult> LoadAll(string root, Func<ExtensionManifest, bool>? isTrusted = null)
    {
        if (!Directory.Exists(root))
        {
            return [];
        }

        var results = new List<ExtensionLoadResult>();
        foreach (var folder in FindExtensionFolders(root))
        {
            var result = Load(folder, isTrusted);
            if (result.State == ExtensionState.Loaded)
            {
                _logger.LogInformation("Loaded extension '{ExtensionId}' from {Folder}", result.Id, folder);
            }
            else
            {
                _logger.LogWarning("Extension in {Folder} is {State}: {Message}", folder, result.State, result.Message);
            }

            results.Add(result);
        }

        return results;
    }

    /// <summary>
    /// Loads the extension(s) a developer points at: either one extension folder (a build output folder with the
    /// manifest in it) or a folder that contains several. They are trusted without asking, loaded from a shadow copy
    /// (so the next build can overwrite the original files) and can be unloaded through <see cref="ExtensionLoadResult.Handle"/>.
    /// </summary>
    public IReadOnlyList<ExtensionLoadResult> LoadDevelopment(string folder)
    {
        if (!Directory.Exists(folder))
        {
            return [];
        }

        var folders = File.Exists(Path.Combine(folder, ExtensionManifest.FileName))
            ? [folder]
            : FindExtensionFolders(folder).ToList();

        var results = new List<ExtensionLoadResult>();
        foreach (var extensionFolder in folders)
        {
            var result = Load(extensionFolder, isTrusted: null, development: true);
            _logger.LogInformation("Developer extension in {Folder}: {State} {Message}", extensionFolder, result.State, result.Message);
            results.Add(result);
        }

        return results;
    }

    /// <summary>
    /// Loads the extension in one folder; never throws for a bad extension. The manifest and the host version are checked
    /// first and no extension code runs until <paramref name="isTrusted"/> agrees (null trusts everything).
    /// </summary>
    /// <param name="folder">The extension folder.</param>
    /// <param name="isTrusted">Decides whether the user trusts the extension; null trusts everything.</param>
    /// <param name="development">Load from a shadow copy into a context that can be unloaded.</param>
    public ExtensionLoadResult Load(string folder, Func<ExtensionManifest, bool>? isTrusted = null, bool development = false)
    {
        ExtensionManifest? manifest = null;
        try
        {
            var manifestPath = Path.Combine(folder, ExtensionManifest.FileName);
            manifest = ExtensionManifest.Parse(File.ReadAllText(manifestPath));

            var errors = manifest.Validate();
            if (errors.Count > 0)
            {
                return Fail(folder, manifest, ExtensionState.Failed, "The manifest is not valid: " + string.Join(" ", errors));
            }

            foreach (var warning in manifest.Warnings())
            {
                _logger.LogWarning("Extension {ExtensionId} in {Folder}: {Warning}", manifest.Id, folder, warning);
            }

            if (!HostVersionRange.TryParse(manifest.Host, out var range, out var rangeError))
            {
                return Fail(folder, manifest, ExtensionState.Failed, rangeError);
            }

            if (!range.Satisfies(_host))
            {
                // A pre-release OctaGrid only runs extensions whose range names a pre-release; say so, because
                // ">=1.0.0 <2.0.0" looks like it should fit.
                var hint = _host.IsPreRelease && !range.AcceptsPreRelease
                    ? $" This is a pre-release, so the range must name a pre-release, for example >={_host.Major}.{_host.Minor}.{_host.Patch}-{_host.PreRelease!.Split('.')[0]}."
                    : string.Empty;
                return Fail(folder, manifest, ExtensionState.Incompatible,
                    $"Requires OctaGrid {range.Text}; this is OctaGrid {_host}.{hint}");
            }

            if (string.IsNullOrWhiteSpace(manifest.Assembly))
            {
                return Fail(folder, manifest, ExtensionState.Failed,
                    "Out-of-process extensions are not supported yet; the manifest needs 'assembly' and 'entryType'.");
            }

            if (isTrusted is not null && !isTrusted(manifest))
            {
                return Fail(folder, manifest, ExtensionState.NeedsApproval,
                    "Review and approve this extension to run it. Its code has not been loaded.");
            }

            var (connector, handle) = CreateConnector(folder, manifest, development);
            if (!string.Equals(connector.Descriptor.Id, manifest.Id, StringComparison.OrdinalIgnoreCase))
            {
                handle?.Unload();
                return Fail(folder, manifest, ExtensionState.Failed,
                    $"The manifest id '{manifest.Id}' differs from the connector id '{connector.Descriptor.Id}'.");
            }

            return new ExtensionLoadResult(folder, manifest, ExtensionState.Loaded, null, connector, handle);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            var message = ex switch
            {
                FileNotFoundException or DirectoryNotFoundException when manifest is null => $"{ExtensionManifest.FileName} was not found.",
                JsonException => $"{ExtensionManifest.FileName} is not valid JSON: {ex.Message}",
                TargetInvocationException { InnerException: { } inner } => $"The connector threw while it was created: {inner.Message}",
                _ => ex.Message,
            };
            return Fail(folder, manifest, ExtensionState.Failed, message);
        }
    }

    private static ExtensionLoadResult Fail(string folder, ExtensionManifest? manifest, ExtensionState state, string message) =>
        new(folder, manifest, state, message, null);

    /// <summary>Where developer-mode extensions are copied to before they are loaded.</summary>
    private static string ShadowRoot => Path.Combine(Path.GetTempPath(), "OctaGrid-extensions-dev");

    /// <summary>
    /// Deletes the shadow copies that earlier runs left behind (they cannot be deleted while the process that loaded them
    /// is still running). Only copies that have not been touched for <paramref name="olderThan"/> (an hour by default)
    /// are removed, so a copy another OctaGrid has just made, and is about to load, is never taken away; copies that are
    /// still in use are skipped.
    /// </summary>
    public static void CleanUpShadowCopies(TimeSpan? olderThan = null)
    {
        var cutoff = DateTime.UtcNow - (olderThan ?? TimeSpan.FromHours(1));
        if (!Directory.Exists(ShadowRoot))
        {
            return;
        }

        foreach (var folder in Directory.EnumerateDirectories(ShadowRoot))
        {
            if (Directory.GetLastWriteTimeUtc(folder) > cutoff)
            {
                continue;
            }

            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Another OctaGrid is running from it.
            }
        }
    }

    private (IConnector Connector, ExtensionHandle? Handle) CreateConnector(string folder, ExtensionManifest manifest, bool development)
    {
        var root = Path.GetFullPath(folder);
        var assemblyPath = Path.GetFullPath(Path.Combine(root, manifest.Assembly!));
        if (!assemblyPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("'assembly' points outside the extension folder.");
        }

        if (!File.Exists(assemblyPath))
        {
            throw new FileNotFoundException($"The assembly '{manifest.Assembly}' was not found in the extension folder.");
        }

        if (!development)
        {
            var connector = Instantiate(new ExtensionLoadContext(manifest.Id, assemblyPath, collectible: false), assemblyPath, manifest);
            return (connector, null);
        }

        // Developer mode: run a copy, so that building the extension again can overwrite the original files while the
        // old version is still loaded, and use a context the host can unload before it loads the new build.
        var shadow = Path.Combine(ShadowRoot, Guid.NewGuid().ToString("N"));
        CopyFolder(root, shadow);
        var shadowAssembly = Path.Combine(shadow, Path.GetRelativePath(root, assemblyPath));

        var context = new ExtensionLoadContext(manifest.Id, shadowAssembly, collectible: true);
        var handle = new ExtensionHandle(context, shadow);
        try
        {
            return (Instantiate(context, shadowAssembly, manifest), handle);
        }
        catch
        {
            handle.Unload();
            throw;
        }
    }

    private static void CopyFolder(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    private IConnector Instantiate(AssemblyLoadContext context, string assemblyPath, ExtensionManifest manifest)
    {
        var assembly = context.LoadFromAssemblyPath(assemblyPath);

        var type = assembly.GetType(manifest.EntryType!, throwOnError: false)
            ?? throw new InvalidOperationException($"The type '{manifest.EntryType}' was not found in '{manifest.Assembly}'.");

        if (!typeof(IConnector).IsAssignableFrom(type) || type.IsAbstract)
        {
            throw new InvalidOperationException(
                $"'{manifest.EntryType}' is not a connector: it must be a class that implements {nameof(IConnector)} " +
                "from the same OctaGrid.Extensions.Abstractions the host uses.");
        }

        // The connector may take the host's logger factory; otherwise it needs a parameterless constructor.
        var withLogging = type.GetConstructor([typeof(ILoggerFactory)]);
        var instance = withLogging is not null
            ? withLogging.Invoke([_loggerFactory])
            : (type.GetConstructor(Type.EmptyTypes)
                ?? throw new InvalidOperationException($"'{manifest.EntryType}' needs a public parameterless constructor."))
                .Invoke(null);

        return (IConnector)instance;
    }

    private static bool IsMarkedForRemoval(string folder) => File.Exists(Path.Combine(folder, ExtensionInstaller.RemoveMarker));

    private static IEnumerable<string> FindExtensionFolders(string root)
    {
        foreach (var directory in Directory.EnumerateDirectories(root).Order(StringComparer.OrdinalIgnoreCase))
        {
            // ".staging-*" holds a package that is being installed.
            if (Path.GetFileName(directory).StartsWith('.'))
            {
                continue;
            }

            if (File.Exists(Path.Combine(directory, ExtensionManifest.FileName)))
            {
                if (!IsMarkedForRemoval(directory))
                {
                    yield return directory;
                }

                continue;
            }

            // <id>/<version>/: load the highest version, ignore the rest.
            var latest = Directory.EnumerateDirectories(directory)
                .Where(d => File.Exists(Path.Combine(d, ExtensionManifest.FileName)) && !IsMarkedForRemoval(d))
                .Select(d => (Folder: d, Version: ExtensionVersion.TryParse(Path.GetFileName(d), out var v) ? v : null))
                .OrderByDescending(x => x.Version)
                .ThenByDescending(x => x.Folder, StringComparer.OrdinalIgnoreCase)
                .Select(x => x.Folder)
                .FirstOrDefault();

            if (latest is not null)
            {
                yield return latest;
            }
        }
    }

    /// <summary>
    /// Loads an extension's assemblies from its own folder. The SDK, the protocol types, the framework and the
    /// logging abstractions come from the host so that <see cref="IConnector"/> is the same type on both sides.
    /// </summary>
    private sealed class ExtensionLoadContext : AssemblyLoadContext
    {
        private static readonly string[] SharedPrefixes =
        [
            "OctaGrid.Extensions.Abstractions",
            "OctaGrid.Protocol",
            "Microsoft.Extensions.",
            "System.",
        ];

        private readonly AssemblyDependencyResolver _resolver;
        private readonly string _directory;

        public ExtensionLoadContext(string extensionId, string mainAssemblyPath, bool collectible)
            : base($"extension:{extensionId}", isCollectible: collectible)
        {
            _resolver = new AssemblyDependencyResolver(mainAssemblyPath);
            _directory = Path.GetDirectoryName(mainAssemblyPath)!;
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            var name = assemblyName.Name;
            if (name is null)
            {
                return null;
            }

            if (SharedPrefixes.Any(p => name.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
            {
                // Use the host's copy whatever version the extension was built against. Asking by name only avoids
                // the default context refusing a higher build number (it changes with every commit); real
                // incompatibility is what the manifest's host range is for.
                try
                {
                    return Default.LoadFromAssemblyName(new AssemblyName(name));
                }
                catch (FileNotFoundException)
                {
                    return null;
                }
            }

            var path = _resolver.ResolveAssemblyToPath(assemblyName);
            if (path is null)
            {
                var sibling = Path.Combine(_directory, name + ".dll");
                path = File.Exists(sibling) ? sibling : null;
            }

            return path is null ? null : LoadFromAssemblyPath(path);
        }

        protected override nint LoadUnmanagedDll(string unmanagedDllName)
        {
            var path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
            return path is null ? nint.Zero : LoadUnmanagedDllFromPath(path);
        }
    }
}
