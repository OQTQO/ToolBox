using System.Diagnostics.CodeAnalysis;
using ToolBox.PluginSdk;

namespace ToolBox.Core.Services;

public sealed class ServiceBroker : IServiceBroker, IAsyncDisposable, IDisposable
{
    public static readonly TimeSpan DefaultIdleTimeout = TimeSpan.FromSeconds(1);

    private readonly object _gate = new();
    private readonly Dictionary<string, IServiceEntry> _entries = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private Task? _disposeTask;
    private Exception? _lastStopError;
    private bool _disposed;

    public Exception? LastStopError
    {
        get
        {
            lock (_gate)
            {
                return _lastStopError;
            }
        }
    }

    public void Register<T>(
        string serviceKey,
        Func<CancellationToken, ValueTask<T>> startAsync,
        Func<T, CancellationToken, ValueTask>? stopAsync = null,
        TimeSpan? idleTimeout = null)
        where T : class
    {
        serviceKey = NormalizeKey(serviceKey, nameof(serviceKey));
        ArgumentNullException.ThrowIfNull(startAsync);

        var actualIdleTimeout = idleTimeout ?? DefaultIdleTimeout;
        ValidateIdleTimeout(actualIdleTimeout);

        var entry = new ServiceEntry<T>(
            this,
            serviceKey,
            startAsync,
            stopAsync ?? DefaultStopAsync,
            actualIdleTimeout);

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (!_entries.TryAdd(serviceKey, entry))
            {
                throw new InvalidOperationException(
                    $"Service '{serviceKey}' is already registered.");
            }
        }
    }

    public IServiceBroker Bind(
        string ownerPluginId,
        IPluginLifetimeScope? lifetimeScope = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerPluginId);
        return new BoundServiceBroker(this, ownerPluginId.Trim(), lifetimeScope);
    }

    public ValueTask<IServiceLease<T>> AcquireAsync<T>(
        string serviceKey,
        CancellationToken cancellationToken = default)
        where T : class
    {
        return AcquireCoreAsync<T>("host", serviceKey, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        Task disposeTask;

        lock (_gate)
        {
            _disposeTask ??= CreateDisposeTask();
            disposeTask = _disposeTask;
        }

        await disposeTask.ConfigureAwait(false);
    }

    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    public int GetReferenceCount(string serviceKey)
    {
        serviceKey = NormalizeKey(serviceKey, nameof(serviceKey));

        lock (_gate)
        {
            return _entries.TryGetValue(serviceKey, out var entry)
                ? entry.ReferenceCount
                : 0;
        }
    }

    public bool IsStarted(string serviceKey)
    {
        serviceKey = NormalizeKey(serviceKey, nameof(serviceKey));

        lock (_gate)
        {
            return _entries.TryGetValue(serviceKey, out var entry)
                && entry.IsStarted;
        }
    }

    internal CancellationToken LifetimeToken => _lifetimeCancellation.Token;

    internal bool IsDisposed
    {
        get
        {
            lock (_gate)
            {
                return _disposed;
            }
        }
    }

    internal void RecordStopFailure(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        lock (_gate)
        {
            _lastStopError = exception;
        }
    }

    private async ValueTask<IServiceLease<T>> AcquireCoreAsync<T>(
        string ownerPluginId,
        string serviceKey,
        CancellationToken cancellationToken)
        where T : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerPluginId);
        serviceKey = NormalizeKey(serviceKey, nameof(serviceKey));

        IServiceEntry entry;

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (!_entries.TryGetValue(serviceKey, out entry!))
            {
                throw new InvalidOperationException(
                    $"Service '{serviceKey}' is not registered.");
            }

            if (entry.ServiceType != typeof(T))
            {
                throw new InvalidOperationException(
                    $"Service '{serviceKey}' is registered as '{entry.ServiceType.FullName}', not '{typeof(T).FullName}'.");
            }
        }

        return await ((ServiceEntry<T>)entry)
            .AcquireAsync(ownerPluginId, cancellationToken)
            .ConfigureAwait(false);
    }

    private Task CreateDisposeTask()
    {
        IServiceEntry[] entries;

        lock (_gate)
        {
            if (_disposed)
            {
                return Task.CompletedTask;
            }

            _disposed = true;
            _lifetimeCancellation.Cancel();
            entries = _entries.Values.ToArray();
        }

        return DisposeEntriesAsync(entries);
    }

    private async Task DisposeEntriesAsync(IReadOnlyList<IServiceEntry> entries)
    {
        var stopTasks = entries.Select(entry => entry.StopNowAsync()).ToArray();

        try
        {
            await Task.WhenAll(stopTasks).ConfigureAwait(false);
        }
        finally
        {
            _lifetimeCancellation.Dispose();
        }
    }

    private static async ValueTask DefaultStopAsync<T>(T service, CancellationToken cancellationToken)
        where T : class
    {
        cancellationToken.ThrowIfCancellationRequested();

        switch (service)
        {
            case IAsyncDisposable asyncDisposable:
                await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                break;
            case IDisposable disposable:
                disposable.Dispose();
                break;
        }
    }

    private static string NormalizeKey(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return value.Trim();
    }

    private static void ValidateIdleTimeout(TimeSpan idleTimeout)
    {
        if (idleTimeout < TimeSpan.Zero
            || idleTimeout == Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentOutOfRangeException(
                nameof(idleTimeout),
                idleTimeout,
                "Idle timeout must be non-negative and finite.");
        }
    }

    private interface IServiceEntry
    {
        Type ServiceType { get; }

        int ReferenceCount { get; }

        bool IsStarted { get; }

        Task StopNowAsync();
    }

    [SuppressMessage(
        "Design",
        "CA1001:TypesThatOwnDisposableFieldsShouldBeDisposable",
        Justification = "The entry gate is owned by the broker lifetime and is intentionally retained until process shutdown.")]
    private sealed class ServiceEntry<T> : IServiceEntry
        where T : class
    {
        private readonly ServiceBroker _broker;
        private readonly string _serviceKey;
        private readonly Func<CancellationToken, ValueTask<T>> _startAsync;
        private readonly Func<T, CancellationToken, ValueTask> _stopAsync;
        private readonly TimeSpan _idleTimeout;
        private readonly SemaphoreSlim _gate = new(1, 1);
        private T? _service;
        private Task<T>? _startTask;
        private Task? _stopTask;
        private CancellationTokenSource? _idleCancellation;
        private Task? _idleTask;
        private int _referenceCount;

        public ServiceEntry(
            ServiceBroker broker,
            string serviceKey,
            Func<CancellationToken, ValueTask<T>> startAsync,
            Func<T, CancellationToken, ValueTask> stopAsync,
            TimeSpan idleTimeout)
        {
            _broker = broker;
            _serviceKey = serviceKey;
            _startAsync = startAsync;
            _stopAsync = stopAsync;
            _idleTimeout = idleTimeout;
        }

        public Type ServiceType => typeof(T);

        public int ReferenceCount => Volatile.Read(ref _referenceCount);

        public bool IsStarted => Volatile.Read(ref _referenceCount) >= 0
            && _service is not null;

        public async ValueTask<IServiceLease<T>> AcquireAsync(
            string ownerPluginId,
            CancellationToken cancellationToken)
        {
            while (true)
            {
                Task<T>? startTask = null;
                Task? stopTask = null;

                await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

                try
                {
                    _broker.ThrowIfDisposed();
                    CancelIdleTimerLocked();

                    if (_service is not null)
                    {
                        _referenceCount++;
                        return CreateLease(ownerPluginId, _service);
                    }

                    if (_stopTask is not null)
                    {
                        stopTask = _stopTask;
                    }
                    else
                    {
                        _startTask ??= StartCoreAsync();
                        startTask = _startTask;
                    }
                }
                finally
                {
                    _gate.Release();
                }

                if (stopTask is not null)
                {
                    await stopTask.WaitAsync(cancellationToken).ConfigureAwait(false);
                    continue;
                }

                await startTask!.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        public async Task StopNowAsync()
        {
            while (true)
            {
                Task<T>? startTask = null;
                Task? stopTask = null;

                await _gate.WaitAsync().ConfigureAwait(false);

                try
                {
                    CancelIdleTimerLocked();

                    if (_stopTask is not null)
                    {
                        stopTask = _stopTask;
                    }
                    else if (_startTask is not null)
                    {
                        startTask = _startTask;
                    }
                    else if (_service is not null)
                    {
                        _referenceCount = 0;
                        _stopTask = StopCoreAsync(_service);
                        stopTask = _stopTask;
                        _service = null;
                    }
                    else
                    {
                        return;
                    }
                }
                finally
                {
                    _gate.Release();
                }

                if (startTask is not null)
                {
                    try
                    {
                        await startTask.ConfigureAwait(false);
                    }
                    catch
                    {
                        return;
                    }

                    continue;
                }

                await stopTask!.ConfigureAwait(false);
                return;
            }
        }

        private async Task<T> StartCoreAsync()
        {
            T service;

            try
            {
                service = await _startAsync(_broker.LifetimeToken).ConfigureAwait(false)
                    ?? throw new InvalidOperationException(
                        $"Service '{_serviceKey}' returned a null instance from its start callback.");
            }
            catch
            {
                await _gate.WaitAsync().ConfigureAwait(false);
                try
                {
                    _startTask = null;
                }
                finally
                {
                    _gate.Release();
                }

                throw;
            }

            var shouldDisposeImmediately = false;

            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                _startTask = null;

                if (_broker.IsDisposed)
                {
                    shouldDisposeImmediately = true;
                }
                else
                {
                    _service = service;

                    if (_referenceCount == 0)
                    {
                        ScheduleIdleTimerLocked();
                    }
                }
            }
            finally
            {
                _gate.Release();
            }

            if (shouldDisposeImmediately)
            {
                await _stopAsync(service, CancellationToken.None).ConfigureAwait(false);
                throw new ObjectDisposedException(nameof(ServiceBroker));
            }

            return service;
        }

        private async Task StopIfIdleAsync()
        {
            Task? stopTask = null;

            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_referenceCount != 0 || _service is null || _stopTask is not null)
                {
                    return;
                }

                var service = _service;
                _service = null;
                _stopTask = StopCoreAsync(service);
                stopTask = _stopTask;
            }
            finally
            {
                _gate.Release();
            }

            await stopTask!.ConfigureAwait(false);
        }

        private async Task StopCoreAsync(T service)
        {
            try
            {
                await _stopAsync(service, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                _broker.RecordStopFailure(exception);
                throw;
            }
            finally
            {
                await _gate.WaitAsync().ConfigureAwait(false);
                try
                {
                    _stopTask = null;
                }
                finally
                {
                    _gate.Release();
                }
            }
        }

        private ServiceLease<T> CreateLease(string ownerPluginId, T service)
        {
            return new ServiceLease<T>(
                _serviceKey,
                ownerPluginId,
                service,
                () => ReleaseAsync());
        }

        private async ValueTask ReleaseAsync()
        {
            await _gate.WaitAsync().ConfigureAwait(false);

            try
            {
                if (_referenceCount == 0)
                {
                    return;
                }

                _referenceCount--;

                if (_referenceCount == 0 && _service is not null)
                {
                    ScheduleIdleTimerLocked();
                }
            }
            finally
            {
                _gate.Release();
            }
        }

        private void ScheduleIdleTimerLocked()
        {
            if (_idleCancellation is not null || _service is null || _referenceCount != 0)
            {
                return;
            }

            var cancellation = new CancellationTokenSource();
            _idleCancellation = cancellation;
            _idleTask = RunIdleStopAsync(cancellation);
        }

        private async Task RunIdleStopAsync(CancellationTokenSource cancellation)
        {
            try
            {
                await Task.Delay(_idleTimeout, cancellation.Token).ConfigureAwait(false);

                if (!cancellation.IsCancellationRequested)
                {
                    await StopIfIdleAsync().ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                // A new lease arrived before the idle timeout elapsed.
            }
            catch (Exception exception)
            {
                _broker.RecordStopFailure(exception);
            }
            finally
            {
                await _gate.WaitAsync().ConfigureAwait(false);
                try
                {
                    if (ReferenceEquals(_idleCancellation, cancellation))
                    {
                        _idleCancellation = null;
                        _idleTask = null;
                    }
                }
                finally
                {
                    _gate.Release();
                }

                cancellation.Dispose();
            }
        }

        private void CancelIdleTimerLocked()
        {
            var cancellation = _idleCancellation;
            _idleCancellation = null;
            _idleTask = null;
            cancellation?.Cancel();
        }
    }

    private sealed class BoundServiceBroker : IServiceBroker
    {
        private readonly ServiceBroker _broker;
        private readonly string _ownerPluginId;
        private readonly IPluginLifetimeScope? _lifetimeScope;

        public BoundServiceBroker(
            ServiceBroker broker,
            string ownerPluginId,
            IPluginLifetimeScope? lifetimeScope)
        {
            _broker = broker;
            _ownerPluginId = ownerPluginId;
            _lifetimeScope = lifetimeScope;
        }

        public async ValueTask<IServiceLease<T>> AcquireAsync<T>(
            string serviceKey,
            CancellationToken cancellationToken = default)
            where T : class
        {
            var lease = await _broker
                .AcquireCoreAsync<T>(_ownerPluginId, serviceKey, cancellationToken)
                .ConfigureAwait(false);

            if (_lifetimeScope is null)
            {
                return lease;
            }

            try
            {
                _lifetimeScope.Register((IAsyncDisposable)lease);
                return lease;
            }
            catch
            {
                await lease.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }
    }

    private void ThrowIfDisposed()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
        }
    }
}
