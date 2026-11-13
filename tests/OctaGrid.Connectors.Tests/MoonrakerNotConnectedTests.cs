using OctaGrid.Connectors.Moonraker;
using OctaGrid.Extensions.Models;
using OctaGrid.Extensions.Models.Exceptions;

namespace OctaGrid.Connectors.Tests;

/// <summary>
/// A Moonraker printer that is off or still reconnecting refuses commands with a message the user can be shown, not with
/// a bare exception that reads as a bug.
/// </summary>
public class MoonrakerNotConnectedTests
{
    private static MoonrakerClient CreateClient() =>
        new(new PrinterProfile(Guid.NewGuid(), "moonraker", new Dictionary<string, string> { ["url"] = "10.0.0.5" }));

    public static TheoryData<string> Operations => ["list", "delete", "pause", "resume", "start", "stop", "command"];

    [Theory]
    [MemberData(nameof(Operations))]
    public async Task A_command_without_a_connection_is_refused_as_not_connected(string operation)
    {
        await using var client = CreateClient();
        var ct = TestContext.Current.CancellationToken;

        Task action = operation switch
        {
            "list" => client.ListFilesAsync(ct),
            "delete" => client.DeleteFileAsync("benchy.gcode", ct),
            "pause" => client.PausePrintAsync(ct),
            "resume" => client.ResumePrintAsync(ct),
            "start" => client.StartPrintAsync("benchy.gcode", ct),
            "stop" => client.StopPrintAsync(ct),
            _ => client.SendCommandAsync("G28", ct),
        };

        var ex = await Assert.ThrowsAsync<NotFoundException>(() => action);
        Assert.Equal("The printer is not connected.", ex.Message);
    }
}
