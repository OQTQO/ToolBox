using ToolBox.Core.Plugins;
using ToolBox.Core.Plugins.Worker;
using ToolBox.PluginSdk;

namespace ToolBox.PluginWorker;

internal sealed class UiUpdateSubscription : IDisposable
{
    private readonly IPluginUiUpdateSource _source;
    private readonly EventHandler<PluginUiSnapshotUpdatedEventArgs> _handler;
    private bool _disposed;

    private UiUpdateSubscription(
        IPluginUiUpdateSource source,
        EventHandler<PluginUiSnapshotUpdatedEventArgs> handler)
    {
        _source = source;
        _handler = handler;
        _source.SnapshotUpdated += _handler;
    }

    public static UiUpdateSubscription? Attach(
        LoadedInProcessPlugin plugin,
        string launchId,
        WorkerMessageWriter writer)
    {
        ArgumentNullException.ThrowIfNull(plugin);
        ArgumentException.ThrowIfNullOrWhiteSpace(launchId);
        ArgumentNullException.ThrowIfNull(writer);

        var source = plugin.GetCapability<IPluginUiUpdateSource>();
        if (source is null)
        {
            return null;
        }

        EventHandler<PluginUiSnapshotUpdatedEventArgs> handler = (_, args) =>
        {
            try
            {
                writer.TryEnqueueLatestUiUpdate(
                    WorkerProtocol.CreateEvent(
                        launchId,
                        "ui.updated",
                        WorkerProtocol.SerializePayload(args.Snapshot)));
            }
            catch (Exception)
            {
                // The Worker is shutting down or the plugin supplied an invalid
                // event payload. The next request or process boundary reports it.
            }
        };

        return new UiUpdateSubscription(source, handler);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _source.SnapshotUpdated -= _handler;
    }
}
