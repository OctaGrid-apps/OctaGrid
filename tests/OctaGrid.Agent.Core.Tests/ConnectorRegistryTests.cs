using OctaGrid.Agent.Core.Connectors;

namespace OctaGrid.Agent.Core.Tests;

public class ConnectorRegistryTests
{
    private static FakeConnector Connector(string id) => new(id, _ => new MinimalSession());

    [Fact]
    public void Finds_a_connector_by_id_ignoring_case()
    {
        var connector = Connector("acme.test");
        var registry = new ConnectorRegistry().Add(connector);

        Assert.True(registry.TryGet("acme.test", out var found));
        Assert.True(registry.TryGet("ACME.TEST", out var foundUpper));
        Assert.Same(connector, found);
        Assert.Same(connector, foundUpper);
    }

    [Fact]
    public void Unknown_ids_are_not_found()
    {
        var registry = new ConnectorRegistry().Add(Connector("acme.test"));

        Assert.False(registry.TryGet("other.thing", out var connector));
        Assert.Null(connector);
        Assert.False(registry.TryGet(null, out _));
    }

    [Fact]
    public void Rejects_a_duplicate_id()
    {
        var registry = new ConnectorRegistry().Add(Connector("acme.test"));

        Assert.Throws<ArgumentException>(() => registry.Add(Connector("ACME.test")));
        Assert.Single(registry.Connectors);
    }

    [Fact]
    public void Rejects_an_invalid_connector_id() =>
        Assert.Throws<ArgumentException>(() => new ConnectorRegistry().Add(Connector("Not Valid")));

    [Fact]
    public void Lists_descriptors_in_registration_order()
    {
        var registry = new ConnectorRegistry().Add(Connector("acme.one")).Add(Connector("acme.two"));

        Assert.Equal(["acme.one", "acme.two"], registry.Descriptors.Select(d => d.Id));
    }
}
