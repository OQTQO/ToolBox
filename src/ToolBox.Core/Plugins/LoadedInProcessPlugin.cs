using ToolBox.Core.Lifetime;
using ToolBox.Core.Resources;
using ToolBox.Core.Services;
using ToolBox.PluginSdk;

namespace ToolBox.Core.Plugins;

public sealed class LoadedInProcessPlugin : IAsyncDisposable
{
    private PluginAssemblyLoadContext? _loadContext;
    private IPlugin? _plugin;
    private PluginContext? _pluginContext;
    private readonly PluginLifetimeScope _lifetimeScope;
    private readonly ResourceManager _resourceManager;
    private readonly ServiceBroker _serviceBroker;
    private PluginState _state;
    private bool _disposed;

    internal LoadedInProcessPlugin(
        DiscoveredPlugin discoveredPlugin,
        PluginAssemblyLoadContext loadContext,
        IPlugin plugin,
        ResourceManager resourceManager,
        ServiceBroker serviceBroker)
    {
        Discovered = discoveredPlugin ?? throw new ArgumentNullException(nameof(discoveredPlugin));
        _loadContext = loadContext ?? throw new ArgumentNullException(nameof(loadContext));
        _plugin = plugin ?? throw new ArgumentNullException(nameof(plugin));
        _resourceManager = resourceManager ?? throw new ArgumentNullException(nameof(resourceManager));
        _serviceBroker = serviceBroker ?? throw new ArgumentNullException(nameof(serviceBroker));

        _lifetimeScope = new PluginLifetimeScope();
        _pluginContext = new PluginContext(
            Manifest.Id,
            _lifetimeScope,
            _resourceManager.Bind(Manifest.Id, _lifetimeScope),
            _serviceBroker.Bind(Manifest.Id, _lifetimeScope));
        _state = PluginState.CreateInstalled(Manifest).TransitionTo(PluginLifecycleState.Disabled);
        LoadContextReference = new WeakReference(loadContext);
    }

    public DiscoveredPlugin Discovered { get; }

    public PluginManifest Manifest => Discovered.Manifest;

    public PluginState State => _state;

    public WeakReference LoadContextReference { get; }

    public PluginLifetimeScope LifetimeScope => _lifetimeScope;

