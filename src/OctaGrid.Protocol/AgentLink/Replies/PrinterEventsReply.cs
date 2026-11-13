using OctaGrid.Protocol.Messaging;

namespace OctaGrid.Protocol.AgentLink.Replies;

/// <summary>A slice of a printer's event history.</summary>
/// <param name="Page">The events and where they sit in the history.</param>
public sealed record PrinterEventsReply(PrinterEventPage Page) : AgentReply;
