using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using CrashPlugin;
using HangPlugin;
using ToolBox.Core.Lifetime;
using ToolBox.Core.Plugins;
using ToolBox.Core.Plugins.Worker;
using ToolBox.PluginSdk;
using UnloadLeakPlugin;
using Xunit;

namespace ToolBox.Core.Tests;

[SupportedOSPlatform("windows")]
public sealed class FaultFixtureTests
{
    [Fact]
    public async Task CrashPluginStartupFailureRemainsFaulted()
    {
        var fixtureRoot = CreateFixtureRoot(
            "CrashPlugin",
            typeof(CrashPlugin.CrashPlugin),
            "CrashPlugin.manifest.json");

        try
        {
            var state = await ExerciseCrashPluginAsync(fixtureRoot);

            Assert.Equal(PluginLifecycleState.Faulted, state.LifecycleState);
            Assert.Equal("PLUGIN_START_FAILED", state.LastErrorCode);
            Assert.Contains("intentionally failed", state.LastErrorMessage, StringComparison.Ordinal);
        }
        finally
        {
            DeleteFixtureRoot(fixtureRoot);
        }
    }

    [Fact]
    public async Task CrashPluginFailureIsPropagatedAcrossWorkerBoundary()
    {
        var fixtureRoot = CreateFixtureRoot(
            "CrashPlugin",
            typeof(CrashPlugin.CrashPlugin),
            "CrashPlugin.manifest.json");

        try
        {
            var runtime = new OutOfProcessPluginRuntime(GetWorkerPath());
            await using var session = await runtime.StartAsync(
                Path.Combine(fixtureRoot, "CrashPlugin"));

            var exception = await Assert.ThrowsAsync<WorkerProtocolException>(
                () => session.StartPluginAsync());

            Assert.Equal("PLUGIN_START_FAILED", exception.ErrorCode);
            Assert.Equal(PluginLifecycleState.Faulted, session.State.LifecycleState);
            Assert.Equal("PLUGIN_START_FAILED", session.State.LastErrorCode);

            session.Quarantine("CrashPlugin exceeded the startup failure policy.");
            Assert.Equal(PluginLifecycleState.Quarantined, session.State.LifecycleState);
            Assert.Equal("PLUGIN_QUARANTINED", session.State.LastErrorCode);
        }
        finally
        {
            DeleteFixtureRoot(fixtureRoot);
        }
    }

    [Fact]
    public async Task HangPluginCancellationRemainsRestartRequired()
    {
        var fixtureRoot = CreateFixtureRoot(
            "HangPlugin",
            typeof(HangPlugin.HangPlugin),
            "HangPlugin.manifest.json");

        try
        {
            var state = await ExerciseHangPluginAsync(fixtureRoot);

            Assert.Equal(PluginLifecycleState.RestartRequired, state.LifecycleState);
            Assert.Equal("PLUGIN_STOP_FAILED", state.LastErrorCode);
            Assert.NotEqual(PluginLifecycleState.Disabled, state.LifecycleState);
        }
        finally
        {
            DeleteFixtureRoot(fixtureRoot);
        }
    }

