using ToolBox.Core.Diagnostics;
using ToolBox.Core.Packaging;
using ToolBox.Host;
using Xunit;

namespace ToolBox.Host.Tests;

public sealed class MainWindowViewModelStateTests
{
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
        using var viewModel = new MainWindowViewModel(
            new HostDiagnostics("launch", "session", "0.1.0"),
            logger,
            fixture.KeyboardPluginDirectory,
            audioRelayPluginDirectory: null,
            installer,
            localization,
            settings);

        try
        {
            Assert.True(viewModel.IsKeyboardInstalled);
            Assert.True(viewModel.IsKeyboardOpened);
            Assert.False(viewModel.KeyboardTest.IsRuntimeEnabled);

            Assert.True(await viewModel.KeyboardTest.SetRuntimeEnabledAsync(enabled: true));
            Assert.True(viewModel.KeyboardTest.IsRuntimeEnabled);
            Assert.True(viewModel.IsKeyboardOpened);

            Assert.True(await viewModel.KeyboardTest.SetRuntimeEnabledAsync(enabled: false));
            Assert.False(viewModel.KeyboardTest.IsRuntimeEnabled);
            Assert.True(viewModel.IsKeyboardOpened);

            await viewModel.ToggleKeyboardOpenedAsync();
            Assert.False(viewModel.IsKeyboardOpened);
            Assert.False(viewModel.KeyboardTest.IsRuntimeEnabled);

            await viewModel.ToggleKeyboardOpenedAsync();
            Assert.True(viewModel.IsKeyboardOpened);
            Assert.False(viewModel.KeyboardTest.IsRuntimeEnabled);
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
        using var viewModel = new MainWindowViewModel(
            new HostDiagnostics("launch", "session", "0.1.0"),
            logger,
            fixture.KeyboardPluginDirectory,
            audioRelayPluginDirectory: null,
            installer,
            localization,
            settings);

        try
        {
            Assert.True(await viewModel.KeyboardTest.SetRuntimeEnabledAsync(enabled: true));
            viewModel.SelectPage(ShellPage.KeyboardTest);

            await viewModel.ToggleKeyboardOpenedAsync();

            Assert.False(viewModel.KeyboardTest.IsRuntimeEnabled);
            Assert.False(viewModel.IsKeyboardOpened);
            Assert.True(viewModel.IsSettingsPage);
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
}
