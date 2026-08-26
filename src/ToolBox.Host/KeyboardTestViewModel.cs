using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using System.Windows.Threading;
using ToolBox.Core.Diagnostics;
using ToolBox.Core.Packaging;
using ToolBox.Core.Plugins;
using ToolBox.PluginSdk;
using ToolBox.PluginSdk.Experimental;

namespace ToolBox.Host;

public sealed class KeyboardTestViewModel : INotifyPropertyChanged, IDisposable
{
    private static readonly SolidColorBrush HealthyBrush = CreateBrush("#92E6B5");
    private static readonly SolidColorBrush WarningBrush = CreateBrush("#F5B85B");
    private static readonly SolidColorBrush ErrorBrush = CreateBrush("#FF8F86");
    private const string ProductName = "Keyboard & Mouse Test";

    private readonly IStructuredLogger _logger;
    private readonly PluginPackageInstaller _packageInstaller;
    private readonly InProcessPluginRuntime _runtime;
    private string? _pluginDirectory;
    private readonly Dispatcher _dispatcher;
    private KeyboardTestSettings _settings = KeyboardTestSettings.Default;
    private KeyboardTestSnapshot _snapshot = KeyboardTestSnapshot.Disabled(KeyboardTestSettings.Default);
    private LoadedInProcessPlugin? _loadedPlugin;
    private IKeyboardTestPlugin? _plugin;
    private string? _errorMessage;
    private bool _operationInProgress;
    private bool _disposed;

    public KeyboardTestViewModel(
        IStructuredLogger logger,
        string? pluginDirectory,
        PluginPackageInstaller packageInstaller)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _packageInstaller = packageInstaller ?? throw new ArgumentNullException(nameof(packageInstaller));
        _pluginDirectory = pluginDirectory;
        _runtime = new InProcessPluginRuntime();
        _dispatcher = System.Windows.Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool IncludeKeyUpEvents
    {
        get => _settings.IncludeKeyUpEvents;
        set
        {
            if (_settings.IncludeKeyUpEvents == value)
            {
                return;
            }

            _settings = _settings with { IncludeKeyUpEvents = value };
            Notify(nameof(IncludeKeyUpEvents));
            Notify(nameof(SettingsSummary));
        }
    }

    public bool IncludeMouseEvents
    {
        get => _settings.IncludeMouseEvents;
        set
        {
            if (_settings.IncludeMouseEvents == value)
            {
                return;
            }

            _settings = _settings with { IncludeMouseEvents = value };
            Notify(nameof(IncludeMouseEvents));
            Notify(nameof(SettingsSummary));
        }
    }

    public string ToggleLabel => _loadedPlugin is null ? "Enable test" : "Disable test";

    public string PackageActionLabel => _pluginDirectory is null ? "Install .tpk" : "Install update";

    public bool IsToggleEnabled => _pluginDirectory is not null
        && !_operationInProgress
        && (_loadedPlugin is null
            || _loadedPlugin.State.LifecycleState is PluginLifecycleState.Running or PluginLifecycleState.Disabled);

    public bool IsSettingsEnabled => _pluginDirectory is not null
        && !_operationInProgress
        && (_loadedPlugin is null || _loadedPlugin.State.LifecycleState == PluginLifecycleState.Running);

    public bool IsInstallEnabled => !_operationInProgress && _loadedPlugin is null;

    public string StatusLabel => _pluginDirectory is null
        ? "Not installed"
        : _loadedPlugin?.State.LifecycleState switch
    {
        PluginLifecycleState.Starting => "Starting",
        PluginLifecycleState.Running => "Enabled",
        PluginLifecycleState.Stopping => "Stopping",
        PluginLifecycleState.Faulted => "Faulted",
        PluginLifecycleState.RestartRequired => "Restart required",
        _ => "Disabled"
    };

