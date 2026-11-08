using System.Text.Json;

namespace OctaGrid.Extensions.Abstractions.Tests;

public class ExtensionManifestTests
{
    private const string ValidJson = """
        {
          "id": "acme.prusalink",
          "name": "PrusaLink",
          "version": "1.2.0",
          "publisher": "Acme",
          "kind": "connector",
          "host": ">=1.1.0 <2.0.0",
          "assembly": "Acme.PrusaLink.dll",
          "entryType": "Acme.PrusaLink.PrusaLinkConnector"
        }
        """;

    private static ExtensionManifest Valid() => ExtensionManifest.Parse(ValidJson);

    [Fact]
    public void Parses_the_documented_example()
    {
        var manifest = Valid();

        Assert.Equal("acme.prusalink", manifest.Id);
        Assert.Equal("PrusaLink", manifest.Name);
        Assert.Equal("1.2.0", manifest.Version);
        Assert.Equal("Acme", manifest.Publisher);
        Assert.Equal(ExtensionKinds.Connector, manifest.Kind);
        Assert.Equal(">=1.1.0 <2.0.0", manifest.Host);
        Assert.Equal("Acme.PrusaLink.dll", manifest.Assembly);
        Assert.Equal("Acme.PrusaLink.PrusaLinkConnector", manifest.EntryType);
        Assert.Null(manifest.Alias);
        Assert.Empty(manifest.Validate());
    }

    [Fact]
    public void Ignores_unknown_properties_comments_and_trailing_commas()
    {
        var manifest = ExtensionManifest.Parse("""
            {
              // a comment
              "id": "acme.x", "name": "X", "version": "1.0.0", "publisher": "Acme",
              "kind": "connector", "host": ">=1.0.0", "alias": "octagrid-ext-x.exe",
              "somethingNew": 42,
            }
            """);

        Assert.Empty(manifest.Validate());
        Assert.Equal("octagrid-ext-x.exe", manifest.Alias);
    }

    [Fact]
    public void Out_of_process_manifest_needs_no_assembly()
    {
        var manifest = Valid() with { Assembly = null, EntryType = null, Alias = "octagrid-ext-x.exe" };

        Assert.Empty(manifest.Validate());
    }

    [Fact]
    public void Invalid_json_throws() => Assert.ThrowsAny<JsonException>(() => ExtensionManifest.Parse("{ not json"));

    [Fact]
    public void Json_null_throws() => Assert.Throws<JsonException>(() => ExtensionManifest.Parse("null"));

    [Fact]
    public void Missing_fields_are_reported_instead_of_throwing()
    {
        var errors = ExtensionManifest.Parse("{}").Validate();

        Assert.Contains(errors, e => e.Contains("'id'"));
        Assert.Contains(errors, e => e.Contains("'name'"));
        Assert.Contains(errors, e => e.Contains("'publisher'"));
        Assert.Contains(errors, e => e.Contains("'version'"));
        Assert.Contains(errors, e => e.Contains("'kind'"));
        Assert.Contains(errors, e => e.Contains("'host'"));
        Assert.Contains(errors, e => e.Contains("'assembly'") && e.Contains("'alias'"));
    }

    [Theory]
    [InlineData("1.0")]
    [InlineData("v1.0.0")]
    [InlineData("1.0.0-")]
    [InlineData("one.two.three")]
    public void Rejects_versions_that_are_not_semver(string version) =>
        Assert.Contains((Valid() with { Version = version }).Validate(), e => e.Contains("'version'"));

    [Theory]
    [InlineData("1.0.0")]
    [InlineData("1.0.17-pre-alpha")]
    [InlineData("1.1.0-preview.2")]
    [InlineData("2.0.0-rc.1+build.5")]
    public void Accepts_semver_versions(string version) =>
        Assert.Empty((Valid() with { Version = version }).Validate());

    [Fact]
    public void Rejects_an_invalid_id() =>
        Assert.Contains((Valid() with { Id = "Not Valid" }).Validate(), e => e.Contains("'id'"));

    [Fact]
    public void Rejects_an_unknown_kind() =>
        Assert.Contains((Valid() with { Kind = "theme" }).Validate(), e => e.Contains("'kind'"));

    [Fact]
    public void The_project_url_is_optional_and_read_from_the_manifest()
    {
        Assert.Null(Valid().ProjectUrl);

        var manifest = ExtensionManifest.Parse(ValidJson.Replace("\"kind\"", "\"projectUrl\": \"https://github.com/acme/prusalink\", \"kind\""));

        Assert.Equal("https://github.com/acme/prusalink", manifest.ProjectUrl);
        Assert.Empty(manifest.Validate());
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///C:/x")]
    [InlineData("acme.com/prusalink")]
    public void Ignores_a_project_url_that_is_not_a_web_address_instead_of_failing_the_manifest(string url)
    {
        var manifest = ExtensionManifest.Parse(ValidJson.Replace("\"kind\"", $"\"projectUrl\": \"{url}\", \"kind\""));

        Assert.Null(manifest.ProjectUrl);
        Assert.Empty(manifest.Validate());
    }

    [Theory]
    [InlineData(@"C:\evil\x.dll")]
    [InlineData("/etc/x.dll")]
    [InlineData("..\\x.dll")]
    [InlineData("sub/../../x.dll")]
    public void Rejects_assembly_paths_that_leave_the_package(string path) =>
        Assert.Contains((Valid() with { Assembly = path }).Validate(), e => e.Contains("relative path"));
}
