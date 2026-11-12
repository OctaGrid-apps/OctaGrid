using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using OctaGrid.Protocol.AgentLink;

namespace OctaGrid.Protocol.Tests;

/// <summary>
/// The agent messages are a wire contract between parts that are updated separately. These tests hold the
/// contract still: every message has a wire name, the names do not change, and every message survives JSON.
/// </summary>
public sealed class AgentMessageSetTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly Guid AgentId = Guid.Parse("00000000-0000-0000-0000-00000000aaaa");
    private static readonly Guid CorrelationId = Guid.Parse("00000000-0000-0000-0000-00000000cccc");

    private static IReadOnlyList<Type> ConcreteTypes(Type baseType) =>
        baseType.Assembly.GetTypes()
            .Where(t => t is { IsAbstract: false, IsClass: true } && baseType.IsAssignableFrom(t))
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .ToList();

    private static IReadOnlyDictionary<Type, string> WireNames(Type baseType) =>
        baseType.GetCustomAttributes<JsonDerivedTypeAttribute>()
            .ToDictionary(a => a.DerivedType, a => (string)a.TypeDiscriminator!);

    public static TheoryData<Type> MessageBases() => [typeof(AgentRequest), typeof(AgentReply), typeof(AgentNotification)];

    [Theory]
    [MemberData(nameof(MessageBases))]
    public void Every_message_type_has_a_wire_name(Type baseType)
    {
        var named = WireNames(baseType).Keys.OrderBy(t => t.Name, StringComparer.Ordinal);

        Assert.Equal(ConcreteTypes(baseType), named);
    }

    [Theory]
    [MemberData(nameof(MessageBases))]
    public void Wire_names_are_unique(Type baseType)
    {
        var names = WireNames(baseType).Values.ToList();

        Assert.Equal(names.Count, names.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void The_wire_names_of_requests_do_not_change()
    {
        string[] expected =
        [
            "connection.test", "connectors.get", "console.get", "console.send", "events.get",
            "extensions.approve", "extensions.dev-folder.get", "extensions.dev-folder.reload", "extensions.dev-folder.set",
            "extensions.get", "extensions.install", "extensions.set-enabled", "extensions.uninstall",
            "files.delete", "files.list", "files.store", "files.thumbnail",
            "print.pause", "print.resume", "print.start", "print.stop",
            "profiles.apply", "state.request", "upload.abort", "upload.begin", "upload.chunk",
        ];

        Assert.Equal(expected, WireNames(typeof(AgentRequest)).Values.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void The_wire_names_of_replies_do_not_change()
    {
        string[] expected =
        [
            "ack", "connection-test", "connectors", "console", "dev-folder", "events",
            "extension-change", "extensions", "files", "thumbnail", "upload-started",
        ];

        Assert.Equal(expected, WireNames(typeof(AgentReply)).Values.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void The_wire_names_of_notifications_do_not_change()
    {
        string[] expected = ["connectors-changed", "console-line", "printer-event", "telemetry"];

        Assert.Equal(expected, WireNames(typeof(AgentNotification)).Values.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Every_request_names_a_concrete_reply()
    {
        foreach (var request in ConcreteTypes(typeof(AgentRequest)))
        {
            var typed = request.BaseType;
            Assert.True(
                typed is { IsGenericType: true } && typed.GetGenericTypeDefinition() == typeof(AgentRequest<>),
                $"{request.Name} must derive from AgentRequest<TReply>.");
            Assert.False(typed!.GetGenericArguments()[0].IsAbstract, $"{request.Name} must name a concrete reply.");
        }
    }

    [Fact]
    public void There_is_a_sample_of_every_message()
    {
        Assert.Equal(ConcreteTypes(typeof(AgentRequest)), SampleTypes(AgentMessageSamples.Requests));
        Assert.Equal(ConcreteTypes(typeof(AgentReply)), SampleTypes(AgentMessageSamples.Replies));
        Assert.Equal(ConcreteTypes(typeof(AgentNotification)), SampleTypes(AgentMessageSamples.Notifications));

        static IEnumerable<Type> SampleTypes(IEnumerable<object> samples) =>
            samples.Select(s => s.GetType()).OrderBy(t => t.Name, StringComparer.Ordinal);
    }

    [Fact]
    public void Every_request_survives_json_inside_its_envelope()
    {
        foreach (var request in AgentMessageSamples.Requests)
        {
            var envelope = new AgentRequestEnvelope(AgentId, CorrelationId, request);

            var json = JsonSerializer.Serialize(envelope, Json);
            var back = JsonSerializer.Deserialize<AgentRequestEnvelope>(json, Json)!;

            Assert.Equal(AgentId, back.AgentId);
            Assert.Equal(CorrelationId, back.CorrelationId);
            Assert.IsType(request.GetType(), back.Request);
            // Records with collections do not compare by content, so the JSON is compared instead.
            Assert.Equal(json, JsonSerializer.Serialize(back, Json));
        }
    }

    [Fact]
    public void Every_reply_survives_json_inside_its_envelope()
    {
        foreach (var reply in AgentMessageSamples.Replies)
        {
            var envelope = new AgentReplyEnvelope(CorrelationId, reply);

            var json = JsonSerializer.Serialize(envelope, Json);
            var back = JsonSerializer.Deserialize<AgentReplyEnvelope>(json, Json)!;

            Assert.True(back.Succeeded);
            Assert.IsType(reply.GetType(), back.Reply);
            Assert.Equal(json, JsonSerializer.Serialize(back, Json));
        }
    }

    [Fact]
    public void Every_notification_survives_json_inside_its_envelope()
    {
        foreach (var notification in AgentMessageSamples.Notifications)
        {
            var envelope = new AgentNotificationEnvelope(AgentId, notification);

            var json = JsonSerializer.Serialize(envelope, Json);
            var back = JsonSerializer.Deserialize<AgentNotificationEnvelope>(json, Json)!;

            Assert.Equal(AgentId, back.AgentId);
            Assert.IsType(notification.GetType(), back.Notification);
            Assert.Equal(json, JsonSerializer.Serialize(back, Json));
        }
    }

    [Fact]
    public void A_failed_request_is_answered_with_an_error_and_no_reply()
    {
        var envelope = new AgentReplyEnvelope(
            CorrelationId,
            Reply: null,
            new AgentError("/errors/validation", "Validation Error", "Unknown command.", new Dictionary<string, string> { ["command"] = "Unknown command." }));

        var back = JsonSerializer.Deserialize<AgentReplyEnvelope>(JsonSerializer.Serialize(envelope, Json), Json)!;

        Assert.False(back.Succeeded);
        Assert.Null(back.Reply);
        Assert.Equal("/errors/validation", back.Error!.TypeUri);
        Assert.Equal("Unknown command.", back.Error.Errors!["command"]);
    }

    [Fact]
    public void A_message_with_an_unknown_wire_name_is_rejected_not_guessed()
    {
        var json = "{\"agentId\":\"" + AgentId + "\",\"correlationId\":\"" + CorrelationId + "\",\"request\":{\"$type\":\"print.teleport\"}}";

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<AgentRequestEnvelope>(json, Json));
    }

    [Fact]
    public void The_handshake_survives_json()
    {
        var hello = new AgentHello(AgentId, "1.0.24-pre-alpha", AgentProtocol.Version);
        var result = new AgentHelloResult(AgentHelloStatus.AgentUpdateRequired, AgentProtocol.Version, "Update the agent.");

        Assert.Equal(hello, JsonSerializer.Deserialize<AgentHello>(JsonSerializer.Serialize(hello, Json), Json));
        Assert.Equal(result, JsonSerializer.Deserialize<AgentHelloResult>(JsonSerializer.Serialize(result, Json), Json));
    }
}
