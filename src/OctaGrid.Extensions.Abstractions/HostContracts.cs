using Microsoft.Extensions.Logging;
using OctaGrid.Extensions.Models;

namespace OctaGrid.Extensions.Abstractions;

/// <summary>Receives the state a session reports. Implemented by the host.</summary>
public interface ITelemetrySink
{
    /// <summary>Publishes the printer's current state. May be called from any thread.</summary>
    void Send(PrinterTelemetry telemetry);
}

/// <summary>Services the host gives to a connector session.</summary>
public interface IConnectorContext
{
    /// <summary>Logger scoped to the connector and printer.</summary>
    ILogger Logger { get; }

    /// <summary>A folder the connector may use for its own cache and files. Created on first use.</summary>
    string DataDirectory { get; }
}
