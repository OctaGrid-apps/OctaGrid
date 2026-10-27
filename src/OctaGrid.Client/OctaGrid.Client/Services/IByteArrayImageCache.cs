namespace OctaGrid.Client.Services;

public interface IByteArrayImageCache
{
    void Add(string key, byte[] bytes);

    void Clear();

    void Remove(string key);

    bool TryGetBytes(string key, out byte[]? bytes);
}
