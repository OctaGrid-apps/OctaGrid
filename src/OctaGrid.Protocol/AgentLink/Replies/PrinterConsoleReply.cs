using OctaGrid.Protocol.Messaging;

namespace OctaGrid.Protocol.AgentLink.Replies;

/// <summary>A slice of a printer's console log.</summary>
/// <param name="Page">The lines and where they sit in the log.</param>
public sealed record PrinterConsoleReply(ConsolePage Page) : AgentReply;
