using System.Text;
using System.Text.Json;
using OctaGrid.Connectors.BambuLab;
using OctaGrid.Extensions.Models;
using OctaGrid.Extensions.Models.Exceptions;

namespace OctaGrid.Connectors.Tests;

/// <summary>
/// Starting, pausing, resuming and stopping Bambu prints: the commands sent, the printer's answers, and how a plate's
/// filaments are matched to the loaded spools.
/// </summary>
public class BambuPrintControlTests
{
    // A full report's AMS part: one AMS with white PLA, an empty slot, black PETG and red PLA; white PLA outside.
    private const string FullReport = """
        {
          "ams": {
            "ams_exist_bits": "1",
            "tray_exist_bits": "d",
            "ams": [
              {
                "id": "0",
                "tray": [
                  { "id": "0", "tray_type": "PLA", "tray_color": "FFFFFFFF" },
                  { "id": "1" },
                  { "id": "2", "tray_type": "PETG", "tray_color": "000000FF" },
                  { "id": "3", "tray_type": "PLA", "tray_color": "FF0000FF" }
                ]
              }
            ]
          },
          "vt_tray": { "id": "254", "tray_type": "PLA", "tray_color": "FFFFFFFF" }
        }
        """;

    private static BambuLoadedFilaments Loaded(params string[] reports)
    {
        var tracker = new BambuAmsTracker();
        foreach (var report in reports)
        {
            tracker.Apply(JsonDocument.Parse(report).RootElement);
        }

        return tracker.Loaded;
    }

    private static BambuPlate Plate(params BambuPlateFilament[] filaments) => new(1, filaments);

    private static JsonElement Print(string json) => JsonDocument.Parse(json).RootElement;

    // ───────────────────────── Commands and answers ─────────────────────────

    [Fact]
    public async Task The_printers_answer_with_the_commands_sequence_id_completes_it()
    {
        var replies = new BambuCommandReplies();
        var command = Payload.Pause();
        var reply = replies.Expect(command);

        replies.TryComplete(Print($$"""{ "command": "pause", "sequence_id": "{{command.SequenceId}}", "result": "SUCCESS" }"""));

        Assert.True((await reply).Succeeded);
    }

    [Fact]
    public async Task A_refusal_carries_the_printers_reason()
    {
        var replies = new BambuCommandReplies();
        var command = Payload.Resume();
        var reply = replies.Expect(command);

        replies.TryComplete(Print($$"""{ "command": "resume", "sequence_id": {{command.SequenceId}}, "result": "fail", "reason": "busy" }"""));

        var answer = await reply;
        Assert.False(answer.Succeeded);
        Assert.Equal("busy", answer.Reason);
    }

    [Theory]
    [InlineData("failed", "mqtt message verify failed", "Developer Mode")]
    [InlineData("fail", "busy", "The printer refused to start the print: busy.")]
    [InlineData("fail", "fail", "The printer refused to start the print.")]
    [InlineData("fail", null, "The printer refused to start the print.")]
    public void A_refusal_tells_the_user_why(string result, string? reason, string expected) =>
        Assert.Contains(expected, new BambuCommandReply(result, reason).RefusalMessage("start the print"));

    [Fact]
    public void An_answer_to_another_clients_command_is_not_taken_for_ours()
    {
        var replies = new BambuCommandReplies();
        var command = Payload.Stop();
        var reply = replies.Expect(command);

        Assert.False(replies.TryComplete(Print("""{ "command": "stop", "sequence_id": "0", "result": "success" }""")));
        Assert.False(replies.TryComplete(Print($$"""{ "command": "pause", "sequence_id": "{{command.SequenceId}}", "result": "success" }""")));
        Assert.False(reply.IsCompleted);
    }

    [Fact]
    public async Task Waiting_commands_fail_when_the_connection_drops()
    {
        var replies = new BambuCommandReplies();
        var reply = replies.Expect(Payload.Pause());

        replies.FailAll(new NotFoundException("The printer is not connected."));

        await Assert.ThrowsAsync<NotFoundException>(() => reply);
    }

