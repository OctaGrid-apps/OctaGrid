using OctaGrid.Extensions.Models;


namespace OctaGrid.Extensions.Abstractions.Tests;

public class ExtensionIdTests
{
    [Theory]
    [InlineData("octagrid.moonraker")]
    [InlineData("octagrid.bambu-mqtt")]
    [InlineData("acme.prusa-link")]
    [InlineData("a1.b2")]
    public void Accepts_publisher_dot_name(string id) => Assert.True(ExtensionId.IsValid(id));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("moonraker")]
    [InlineData("OctaGrid.Moonraker")]
    [InlineData("octagrid.moon_raker")]
    [InlineData("octagrid..moonraker")]
    [InlineData("octagrid.moonraker.extra")]
    [InlineData("-octagrid.moonraker")]
    [InlineData("octagrid-.moonraker")]
    [InlineData("octagrid.moon raker")]
    public void Rejects_everything_else(string? id) => Assert.False(ExtensionId.IsValid(id));

    [Fact]
    public void Rejects_ids_longer_than_the_maximum()
    {
        var id = new string('a', ExtensionId.MaxLength) + ".b";
        Assert.False(ExtensionId.IsValid(id));
    }
}
