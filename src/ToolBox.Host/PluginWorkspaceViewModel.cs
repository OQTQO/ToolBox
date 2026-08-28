using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using ToolBox.Core.Diagnostics;
using ToolBox.Core.Packaging;
using ToolBox.Core.Plugins;
using ToolBox.Core.Plugins.Worker;
using ToolBox.PluginSdk;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;

namespace ToolBox.Host;

/// <summary>
/// The only Host-side plugin projection. It deliberately exposes Manifest and
/// lifecycle state, never a plugin-specific view or capability interface.
/// </summary>
public sealed class PluginWorkspaceViewModel : INotifyPropertyChanged, IDisposable
{
    private static readonly Brush HealthyBrush = CreateBrush("#92E6B5");
    private static readonly Brush WarningBrush = CreateBrush("#F5B85B");
    private static readonly Brush ErrorBrush = CreateBrush("#FF8F86");
    private static readonly Brush MutedBrush = CreateBrush("#A9C7E8");
    private static readonly Geometry GenericIcon = Geometry.Parse(
        "M 4,3 L 16,3 L 16,7 L 20,7 L 20,17 L 4,17 Z M 7,3 L 7,7 M 10,3 L 10,7 M 13,3 L 13,7 M 8,12 L 16,12 M 8,15 L 13,15");

    private readonly InstalledPluginDescriptor _descriptor;
    private readonly PluginPackageInstaller _packageInstaller;
    private readonly OutOfProcessPluginRuntime _runtime;
    private readonly IStructuredLogger _logger;
    private readonly LocalizationService _localization;
    private readonly HostSettingsService _settings;
    private readonly IHostUiDispatcher _uiDispatcher;
    private readonly Geometry _iconGeometry = GenericIcon;
    private readonly bool _isInstalled = true;
    private OutOfProcessPluginSession? _session;
    private PluginState _state;
    private string? _errorMessage;
    private bool _isSelected;
    private bool _operationInProgress;
    private bool _disposed;

    internal PluginWorkspaceViewModel(
        InstalledPluginDescriptor descriptor,
        PluginPackageInstaller packageInstaller,
        OutOfProcessPluginRuntime runtime,
        IStructuredLogger logger,
        LocalizationService localization,
        HostSettingsService settings,
        IHostUiDispatcher uiDispatcher)
    {
        _descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
        _packageInstaller = packageInstaller ?? throw new ArgumentNullException(nameof(packageInstaller));
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _localization = localization ?? throw new ArgumentNullException(nameof(localization));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _uiDispatcher = uiDispatcher ?? throw new ArgumentNullException(nameof(uiDispatcher));
        _state = PluginState.CreateInstalled(Manifest).TransitionTo(PluginLifecycleState.Disabled);
        if (!SupportsOutOfProcess)
        {
            _errorMessage = $"Plugin '{PluginId}' does not support the required 'outOfProcess' execution mode.";
        }

        _localization.LanguageChanged += OnLanguageChanged;
        _settings.Changed += OnSettingsChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string PluginId => Manifest.Id;

    public string DisplayName => Manifest.Name;

    public string Publisher => Manifest.Publisher;

    public string InstallDialogTitle => _localization["InstallPluginDialogTitle"];

    public Geometry IconGeometry => _iconGeometry;

    public PluginManifest Manifest => _descriptor.Manifest;

    public string VersionDirectory => _descriptor.VersionDirectory;

    public bool IsInstalled => _isInstalled;

    public bool IsOpened => _settings.IsPluginOpened(PluginId);

    public bool IsRuntimeEnabled => _state.LifecycleState == PluginLifecycleState.Running;

    public string InstalledVersion => Manifest.Version;

    public string RuntimeMode => Manifest.Runtime.PreferredMode.ToString();

    public string RuntimeDescription => Manifest.Runtime.Background
        ? $"{RuntimeMode} · background metadata"
        : RuntimeMode;

    public PluginLifecycleState LifecycleState => _state.LifecycleState;

    public string LifecycleStateLabel => _localization[$"PluginState{LifecycleState}"];

    public string StatusDescription => LifecycleState switch
    {
        PluginLifecycleState.Running => _localization["PluginRunningDescription"],
        PluginLifecycleState.Faulted or PluginLifecycleState.DisableFailed or PluginLifecycleState.RestartRequired
            => ErrorMessage,
        PluginLifecycleState.Starting => _localization["PluginStartingDescription"],
        PluginLifecycleState.Stopping => _localization["PluginStoppingDescription"],
        _ when !SupportsOutOfProcess => _localization["PluginUnsupportedRuntimeDescription"],
        _ => _localization["PluginDisabledDescription"]
    };

    public bool SupportsOutOfProcess => Manifest.Runtime.SupportedModes.Contains(PluginExecutionMode.OutOfProcess);

    public bool IsRuntimeActionEnabled => !_operationInProgress
        && (LifecycleState is PluginLifecycleState.Disabled or PluginLifecycleState.Running)
        && SupportsOutOfProcess;

    public string RuntimeActionLabel => LifecycleState == PluginLifecycleState.Running
        ? _localization["DisablePlugin"]
        : _localization["EnablePlugin"];

    public bool IsInstallEnabled => !_operationInProgress
        && LifecycleState is not PluginLifecycleState.Running
        and not PluginLifecycleState.Starting
        and not PluginLifecycleState.Stopping;

    public bool IsUninstallEnabled => IsInstallEnabled;

    public Brush StatusAccentBrush => LifecycleState switch
    {
        PluginLifecycleState.Running => HealthyBrush,
        PluginLifecycleState.Faulted or PluginLifecycleState.DisableFailed or PluginLifecycleState.RestartRequired
            => ErrorBrush,
        PluginLifecycleState.Disabled => MutedBrush,
        _ => WarningBrush
    };

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public string ErrorMessage => _errorMessage
        ?? _state.LastErrorMessage
        ?? string.Empty;

    public bool RequiresHostRestart => LifecycleState == PluginLifecycleState.RestartRequired;

    public string OpenedStateLabel => IsOpened
        ? _localization["StatusOpened"]
        : _localization["StatusClosed"];

    public bool IsSelected
    {
        get => _isSelected;
        internal set
        {
            if (_isSelected == value)
            {
                return;
            }

            _isSelected = value;
            OnPropertyChanged();
        }
    }

    internal async Task<bool> SetOpenedAsync(bool opened)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!opened && IsRuntimeEnabled && !await SetRuntimeEnabledAsync(false).ConfigureAwait(false))
        {
            return false;
        }

