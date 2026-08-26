using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using ToolBox.Core.Plugins;
using ToolBox.PluginSdk;
using Xunit;

namespace ToolBox.Core.Tests;

public sealed class PluginSdkCompatibilityTests
{
    [Fact]
    public async Task LegacyPluginCompiledAgainstPinnedSdkLoadsAgainstCurrentSdk()
    {
        var sourceDirectory = Path.Combine(AppContext.BaseDirectory, "PluginSdkCompatibility");
        var legacyAssemblyPath = Path.Combine(sourceDirectory, "LegacyPlugin.dll");
        var legacyDepsPath = Path.Combine(sourceDirectory, "LegacyPlugin.deps.json");
        var legacyManifestPath = Path.Combine(sourceDirectory, "manifest.json");

        Assert.True(File.Exists(legacyAssemblyPath), $"Missing legacy fixture: '{legacyAssemblyPath}'.");
        Assert.True(File.Exists(legacyDepsPath), $"Missing legacy dependency metadata: '{legacyDepsPath}'.");
        Assert.True(File.Exists(legacyManifestPath), $"Missing legacy manifest: '{legacyManifestPath}'.");
        Assert.False(
            File.Exists(Path.Combine(sourceDirectory, "ToolBox.PluginSdk.dll")),
            "The compatibility fixture must resolve the current shared SDK instead of shipping a private SDK copy.");

        await using (var legacyAssembly = File.OpenRead(legacyAssemblyPath))
        using (var peReader = new PEReader(legacyAssembly))
        {
            var metadata = peReader.GetMetadataReader();
            var sdkReference = metadata.AssemblyReferences
                .Select(metadata.GetAssemblyReference)
                .Single(reference => string.Equals(
                    metadata.GetString(reference.Name),
                    "ToolBox.PluginSdk",
                    StringComparison.Ordinal));

            Assert.Equal(new Version(0, 0, 1, 0), sdkReference.Version);
        }

        var root = Path.Combine(
            Path.GetTempPath(),
            "ToolBoxPluginSdkCompatibility",
            Guid.NewGuid().ToString("N"));
        var pluginDirectory = Path.Combine(root, "LegacyPlugin");

        try
        {
            Directory.CreateDirectory(pluginDirectory);
            foreach (var file in Directory.EnumerateFiles(sourceDirectory, "LegacyPlugin.*"))
            {
                File.Copy(file, Path.Combine(pluginDirectory, Path.GetFileName(file)));
            }

            File.Copy(legacyManifestPath, Path.Combine(pluginDirectory, "manifest.json"));

            var runtime = new InProcessPluginRuntime();
            var discovered = runtime.DiscoverSingle(pluginDirectory);

            await using var loaded = runtime.Load(discovered);
            await loaded.StartAsync();

            Assert.Equal(PluginLifecycleState.Running, loaded.State.LifecycleState);
            await loaded.StopAndUnloadAsync();
            Assert.Equal(PluginLifecycleState.Disabled, loaded.State.LifecycleState);
            Assert.False(loaded.LoadContextReference.IsAlive);
        }
        finally
        {
            DeleteTemporaryRoot(root);
        }
    }

    private static void DeleteTemporaryRoot(string root)
    {
        for (var attempt = 0; attempt < 20 && Directory.Exists(root); attempt++)
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch (IOException) when (Directory.Exists(root))
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                Thread.Sleep(50);
            }
        }

        Assert.False(Directory.Exists(root), $"Compatibility fixture could not be cleaned: '{root}'.");
    }
}
