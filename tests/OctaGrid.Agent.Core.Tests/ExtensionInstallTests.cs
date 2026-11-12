using System.IO.Compression;
using OctaGrid.Agent.Core.Connectors;
using OctaGrid.Agent.Core.Extensions;
using OctaGrid.Extensions.Abstractions;
using OctaGrid.Extensions.Models;
using OctaGrid.Protocol.Extensions;

namespace OctaGrid.Agent.Core.Tests;

/// <summary>A connector only the trust tests load, so that "its code was loaded" can be checked reliably.</summary>
public sealed class TrustSampleConnector : IConnector
{
    public const string Id = "acme.trust-sample";

    public ConnectorDescriptor Descriptor { get; } = new(Id, "Trust Sample", "1.0.0", "Acme", ConnectorCapabilities.None, [], []);

    public IPrinterSession CreateSession(PrinterProfile profile, IConnectorContext context) => new MinimalSession();
}

public sealed class ExtensionInstallTests : IDisposable
{
    private static readonly ExtensionVersion Host = ExtensionVersion.Parse("1.2.0");

    private readonly string _work = Path.Combine(Path.GetTempPath(), "octagrid-install-" + Guid.NewGuid().ToString("N"));

    private string Root => Path.Combine(_work, "extensions");

    private string Packages => Path.Combine(_work, "packages");

