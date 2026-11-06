using Microsoft.Extensions.Logging;
using OctaGrid.Extensions.Abstractions;

namespace OctaGrid.Agent.Core.Connectors;

/// <summary>The host services handed to a connector session.</summary>
public sealed class ConnectorContext(ILogger logger, string dataDirectory) : IConnectorContext
{
    private bool _created;

    /// <summary>Default parent folder for connector data: <c>%LocalAppData%/OctaGrid/connectors</c>.</summary>
    public static string DefaultRoot { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OctaGrid", "connectors");

    public ILogger Logger { get; } = logger;

    /// <summary>The connector's private folder. It is created the first time this property is read.</summary>
    public string DataDirectory
    {
        get
        {
            if (!_created)
            {
                Directory.CreateDirectory(dataDirectory);
                _created = true;
            }

            return dataDirectory;
        }
    }
}
