using Microsoft.Extensions.Logging;
using OctaGrid.Extensions.Abstractions;
using OctaGrid.Protocol.Agents;
using OctaGrid.Protocol.Extensions;
using OctaGrid.Protocol.Messaging;
using OctaGrid.Protocol.Printers;

namespace OctaGrid.Extensions.Sample;

/// <summary>
/// A sample connector for trying the extension system. It needs no printer: every printer you add with it is
/// simulated. It heats up, "prints" a file, can be paused, resumed and stopped, keeps a few fake files and has one
/// custom command. It is also a small, complete example of what a connector looks like.
/// <para>
/// It shows how a connector reports problems: near the end of a print it publishes a "filament is running low"
/// condition, and leaves it out of later snapshots once the print is over. A connector never writes events itself.
/// The host compares snapshots and records an event when the state changes or a condition appears or goes away, so
/// the printer's history fills up without the connector keeping a log.
/// </para>
/// </summary>
public sealed class DemoPrinterConnector : IConnector
{
    /// <summary>The id from <c>octagrid-extension.json</c>; the two must be the same.</summary>
    public const string Id = "octagrid-samples.demo-printer";

    public ConnectorDescriptor Descriptor { get; } = new(
        Id,
        "Demo printer (simulated)",
        "1.0.0",
        "OctaGrid Samples",
        ConnectorCapabilities.PrintControl | ConnectorCapabilities.Files | ConnectorCapabilities.CustomCommands,
        [
            new SettingField(
                DemoSession.SpeedKey, "Simulation speed", SettingFieldType.Choice,
                Required: false, DefaultValue: DemoSession.NormalSpeed,
                Help: "Fast makes a print take seconds instead of minutes.",
                Choices: [DemoSession.NormalSpeed, DemoSession.FastSpeed]),
        ],
        [
            new CustomCommandDescriptor(
                DemoSession.HelloCommand, "Say hello", "Answers with a message, to show that custom commands work.",
                CustomCommandRisk.Low, []),
        ]);

    public IPrinterSession CreateSession(PrinterProfile profile, IConnectorContext context) =>
        new DemoSession(profile, context);
}

/// <summary>The simulated printer behind <see cref="DemoPrinterConnector"/>.</summary>
public sealed class DemoSession : IPrinterSession, IPrintControl, IFileStorage, ICustomCommands
{
    public const string SpeedKey = "speed";
    public const string NormalSpeed = "Normal";
    public const string FastSpeed = "Fast";
    public const string HelloCommand = "hello";

    /// <summary>The code of the condition published near the end of a print. Stays the same while the problem lasts.</summary>
    public const string FilamentLowCode = "filament.low";

    /// <summary>What the "filament is running low" condition tells the user.</summary>
    public const string FilamentLowMessage = "Filament is running low.";

    private const double Ambient = 24;
    private const double NozzleTarget = 210;
    private const double BedTarget = 60;

    private readonly Lock _gate = new();
    private readonly PrinterProfile _profile;
    private readonly IConnectorContext _context;
    private readonly TimeSpan _tick;
    private readonly int _heatingTicks;
    private readonly int _printingTicks;
    private readonly List<PrintMetadata> _files =
    [
        new("benchy.gcode", 1_200_000, "PLA", 0.4, 5400),
        new("calibration-cube.gcode", 300_000, "PLA", 0.4, 1200),
    ];

    private ITelemetrySink? _sink;
    private UniversalPrinterState _state = UniversalPrinterState.Idle;
    private string? _fileName;
    private int _ticksInState;
    private int _printTicks;
    private double _nozzle = Ambient;
    private double _bed = Ambient;

    public DemoSession(PrinterProfile profile, IConnectorContext context)
    {
        _profile = profile;
        _context = context;

        var fast = string.Equals(profile.Get(SpeedKey), FastSpeed, StringComparison.OrdinalIgnoreCase);
        _tick = fast ? TimeSpan.FromMilliseconds(50) : TimeSpan.FromSeconds(1);
        _heatingTicks = 10;
        _printingTicks = fast ? 40 : 120;
    }

