using System.Net.Http.Headers;
using System.Net.Mime;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OctaGrid.Protocol.Printers;
using StreamJsonRpc;

namespace OctaGrid.Connectors.Moonraker;

/// <summary>
/// Outbound file-metadata and thumbnail-download calls for one Moonraker host. Kept separate from
/// MoonrakerClient (connection lifecycle) and MoonrakerListener (inbound notifications) so neither
/// grows to also own HTTP and file-path logic. Knows nothing about PrinterTelemetry — it only
/// parses Moonraker's response and returns plain values; the caller decides what to do with them.
/// </summary>
internal sealed class MoonrakerFileClient(string host, ILogger<MoonrakerFileClient>? logger = null)
{
    // Shared across every call this instance makes (and safe to share across devices too, since
    // HttpClient is designed for concurrent reuse) — never `new HttpClient()` per request.
    private static readonly HttpClient Http = new();

    private static readonly TimeSpan MetadataTimeout = TimeSpan.FromSeconds(10);
    private readonly ILogger<MoonrakerFileClient> _logger = logger ?? NullLogger<MoonrakerFileClient>.Instance;

    public readonly record struct FileMetadataResult(string FileName, int FileSize, string? MaterialType, double? NozzleDiameter, double? EstimatedTimeSeconds, string? ThumbnailFilePath);

    public static async Task<FileMetadataResult> GetFileMetadataAsync(
        JsonRpc rpc,
        string fileName,
        CancellationToken cancellationToken,
        ILogger? logger = null)
    {
        var call = rpc.InvokeWithParameterObjectAsync<JsonElement>(
            "server.files.metadata", new { filename = fileName }, CancellationToken.None);

        JsonElement metadata;
        try
        {
            metadata = await call.WaitAsync(MetadataTimeout, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            _ = call.ContinueWith(static t => _ = t.Exception, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
            logger?.LogWarning("Timed out querying Moonraker metadata for file '{FileName}'", fileName);
            return default;
        }
        catch (Exception ex) when (ex is ConnectionLostException or ObjectDisposedException or RemoteInvocationException)
        {
            logger?.LogDebug(ex, "Failed to query Moonraker metadata for file '{FileName}'", fileName);
            return default;
        }

        double? estimatedTime = metadata.TryGetProperty("estimated_time", out var est) && est.ValueKind == JsonValueKind.Number
            ? est.GetDouble()
            : null;

        var size = metadata.TryGetProperty("size", out var fileSize) && fileSize.ValueKind == JsonValueKind.Number
            ? fileSize.GetInt32()
            : 0;

        var materialType = metadata.TryGetProperty("filament_type", out var filament) && filament.ValueKind == JsonValueKind.String
            ? filament.GetString()
            : null;

        double? nozzle_diameter = metadata.TryGetProperty("nozzle_diameter", out var nozzle) && nozzle.ValueKind == JsonValueKind.Number
            ? nozzle.GetDouble()
            : null;

        var thumbnailPath = GetBestThumbnailPath(metadata);
        return new FileMetadataResult(fileName, size, materialType, nozzle_diameter, estimatedTime, thumbnailPath);
    }

    public async Task<PrintThumbnail?> DownloadBestThumbnailAsync(string fileName, string thumbnailPath, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(fileName)?.Replace('\\', '/') ?? "";
        var fullPath = string.IsNullOrEmpty(directory) ? thumbnailPath : $"{directory}/{thumbnailPath}";

        var segments = fullPath.Split('/').Select(Uri.EscapeDataString);
        var url = $"http://{host}/server/files/gcodes/{string.Join('/', segments)}";

        try
        {
            using var response = await Http.GetAsync(url, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogDebug("Thumbnail download returned HTTP {StatusCode} for '{FileName}' on {Host}", (int)response.StatusCode, fileName, host);
                return null;
            }

            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            var contentType = response.Content.Headers.ContentType?.MediaType ?? "image/png";
            return new PrintThumbnail(contentType, bytes);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogDebug(ex, "Failed to download thumbnail for '{FileName}' from {Host}", fileName, host);
            return null;
        }
    }

    public async Task UploadFileAsync(Stream stream, string fileName, CancellationToken cancellationToken)
    {
        var url = $"http://{host}/server/files/upload";
        _logger.LogInformation("Uploading file '{FileName}' to Moonraker host {Host}", fileName, host);

        try
        {
            using var multipartContent = new MultipartFormDataContent();

            using var streamContent = new StreamContent(stream);

            streamContent.Headers.ContentType = new MediaTypeHeaderValue(MediaTypeNames.Application.Octet);
            multipartContent.Add(streamContent, "file", fileName);

            using var response = await Http.PostAsync(url, multipartContent, cancellationToken).ConfigureAwait(false);

            response.EnsureSuccessStatusCode();
            _logger.LogInformation("Uploaded file '{FileName}' to Moonraker host {Host} successfully", fileName, host);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to upload file '{FileName}' to Moonraker host {Host}", fileName, host);
            throw;
        }
    }

    private static string? GetBestThumbnailPath(JsonElement metadata)
    {
        if (!metadata.TryGetProperty("thumbnails", out var thumbnails) || thumbnails.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        JsonElement? best = null;
        var bestArea = -1L;
        foreach (var thumb in thumbnails.EnumerateArray())
        {
            if (!thumb.TryGetProperty("width", out var w) || !thumb.TryGetProperty("height", out var h))
            {
                continue;
            }

            var area = (long)w.GetInt32() * h.GetInt32();
            if (area > bestArea)
            {
                bestArea = area;
                best = thumb;
            }
        }

        if (best is not { } chosen || !chosen.TryGetProperty("relative_path", out var relativeProp))
        {
            return null;
        }

        return relativeProp.GetString();
    }
}
