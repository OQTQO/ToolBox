using KeyboardTestPlugin;
using ToolBox.Core.Plugins;
using ToolBox.PluginSdk;
using ToolBox.PluginSdk.Experimental;
using Xunit;

namespace ToolBox.Core.Tests;

public sealed class KeyboardTestPluginTests
{
    [Fact]
    public async Task KeyboardTestPluginCapturesInputAppliesSettingsAndUnloads()
    {
        var fixtureRoot = CreateFixtureRoot();

        try
        {
            var runtime = new InProcessPluginRuntime();
            var candidate = runtime.DiscoverSingle(Path.Combine(fixtureRoot, "KeyboardTest"));

            await using var loaded = runtime.Load(candidate);
            await ExercisePluginAsync(loaded);

            Assert.Equal(
                1,
                runtime.ResourceManager.GetActiveLeaseCount(new ToolBox.PluginSdk.ResourceKey("keyboard.test.surface")));

            await loaded.StopAndUnloadAsync();

            Assert.Equal(PluginLifecycleState.Disabled, loaded.State.LifecycleState);
            Assert.False(loaded.LoadContextReference.IsAlive);
            Assert.Equal(
                0,
                runtime.ResourceManager.GetActiveLeaseCount(new ToolBox.PluginSdk.ResourceKey("keyboard.test.surface")));
        }
        finally
        {
            DeleteFixtureRoot(fixtureRoot);
        }
    }

    [Fact]
    public async Task KeyboardTestResourceConflictRemainsFaultedAndReportsTheHolder()
    {
        var fixtureRoot = CreateFixtureRoot();

        try
        {
            var runtime = new InProcessPluginRuntime();
            var candidate = runtime.DiscoverSingle(Path.Combine(fixtureRoot, "KeyboardTest"));
            await using var first = runtime.Load(candidate);
            await using var second = runtime.Load(candidate);

            await first.StartAsync();
            var exception = await Assert.ThrowsAsync<ResourceConflictException>(
                () => second.StartAsync().AsTask());

            Assert.Equal("keyboard.test.surface", exception.ResourceKey.Value);
            Assert.Equal(first.Manifest.Id, exception.CurrentOwner);
            Assert.Equal(PluginLifecycleState.Faulted, second.State.LifecycleState);
            Assert.Equal("PLUGIN_START_FAILED", second.State.LastErrorCode);

            await second.DisposeAsync();
            await first.StopAndUnloadAsync();
        }
        finally
        {
            DeleteFixtureRoot(fixtureRoot);
        }
    }

    private static async Task ExercisePluginAsync(LoadedInProcessPlugin loaded)
    {
        var plugin = Assert.IsAssignableFrom<IKeyboardTestPlugin>(
            loaded.GetCapability<IKeyboardTestPlugin>());
        var snapshots = new List<KeyboardTestSnapshot>();
        plugin.SnapshotChanged += snapshots.Add;

        await loaded.StartAsync();
        plugin.ObserveKey("A", isDown: true);
        plugin.ObserveKey("A", isDown: false);
        plugin.ObserveMouse(KeyboardTestMouseButton.Left, isDown: true, x: 12, y: 20);

        Assert.True(plugin.Snapshot.IsEnabled);
        Assert.Equal(1, plugin.Snapshot.KeyEventCount);
        Assert.Equal(1, plugin.Snapshot.MouseEventCount);
        Assert.Contains("Left", plugin.Snapshot.LastInput, StringComparison.Ordinal);

        var settings = new KeyboardTestSettings(
            IncludeKeyUpEvents: true,
            IncludeMouseEvents: false);
        await plugin.ApplySettingsAsync(settings, CancellationToken.None);
        plugin.ObserveKey("A", isDown: false);
        plugin.ObserveMouse(KeyboardTestMouseButton.Right, isDown: true, x: 4, y: 5);

        Assert.Equal(2, plugin.Snapshot.KeyEventCount);
        Assert.Equal(1, plugin.Snapshot.MouseEventCount);
        Assert.Equal(settings, plugin.Snapshot.Settings);
        Assert.NotEmpty(snapshots);

        plugin.SnapshotChanged -= snapshots.Add;
    }

    private static string CreateFixtureRoot()
    {
        var sourceDirectory = Path.GetDirectoryName(typeof(KeyboardTestPlugin.KeyboardTestPlugin).Assembly.Location)!;
        var sourceAssemblyPath = typeof(KeyboardTestPlugin.KeyboardTestPlugin).Assembly.Location;
        var sourceDepsPath = Path.Combine(sourceDirectory, "KeyboardTest.deps.json");
        var manifestPath = Path.Combine(AppContext.BaseDirectory, "KeyboardTest.manifest.json");
        var root = Path.Combine(Path.GetTempPath(), "ToolBoxKeyboardTest", Guid.NewGuid().ToString("N"));
        var pluginDirectory = Path.Combine(root, "KeyboardTest");

        Directory.CreateDirectory(pluginDirectory);
        File.Copy(sourceAssemblyPath, Path.Combine(pluginDirectory, "KeyboardTest.dll"));

        if (File.Exists(sourceDepsPath))
        {
            File.Copy(sourceDepsPath, Path.Combine(pluginDirectory, "KeyboardTest.deps.json"));
        }

        File.Copy(manifestPath, Path.Combine(pluginDirectory, "manifest.json"));
        return root;
    }

    private static void DeleteFixtureRoot(string root)
    {
        for (var attempt = 0; attempt < 20 && Directory.Exists(root); attempt++)
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

        if (Directory.Exists(root))
        {
            throw new IOException($"KeyboardTest fixture directory could not be cleaned: '{root}'.");
        }
    }
}
