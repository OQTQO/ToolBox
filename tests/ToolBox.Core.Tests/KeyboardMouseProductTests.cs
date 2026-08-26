using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KeyboardTestPlugin;
using ToolBox.Core.Packaging;
using ToolBox.Core.Plugins;
using ToolBox.PluginSdk;
using ToolBox.PluginSdk.Experimental;
using Xunit;

namespace ToolBox.Core.Tests;

public sealed class KeyboardMouseProductTests
{
    private const string ProductId = "com.toolbox.keyboard-test";
    private const string ProductVersion = "0.1.0";
    private const string ProductVersion2 = "0.2.0";

    [Fact]
    public async Task ProductPackageInstallsResolvesActiveVersionRunsInputAndUninstalls()
    {
        var root = CreateTemporaryRoot();

        try
        {
            using var installer = new PluginPackageInstaller(
                Path.Combine(root, "Plugins"),
                Path.Combine(root, "PluginData"));
            var packagePath = CreateProductPackage(root);
            var installed = await installer.InstallAsync(packagePath);

            Assert.Equal(ProductId, installed.PluginId);
            Assert.Equal(ProductVersion, installed.Version);
            var activeDirectory = installer.GetActiveVersionDirectory(ProductId);
            Assert.Equal(installed.VersionDirectory, activeDirectory);
            Assert.NotNull(activeDirectory);

            var runtime = new InProcessPluginRuntime();
            await ExerciseProductPluginAsync(runtime, activeDirectory!);

            var uninstalled = await installer.UninstallAsync(ProductId, ProductVersion);
            Assert.True(uninstalled.WasActive);
            Assert.Null(uninstalled.ActiveVersionAfterUninstall);
            Assert.Null(installer.GetActiveVersionDirectory(ProductId));
        }
        finally
        {
            DeleteTemporaryRoot(root);
        }
    }

    [Fact]
    public async Task ProductPackageResourceConflictKeepsSecondInstanceFaulted()
    {
        var root = CreateTemporaryRoot();

        try
        {
            using var installer = new PluginPackageInstaller(
                Path.Combine(root, "Plugins"),
                Path.Combine(root, "PluginData"));
            await installer.InstallAsync(CreateProductPackage(root));
            var activeDirectory = installer.GetActiveVersionDirectory(ProductId);
            Assert.NotNull(activeDirectory);

            var runtime = new InProcessPluginRuntime();
            var candidate = runtime.DiscoverSingle(activeDirectory!);
            await using var first = runtime.Load(candidate);
            await using var second = runtime.Load(candidate);

            await first.StartAsync();
            var exception = await Assert.ThrowsAsync<ResourceConflictException>(
                () => second.StartAsync().AsTask());

            Assert.Equal("keyboard.test.surface", exception.ResourceKey.Value);
            Assert.Equal(ProductId, exception.CurrentOwner);
            Assert.Equal(PluginLifecycleState.Faulted, second.State.LifecycleState);
            Assert.Equal("PLUGIN_START_FAILED", second.State.LastErrorCode);

            await second.DisposeAsync();
            await first.StopAndUnloadAsync();
            await installer.UninstallAsync(ProductId, ProductVersion);
        }
        finally
        {
            DeleteTemporaryRoot(root);
        }
    }

    [Fact]
    public async Task ProductPackageUpgradeAndActiveUninstallFallbackRemainLoadable()
    {
        var root = CreateTemporaryRoot();

        try
        {
            using var installer = new PluginPackageInstaller(
                Path.Combine(root, "Plugins"),
                Path.Combine(root, "PluginData"));
            var installedV1 = await installer.InstallAsync(
                CreateProductPackage(root, ProductVersion));
            var installedV2 = await installer.InstallAsync(
                CreateProductPackage(root, ProductVersion2));

            Assert.Equal(ProductVersion, installedV2.PreviousActiveVersion);
            Assert.Equal(installedV2.VersionDirectory, installer.GetActiveVersionDirectory(ProductId));

            var runtime = new InProcessPluginRuntime();
            await ExerciseProductPluginAsync(
                runtime,
                installer.GetActiveVersionDirectory(ProductId)!);

            var removedV2 = await installer.UninstallAsync(ProductId, ProductVersion2);
            Assert.True(removedV2.WasActive);
            Assert.Equal(ProductVersion, removedV2.ActiveVersionAfterUninstall);
            var fallbackDirectory = installer.GetActiveVersionDirectory(ProductId);
            Assert.Equal(installedV1.VersionDirectory, fallbackDirectory);

            await ExerciseProductPluginAsync(runtime, fallbackDirectory!);

            var removedV1 = await installer.UninstallAsync(ProductId, ProductVersion);
            Assert.True(removedV1.WasActive);
            Assert.Null(installer.GetActiveVersionDirectory(ProductId));
        }
        finally
        {
            DeleteTemporaryRoot(root);
        }
    }

