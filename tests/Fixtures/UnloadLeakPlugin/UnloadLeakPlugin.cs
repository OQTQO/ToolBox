using System.Globalization;
using System.Runtime.InteropServices;
using ToolBox.PluginSdk;

namespace UnloadLeakPlugin;

public sealed class UnloadLeakPlugin : IPlugin
{
    private GCHandle _leakHandle;

    public string Id => "com.toolbox.unload-leak";

    public ValueTask StartAsync(IPluginContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        _leakHandle = GCHandle.Alloc(this, GCHandleType.Normal);

        var pluginDirectory = Path.GetDirectoryName(typeof(UnloadLeakPlugin).Assembly.Location)
            ?? throw new InvalidOperationException("The plugin directory is not available.");
        File.WriteAllText(
            Path.Combine(pluginDirectory, "leak.handle"),
            GCHandle.ToIntPtr(_leakHandle).ToInt64().ToString(CultureInfo.InvariantCulture));

        return ValueTask.CompletedTask;
    }

    public ValueTask StopAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        // Deliberately retain the GCHandle. The test frees it after observing the
        // RestartRequired unload result so the process does not keep a permanent leak.
        return ValueTask.CompletedTask;
    }
}