    public string StatusDescription => _pluginDirectory is null
        ? "Install and activate a Keyboard & Mouse Test package to begin."
        : _loadedPlugin?.State.LifecycleState switch
    {
        PluginLifecycleState.Starting => $"{ProductName} is entering its active lifetime.",
        PluginLifecycleState.Running => "Input is observed only inside this surface; no global hook is active.",
        PluginLifecycleState.Stopping => "The Host is stopping the plugin before it can claim Disabled.",
        PluginLifecycleState.Faulted => "The plugin reported a lifecycle failure. The Host keeps that state visible.",
        PluginLifecycleState.RestartRequired => "The plugin did not finish its lifecycle. Restart the Host before retrying.",
        _ => $"{ProductName} is installed but disabled. Enable it to test this surface."
    };

    public SolidColorBrush StatusAccentBrush => _loadedPlugin?.State.LifecycleState switch
    {
        PluginLifecycleState.Running => HealthyBrush,
        PluginLifecycleState.Faulted or PluginLifecycleState.RestartRequired => ErrorBrush,
        _ => WarningBrush
    };

    public string LastInputLabel => string.IsNullOrWhiteSpace(_snapshot.LastInput)
        ? "Waiting for a signal"
        : _snapshot.LastInput;

    public string KeyEventCountLabel => _snapshot.KeyEventCount.ToString("N0", CultureInfo.InvariantCulture);

    public string MouseEventCountLabel => _snapshot.MouseEventCount.ToString("N0", CultureInfo.InvariantCulture);

    public string SettingsSummary => $"Key-up {(_settings.IncludeKeyUpEvents ? "on" : "off")}  ·  Mouse {(_settings.IncludeMouseEvents ? "on" : "off")}";

    public bool HasError => !string.IsNullOrWhiteSpace(_errorMessage);

    public string ErrorMessage => _errorMessage ?? string.Empty;