    public T? GetCapability<T>()
        where T : class
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _plugin as T;
    }

    public async ValueTask StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var plugin = _plugin ?? throw new PluginLoadException(
            "PLUGIN_INSTANCE_MISSING",
            "The plugin instance is no longer available.");
        var context = _pluginContext ?? throw new PluginLoadException(
            "PLUGIN_CONTEXT_MISSING",
            "The plugin context is no longer available.");

        _state = _state.TransitionTo(PluginLifecycleState.Starting);

        try
        {
            await plugin.StartAsync(context, cancellationToken).ConfigureAwait(false);
            _state = _state.TransitionTo(PluginLifecycleState.Running);
        }
        catch (Exception exception)
        {
            _state = _state.TransitionTo(
                PluginLifecycleState.Faulted,
                errorCode: "PLUGIN_START_FAILED",
                errorMessage: exception.Message);
            throw;
        }
    }

    public ValueTask StopAndUnloadAsync(CancellationToken cancellationToken = default)
    {
        return StopAndUnloadAsync(PluginShutdownOptions.Default, cancellationToken);
    }

    public async ValueTask StopAndUnloadAsync(
        PluginShutdownOptions options,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(options);

        if (_loadContext is null)
        {
            return;
        }

        if (_state.LifecycleState is not PluginLifecycleState.Running and not PluginLifecycleState.Disabled)
        {
            throw new PluginLoadException(
                "PLUGIN_STOP_STATE_INVALID",
                $"Plugin cannot be stopped from state '{_state.LifecycleState}'.");
        }

        IPlugin? plugin = _plugin;

        if (_state.LifecycleState == PluginLifecycleState.Running)
        {
            _state = _state.TransitionTo(PluginLifecycleState.Stopping);
        }

        using var deadline = ShutdownDeadline.Start(options, cancellationToken);

        try
        {
            _lifetimeScope.Cancel();
            deadline.ThrowIfExpired();

            if (plugin is not null && _state.LifecycleState == PluginLifecycleState.Stopping)
            {
                await plugin.StopAsync(deadline.Token)
                    .AsTask()
                    .WaitAsync(deadline.Token)
                    .ConfigureAwait(false);
            }

            await _lifetimeScope.CleanupAsync(deadline.Token)
                .AsTask()
                .WaitAsync(deadline.Token)
                .ConfigureAwait(false);

            if (plugin is not null)
            {
                await plugin.DisposeAsync()
                    .AsTask()
                    .WaitAsync(deadline.Token)
                    .ConfigureAwait(false);
            }

            _plugin = null;
            _pluginContext = null;
            plugin = null;
            _lifetimeScope.Dispose();

            var unloadReference = UnloadLoadContext();

            if (!await WaitForUnloadAsync(unloadReference, deadline).ConfigureAwait(false))
            {
                throw new PluginLoadException(
                    "PLUGIN_ALC_UNLOAD_FAILED",
                    "The plugin AssemblyLoadContext could not be unloaded before the shutdown deadline.");
            }

            _state = _state.TransitionTo(PluginLifecycleState.Disabled);
            _disposed = true;
        }
        catch (Exception exception)
        {
            _state = _state.TransitionTo(
                PluginLifecycleState.DisableFailed,
                errorCode: GetFailureCode(exception, deadline),
                errorMessage: exception.Message);
            _state = _state.TransitionTo(
                PluginLifecycleState.RestartRequired,
                errorCode: GetFailureCode(exception, deadline),
                errorMessage: exception.Message);
            throw;
        }
        finally
        {
            _plugin = null;
            _pluginContext = null;
            plugin = null;
            _lifetimeScope.Dispose();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        if (_state.LifecycleState is PluginLifecycleState.Faulted
            or PluginLifecycleState.DisableFailed
            or PluginLifecycleState.RestartRequired
            or PluginLifecycleState.Quarantined)
        {
            await ReleaseAfterFailureAsync().ConfigureAwait(false);
            return;
        }

        await StopAndUnloadAsync().ConfigureAwait(false);
    }

    public void RequireRestart(
        string reason,
        string errorCode = "PLUGIN_RESTART_REQUIRED")
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);

        if (_state.LifecycleState == PluginLifecycleState.RestartRequired)
        {
            return;
        }

        _state = _state.TransitionTo(
            PluginLifecycleState.RestartRequired,
            errorCode: errorCode,
            errorMessage: reason);
    }

    public void Quarantine(
        string reason,
        string errorCode = "PLUGIN_QUARANTINED")
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);

        _state = _state.TransitionTo(
            PluginLifecycleState.Quarantined,
            errorCode: errorCode,
            errorMessage: reason);
    }

    private WeakReference UnloadLoadContext()
    {
        var loadContext = _loadContext
            ?? throw new PluginLoadException(
                "PLUGIN_ALC_MISSING",
                "The plugin AssemblyLoadContext is no longer available.");

        _loadContext = null;
        loadContext.Unload();

        // Keep the strong ALC reference inside this helper so it is out of scope
        // before the caller starts forced collection for unload verification.
        return LoadContextReference;
    }

    private static async Task<bool> WaitForUnloadAsync(
        WeakReference loadContextReference,
        ShutdownDeadline deadline)
    {
        while (loadContextReference.IsAlive)
        {
            if (deadline.IsExpired || deadline.Token.IsCancellationRequested)
            {
                return false;
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            if (!loadContextReference.IsAlive)
            {
                break;
            }

            try
            {
                await Task.Delay(
                        TimeSpan.FromMilliseconds(10),
                        deadline.Token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }

        return !loadContextReference.IsAlive;
    }

    private async ValueTask ReleaseAfterFailureAsync()
    {
        var failureState = _state.LifecycleState;
        var plugin = _plugin;

        if (failureState is PluginLifecycleState.Faulted or PluginLifecycleState.Quarantined)
        {
            using var deadline = ShutdownDeadline.Start(PluginShutdownOptions.Default);

            try
            {
                _lifetimeScope.Cancel();
                await _lifetimeScope.CleanupAsync(deadline.Token)
                    .AsTask()
                    .WaitAsync(deadline.Token)
                    .ConfigureAwait(false);

                if (plugin is not null)
                {
                    await plugin.DisposeAsync()
                        .AsTask()
                        .WaitAsync(deadline.Token)
                        .ConfigureAwait(false);
                }
            }
            catch (Exception exception)
            {
                if (_state.LifecycleState == PluginLifecycleState.Faulted)
                {
                    _state = _state.TransitionTo(
                        PluginLifecycleState.RestartRequired,
                        errorCode: GetFailureCode(exception, deadline),
                        errorMessage: exception.Message);
                }
            }
        }

        _plugin = null;
        _pluginContext = null;
        _lifetimeScope.Dispose();

        if (_loadContext is not null)
        {
            var loadContext = _loadContext;
            _loadContext = null;
            loadContext.Unload();
        }

        _disposed = true;
    }

    private static string GetFailureCode(Exception exception, ShutdownDeadline deadline)
    {
        if (exception is PluginLoadException loadException)
        {
            return loadException.ErrorCode;
        }

        if (deadline.IsExpired && !deadline.IsExternallyCancelled)
        {
            return "PLUGIN_SHUTDOWN_TIMEOUT";
        }

        return exception switch
        {
            _ => "PLUGIN_STOP_FAILED"
        };
    }

    private sealed class PluginContext : IPluginContext
    {
        public PluginContext(
            string pluginId,
            PluginLifetimeScope lifetimeScope,
            IResourceManager resources,
            IServiceBroker services)
        {
            PluginId = pluginId;
            LifetimeToken = lifetimeScope.LifetimeToken;
            LifetimeScope = lifetimeScope;
            Resources = resources;
            Services = services;
        }

        public string PluginId { get; }

        public CancellationToken LifetimeToken { get; }

        public IPluginLifetimeScope LifetimeScope { get; }

        public IResourceManager Resources { get; }

        public IServiceBroker Services { get; }
    }
}