    [Fact]
    public void The_start_command_prints_the_plate_with_the_matched_trays()
    {
        var options = ProjectFileOptions.FromSdCard("Benchy_PLA_1h.gcode.3mf", 2, new BambuFilamentMapping(true, [3, -1, 0]));

        var print = JsonDocument.Parse(Payload.PrintProjectFile(options).Json).RootElement.GetProperty("print");

        Assert.Equal("project_file", print.GetProperty("command").GetString());
        Assert.Equal("Metadata/plate_2.gcode", print.GetProperty("param").GetString());
        Assert.Equal("file:///sdcard/Benchy_PLA_1h.gcode.3mf", print.GetProperty("url").GetString());
        Assert.Equal("Benchy_PLA_1h", print.GetProperty("subtask_name").GetString());
        Assert.True(print.GetProperty("use_ams").GetBoolean());
        Assert.Equal([3, -1, 0], print.GetProperty("ams_mapping").EnumerateArray().Select(e => e.GetInt32()));
        Assert.False(print.GetProperty("timelapse").GetBoolean());
        Assert.True(print.GetProperty("bed_levelling").GetBoolean());
    }

    [Theory]
    [InlineData("pause")]
    [InlineData("resume")]
    [InlineData("stop")]
    public void Print_control_commands_go_out_under_the_print_category(string name)
    {
        var command = name switch
        {
            "pause" => Payload.Pause(),
            "resume" => Payload.Resume(),
            _ => Payload.Stop(),
        };

        var print = JsonDocument.Parse(command.Json).RootElement.GetProperty("print");

        Assert.Equal(name, command.Name);
        Assert.Equal(name, print.GetProperty("command").GetString());
        Assert.Equal(command.SequenceId, print.GetProperty("sequence_id").GetString());
    }

    [Theory]
    [InlineData("Benchy.gcode.3mf", "Benchy")]
    [InlineData("Benchy_PLA_1h2m.gcode.3mf", "Benchy_PLA_1h2m")]
    [InlineData("Project.3MF", "Project")]
    [InlineData(".3mf", ".3mf")]
    public void The_job_is_named_after_the_file_without_its_extension(string fileName, string jobName) =>
        Assert.Equal(jobName, ProjectFileOptions.JobName(fileName));

    // ───────────────────────── Job state ─────────────────────────

    [Fact]
    public void A_stopped_job_is_cancelled_not_failed()
    {
        // Bambu reports a stopped job as FAILED, with the "task was cancelled" error 0x0300400C.
        var mapper = new BambuReportMapper(Guid.NewGuid());
        mapper.Apply(Print("""{ "gcode_state": "RUNNING", "print_error": 0 }"""));

        mapper.Apply(Print("""{ "gcode_state": "FAILED", "print_error": 50348044 }"""));

        Assert.Equal(UniversalPrinterState.Cancelled, mapper.Snapshot().State);
    }

    [Fact]
    public void The_cancel_code_counts_when_it_arrives_in_a_later_report()
    {
        var mapper = new BambuReportMapper(Guid.NewGuid());
        mapper.Apply(Print("""{ "gcode_state": "FAILED" }"""));

        Assert.True(mapper.Apply(Print("""{ "print_error": 50348044 }""")));

        Assert.Equal(UniversalPrinterState.Cancelled, mapper.Snapshot().State);
    }

    [Fact]
    public void A_cancelled_job_stays_cancelled_when_the_printer_clears_the_error()
    {
        var mapper = new BambuReportMapper(Guid.NewGuid());
        mapper.Apply(Print("""{ "gcode_state": "FAILED", "print_error": 50348044 }"""));

        mapper.Apply(Print("""{ "print_error": 0 }"""));
        mapper.Apply(Print("""{ "gcode_state": "FAILED", "print_error": 0 }"""));

        Assert.Equal(UniversalPrinterState.Cancelled, mapper.Snapshot().State);
    }

