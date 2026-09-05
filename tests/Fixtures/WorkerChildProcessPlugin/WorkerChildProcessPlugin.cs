using System.Diagnostics;
using System.Globalization;
using ToolBox.PluginSdk;

namespace WorkerChildProcessPlugin;

public sealed class WorkerChildProcessPlugin : IPlugin, IPluginUiProvider, IPluginUiUpdateSource
{
    private Process? _childProcess;
    private int _actionCount;

    public string Id => "com.toolbox.worker-child-process";

    public event EventHandler<PluginUiSnapshotUpdatedEventArgs>? SnapshotUpdated;

    public ValueTask StartAsync(IPluginContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        var processPath = Environment.ProcessPath
            ?? throw new InvalidOperationException("The Worker process path is not available.");
        var processStartInfo = new ProcessStartInfo
        {
            FileName = processPath,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        processStartInfo.ArgumentList.Add("--child-sleeper");

        _childProcess = Process.Start(processStartInfo)
            ?? throw new InvalidOperationException("The Worker child process could not be started.");

        var pluginDirectory = Path.GetDirectoryName(typeof(WorkerChildProcessPlugin).Assembly.Location)
            ?? throw new InvalidOperationException("The plugin directory is not available.");
        File.WriteAllText(
            Path.Combine(pluginDirectory, "child.pid"),
            _childProcess.Id.ToString(CultureInfo.InvariantCulture));
        _actionCount = 0;

        return ValueTask.CompletedTask;
    }

    public ValueTask StopAsync(CancellationToken cancellationToken)
    {
        // Deliberately leave the child alive. The Worker Job Object must clean it up.
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        _childProcess?.Dispose();
        _childProcess = null;
        return ValueTask.CompletedTask;
    }

    public PluginUiSnapshot GetSnapshot()
    {
        return new PluginUiSnapshot(
            "Worker child test plugin is running.",
            [new PluginUiValue("Actions", _actionCount.ToString(CultureInfo.InvariantCulture))],
            [
                new PluginUiAction("touch", "Touch plugin"),
                new PluginUiAction("hang", "Hang plugin")
            ],
            null)
        {
            Elements =
            [
                new PluginUiElement
                {
                    Id = "refresh",
                    Kind = PluginUiElementKind.Action,
                    ActionId = "touch",
                    Command = PluginUiCommand.Refresh,
                    Style = PluginUiActionStyle.Primary
                },
                new PluginUiElement
                {
                    Id = "mode",
                    Kind = PluginUiElementKind.Select,
                    Label = "Mode",
                    ActionId = "mode",
                    Value = "a",
                    Options = [new PluginUiOption("a", "A"), new PluginUiOption("b", "B")]
                },
                new PluginUiElement
                {
                    Id = "enabled",
                    Kind = PluginUiElementKind.Toggle,
                    Label = "Enabled",
                    ActionId = "enabled",
                    Value = "true"
                },
                new PluginUiElement
                {
                    Id = "volume",
                    Kind = PluginUiElementKind.Slider,
                    Label = "Volume",
                    ActionId = "volume",
                    Value = "50",
                    Minimum = 0,
                    Maximum = 100,
                    Step = 5
                }
            ],
            Status = new PluginUiStatus
            {
                Kind = PluginUiStatusKind.Success,
                Message = "Ready"
            }
        };
    }

    public ValueTask<PluginUiSnapshot> ExecuteAsync(
        string actionId,
        string? argument,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.Equals(actionId, "hang", StringComparison.Ordinal))
        {
            return new ValueTask<PluginUiSnapshot>(HangAsync(cancellationToken));
        }

        if (!string.Equals(actionId, "touch", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Unknown action '{actionId}'.");
        }

        _actionCount++;
        var snapshot = GetSnapshot();
        SnapshotUpdated?.Invoke(this, new PluginUiSnapshotUpdatedEventArgs(snapshot));
        return ValueTask.FromResult(snapshot);
    }

    private static async Task<PluginUiSnapshot> HangAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
        throw new InvalidOperationException("The hanging test action unexpectedly completed.");
    }

    public ValueTask<PluginUiSnapshot> HandleInputAsync(
        PluginInputEvent input,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(GetSnapshot());
    }
}
