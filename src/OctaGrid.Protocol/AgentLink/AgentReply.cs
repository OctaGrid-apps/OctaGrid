using System.Text.Json.Serialization;
using OctaGrid.Protocol.AgentLink.Replies;

namespace OctaGrid.Protocol.AgentLink;

/// <summary>
/// What an agent answers to an <see cref="AgentRequest"/> that succeeded. A request that failed is answered
/// with an <see cref="AgentError"/> instead. The names below are the wire names: never change or reuse one.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(AgentAck), "ack")]
[JsonDerivedType(typeof(PrinterFilesReply), "files")]
[JsonDerivedType(typeof(PrintThumbnailReply), "thumbnail")]
[JsonDerivedType(typeof(PrinterConsoleReply), "console")]
[JsonDerivedType(typeof(PrinterEventsReply), "events")]
[JsonDerivedType(typeof(ConnectionTestReply), "connection-test")]
[JsonDerivedType(typeof(ConnectorsReply), "connectors")]
[JsonDerivedType(typeof(ExtensionsReply), "extensions")]
[JsonDerivedType(typeof(ExtensionChangeReply), "extension-change")]
[JsonDerivedType(typeof(DeveloperFolderReply), "dev-folder")]
[JsonDerivedType(typeof(UploadStartedReply), "upload-started")]
public abstract record AgentReply;
