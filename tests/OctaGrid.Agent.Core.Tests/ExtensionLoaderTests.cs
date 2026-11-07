using System.Runtime.Loader;
using OctaGrid.Agent.Core.Connectors;
using OctaGrid.Extensions.Abstractions;
using OctaGrid.Protocol.Extensions;

namespace OctaGrid.Agent.Core.Tests;

/// <summary>The connector the loader tests load from a copy of this test assembly, the way a real extension is loaded.</summary>
public sealed class SampleFolderConnector : IConnector
{
    public const string Id = "acme.folder-sample";

    public ConnectorDescriptor Descriptor { get; } = new(Id, "Folder Sample", "2.0.0", "Acme", ConnectorCapabilities.None, [], []);

    public IPrinterSession CreateSession(PrinterProfile profile, IConnectorContext context) => new MinimalSession();
}

/// <summary>A connector that cannot be created, to check that one broken extension does not stop the others.</summary>
public sealed class ThrowingFolderConnector : IConnector
{
    public ThrowingFolderConnector() => throw new InvalidOperationException("boom");

    public ConnectorDescriptor Descriptor => throw new NotSupportedException();

    public IPrinterSession CreateSession(PrinterProfile profile, IConnectorContext context) => throw new NotSupportedException();
}

public sealed class ExtensionLoaderTests : IDisposable
{
    private static readonly ExtensionVersion Host = ExtensionVersion.Parse("1.2.0");

