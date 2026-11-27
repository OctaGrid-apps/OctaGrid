using OctaGrid.Agent.Core.Files;
using OctaGrid.Extensions.Models;

namespace OctaGrid.Agent.Core.Tests;

/// <summary>
/// The picture of the print OctaGrid started: found by the file's name or the job name the printer reports, kept across
/// restarts, replaced by the next start and removed with its printer.
/// </summary>
public sealed class PrinterJobPicturesTests : IDisposable
{
    private static readonly CancellationToken Ct = CancellationToken.None;
    private static readonly PrintThumbnail Plate3 = new("image/png", [3, 3, 3]);

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "octagrid-job-pictures-tests", Guid.NewGuid().ToString("N"));
    private readonly Guid _printer = Guid.NewGuid();

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Theory]
    [InlineData("Benchy_PLA_1h.gcode.3mf", "Benchy_PLA_1h.gcode.3mf", true)]
    [InlineData("Benchy_PLA_1h.gcode.3mf", "Benchy_PLA_1h", true)]
    [InlineData("Project.3MF", "Project", true)]
    [InlineData("cube.gcode", "cube", true)]
    [InlineData("Benchy_PLA_1h.gcode.3mf", "Benchy_PLA_1h.gcode", false)]
    [InlineData("Benchy.gcode.3mf", "benchy", false)]
    [InlineData("Benchy.gcode.3mf", "Benchy 2", false)]
    public void A_job_is_the_files_when_the_printer_names_it_after_the_file(string fileName, string jobName, bool expected) =>
        Assert.Equal(expected, PrinterJobPictures.IsJobOf(fileName, jobName));

    [Fact]
    public async Task The_picture_is_found_by_the_file_name_and_the_job_name_after_a_restart()
    {
        await new PrinterJobPictures(_folder).RememberAsync(_printer, "Benchy.gcode.3mf", Plate3, Ct);

        var afterRestart = new PrinterJobPictures(_folder);

        Assert.Equal(Plate3.Data, (await afterRestart.FindAsync(_printer, "Benchy.gcode.3mf", Ct))?.Data);
        Assert.Equal("image/png", (await afterRestart.FindAsync(_printer, "Benchy", Ct))?.ContentType);
        Assert.Null(await afterRestart.FindAsync(_printer, "Cube", Ct));
        Assert.Null(await afterRestart.FindAsync(Guid.NewGuid(), "Benchy", Ct));
    }

    [Fact]
    public async Task The_next_start_replaces_the_picture()
    {
        var pictures = new PrinterJobPictures(_folder);
        await pictures.RememberAsync(_printer, "Benchy.gcode.3mf", Plate3, Ct);

        await pictures.RememberAsync(_printer, "Cube.gcode.3mf", new PrintThumbnail("image/jpeg", [7]), Ct);

        Assert.Null(await pictures.FindAsync(_printer, "Benchy", Ct));
        Assert.Equal("image/jpeg", (await pictures.FindAsync(_printer, "Cube", Ct))?.ContentType);
    }

    [Fact]
    public async Task A_printer_that_is_no_longer_the_agents_loses_its_picture()
    {
        var other = Guid.NewGuid();
        var pictures = new PrinterJobPictures(_folder);
        await pictures.RememberAsync(_printer, "Benchy.gcode.3mf", Plate3, Ct);
        await pictures.RememberAsync(other, "Cube.gcode.3mf", Plate3, Ct);

        await pictures.PruneAsync([other], Ct);

        Assert.Null(await pictures.FindAsync(_printer, "Benchy", Ct));
        Assert.NotNull(await pictures.FindAsync(other, "Cube", Ct));
    }

    [Fact]
    public async Task The_file_cache_keeps_the_pictures_apart_from_its_printers_and_cleans_them_up_too()
    {
        var cache = new PrinterFileCache(_folder);
        await cache.Jobs.RememberAsync(_printer, "Benchy.gcode.3mf", Plate3, Ct);

        // The cache's own cleanup must not take the pictures' folder for a printer's.
        await cache.PruneAsync([_printer], Ct);
        Assert.NotNull(await cache.Jobs.FindAsync(_printer, "Benchy", Ct));

        await cache.PruneAsync([], Ct);
        Assert.Null(await cache.Jobs.FindAsync(_printer, "Benchy", Ct));
    }

    [Fact]
    public async Task A_folder_that_cannot_be_written_does_not_fail_the_start()
    {
        // A file in place of the folder makes every write fail.
        Directory.CreateDirectory(Path.GetDirectoryName(_folder)!);
        await File.WriteAllTextAsync(_folder, "in the way", Ct);
        try
        {
            var pictures = new PrinterJobPictures(_folder);

            await pictures.RememberAsync(_printer, "Benchy.gcode.3mf", Plate3, Ct);

            Assert.Null(await pictures.FindAsync(_printer, "Benchy", Ct));
        }
        finally
        {
            File.Delete(_folder);
        }
    }
}