    [Fact]
    public async Task HangPluginShutdownDeadlineIsBoundedAndRestartRequired()
    {
        var fixtureRoot = CreateFixtureRoot(
            "HangPlugin",
            typeof(HangPlugin.HangPlugin),
            "HangPlugin.manifest.json");
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var state = await ExerciseHangPluginWithDeadlineAsync(fixtureRoot);
            stopwatch.Stop();

            Assert.Equal(PluginLifecycleState.RestartRequired, state.LifecycleState);
            Assert.Equal("PLUGIN_SHUTDOWN_TIMEOUT", state.LastErrorCode);
            Assert.True(
                stopwatch.Elapsed < TimeSpan.FromSeconds(2),
                $"Shutdown exceeded the single deadline budget: {stopwatch.Elapsed}.");
        }
        finally
        {
            DeleteFixtureRoot(fixtureRoot);
        }
    }

    [Fact]
    public async Task OutOfProcessHangUsesHostShutdownDeadlineAndRequiresRestart()
    {
        var fixtureRoot = CreateFixtureRoot(
            "HangPlugin",
            typeof(HangPlugin.HangPlugin),
            "HangPlugin.manifest.json");

        try
        {
            var runtime = new OutOfProcessPluginRuntime(GetWorkerPath());
            await using var session = await runtime.StartAsync(
                Path.Combine(fixtureRoot, "HangPlugin"));
            await session.StartPluginAsync();

            var stopwatch = Stopwatch.StartNew();
            var exception = await Assert.ThrowsAsync<WorkerProtocolException>(
                () => session.StopAsync(
                    new PluginShutdownOptions(TimeSpan.FromMilliseconds(100))));
            stopwatch.Stop();

            Assert.Equal("PLUGIN_SHUTDOWN_TIMEOUT", exception.ErrorCode);
            Assert.Equal(PluginLifecycleState.RestartRequired, session.State.LifecycleState);
            Assert.True(
                stopwatch.Elapsed < TimeSpan.FromSeconds(2),
                $"Worker shutdown exceeded the single deadline budget: {stopwatch.Elapsed}.");
        }
        finally
        {
            DeleteFixtureRoot(fixtureRoot);
        }
    }

    [Fact]
    public async Task UnloadLeakPluginRemainsRestartRequiredUntilLeakIsReleased()
    {
        var fixtureRoot = CreateFixtureRoot(
            "UnloadLeakPlugin",
            typeof(UnloadLeakPlugin.UnloadLeakPlugin),
            "UnloadLeakPlugin.manifest.json");
        var leakHandle = IntPtr.Zero;

        try
        {
            var result = await ExerciseUnloadLeakPluginAsync(fixtureRoot);
            leakHandle = result.LeakHandle;

            Assert.Equal(PluginLifecycleState.RestartRequired, result.State.LifecycleState);
            Assert.Equal("PLUGIN_ALC_UNLOAD_FAILED", result.State.LastErrorCode);

            GCHandle.FromIntPtr(leakHandle).Free();
            leakHandle = IntPtr.Zero;

            Assert.True(await WaitForUnloadAsync(result.LoadContextReference));
        }
        finally
        {
            if (leakHandle != IntPtr.Zero)
            {
                GCHandle.FromIntPtr(leakHandle).Free();
            }

            DeleteFixtureRoot(fixtureRoot);
        }
    }

    private static async Task<PluginState> ExerciseCrashPluginAsync(string fixtureRoot)
    {
        var runtime = new InProcessPluginRuntime();
        var loaded = runtime.Load(
            runtime.DiscoverSingle(Path.Combine(fixtureRoot, "CrashPlugin")));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => loaded.StartAsync().AsTask());

        return loaded.State;
    }

    private static async Task<PluginState> ExerciseHangPluginAsync(string fixtureRoot)
    {
        var runtime = new InProcessPluginRuntime();
        var loaded = runtime.Load(
            runtime.DiscoverSingle(Path.Combine(fixtureRoot, "HangPlugin")));
        await loaded.StartAsync();

        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => loaded.StopAndUnloadAsync(cancellation.Token).AsTask());

        return loaded.State;
    }

    private static async Task<PluginState> ExerciseHangPluginWithDeadlineAsync(string fixtureRoot)
    {
        var runtime = new InProcessPluginRuntime();
        var loaded = runtime.Load(
            runtime.DiscoverSingle(Path.Combine(fixtureRoot, "HangPlugin")));
        await loaded.StartAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => loaded.StopAndUnloadAsync(
                    new PluginShutdownOptions(TimeSpan.FromMilliseconds(100)))
                .AsTask());

        return loaded.State;
    }

    private static async Task<UnloadLeakResult> ExerciseUnloadLeakPluginAsync(string fixtureRoot)
    {
        var runtime = new InProcessPluginRuntime();
        var loaded = runtime.Load(
            runtime.DiscoverSingle(Path.Combine(fixtureRoot, "UnloadLeakPlugin")));
        await loaded.StartAsync();

        var handlePath = Path.Combine(fixtureRoot, "UnloadLeakPlugin", "leak.handle");
        var leakHandle = await WaitForHandleAsync(handlePath);
        var loadContextReference = loaded.LoadContextReference;

        var exception = await Assert.ThrowsAsync<PluginLoadException>(
            () => loaded.StopAndUnloadAsync().AsTask());

        Assert.Equal("PLUGIN_ALC_UNLOAD_FAILED", exception.ErrorCode);
        return new UnloadLeakResult(loaded.State, loadContextReference, leakHandle);
    }

    private static async Task<IntPtr> WaitForHandleAsync(string handlePath)
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            if (File.Exists(handlePath)
                && long.TryParse(
                    await File.ReadAllTextAsync(handlePath),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var handleValue))
            {
                return new IntPtr(handleValue);
            }

            await Task.Delay(25);
        }

        throw new Xunit.Sdk.XunitException($"The leak handle file was not created: '{handlePath}'.");
    }

    private static async Task<bool> WaitForUnloadAsync(WeakReference loadContextReference)
    {
        for (var attempt = 0; attempt < 100 && loadContextReference.IsAlive; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            await Task.Delay(25);
        }

        return !loadContextReference.IsAlive;
    }

    private static string GetWorkerPath()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "ToolBox.PluginWorker.exe");
        Assert.True(File.Exists(path), $"PluginWorker executable was not deployed to '{path}'.");
        return path;
    }

    private static string CreateFixtureRoot(
        string fixtureName,
        Type fixtureType,
        string manifestName)
    {
        var sourceDirectory = Path.GetDirectoryName(fixtureType.Assembly.Location)!;
        var root = Path.Combine(
            Path.GetTempPath(),
            "ToolBoxFaultFixtures",
            Guid.NewGuid().ToString("N"));
        var pluginDirectory = Path.Combine(root, fixtureName);
        Directory.CreateDirectory(pluginDirectory);

        var assemblyName = fixtureType.Assembly.GetName().Name
            ?? throw new InvalidOperationException("The fixture assembly name is missing.");
        CopyRequiredFile(
            Path.Combine(sourceDirectory, assemblyName + ".dll"),
            Path.Combine(pluginDirectory, assemblyName + ".dll"));

        var depsPath = Path.Combine(sourceDirectory, assemblyName + ".deps.json");
        if (File.Exists(depsPath))
        {
            File.Copy(depsPath, Path.Combine(pluginDirectory, assemblyName + ".deps.json"));
        }

        CopyRequiredFile(
            Path.Combine(AppContext.BaseDirectory, manifestName),
            Path.Combine(pluginDirectory, "manifest.json"));
        return root;
    }

    private static void CopyRequiredFile(string sourcePath, string destinationPath)
    {
        Assert.True(File.Exists(sourcePath), $"Fixture file was not deployed: '{sourcePath}'.");
        File.Copy(sourcePath, destinationPath);
    }

    private static void DeleteFixtureRoot(string root)
    {
        for (var attempt = 0; attempt < 30 && Directory.Exists(root); attempt++)
        {
            try
            {
                foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                }

                Directory.Delete(root, recursive: true);
            }
            catch (IOException) when (Directory.Exists(root))
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                Thread.Sleep(50);
            }
            catch (UnauthorizedAccessException) when (Directory.Exists(root))
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                Thread.Sleep(50);
            }
        }

        Assert.False(Directory.Exists(root), $"Fixture directory could not be cleaned: '{root}'.");
    }

    private sealed record UnloadLeakResult(
        PluginState State,
        WeakReference LoadContextReference,
        IntPtr LeakHandle);
}