    private static async Task ExerciseProductPluginAsync(
        InProcessPluginRuntime runtime,
        string activeDirectory)
    {
        var candidate = runtime.DiscoverSingle(activeDirectory);
        Assert.Equal("Keyboard & Mouse Test", candidate.Manifest.Name);

        await using var loaded = runtime.Load(candidate);
        await ExerciseLoadedPluginAsync(loaded);
        await loaded.StopAndUnloadAsync();

        Assert.Equal(PluginLifecycleState.Disabled, loaded.State.LifecycleState);
        Assert.False(loaded.LoadContextReference.IsAlive);
        Assert.Equal(
            0,
            runtime.ResourceManager.GetActiveLeaseCount(new ResourceKey("keyboard.test.surface")));
    }

    private static async Task ExerciseLoadedPluginAsync(LoadedInProcessPlugin loaded)
    {
        var plugin = Assert.IsAssignableFrom<IKeyboardTestPlugin>(
            loaded.GetCapability<IKeyboardTestPlugin>());

        await loaded.StartAsync();
        plugin.ObserveKey("A", isDown: true);
        plugin.ObserveMouse(KeyboardTestMouseButton.Left, isDown: true, x: 18, y: 24);

        Assert.True(plugin.Snapshot.IsEnabled);
        Assert.Equal(1, plugin.Snapshot.KeyEventCount);
        Assert.Equal(1, plugin.Snapshot.MouseEventCount);
        Assert.Contains("Left", plugin.Snapshot.LastInput, StringComparison.Ordinal);

        var settings = new KeyboardTestSettings(
            IncludeKeyUpEvents: true,
            IncludeMouseEvents: false);
        await plugin.ApplySettingsAsync(settings, CancellationToken.None);
        plugin.ObserveKey("A", isDown: false);
        plugin.ObserveMouse(KeyboardTestMouseButton.Right, isDown: true, x: 3, y: 7);

        Assert.Equal(settings, plugin.Snapshot.Settings);
        Assert.Equal(2, plugin.Snapshot.KeyEventCount);
        Assert.Equal(1, plugin.Snapshot.MouseEventCount);
    }

    private static string CreateProductPackage(string root, string version = ProductVersion)
    {
        var manifest = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "KeyboardTest.manifest.json"));
        manifest = manifest.Replace(
            $"\"version\": \"{ProductVersion}\"",
            $"\"version\": \"{version}\"",
            StringComparison.Ordinal);
        var assemblyPath = typeof(KeyboardTestPlugin.KeyboardTestPlugin).Assembly.Location;
        var assemblyDirectory = Path.GetDirectoryName(assemblyPath)!;
        var payload = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["manifest.json"] = Encoding.UTF8.GetBytes(manifest),
            ["runtime/KeyboardTest.dll"] = File.ReadAllBytes(assemblyPath)
        };

        var depsPath = Path.Combine(assemblyDirectory, "KeyboardTest.deps.json");
        if (File.Exists(depsPath))
        {
            payload["runtime/KeyboardTest.deps.json"] = File.ReadAllBytes(depsPath);
        }

        var files = payload
            .Select(entry => new
            {
                path = entry.Key,
                sha256 = Convert.ToHexString(SHA256.HashData(entry.Value)).ToLowerInvariant()
            })
            .ToArray();
        payload["package.json"] = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(
            new
            {
                packageFormatVersion = 1,
                pluginId = ProductId,
                pluginVersion = version,
                automaticRollbackSupported = true,
                files
            }));

        return CreateArchive(root, $"KeyboardMouse-{Guid.NewGuid():N}.tpk", payload);
    }

    private static string CreateArchive(
        string root,
        string fileName,
        IReadOnlyDictionary<string, byte[]> entries)
    {
        Directory.CreateDirectory(root);
        var packagePath = Path.Combine(root, fileName);

        using (var archive = ZipFile.Open(packagePath, ZipArchiveMode.Create))
        {
            foreach (var entry in entries)
            {
                var zipEntry = archive.CreateEntry(entry.Key, CompressionLevel.Optimal);
                using var stream = zipEntry.Open();
                stream.Write(entry.Value, 0, entry.Value.Length);
            }
        }

        return packagePath;
    }

    private static string CreateTemporaryRoot()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "ToolBoxKeyboardMouseProductTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void DeleteTemporaryRoot(string root)
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

        Assert.False(Directory.Exists(root), $"Keyboard & Mouse Test package directory could not be cleaned: '{root}'.");
    }
}