    [Fact]
    public void The_next_job_failing_after_a_cancelled_one_is_failed()
    {
        var mapper = new BambuReportMapper(Guid.NewGuid());
        mapper.Apply(Print("""{ "gcode_state": "FAILED", "print_error": 50348044 }"""));
        mapper.Apply(Print("""{ "gcode_state": "RUNNING", "print_error": 0 }"""));

        mapper.Apply(Print("""{ "gcode_state": "FAILED" }"""));

        Assert.Equal(UniversalPrinterState.Failed, mapper.Snapshot().State);
    }

    [Fact]
    public void A_job_that_fails_for_another_reason_stays_failed()
    {
        var mapper = new BambuReportMapper(Guid.NewGuid());

        mapper.Apply(Print("""{ "gcode_state": "FAILED", "print_error": 50364437 }"""));

        Assert.Equal(UniversalPrinterState.Failed, mapper.Snapshot().State);
    }

    [Fact]
    public void A_new_job_after_a_cancelled_one_is_printing()
    {
        var mapper = new BambuReportMapper(Guid.NewGuid());
        mapper.Apply(Print("""{ "gcode_state": "FAILED", "print_error": 50348044 }"""));

        mapper.Apply(Print("""{ "gcode_state": "RUNNING" }"""));

        Assert.Equal(UniversalPrinterState.Printing, mapper.Snapshot().State);
    }

    // ───────────────────────── Loaded spools ─────────────────────────

    [Fact]
    public void A_full_report_lists_the_loaded_trays_by_tray_id()
    {
        var loaded = Loaded(FullReport);

        Assert.True(loaded.Known);
        Assert.True(loaded.HasAms);
        Assert.Equal(
            [new BambuTray(0, "PLA", "FFFFFF"), new BambuTray(2, "PETG", "000000"), new BambuTray(3, "PLA", "FF0000")],
            loaded.AmsTrays);
        Assert.Equal(new BambuTray(254, "PLA", "FFFFFF"), loaded.ExternalSpool);
    }

    [Fact]
    public void A_report_of_one_tray_keeps_the_others()
    {
        var loaded = Loaded(FullReport, """{ "ams": { "ams": [ { "id": "0", "tray": [ { "id": "0", "remain": 80 } ] } ] } }""");

        Assert.Equal(3, loaded.AmsTrays.Count);
    }

    [Fact]
    public void An_unloaded_tray_is_no_longer_offered()
    {
        var emptied = Loaded(FullReport, """{ "ams": { "ams": [ { "id": "0", "tray": [ { "id": "3", "tray_type": "" } ] } ] } }""");
        var removed = Loaded(FullReport, """{ "ams": { "tray_exist_bits": "5" } }""");

        Assert.DoesNotContain(emptied.AmsTrays, t => t.TrayId == 3);
        Assert.DoesNotContain(removed.AmsTrays, t => t.TrayId == 3);
    }

    [Fact]
    public void A_detached_AMS_takes_its_trays_with_it()
    {
        var loaded = Loaded(FullReport, """{ "ams": { "ams_exist_bits": "0" } }""");

        Assert.False(loaded.HasAms);
        Assert.Empty(loaded.AmsTrays);
    }

    [Fact]
    public void Until_a_report_describes_the_AMS_the_spools_are_unknown()
    {
        Assert.False(Loaded("""{ "nozzle_temper": 210 }""").Known);
        Assert.True(Loaded("""{ "ams": { "ams": [] } }""").Known);
    }

    [Fact]
    public void Existence_bits_of_no_unit_describe_the_AMS()
    {
        var none = Loaded("""{ "ams": { "ams_exist_bits": "0" } }""");

        Assert.True(none.Known);
        Assert.False(none.HasAms);
    }

    [Fact]
    public void Existence_bits_of_a_unit_wait_for_its_trays()
    {
        // Known with no trays yet would read as no AMS, and the start would turn the AMS off.
        Assert.False(Loaded("""{ "ams": { "ams_exist_bits": "1" } }""").Known);
    }

