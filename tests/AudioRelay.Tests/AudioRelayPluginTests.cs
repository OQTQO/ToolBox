using AudioRelayPlugin;
using System.Runtime.CompilerServices;
using ToolBox.Core.Plugins;
using ToolBox.PluginSdk;
using ToolBox.PluginSdk.Experimental;
using Xunit;

namespace AudioRelay.Tests;

public sealed class AudioRelayPluginTests
{
    [Fact]
    public async Task WindowsPlatformProbesPairedSourcesWithoutOpeningAConnection()
    {
        using var platform = new WindowsAudioRelayPlatform();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        Assert.True(platform.IsSupported);

        var devices = await platform.FindDevicesAsync(timeout.Token);

        Assert.NotNull(devices);
        Assert.All(devices, device =>
        {
            Assert.False(string.IsNullOrWhiteSpace(device.Id));
            Assert.False(string.IsNullOrWhiteSpace(device.Name));
        });
    }

    [Fact]
    public async Task ProductStartsAndUnloadsThroughTheRealInProcessRuntime()
    {
        var root = Path.Combine(Path.GetTempPath(), "ToolBoxAudioRelay", Guid.NewGuid().ToString("N"));
        var pluginDirectory = Path.Combine(root, "AudioRelay");
        Directory.CreateDirectory(pluginDirectory);

        try
        {
            var assemblyPath = typeof(AudioRelayPlugin.AudioRelayPlugin).Assembly.Location;
            var assemblyDirectory = Path.GetDirectoryName(assemblyPath)!;
            File.Copy(assemblyPath, Path.Combine(pluginDirectory, "AudioRelay.dll"));
            var depsPath = Path.Combine(assemblyDirectory, "AudioRelay.deps.json");
            if (File.Exists(depsPath))
            {
                File.Copy(depsPath, Path.Combine(pluginDirectory, "AudioRelay.deps.json"));
            }
            File.Copy(
                Path.Combine(assemblyDirectory, "Microsoft.Windows.SDK.NET.dll"),
                Path.Combine(pluginDirectory, "Microsoft.Windows.SDK.NET.dll"));
            File.Copy(
                Path.Combine(assemblyDirectory, "WinRT.Runtime.dll"),
                Path.Combine(pluginDirectory, "WinRT.Runtime.dll"));
            File.Copy(
                Path.Combine(AppContext.BaseDirectory, "AudioRelay.manifest.json"),
                Path.Combine(pluginDirectory, "manifest.json"));

            var runtime = new InProcessPluginRuntime();
            var discovered = runtime.DiscoverSingle(pluginDirectory);
            var loaded = runtime.Load(discovered);

            Assert.Equal("com.toolbox.audio-relay", discovered.Manifest.Id);
            Assert.DoesNotContain(
                Directory.EnumerateFiles(pluginDirectory),
                path => string.Equals(Path.GetFileName(path), "ToolBox.PluginSdk.dll", StringComparison.OrdinalIgnoreCase));

            await loaded.StartAsync();
            var snapshot = ReadSnapshotWithoutRetainingCapability(loaded);

            Assert.Equal(AudioRelayStatus.Ready, snapshot.Status);

            await loaded.StopAndUnloadAsync();
            Assert.Equal(PluginLifecycleState.Disabled, loaded.State.LifecycleState);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static AudioRelaySnapshot ReadSnapshotWithoutRetainingCapability(
        LoadedInProcessPlugin loaded)
    {
        var capability = Assert.IsAssignableFrom<IAudioRelayPlugin>(
            loaded.GetCapability<IAudioRelayPlugin>());
        return capability.Snapshot;
    }

    [Fact]
    public async Task PluginDiscoversConnectsDisconnectsAndStopsCleanly()
    {
        var platform = new FakeAudioRelayPlatform(
            [new AudioRelayDevice("phone-1", "Pixel Test Phone")]);
        await using var plugin = new AudioRelayPlugin.AudioRelayPlugin(platform);
        using var context = new TestPluginContext(plugin.Id);
        var snapshots = new List<AudioRelaySnapshot>();
        plugin.SnapshotChanged += snapshots.Add;

        await plugin.StartAsync(context, CancellationToken.None);

        Assert.Equal(AudioRelayStatus.Ready, plugin.Snapshot.Status);
        var device = Assert.Single(plugin.Snapshot.Devices);
        Assert.Equal("Pixel Test Phone", device.Name);
        Assert.Equal("audio.bluetooth.a2dp-sink", context.Resources.LastKey?.Value);
        Assert.Equal(ResourceAccessMode.Exclusive, context.Resources.LastAccessMode);

        await plugin.ConnectAsync(device.Id, CancellationToken.None);

        Assert.Equal("phone-1", platform.ConnectedDeviceId);
        Assert.Equal(AudioRelayStatus.Streaming, plugin.Snapshot.Status);
        Assert.Contains("Windows mix", plugin.Snapshot.StatusMessage, StringComparison.OrdinalIgnoreCase);

        await plugin.DisconnectAsync(CancellationToken.None);

        Assert.Null(platform.ConnectedDeviceId);
        Assert.Equal(AudioRelayStatus.Ready, plugin.Snapshot.Status);

        await plugin.StopAsync(CancellationToken.None);

        Assert.Equal(AudioRelayStatus.Disabled, plugin.Snapshot.Status);
        Assert.Contains(snapshots, snapshot => snapshot.Status == AudioRelayStatus.Refreshing);
        Assert.Contains(snapshots, snapshot => snapshot.Status == AudioRelayStatus.Streaming);
    }

    [Fact]
    public async Task UnsupportedWindowsStateIsVisibleWithoutDiscovery()
    {
        var platform = new FakeAudioRelayPlatform([]) { IsSupported = false };
        await using var plugin = new AudioRelayPlugin.AudioRelayPlugin(platform);
        using var context = new TestPluginContext(plugin.Id);

        await plugin.StartAsync(context, CancellationToken.None);

        Assert.Equal(AudioRelayStatus.Unsupported, plugin.Snapshot.Status);
        Assert.Equal("AUDIO_RELAY_WINDOWS_UNSUPPORTED", plugin.Snapshot.ErrorCode);
        Assert.Equal(0, platform.DiscoveryCount);
    }

    [Fact]
    public async Task PhoneClosingTheTransportReturnsPluginToReady()
    {
        var platform = new FakeAudioRelayPlatform(
            [new AudioRelayDevice("phone-2", "Android Phone")]);
        await using var plugin = new AudioRelayPlugin.AudioRelayPlugin(platform);
        using var context = new TestPluginContext(plugin.Id);

        await plugin.StartAsync(context, CancellationToken.None);
        await plugin.ConnectAsync("phone-2", CancellationToken.None);

        platform.CloseFromPhone();

        Assert.Equal(AudioRelayStatus.Ready, plugin.Snapshot.Status);
        Assert.Contains("closed", plugin.Snapshot.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class FakeAudioRelayPlatform(AudioRelayDevice[] devices) : IAudioRelayPlatform
    {
        public bool IsSupported { get; set; } = true;

        public string? ConnectedDeviceId { get; private set; }

        public int DiscoveryCount { get; private set; }

        public event Action<AudioRelayTransportState>? StateChanged;

        public ValueTask<AudioRelayDevice[]> FindDevicesAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DiscoveryCount++;
            return ValueTask.FromResult(devices.ToArray());
        }

        public ValueTask ConnectAsync(string deviceId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ConnectedDeviceId = deviceId;
            StateChanged?.Invoke(AudioRelayTransportState.Opened);
            return ValueTask.CompletedTask;
        }

        public void Disconnect()
        {
            ConnectedDeviceId = null;
        }

        public void CloseFromPhone()
        {
            ConnectedDeviceId = null;
            StateChanged?.Invoke(AudioRelayTransportState.Closed);
        }

        public void Dispose()
        {
            ConnectedDeviceId = null;
            StateChanged = null;
        }
    }

    private sealed class TestPluginContext : IPluginContext, IDisposable
    {
        private readonly CancellationTokenSource _lifetime = new();

        public TestPluginContext(string pluginId)
        {
            PluginId = pluginId;
            LifetimeScope = new TestLifetimeScope(_lifetime.Token);
            Resources = new TestResourceManager(pluginId);
            Services = new TestServiceBroker();
        }

        public string PluginId { get; }

        public CancellationToken LifetimeToken => _lifetime.Token;

        public IPluginLifetimeScope LifetimeScope { get; }

        public TestResourceManager Resources { get; }

        IResourceManager IPluginContext.Resources => Resources;

        public IServiceBroker Services { get; }

        public void Dispose()
        {
            _lifetime.Cancel();
            ((TestLifetimeScope)LifetimeScope).Dispose();
            _lifetime.Dispose();
        }
    }

    private sealed class TestLifetimeScope(CancellationToken lifetimeToken) : IPluginLifetimeScope, IDisposable
    {
        private readonly List<IDisposable> _resources = [];

        public CancellationToken LifetimeToken { get; } = lifetimeToken;

        public bool IsStopping => LifetimeToken.IsCancellationRequested;

        public void Track(Task backgroundTask)
        {
        }

        public IDisposable Register(IDisposable resource)
        {
            _resources.Add(resource);
            return resource;
        }

        public IDisposable Register(IAsyncDisposable resource)
        {
            var registration = new AsyncDisposableRegistration(resource);
            _resources.Add(registration);
            return registration;
        }

        public IDisposable Register(Func<CancellationToken, ValueTask> cleanup)
        {
            var registration = new CleanupRegistration(cleanup);
            _resources.Add(registration);
            return registration;
        }

        public void Dispose()
        {
            for (var index = _resources.Count - 1; index >= 0; index--)
            {
                _resources[index].Dispose();
            }

            _resources.Clear();
        }
    }

    private sealed class TestResourceManager(string ownerPluginId) : IResourceManager
    {
        public ResourceKey? LastKey { get; private set; }

        public ResourceAccessMode? LastAccessMode { get; private set; }

        public IResourceLease Acquire(ResourceKey key, ResourceAccessMode accessMode)
        {
            LastKey = key;
            LastAccessMode = accessMode;
            return new TestResourceLease(key, accessMode, ownerPluginId);
        }
    }

    private sealed class TestResourceLease(
        ResourceKey key,
        ResourceAccessMode accessMode,
        string ownerPluginId) : IResourceLease
    {
        public ResourceKey Key { get; } = key;

        public ResourceAccessMode AccessMode { get; } = accessMode;

        public string OwnerPluginId { get; } = ownerPluginId;

        public bool IsReleased { get; private set; }

        public void Dispose() => IsReleased = true;
    }

    private sealed class TestServiceBroker : IServiceBroker
    {
        public ValueTask<IServiceLease<T>> AcquireAsync<T>(
            string serviceKey,
            CancellationToken cancellationToken = default)
            where T : class
        {
            throw new NotSupportedException();
        }
    }

    private sealed class AsyncDisposableRegistration(IAsyncDisposable resource) : IDisposable
    {
        public void Dispose() => resource.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    private sealed class CleanupRegistration(Func<CancellationToken, ValueTask> cleanup) : IDisposable
    {
        public void Dispose() => cleanup(CancellationToken.None).AsTask().GetAwaiter().GetResult();
    }
}
