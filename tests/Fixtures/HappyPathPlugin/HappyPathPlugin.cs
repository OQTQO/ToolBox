using ToolBox.PluginSdk;

namespace HappyPathPlugin;

public sealed class HappyPathPlugin : IPlugin
{
    public string Id => "com.toolbox.happy-path";

    public ValueTask StartAsync(IPluginContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!string.Equals(context.PluginId, Id, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("HappyPathPlugin received the wrong plugin context.");
        }

        if (context.LifetimeScope is null
            || context.LifetimeScope.LifetimeToken != context.LifetimeToken)
        {
            throw new InvalidOperationException("HappyPathPlugin did not receive its lifetime scope.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.CompletedTask;
    }

    public ValueTask StopAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }
}