    [Fact]
    public void An_AMS_HT_counts_as_an_AMS_without_offering_its_spool()
    {
        var loaded = Loaded("""
            { "ams": { "ams": [ { "id": "128", "tray": [ { "id": "0", "tray_type": "PLA", "tray_color": "FFFFFFFF" } ] } ] } }
            """);

        Assert.True(loaded.Known);
        Assert.True(loaded.HasAms);
        Assert.True(loaded.HasAmsHt);
        Assert.Empty(loaded.AmsTrays);
    }

    [Fact]
    public void A_filament_only_in_an_AMS_HT_stops_the_start()
    {
        var loaded = Loaded("""
            { "ams": { "ams": [ { "id": "128", "tray": [ { "id": "0", "tray_type": "PLA", "tray_color": "FFFFFFFF" } ] } ] } }
            """);

        Assert.False(BambuFilamentMatcher.TryMatch(Plate(new BambuPlateFilament(1, "PLA", "FFFFFF")), loaded, out _, out var problem));
        Assert.Contains("AMS HT", problem);
    }

    // ───────────────────────── Plates ─────────────────────────

    [Fact]
    public void The_sliced_plates_and_their_filaments_are_read_from_slice_info()
    {
        var sliceInfo = Encoding.UTF8.GetBytes("""
            <?xml version="1.0" encoding="UTF-8"?>
            <config>
              <plate>
                <metadata key="index" value="3"/>
                <filament id="1" type="PLA" color="#ffffff" used_g="12.25"/>
                <filament id="4" type="PETG" color="#000000" used_g="3.5"/>
              </plate>
            </config>
            """);

        var plate = Assert.Single(BambuSliceInfo.ReadPlates(sliceInfo));

        Assert.Equal(3, plate.Index);
        Assert.Equal([new BambuPlateFilament(1, "PLA", "FFFFFF"), new BambuPlateFilament(4, "PETG", "000000")], plate.Filaments);
    }

    [Fact]
    public void A_plates_time_weight_and_picture_come_from_slice_info()
    {
        var sliceInfo = Encoding.UTF8.GetBytes("""
            <config>
              <plate>
                <metadata key="index" value="2"/>
                <metadata key="prediction" value="2460"/>
                <metadata key="weight" value="15.30"/>
                <filament id="1" type="PLA" color="#FFFFFF"/>
              </plate>
            </config>
            """);

        var plate = Assert.Single(BambuSliceInfo.ReadPlates(sliceInfo));

        Assert.Equal(2460, plate.EstimatedTimeSeconds);
        Assert.Equal(15.3, plate.WeightGrams);
        Assert.Equal("Metadata/plate_2.png", plate.ThumbnailEntry);
    }

    [Fact]
    public void Slice_info_that_is_not_XML_has_no_plates() =>
        Assert.Empty(BambuSliceInfo.ReadPlates(Encoding.UTF8.GetBytes("not xml")));

    // ───────────────────────── Matching ─────────────────────────

    [Fact]
    public void Each_filament_goes_to_the_tray_with_its_type_and_colour()
    {
        var plate = Plate(new BambuPlateFilament(1, "PLA", "FF0000"), new BambuPlateFilament(3, "PETG", "000000"));

        Assert.True(BambuFilamentMatcher.TryMatch(plate, Loaded(FullReport), out var mapping, out _));

        Assert.True(mapping.UseAms);
        Assert.Equal([3, -1, 2], mapping.AmsMapping);
    }

    [Fact]
    public void Of_several_identical_spools_the_lowest_tray_is_used()
    {
        var twoWhite = FullReport.Replace("FF0000FF", "FFFFFFFF");

        Assert.True(BambuFilamentMatcher.TryMatch(Plate(new BambuPlateFilament(1, "PLA", "FFFFFF")), Loaded(twoWhite), out var mapping, out _));

        Assert.Equal([0], mapping.AmsMapping);
    }

