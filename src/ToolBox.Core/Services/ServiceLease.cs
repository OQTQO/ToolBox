using ToolBox.PluginSdk;

namespace ToolBox.Core.Services;

public sealed class ServiceLease<T> : IServiceLease<T>
    where T : class
{
    private readonly Func<ValueTask> _releaseAsync;
    private int _released;

    internal ServiceLease(
        string serviceKey,
        string ownerPluginId,
        T service,
        Func<ValueTask> releaseAsync)
    {
        ServiceKey = serviceKey;
        OwnerPluginId = ownerPluginId;
        Service = service ?? throw new ArgumentNullException(nameof(service));
        _releaseAsync = releaseAsync ?? throw new ArgumentNullException(nameof(releaseAsync));
    }

    public string ServiceKey { get; }

    public string OwnerPluginId { get; }

    public T Service { get; }

    public bool IsReleased => Volatile.Read(ref _released) != 0;

    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _released, 1) != 0)
        {
            return ValueTask.CompletedTask;
        }

        return _releaseAsync();
    }
}
