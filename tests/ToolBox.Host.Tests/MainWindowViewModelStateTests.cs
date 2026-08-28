using System.ComponentModel;
using System.IO.Compression;
using System.Text;
using System.Windows.Media;
using ToolBox.Core.Diagnostics;
using ToolBox.Core.Packaging;
using ToolBox.Host;
using Xunit;

namespace ToolBox.Host.Tests;

public sealed class MainWindowViewModelStateTests
{
    [Fact]
    public async Task NavigationAndSettingsCollectionsAreDrivenByWorkspaceRegistrations()
    {
        using var fixture = new HostFixture();
        var settings = new HostSettingsService(fixture.SettingsPath);
        settings.SetPluginOpened("test.workspace.one", opened: true);
        settings.SetPluginOpened("test.workspace.two", opened: false);
        var localization = new LocalizationService(settings);
        var logger = new StructuredLogger(
            new LoggerOptions { DirectoryPath = fixture.LogsRoot },
            "session",
            "0.1.0");
        var firstState = new FakeWorkspaceState();
        var secondState = new FakeWorkspaceState();
        using var viewModel = new MainWindowViewModel(
            new HostDiagnostics("launch", "session", "0.1.0"),
            logger,
            [
                CreateRegistration("test.workspace.one", firstState),
                CreateRegistration("test.workspace.two", secondState)
            ],
            localization,
            settings);
        var first = viewModel.PluginWorkspaces[0];
        var second = viewModel.PluginWorkspaces[1];

        try
        {
            Assert.Equal(2, viewModel.InstalledPluginWorkspaces.Count);
            Assert.Equal([first], viewModel.OpenedPluginWorkspaces);

            viewModel.SelectPluginWorkspace(first);
            Assert.True(viewModel.IsPluginPage);
            Assert.True(first.IsSelected);

            viewModel.SelectPluginWorkspace(second);
            Assert.True(viewModel.IsOverviewPage);
            Assert.Null(viewModel.SelectedPluginWorkspace);
            Assert.False(first.IsSelected);

            await viewModel.ToggleWorkspaceOpenedAsync(second);
            Assert.Equal([first, second], viewModel.OpenedPluginWorkspaces);

            viewModel.SelectPluginWorkspace(second);
            await viewModel.ToggleWorkspaceOpenedAsync(second);

            Assert.True(viewModel.IsSettingsPage);
            Assert.Null(viewModel.SelectedPluginWorkspace);
            Assert.Equal(1, secondState.StopRequests);
        }
        finally
        {
            await logger.DisposeAsync();
        }
    }

    [Fact]
    public async Task GenericWorkspaceUninstallRemovesNavigationAndPersistedOpenState()
    {
        using var fixture = new HostFixture();
        var settings = new HostSettingsService(fixture.SettingsPath);
        settings.SetPluginOpened("test.workspace", opened: true);
        var localization = new LocalizationService(settings);
        var logger = new StructuredLogger(
            new LoggerOptions { DirectoryPath = fixture.LogsRoot },
            "session",
            "0.1.0");
        var state = new FakeWorkspaceState();
        using var viewModel = new MainWindowViewModel(
            new HostDiagnostics("launch", "session", "0.1.0"),
            logger,
            [CreateRegistration("test.workspace", state)],
            localization,
            settings);
        var workspace = Assert.Single(viewModel.PluginWorkspaces);

        try
        {
            viewModel.SelectPluginWorkspace(workspace);
            await viewModel.UninstallWorkspaceAsync(workspace);

            Assert.Empty(viewModel.InstalledPluginWorkspaces);
            Assert.Empty(viewModel.OpenedPluginWorkspaces);
            Assert.True(viewModel.IsSettingsPage);
            Assert.DoesNotContain(
                "test.workspace",
                File.ReadAllText(fixture.SettingsPath),
                StringComparison.Ordinal);
        }
        finally
        {
            await logger.DisposeAsync();
        }
    }