    private readonly string _root = Path.Combine(Path.GetTempPath(), "octagrid-ext-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Loaded assemblies stay locked on Windows until the process ends.
        }
    }

    private string Install(string folderName, string manifest, bool copyAssembly = true)
    {
        var folder = Path.Combine(_root, folderName);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, ExtensionManifest.FileName), manifest);
        if (copyAssembly)
        {
            var source = typeof(ExtensionLoaderTests).Assembly.Location;
            File.Copy(source, Path.Combine(folder, Path.GetFileName(source)), overwrite: true);
        }

        return folder;
    }

    private static string Manifest(
        string id = SampleFolderConnector.Id,
        string host = ">=1.0.0 <2.0.0",
        string? entryType = null,
        string? assembly = null) => $$"""
        {
          "id": "{{id}}",
          "name": "Folder Sample",
          "version": "2.0.0",
          "publisher": "Acme",
          "kind": "connector",
          "host": "{{host}}",
          "assembly": "{{assembly ?? Path.GetFileName(typeof(ExtensionLoaderTests).Assembly.Location)}}",
          "entryType": "{{entryType ?? typeof(SampleFolderConnector).FullName}}"
        }
        """;

    private static ExtensionLoader Loader() => new(Host);

    [Fact]
    public void A_missing_folder_has_no_extensions() =>
        Assert.Empty(Loader().LoadAll(Path.Combine(_root, "nothing-here")));

    [Fact]
    public void Loads_a_connector_from_a_folder_into_its_own_load_context()
    {
        Install("sample", Manifest());

        var result = Assert.Single(Loader().LoadAll(_root));

        Assert.Equal(ExtensionState.Loaded, result.State);
        Assert.Equal(SampleFolderConnector.Id, result.Connector!.Descriptor.Id);
        Assert.NotSame(AssemblyLoadContext.Default, AssemblyLoadContext.GetLoadContext(result.Connector.GetType().Assembly));
        Assert.NotSame(typeof(SampleFolderConnector), result.Connector.GetType());
    }

    [Fact]
    public void The_loaded_connector_shares_the_sdk_types_with_the_host()
    {
        Install("sample", Manifest());

        var connector = Loader().LoadAll(_root).Single().Connector!;

        // It is an IConnector of the host's SDK assembly, so the host can call it without reflection.
        Assert.IsAssignableFrom<IConnector>(connector);
        Assert.NotNull(connector.CreateSession(new PrinterProfile(Guid.NewGuid(), SampleFolderConnector.Id,
            new Dictionary<string, string>()), null!));
    }

    [Fact]
    public void Loads_only_the_highest_version_of_an_installed_package()
    {
        Install(Path.Combine("acme.folder-sample", "1.9.0"), Manifest());
        var newer = Install(Path.Combine("acme.folder-sample", "1.10.0"), Manifest());

        var result = Assert.Single(Loader().LoadAll(_root));

        Assert.Equal(newer, result.Folder);
    }

    [Fact]
    public void An_extension_that_needs_another_host_version_is_incompatible()
    {
        Install("sample", Manifest(host: ">=2.0.0"));

        var result = Assert.Single(Loader().LoadAll(_root));

        Assert.Equal(ExtensionState.Incompatible, result.State);
        Assert.Contains("this is OctaGrid 1.2.0", result.Message);
        Assert.Null(result.Connector);
    }

    [Fact]
    public void An_extension_built_for_an_earlier_pre_release_runs_on_a_later_one()
    {
        Install("sample", Manifest(host: ">=1.0.19-pre-alpha <2.0.0"));

        var result = Assert.Single(new ExtensionLoader(ExtensionVersion.Parse("1.0.20-pre-alpha.1")).LoadAll(_root));

        Assert.Equal(ExtensionState.Loaded, result.State);
    }

    [Fact]
    public void A_pre_release_host_refuses_a_range_without_a_pre_release_and_says_what_to_write()
    {
        Install("sample", Manifest(host: ">=1.0.0 <2.0.0"));

        var result = Assert.Single(new ExtensionLoader(ExtensionVersion.Parse("1.0.20-pre-alpha.1")).LoadAll(_root));

        Assert.Equal(ExtensionState.Incompatible, result.State);
        Assert.Contains("this is OctaGrid 1.0.20-pre-alpha.1", result.Message);
        Assert.Contains("must name a pre-release", result.Message);
        Assert.Contains(">=1.0.20-pre-alpha", result.Message);
    }

    [Fact]
    public void The_pre_release_hint_is_left_out_when_it_would_not_help()
    {
        Install("sample", Manifest(host: ">=5.0.0"));

        var result = Assert.Single(Loader().LoadAll(_root));

        Assert.DoesNotContain("pre-release", result.Message);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    public void A_broken_manifest_fails_without_throwing(string manifest)
    {
        Install("broken", manifest);

        var result = Assert.Single(Loader().LoadAll(_root));

        Assert.Equal(ExtensionState.Failed, result.State);
        Assert.NotNull(result.Message);
    }

    [Fact]
    public void A_folder_without_a_manifest_is_ignored()
    {
        Directory.CreateDirectory(Path.Combine(_root, "empty"));

        Assert.Empty(Loader().LoadAll(_root));
    }

    [Fact]
    public void Fails_when_the_assembly_is_missing()
    {
        Install("sample", Manifest(assembly: "Missing.dll"), copyAssembly: false);

        var result = Assert.Single(Loader().LoadAll(_root));

        Assert.Equal(ExtensionState.Failed, result.State);
        Assert.Contains("Missing.dll", result.Message);
    }

    [Fact]
    public void Fails_when_the_entry_type_is_not_in_the_assembly()
    {
        Install("sample", Manifest(entryType: "Acme.Nope"));

        var result = Assert.Single(Loader().LoadAll(_root));

        Assert.Equal(ExtensionState.Failed, result.State);
        Assert.Contains("Acme.Nope", result.Message);
    }

    [Fact]
    public void Fails_when_the_entry_type_is_not_a_connector()
    {
        Install("sample", Manifest(entryType: typeof(ExtensionLoaderTests).FullName));

        var result = Assert.Single(Loader().LoadAll(_root));

        Assert.Equal(ExtensionState.Failed, result.State);
        Assert.Contains("not a connector", result.Message);
    }

    [Fact]
    public void Fails_when_the_manifest_id_differs_from_the_connector_id()
    {
        Install("sample", Manifest(id: "acme.other"));

        var result = Assert.Single(Loader().LoadAll(_root));

        Assert.Equal(ExtensionState.Failed, result.State);
        Assert.Contains("differs", result.Message);
    }

    [Fact]
    public void A_connector_that_throws_fails_but_the_others_still_load()
    {
        Install("a-broken", Manifest(id: "acme.throwing", entryType: typeof(ThrowingFolderConnector).FullName));
        Install("b-good", Manifest());

        var results = Loader().LoadAll(_root);

        Assert.Equal(2, results.Count);
        Assert.Contains("boom", results.Single(r => r.State == ExtensionState.Failed).Message);
        Assert.Single(results, r => r.State == ExtensionState.Loaded);
    }

    [Fact]
    public void Rejects_an_assembly_path_that_leaves_the_folder()
    {
        Install("sample", Manifest(assembly: "../outside.dll"));

        var result = Assert.Single(Loader().LoadAll(_root));

        Assert.Equal(ExtensionState.Failed, result.State);
    }

    [Fact]
    public void The_manager_registers_loaded_extensions_and_lists_the_others_with_their_reason()
    {
        Install("good", Manifest());
        Install("old", Manifest(id: "acme.old", host: ">=3.0.0"));
        var manager = new ExtensionManager(new ConnectorRegistry(), new InMemoryStateStore());

        manager.AddFolderExtensions(Loader().LoadAll(_root));

        var extensions = manager.GetExtensions();
        var loaded = Assert.Single(extensions, e => e.State == ExtensionState.Loaded);
        Assert.Equal(ExtensionSource.UserFolder, loaded.Source);
        Assert.True(loaded.CanUninstall);
        Assert.True(manager.TryGetConnector(SampleFolderConnector.Id, out _));

        var incompatible = Assert.Single(extensions, e => e.State == ExtensionState.Incompatible);
        Assert.Equal("acme.old", incompatible.Id);
        Assert.NotNull(incompatible.StateMessage);
        Assert.False(manager.TryGetConnector("acme.old", out _));
    }

    [Fact]
    public void An_extension_with_the_id_of_a_bundled_connector_fails_instead_of_replacing_it()
    {
        Install("sample", Manifest());
        var registry = new ConnectorRegistry().Add(new FakeConnector(SampleFolderConnector.Id, _ => new MinimalSession()));
        var manager = new ExtensionManager(registry, new InMemoryStateStore());

        manager.AddFolderExtensions(Loader().LoadAll(_root));

        var failed = Assert.Single(manager.GetExtensions(), e => e.State == ExtensionState.Failed);
        Assert.Contains("already registered", failed.StateMessage);
        Assert.Equal(ExtensionSource.Bundled, registry.GetSource(SampleFolderConnector.Id));
    }
}
