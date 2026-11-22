using System.Text.Json;
using OctaGrid.Protocol.AgentLink;
using OctaGrid.Protocol.AgentLink.Notifications;

namespace OctaGrid.Protocol.Tests;

/// <summary>Which stream each agent notification travels on. Remote mode sends the two over different transports.</summary>
public sealed class AgentNotificationChannelTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Only_telemetry_travels_on_the_telemetry_channel()
    {
        foreach (var notification in AgentMessageSamples.Notifications)
        {
            var expected = notification is PrinterTelemetryReported ? AgentNotificationChannel.Telemetry : AgentNotificationChannel.Events;
            Assert.Equal(expected, notification.Channel);
        }
    }

    [Fact]
    public void The_channel_is_not_sent_because_it_follows_from_the_type()
    {
        foreach (var notification in AgentMessageSamples.Notifications)
        {
            var json = JsonSerializer.Serialize(notification, Json);

            Assert.DoesNotContain("\"channel\"", json, StringComparison.OrdinalIgnoreCase);
        }
    }
}
