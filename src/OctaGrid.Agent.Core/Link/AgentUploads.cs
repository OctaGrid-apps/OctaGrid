using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OctaGrid.Extensions.Models.Exceptions;

namespace OctaGrid.Agent.Core.Link;

/// <summary>
/// Receives files that are sent to the agent in pieces (a print file, an extension package) and keeps each
/// in a temporary file until the request that uses it arrives. Uploads are bounded in size and forgotten
/// when nothing arrives for a while, so a sender that stops halfway leaves nothing behind.
/// </summary>
public sealed class AgentUploads : IDisposable
{
    /// <summary>The most bytes accepted in one piece.</summary>
    public const int MaxChunkSize = 64 * 1024;

    /// <summary>The most bytes one upload may hold unless the host sets another limit.</summary>
    public const long DefaultMaxBytes = 1024L * 1024 * 1024;

    private static readonly TimeSpan DefaultIdleTimeout = TimeSpan.FromMinutes(10);

    private readonly string _folder;
    private readonly TimeSpan _idleTimeout;
    private readonly ILogger<AgentUploads> _logger;
    private readonly long _maxBytes;
    private readonly TimeProvider _time;
    private readonly ConcurrentDictionary<Guid, Upload> _uploads = new();

    /// <param name="folder">Where the temporary files go. Created when the first upload starts.</param>
    /// <param name="maxBytes">The most bytes one upload may hold.</param>
    /// <param name="idleTimeout">How long an upload may receive nothing before it is discarded.</param>
    /// <param name="timeProvider">The clock, for tests.</param>
    /// <param name="logger">Optional logger.</param>
    public AgentUploads(
        string folder,
        long maxBytes = DefaultMaxBytes,
        TimeSpan? idleTimeout = null,
        TimeProvider? timeProvider = null,
        ILogger<AgentUploads>? logger = null)
    {
        _folder = folder;
        _maxBytes = maxBytes;
        _idleTimeout = idleTimeout ?? DefaultIdleTimeout;
        _time = timeProvider ?? TimeProvider.System;
        _logger = logger ?? NullLogger<AgentUploads>.Instance;
    }

    /// <summary>Starts an upload.</summary>
    /// <param name="length">The total size when the sender knows it, so an upload that is too large is refused before it is sent.</param>
    /// <returns>The id the pieces and the request that uses the upload refer to.</returns>
    /// <exception cref="ValidationException">The announced size is over the limit.</exception>
    public Guid Begin(long? length)
    {
        DiscardIdleUploads();

        if (length < 0 || length > _maxBytes)
        {
            throw new ValidationException("upload", $"The file is too large. The limit is {_maxBytes / (1024 * 1024)} MB.");
        }

        Directory.CreateDirectory(_folder);
        var id = Guid.NewGuid();
        var path = Path.Combine(_folder, $"{id:N}.upload");
        var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        _uploads[id] = new Upload(path, stream, _time.GetUtcNow());
        return id;
    }

    /// <summary>Adds the next piece to an upload.</summary>
    /// <exception cref="ValidationException">The upload is unknown, the piece is out of order or too large, or the upload grew over the limit.</exception>
    public async Task AppendAsync(Guid uploadId, int sequence, ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
    {
        var upload = Find(uploadId);
        await upload.Gate.WaitAsync(cancellationToken);
        try
        {
            if (upload.Stream is null)
            {
                throw Unknown();
            }

            if (sequence != upload.NextSequence)
            {
                throw new ValidationException("upload", $"Expected piece {upload.NextSequence} of the upload but got {sequence}.");
            }

            if (data.Length > MaxChunkSize)
            {
                throw new ValidationException("upload", $"A piece of an upload may hold at most {MaxChunkSize} bytes.");
            }

            if (upload.Length + data.Length > _maxBytes)
            {
                throw new ValidationException("upload", $"The file is too large. The limit is {_maxBytes / (1024 * 1024)} MB.");
            }

            await upload.Stream.WriteAsync(data, cancellationToken);
            upload.Length += data.Length;
            upload.NextSequence++;
            upload.LastActivity = _time.GetUtcNow();
        }
        finally
        {
            upload.Gate.Release();
        }
    }

    /// <summary>
    /// Ends an upload and hands over the file that holds it. The caller disposes the result when it is done,
    /// which deletes the file.
    /// </summary>
    /// <param name="uploadId">The upload.</param>
    /// <param name="extension">
    /// The extension the file gets, with the dot, when what reads it decides by file name (an extension package).
    /// </param>
    /// <param name="cancellationToken">Cancels waiting for a piece that is still being written.</param>
    /// <exception cref="ValidationException">The upload is unknown.</exception>
    public async Task<ReceivedUpload> CompleteAsync(Guid uploadId, string? extension, CancellationToken cancellationToken)
    {
        if (!_uploads.TryRemove(uploadId, out var upload))
        {
            throw Unknown();
        }

        await upload.Gate.WaitAsync(cancellationToken);
        try
        {
            await upload.Stream!.DisposeAsync();
            upload.Stream = null;
        }
        finally
        {
            upload.Gate.Release();
        }

        var path = upload.Path;
        if (!string.IsNullOrEmpty(extension))
        {
            path = Path.ChangeExtension(upload.Path, extension);
            File.Move(upload.Path, path);
        }

        return new ReceivedUpload(path, upload.Length, _logger);
    }

    /// <summary>Discards an upload. Unknown ids are ignored, so aborting twice is harmless.</summary>
    public void Abort(Guid uploadId)
    {
        if (_uploads.TryRemove(uploadId, out var upload))
        {
            Discard(upload);
        }
    }

    public void Dispose()
    {
        foreach (var id in _uploads.Keys)
        {
            Abort(id);
        }
    }

    private static ValidationException Unknown() =>
        new("upload", "The upload is unknown. It may have been finished, cancelled, or discarded after a long pause.");

    private Upload Find(Guid uploadId) => _uploads.TryGetValue(uploadId, out var upload) ? upload : throw Unknown();

    private void DiscardIdleUploads()
    {
        var cutoff = _time.GetUtcNow() - _idleTimeout;
        foreach (var (id, upload) in _uploads)
        {
            if (upload.LastActivity < cutoff && _uploads.TryRemove(id, out var idle))
            {
                _logger.LogInformation("Discarding upload {UploadId}: nothing arrived for {IdleMinutes} minutes", id, _idleTimeout.TotalMinutes);
                Discard(idle);
            }
        }
    }

    private void Discard(Upload upload)
    {
        try
        {
            upload.Stream?.Dispose();
            upload.Stream = null;
            File.Delete(upload.Path);
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Could not delete the temporary file of a discarded upload");
        }
    }

    private sealed class Upload
    {
        public Upload(string path, FileStream stream, DateTimeOffset now)
        {
            Path = path;
            Stream = stream;
            LastActivity = now;
        }

        public SemaphoreSlim Gate { get; } = new(1, 1);

        public DateTimeOffset LastActivity { get; set; }

        public long Length { get; set; }

        public int NextSequence { get; set; }

        public string Path { get; }

        public FileStream? Stream { get; set; }
    }
}
