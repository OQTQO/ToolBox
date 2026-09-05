using Xunit;

namespace ToolBox.Host.Tests;

public sealed class HostLaunchOptionsTests
{
    [Fact]
    public void ParsesIsolatedAcceptanceRootAndPackage()
    {
        var root = Path.Combine(Path.GetTempPath(), "ToolBoxHostLaunchOptions", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var package = Path.Combine(root, "sample.tpk");
        File.WriteAllText(package, string.Empty);

        try
        {
            var options = HostLaunchOptions.Parse(
                ["--ui-acceptance-root", root, "--ui-acceptance-package", package]);

            Assert.Equal(Path.GetFullPath(root), options.UiAcceptanceRoot);
            Assert.Equal(Path.GetFullPath(package), options.UiAcceptancePackage);

            var storage = HostStoragePaths.Create(options.UiAcceptanceRoot);
            Assert.Equal(Path.GetFullPath(root), storage.DataRoot);
            Assert.Equal(Path.Combine(root, "Plugins"), storage.PluginsRoot);
            Assert.Equal(Path.Combine(root, "PluginData"), storage.PluginDataRoot);
            Assert.Equal(Path.Combine(root, "Logs"), storage.LogsRoot);
            Assert.Equal(Path.Combine(root, "ui-settings.json"), storage.SettingsPath);
            Assert.True(storage.IsAcceptance);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void NormalStorageUsesInstallDataDirectory()
    {
        var storage = HostStoragePaths.Create(null);
        var expectedDataRoot = Path.Combine(AppContext.BaseDirectory, "Data");

        Assert.Equal(expectedDataRoot, storage.DataRoot);
        Assert.Equal(Path.Combine(expectedDataRoot, "Plugins"), storage.PluginsRoot);
        Assert.Equal(Path.Combine(expectedDataRoot, "PluginData"), storage.PluginDataRoot);
        Assert.Equal(Path.Combine(expectedDataRoot, "Logs"), storage.LogsRoot);
        Assert.Equal(Path.Combine(expectedDataRoot, "ui-settings.json"), storage.SettingsPath);
        Assert.False(storage.IsAcceptance);
    }

    [Fact]
    public void LegacyDataMigrationCopiesMissingFilesKeepsSourcesAndIsIdempotent()
    {
        var root = Path.Combine(Path.GetTempPath(), "ToolBoxHostDataMigration", Guid.NewGuid().ToString("N"));
        var legacyInstallRoot = Path.Combine(root, "legacy-install");
        var legacyDataRoot = Path.Combine(root, "legacy-data");
        var currentDataRoot = Path.Combine(root, "current", "Data");
        var storage = new HostStoragePaths(
            currentDataRoot,
            Path.Combine(currentDataRoot, "Plugins"),
            Path.Combine(currentDataRoot, "PluginData"),
            Path.Combine(currentDataRoot, "Logs"),
            Path.Combine(currentDataRoot, "ui-settings.json"),
            false);

        try
        {
            Directory.CreateDirectory(Path.Combine(legacyInstallRoot, "Plugins", "com.example.legacy"));
            Directory.CreateDirectory(Path.Combine(legacyDataRoot, "Plugins", "com.example.legacy"));
            Directory.CreateDirectory(Path.Combine(legacyDataRoot, "Logs"));
            Directory.CreateDirectory(currentDataRoot);
            File.WriteAllText(
                Path.Combine(legacyInstallRoot, "Plugins", "com.example.legacy", "manifest.json"),
                "legacy-plugin");
            File.WriteAllText(
                Path.Combine(legacyDataRoot, "Plugins", "com.example.legacy", "state.json"),
                "legacy-state");
            File.WriteAllText(Path.Combine(legacyDataRoot, "Logs", "host.log"), "legacy-log");
            File.WriteAllText(Path.Combine(legacyDataRoot, "ui-settings.json"), "legacy-settings");

            Directory.CreateDirectory(Path.GetDirectoryName(storage.SettingsPath)!);
            File.WriteAllText(storage.SettingsPath, "current-settings");

            var result = HostDataMigration.Migrate(storage, legacyInstallRoot, legacyDataRoot);

            Assert.True(result.CopiedFileCount >= 3);
            Assert.Empty(result.Warnings);
            Assert.Equal("legacy-plugin", File.ReadAllText(Path.Combine(storage.PluginsRoot, "com.example.legacy", "manifest.json")));
            Assert.Equal("legacy-state", File.ReadAllText(Path.Combine(storage.PluginDataRoot, "com.example.legacy", "state.json")));
            Assert.Equal("legacy-log", File.ReadAllText(Path.Combine(storage.LogsRoot, "host.log")));
            Assert.Equal("current-settings", File.ReadAllText(storage.SettingsPath));
            Assert.True(File.Exists(Path.Combine(storage.DataRoot, HostDataMigration.MarkerFileName)));

            File.WriteAllText(Path.Combine(legacyDataRoot, "Logs", "new.log"), "must-not-copy-after-marker");
            var secondResult = HostDataMigration.Migrate(storage, legacyInstallRoot, legacyDataRoot);

            Assert.Equal(0, secondResult.CopiedFileCount);
            Assert.False(File.Exists(Path.Combine(storage.LogsRoot, "new.log")));
            Assert.True(File.Exists(Path.Combine(legacyDataRoot, "Logs", "host.log")));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void PackageRequiresAcceptanceRoot()
    {
        var package = Path.Combine(Path.GetTempPath(), $"toolbox-{Guid.NewGuid():N}.tpk");
        File.WriteAllText(package, string.Empty);

        try
        {
            var exception = Assert.Throws<ArgumentException>(() =>
                HostLaunchOptions.Parse(["--ui-acceptance-package", package]));

            Assert.Contains("--ui-acceptance-root", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(package);
        }
    }
}