    [Fact]
    public async Task TargetedWorkspaceInstallRejectsPackageForDifferentPlugin()
    {
        using var fixture = new HostFixture();
        var settings = new HostSettingsService(fixture.SettingsPath);
        var localization = new LocalizationService(settings);
        var logger = new StructuredLogger(
            new LoggerOptions { DirectoryPath = fixture.LogsRoot },
            "session",
            "0.1.0");
        var state = new FakeWorkspaceState();
        using var viewModel = new MainWindowViewModel(
            new HostDiagnostics("launch", "session", "0.1.0"),
            logger,
            [CreateRegistration("test.workspace.expected", state)],
            localization,
            settings);
        var workspace = Assert.Single(viewModel.PluginWorkspaces);
        var packagePath = Path.Combine(fixture.PluginsRoot, "wrong-plugin.tpk");
        Directory.CreateDirectory(fixture.PluginsRoot);
        using (var archive = ZipFile.Open(packagePath, ZipArchiveMode.Create))
        {
            var manifest = archive.CreateEntry("manifest.json");
            await using var stream = manifest.Open();
            await using var writer = new StreamWriter(stream, Encoding.UTF8);
            await writer.WriteAsync("""
                {
                  "formatVersion": 1,
                  "id": "test.workspace.different",
                  "name": "Different workspace",
                  "version": "1.0.0",
                  "pluginApiMajor": 1,
                  "publisher": "toolbox.tests",
                  "platform": { "os": "windows", "arch": "x64" },
                  "runtime": {
                    "supportedModes": ["inProcess"],
                    "preferredMode": "inProcess",
                    "background": false
                  },
                  "entryPoint": "Test.Plugin, Test"
                }
                """);
        }

        try
        {
            await viewModel.InstallWorkspacePackageAsync(workspace, packagePath);

            Assert.Equal(0, state.InstallRequests);
            Assert.True(viewModel.HasPluginManagerError);
            Assert.Contains("test.workspace.different", viewModel.PluginManagerError, StringComparison.Ordinal);
        }
        finally
        {
            await logger.DisposeAsync();
        }
    }

    [Fact]
    public async Task OpenVisibilityAndRuntimeRemainIndependent()
    {
        using var fixture = new HostFixture();
        var settings = new HostSettingsService(fixture.SettingsPath);
        settings.SetPluginOpened("com.toolbox.keyboard-test", opened: true);
        var localization = new LocalizationService(settings);
        var logger = new StructuredLogger(
            new LoggerOptions { DirectoryPath = fixture.LogsRoot },
            "session",
            "0.1.0");
        using var installer = new PluginPackageInstaller(fixture.PluginsRoot, fixture.PluginDataRoot);
        var registrations = BuiltInPluginWorkspaceCatalog.Create(
            logger,
            installer,
            localization,
            fixture.KeyboardPluginDirectory,
            audioRelayPluginDirectory: null);
        using var viewModel = new MainWindowViewModel(
            new HostDiagnostics("launch", "session", "0.1.0"),
            logger,
            registrations,
            localization,
            settings);
        var workspace = Assert.Single(
            viewModel.PluginWorkspaces,
            candidate => candidate.PluginId == "com.toolbox.keyboard-test");
        var keyboard = Assert.IsType<KeyboardTestViewModel>(workspace.PageViewModel);

        try
        {
            Assert.True(workspace.IsInstalled);
            Assert.True(workspace.IsOpened);
            Assert.False(keyboard.IsRuntimeEnabled);
            Assert.Single(viewModel.OpenedPluginWorkspaces);

            Assert.True(await keyboard.SetRuntimeEnabledAsync(enabled: true));
            Assert.True(keyboard.IsRuntimeEnabled);
            Assert.True(workspace.IsOpened);

            Assert.True(await keyboard.SetRuntimeEnabledAsync(enabled: false));
            Assert.False(keyboard.IsRuntimeEnabled);
            Assert.True(workspace.IsOpened);

            await viewModel.ToggleWorkspaceOpenedAsync(workspace);
            Assert.False(workspace.IsOpened);
            Assert.False(keyboard.IsRuntimeEnabled);
            Assert.Empty(viewModel.OpenedPluginWorkspaces);

            await viewModel.ToggleWorkspaceOpenedAsync(workspace);
            Assert.True(workspace.IsOpened);
            Assert.False(keyboard.IsRuntimeEnabled);
            Assert.Single(viewModel.OpenedPluginWorkspaces);
        }
        finally
        {
            await logger.DisposeAsync();
        }
    }

