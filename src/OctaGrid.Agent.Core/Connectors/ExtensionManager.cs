using System.Diagnostics.CodeAnalysis;
using OctaGrid.Extensions.Abstractions;
using OctaGrid.Protocol.Extensions;

namespace OctaGrid.Agent.Core.Connectors;

/// <summary>
/// Knows which extensions are installed, whether each is enabled and trusted, and remembers the user's choices.
/// Extensions are bundled connectors or connectors installed in the extensions folder; Store extensions plug in here later.
/// </summary>
/// <remarks>
/// An extension the user installed is not run until the user approves it: its code is not loaded before that, and the
/// approval is remembered for that version only.
/// </remarks>
public sealed class ExtensionManager
{
    private readonly object _gate = new();
    private readonly HashSet<string> _disabled;
    private readonly Dictionary<string, string> _trusted;
    private readonly Dictionary<string, ExtensionLoadResult> _notLoaded = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _folders = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _removed = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConnectorRegistry _registry;
    private readonly IExtensionStateStore _store;
    private readonly ExtensionLoader _loader;
    private readonly ExtensionInstaller? _installer;
    private readonly string? _extensionsRoot;

    /// <param name="registry">The connectors, starting with the bundled ones.</param>
    /// <param name="store">Where the user's choices are saved.</param>
    /// <param name="loader">Loads extension code; the default host version is used when null.</param>
    /// <param name="extensionsRoot">The extensions folder. Without it extensions cannot be installed or loaded from a folder.</param>
    public ExtensionManager(
        ConnectorRegistry registry,
        IExtensionStateStore store,
        ExtensionLoader? loader = null,
        string? extensionsRoot = null)
    {
        _registry = registry;
        _store = store;
        _loader = loader ?? new ExtensionLoader();
        _extensionsRoot = extensionsRoot;
        _installer = extensionsRoot is null ? null : new ExtensionInstaller(extensionsRoot);
        _disabled = new HashSet<string>(store.LoadDisabled(), StringComparer.OrdinalIgnoreCase);
        _trusted = new Dictionary<string, string>(store.LoadTrusted(), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>The installed connectors, for the orchestrator that creates sessions from them.</summary>
    public ConnectorRegistry Registry => _registry;

    /// <summary>Raised after an extension is enabled, disabled, installed, approved or removed.</summary>
    public event Action? Changed;

    /// <summary>True when the user agreed to run this version of the extension.</summary>
    public bool IsTrusted(ExtensionManifest manifest)
    {
        lock (_gate)
        {
            return _trusted.TryGetValue(manifest.Id, out var version) && string.Equals(version, manifest.Version, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Finishes removals that were waiting for a restart, then finds the extensions in the extensions folder.
    /// Extensions the user has not approved are listed but their code is not loaded.
    /// </summary>
    public void LoadInstalledExtensions()
    {
        if (_installer is null || _extensionsRoot is null)
        {
            return;
        }

        _installer.RemoveMarkedFolders();
        AddFolderExtensions(_loader.LoadAll(_extensionsRoot, IsTrusted));
    }

    /// <summary>
    /// Adds the extensions found in the extensions folder. Loaded ones join the registry; the others are
    /// listed with their state and the reason, so the user can see why an extension is not available.
    /// </summary>
    public void AddFolderExtensions(IEnumerable<ExtensionLoadResult> results)
    {
        lock (_gate)
        {
            foreach (var result in results)
            {
                if (result is { State: ExtensionState.Loaded, Connector: { } connector })
                {
                    try
                    {
                        _registry.Add(connector, ExtensionSource.UserFolder);
                        _folders[result.Id] = result.Folder;
                        continue;
                    }
                    catch (ArgumentException ex)
                    {
                        _notLoaded[result.Id] = result with { State = ExtensionState.Failed, Message = ex.Message };
                        continue;
                    }
                }

                _notLoaded[result.Id] = result;
                _folders[result.Id] = result.Folder;
            }
        }

        Changed?.Invoke();
    }

    /// <summary>
    /// Installs a package (a folder or a <c>.octaext</c> file) into the extensions folder. Nothing from the package
    /// runs; the extension waits for the user's approval.
    /// </summary>
    public ExtensionInstallResult Install(string packagePath)
    {
        if (_installer is null)
        {
            return ExtensionInstallResult.Fail("Installing extensions is not available here.");
        }

        var installed = _installer.Install(packagePath, out var folder);
        if (!installed.Success || folder is null)
        {
            return installed;
        }

        // Inspect only: "nobody is trusted" makes the loader stop before any extension code is loaded.
        var inspected = _loader.Load(folder, _ => false);
        var id = inspected.Id;
        var name = inspected.Manifest?.Name ?? id;

        lock (_gate)
        {
            if (_registry.TryGet(id, out _))
            {
                // A version of this extension is running, and its files cannot be replaced while it runs.
                return ExtensionInstallResult.Ok(id, $"Installed {name} {inspected.Manifest?.Version}. Restart OctaGrid to use it instead of the running version.");
            }

            _notLoaded[id] = inspected;
            _folders[id] = folder;
        }

        Changed?.Invoke();

        return inspected.State switch
        {
            ExtensionState.NeedsApproval => ExtensionInstallResult.Ok(id, $"Installed {name}. Review it to turn it on."),
            _ => ExtensionInstallResult.Ok(id, $"Installed {name}, but it cannot run: {inspected.Message}"),
        };
    }

    /// <summary>Trusts an installed extension and loads it now.</summary>
    public ExtensionInstallResult Approve(string id)
    {
        ExtensionLoadResult pending;
        lock (_gate)
        {
            if (!_notLoaded.TryGetValue(id, out pending!) || pending.State != ExtensionState.NeedsApproval || pending.Manifest is null)
            {
                return ExtensionInstallResult.Fail("This extension is not waiting for approval.", id);
            }
        }

        // Load outside the lock: this runs the extension's own code.
        var loaded = _loader.Load(pending.Folder);

        lock (_gate)
        {
            if (loaded is not { State: ExtensionState.Loaded, Connector: { } connector })
            {
                _notLoaded[id] = loaded;
                Changed?.Invoke();
                return ExtensionInstallResult.Fail(loaded.Message ?? "The extension could not be loaded.", id);
            }

            try
            {
                _registry.Add(connector, ExtensionSource.UserFolder);
            }
            catch (ArgumentException ex)
            {
                _notLoaded[id] = loaded with { State = ExtensionState.Failed, Message = ex.Message, Connector = null };
                Changed?.Invoke();
                return ExtensionInstallResult.Fail(ex.Message, id);
            }

            _notLoaded.Remove(id);
            _trusted[id] = pending.Manifest.Version;
            _store.SaveTrusted(_trusted);
        }

        Changed?.Invoke();
        return ExtensionInstallResult.Ok(id, $"{pending.Manifest.Name} is turned on.");
    }

    /// <summary>
    /// Removes an installed extension and forgets that the user trusted it. An extension that is running is turned
    /// off now and its files are deleted the next time OctaGrid starts.
    /// </summary>
    public ExtensionInstallResult Uninstall(string id)
    {
        if (_installer is null)
        {
            return ExtensionInstallResult.Fail("Removing extensions is not available here.", id);
        }

        string message;
        lock (_gate)
        {
            var isLoaded = _registry.TryGet(id, out var connector);
            if (isLoaded && _registry.GetSource(connector!.Descriptor.Id) == ExtensionSource.Bundled)
            {
                return ExtensionInstallResult.Fail("Built-in extensions cannot be removed. You can turn them off instead.", id);
            }

            if (!_folders.TryGetValue(id, out var folder))
            {
                return ExtensionInstallResult.Fail("This extension is not installed.", id);
            }

            var canonicalId = isLoaded ? connector!.Descriptor.Id : id;
            _installer.Remove(folder, isLoaded);

            if (_trusted.Remove(canonicalId))
            {
                _store.SaveTrusted(_trusted);
            }

            if (_disabled.Remove(canonicalId))
            {
                _store.SaveDisabled(_disabled.ToList());
            }

            if (isLoaded)
            {
                _removed.Add(canonicalId);
                message = "Removed. It is turned off now and its files are deleted the next time OctaGrid starts.";
            }
            else
            {
                _notLoaded.Remove(id);
                _folders.Remove(id);
                message = "Removed.";
            }
        }

        Changed?.Invoke();
        return ExtensionInstallResult.Ok(id, message);
    }

    /// <summary>All installed extensions, ordered by name.</summary>
    public IReadOnlyList<ExtensionInfo> GetExtensions()
    {
        lock (_gate)
        {
            return _registry.Connectors
                .Select(ToInfo)
                .Concat(_notLoaded.Values.Select(ToUnavailableInfo))
                .OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }
    }

    /// <summary>Descriptors of the connectors that are currently enabled.</summary>
    public IReadOnlyList<ConnectorDescriptor> EnabledDescriptors
    {
        get
        {
            lock (_gate)
            {
                return _registry.Connectors.Where(c => !IsOff(c.Descriptor.Id)).Select(c => c.Descriptor).ToList();
            }
        }
    }

    /// <summary>True when the extension is turned off, by the user or because it was removed. Unknown ids are not disabled.</summary>
    public bool IsDisabled(string id)
    {
        lock (_gate)
        {
            return IsOff(id);
        }
    }

    /// <summary>Finds an installed connector, whether or not it is enabled.</summary>
    public bool TryGetConnector(string id, [NotNullWhen(true)] out IConnector? connector) =>
        _registry.TryGet(id, out connector);

    /// <summary>Turns an extension on or off and remembers the choice.</summary>
    /// <returns>False when no such extension is installed.</returns>
    public bool SetEnabled(string id, bool enabled)
    {
        if (!_registry.TryGet(id, out var connector))
        {
            return false;
        }

        var canonicalId = connector.Descriptor.Id;
        bool changed;
        lock (_gate)
        {
            if (_removed.Contains(canonicalId))
            {
                return false;
            }

            changed = enabled ? _disabled.Remove(canonicalId) : _disabled.Add(canonicalId);
            if (changed)
            {
                _store.SaveDisabled(_disabled.ToList());
            }
        }

        if (changed)
        {
            Changed?.Invoke();
        }

        return true;
    }

    private bool IsOff(string id) => _disabled.Contains(id) || _removed.Contains(id);

    private static ExtensionInfo ToUnavailableInfo(ExtensionLoadResult result) => new(
        result.Id,
        result.Manifest?.Name ?? result.Id,
        result.Manifest?.Version ?? string.Empty,
        result.Manifest?.Publisher ?? string.Empty,
        result.Manifest?.Kind ?? ExtensionKinds.Connector,
        ExtensionSource.UserFolder,
        result.State,
        result.Message,
        ConnectorCapabilities.None,
        CanUninstall: true,
        Permissions: result.Manifest?.Permissions);

    private ExtensionInfo ToInfo(IConnector connector)
    {
        var descriptor = connector.Descriptor;
        var source = _registry.GetSource(descriptor.Id);
        var removed = _removed.Contains(descriptor.Id);
        return new ExtensionInfo(
            descriptor.Id,
            descriptor.DisplayName,
            descriptor.Version,
            descriptor.Publisher,
            ExtensionKinds.Connector,
            source,
            removed || _disabled.Contains(descriptor.Id) ? ExtensionState.Disabled : ExtensionState.Loaded,
            StateMessage: removed ? "Removed. It is turned off and will be deleted when OctaGrid restarts." : null,
            descriptor.Capabilities,
            CanUninstall: source != ExtensionSource.Bundled && !removed);
    }
}
