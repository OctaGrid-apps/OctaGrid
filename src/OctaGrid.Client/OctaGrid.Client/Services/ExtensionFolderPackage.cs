using System.IO.Compression;
using Avalonia.Platform.Storage;

namespace OctaGrid.Client.Services;

/// <summary>
/// Turns a folder the user picked into a package an agent can install. An agent takes a package as one file, and the
/// folder may be on another computer than the agent, so the client zips it. It reads through the storage provider, not
/// file paths, so it also works where there are none (WASM).
/// </summary>
public static class ExtensionFolderPackage
{
    /// <summary>Zips the folder's content (not the folder itself) into a stream positioned at its start.</summary>
    public static Task<Stream> ZipAsync(IStorageFolder folder, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(folder);
        return ZipAsync(FilesOf(folder, prefix: "", cancellationToken), cancellationToken);
    }

    /// <summary>Zips files, each under its path inside the package, into a stream positioned at its start.</summary>
    internal static async Task<Stream> ZipAsync(IAsyncEnumerable<PackageFile> files, CancellationToken cancellationToken)
    {
        var zipped = new MemoryStream();
        try
        {
            using (var archive = new ZipArchive(zipped, ZipArchiveMode.Create, leaveOpen: true))
            {
                await foreach (var file in files.WithCancellation(cancellationToken))
                {
                    var entry = archive.CreateEntry(file.Path, CompressionLevel.Optimal);
                    await using var source = await file.OpenReadAsync();
                    await using var target = entry.Open();
                    await source.CopyToAsync(target, cancellationToken);
                }
            }

            zipped.Position = 0;
            return zipped;
        }
        catch
        {
            await zipped.DisposeAsync();
            throw;
        }
    }

    private static async IAsyncEnumerable<PackageFile> FilesOf(
        IStorageFolder folder, string prefix, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var item in folder.GetItemsAsync().WithCancellation(cancellationToken))
        {
            var path = prefix + item.Name;
            switch (item)
            {
                case IStorageFolder child:
                    await foreach (var file in FilesOf(child, path + "/", cancellationToken))
                    {
                        yield return file;
                    }

                    break;
                case IStorageFile file:
                    yield return new PackageFile(path, file.OpenReadAsync);
                    break;
            }
        }
    }

    /// <summary>A file to put in a package.</summary>
    /// <param name="Path">Its path inside the package, with <c>/</c> between folders.</param>
    /// <param name="OpenReadAsync">Opens its content.</param>
    internal sealed record PackageFile(string Path, Func<Task<Stream>> OpenReadAsync);
}
