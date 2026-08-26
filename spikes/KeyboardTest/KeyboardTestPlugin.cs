using System.Globalization;
using ToolBox.PluginSdk;
using ToolBox.PluginSdk.Experimental;

namespace KeyboardTestPlugin;

public sealed class KeyboardTestPlugin : IKeyboardTestPlugin
{
    private KeyboardTestSettings _settings = KeyboardTestSettings.Default;
    private KeyboardTestSnapshot _snapshot = KeyboardTestSnapshot.Disabled(KeyboardTestSettings.Default);
    private bool _disposed;

    public string Id => "com.toolbox.keyboard-test";

    public KeyboardTestSnapshot Snapshot => _snapshot;

    public event Action<KeyboardTestSnapshot>? SnapshotChanged;

    public ValueTask StartAsync(IPluginContext context, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        var inputSurfaceLease = context.Resources.Acquire(
            new ResourceKey("keyboard.test.surface"),
            ResourceAccessMode.Exclusive);

        try
        {
            context.LifetimeScope.Register(inputSurfaceLease);
        }
        catch
        {
            inputSurfaceLease.Dispose();
            throw;
        }

        _snapshot = _snapshot with
        {
            IsEnabled = true,
            Settings = _settings,
            LastInput = string.Empty,
            LastEventAtUtc = null
        };
        RaiseSnapshotChanged();
        return ValueTask.CompletedTask;
    }

    public ValueTask StopAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        _snapshot = _snapshot with
        {
            IsEnabled = false,
            Settings = _settings
        };
        RaiseSnapshotChanged();
        return ValueTask.CompletedTask;
    }

    public ValueTask ApplySettingsAsync(KeyboardTestSettings settings, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(settings);
        cancellationToken.ThrowIfCancellationRequested();

        _settings = settings;
        _snapshot = _snapshot with { Settings = settings };
        RaiseSnapshotChanged();
        return ValueTask.CompletedTask;
    }

    public void ObserveKey(string key, bool isDown)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_snapshot.IsEnabled || string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        if (!isDown && !_settings.IncludeKeyUpEvents)
        {
            return;
        }

        var action = isDown ? "down" : "up";
        _snapshot = _snapshot with
        {
            KeyEventCount = _snapshot.KeyEventCount + 1,
            LastInput = $"{key} · {action}",
            LastEventAtUtc = DateTimeOffset.UtcNow
        };
        RaiseSnapshotChanged();
    }

    public void ObserveMouse(KeyboardTestMouseButton button, bool isDown, int x, int y)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_snapshot.IsEnabled || !_settings.IncludeMouseEvents)
        {
            return;
        }

        var action = isDown ? "down" : "up";
        var position = string.Create(
            CultureInfo.InvariantCulture,
            $"{x},{y}");
        _snapshot = _snapshot with
        {
            MouseEventCount = _snapshot.MouseEventCount + 1,
            LastInput = $"{button} · {action} · {position}",
            LastEventAtUtc = DateTimeOffset.UtcNow
        };
        RaiseSnapshotChanged();
    }

    public ValueTask DisposeAsync()
    {
        _disposed = true;
        _snapshot = _snapshot with { IsEnabled = false };
        SnapshotChanged = null;
        return ValueTask.CompletedTask;
    }

    private void RaiseSnapshotChanged()
    {
        SnapshotChanged?.Invoke(_snapshot);
    }
}
