namespace ToolBox.PluginSdk;

public interface IServiceBroker
{
    ValueTask<IServiceLease<T>> AcquireAsync<T>(
        string serviceKey,
        CancellationToken cancellationToken = default)
        where T : class;
}

public interface IServiceLease<out T> : IDisposable, IAsyncDisposable
    where T : class
{
    string ServiceKey { get; }

    string OwnerPluginId { get; }

    T Service { get; }

    bool IsReleased { get; }
}
