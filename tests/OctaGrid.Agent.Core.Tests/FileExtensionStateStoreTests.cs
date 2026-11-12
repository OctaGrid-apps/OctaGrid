using OctaGrid.Agent.Core.Extensions;

namespace OctaGrid.Agent.Core.Tests;

public sealed class FileExtensionStateStoreTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "octagrid-tests", Guid.NewGuid().ToString("N"));

    private string StatePath => Path.Combine(_folder, "extensions.json");

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public void A_missing_file_means_nothing_is_disabled() =>
        Assert.Empty(new FileExtensionStateStore(StatePath).LoadDisabled());

    [Fact]
    public void Saved_ids_are_loaded_back_ignoring_case()
    {
        var store = new FileExtensionStateStore(StatePath);

        store.SaveDisabled(["acme.two", "acme.one"]);
        var loaded = new FileExtensionStateStore(StatePath).LoadDisabled();

        Assert.Equal(2, loaded.Count);
        Assert.Contains("ACME.ONE", loaded);
        Assert.Contains("acme.two", loaded);
    }

    [Fact]
    public void Saving_creates_the_folder_and_leaves_no_temporary_file()
    {
        new FileExtensionStateStore(StatePath).SaveDisabled(["acme.one"]);

        Assert.True(File.Exists(StatePath));
        Assert.False(File.Exists(StatePath + ".tmp"));
    }

    [Fact]
    public void Saving_again_replaces_the_previous_choice()
    {
        var store = new FileExtensionStateStore(StatePath);
        store.SaveDisabled(["acme.one"]);

        store.SaveDisabled([]);

        Assert.Empty(store.LoadDisabled());
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{ \"disabled\": 5 }")]
    [InlineData("null")]
    public void A_damaged_file_means_nothing_is_disabled(string content)
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(StatePath, content);

        Assert.Empty(new FileExtensionStateStore(StatePath).LoadDisabled());
    }
}
