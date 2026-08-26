namespace ToolBox.PluginSdk.Experimental;

public interface IAudioRelayPlugin : IPlugin
{
    AudioRelaySnapshot Snapshot { get; }

    event Action<AudioRelaySnapshot>? SnapshotChanged;

    ValueTask RefreshDevicesAsync(CancellationToken cancellationToken);

    ValueTask ConnectAsync(string deviceId, CancellationToken cancellationToken);

    ValueTask DisconnectAsync(CancellationToken cancellationToken);
}

public enum AudioRelayStatus
{
    Disabled,
    Refreshing,
    Ready,
    Connecting,
    Streaming,
    Unsupported,
    Error
}

public sealed record AudioRelayDevice(string Id, string Name);

public sealed record AudioRelaySnapshot(
    AudioRelayStatus Status,
    AudioRelayDevice[] Devices,
    string? SelectedDeviceId,
    string? SelectedDeviceName,
    string StatusMessage,
    string? ErrorCode)
{
    public static AudioRelaySnapshot Disabled()
    {
        return new AudioRelaySnapshot(
            AudioRelayStatus.Disabled,
            [],
            SelectedDeviceId: null,
            SelectedDeviceName: null,
            StatusMessage: "Audio relay is disabled.",
            ErrorCode: null);
    }
}
