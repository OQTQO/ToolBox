using System.Globalization;
using ToolBox.PluginSdk;

namespace HelloPlugin;

public sealed class HelloPlugin : IPlugin, IPluginUiProvider, IPluginUiUpdateSource
{
    private Task? _backgroundTask;
    private CancellationTokenSource? _backgroundCancellation;
    private bool _disposed;
    private int _clickCount;
    private string _greeting = "hello";
    private bool _automatic;
    private double _volume = 50;

    public string Id => "com.toolbox.hello";

    public event EventHandler<PluginUiSnapshotUpdatedEventArgs>? SnapshotUpdated;

    public ValueTask StartAsync(IPluginContext context, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        _clickCount = 0;
        _greeting = "hello";
        _automatic = false;
        _volume = 50;
        _backgroundCancellation = CancellationTokenSource.CreateLinkedTokenSource(context.LifetimeToken);
        context.LifetimeScope.Register(_backgroundCancellation);
        _backgroundTask = RunBackgroundLoopAsync(_backgroundCancellation.Token);
        context.LifetimeScope.Track(_backgroundTask);
        return ValueTask.CompletedTask;
    }

    public async ValueTask StopAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        if (_backgroundTask is not null)
        {
            _backgroundCancellation?.Cancel();
            try
            {
                await _backgroundTask.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (_backgroundCancellation?.IsCancellationRequested == true)
            {
            }

            _backgroundTask = null;
            _backgroundCancellation = null;
        }
    }

    public ValueTask DisposeAsync()
    {
        _disposed = true;
        _backgroundCancellation?.Cancel();
        _backgroundTask = null;
        _backgroundCancellation = null;
        return ValueTask.CompletedTask;
    }

    public PluginUiSnapshot GetSnapshot()
    {
        return new PluginUiSnapshot(
            "HelloPlugin 正在运行。可以用通用控件验证 Worker 通道。",
            [
                new PluginUiValue("按钮次数", _clickCount.ToString(CultureInfo.InvariantCulture)),
                new PluginUiValue("当前进程", "PluginWorker")
            ],
            [new PluginUiAction("hello", "问候")],
            null)
        {
            Elements =
            [
                new PluginUiElement
                {
                    Id = "greeting",
                    Kind = PluginUiElementKind.Select,
                    Label = "问候方式",
                    Group = "通用控件",
                    ActionId = "greeting",
                    Value = _greeting,
                    Options =
                    [
                        new PluginUiOption("hello", "你好"),
                        new PluginUiOption("welcome", "欢迎")
                    ]
                },
                new PluginUiElement
                {
                    Id = "automatic",
                    Kind = PluginUiElementKind.Toggle,
                    Label = "自动运行",
                    Group = "通用控件",
                    ActionId = "automatic",
                    Value = _automatic ? "true" : "false"
                },
                new PluginUiElement
                {
                    Id = "volume",
                    Kind = PluginUiElementKind.Slider,
                    Label = "音量示例",
                    Group = "通用控件",
                    ActionId = "volume",
                    Value = _volume.ToString("R", CultureInfo.InvariantCulture),
                    Minimum = 0,
                    Maximum = 100,
                    Step = 5,
                    Unit = "%"
                },
                new PluginUiElement
                {
                    Id = "refresh",
                    Kind = PluginUiElementKind.Action,
                    Group = "操作",
                    ActionId = "refresh",
                    Command = PluginUiCommand.Refresh,
                    Style = PluginUiActionStyle.Primary
                }
            ],
            Status = new PluginUiStatus
            {
                Kind = PluginUiStatusKind.Success,
                Message = "通用 UI 已加载"
            }
        };
    }

    public ValueTask<PluginUiSnapshot> ExecuteAsync(
        string actionId,
        string? argument,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        switch (actionId)
        {
            case "hello":
            case "refresh":
                _clickCount++;
                break;
            case "greeting":
                _greeting = argument is "welcome" ? "welcome" : "hello";
                break;
            case "automatic":
                _automatic = bool.TryParse(argument, out var automatic) && automatic;
                break;
            case "volume":
                if (double.TryParse(argument, NumberStyles.Float, CultureInfo.InvariantCulture, out var volume)
                    && double.IsFinite(volume))
                {
                    _volume = Math.Clamp(volume, 0, 100);
                }

                break;
            default:
                throw new InvalidOperationException($"Unknown HelloPlugin action '{actionId}'.");
        }

        var snapshot = GetSnapshot();
        SnapshotUpdated?.Invoke(this, new PluginUiSnapshotUpdatedEventArgs(snapshot));
        return ValueTask.FromResult(snapshot);
    }

    public ValueTask<PluginUiSnapshot> HandleInputAsync(
        PluginInputEvent input,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(input);
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(GetSnapshot());
    }

    private static async Task RunBackgroundLoopAsync(CancellationToken lifetimeToken)
    {
        while (!lifetimeToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(30), lifetimeToken);
        }
    }
}
