using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Threading;
using ToolBox.Core.Diagnostics;
using ToolBox.Core.Packaging;
using ToolBox.Core.Plugins;
using ToolBox.PluginSdk;
using ToolBox.PluginSdk.Experimental;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;

namespace ToolBox.Host;

public sealed class AudioRelayViewModel : INotifyPropertyChanged, IDisposable
{
    private static readonly SolidColorBrush HealthyBrush = CreateBrush("#92E6B5");
    private static readonly SolidColorBrush WarningBrush = CreateBrush("#F5B85B");
    private static readonly SolidColorBrush ErrorBrush = CreateBrush("#FF8F86");
    private const string ProductId = "com.toolbox.audio-relay";
    private const string ProductName = "Phone Audio Relay";

    private readonly IStructuredLogger _logger;
    private readonly PluginPackageInstaller _packageInstaller;
    private readonly LocalizationService _localization;
    private readonly InProcessPluginRuntime _runtime = new();
    private readonly Dispatcher _dispatcher;
    private string? _pluginDirectory;
    private AudioRelaySnapshot _snapshot = AudioRelaySnapshot.Disabled();
    private LoadedInProcessPlugin? _loadedPlugin;
    private IAudioRelayPlugin? _plugin;
    private AudioRelayDeviceOption? _selectedDevice;
    private string? _errorMessage;
    private bool _operationInProgress;
    private bool _disposed;

    public AudioRelayViewModel(
        IStructuredLogger logger,
        string? pluginDirectory,
        PluginPackageInstaller packageInstaller,
        LocalizationService localization)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _pluginDirectory = Directory.Exists(pluginDirectory) ? pluginDirectory : null;
        _packageInstaller = packageInstaller ?? throw new ArgumentNullException(nameof(packageInstaller));
        _localization = localization ?? throw new ArgumentNullException(nameof(localization));
        _dispatcher = System.Windows.Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        Devices = new ObservableCollection<AudioRelayDeviceOption>();
        _localization.LanguageChanged += OnLanguageChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool IsInstalled => _pluginDirectory is not null && Directory.Exists(_pluginDirectory);

    public bool IsRuntimeEnabled => IsPluginRunning;

