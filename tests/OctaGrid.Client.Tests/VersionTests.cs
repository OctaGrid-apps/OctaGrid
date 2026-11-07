namespace OctaGrid.Client.Tests;

public class VersionTests
{
    [Theory]
    [InlineData("1.0.20.1-pre-alpha+4f6d5bbd0b", "1.0.20-pre-alpha")]
    [InlineData("1.0.20.1-pre-alpha", "1.0.20-pre-alpha")]
    [InlineData("1.0.20-pre-alpha+4f6d5bbd0b", "1.0.20-pre-alpha")]
    [InlineData("1.0.20-pre-alpha", "1.0.20-pre-alpha")]
    [InlineData("1.1.0-preview.1", "1.1.0-preview.1")]
    [InlineData("1.1.0.12-preview.1+abc", "1.1.0-preview.1")]
    [InlineData("1.2.0.7", "1.2.0")]
    [InlineData("1.2.0", "1.2.0")]
    [InlineData("v1.2.0", "1.2.0")]
    [InlineData("  1.0.20.1-pre-alpha  ", "1.0.20-pre-alpha")]
    public void The_displayed_version_is_the_release_version(string stamped, string expected) =>
        Assert.Equal(expected, Helper.ToReleaseVersion(stamped));

    [Fact]
    public void A_version_it_cannot_read_is_only_stripped_of_build_metadata() =>
        Assert.Equal("nightly", Helper.ToReleaseVersion("nightly+abc"));

    [Fact]
    public void The_version_shown_in_the_app_has_one_v_and_no_git_height()
    {
        var version = Helper.GetAppVersion();

        Assert.StartsWith("v", version);
        Assert.Matches(@"^v\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$", version);
    }

    // Nerdbank.GitVersioning uses the nearest version.json above a project, so a component (for example a separate
    // agent) may have its own. But a file with a two-part version such as "1.0-pre-alpha" turns the patch number into
    // the git height. One of those once sat next to the desktop project and overrode the root file: release 1.0.20
    // showed up as 1.0.38 in the app. Every version.json must say the full major.minor.patch.
    [Fact]
    public void Every_version_json_names_the_full_major_minor_patch_version()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "OctaGrid.slnx")))
        {
            root = root.Parent;
        }

        Assert.NotNull(root);

        var files = Directory.EnumerateFiles(root!.FullName, "version.json", SearchOption.AllDirectories)
            .Where(f => !f.Split(Path.DirectorySeparatorChar).Any(p => p is "bin" or "obj" or "node_modules" or ".git"))
            .ToList();

        Assert.Contains(files, f => Path.GetRelativePath(root.FullName, f) == "version.json");

        foreach (var file in files)
        {
            using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(file));
            var version = document.RootElement.GetProperty("version").GetString();

            Assert.True(
                version is not null && System.Text.RegularExpressions.Regex.IsMatch(version, @"^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$"),
                $"{Path.GetRelativePath(root.FullName, file)} has version '{version}'. Use major.minor.patch (for example 1.0.20-pre-alpha): " +
                "with only major.minor the git height becomes the patch number.");
        }
    }
}