    public async Task ToggleAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!IsToggleEnabled)
        {
            return;
        }

        SetOperationInProgress(true);

        try
        {
            _errorMessage = null;
            Notify(nameof(HasError));
            Notify(nameof(ErrorMessage));

            if (_loadedPlugin is null)
            {
                await EnableCoreAsync();
            }
            else
            {
                await DisableCoreAsync();
            }
        }
        catch (Exception exception)
        {
            _errorMessage = exception.Message;
            _logger.Error(
                "KeyboardTest",
                $"The {ProductName} lifecycle operation failed.",
                errorCode: "KEYBOARD_TEST_LIFECYCLE_FAILED",
                exception: exception);
            Notify(nameof(HasError));
            Notify(nameof(ErrorMessage));
            Notify(nameof(StatusLabel));
            Notify(nameof(StatusDescription));
            Notify(nameof(StatusAccentBrush));
        }
        finally
        {
            SetOperationInProgress(false);
        }
    }

    public async Task ApplySettingsAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!IsSettingsEnabled)
        {
            return;
        }

        try
        {
            _errorMessage = null;
            Notify(nameof(HasError));
            Notify(nameof(ErrorMessage));

            if (_plugin is not null)
            {
                await _plugin.ApplySettingsAsync(_settings, CancellationToken.None);
            }

            _logger.Info("KeyboardTest", $"{ProductName} settings applied.");
        }
        catch (Exception exception)
        {
            _errorMessage = exception.Message;
            _logger.Error(
                "KeyboardTest",
                $"{ProductName} settings could not be applied.",
                errorCode: "KEYBOARD_TEST_SETTINGS_FAILED",
                exception: exception);
            Notify(nameof(HasError));
            Notify(nameof(ErrorMessage));
        }
    }

    public async Task InstallPackageAsync(string packagePath)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);

        if (!IsInstallEnabled)
        {
            return;
        }

        SetOperationInProgress(true);

        try
        {
            _errorMessage = null;
            Notify(nameof(HasError));
            Notify(nameof(ErrorMessage));

            var installed = await _packageInstaller.InstallAsync(packagePath);
            _pluginDirectory = installed.VersionDirectory;
            _snapshot = KeyboardTestSnapshot.Disabled(_settings);

            _logger.Log(
                LogLevel.Information,
                "Package",
                $"{ProductName} package installed and activated.",
                pluginId: installed.PluginId,
                pluginVersion: installed.Version);
            NotifySnapshotProperties();
            NotifyRuntimeProperties();
        }
        catch (Exception exception)
        {
            _errorMessage = exception.Message;
            _logger.Error(
                "Package",
                $"The {ProductName} package could not be installed.",
                errorCode: exception is PluginPackageException packageException
                    ? packageException.ErrorCode
                    : "PACKAGE_INSTALL_FAILED",
                exception: exception);
            Notify(nameof(HasError));
            Notify(nameof(ErrorMessage));
        }
        finally
        {
            SetOperationInProgress(false);
        }
    }

    public void ObserveKey(string key, bool isDown)
    {
        if (_disposed || _plugin is null)
        {
            return;
        }

        _plugin.ObserveKey(key, isDown);
    }

    public void ObserveMouse(KeyboardTestMouseButton button, bool isDown, int x, int y)
    {
        if (_disposed || _plugin is null)
        {
            return;
        }

        _plugin.ObserveMouse(button, isDown, x, y);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        DetachPluginCallbacks();

        try
        {
            _loadedPlugin?.StopAndUnloadAsync().AsTask().GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            _logger.Error(
                "KeyboardTest",
                $"{ProductName} could not complete its shutdown lifecycle.",
                errorCode: "KEYBOARD_TEST_SHUTDOWN_FAILED",
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
                $"{ProductName} is not installed or activated. Install a .tpk package first.");
        }

        var discoveredPlugin = _runtime.DiscoverSingle(_pluginDirectory);
        var loadedPlugin = _runtime.Load(discoveredPlugin);
        var plugin = loadedPlugin.GetCapability<IKeyboardTestPlugin>();

        if (plugin is null)
        {
            await loadedPlugin.StopAndUnloadAsync();
            throw new PluginLoadException(
                "KEYBOARD_TEST_CAPABILITY_MISSING",
                $"The {ProductName} package does not expose its product capability.");
        }

        _loadedPlugin = loadedPlugin;
        _plugin = plugin;
        plugin.SnapshotChanged += OnSnapshotChanged;

        await loadedPlugin.StartAsync();
        await plugin.ApplySettingsAsync(_settings, CancellationToken.None);

        _logger.Log(
            LogLevel.Information,
            "KeyboardTest",
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
        _snapshot = KeyboardTestSnapshot.Disabled(_settings);
        _logger.Info("KeyboardTest", $"{ProductName} plugin disabled and unloaded.");
        NotifySnapshotProperties();
        NotifyRuntimeProperties();
    }

    private void OnSnapshotChanged(KeyboardTestSnapshot snapshot)
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

    private void DetachPluginCallbacks()
    {
        var plugin = _plugin;
        _plugin = null;

        if (plugin is not null)
        {
            plugin.SnapshotChanged -= OnSnapshotChanged;
        }

        // Keep the plugin reference inside this short helper so it is gone before
        // LoadedInProcessPlugin begins its forced ALC collection.
        plugin = null;
    }

    private void ApplySnapshot(KeyboardTestSnapshot snapshot)
    {
        if (_disposed)
        {
            return;
        }

        _snapshot = snapshot;
        NotifySnapshotProperties();
    }

    private void SetOperationInProgress(bool value)
    {
        _operationInProgress = value;
        Notify(nameof(IsToggleEnabled));
        Notify(nameof(IsSettingsEnabled));
        Notify(nameof(IsInstallEnabled));
    }

    private void NotifyRuntimeProperties()
    {
        Notify(nameof(ToggleLabel));
        Notify(nameof(PackageActionLabel));
        Notify(nameof(IsToggleEnabled));
        Notify(nameof(IsSettingsEnabled));
        Notify(nameof(IsInstallEnabled));
        Notify(nameof(StatusLabel));
        Notify(nameof(StatusDescription));
        Notify(nameof(StatusAccentBrush));
    }

    private void NotifySnapshotProperties()
    {
        Notify(nameof(LastInputLabel));
        Notify(nameof(KeyEventCountLabel));
        Notify(nameof(MouseEventCountLabel));
        Notify(nameof(SettingsSummary));
    }

    private void Notify([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private static SolidColorBrush CreateBrush(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)!);
        brush.Freeze();
        return brush;
    }
}