    public string InstalledVersion => IsInstalled
        ? Path.GetFileName(_pluginDirectory!.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        : string.Empty;

    public ObservableCollection<AudioRelayDeviceOption> Devices { get; }

    public AudioRelayDeviceOption? SelectedDevice
    {
        get => _selectedDevice;
        set
        {
            if (Equals(_selectedDevice, value))
            {
                return;
            }

            _selectedDevice = value;
            Notify();
            Notify(nameof(IsConnectEnabled));
            Notify(nameof(SelectedPhoneLabel));
        }
    }

    public string ToggleLabel => RequiresHostRestart
        ? T("RestartRequiredAction")
        : _loadedPlugin is null ? T("EnableRelay") : T("DisableRelay");

    public string RestartActionLabel => T("RestartToolBox");

    public bool RequiresHostRestart => _loadedPlugin?.State.LifecycleState == PluginLifecycleState.RestartRequired;

    public bool IsNormalToggleVisible => !RequiresHostRestart;

    public bool IsRestartActionVisible => RequiresHostRestart;

    public bool IsRestartActionEnabled => RequiresHostRestart && !_operationInProgress;

    public string PackageActionLabel => _pluginDirectory is null ? T("InstallPackage") : T("InstallUpdate");

    public bool IsInstallEnabled => !_operationInProgress && _loadedPlugin is null;

    public bool IsUninstallEnabled => IsInstalled && !_operationInProgress && !RequiresHostRestart;

    public bool IsToggleEnabled => _pluginDirectory is not null
        && !_operationInProgress
        && (_loadedPlugin is null
            || _loadedPlugin.State.LifecycleState is PluginLifecycleState.Running or PluginLifecycleState.Disabled);

    public bool IsRefreshEnabled => IsPluginRunning
        && !_operationInProgress
        && _snapshot.Status is AudioRelayStatus.Ready or AudioRelayStatus.Error;

    public bool IsConnectEnabled => IsPluginRunning
        && !_operationInProgress
        && SelectedDevice is not null
        && _snapshot.Status is AudioRelayStatus.Ready or AudioRelayStatus.Error;

    public bool IsDisconnectEnabled => IsPluginRunning
        && !_operationInProgress
        && _snapshot.Status is AudioRelayStatus.Connecting or AudioRelayStatus.Streaming;

    public string StatusLabel
    {
        get
        {
            if (_pluginDirectory is null)
            {
                return T("StatusNotInstalled");
            }

            return _loadedPlugin?.State.LifecycleState switch
            {
                PluginLifecycleState.Starting => T("StatusStarting"),
                PluginLifecycleState.Stopping => T("StatusStopping"),
                PluginLifecycleState.Faulted => T("StatusFaulted"),
                PluginLifecycleState.RestartRequired => T("StatusRestartRequired"),
                PluginLifecycleState.Running => _snapshot.Status switch
                {
                    AudioRelayStatus.Refreshing => T("StatusScanning"),
                    AudioRelayStatus.Ready => T("StatusReady"),
                    AudioRelayStatus.Connecting => T("StatusConnecting"),
                    AudioRelayStatus.Streaming => T("StatusReceiving"),
                    AudioRelayStatus.Unsupported => T("StatusUnsupported"),
                    AudioRelayStatus.Error => T("StatusNeedsAttention"),
                    _ => T("StatusEnabled")
                },
                _ => T("StatusDisabled")
            };
        }
    }

    public string StatusDescription
    {
        get
        {
            if (_pluginDirectory is null)
            {
                return T("RelayInstallDescription");
            }

            if (_loadedPlugin is null)
            {
                return T("RelayDisabledDescription");
            }

            if (RequiresHostRestart)
            {
                return T("RelayRestartRequiredDescription");
            }

            return _snapshot.Status switch
            {
                AudioRelayStatus.Refreshing => T("RelayRefreshingDescription"),
                AudioRelayStatus.Ready when Devices.Count == 0 => T("RelayNoDevicesDescription"),
                AudioRelayStatus.Ready => string.Format(
                    CultureInfo.CurrentCulture,
                    T("RelayReadyDescription"),
                    Devices.Count),
                AudioRelayStatus.Connecting => string.Format(
                    CultureInfo.CurrentCulture,
                    T("RelayConnectingDescription"),
                    _snapshot.SelectedDeviceName ?? T("Phone")),
                AudioRelayStatus.Streaming => string.Format(
                    CultureInfo.CurrentCulture,
                    T("RelayStreamingDescription"),
                    _snapshot.SelectedDeviceName ?? T("Phone")),
                AudioRelayStatus.Unsupported => T("RelayUnsupportedDescription"),
                AudioRelayStatus.Error => string.Format(
                    CultureInfo.CurrentCulture,
                    T("RelayErrorDescription"),
                    _snapshot.ErrorCode ?? T("UnknownFailure")),
                _ => T("RelayDisabledDescription")
            };
        }
    }

    public SolidColorBrush StatusAccentBrush => _loadedPlugin?.State.LifecycleState switch
    {
        PluginLifecycleState.Faulted or PluginLifecycleState.RestartRequired => ErrorBrush,
        PluginLifecycleState.Running when _snapshot.Status == AudioRelayStatus.Streaming => HealthyBrush,
        PluginLifecycleState.Running when _snapshot.Status is AudioRelayStatus.Error or AudioRelayStatus.Unsupported => ErrorBrush,
        _ => WarningBrush
    };

    public string SelectedPhoneLabel => SelectedDevice?.Name ?? T("NoPhoneSelected");

    public string DeviceCountLabel => Devices.Count switch
    {
        0 => T("NoPairedSources"),
        1 => T("OnePairedSource"),
        _ => string.Format(CultureInfo.CurrentCulture, T("ManyPairedSources"), Devices.Count)
    };

    public string RouteStateLabel => _snapshot.Status == AudioRelayStatus.Streaming
        ? T("SignalOpen")
        : T("SignalStandby");

    public bool HasError => !string.IsNullOrWhiteSpace(_errorMessage);

    public string ErrorMessage => _errorMessage ?? string.Empty;

    private bool IsPluginRunning => _loadedPlugin?.State.LifecycleState == PluginLifecycleState.Running;

    public async Task ToggleAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!IsToggleEnabled || RequiresHostRestart)
        {
            return;
        }

