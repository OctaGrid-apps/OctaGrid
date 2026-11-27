using OctaGrid.Client.DesignTime;
using OctaGrid.Client.ViewModels;
using OctaGrid.Extensions.Models;
using OctaGrid.Extensions.Models.Exceptions;

namespace OctaGrid.Client.Tests;

/// <summary>
/// The start dialog: nothing starts until the user confirms the build plate is clear, a plate that cannot start cannot
/// be started, and the choices sent back are the ones on screen.
/// </summary>
public class PrintStartViewModelTests
{
    private static async Task<(PrintStartViewModel Start, PrinterPageTabsTests.FakeCommands Commands)> LoadedAsync(PrintStartPlan? plan)
    {
        var commands = new PrinterPageTabsTests.FakeCommands { Plan = plan };
        var start = new PrintStartViewModel(Guid.NewGuid(), "X1C", "benchy.gcode.3mf", commands);
        await start.LoadCommand.ExecuteAsync(null);
        return (start, commands);
    }

    [Fact]
    public async Task Nothing_starts_until_the_user_says_the_build_plate_is_clear()
    {
        var (start, _) = await LoadedAsync(DesignAgentCommands.DesignPlan("benchy.gcode.3mf"));

        Assert.False(start.StartCommand.CanExecute(null));

        start.IsPlateClear = true;

        Assert.True(start.StartCommand.CanExecute(null));
    }

    [Fact]
    public async Task The_first_plate_that_can_start_is_chosen_and_one_that_cannot_is_refused()
    {
        var (start, _) = await LoadedAsync(DesignAgentCommands.DesignPlan("benchy.gcode.3mf"));
        start.IsPlateClear = true;

        Assert.True(start.HasPlateChoice);
        Assert.Equal(1, start.SelectedPlate?.Number);

        start.SelectedPlate = start.Plates[1];

        Assert.False(start.StartCommand.CanExecute(null));
        Assert.Equal("No loaded spool matches PETG #000000.", start.SelectedPlate.Problem);
    }

    [Fact]
    public async Task The_choices_are_the_plate_and_the_options_on_screen()
    {
        var (start, _) = await LoadedAsync(DesignAgentCommands.DesignPlan("benchy.gcode.3mf"));
        start.Options.Single(o => o.Key == "timelapse").BoolValue = true;

        var choices = start.Choices();

        Assert.Equal(1, choices.Plate);
        Assert.Equal("true", choices.Options?["timelapse"]);
        Assert.Equal("true", choices.Options?["bed_levelling"]);
    }

    [Fact]
    public async Task A_printer_without_choices_only_needs_the_confirmation()
    {
        var (start, _) = await LoadedAsync(plan: null);

        Assert.False(start.HasPlateChoice);
        Assert.False(start.HasOptions);
        start.IsPlateClear = true;

        Assert.True(start.StartCommand.CanExecute(null));
        Assert.Equal(new PrintStartChoices(), start.Choices());
    }

    [Fact]
    public async Task A_file_that_cannot_be_read_says_why_and_cannot_be_started()
    {
        var commands = new PrinterPageTabsTests.FakeCommands { PlanFailure = new NotFoundException("'benchy.gcode.3mf' is not on the printer's SD card.") };
        var start = new PrintStartViewModel(Guid.NewGuid(), "X1C", "benchy.gcode.3mf", commands);

        await start.LoadCommand.ExecuteAsync(null);
        start.IsPlateClear = true;

        Assert.Equal("'benchy.gcode.3mf' is not on the printer's SD card.", start.ErrorMessage);
        Assert.False(start.StartCommand.CanExecute(null));
    }

    [Fact]
    public async Task A_plan_without_plates_says_so_instead_of_leaving_start_disabled_silently()
    {
        var (start, _) = await LoadedAsync(new PrintStartPlan("benchy.gcode.3mf", [], []));
        start.IsPlateClear = true;

        Assert.Equal("The file has no plate to print.", start.ErrorMessage);
        Assert.False(start.StartCommand.CanExecute(null));
    }

    [Fact]
    public async Task A_number_option_is_edited_as_text_and_checked_before_the_start()
    {
        var plan = new PrintStartPlan(
            "benchy.gcode.3mf",
            [new PrintPlate(1)],
            [new SettingField("bed_temp", "Bed temperature", SettingFieldType.Number, Required: false, DefaultValue: "60")]);
        var (start, _) = await LoadedAsync(plan);
        var option = Assert.Single(start.Options);
        start.IsPlateClear = true;

        Assert.True(option.IsTextInput);
        option.Value = "hot";
        start.StartCommand.Execute(null);

        Assert.Equal("Enter a number.", option.Error);

        option.Value = "65";
        Assert.Equal("65", start.Choices().Options?["bed_temp"]);
    }

    [Fact]
    public void A_plate_shows_its_time_weight_and_where_each_filament_comes_from()
    {
        var plate = new PrintPlateViewModel(DesignAgentCommands.DesignPlan("a.3mf").Plates[0]);

        Assert.Equal("Plate 1: Benchy", plate.Title);
        Assert.Contains(PrintFileText.Weight(15.3)!, plate.Summary, StringComparison.Ordinal);
        Assert.Equal(["AMS A1", "AMS A3"], plate.Filaments.Select(f => f.Source));
        Assert.Equal("#FFFFFF", plate.Filaments[0].ColorHex);
    }
}
