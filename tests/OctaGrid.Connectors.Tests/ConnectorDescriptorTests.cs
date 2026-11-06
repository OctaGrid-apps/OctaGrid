using Microsoft.Extensions.Logging.Abstractions;
using OctaGrid.Connectors.BambuLab;
using OctaGrid.Connectors.Moonraker;
using OctaGrid.Extensions.Abstractions;
using OctaGrid.Protocol.Extensions;

namespace OctaGrid.Connectors.Tests;

/// <summary>The bundled connectors are held to the same rules as any third-party connector.</summary>
public class ConnectorDescriptorTests
{
    private sealed class TestContext : IConnectorContext
    {
        public Microsoft.Extensions.Logging.ILogger Logger => NullLogger.Instance;

        public string DataDirectory => Path.GetTempPath();
    }

    public static TheoryData<IConnector> Connectors => new()
    {
        new MoonrakerConnector(),
        new BambuLabConnector(),
    };

    [Fact]
    public void Ids_are_stable()
    {
        Assert.Equal("octagrid.moonraker", MoonrakerConnector.Id);
        Assert.Equal("octagrid.bambu-mqtt", BambuLabConnector.Id);
    }

    [Theory]
    [MemberData(nameof(Connectors))]
    public void Descriptor_is_well_formed(IConnector connector)
    {
        var descriptor = connector.Descriptor;

        Assert.True(ExtensionId.IsValid(descriptor.Id));
        Assert.Equal("OctaGrid", descriptor.Publisher);
        Assert.False(string.IsNullOrWhiteSpace(descriptor.DisplayName));
        Assert.False(string.IsNullOrWhiteSpace(descriptor.Version));
        Assert.Contains(descriptor.Settings, s => s.Key == "url" && s.Required);
        Assert.Equal(descriptor.Settings.Count, descriptor.Settings.Select(s => s.Key).Distinct().Count());
        Assert.All(descriptor.Settings, s => Assert.False(string.IsNullOrWhiteSpace(s.Label)));
    }

    [Theory]
    [MemberData(nameof(Connectors))]
    public async Task Declared_capabilities_match_what_the_session_implements(IConnector connector)
    {
        var profile = new PrinterProfile(
            Guid.NewGuid(), connector.Descriptor.Id, new Dictionary<string, string> { ["url"] = "127.0.0.1" });

        await using var session = connector.CreateSession(profile, new TestContext());

        Assert.Null(CapabilityInspector.FindMismatch(connector, session));
    }

    [Fact]
    public void The_api_key_is_a_secret_and_the_bambu_access_code_is_required()
    {
        var accessCode = new BambuLabConnector().Descriptor.Settings.Single(s => s.Key == "apiKey");
        var apiKey = new MoonrakerConnector().Descriptor.Settings.Single(s => s.Key == "apiKey");

        Assert.True(accessCode.Secret);
        Assert.True(accessCode.Required);
        Assert.True(apiKey.Secret);
        Assert.False(apiKey.Required);
    }

    [Fact]
    public void Bambu_serial_number_is_optional() =>
        Assert.False(new BambuLabConnector().Descriptor.Settings.Single(s => s.Key == "serialNumber").Required);
}
