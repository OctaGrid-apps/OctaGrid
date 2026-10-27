using System.Threading.Channels;
using OctaGrid.Protocol.Messaging;

namespace OctaGrid.Client.Local;

internal sealed class EntityChangeFeed
{
    private readonly Channel<EntityChanged> _channel = Channel.CreateUnbounded<EntityChanged>();

    public IAsyncEnumerable<EntityChanged> ReadAllAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);

    public void Write(EntityChanged change) => _channel.Writer.TryWrite(change);
}
