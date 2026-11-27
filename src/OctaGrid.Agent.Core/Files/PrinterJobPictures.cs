using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OctaGrid.Extensions.Models;

namespace OctaGrid.Agent.Core.Files;

/// <summary>
/// The picture of the print OctaGrid last started on each printer, kept on disk so the card can show it while the
/// job runs, after a restart, and when the file is no longer on the printer. It is the picture of the plate that was
/// started, which a file with several plates does not otherwise give.
/// <para>
/// One record per printer, replaced by the next start. A job is the record's when its name is the started file's
/// name, with or without the print extension, as printers report it. Best effort: disk errors are logged and the
/// thumbnail is read from the printer instead.
/// </para>
/// </summary>
public sealed class PrinterJobPictures
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    // Longest first, so "a.gcode.3mf" loses ".gcode.3mf" and not only ".3mf".
    private static readonly string[] PrintExtensions = [".gcode.3mf", ".3mf", ".gcode", ".bgcode"];

    private readonly string _folder;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ILogger _logger;

    /// <param name="folder">The folder the records are kept in, two files per printer.</param>
    /// <param name="logger">Where disk errors go.</param>
    public PrinterJobPictures(string folder, ILogger? logger = null)
    {
        _folder = folder;
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>Whether a printer reporting <paramref name="jobName"/> is printing <paramref name="fileName"/>.</summary>
    public static bool IsJobOf(string fileName, string jobName)
    {
        if (string.Equals(fileName, jobName, StringComparison.Ordinal))
        {
            return true;
        }

        foreach (var extension in PrintExtensions)
        {
            if (fileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            {
                return string.Equals(fileName[..^extension.Length], jobName, StringComparison.Ordinal);
            }
        }

        return false;
    }

    /// <summary>Remembers <paramref name="picture"/> as the picture of the print just started from <paramref name="fileName"/>.</summary>
    public async Task RememberAsync(Guid printerId, string fileName, PrintThumbnail picture, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(_folder);
            await File.WriteAllBytesAsync(PicturePath(printerId), picture.Data, cancellationToken);
            await File.WriteAllTextAsync(
                RecordPath(printerId), JsonSerializer.Serialize(new Record(fileName, picture.ContentType), JsonOptions), cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not keep the picture of the print started on printer {PrinterId}", printerId);
            Delete(printerId);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// The picture of the print last started on the printer, when <paramref name="name"/> names it: the file's name
    /// or the job name the printer reports for it. Null otherwise.
    /// </summary>
    public async Task<PrintThumbnail?> FindAsync(Guid printerId, string name, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(RecordPath(printerId)))
            {
                return null;
            }

            var record = JsonSerializer.Deserialize<Record>(await File.ReadAllTextAsync(RecordPath(printerId), cancellationToken), JsonOptions);
            if (record is null || !IsJobOf(record.FileName, name))
            {
                return null;
            }

            var data = await File.ReadAllBytesAsync(PicturePath(printerId), cancellationToken);
            return data.Length > 0 ? new PrintThumbnail(record.ContentType, data) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            _logger.LogDebug(ex, "Could not read the picture of the print started on printer {PrinterId}", printerId);
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Deletes the records of printers not in <paramref name="printers"/>.</summary>
    public async Task PruneAsync(IReadOnlyCollection<Guid> printers, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(_folder))
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var keep = printers.ToHashSet();
            var recorded = Directory.EnumerateFiles(_folder)
                .Select(path => Guid.TryParseExact(Path.GetFileNameWithoutExtension(path), "N", out var id) ? id : Guid.Empty)
                .Where(id => id != Guid.Empty && !keep.Contains(id))
                .Distinct()
                .ToList();
            foreach (var printerId in recorded)
            {
                Delete(printerId);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogDebug(ex, "Could not clean up the pictures of started prints in {Folder}", _folder);
        }
        finally
        {
            _gate.Release();
        }
    }

    private void Delete(Guid printerId)
    {
        try
        {
            File.Delete(RecordPath(printerId));
            File.Delete(PicturePath(printerId));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogDebug(ex, "Could not delete the picture of the print started on printer {PrinterId}", printerId);
        }
    }

    private string RecordPath(Guid printerId) => Path.Combine(_folder, printerId.ToString("N") + ".json");

    private string PicturePath(Guid printerId) => Path.Combine(_folder, printerId.ToString("N") + ".img");

    private sealed record Record(string FileName, string ContentType);
}
