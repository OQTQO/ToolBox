using System.Diagnostics;
using System.Globalization;
using ToolBox.PluginSdk;

namespace WorkerChildProcessPlugin;

public sealed class WorkerChildProcessPlugin : IPlugin, IPluginUiProvider
{
    private Process? _childProcess;
    private int _actionCount;

    public string Id => "com.toolbox.worker-child-process";

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
            [new PluginUiAction("touch", "Touch plugin")],
            null);
    }

    public ValueTask<PluginUiSnapshot> ExecuteAsync(
        string actionId,
        string? argument,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!string.Equals(actionId, "touch", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Unknown action '{actionId}'.");
        }

        _actionCount++;
        return ValueTask.FromResult(GetSnapshot());
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
