using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ToolBox.Core.Diagnostics;
using ToolBox.Core.Packaging;
using ToolBox.Core.Plugins;
using ToolBox.Host;
using ToolBox.PluginSdk;
using Xunit;

namespace ToolBox.Host.Tests;

public sealed class DynamicPluginWorkspaceTests
{
    [Fact]
    public void HostDiscoversEveryCommittedPluginWithoutAPluginSpecificRegistration()
    {
        using var fixture = new Fixture();
        fixture.WriteInstalledPlugin("com.example.alpha", "1.0.0", "Alpha");
        fixture.WriteInstalledPlugin("com.example.beta", "2.0.0", "Beta");

        using var installer = new PluginPackageInstaller(fixture.PluginsRoot, fixture.DataRoot);
        var snapshot = new InstalledPluginCatalog(installer).Scan();

        Assert.Equal(["com.example.alpha", "com.example.beta"], snapshot.Plugins.Select(plugin => plugin.PluginId));
        Assert.Empty(snapshot.Issues);
    }

    [Fact]
    public async Task InstallingAValidPackageCreatesAGenericWorkspace()
    {
        using var fixture = new Fixture();
        var packagePath = fixture.CreatePackage("com.example.installed", "1.0.0", "Installed Sample");
        using var installer = new PluginPackageInstaller(fixture.PluginsRoot, fixture.DataRoot);
        var settings = new HostSettingsService(fixture.SettingsPath);
        var localization = new LocalizationService(settings);
        await using var logger = new StructuredLogger(
            new LoggerOptions { DirectoryPath = fixture.LogsRoot },
            "session",
            "0.2.0");
        using var viewModel = new MainWindowViewModel(
            new HostDiagnostics("launch", "session", "0.2.0"),
            logger,
            new InstalledPluginCatalog(installer),
            installer,
            new OutOfProcessPluginRuntime(Path.Combine(AppContext.BaseDirectory, "ToolBox.PluginWorker.exe")),
            localization,
            settings);

        await viewModel.InstallPackageAsync(packagePath);

        var workspace = Assert.Single(viewModel.PluginWorkspaces);
        Assert.Equal("com.example.installed", workspace.PluginId);
        Assert.Equal("Installed Sample", workspace.DisplayName);
        Assert.Equal("1.0.0", workspace.InstalledVersion);
        Assert.True(workspace.IsOpened);
        Assert.Equal(PluginLifecycleState.Disabled, workspace.LifecycleState);
        Assert.False(workspace.IsRuntimeEnabled);
    }

    [Fact]
    public async Task PackageOperationsRefreshWorkspaceCollectionsOnTheUiDispatcher()
    {
        using var fixture = new Fixture();
        var packagePath = fixture.CreatePackage("com.example.dispatch", "1.0.0", "Dispatch Sample");
        using var installer = new PluginPackageInstaller(fixture.PluginsRoot, fixture.DataRoot);
        var settings = new HostSettingsService(fixture.SettingsPath);
        var localization = new LocalizationService(settings);
        await using var logger = new StructuredLogger(
            new LoggerOptions { DirectoryPath = fixture.LogsRoot },
            "session",
            "0.2.0");
        var dispatcher = new QueuedHostUiDispatcher();
        using var viewModel = new MainWindowViewModel(
            new HostDiagnostics("launch", "session", "0.2.0"),
            logger,
            new InstalledPluginCatalog(installer),
            installer,
            new OutOfProcessPluginRuntime(Path.Combine(AppContext.BaseDirectory, "ToolBox.PluginWorker.exe")),
            localization,
            settings,
            dispatcher);

        dispatcher.Drain();
        Assert.Empty(viewModel.PluginWorkspaces);

        await viewModel.InstallPackageAsync(packagePath);

        // The package operation resumes off the WPF thread. The collection
        // must remain untouched until its queued UI refresh is processed.
        Assert.Empty(viewModel.PluginWorkspaces);
        dispatcher.Drain();
        var workspace = Assert.Single(viewModel.PluginWorkspaces);

        await viewModel.UninstallWorkspaceAsync(workspace);

        Assert.Single(viewModel.PluginWorkspaces);
        dispatcher.Drain();
        Assert.Empty(viewModel.PluginWorkspaces);
        Assert.Empty(viewModel.InstalledPluginWorkspaces);
        Assert.Empty(installer.GetInstalledVersions(workspace.PluginId));
        Assert.Null(installer.GetActiveVersionDirectory(workspace.PluginId));
    }

