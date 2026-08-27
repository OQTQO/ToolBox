using System.Text.Json;
using ToolBox.Core.Diagnostics;
using ToolBox.Core.Packaging;
using ToolBox.Host;
using ToolBox.PluginSdk;
using ToolBox.PluginSdk.Experimental;
using Xunit;

namespace ToolBox.Host.Tests;

public sealed class AudioRelayLifecycleBoundaryTests
{
    [Fact]
    public async Task FailedAudioStopKeepsPluginVisibleAndExposesRestartBoundary()
    {
        using var fixture = new AudioFailureFixture();
        var settings = new HostSettingsService(fixture.SettingsPath);
        settings.SetLanguage(AppLanguage.Chinese);
        var localization = new LocalizationService(settings);
        var logger = new StructuredLogger(
            new LoggerOptions { DirectoryPath = fixture.LogsRoot },
            "session",
            "0.1.0");
        using var installer = new PluginPackageInstaller(fixture.PluginsRoot, fixture.PluginDataRoot);
        using var viewModel = new MainWindowViewModel(
            new HostDiagnostics("launch", "session", "0.1.0"),
            logger,
            keyboardTestPluginDirectory: null,
            fixture.AudioPluginDirectory,
            installer,
            localization,
            settings);

        try
        {
            Assert.True(viewModel.IsAudioRelayInstalled);
            Assert.True(viewModel.IsAudioRelayOpened);
            Assert.True(await viewModel.AudioRelay.SetRuntimeEnabledAsync(enabled: true));

            viewModel.SelectPage(ShellPage.AudioRelay);
            await viewModel.ToggleAudioRelayOpenedAsync();

            Assert.True(viewModel.IsAudioRelayOpened);
            Assert.True(viewModel.IsAudioRelayPage);
            Assert.True(viewModel.AudioRelay.RequiresHostRestart);
            Assert.False(viewModel.AudioRelay.IsToggleEnabled);
            Assert.True(viewModel.AudioRelay.IsRestartActionVisible);
            Assert.True(viewModel.AudioRelay.IsRestartActionEnabled);
            Assert.Contains("重启", viewModel.AudioRelay.StatusDescription, StringComparison.Ordinal);
            Assert.Contains("重启", viewModel.PluginManagerError, StringComparison.Ordinal);
        }
        finally
        {
            await logger.DisposeAsync();
        }
    }

    [Fact]
    public void RestartBoundaryCopyIsAvailableInBothLanguages()
    {
        using var fixture = new AudioFailureFixture();
        var settings = new HostSettingsService(fixture.SettingsPath);
        var localization = new LocalizationService(settings);

        localization.SetLanguage(AppLanguage.English);
        Assert.Contains("restart", localization["RelayRestartRequiredDescription"], StringComparison.OrdinalIgnoreCase);
        localization.SetLanguage(AppLanguage.Chinese);
        Assert.Contains("重启", localization["RelayRestartRequiredDescription"], StringComparison.Ordinal);
        Assert.Equal("重启 ToolBox", localization["RestartToolBox"]);
    }

    public sealed class FailingAudioRelayPlugin : IAudioRelayPlugin
    {
        private AudioRelaySnapshot _snapshot = new(
            AudioRelayStatus.Ready,
            [new AudioRelayDevice("test-phone", "Test Phone")],
            "test-phone",
            "Test Phone",
            "Ready",
            null);

        public string Id => "com.toolbox.audio-relay";

        public AudioRelaySnapshot Snapshot => _snapshot;

        public event Action<AudioRelaySnapshot>? SnapshotChanged;

        public ValueTask StartAsync(IPluginContext context, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(context);
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.CompletedTask;
        }

        public ValueTask StopAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new InvalidOperationException("Simulated audio resource release failure.");
        }

        public ValueTask RefreshDevicesAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SnapshotChanged?.Invoke(_snapshot);
            return ValueTask.CompletedTask;
        }

        public ValueTask ConnectAsync(string deviceId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _snapshot = _snapshot with
            {
                Status = AudioRelayStatus.Streaming,
                SelectedDeviceId = deviceId,
                SelectedDeviceName = "Test Phone",
                StatusMessage = "Streaming",
                ErrorCode = null
            };
            SnapshotChanged?.Invoke(_snapshot);
            return ValueTask.CompletedTask;
        }

        public ValueTask DisconnectAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _snapshot = _snapshot with
            {
                Status = AudioRelayStatus.Ready,
                StatusMessage = "Ready",
                ErrorCode = null
            };
            SnapshotChanged?.Invoke(_snapshot);
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            SnapshotChanged = null;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class AudioFailureFixture : IDisposable
    {
        private readonly string _root;

        public AudioFailureFixture()
        {
            _root = Path.Combine(
                Path.GetTempPath(),
                "ToolBox.Host.Tests",
                Guid.NewGuid().ToString("N"));
            AudioPluginDirectory = Path.Combine(_root, "audio", "0.1.0");
            PluginsRoot = Path.Combine(_root, "plugins");
            PluginDataRoot = Path.Combine(_root, "plugin-data");
            LogsRoot = Path.Combine(_root, "logs");
            SettingsPath = Path.Combine(_root, "ui-settings.json");
            Directory.CreateDirectory(AudioPluginDirectory);
            File.Copy(
                typeof(FailingAudioRelayPlugin).Assembly.Location,
                Path.Combine(AudioPluginDirectory, "ToolBox.Host.Tests.dll"));

            var manifest = new
            {
                formatVersion = 1,
                id = "com.toolbox.audio-relay",
                name = "Phone Audio Relay",
                version = "0.1.0",
                pluginApiMajor = 1,
                publisher = "toolbox.tests",
                platform = new { os = "windows", arch = "x64" },
                runtime = new { supportedModes = new[] { "inProcess" }, preferredMode = "inProcess", background = true },
                entryPoint = "ToolBox.Host.Tests.AudioRelayLifecycleBoundaryTests+FailingAudioRelayPlugin, ToolBox.Host.Tests"
            };
            File.WriteAllText(
                Path.Combine(AudioPluginDirectory, "manifest.json"),
                JsonSerializer.Serialize(manifest));
        }

        public string AudioPluginDirectory { get; }
        public string PluginsRoot { get; }
        public string PluginDataRoot { get; }
        public string LogsRoot { get; }
        public string SettingsPath { get; }

        public void Dispose()
        {
            for (var attempt = 0; attempt < 30 && Directory.Exists(_root); attempt++)
            {
                try
                {
                    foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
                    {
                        File.SetAttributes(file, FileAttributes.Normal);
                    }

                    Directory.Delete(_root, recursive: true);
                }
                catch (IOException) when (Directory.Exists(_root))
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    Thread.Sleep(50);
                }
                catch (UnauthorizedAccessException) when (Directory.Exists(_root))
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    Thread.Sleep(50);
                }
            }

            Assert.False(Directory.Exists(_root), $"Fixture directory could not be cleaned: '{_root}'.");
        }
    }
}
