using ToolBox.PluginSdk;

namespace CrashPlugin;

public sealed class CrashPlugin : IPlugin
{
    public string Id => "com.toolbox.crash";

    public ValueTask StartAsync(IPluginContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        throw new InvalidOperationException(
            "CrashPlugin intentionally failed during startup.");
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
