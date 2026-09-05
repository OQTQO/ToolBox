using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;
using ToolBox.Core.Plugins;
using ToolBox.Core.Plugins.Worker;
using ToolBox.PluginSdk;
using Xunit;

namespace ToolBox.Core.Tests;

[SupportedOSPlatform("windows")]
public sealed class OutOfProcessPluginRuntimeTests
{
    [Fact]
    public async Task WorkerStartsStopsAndCleansChildProcessThroughJobObject()
    {
        var pluginDirectory = PrepareWorkerChildPlugin();

        try
        {
            var runtime = new OutOfProcessPluginRuntime(GetWorkerPath());
            await using var session = await runtime.StartAsync(pluginDirectory);

            await session.StartPluginAsync();
            Assert.Equal(PluginLifecycleState.Running, session.State.LifecycleState);

            await session.SendHeartbeatAsync();

            var childPid = await WaitForChildPidAsync(pluginDirectory);
            Assert.True(IsProcessAlive(childPid));

            await session.StopAsync();

            Assert.Equal(PluginLifecycleState.Disabled, session.State.LifecycleState);
            Assert.True(await WaitForProcessExitAsync(childPid));
            Assert.True(session.WorkerHasExited);
        }
        finally
        {
            DeleteDirectoryWithRetry(pluginDirectory);
        }
    }

    [Fact]
    public async Task WorkerExposesOptionalPluginControlsWithoutLoadingPluginIntoTheHost()
    {
        var pluginDirectory = PrepareWorkerChildPlugin();

        try
        {
            var runtime = new OutOfProcessPluginRuntime(GetWorkerPath());
            await using var session = await runtime.StartAsync(pluginDirectory);
            await session.StartPluginAsync();

            var initial = await session.GetUiSnapshotAsync();
            Assert.NotNull(initial);
            Assert.Contains(initial!.Actions, action => action.Id == "touch");
            Assert.Equal("0", Assert.Single(initial.Values).Value);
            Assert.Contains(initial.Elements, element => element.Command == PluginUiCommand.Refresh);
            Assert.Equal(PluginUiStatusKind.Success, initial.Status!.Kind);

            var pushedUpdate = new TaskCompletionSource<PluginUiSnapshot>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            session.UiSnapshotUpdated += (_, args) => pushedUpdate.TrySetResult(args.Snapshot);

            var afterAction = await session.ExecuteUiActionAsync("touch");
            Assert.Equal("1", Assert.Single(afterAction.Values).Value);
            var unsolicited = await pushedUpdate.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal("1", Assert.Single(unsolicited.Values).Value);

            var afterInput = await session.SendUiInputAsync(
                new PluginInputEvent(PluginInputEventType.KeyDown, Key: "A"));
            Assert.Equal("1", Assert.Single(afterInput.Values).Value);

            await session.StopAsync();
        }
        finally
        {
            DeleteDirectoryWithRetry(pluginDirectory);
        }
    }

    [Fact]
    public async Task WorkerCrashIsIsolatedAndJobObjectCleansChildProcess()
    {
        var pluginDirectory = PrepareWorkerChildPlugin();

        try
        {
            var runtime = new OutOfProcessPluginRuntime(GetWorkerPath());
            await using var session = await runtime.StartAsync(pluginDirectory);
            await session.StartPluginAsync();

            var childPid = await WaitForChildPidAsync(pluginDirectory);
            Assert.True(IsProcessAlive(childPid));

            session.TerminateForTest();

            Assert.True(session.WorkerHasExited);
            Assert.NotEqual(PluginLifecycleState.Disabled, session.State.LifecycleState);
            Assert.True(await WaitForProcessExitAsync(childPid));

            // A terminated Worker must not take down the host test process or prevent a new session.
            await using var replacement = await runtime.StartAsync(pluginDirectory);
            await replacement.StartPluginAsync();
            await replacement.StopAsync();
        }
        finally
        {
            DeleteDirectoryWithRetry(pluginDirectory);
        }
    }

    [Fact]
    public async Task HungPluginUiRequestTerminatesWorkerAndRequiresRestart()
    {
        var pluginDirectory = PrepareWorkerChildPlugin();

        try
        {
            var runtime = new OutOfProcessPluginRuntime(
                GetWorkerPath(),
                uiRequestTimeout: TimeSpan.FromMilliseconds(500));
            await using var session = await runtime.StartAsync(pluginDirectory);
            await session.StartPluginAsync();
            var childPid = await WaitForChildPidAsync(pluginDirectory);

            var exception = await Assert.ThrowsAsync<WorkerProtocolException>(
                () => session.ExecuteUiActionAsync("hang"));

            Assert.Equal("PLUGIN_UI_TIMEOUT", exception.ErrorCode);
            Assert.Equal(PluginLifecycleState.RestartRequired, session.State.LifecycleState);
            Assert.True(await WaitForProcessExitAsync(childPid));
            Assert.True(session.WorkerHasExited);
        }
        finally
        {
            DeleteDirectoryWithRetry(pluginDirectory);
        }
    }

