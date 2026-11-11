namespace OctaGrid.Application.Secrets;

/// <summary>
/// Supplies the key secrets are encrypted with. Each host keeps it where the platform protects it best
/// (the OS credential store on a desktop), never in the database it protects.
/// </summary>
public interface ISecretKeySource
{
    /// <summary>The 256-bit key, created and saved on first use.</summary>
    byte[] GetOrCreateKey();
}
