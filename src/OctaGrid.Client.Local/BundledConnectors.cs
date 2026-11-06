using Microsoft.Extensions.Logging;
using OctaGrid.Agent.Core.Connectors;
using OctaGrid.Connectors.BambuLab;
using OctaGrid.Connectors.Moonraker;

namespace OctaGrid.Client.Local;

/// <summary>The connectors that ship inside OctaGrid.</summary>
internal static class BundledConnectors
{
    public static ConnectorRegistry CreateRegistry(ILoggerFactory? loggerFactory = null) =>
        new ConnectorRegistry()
            .Add(new MoonrakerConnector(loggerFactory))
            .Add(new BambuLabConnector(loggerFactory));
}
