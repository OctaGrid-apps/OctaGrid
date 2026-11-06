using OctaGrid.Agent.Core.Connectors;
using OctaGrid.Protocol.Extensions;

namespace OctaGrid.Agent.Core.Tests;

public class ExtensionManagerTests
{
    private static FakeConnector Connector(string id) => new(id, _ => new MinimalSession());

    private static (ExtensionManager Manager, InMemoryStateStore Store) Create(params string[] ids)
    {
        var registry = new ConnectorRegistry();
        foreach (var id in ids)
        {
            registry.Add(Connector(id));
        }

        var store = new InMemoryStateStore();
        return (new ExtensionManager(registry, store), store);
    }

    [Fact]
    public void Lists_extensions_by_name_with_their_details()
    {
        var (manager, _) = Create("acme.one");

        var info = Assert.Single(manager.GetExtensions());

        Assert.Equal("acme.one", info.Id);
        Assert.Equal("Fake", info.Name);
        Assert.Equal("1.0.0", info.Version);
        Assert.Equal("Tests", info.Publisher);
        Assert.Equal("connector", info.Kind);
        Assert.Equal(ExtensionState.Loaded, info.State);
        Assert.Null(info.StateMessage);
    }

    [Fact]
    public void Bundled_extensions_cannot_be_uninstalled()
    {
        var (manager, _) = Create("acme.one");

        var info = Assert.Single(manager.GetExtensions());

        Assert.Equal(ExtensionSource.Bundled, info.Source);
        Assert.False(info.CanUninstall);
    }

    [Fact]
    public void Extensions_from_other_sources_can_be_uninstalled()
    {
        var registry = new ConnectorRegistry().Add(Connector("acme.one"), ExtensionSource.UserFolder);
        var manager = new ExtensionManager(registry, new InMemoryStateStore());

        var info = Assert.Single(manager.GetExtensions());

        Assert.Equal(ExtensionSource.UserFolder, info.Source);
        Assert.True(info.CanUninstall);
    }

    [Fact]
    public void Disabling_marks_it_disabled_remembers_it_and_raises_changed()
    {
        var (manager, store) = Create("acme.one");
        var raised = 0;
        manager.Changed += () => raised++;

        var found = manager.SetEnabled("acme.one", false);

        Assert.True(found);
        Assert.True(manager.IsDisabled("acme.one"));
        Assert.Equal(ExtensionState.Disabled, Assert.Single(manager.GetExtensions()).State);
        Assert.Equal(["acme.one"], store.LoadDisabled());
        Assert.Equal(1, raised);
    }

    [Fact]
    public void Enabling_again_clears_the_choice()
    {
        var (manager, store) = Create("acme.one");
        manager.SetEnabled("acme.one", false);

        manager.SetEnabled("acme.one", true);

        Assert.False(manager.IsDisabled("acme.one"));
        Assert.Empty(store.LoadDisabled());
    }

    [Fact]
    public void Repeating_the_same_choice_saves_and_notifies_nothing()
    {
        var (manager, store) = Create("acme.one");
        var raised = 0;
        manager.Changed += () => raised++;

        manager.SetEnabled("acme.one", true);
        manager.SetEnabled("acme.one", false);
        manager.SetEnabled("acme.one", false);

        Assert.Equal(1, store.Saves);
        Assert.Equal(1, raised);
    }

    [Fact]
    public void Unknown_extensions_cannot_be_changed()
    {
        var (manager, store) = Create("acme.one");

        Assert.False(manager.SetEnabled("acme.missing", false));
        Assert.False(manager.IsDisabled("acme.missing"));
        Assert.Equal(0, store.Saves);
    }

    [Fact]
    public void The_id_is_matched_ignoring_case_but_stored_in_canonical_form()
    {
        var (manager, store) = Create("acme.one");

        manager.SetEnabled("ACME.ONE", false);

        Assert.Equal(["acme.one"], store.LoadDisabled());
    }

    [Fact]
    public void Enabled_descriptors_leave_out_disabled_connectors()
    {
        var (manager, _) = Create("acme.one", "acme.two");

        manager.SetEnabled("acme.one", false);

        Assert.Equal(["acme.two"], manager.EnabledDescriptors.Select(d => d.Id));
    }

    [Fact]
    public void A_saved_choice_is_applied_when_the_manager_starts()
    {
        var registry = new ConnectorRegistry().Add(Connector("acme.one"));
        var store = new InMemoryStateStore();
        store.SaveDisabled(["acme.one"]);

        var manager = new ExtensionManager(registry, store);

        Assert.True(manager.IsDisabled("acme.one"));
        Assert.Empty(manager.EnabledDescriptors);
    }

    [Fact]
    public void A_disabled_connector_can_still_be_found_for_management()
    {
        var (manager, _) = Create("acme.one");
        manager.SetEnabled("acme.one", false);

        Assert.True(manager.TryGetConnector("acme.one", out var connector));
        Assert.NotNull(connector);
    }
}
