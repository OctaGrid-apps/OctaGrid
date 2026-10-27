namespace OctaGrid.Client.Contracts;

/// <summary>
/// Whatever a client mode needs running in the background for the app's lifetime. Each mode
/// registers its own: Local runs the in-process agent, Remote will hold the server connection
/// (SignalR) that feeds telemetry and entity changes into IClientMessageHandler.
/// </summary>
public interface IClientRuntime
{
    /// <summary>Runs until <paramref name="cancellationToken"/> is cancelled.</summary>
    Task StartAsync(CancellationToken cancellationToken);
}
