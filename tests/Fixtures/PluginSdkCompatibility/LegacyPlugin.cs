using ToolBox.PluginSdk;

namespace LegacyPlugin;

public sealed class LegacyPlugin : IPlugin
{
    public string Id => "com.toolbox.plugin-sdk-compatibility";

    public ValueTask StartAsync(IPluginContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        if (!string.Equals(context.PluginId, Id, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("LegacyPlugin received the wrong plugin context.");
        }

        if (context.LifetimeScope.LifetimeToken != context.LifetimeToken
            || context.LifetimeScope.IsStopping)
        {
            throw new InvalidOperationException("LegacyPlugin received an invalid lifetime scope.");
        }

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
