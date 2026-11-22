using System.Text.Json.Serialization;

namespace OctaGrid.Protocol.Agents;

/// <summary>Whether the backend can reach an agent right now. Sent as its name. New values go at the end.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AgentConnectionState>))]
public enum AgentConnectionState
{
    /// <summary>The agent is not connected, so its printers cannot be reached.</summary>
    Offline,

    /// <summary>The agent is connected and its protocol version was accepted.</summary>
    Online,

    /// <summary>The agent connected, but it is too old for this backend and has to be updated first.</summary>
    AgentUpdateRequired,

    /// <summary>The agent connected, but it is newer than this backend understands; the backend has to be updated first.</summary>
    BackendUpdateRequired,
}