        await RunOperationAsync(
            _loadedPlugin is null ? EnableCoreAsync : DisableCoreAsync,
            "AUDIO_RELAY_LIFECYCLE_FAILED",
            $"The {ProductName} lifecycle operation failed.");
    }

    public async Task<bool> SetRuntimeEnabledAsync(bool enabled)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (RequiresHostRestart)
        {
            return false;
        }

        if (IsRuntimeEnabled == enabled)
        {
            return true;
        }

        if (!IsToggleEnabled)
        {
            return false;
        }

        await ToggleAsync();
        return IsRuntimeEnabled == enabled && !RequiresHostRestart;
    }

    public async Task RefreshAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!IsRefreshEnabled || _plugin is null)
        {
            return;
        }

        await RunOperationAsync(
            async () => await _plugin.RefreshDevicesAsync(CancellationToken.None),
            "AUDIO_RELAY_DISCOVERY_FAILED",
            "Paired Bluetooth audio devices could not be refreshed.");
    }

    public async Task ConnectAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var selected = SelectedDevice;
        if (!IsConnectEnabled || _plugin is null || selected is null)
        {
            return;
        }

        await RunOperationAsync(
            async () => await _plugin.ConnectAsync(selected.Id, CancellationToken.None),
            "AUDIO_RELAY_CONNECTION_FAILED",
            $"Audio receiving from {selected.Name} could not be started.");
    }

    public async Task DisconnectAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!IsDisconnectEnabled || _plugin is null)
        {
            return;
        }

        await RunOperationAsync(
            async () => await _plugin.DisconnectAsync(CancellationToken.None),
            "AUDIO_RELAY_DISCONNECT_FAILED",
            "Phone audio receiving could not be stopped cleanly.");
    }

    public async Task InstallPackageAsync(string packagePath)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);
        if (!IsInstallEnabled)
        {
            return;
        }

        await RunOperationAsync(
            async () =>
            {
                EnsureAudioRelayPackage(packagePath);
                var installed = await _packageInstaller.InstallAsync(packagePath);
                if (!string.Equals(installed.PluginId, ProductId, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"This package contains '{installed.PluginId}', not the {ProductName} plugin.");
                }

                _pluginDirectory = installed.VersionDirectory;
                _snapshot = AudioRelaySnapshot.Disabled();
                SyncDevices(_snapshot);
                _logger.Log(
                    LogLevel.Information,
                    "Package",
                    $"{ProductName} package installed and activated.",
                    pluginId: installed.PluginId,
                    pluginVersion: installed.Version);
                NotifyManagementProperties();
                NotifyRuntimeProperties();
            },
            "PACKAGE_INSTALL_FAILED",
            $"The {ProductName} package could not be installed.");
    }

    public async Task UninstallAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var removedVersion = InstalledVersion;
        if (!IsUninstallEnabled || string.IsNullOrWhiteSpace(removedVersion))
        {
            return;
        }

        await RunOperationAsync(
            async () =>
            {
                if (_loadedPlugin is not null)
                {
                    await DisableCoreAsync();
                }

                var result = await _packageInstaller.UninstallAsync(ProductId, removedVersion);
                _pluginDirectory = string.IsNullOrWhiteSpace(result.ActiveVersionAfterUninstall)
                    ? null
                    : _packageInstaller.GetActiveVersionDirectory(ProductId);
                _snapshot = AudioRelaySnapshot.Disabled();
                SyncDevices(_snapshot);
                _logger.Info("Package", $"{ProductName} {removedVersion} uninstalled.");
                NotifyManagementProperties();
                NotifyRuntimeProperties();
                NotifySnapshotProperties();
            },
            "PACKAGE_UNINSTALL_FAILED",
            $"The {ProductName} package could not be uninstalled.");
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _localization.LanguageChanged -= OnLanguageChanged;
        DetachPluginCallbacks();

        try
        {
            _loadedPlugin?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            _logger.Error(
                "AudioRelay",
                $"{ProductName} could not complete its shutdown lifecycle.",
                errorCode: "AUDIO_RELAY_SHUTDOWN_FAILED",
                exception: exception);
        }
        finally
        {
            _plugin = null;
            _loadedPlugin = null;
        }
    }

    private async Task EnableCoreAsync()
    {
        if (_pluginDirectory is null || !Directory.Exists(_pluginDirectory))
        {
            throw new InvalidOperationException(
                $"{ProductName} is not installed or activated. Install its .tpk package first.");
        }

        var discoveredPlugin = _runtime.DiscoverSingle(_pluginDirectory);
        if (!string.Equals(discoveredPlugin.Manifest.Id, ProductId, StringComparison.Ordinal))
        {
            throw new PluginLoadException(
                "AUDIO_RELAY_PLUGIN_ID_MISMATCH",
                $"The active package is '{discoveredPlugin.Manifest.Id}', not '{ProductId}'.");
        }

        var loadedPlugin = _runtime.Load(discoveredPlugin);
        var plugin = loadedPlugin.GetCapability<IAudioRelayPlugin>();
        if (plugin is null)
        {
            await loadedPlugin.StopAndUnloadAsync();
            throw new PluginLoadException(
                "AUDIO_RELAY_CAPABILITY_MISSING",
                $"The {ProductName} package does not expose its product capability.");
        }

        _loadedPlugin = loadedPlugin;
        _plugin = plugin;
        plugin.SnapshotChanged += OnSnapshotChanged;

        try
        {
            await loadedPlugin.StartAsync();
            ApplySnapshot(plugin.Snapshot);
        }
        catch
        {
            DetachPluginCallbacks();
            await loadedPlugin.StopAndUnloadAsync();
            _loadedPlugin = null;
            throw;
        }

        _logger.Log(
            LogLevel.Information,
            "AudioRelay",
            $"{ProductName} plugin enabled.",
            pluginId: discoveredPlugin.Manifest.Id,
            pluginVersion: discoveredPlugin.Manifest.Version);
        NotifyRuntimeProperties();
    }

    private async Task DisableCoreAsync()
    {
        var loadedPlugin = _loadedPlugin ?? throw new InvalidOperationException($"{ProductName} is not loaded.");
        DetachPluginCallbacks();
        await loadedPlugin.StopAndUnloadAsync();
        _loadedPlugin = null;
        _snapshot = AudioRelaySnapshot.Disabled();
        SyncDevices(_snapshot);
        _logger.Info("AudioRelay", $"{ProductName} plugin disabled and unloaded.");
        NotifyRuntimeProperties();
        NotifySnapshotProperties();
    }

    private async Task RunOperationAsync(Func<Task> operation, string errorCode, string errorMessage)
    {
        SetOperationInProgress(true);
        ClearError();

        try
        {
            await operation();
        }
        catch (Exception exception)
        {
            _errorMessage = exception.Message;
            _logger.Error(
                "AudioRelay",
                errorMessage,
                errorCode: exception is PluginPackageException packageException
                    ? packageException.ErrorCode
                    : errorCode,
                exception: exception);
            Notify(nameof(HasError));
            Notify(nameof(ErrorMessage));
            NotifyRuntimeProperties();
        }
        finally
        {
            SetOperationInProgress(false);
        }
    }

    private void OnSnapshotChanged(AudioRelaySnapshot snapshot)
    {
        if (_dispatcher.CheckAccess())
        {
            ApplySnapshot(snapshot);
        }
        else
        {
            _dispatcher.BeginInvoke(new Action(() => ApplySnapshot(snapshot)));
        }
    }

    private void ApplySnapshot(AudioRelaySnapshot snapshot)
    {
        if (_disposed)
        {
            return;
        }

        _snapshot = snapshot with { Devices = [.. snapshot.Devices] };
        SyncDevices(_snapshot);
        NotifySnapshotProperties();
        NotifyRuntimeProperties();
    }

    private void SyncDevices(AudioRelaySnapshot snapshot)
    {
        var preferredId = snapshot.SelectedDeviceId ?? SelectedDevice?.Id;
        Devices.Clear();
        foreach (var device in snapshot.Devices)
        {
            Devices.Add(new AudioRelayDeviceOption(device.Id, device.Name));
        }

        SelectedDevice = Devices.FirstOrDefault(device => string.Equals(
            device.Id,
            preferredId,
            StringComparison.Ordinal));
        Notify(nameof(DeviceCountLabel));
    }

    private void DetachPluginCallbacks()
    {
        var plugin = _plugin;
        _plugin = null;
        if (plugin is not null)
        {
            plugin.SnapshotChanged -= OnSnapshotChanged;
        }

        plugin = null;
    }

    private void SetOperationInProgress(bool value)
    {
        _operationInProgress = value;
        NotifyRuntimeProperties();
    }

    private void ClearError()
    {
        _errorMessage = null;
        Notify(nameof(HasError));
        Notify(nameof(ErrorMessage));
    }

    private void NotifyRuntimeProperties()
    {
        Notify(nameof(IsRuntimeEnabled));
        Notify(nameof(ToggleLabel));
        Notify(nameof(PackageActionLabel));
        Notify(nameof(IsInstallEnabled));
        Notify(nameof(IsUninstallEnabled));
        Notify(nameof(IsToggleEnabled));
        Notify(nameof(RequiresHostRestart));
        Notify(nameof(IsNormalToggleVisible));
        Notify(nameof(IsRestartActionVisible));
        Notify(nameof(IsRestartActionEnabled));
        Notify(nameof(IsRefreshEnabled));
        Notify(nameof(IsConnectEnabled));
        Notify(nameof(IsDisconnectEnabled));
        Notify(nameof(StatusLabel));
        Notify(nameof(StatusDescription));
        Notify(nameof(StatusAccentBrush));
    }

    private void NotifyManagementProperties()
    {
        Notify(nameof(IsInstalled));
        Notify(nameof(InstalledVersion));
        Notify(nameof(IsUninstallEnabled));
    }

    private void NotifySnapshotProperties()
    {
        Notify(nameof(SelectedPhoneLabel));
        Notify(nameof(DeviceCountLabel));
        Notify(nameof(RouteStateLabel));
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        NotifyRuntimeProperties();
        NotifySnapshotProperties();
    }

    private void Notify([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private string T(string key) => _localization[key];

    private static void EnsureAudioRelayPackage(string packagePath)
    {
        using var archive = ZipFile.OpenRead(packagePath);
        var manifestEntry = archive.Entries.SingleOrDefault(entry => string.Equals(
            entry.FullName.Replace('\\', '/'),
            "manifest.json",
            StringComparison.Ordinal));
        if (manifestEntry is null)
        {
            throw new InvalidOperationException("The selected package does not contain a root manifest.json.");
        }

        if (manifestEntry.Length > 1024 * 1024)
        {
            throw new InvalidOperationException("The selected package manifest is larger than 1 MiB.");
        }

        using var manifestStream = manifestEntry.Open();
        using var document = JsonDocument.Parse(manifestStream);
        if (!document.RootElement.TryGetProperty("id", out var idElement)
            || !string.Equals(idElement.GetString(), ProductId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Select a {ProductName} package ({ProductId}).");
        }
    }

    private static SolidColorBrush CreateBrush(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)!);
        brush.Freeze();
        return brush;
    }
}

public sealed record AudioRelayDeviceOption(string Id, string Name)
{
    public override string ToString() => Name;
}
