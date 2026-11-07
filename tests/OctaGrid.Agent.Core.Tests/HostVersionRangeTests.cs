using OctaGrid.Agent.Core.Connectors;

namespace OctaGrid.Agent.Core.Tests;

public class HostVersionRangeTests
{
    private static bool Satisfies(string range, string host)
    {
        Assert.True(HostVersionRange.TryParse(range, out var parsed, out var error), error);
        return parsed.Satisfies(ExtensionVersion.Parse(host));
    }

    [Theory]
    [InlineData(">=1.1.0 <2.0.0", "1.1.0", true)]
    [InlineData(">=1.1.0 <2.0.0", "1.9.9", true)]
    [InlineData(">=1.1.0 <2.0.0", "2.0.0", false)]
    [InlineData(">=1.1.0 <2.0.0", "1.0.9", false)]
    [InlineData(">1.0.0", "1.0.0", false)]
    [InlineData("<=1.0.0", "1.0.0", true)]
    [InlineData("1.2.3", "1.2.3", true)]
    [InlineData("=1.2.3", "1.2.4", false)]
    [InlineData("*", "9.9.9", true)]
    public void Checks_every_comparator(string range, string host, bool expected) =>
        Assert.Equal(expected, Satisfies(range, host));

    [Theory]
    [InlineData(">=1.0.0 <2.0.0", "1.5.0-pre-alpha.3", false)]
    [InlineData(">=1.5.0-pre-alpha <2.0.0", "1.5.0-pre-alpha.3", true)]
    [InlineData(">=1.5.0-pre-alpha <2.0.0", "1.5.0-pre-alpha", true)]
    [InlineData(">=1.5.0-preview.1 <2.0.0", "1.5.0-pre-alpha.3", false)]
    [InlineData(">=1.5.0-pre-alpha <2.0.0", "1.6.0-pre-alpha.1", false)]
    [InlineData("*", "1.5.0-pre-alpha.3", false)]
    public void A_pre_release_host_only_matches_a_range_that_names_its_pre_release_line(string range, string host, bool expected) =>
        Assert.Equal(expected, Satisfies(range, host));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(">=one")]
    [InlineData("~1.2")]
    public void Rejects_ranges_it_cannot_read(string range) =>
        Assert.False(HostVersionRange.TryParse(range, out _, out var error) && error is null);

    [Fact]
    public void Orders_versions_by_semver_precedence()
    {
        string[] ordered = ["1.0.0-alpha", "1.0.0-alpha.1", "1.0.0-alpha.beta", "1.0.0-beta", "1.0.0-beta.2", "1.0.0-beta.11", "1.0.0-rc.1", "1.0.0", "1.0.1", "1.10.0"];

        var sorted = ordered.Reverse().Select(ExtensionVersion.Parse).Order().Select(v => v.ToString());

        Assert.Equal(ordered, sorted);
    }

    [Fact]
    public void Ignores_build_metadata()
    {
        Assert.Equal(ExtensionVersion.Parse("1.2.3+abc"), ExtensionVersion.Parse("1.2.3+def"));
    }
}
