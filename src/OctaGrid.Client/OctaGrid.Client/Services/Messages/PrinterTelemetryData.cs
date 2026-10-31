using CommunityToolkit.Mvvm.Messaging.Messages;
using OctaGrid.Protocol.Messaging;

namespace OctaGrid.Client.Services;

public class PrinterTelemetryData(PrinterTelemetry telemetryMessage) : ValueChangedMessage<PrinterTelemetry>(telemetryMessage)
{ }
