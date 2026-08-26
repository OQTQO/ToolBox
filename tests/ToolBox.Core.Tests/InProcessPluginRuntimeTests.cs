using HappyPathPlugin;
using ToolBox.Core.Plugins;
using ToolBox.PluginSdk;
using Xunit;

namespace ToolBox.Core.Tests;

public sealed class InProcessPluginRuntimeTests
{
    [Fact]
    public async Task HappyPathPluginLoadsStartsStopsAndUnloads()
    {
        var fixtureRoot = CreateFixtureRoot();

        try
        {
            var runtime = new InProcessPluginRuntime();
            var discovered = runtime.Discover(fixtureRoot);

            var candidate = Assert.Single(discovered);
            Assert.Equal("com.toolbox.happy-path", candidate.Manifest.Id);

            await using var loaded = runtime.Load(candidate);
            var loadContextReference = loaded.LoadContextReference;

            Assert.Equal(PluginLifecycleState.Disabled, loaded.State.LifecycleState);
            Assert.True(loadContextReference.IsAlive);

            await loaded.StartAsync();
            Assert.Equal(PluginLifecycleState.Running, loaded.State.LifecycleState);

            await loaded.StopAndUnloadAsync();

            Assert.Equal(PluginLifecycleState.Disabled, loaded.State.LifecycleState);
            Assert.False(loadContextReference.IsAlive);
        }
        finally
        {
            DeleteFixtureRoot(fixtureRoot);
        }
    }

    private static string CreateFixtureRoot()
    {
        var sourceDirectory = Path.GetDirectoryName(typeof(HappyPathPlugin.HappyPathPlugin).Assembly.Location)!;
        var sourceAssemblyPath = typeof(HappyPathPlugin.HappyPathPlugin).Assembly.Location;
        var sourceDepsPath = Path.Combine(sourceDirectory, "HappyPathPlugin.deps.json");
        var manifestPath = Path.Combine(AppContext.BaseDirectory, "HappyPathPlugin.manifest.json");
        var root = Path.Combine(Path.GetTempPath(), "ToolBoxHappyPath", Guid.NewGuid().ToString("N"));
        var pluginDirectory = Path.Combine(root, "HappyPathPlugin");

        Directory.CreateDirectory(pluginDirectory);
        File.Copy(sourceAssemblyPath, Path.Combine(pluginDirectory, "HappyPathPlugin.dll"));

        if (File.Exists(sourceDepsPath))
        {
            File.Copy(sourceDepsPath, Path.Combine(pluginDirectory, "HappyPathPlugin.deps.json"));
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
            throw new IOException($"HappyPath fixture directory could not be cleaned: '{root}'.");
        }
    }
}
