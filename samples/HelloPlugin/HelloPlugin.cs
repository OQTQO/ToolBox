using System.Globalization;
using ToolBox.PluginSdk;

namespace HelloPlugin;

public sealed class HelloPlugin : IPlugin, IPluginUiProvider
{
    private Task? _backgroundTask;
    private CancellationTokenSource? _backgroundCancellation;
    private bool _disposed;
    private int _clickCount;

    public string Id => "com.toolbox.hello";

    public ValueTask StartAsync(IPluginContext context, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        _clickCount = 0;
        _backgroundCancellation = CancellationTokenSource.CreateLinkedTokenSource(context.LifetimeToken);
        context.LifetimeScope.Register(_backgroundCancellation);
        _backgroundTask = RunBackgroundLoopAsync(_backgroundCancellation.Token);
        context.LifetimeScope.Track(_backgroundTask);
        return ValueTask.CompletedTask;
    }

    public async ValueTask StopAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        if (_backgroundTask is not null)
        {
            _backgroundCancellation?.Cancel();
            try
            {
                await _backgroundTask.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (_backgroundCancellation?.IsCancellationRequested == true)
            {
            }

            _backgroundTask = null;
            _backgroundCancellation = null;
        }
    }

    public ValueTask DisposeAsync()
    {
        _disposed = true;
        _backgroundCancellation?.Cancel();
        _backgroundTask = null;
        _backgroundCancellation = null;
        return ValueTask.CompletedTask;
    }

    public PluginUiSnapshot GetSnapshot()
    {
        return new PluginUiSnapshot(
            "HelloPlugin is running. Use the button to verify the Worker control channel.",
            [
                new PluginUiValue("Button clicks", _clickCount.ToString(CultureInfo.InvariantCulture)),
                new PluginUiValue("Current process", "PluginWorker")
            ],
            [new PluginUiAction("hello", "Say hello")],
            null);
    }

    public ValueTask<PluginUiSnapshot> ExecuteAsync(
        string actionId,
        string? argument,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        if (!string.Equals(actionId, "hello", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Unknown HelloPlugin action '{actionId}'.");
        }

        _clickCount++;
        return ValueTask.FromResult(GetSnapshot());
    }

    public ValueTask<PluginUiSnapshot> HandleInputAsync(
        PluginInputEvent input,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(input);
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(GetSnapshot());
    }

    private static async Task RunBackgroundLoopAsync(CancellationToken lifetimeToken)
    {
        while (!lifetimeToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(30), lifetimeToken);
        }
    }
}
