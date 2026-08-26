using ToolBox.PluginSdk;

namespace ToolBox.Core.Plugins;

public sealed class PluginLifetimeScope : IPluginLifetimeScope, IDisposable, IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private List<ScopeCleanup>? _cleanups = new();
    private List<Task>? _backgroundTasks = new();
    private bool _stopping;
    private bool _cleanupStarted;
    private bool _disposed;

    public CancellationToken LifetimeToken => _lifetimeCancellation.Token;

    public bool IsStopping
    {
        get
        {
            lock (_gate)
            {
                return _stopping;
            }
        }
    }

    public void Cancel()
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            _stopping = true;
        }

        _lifetimeCancellation.Cancel(throwOnFirstException: false);
    }

    public void Track(Task backgroundTask)
    {
        ArgumentNullException.ThrowIfNull(backgroundTask);

        lock (_gate)
        {
            ThrowIfDisposed();

            if (_stopping)
            {
                throw new InvalidOperationException(
                    "The plugin lifetime scope is stopping and cannot accept new background tasks.");
            }

            _backgroundTasks!.Add(backgroundTask);
        }
    }

    public IDisposable Register(IDisposable resource)
    {
        ArgumentNullException.ThrowIfNull(resource);

        return Register(_ =>
        {
            resource.Dispose();
            return ValueTask.CompletedTask;
        });
    }

    public IDisposable Register(IAsyncDisposable resource)
    {
        ArgumentNullException.ThrowIfNull(resource);

        return Register(_ => resource.DisposeAsync());
    }

    public IDisposable Register(Func<CancellationToken, ValueTask> cleanup)
    {
        ArgumentNullException.ThrowIfNull(cleanup);

        var entry = new ScopeCleanup(cleanup);

        lock (_gate)
        {
            ThrowIfDisposed();

            if (_stopping)
            {
                throw new InvalidOperationException(
                    "The plugin lifetime scope is stopping and cannot accept new cleanup registrations.");
            }

            _cleanups!.Add(entry);
        }

        return new ScopeRegistration(() => Remove(entry));
    }

    public async ValueTask CleanupAsync(CancellationToken cancellationToken = default)
    {
        ScopeCleanup[] cleanups;
        Task[] backgroundTasks;

        lock (_gate)
        {
            ThrowIfDisposed();

            if (_cleanupStarted)
            {
                return;
            }

            _stopping = true;
            _cleanupStarted = true;
            cleanups = _cleanups!.ToArray();
            backgroundTasks = _backgroundTasks!.ToArray();
            _cleanups.Clear();
            _backgroundTasks.Clear();
        }

        var failures = new List<Exception>();

        for (var index = cleanups.Length - 1; index >= 0; index--)
        {
            try
            {
                await cleanups[index].Callback(cancellationToken)
                    .AsTask()
                    .WaitAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }

        if (backgroundTasks.Length > 0)
        {
            try
            {
                await Task.WhenAll(backgroundTasks)
                    .WaitAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }

        if (failures.Count > 0)
        {
            throw new AggregateException(
                "One or more plugin lifetime resources could not be cleaned up.",
                failures);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            Cancel();
            await CleanupAsync().ConfigureAwait(false);
        }
        finally
        {
            Dispose();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _cleanups?.Clear();
            _backgroundTasks?.Clear();
            _cleanups = null;
            _backgroundTasks = null;
        }

        _lifetimeCancellation.Dispose();
    }

    private void Remove(ScopeCleanup entry)
    {
        lock (_gate)
        {
            _cleanups?.Remove(entry);
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private sealed record ScopeCleanup(Func<CancellationToken, ValueTask> Callback);

    private sealed class ScopeRegistration : IDisposable
    {
        private readonly Action _remove;
        private int _disposed;

        public ScopeRegistration(Action remove)
        {
            _remove = remove;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _remove();
            }
        }
    }
}
