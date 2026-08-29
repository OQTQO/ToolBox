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
