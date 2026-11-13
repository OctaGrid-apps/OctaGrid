using CommunityToolkit.Mvvm.Messaging.Messages;
using OctaGrid.Extensions.Models;

namespace OctaGrid.Client.Services;

public class PrinterTelemetryData(PrinterTelemetry telemetryMessage) : ValueChangedMessage<PrinterTelemetry>(telemetryMessage)
{ }