    [Fact]
    public async Task InvalidStateAndStagingDirectoriesDoNotPreventHostStartup()
    {
        using var fixture = new Fixture();
        fixture.WriteInstalledPlugin("com.example.good", "1.0.0", "Good");
        Directory.CreateDirectory(Path.Combine(fixture.PluginsRoot, ".staging", "unfinished"));
        var brokenRoot = Path.Combine(fixture.PluginsRoot, "com.example.broken");
        Directory.CreateDirectory(brokenRoot);
        await File.WriteAllTextAsync(Path.Combine(brokenRoot, "state.json"), "{ this is not valid json }");

        using var installer = new PluginPackageInstaller(fixture.PluginsRoot, fixture.DataRoot);
        var settings = new HostSettingsService(fixture.SettingsPath);
        var localization = new LocalizationService(settings);
        await using var logger = new StructuredLogger(
            new LoggerOptions { DirectoryPath = fixture.LogsRoot },
            "session",
            "0.2.0");
        using var viewModel = new MainWindowViewModel(
            new HostDiagnostics("launch", "session", "0.2.0"),
            logger,
            new InstalledPluginCatalog(installer),
            installer,
            new OutOfProcessPluginRuntime(Path.Combine(AppContext.BaseDirectory, "ToolBox.PluginWorker.exe")),
            localization,
            settings);

        Assert.Single(viewModel.PluginWorkspaces);
        Assert.Equal("com.example.good", viewModel.PluginWorkspaces[0].PluginId);
        Assert.Equal(PluginLifecycleState.Disabled, viewModel.PluginWorkspaces[0].LifecycleState);
        Assert.False(viewModel.PluginWorkspaces[0].IsRuntimeEnabled);
        Assert.Contains(viewModel.RecentEvents, entry => entry.Message.Contains("broken", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task UnsupportedRuntimeIsVisibleAndCannotBeEnabled()
    {
        using var fixture = new Fixture();
        fixture.WriteInstalledPlugin("com.example.inprocess", "1.0.0", "InProcess only", supportsOutOfProcess: false);

        using var installer = new PluginPackageInstaller(fixture.PluginsRoot, fixture.DataRoot);
        var settings = new HostSettingsService(fixture.SettingsPath);
        var localization = new LocalizationService(settings);
        await using var logger = new StructuredLogger(
            new LoggerOptions { DirectoryPath = fixture.LogsRoot },
            "session",
            "0.2.0");
        using var viewModel = new MainWindowViewModel(
            new HostDiagnostics("launch", "session", "0.2.0"),
            logger,
            new InstalledPluginCatalog(installer),
            installer,
            new OutOfProcessPluginRuntime(Path.Combine(AppContext.BaseDirectory, "ToolBox.PluginWorker.exe")),
            localization,
            settings);

        var workspace = Assert.Single(viewModel.PluginWorkspaces);
        Assert.False(workspace.SupportsOutOfProcess);
        Assert.False(workspace.IsRuntimeActionEnabled);
        Assert.Contains("outOfProcess", workspace.ErrorMessage, StringComparison.Ordinal);
    }

    private sealed class Fixture : IDisposable
    {
        private static readonly JsonSerializerOptions StateJsonOptions = CreateStateJsonOptions();
        private readonly string _root = Path.Combine(Path.GetTempPath(), "ToolBox.Host.Dynamic", Guid.NewGuid().ToString("N"));

        public Fixture()
        {
            PluginsRoot = Path.Combine(_root, "Plugins");
            DataRoot = Path.Combine(_root, "Data");
            LogsRoot = Path.Combine(_root, "Logs");
            SettingsPath = Path.Combine(_root, "ui-settings.json");
            Directory.CreateDirectory(PluginsRoot);
        }

        public string PluginsRoot { get; }
        public string DataRoot { get; }
        public string LogsRoot { get; }
        public string SettingsPath { get; }

        public string CreatePackage(string id, string version, string name)
        {
            var manifest = $$"""
                {
                  "formatVersion": 1,
                  "id": "{{id}}",
                  "name": "{{name}}",
                  "version": "{{version}}",
                  "pluginApiMajor": 1,
                  "publisher": "example.test",
                  "platform": { "os": "windows", "arch": "x64" },
                  "runtime": {
                    "supportedModes": ["outOfProcess"],
                    "preferredMode": "outOfProcess",
                    "background": true
                  },
                  "entryPoint": "Example.Plugin, Example"
                }
                """;
            var payload = new Dictionary<string, byte[]>(StringComparer.Ordinal)
            {
                ["manifest.json"] = Encoding.UTF8.GetBytes(manifest),
                ["runtime/Example.dll"] = [0x4D, 0x5A, 0x01, 0x02]
            };
            var files = payload.Select(entry => new
            {
                path = entry.Key,
                sha256 = Convert.ToHexString(SHA256.HashData(entry.Value)).ToLowerInvariant()
            }).ToArray();
            payload["package.json"] = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
            {
                packageFormatVersion = 1,
                pluginId = id,
                pluginVersion = version,
                automaticRollbackSupported = true,
                files
            }));

            var packagePath = Path.Combine(_root, $"{id}-{version}.tpk");
            using var archive = ZipFile.Open(packagePath, ZipArchiveMode.Create);
            foreach (var entry in payload)
            {
                var zipEntry = archive.CreateEntry(entry.Key, CompressionLevel.Optimal);
                using var stream = zipEntry.Open();
                stream.Write(entry.Value, 0, entry.Value.Length);
            }

            return packagePath;
        }

        public void WriteInstalledPlugin(
            string id,
            string version,
            string name,
            bool supportsOutOfProcess = true)
        {
            var root = Path.Combine(PluginsRoot, id);
            var versionDirectory = Path.Combine(root, "versions", version);
            Directory.CreateDirectory(versionDirectory);
            File.WriteAllText(Path.Combine(versionDirectory, "manifest.json"), $$"""
                {
                  "formatVersion": 1,
                  "id": "{{id}}",
                  "name": "{{name}}",
                  "version": "{{version}}",
                  "pluginApiMajor": 1,
                  "publisher": "example.test",
                  "platform": { "os": "windows", "arch": "x64" },
                  "runtime": {
                    "supportedModes": ["{{(supportsOutOfProcess ? "outOfProcess" : "inProcess")}}"],
                    "preferredMode": "{{(supportsOutOfProcess ? "outOfProcess" : "inProcess")}}",
                    "background": true
                  },
                  "entryPoint": "Example.Plugin, Example"
                }
                """);

            var state = new PluginPackageState(
                1,
                id,
                version,
                version,
                version,
                "test",
                PluginPackageStatePhase.Committed,
                1,
                true,
                DateTimeOffset.UtcNow);
            File.WriteAllText(Path.Combine(root, "state.json"), JsonSerializer.Serialize(state, StateJsonOptions));
        }

        private static JsonSerializerOptions CreateStateJsonOptions()
        {
            var options = new JsonSerializerOptions();
            options.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
            return options;
        }

        public void Dispose()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
    }

    private sealed class QueuedHostUiDispatcher : IHostUiDispatcher
    {
        private readonly Queue<Action> _pending = new();
        private int _uiThreadId;

        public void Dispatch(Action action)
        {
            ArgumentNullException.ThrowIfNull(action);

            if (_uiThreadId == Environment.CurrentManagedThreadId)
            {
                action();
                return;
            }

            lock (_pending)
            {
                _pending.Enqueue(action);
            }
        }

        public void Drain()
        {
            _uiThreadId = Environment.CurrentManagedThreadId;
            while (true)
            {
                Action? action = null;
                lock (_pending)
                {
                    if (_pending.Count > 0)
                    {
                        action = _pending.Dequeue();
                    }
                }

                if (action is null)
                {
                    return;
                }

                action();
            }
        }
    }
}