    [Fact]
    public async Task ClosingOpenedPluginStopsRuntimeBeforeHidingNavigation()
    {
        using var fixture = new HostFixture();
        var settings = new HostSettingsService(fixture.SettingsPath);
        var localization = new LocalizationService(settings);
        var logger = new StructuredLogger(
            new LoggerOptions { DirectoryPath = fixture.LogsRoot },
            "session",
            "0.1.0");
        using var installer = new PluginPackageInstaller(fixture.PluginsRoot, fixture.PluginDataRoot);
        var registrations = BuiltInPluginWorkspaceCatalog.Create(
            logger,
            installer,
            localization,
            fixture.KeyboardPluginDirectory,
            audioRelayPluginDirectory: null);
        using var viewModel = new MainWindowViewModel(
            new HostDiagnostics("launch", "session", "0.1.0"),
            logger,
            registrations,
            localization,
            settings);
        var workspace = Assert.Single(
            viewModel.PluginWorkspaces,
            candidate => candidate.PluginId == "com.toolbox.keyboard-test");
        var keyboard = Assert.IsType<KeyboardTestViewModel>(workspace.PageViewModel);

        try
        {
            Assert.True(await keyboard.SetRuntimeEnabledAsync(enabled: true));
            viewModel.SelectPluginWorkspace(workspace);

            await viewModel.ToggleWorkspaceOpenedAsync(workspace);

            Assert.False(keyboard.IsRuntimeEnabled);
            Assert.False(workspace.IsOpened);
            Assert.True(viewModel.IsSettingsPage);
            Assert.Null(viewModel.SelectedPluginWorkspace);
            Assert.False(new HostSettingsService(fixture.SettingsPath)
                .IsPluginOpened("com.toolbox.keyboard-test"));
        }
        finally
        {
            await logger.DisposeAsync();
        }
    }

    private sealed class HostFixture : IDisposable
    {
        private readonly string _root;

        public HostFixture()
        {
            _root = Path.Combine(Path.GetTempPath(), "ToolBox.Host.Tests", Guid.NewGuid().ToString("N"));
            KeyboardPluginDirectory = Path.Combine(_root, "keyboard", "0.1.0");
            PluginsRoot = Path.Combine(_root, "plugins");
            PluginDataRoot = Path.Combine(_root, "plugin-data");
            LogsRoot = Path.Combine(_root, "logs");
            SettingsPath = Path.Combine(_root, "ui-settings.json");
            Directory.CreateDirectory(KeyboardPluginDirectory);
            File.Copy(
                Path.Combine(AppContext.BaseDirectory, "KeyboardTest.dll"),
                Path.Combine(KeyboardPluginDirectory, "KeyboardTest.dll"));
            File.Copy(
                Path.Combine(AppContext.BaseDirectory, "KeyboardTest.manifest.json"),
                Path.Combine(KeyboardPluginDirectory, "manifest.json"));
        }

        public string KeyboardPluginDirectory { get; }
        public string PluginsRoot { get; }
        public string PluginDataRoot { get; }
        public string LogsRoot { get; }
        public string SettingsPath { get; }

        public void Dispose()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
    }

    private static PluginWorkspaceRegistration CreateRegistration(
        string pluginId,
        FakeWorkspaceState state)
    {
        return new PluginWorkspaceRegistration
        {
            PluginId = pluginId,
            DisplayNameResourceKey = "KeyboardMouse",
            InstallDialogTitleResourceKey = "InstallKeyboardDialogTitle",
            IconGeometry = Geometry.Parse("M 0,0 L 1,1"),
            PageViewModel = state,
            StateSource = state,
            GetIsInstalled = () => state.IsInstalled,
            GetIsRuntimeEnabled = () => state.IsRuntimeEnabled,
            GetInstalledVersion = static () => "1.0.0",
            GetIsInstallEnabled = static () => true,
            GetIsUninstallEnabled = static () => true,
            GetStatusAccentBrush = static () => Brushes.Green,
            GetHasError = static () => false,
            GetErrorMessage = static () => string.Empty,
            GetRequiresHostRestart = static () => false,
            SetRuntimeEnabledAsync = state.SetRuntimeEnabledAsync,
            InstallPackageAsync = state.InstallPackageAsync,
            UninstallAsync = state.UninstallAsync,
            Dispose = static () => { }
        };
    }

    private sealed class FakeWorkspaceState : INotifyPropertyChanged
    {
        private bool _isInstalled = true;

        public event PropertyChangedEventHandler? PropertyChanged;

        public bool IsInstalled => _isInstalled;

        public bool IsRuntimeEnabled { get; private set; }

        public int StopRequests { get; private set; }

        public int InstallRequests { get; private set; }

        public Task<bool> SetRuntimeEnabledAsync(bool enabled)
        {
            if (!enabled)
            {
                StopRequests++;
            }

            IsRuntimeEnabled = enabled;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsRuntimeEnabled)));
            return Task.FromResult(true);
        }

        public Task UninstallAsync()
        {
            _isInstalled = false;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsInstalled)));
            return Task.CompletedTask;
        }

        public Task InstallPackageAsync(string packagePath)
        {
            InstallRequests++;
            return Task.CompletedTask;
        }
    }
}
