using System.Globalization;
using Avalonia.Data.Converters;
using OctaGrid.Protocol.Agents;

namespace OctaGrid.Client.Converters;

/// <summary>Says in words whether an agent can be reached, and what to update when it cannot.</summary>
public sealed class AgentConnectionTextConverter : IValueConverter
{
    public static readonly AgentConnectionTextConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        AgentConnectionState.Online => "Online",
        AgentConnectionState.Offline => "Offline",
        AgentConnectionState.AgentUpdateRequired => "Update the agent",
        AgentConnectionState.BackendUpdateRequired => "Update the app",
        _ => value,
    };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
