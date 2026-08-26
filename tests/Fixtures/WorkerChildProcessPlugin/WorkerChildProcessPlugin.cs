using System.Diagnostics;
using System.Globalization;
using ToolBox.PluginSdk;

namespace WorkerChildProcessPlugin;

public sealed class WorkerChildProcessPlugin : IPlugin
{
    private Process? _childProcess;

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
}
