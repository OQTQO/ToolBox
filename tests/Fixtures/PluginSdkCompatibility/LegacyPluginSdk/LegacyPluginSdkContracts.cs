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
}

public interface IPluginLifetimeScope
{
    CancellationToken LifetimeToken { get; }

    bool IsStopping { get; }
}
