using ToolBox.PluginSdk;
using ToolBox.PluginSdk.Experimental;

namespace AudioRelayPlugin;

public sealed class AudioRelayPlugin : IAudioRelayPlugin
{
    private readonly object _gate = new();
    private readonly IAudioRelayPlatform _platform;
    private AudioRelaySnapshot _snapshot = AudioRelaySnapshot.Disabled();
    private bool _started;
    private bool _disposed;

    public AudioRelayPlugin()
        : this(new WindowsAudioRelayPlatform())
    {
    }

    internal AudioRelayPlugin(IAudioRelayPlatform platform)
    {
        _platform = platform ?? throw new ArgumentNullException(nameof(platform));
        _platform.StateChanged += OnPlatformStateChanged;
    }

    public string Id => "com.toolbox.audio-relay";

    public AudioRelaySnapshot Snapshot
    {
        get
        {
            lock (_gate)
            {
                return CloneSnapshot(_snapshot);
            }
        }
    }

    public event Action<AudioRelaySnapshot>? SnapshotChanged;

    public async ValueTask StartAsync(IPluginContext context, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        var audioSinkLease = context.Resources.Acquire(
            new ResourceKey("audio.bluetooth.a2dp-sink"),
            ResourceAccessMode.Exclusive);

        try
        {
            context.LifetimeScope.Register(audioSinkLease);
        }
        catch
        {
            audioSinkLease.Dispose();
            throw;
        }

        _started = true;
        if (!_platform.IsSupported)
        {
            Publish(new AudioRelaySnapshot(
                AudioRelayStatus.Unsupported,
                [],
                SelectedDeviceId: null,
                SelectedDeviceName: null,
                StatusMessage: "This PC needs Windows 10 version 2004 or later for Bluetooth audio receiving.",
                ErrorCode: "AUDIO_RELAY_WINDOWS_UNSUPPORTED"));
            return;
        }

        await RefreshDevicesAsync(cancellationToken);
    }

    public ValueTask StopAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        _platform.Disconnect();
        _started = false;
        Publish(AudioRelaySnapshot.Disabled());
        return ValueTask.CompletedTask;
    }

    public async ValueTask RefreshDevicesAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureStarted();
        cancellationToken.ThrowIfCancellationRequested();

        var current = Snapshot;
        Publish(current with
        {
            Status = AudioRelayStatus.Refreshing,
            StatusMessage = "Looking for paired phones that support Bluetooth media audio…",
            ErrorCode = null
        });

        try
        {
            var devices = await _platform.FindDevicesAsync(cancellationToken);
            var selected = devices.FirstOrDefault(device => string.Equals(
                device.Id,
                current.SelectedDeviceId,
                StringComparison.Ordinal));
            Publish(new AudioRelaySnapshot(
                AudioRelayStatus.Ready,
                devices,
                selected?.Id,
                selected?.Name,
                devices.Length == 0
                    ? "No paired phone is available. Pair it in Windows Bluetooth settings, then refresh."
                    : $"Found {devices.Length} paired Bluetooth audio source(s).",
                ErrorCode: null));
        }
        catch (Exception exception)
        {
            PublishFailure("AUDIO_RELAY_DISCOVERY_FAILED", "Windows could not enumerate paired Bluetooth audio devices.", exception);
            throw;
        }
    }

    public async ValueTask ConnectAsync(string deviceId, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);
        EnsureStarted();
        cancellationToken.ThrowIfCancellationRequested();

        var current = Snapshot;
        var selected = current.Devices.SingleOrDefault(device => string.Equals(
            device.Id,
            deviceId,
            StringComparison.Ordinal))
            ?? throw new InvalidOperationException(
                "The selected phone is no longer available. Refresh the paired device list.");

        Publish(current with
        {
            Status = AudioRelayStatus.Connecting,
            SelectedDeviceId = selected.Id,
            SelectedDeviceName = selected.Name,
            StatusMessage = $"Opening Bluetooth media audio from {selected.Name}…",
            ErrorCode = null
        });

        try
        {
            await _platform.ConnectAsync(selected.Id, cancellationToken);
            Publish(Snapshot with
            {
                Status = AudioRelayStatus.Streaming,
                StatusMessage = $"Receiving media audio from {selected.Name}. PC audio continues through the normal Windows mix.",
                ErrorCode = null
            });
        }
        catch (Exception exception)
        {
            var errorCode = exception is AudioRelayPlatformException platformException
                ? platformException.ErrorCode
                : "AUDIO_RELAY_CONNECTION_FAILED";
            PublishFailure(errorCode, $"Could not receive audio from {selected.Name}.", exception);
            throw;
        }
    }

    public ValueTask DisconnectAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureStarted();
        cancellationToken.ThrowIfCancellationRequested();

        var current = Snapshot;
        _platform.Disconnect();
        Publish(current with
        {
            Status = AudioRelayStatus.Ready,
            StatusMessage = "Phone audio receiving stopped. The paired device remains available.",
            ErrorCode = null
        });
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return ValueTask.CompletedTask;
        }

        _disposed = true;
        _started = false;
        _platform.StateChanged -= OnPlatformStateChanged;
        _platform.Dispose();
        Publish(AudioRelaySnapshot.Disabled());
        SnapshotChanged = null;
        return ValueTask.CompletedTask;
    }

    private void OnPlatformStateChanged(AudioRelayTransportState state)
    {
        var current = Snapshot;
        if (!_started)
        {
            return;
        }

        if (state == AudioRelayTransportState.Opened)
        {
            if (current.Status == AudioRelayStatus.Connecting)
            {
                Publish(current with
                {
                    Status = AudioRelayStatus.Streaming,
                    StatusMessage = $"Receiving media audio from {current.SelectedDeviceName ?? "the phone"}. PC audio remains in the Windows mix.",
                    ErrorCode = null
                });
            }

            return;
        }

        if (current.Status is AudioRelayStatus.Streaming or AudioRelayStatus.Connecting)
        {
            Publish(current with
            {
                Status = AudioRelayStatus.Ready,
                StatusMessage = "The phone closed the Bluetooth audio connection. Select it and start receiving again.",
                ErrorCode = null
            });
        }
    }

    private void PublishFailure(string errorCode, string message, Exception exception)
    {
        Publish(Snapshot with
        {
            Status = AudioRelayStatus.Error,
            StatusMessage = $"{message} {exception.Message}",
            ErrorCode = errorCode
        });
    }

    private void Publish(AudioRelaySnapshot snapshot)
    {
        AudioRelaySnapshot published;
        lock (_gate)
        {
            _snapshot = CloneSnapshot(snapshot);
            published = CloneSnapshot(_snapshot);
        }

        SnapshotChanged?.Invoke(published);
    }

    private void EnsureStarted()
    {
        if (!_started)
        {
            throw new InvalidOperationException("Enable the Phone Audio Relay plugin before using it.");
        }
    }

    private static AudioRelaySnapshot CloneSnapshot(AudioRelaySnapshot snapshot)
    {
        return snapshot with { Devices = [.. snapshot.Devices] };
    }
}
