using ToolBox.PluginSdk;

namespace HelloPlugin;

public sealed class HelloPlugin : IPlugin
{
    private Task? _backgroundTask;
    private CancellationTokenSource? _backgroundCancellation;
    private bool _disposed;

    public string Id => "com.toolbox.hello";

    public ValueTask StartAsync(IPluginContext context, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

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

    private static async Task RunBackgroundLoopAsync(CancellationToken lifetimeToken)
    {
        while (!lifetimeToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(30), lifetimeToken);
        }
    }
}