    public void Dispose()
    {
        try
        {
            Directory.Delete(_work, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Loaded assemblies stay locked on Windows until the process ends.
        }
    }

    private static string Manifest(string version = "1.0.0", string[]? permissions = null, string? projectUrl = null) => $$"""
        {
          "id": "{{TrustSampleConnector.Id}}",
          "name": "Trust Sample",
          "version": "{{version}}",
          "publisher": "Acme",
          "kind": "connector",
          "host": ">=1.0.0 <2.0.0",
          "assembly": "{{Path.GetFileName(typeof(ExtensionInstallTests).Assembly.Location)}}",
          "entryType": "{{typeof(TrustSampleConnector).FullName}}",
          "permissions": [{{string.Join(", ", (permissions ?? []).Select(p => $"\"{p}\""))}}],
          "projectUrl": {{(projectUrl is null ? "null" : $"\"{projectUrl}\"")}}
        }
        """;

    /// <summary>Creates a package folder: the manifest and a copy of this test assembly, which holds the connector.</summary>
    private string Package(string name, string? manifest = null)
    {
        var folder = Path.Combine(Packages, name);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, ExtensionManifest.FileName), manifest ?? Manifest());
        var source = typeof(ExtensionInstallTests).Assembly.Location;
        File.Copy(source, Path.Combine(folder, Path.GetFileName(source)), overwrite: true);
        return folder;
    }

    private string Zip(string packageFolder, bool keepTopLevelFolder = false)
    {
        var zip = Path.Combine(Packages, Path.GetFileName(packageFolder) + ".octaext");
        if (!keepTopLevelFolder)
        {
            ZipFile.CreateFromDirectory(packageFolder, zip);
        }
        else
        {
            ZipFile.CreateFromDirectory(packageFolder, zip, CompressionLevel.Fastest, includeBaseDirectory: true);
        }

        return zip;
    }

    private (ExtensionManager Manager, InMemoryStateStore Store) Create(InMemoryStateStore? store = null, ConnectorRegistry? registry = null)
    {
        store ??= new InMemoryStateStore();
        return (new ExtensionManager(registry ?? new ConnectorRegistry(), store, new ExtensionLoader(Host), Root), store);
    }

    // Each test has its own folder, so this is true only when this test loaded code from its extensions folder.
    private bool CodeWasLoaded() => AppDomain.CurrentDomain.GetAssemblies()
        .Any(a => !a.IsDynamic && a.Location.StartsWith(Root, StringComparison.OrdinalIgnoreCase));

    // ---- Install ----

    [Fact]
    public void Installing_a_folder_copies_it_and_waits_for_approval_without_loading_its_code()
    {
        var (manager, _) = Create();

        var result = manager.Install(Package("sample"));

        Assert.True(result.Success, result.Message);
        Assert.Equal(TrustSampleConnector.Id, result.ExtensionId);
        Assert.True(File.Exists(Path.Combine(Root, TrustSampleConnector.Id, "1.0.0", ExtensionManifest.FileName)));

        var info = Assert.Single(manager.GetExtensions());
        Assert.Equal(ExtensionState.NeedsApproval, info.State);
        Assert.Equal("Acme", info.Publisher);
        Assert.True(info.CanUninstall);
        Assert.False(manager.TryGetConnector(TrustSampleConnector.Id, out _));
        Assert.False(CodeWasLoaded());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Installing_an_octaext_file_works_with_or_without_a_top_level_folder(bool keepTopLevelFolder)
    {
        var (manager, _) = Create();
        var zip = Zip(Package("zipped"), keepTopLevelFolder);

        var result = manager.Install(zip);

        Assert.True(result.Success, result.Message);
        Assert.Equal(ExtensionState.NeedsApproval, Assert.Single(manager.GetExtensions()).State);
    }

    [Fact]
    public void The_manifest_permissions_are_shown_for_the_review()
    {
        var (manager, _) = Create();

        manager.Install(Package("sample", Manifest(permissions: ["network", "files"])));

        Assert.Equal(["network", "files"], Assert.Single(manager.GetExtensions()).Permissions);
    }

    [Fact]
    public void An_extension_that_does_not_fit_this_host_is_installed_but_reported_incompatible()
    {
        var (manager, _) = Create();
        var manifest = Manifest().Replace(">=1.0.0 <2.0.0", ">=5.0.0");

        var result = manager.Install(Package("sample", manifest));

        Assert.True(result.Success);
        Assert.Contains("cannot run", result.Message);
        Assert.Equal(ExtensionState.Incompatible, Assert.Single(manager.GetExtensions()).State);
    }

    [Fact]
    public void Installing_over_a_folder_marked_for_removal_replaces_it()
    {
        var installer = new ExtensionInstaller(Root);
        var stale = Path.Combine(Root, TrustSampleConnector.Id, "1.0.0");
        Directory.CreateDirectory(stale);
        File.WriteAllText(Path.Combine(stale, ExtensionInstaller.RemoveMarker), string.Empty);

        var result = installer.Install(Package("one"), out var folder);

        Assert.True(result.Success, result.Message);
        Assert.Equal(stale, folder);
        Assert.False(File.Exists(Path.Combine(stale, ExtensionInstaller.RemoveMarker)));
    }

    [Fact]
    public void Installing_the_same_version_twice_fails()
    {
        var (manager, _) = Create();
        manager.Install(Package("one"));

        var result = manager.Install(Package("two"));

        Assert.False(result.Success);
        Assert.Contains("already installed", result.Message);
    }

    [Fact]
    public void A_package_without_a_manifest_is_rejected_and_leaves_nothing_behind()
    {
        var (manager, _) = Create();
        var empty = Path.Combine(Packages, "empty");
        Directory.CreateDirectory(empty);
        File.WriteAllText(Path.Combine(empty, "readme.txt"), "x");

        var result = manager.Install(empty);

        Assert.False(result.Success);
        Assert.Contains(ExtensionManifest.FileName, result.Message);
        Assert.Empty(Directory.EnumerateFileSystemEntries(Root));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    public void A_broken_manifest_is_rejected(string manifest)
    {
        var (manager, _) = Create();

        var result = manager.Install(Package("broken", manifest));

        Assert.False(result.Success);
        Assert.Empty(manager.GetExtensions());
    }

    [Fact]
    public void A_package_that_does_not_exist_is_rejected()
    {
        var (manager, _) = Create();

        Assert.False(manager.Install(Path.Combine(Packages, "missing.octaext")).Success);
    }

    [Fact]
    public void A_zip_with_a_path_that_leaves_the_folder_is_rejected()
    {
        var (manager, _) = Create();
        Directory.CreateDirectory(Packages);
        var zip = Path.Combine(Packages, "evil.octaext");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            using (var writer = new StreamWriter(archive.CreateEntry(ExtensionManifest.FileName).Open()))
            {
                writer.Write(Manifest());
            }

            using var evil = new StreamWriter(archive.CreateEntry("../evil.txt").Open());
            evil.Write("x");
        }

        var result = manager.Install(zip);

        Assert.False(result.Success);
        Assert.Contains("outside", result.Message);
        Assert.False(File.Exists(Path.Combine(Root, "evil.txt")));
        Assert.False(File.Exists(Path.Combine(_work, "evil.txt")));
    }

    [Fact]
    public void Installing_is_not_available_without_an_extensions_folder()
    {
        var manager = new ExtensionManager(new ConnectorRegistry(), new InMemoryStateStore());

        Assert.False(manager.Install(Package("sample")).Success);
    }

    [Fact]
    public void The_project_url_is_shown_before_and_after_the_extension_is_approved()
    {
        var (manager, _) = Create();
        manager.Install(Package("sample", Manifest(projectUrl: "https://example.com/trust-sample")));

        Assert.Equal("https://example.com/trust-sample", Assert.Single(manager.GetExtensions()).ProjectUrl);

        manager.Approve(TrustSampleConnector.Id);

        var info = Assert.Single(manager.GetExtensions());
        Assert.Equal(ExtensionState.Loaded, info.State);
        Assert.Equal("https://example.com/trust-sample", info.ProjectUrl);
    }

    [Fact]
    public void A_second_extension_with_the_same_id_does_not_replace_the_project_url_of_the_loaded_one()
    {
        var (manager, _) = Create();
        manager.Install(Package("sample", Manifest(projectUrl: "https://example.com/trust-sample")));
        manager.Approve(TrustSampleConnector.Id);

        var duplicate = ExtensionManifest.Parse(Manifest());
        manager.AddFolderExtensions([
            new ExtensionLoadResult(Path.Combine(Packages, "other"), duplicate, ExtensionState.Loaded, null, new TrustSampleConnector()),
        ]);

        var loaded = manager.GetExtensions().Single(e => e.State == ExtensionState.Loaded);
        Assert.Equal("https://example.com/trust-sample", loaded.ProjectUrl);
    }

    [Fact]
    public void A_project_url_that_is_not_a_web_address_is_not_shown()
    {
        var (manager, _) = Create();
        manager.Install(Package("sample", Manifest(projectUrl: "javascript:alert(1)")));

        Assert.Null(Assert.Single(manager.GetExtensions()).ProjectUrl);
    }

    // ---- Trust ----

    [Fact]
    public void Approving_loads_the_extension_and_remembers_the_trusted_version()
    {
        var (manager, store) = Create();
        manager.Install(Package("sample"));

        var result = manager.Approve(TrustSampleConnector.Id);

        Assert.True(result.Success, result.Message);
        Assert.True(CodeWasLoaded());
        Assert.True(manager.TryGetConnector(TrustSampleConnector.Id, out _));
        Assert.Equal(ExtensionState.Loaded, Assert.Single(manager.GetExtensions()).State);
        Assert.Equal("1.0.0", store.LoadTrusted()[TrustSampleConnector.Id]);
    }

    [Fact]
    public void A_trusted_extension_loads_at_the_next_start_without_asking_again()
    {
        var store = new InMemoryStateStore();
        var (first, _) = Create(store);
        first.Install(Package("sample"));
        first.Approve(TrustSampleConnector.Id);

        var (second, _) = Create(store);
        second.LoadInstalledExtensions();

        Assert.Equal(ExtensionState.Loaded, Assert.Single(second.GetExtensions()).State);
    }

    [Fact]
    public void An_extension_that_is_not_trusted_is_listed_at_start_and_its_code_stays_unloaded()
    {
        var (installer, _) = Create();
        installer.Install(Package("sample"));

        var (manager, _) = Create();
        manager.LoadInstalledExtensions();

        Assert.Equal(ExtensionState.NeedsApproval, Assert.Single(manager.GetExtensions()).State);
        Assert.False(manager.TryGetConnector(TrustSampleConnector.Id, out _));
    }

    [Fact]
    public void Trust_is_for_one_version_only()
    {
        var store = new InMemoryStateStore();
        store.SaveTrusted(new Dictionary<string, string> { [TrustSampleConnector.Id] = "1.0.0" });
        var (manager, _) = Create(store);

        Assert.True(manager.IsTrusted(ExtensionManifest.Parse(Manifest("1.0.0"))));
        Assert.False(manager.IsTrusted(ExtensionManifest.Parse(Manifest("1.1.0"))));
    }

    [Fact]
    public void Approving_something_that_is_not_waiting_fails()
    {
        var (manager, _) = Create();

        Assert.False(manager.Approve("acme.nothing").Success);
    }

    [Fact]
    public void An_approved_extension_that_cannot_load_stays_untrusted_and_reports_why()
    {
        var (manager, store) = Create();
        manager.Install(Package("sample", Manifest().Replace(typeof(TrustSampleConnector).FullName!, "Acme.Missing")));

        var result = manager.Approve(TrustSampleConnector.Id);

        Assert.False(result.Success);
        Assert.Contains("Acme.Missing", result.Message);
        Assert.Equal(ExtensionState.Failed, Assert.Single(manager.GetExtensions()).State);
        Assert.Empty(store.LoadTrusted());
    }

    // ---- Uninstall ----

    [Fact]
    public void Removing_an_extension_that_never_ran_deletes_its_files_and_trust()
    {
        var (manager, store) = Create();
        manager.Install(Package("sample"));

        var result = manager.Uninstall(TrustSampleConnector.Id);

        Assert.True(result.Success, result.Message);
        Assert.Empty(manager.GetExtensions());
        Assert.False(Directory.Exists(Path.Combine(Root, TrustSampleConnector.Id)));
        Assert.Empty(store.LoadTrusted());
    }

    [Fact]
    public void Removing_a_running_extension_turns_it_off_and_marks_its_files_for_deletion()
    {
        var (manager, store) = Create();
        manager.Install(Package("sample"));
        manager.Approve(TrustSampleConnector.Id);

        var result = manager.Uninstall(TrustSampleConnector.Id);

        Assert.True(result.Success, result.Message);
        Assert.True(manager.IsDisabled(TrustSampleConnector.Id));
        Assert.Empty(manager.EnabledDescriptors);
        Assert.False(manager.SetEnabled(TrustSampleConnector.Id, true));
        Assert.Empty(store.LoadTrusted());

        var info = Assert.Single(manager.GetExtensions());
        Assert.Equal(ExtensionState.Disabled, info.State);
        Assert.Contains("restart", info.StateMessage, StringComparison.OrdinalIgnoreCase);
        Assert.False(info.CanUninstall);
        Assert.True(File.Exists(Path.Combine(Root, TrustSampleConnector.Id, "1.0.0", ExtensionInstaller.RemoveMarker)));
    }

    [Fact]
    public void Folders_marked_for_removal_are_deleted_at_the_next_start_and_not_loaded()
    {
        var (manager, _) = Create();
        manager.Install(Package("sample"));
        var folder = Path.Combine(Root, TrustSampleConnector.Id, "1.0.0");
        File.WriteAllText(Path.Combine(folder, ExtensionInstaller.RemoveMarker), string.Empty);

        var (restarted, _) = Create();
        restarted.LoadInstalledExtensions();

        Assert.Empty(restarted.GetExtensions());
        Assert.False(Directory.Exists(folder));
        Assert.False(Directory.Exists(Path.Combine(Root, TrustSampleConnector.Id)));
    }

    [Fact]
    public void Built_in_extensions_cannot_be_removed()
    {
        var registry = new ConnectorRegistry().Add(new FakeConnector("acme.builtin", _ => new MinimalSession()));
        var (manager, _) = Create(registry: registry);

        var result = manager.Uninstall("acme.builtin");

        Assert.False(result.Success);
        Assert.Contains("Built-in", result.Message);
    }

    [Fact]
    public void Removing_something_that_is_not_installed_fails()
    {
        var (manager, _) = Create();

        Assert.False(manager.Uninstall("acme.nothing").Success);
    }

    [Fact]
    public void Staging_folders_are_not_mistaken_for_extensions()
    {
        Directory.CreateDirectory(Path.Combine(Root, ".staging-abc"));
        File.WriteAllText(Path.Combine(Root, ".staging-abc", ExtensionManifest.FileName), Manifest());
        var (manager, _) = Create();

        manager.LoadInstalledExtensions();

        Assert.Empty(manager.GetExtensions());
    }

    [Fact]
    public void The_state_file_keeps_disabled_and_trusted_extensions_apart()
    {
        var path = Path.Combine(_work, "state", "extensions.json");
        var store = new FileExtensionStateStore(path);

        store.SaveDisabled(["acme.off"]);
        store.SaveTrusted(new Dictionary<string, string> { ["acme.on"] = "1.2.3" });
        var reloaded = new FileExtensionStateStore(path);

        Assert.Equal(["acme.off"], reloaded.LoadDisabled());
        Assert.Equal("1.2.3", reloaded.LoadTrusted()["ACME.ON"]);
    }
}
