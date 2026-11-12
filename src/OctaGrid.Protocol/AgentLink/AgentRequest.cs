using System.Text.Json.Serialization;
using OctaGrid.Protocol.AgentLink.Requests;

namespace OctaGrid.Protocol.AgentLink;

/// <summary>
/// Something the backend asks an agent to do. Every request has exactly one reply type; see
/// <see cref="AgentRequest{TReply}"/>. The names below are the wire names: never change or reuse one.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(StartPrintRequest), "print.start")]
[JsonDerivedType(typeof(PausePrintRequest), "print.pause")]
[JsonDerivedType(typeof(ResumePrintRequest), "print.resume")]
[JsonDerivedType(typeof(StopPrintRequest), "print.stop")]
[JsonDerivedType(typeof(ListPrinterFilesRequest), "files.list")]
[JsonDerivedType(typeof(GetPrintThumbnailRequest), "files.thumbnail")]
[JsonDerivedType(typeof(DeletePrinterFileRequest), "files.delete")]
[JsonDerivedType(typeof(StorePrinterFileRequest), "files.store")]
[JsonDerivedType(typeof(GetPrinterConsoleRequest), "console.get")]
[JsonDerivedType(typeof(SendConsoleCommandRequest), "console.send")]
[JsonDerivedType(typeof(GetPrinterEventsRequest), "events.get")]
[JsonDerivedType(typeof(RequestPrinterStateRequest), "state.request")]
[JsonDerivedType(typeof(ApplyPrinterProfilesRequest), "profiles.apply")]
[JsonDerivedType(typeof(TestConnectionRequest), "connection.test")]
[JsonDerivedType(typeof(GetConnectorsRequest), "connectors.get")]
[JsonDerivedType(typeof(GetExtensionsRequest), "extensions.get")]
[JsonDerivedType(typeof(SetExtensionEnabledRequest), "extensions.set-enabled")]
[JsonDerivedType(typeof(InstallExtensionRequest), "extensions.install")]
[JsonDerivedType(typeof(ApproveExtensionRequest), "extensions.approve")]
[JsonDerivedType(typeof(UninstallExtensionRequest), "extensions.uninstall")]
[JsonDerivedType(typeof(GetDeveloperFolderRequest), "extensions.dev-folder.get")]
[JsonDerivedType(typeof(SetDeveloperFolderRequest), "extensions.dev-folder.set")]
[JsonDerivedType(typeof(ReloadDeveloperExtensionsRequest), "extensions.dev-folder.reload")]
[JsonDerivedType(typeof(BeginUploadRequest), "upload.begin")]
[JsonDerivedType(typeof(UploadChunkRequest), "upload.chunk")]
[JsonDerivedType(typeof(AbortUploadRequest), "upload.abort")]
public abstract record AgentRequest;