    [Theory]
    [InlineData("PLA", "00FF00")]
    [InlineData("ABS", "FFFFFF")]
    public void A_filament_without_a_matching_spool_stops_the_start(string type, string color)
    {
        var plate = Plate(new BambuPlateFilament(1, "PLA", "FFFFFF"), new BambuPlateFilament(2, type, color));

        Assert.False(BambuFilamentMatcher.TryMatch(plate, Loaded(FullReport), out _, out var problem));

        Assert.Contains($"{type} #{color}", problem);
        Assert.DoesNotContain("PLA #FFFFFF", problem);
    }

    [Fact]
    public void One_filament_found_only_on_the_external_spool_prints_from_it()
    {
        var loaded = Loaded("""
            {
              "ams": { "ams": [ { "id": "0", "tray": [ { "id": "0", "tray_type": "PETG", "tray_color": "FFFFFFFF" } ] } ] },
              "vt_tray": { "id": "254", "tray_type": "PLA", "tray_color": "FFFFFFFF" }
            }
            """);

        Assert.True(BambuFilamentMatcher.TryMatch(Plate(new BambuPlateFilament(1, "PLA", "FFFFFF")), loaded, out var mapping, out _));

        Assert.False(mapping.UseAms);
        Assert.Empty(mapping.AmsMapping);
    }

    [Fact]
    public void Without_an_AMS_one_filament_prints_from_the_external_spool()
    {
        var loaded = Loaded("""{ "ams": { "ams": [] }, "vt_tray": { "id": "254", "tray_type": "" } }""");

        Assert.True(BambuFilamentMatcher.TryMatch(Plate(new BambuPlateFilament(1, "PETG", "123456")), loaded, out var mapping, out _));
        Assert.False(mapping.UseAms);
    }

    [Fact]
    public void Without_an_AMS_a_spool_set_to_another_type_stops_the_start()
    {
        var loaded = Loaded("""{ "ams": { "ams": [] }, "vt_tray": { "id": "254", "tray_type": "TPU", "tray_color": "000000FF" } }""");

        Assert.False(BambuFilamentMatcher.TryMatch(Plate(new BambuPlateFilament(1, "PLA", "FFFFFF")), loaded, out _, out var problem));
        Assert.Contains("TPU", problem);
    }

    [Fact]
    public void Without_an_AMS_several_filaments_cannot_print()
    {
        var loaded = Loaded("""{ "ams": { "ams": [] } }""");
        var plate = Plate(new BambuPlateFilament(1, "PLA", "FFFFFF"), new BambuPlateFilament(2, "PLA", "000000"));

        Assert.False(BambuFilamentMatcher.TryMatch(plate, loaded, out _, out var problem));
        Assert.Contains("no AMS", problem);
    }

    [Fact]
    public void The_start_dialog_shows_each_filament_with_the_tray_it_prints_from()
    {
        var plate = Plate(new BambuPlateFilament(1, "PLA", "FF0000"), new BambuPlateFilament(3, "PETG", "000000"));
        var loaded = Loaded(FullReport);
        BambuFilamentMatcher.TryMatch(plate, loaded, out var mapping, out _);

        var filaments = BambuFilamentMatcher.Describe(plate, loaded, mapping);

        Assert.Equal([new PlateFilament("PLA", "FF0000", "AMS A4"), new PlateFilament("PETG", "000000", "AMS A3")], filaments);
    }

    [Fact]
    public void A_plate_that_cannot_start_still_shows_the_trays_that_match()
    {
        var plate = Plate(new BambuPlateFilament(1, "PLA", "FFFFFF"), new BambuPlateFilament(2, "ABS", "123456"));

        var filaments = BambuFilamentMatcher.Describe(plate, Loaded(FullReport), mapping: null);

        Assert.Equal(["AMS A1", null], filaments.Select(f => f.Source));
    }

