namespace ToolBox.PluginSdk;

/// <summary>
/// Optional, host-rendered interaction surface for a plugin.
/// The contract contains data only; it does not expose WPF or another UI
/// framework to plugins.
/// </summary>
public interface IPluginUiProvider
{
    PluginUiSnapshot GetSnapshot();

    ValueTask<PluginUiSnapshot> ExecuteAsync(
        string actionId,
        string? argument,
        CancellationToken cancellationToken);

    ValueTask<PluginUiSnapshot> HandleInputAsync(
        PluginInputEvent input,
        CancellationToken cancellationToken);
}

public sealed record PluginUiSnapshot(
    string StatusMessage,
    IReadOnlyList<PluginUiValue> Values,
    IReadOnlyList<PluginUiAction> Actions,
    PluginInputSurface? InputSurface)
{
    public IReadOnlyList<PluginUiElement> Elements { get; init; } = Array.Empty<PluginUiElement>();

    public PluginUiStatus? Status { get; init; }

    public PluginUiDialog? Dialog { get; init; }

    public static PluginUiSnapshot Empty { get; } = new(
        string.Empty,
        Array.Empty<PluginUiValue>(),
        Array.Empty<PluginUiAction>(),
        null);
}

public sealed record PluginUiValue(string Label, string Value);

public sealed record PluginUiAction(
    string Id,
    string Label,
    string? Argument = null,
    bool IsEnabled = true,
    string? Description = null);

public sealed record PluginInputSurface(
    string Label,
    string Description,
    bool CaptureKeyboard = true,
    bool CaptureMouse = true);

public enum PluginInputEventType
{
    KeyDown,
    KeyUp,
    MouseDown,
    MouseUp
}

public sealed record PluginInputEvent(
    PluginInputEventType Type,
    string? Key = null,
    string? MouseButton = null,
    int X = 0,
    int Y = 0);

public interface IPluginUiUpdateSource
{
    event EventHandler<PluginUiSnapshotUpdatedEventArgs>? SnapshotUpdated;
}

public sealed class PluginUiSnapshotUpdatedEventArgs : EventArgs
{
    public PluginUiSnapshotUpdatedEventArgs(PluginUiSnapshot snapshot)
    {
        Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
    }

    public PluginUiSnapshot Snapshot { get; }
}

public enum PluginUiElementKind
{
    Unknown = 0,
    Value = 1,
    Action = 2,
    Menu = 3,
    Select = 4,
    MultiSelect = 5,
    Toggle = 6,
    CheckBox = 7,
    RadioGroup = 8,
    TextBox = 9,
    NumberBox = 10,
    Slider = 11
}

public enum PluginUiCommand
{
    Unknown = 0,
    Custom = 1,
    Refresh = 2,
    Retry = 3,
    Search = 4,
    Scan = 5,
    Start = 6,
    Stop = 7,
    Pause = 8,
    Resume = 9,
    Cancel = 10,
    Connect = 11,
    Disconnect = 12,
    Reconnect = 13,
    Save = 14,
    Apply = 15,
    Reset = 16,
    Add = 17,
    Delete = 18,
    Copy = 19,
    Import = 20,
    Export = 21,
    Open = 22,
    Close = 23,
    Settings = 24,
    Help = 25,
    More = 26,
    Play = 27,
    Previous = 28,
    Next = 29,
    Rewind = 30,
    FastForward = 31,
    Mute = 32,
    Unmute = 33,
    VolumeUp = 34,
    VolumeDown = 35
}

public enum PluginUiActionStyle
{
    Unknown = 0,
    Default = 1,
    Primary = 2,
    Secondary = 3,
    Compact = 4,
    Icon = 5,
    Destructive = 6
}

public enum PluginUiUpdateMode
{
    Unknown = 0,
    Default = 1,
    Immediate = 2,
    Commit = 3
}

public enum PluginUiStatusKind
{
    Unknown = 0,
    Information = 1,
    Warning = 2,
    Error = 3,
    Success = 4,
    Busy = 5,
    Progress = 6,
    Cancelled = 7
}

public enum PluginUiDialogKind
{
    Unknown = 0,
    Information = 1,
    Warning = 2,
    Error = 3,
    Confirmation = 4
}

public sealed record PluginUiOption(
    string Value,
    string Label,
    bool IsEnabled = true,
    string? Description = null);

public sealed record PluginUiMenuItem
{
    public string Id { get; init; } = string.Empty;

    public string Label { get; init; } = string.Empty;

    public string? Description { get; init; }

    public bool IsEnabled { get; init; } = true;

    public bool IsChecked { get; init; }

    public string? ActionId { get; init; }

    public string? Argument { get; init; }

    public PluginUiCommand Command { get; init; } = PluginUiCommand.Custom;

    public PluginUiActionStyle Style { get; init; } = PluginUiActionStyle.Default;

    public string? Icon { get; init; }

    public IReadOnlyList<PluginUiMenuItem> Children { get; init; } = Array.Empty<PluginUiMenuItem>();
}

public sealed record PluginUiElement
{
    public string Id { get; init; } = string.Empty;

    public PluginUiElementKind Kind { get; init; } = PluginUiElementKind.Unknown;

    public string? Label { get; init; }

    public string? Description { get; init; }

    public string? Group { get; init; }

    public bool IsEnabled { get; init; } = true;

    public string? ActionId { get; init; }

    public string? Argument { get; init; }

    public PluginUiCommand Command { get; init; } = PluginUiCommand.Custom;

    public string? CommandTarget { get; init; }

    public PluginUiActionStyle Style { get; init; } = PluginUiActionStyle.Default;

    public string? Icon { get; init; }

    public PluginUiUpdateMode UpdateMode { get; init; } = PluginUiUpdateMode.Default;

    public string? Value { get; init; }

    public IReadOnlyList<string> Values { get; init; } = Array.Empty<string>();

    public IReadOnlyList<PluginUiOption> Options { get; init; } = Array.Empty<PluginUiOption>();

    public IReadOnlyList<PluginUiMenuItem> MenuItems { get; init; } = Array.Empty<PluginUiMenuItem>();

    public string? Placeholder { get; init; }

    public double? Minimum { get; init; }

    public double? Maximum { get; init; }

    public double? Step { get; init; }

    public string? Unit { get; init; }

    public string? AccessibleName { get; init; }
}

public sealed record PluginUiProgress
{
    public double? Value { get; init; }

    public double? Maximum { get; init; }

    public bool IsIndeterminate { get; init; }

    public string? CancelActionId { get; init; }

    public string? CancelArgument { get; init; }
}

public sealed record PluginUiStatus
{
    public PluginUiStatusKind Kind { get; init; } = PluginUiStatusKind.Information;

    public string Message { get; init; } = string.Empty;

    public PluginUiProgress? Progress { get; init; }
}

public sealed record PluginUiDialog
{
    public string Id { get; init; } = string.Empty;

    public PluginUiDialogKind Kind { get; init; } = PluginUiDialogKind.Information;

    public string Title { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;

    public IReadOnlyList<PluginUiAction> Actions { get; init; } = Array.Empty<PluginUiAction>();

    public string? DefaultActionId { get; init; }

    public string? CancelActionId { get; init; }
}
