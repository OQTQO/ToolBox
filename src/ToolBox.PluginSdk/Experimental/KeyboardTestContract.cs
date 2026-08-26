namespace ToolBox.PluginSdk.Experimental;

public interface IKeyboardTestPlugin : IPlugin
{
    KeyboardTestSnapshot Snapshot { get; }

    event Action<KeyboardTestSnapshot>? SnapshotChanged;

    ValueTask ApplySettingsAsync(KeyboardTestSettings settings, CancellationToken cancellationToken);

    void ObserveKey(string key, bool isDown);

    void ObserveMouse(KeyboardTestMouseButton button, bool isDown, int x, int y);
}

public enum KeyboardTestMouseButton
{
    Left,
    Right,
    Middle
}

public sealed record KeyboardTestSettings(bool IncludeKeyUpEvents, bool IncludeMouseEvents)
{
    public static KeyboardTestSettings Default { get; } = new(
        IncludeKeyUpEvents: false,
        IncludeMouseEvents: true);
}

public sealed record KeyboardTestSnapshot(
    bool IsEnabled,
    int KeyEventCount,
    int MouseEventCount,
    string LastInput,
    KeyboardTestSettings Settings,
    DateTimeOffset? LastEventAtUtc)
{
    public static KeyboardTestSnapshot Disabled(KeyboardTestSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return new KeyboardTestSnapshot(
            IsEnabled: false,
            KeyEventCount: 0,
            MouseEventCount: 0,
            LastInput: string.Empty,
            Settings: settings,
            LastEventAtUtc: null);
    }
}
