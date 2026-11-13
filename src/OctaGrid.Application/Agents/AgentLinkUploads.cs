using OctaGrid.Protocol.AgentLink.Requests;

namespace OctaGrid.Application.Agents;

/// <summary>Sends file content to an agent in pieces, the only way content can cross the link.</summary>
public static class AgentLinkUploads
{
    /// <summary>
    /// Uploads a stream to an agent. The upload stays on the agent until a request uses it (for example
    /// <see cref="StorePrinterFileRequest"/> or <see cref="InstallExtensionRequest"/>). When sending fails or is
    /// cancelled, the agent is told to discard what it received.
    /// </summary>
    /// <param name="link">The link to the agent.</param>
    /// <param name="agentId">The agent that receives the content.</param>
    /// <param name="content">The content, read from its current position to the end.</param>
    /// <param name="cancellationToken">Cancels the upload.</param>
    /// <returns>The id to pass to the request that uses the upload.</returns>
    public static async Task<Guid> UploadAsync(this IAgentLink link, Guid agentId, Stream content, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(link);
        ArgumentNullException.ThrowIfNull(content);

        long? length = content.CanSeek ? content.Length - content.Position : null;
        var started = await link.SendAsync(agentId, new BeginUploadRequest(length), cancellationToken);
        try
        {
            var buffer = new byte[started.MaxChunkSize];
            var sequence = 0;
            int read;
            while ((read = await content.ReadAtLeastAsync(buffer, buffer.Length, throwOnEndOfStream: false, cancellationToken)) > 0)
            {
                // Each piece gets its own array: the in-process link hands it over without copying.
                await link.SendAsync(agentId, new UploadChunkRequest(started.UploadId, sequence++, buffer[..read]), cancellationToken);
            }

            return started.UploadId;
        }
        catch
        {
            await AbortQuietlyAsync(link, agentId, started.UploadId);
            throw;
        }
    }

    /// <summary>Tells the agent to discard an upload that will not be used. Never throws: it is cleanup.</summary>
    public static async Task AbortQuietlyAsync(IAgentLink link, Guid agentId, Guid uploadId)
    {
        try
        {
            await link.SendAsync(agentId, new AbortUploadRequest(uploadId), CancellationToken.None);
        }
        catch (Exception)
        {
            // The agent discards abandoned uploads by itself after a while, so a failed abort only delays that.
        }
    }
}
