namespace ToolBox.PluginSdk;

public interface IPlugin : IAsyncDisposable
{
    string Id { get; }

    ValueTask StartAsync(IPluginContext context, CancellationToken cancellationToken);

    ValueTask StopAsync(CancellationToken cancellationToken);
}

public interface IPluginContext
{
    string PluginId { get; }

    CancellationToken LifetimeToken { get; }

    IPluginLifetimeScope LifetimeScope { get; }

    IResourceManager Resources { get; }

    IServiceBroker Services { get; }
}

public interface IPluginLifetimeScope
{
    CancellationToken LifetimeToken { get; }

    bool IsStopping { get; }

    void Track(Task backgroundTask);

    IDisposable Register(IDisposable resource);

    IDisposable Register(IAsyncDisposable resource);

    IDisposable Register(Func<CancellationToken, ValueTask> cleanup);
}
