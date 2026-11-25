using System.IO.Compression;
using System.Text;
using System.Text.Json;
using OctaGrid.Connectors.BambuLab;

namespace OctaGrid.Connectors.Tests;

/// <summary>What the Bambu connector reads from a sliced file and from the printer's report.</summary>
public class BambuFileMetadataTests
{
    // slice_info.config as Bambu Studio writes it for a two-plate, two-material project (trimmed to what is read).
    private const string SliceInfo = """
        <?xml version="1.0" encoding="UTF-8"?>
        <config>
          <plate>
            <metadata key="index" value="1"/>
            <metadata key="prediction" value="3600"/>
            <filament id="1" type="PLA" color="#FFFFFF" used_m="4.10" used_g="12.25" nozzle_diameter="0.4"/>
            <filament id="2" type="PETG" color="#000000" used_m="1.00" used_g="3.5"/>
          </plate>
          <plate>
            <metadata key="index" value="2"/>
            <metadata key="prediction" value="600"/>
            <filament id="1" type="PLA" color="#FFFFFF" used_m="1.00" used_g="2.25"/>
          </plate>
        </config>
        """;

    private static MemoryStream ThreeMf(string? sliceInfo)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            if (sliceInfo is not null)
            {
                using var writer = new StreamWriter(archive.CreateEntry("Metadata/slice_info.config").Open(), Encoding.UTF8);
                writer.Write(sliceInfo);
            }
        }

        stream.Position = 0;
        return stream;
    }

    [Fact]
    public void The_filament_weight_is_summed_over_materials_and_plates()
    {
        using var file = ThreeMf(SliceInfo);

        var (metadata, _) = ThreeMfExtractor.ExtractFromStream(file, "project.gcode.3mf", 5_000_000_000);

        Assert.Equal(18.0, metadata.FilamentWeightGrams);
        Assert.Equal(4200, metadata.EstimatedTimeSeconds);
        Assert.Equal("PLA, PETG", metadata.MaterialType);
        Assert.Equal(5_000_000_000, metadata.FileSize); // more than int.MaxValue: sizes are not cut off any more
    }

    [Fact]
    public void A_file_that_records_no_filament_use_has_no_weight()
    {
        using var file = ThreeMf(SliceInfo.Replace(" used_g=\"12.25\"", "").Replace(" used_g=\"3.5\"", "").Replace(" used_g=\"2.25\"", ""));

        var (metadata, _) = ThreeMfExtractor.ExtractFromStream(file, "project.gcode.3mf");

        Assert.Null(metadata.FilamentWeightGrams);
    }

    [Theory]
    [InlineData("""{ "sdcard": false }""", false)]
    [InlineData("""{ "sdcard": true }""", true)]
    [InlineData("""{ "nozzle_temper": 210 }""", null)]
    public void The_report_says_whether_the_printer_has_an_SD_card(string print, bool? expected)
    {
        var mapper = new BambuReportMapper(Guid.NewGuid());

        mapper.Apply(JsonDocument.Parse(print).RootElement);

        Assert.Equal(expected, mapper.HasSdCard);
    }

    [Fact]
    public void A_partial_report_keeps_what_an_earlier_one_said_about_the_SD_card()
    {
        var mapper = new BambuReportMapper(Guid.NewGuid());

        mapper.Apply(JsonDocument.Parse("""{ "sdcard": false }""").RootElement);
        mapper.Apply(JsonDocument.Parse("""{ "mc_percent": 50 }""").RootElement);

        Assert.False(mapper.HasSdCard);
    }
}
