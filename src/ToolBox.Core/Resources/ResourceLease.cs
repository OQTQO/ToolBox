using ToolBox.PluginSdk;

namespace ToolBox.Core.Resources;

public sealed class ResourceLease : IResourceLease
{
    private readonly Action _release;
    private int _released;

    internal ResourceLease(
        ResourceKey key,
        ResourceAccessMode accessMode,
        string ownerPluginId,
        Action release)
    {
        Key = key;
        AccessMode = accessMode;
        OwnerPluginId = ownerPluginId;
        _release = release ?? throw new ArgumentNullException(nameof(release));
    }

    public ResourceKey Key { get; }

    public ResourceAccessMode AccessMode { get; }

    public string OwnerPluginId { get; }

    public bool IsReleased => Volatile.Read(ref _released) != 0;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _released, 1) != 0)
        {
            return;
        }

        _release();
    }
}