        _settings.SetPluginOpened(PluginId, opened);
        RefreshState();
        return true;
    }

    internal Task<bool> SetRuntimeEnabledAsync(bool enabled)
    {
        return enabled ? EnableAsync() : DisableAsync();
    }

    internal async Task<PluginPackageInstallResult> InstallPackageAsync(string packagePath)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!IsInstallEnabled)
        {
            throw new InvalidOperationException(_localization["DisablePluginBeforeUpdate"]);
        }

        var result = await _packageInstaller.InstallAsync(packagePath).ConfigureAwait(false);
        _logger.Log(
            LogLevel.Information,
            "Package",
            $"Installed plugin package '{result.PluginId}' version '{result.Version}'.",
            pluginId: result.PluginId,
            pluginVersion: result.Version);
        return result;
    }

    internal async Task<PluginPackageUninstallResult> UninstallAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!IsUninstallEnabled)
        {
            throw new InvalidOperationException(_localization["DisablePluginBeforeUninstall"]);
        }

        var result = await _packageInstaller.UninstallAsync(PluginId, InstalledVersion).ConfigureAwait(false);
        _logger.Log(
            LogLevel.Information,
            "Package",
            $"Uninstalled plugin package '{PluginId}' version '{InstalledVersion}'.",
            pluginId: PluginId,
            pluginVersion: InstalledVersion);
        return result;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _localization.LanguageChanged -= OnLanguageChanged;
        _settings.Changed -= OnSettingsChanged;

        try
        {
            _session?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            _logger.Error(
                "Plugin",
                $"Plugin '{PluginId}' cleanup failed.",
                errorCode: "PLUGIN_DISPOSE_FAILED",
                exception: exception);
        }
        finally
        {
            _session = null;
        }
    }

    private async Task<bool> EnableAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!SupportsOutOfProcess)
        {
            SetError(new PluginLoadException(
                "PLUGIN_RUNTIME_MODE_UNSUPPORTED",
                $"Plugin '{PluginId}' does not support 'outOfProcess' execution."));
            return false;
        }

        if (!BeginOperation())
        {
            return IsRuntimeEnabled;
        }

        if (LifecycleState != PluginLifecycleState.Disabled)
        {
            EndOperationIfStarted();
            return IsRuntimeEnabled;
        }

        try
        {
            ClearError();
            _state = _state.TransitionTo(PluginLifecycleState.Starting);
            RefreshState();
            _session = await _runtime.StartAsync(VersionDirectory).ConfigureAwait(false);
            await _session.StartPluginAsync().ConfigureAwait(false);
            _state = _session.State;
            _logger.Log(
                LogLevel.Information,
                "Plugin",
                $"Plugin '{PluginId}' enabled.",
                pluginId: PluginId,
                pluginVersion: InstalledVersion);
            return true;
        }
        catch (Exception exception)
        {
            if (_session is not null)
            {
                _state = _session.State;
                try
                {
                    await _session.DisposeAsync().ConfigureAwait(false);
                }
                catch
                {
                    // The session already owns the termination deadline.
                }

                _session = null;
            }

            if (_state.LifecycleState == PluginLifecycleState.Starting)
            {
                _state = _state.TransitionTo(
                    PluginLifecycleState.Faulted,
                    errorCode: GetErrorCode(exception, "PLUGIN_START_FAILED"),
                    errorMessage: exception.Message);
            }

            SetError(exception);
            _logger.Error(
                "Plugin",
                $"Plugin '{PluginId}' could not be enabled.",
                errorCode: GetErrorCode(exception, "PLUGIN_START_FAILED"),
                exception: exception);
            return false;
        }
        finally
        {
            EndOperationIfStarted();
            RefreshState();
        }
    }

    private async Task<bool> DisableAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!BeginOperation())
        {
            return !IsRuntimeEnabled;
        }

        try
        {
            if (_session is null)
            {
                return !IsRuntimeEnabled;
            }

            try
            {
                await _session.StopAsync().ConfigureAwait(false);
                _state = _session.State;
                _logger.Log(
                    LogLevel.Information,
                    "Plugin",
                    $"Plugin '{PluginId}' disabled.",
                    pluginId: PluginId,
                    pluginVersion: InstalledVersion);
                return _state.LifecycleState == PluginLifecycleState.Disabled;
            }
            catch (Exception exception)
            {
                _state = _session.State;
                SetError(exception);
                _logger.Error(
                    "Plugin",
                    $"Plugin '{PluginId}' could not be disabled.",
                    errorCode: GetErrorCode(exception, "PLUGIN_STOP_FAILED"),
                    exception: exception);
                return false;
            }
            finally
            {
                await _session.DisposeAsync().ConfigureAwait(false);
                _session = null;
            }
        }
        finally
        {
            EndOperationIfStarted();
            RefreshState();
        }
    }

    private bool BeginOperation()
    {
        if (_operationInProgress)
        {
            return false;
        }

        _operationInProgress = true;
        RefreshState();
        return true;
    }

    private void EndOperationIfStarted()
    {
        _operationInProgress = false;
        RefreshState();
    }

    private void SetError(Exception exception)
    {
        _errorMessage = exception.Message;
        RefreshState();
    }

    private void ClearError()
    {
        _errorMessage = null;
        RefreshState();
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        _uiDispatcher.Dispatch(() =>
        {
            OnPropertyChanged(nameof(InstallDialogTitle));
            OnPropertyChanged(nameof(LifecycleStateLabel));
            OnPropertyChanged(nameof(StatusDescription));
            OnPropertyChanged(nameof(RuntimeActionLabel));
            OnPropertyChanged(nameof(OpenedStateLabel));
        });
    }

    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        _uiDispatcher.Dispatch(() =>
        {
            OnPropertyChanged(nameof(IsOpened));
            OnPropertyChanged(nameof(OpenedStateLabel));
        });
    }

    private void RefreshState()
    {
        _uiDispatcher.Dispatch(() =>
        {
            OnPropertyChanged(nameof(IsInstalled));
            OnPropertyChanged(nameof(IsOpened));
            OnPropertyChanged(nameof(IsRuntimeEnabled));
            OnPropertyChanged(nameof(LifecycleState));
            OnPropertyChanged(nameof(LifecycleStateLabel));
            OnPropertyChanged(nameof(StatusDescription));
            OnPropertyChanged(nameof(IsRuntimeActionEnabled));
            OnPropertyChanged(nameof(RuntimeActionLabel));
            OnPropertyChanged(nameof(IsInstallEnabled));
            OnPropertyChanged(nameof(IsUninstallEnabled));
            OnPropertyChanged(nameof(StatusAccentBrush));
            OnPropertyChanged(nameof(HasError));
            OnPropertyChanged(nameof(ErrorMessage));
            OnPropertyChanged(nameof(RequiresHostRestart));
            OnPropertyChanged(nameof(OpenedStateLabel));
        });
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private static string GetErrorCode(Exception exception, string fallback)
    {
        return exception switch
        {
            PluginPackageException packageException => packageException.ErrorCode,
            PluginLoadException loadException => loadException.ErrorCode,
            WorkerProtocolException workerException => workerException.ErrorCode,
            _ => fallback
        };
    }

    private static SolidColorBrush CreateBrush(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)!);
        brush.Freeze();
        return brush;
    }
}