    public async Task RunAsync(ITelemetrySink telemetry, CancellationToken cancellationToken)
    {
        _sink = telemetry;
        _context.Logger.LogInformation("Demo printer {PrinterId} started", _profile.PrinterId);

        using var timer = new PeriodicTimer(_tick);
        Publish();
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                Advance();
                Publish();
            }
        }
        catch (OperationCanceledException)
        {
            // The host stopped the session.
        }
    }

    public Task<ConnectionTestResult> TestConnectionAsync(CancellationToken cancellationToken) =>
        Task.FromResult(ConnectionTestResult.Ok());

    public Task RequestStateAsync(CancellationToken cancellationToken)
    {
        Publish();
        return Task.CompletedTask;
    }

    // ---- Print control ----

    public Task StartPrintAsync(string fileName, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_state is UniversalPrinterState.Preparing or UniversalPrinterState.Printing or UniversalPrinterState.Paused)
            {
                throw new InvalidOperationException("The printer is already printing.");
            }

            _fileName = fileName;
            _printTicks = 0;
            Enter(UniversalPrinterState.Preparing);
        }

        Publish();
        return Task.CompletedTask;
    }

    public Task PausePrintAsync(CancellationToken cancellationToken)
    {
        Change(UniversalPrinterState.Printing, UniversalPrinterState.Paused);
        return Task.CompletedTask;
    }

    public Task ResumePrintAsync(CancellationToken cancellationToken)
    {
        Change(UniversalPrinterState.Paused, UniversalPrinterState.Printing);
        return Task.CompletedTask;
    }

    public Task StopPrintAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_state is UniversalPrinterState.Preparing or UniversalPrinterState.Printing or UniversalPrinterState.Paused)
            {
                Enter(UniversalPrinterState.Cancelled);
            }
        }

        Publish();
        return Task.CompletedTask;
    }

    // ---- Files ----

    public Task<IReadOnlyList<PrintMetadata>> ListFilesAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<PrintMetadata>>(_files.ToList());
        }
    }

    public async Task UploadFileAsync(Stream content, string fileName, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);

        lock (_gate)
        {
            _files.RemoveAll(f => f.FileName == fileName);
            _files.Add(new PrintMetadata(fileName, (int)buffer.Length, "PLA", 0.4, 600));
        }
    }

    public Task DeleteFileAsync(string fileName, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _files.RemoveAll(f => f.FileName == fileName);
        }

        return Task.CompletedTask;
    }

    // ---- Custom commands ----

    public Task<CommandResult> ExecuteAsync(
        string commandId,
        IReadOnlyDictionary<string, string> arguments,
        CancellationToken cancellationToken) =>
        Task.FromResult(commandId == HelloCommand
            ? CommandResult.Ok("Hello from the sample extension!")
            : CommandResult.Fail($"Unknown command '{commandId}'."));

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    // ---- Simulation ----

    private void Change(UniversalPrinterState from, UniversalPrinterState to)
    {
        lock (_gate)
        {
            if (_state != from)
            {
                return;
            }

            Enter(to);
        }

        Publish();
    }

    // Callers hold the lock.
    private void Enter(UniversalPrinterState state)
    {
        _state = state;
        _ticksInState = 0;
    }

    private void Advance()
    {
        lock (_gate)
        {
            _ticksInState++;

            switch (_state)
            {
                case UniversalPrinterState.Preparing:
                    _nozzle = Approach(_nozzle, NozzleTarget, (NozzleTarget - Ambient) / _heatingTicks);
                    _bed = Approach(_bed, BedTarget, (BedTarget - Ambient) / _heatingTicks);
                    if (_ticksInState >= _heatingTicks)
                    {
                        Enter(UniversalPrinterState.Printing);
                    }

                    break;

                case UniversalPrinterState.Printing:
                    _printTicks++;
                    if (_printTicks >= _printingTicks)
                    {
                        Enter(UniversalPrinterState.Completed);
                    }

                    break;

                case UniversalPrinterState.Completed:
                case UniversalPrinterState.Cancelled:
                    // Cool down and show the result for a moment before going back to idle.
                    CoolDown();
                    if (_ticksInState >= 5)
                    {
                        _fileName = null;
                        Enter(UniversalPrinterState.Idle);
                    }

                    break;

                case UniversalPrinterState.Idle:
                    CoolDown();
                    break;
            }
        }
    }

    private void CoolDown()
    {
        _nozzle = Approach(_nozzle, Ambient, 20);
        _bed = Approach(_bed, Ambient, 5);
    }

    private static double Approach(double value, double target, double step) =>
        value < target ? Math.Min(target, value + step) : Math.Max(target, value - step);

    private void Publish()
    {
        PrinterTelemetry telemetry;
        lock (_gate)
        {
            var heating = _state is UniversalPrinterState.Preparing or UniversalPrinterState.Printing or UniversalPrinterState.Paused;
            var elapsed = TimeSpan.FromTicks(_tick.Ticks * _printTicks);
            var total = TimeSpan.FromTicks(_tick.Ticks * _printingTicks);

            // The last quarter of a print runs short of filament. The condition is part of every snapshot while it
            // is true and simply missing afterwards; nothing has to be "cleared".
            var filamentLow = _state is UniversalPrinterState.Printing or UniversalPrinterState.Paused
                && _printTicks * 4 >= _printingTicks * 3;

            telemetry = new PrinterTelemetry(_profile.PrinterId)
            {
                State = _state,
                Conditions = filamentLow
                    ? [new PrinterCondition(FilamentLowCode, PrinterSeverity.Warning, FilamentLowMessage)]
                    : [],
                NozzleTemperature = Math.Round(_nozzle, 1),
                NozzleTargetTemperature = heating ? NozzleTarget : 0,
                BedTemperature = Math.Round(_bed, 1),
                BedTargetTemperature = heating ? BedTarget : 0,
                PrintStatus = new PrintStatus
                {
                    FileName = _fileName,
                    Progress = _printingTicks == 0 ? 0 : (double)_printTicks / _printingTicks,
                    PrintTime = TimeOnly.FromTimeSpan(elapsed),
                    PrintTimeTotal = TimeOnly.FromTimeSpan(total),
                },
            };
        }

        _sink?.Send(telemetry);
    }
}