    [Fact]
    public async Task CallerCancellationInterruptsPluginUiRequestAndKeepsWorkerUsable()
    {
        var pluginDirectory = PrepareWorkerChildPlugin();

        try
        {
            var runtime = new OutOfProcessPluginRuntime(
                GetWorkerPath(),
                uiRequestTimeout: TimeSpan.FromSeconds(5));
            await using var session = await runtime.StartAsync(pluginDirectory);
            await session.StartPluginAsync();
            using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => session.ExecuteUiActionAsync("hang", cancellationToken: cancellation.Token));

            var snapshot = await session.GetUiSnapshotAsync();
            Assert.NotNull(snapshot);
            Assert.Equal(PluginLifecycleState.Running, session.State.LifecycleState);
            Assert.False(session.WorkerHasExited);

            await session.StopAsync();
        }
        finally
        {
            DeleteDirectoryWithRetry(pluginDirectory);
        }
    }

    [Fact]
    public async Task WorkerLaunchIdentityMismatchFailsHandshake()
    {
        var pluginDirectory = PrepareWorkerChildPlugin();

        try
        {
            var mismatchWorkerPath = Path.Combine(
                AppContext.BaseDirectory,
                "ProtocolMismatchWorker.exe");
            var runtime = new OutOfProcessPluginRuntime(mismatchWorkerPath);

            var exception = await Assert.ThrowsAsync<WorkerProtocolException>(
                () => runtime.StartAsync(pluginDirectory));

            Assert.Equal("WORKER_LAUNCH_ID_MISMATCH", exception.ErrorCode);
        }
        finally
        {
            DeleteDirectoryWithRetry(pluginDirectory);
        }
    }

    private static string GetWorkerPath()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "ToolBox.PluginWorker.exe");
        Assert.True(File.Exists(path), $"PluginWorker executable was not deployed to '{path}'.");
        return path;
    }

    private static string PrepareWorkerChildPlugin()
    {
        var sourceDirectory = AppContext.BaseDirectory;
        var targetDirectory = Path.Combine(
            Path.GetTempPath(),
            "ToolBoxPhase5",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(targetDirectory);

        CopyRequiredFile(sourceDirectory, targetDirectory, "WorkerChildProcessPlugin.dll");
        CopyRequiredFile(sourceDirectory, targetDirectory, "WorkerChildProcessPlugin.deps.json");
        CopyRequiredFile(sourceDirectory, targetDirectory, "WorkerChildProcessPlugin.manifest.json", "manifest.json");

        return targetDirectory;
    }

    private static void CopyRequiredFile(
        string sourceDirectory,
        string targetDirectory,
        string sourceName,
        string? targetName = null)
    {
        var sourcePath = Path.Combine(sourceDirectory, sourceName);
        Assert.True(File.Exists(sourcePath), $"Fixture file was not deployed: '{sourcePath}'.");
        File.Copy(sourcePath, Path.Combine(targetDirectory, targetName ?? sourceName));
    }

    private static async Task<int> WaitForChildPidAsync(string pluginDirectory)
    {
        var pidPath = Path.Combine(pluginDirectory, "child.pid");

        for (var attempt = 0; attempt < 200; attempt++)
        {
            if (File.Exists(pidPath)
                && int.TryParse(
                    await File.ReadAllTextAsync(pidPath).ConfigureAwait(false),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var processId))
            {
                return processId;
            }

            await Task.Delay(25).ConfigureAwait(false);
        }

        throw new Xunit.Sdk.XunitException($"The child pid file was not created at '{pidPath}'.");
    }

    private static async Task<bool> WaitForProcessExitAsync(int processId)
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            if (!IsProcessAlive(processId))
            {
                return true;
            }

            await Task.Delay(25).ConfigureAwait(false);
        }

        return false;
    }

    private static bool IsProcessAlive(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static void DeleteDirectoryWithRetry(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return;
        }

        for (var attempt = 0; attempt < 20; attempt++)
        {
            try
            {
                Directory.Delete(directory, recursive: true);
                return;
            }
            catch (IOException) when (attempt < 19)
            {
                Thread.Sleep(50);
            }
            catch (UnauthorizedAccessException) when (attempt < 19)
            {
                Thread.Sleep(50);
            }
        }

        Assert.False(Directory.Exists(directory), $"Temporary plugin directory was not removed: '{directory}'.");
    }
}