    [Theory]
    [InlineData(0, "AMS A1")]
    [InlineData(5, "AMS B2")]
    [InlineData(15, "AMS D4")]
    public void Trays_are_named_as_in_Bambus_apps(int trayId, string label) =>
        Assert.Equal(label, BambuFilamentMatcher.TrayLabel(trayId));

    // ───────────────────────── Start options ─────────────────────────

    private static ProjectFileOptions Defaults() =>
        ProjectFileOptions.FromSdCard("a.gcode.3mf", 1, new BambuFilamentMapping(false, []));

    [Fact]
    public void The_offered_options_default_to_what_the_start_command_sends()
    {
        var defaults = Defaults();

        var values = BambuStartOptions.Fields.ToDictionary(f => f.Key, f => f.DefaultValue!);
        Assert.True(BambuStartOptions.TryApply(defaults, values, out var applied, out _));

        Assert.Equal(defaults, applied);
    }

    [Fact]
    public void The_users_options_change_the_start_command()
    {
        var values = new Dictionary<string, string>
        {
            [BambuStartOptions.Timelapse] = "true",
            [BambuStartOptions.BedLevelling] = "false",
            [BambuStartOptions.FlowCalibration] = "False",
            [BambuStartOptions.VibrationCalibration] = "false",
        };

        Assert.True(BambuStartOptions.TryApply(Defaults(), values, out var applied, out _));

        var print = JsonDocument.Parse(Payload.PrintProjectFile(applied).Json).RootElement.GetProperty("print");
        Assert.True(print.GetProperty("timelapse").GetBoolean());
        Assert.False(print.GetProperty("bed_levelling").GetBoolean());
        Assert.False(print.GetProperty("flow_cali").GetBoolean());
        Assert.False(print.GetProperty("vibration_cali").GetBoolean());
    }

    [Theory]
    [InlineData("timelapse", "yes")]
    [InlineData("nozzle_temp", "true")]
    public void An_unknown_option_or_value_is_refused(string key, string value)
    {
        Assert.False(BambuStartOptions.TryApply(Defaults(), new Dictionary<string, string> { [key] = value }, out _, out var problem));
        Assert.Contains(key, problem);
    }

    // ───────────────────────── Without a connection ─────────────────────────

    public static TheoryData<string> Operations => ["start", "start-with-choices", "plan", "pause", "resume", "stop"];

    [Theory]
    [MemberData(nameof(Operations))]
    public async Task A_command_without_a_connection_is_refused_as_not_connected(string operation)
    {
        var profile = new PrinterProfile(Guid.NewGuid(), BambuLabConnector.Id,
            new Dictionary<string, string> { ["url"] = "10.0.0.5", ["apiKey"] = "12345678" });
        await using var client = new MqttClient(profile);
        var ct = TestContext.Current.CancellationToken;

        Task action = operation switch
        {
            "start" => client.StartPrintAsync("benchy.gcode.3mf", ct),
            "start-with-choices" => client.StartPrintAsync("benchy.gcode.3mf", new PrintStartChoices(2), ct),
            "plan" => client.GetStartPlanAsync("benchy.gcode.3mf", ct),
            "pause" => client.PausePrintAsync(ct),
            "resume" => client.ResumePrintAsync(ct),
            _ => client.StopPrintAsync(ct),
        };

        var ex = await Assert.ThrowsAsync<NotFoundException>(() => action);
        Assert.Equal("The printer is not connected.", ex.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("folder/benchy.gcode.3mf")]
    [InlineData("benchy.gcode")]
    public async Task Only_a_3mf_in_the_cards_root_can_be_started(string fileName)
    {
        var profile = new PrinterProfile(Guid.NewGuid(), BambuLabConnector.Id, new Dictionary<string, string> { ["url"] = "10.0.0.5" });
        await using var client = new MqttClient(profile);

        await Assert.ThrowsAsync<ValidationException>(() => client.StartPrintAsync(fileName, TestContext.Current.CancellationToken));
    }
}
