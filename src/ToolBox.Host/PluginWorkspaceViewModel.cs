using System.Collections.ObjectModel;
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
public sealed partial class PluginWorkspaceViewModel : INotifyPropertyChanged, IDisposable
{
    private static readonly Brush HealthyBrush = CreateBrush("#CFFF52");
    private static readonly Brush WarningBrush = CreateBrush("#8C9B51");
    private static readonly Brush ErrorBrush = CreateBrush("#A94D3E");
    private static readonly Brush MutedBrush = CreateBrush("#66766B");
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
    private readonly ObservableCollection<PluginUiActionViewModel> _uiActions = [];
    private readonly ObservableCollection<PluginUiValueViewModel> _uiValues = [];
    private readonly SemaphoreSlim _lifecycleOperationGate = new(1, 1);
    private readonly SemaphoreSlim _uiOperationGate = new(1, 1);
    private OutOfProcessPluginSession? _session;
    private PluginState _state;
    private PluginUiSnapshot? _uiSnapshot;
    private string? _errorMessage;
    private string? _uiErrorMessage;
    private bool _isSelected;
    private int _operationInProgress;
    private int _uiOperationInProgress;
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

        UiActions = new ReadOnlyObservableCollection<PluginUiActionViewModel>(_uiActions);
        UiValues = new ReadOnlyObservableCollection<PluginUiValueViewModel>(_uiValues);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string PluginId => Manifest.Id;

    public string DisplayName => Manifest.Name;

    public string IconLabel => BuildIconLabel(DisplayName);

    public string Publisher => Manifest.Publisher;

    public string InstallDialogTitle => _localization["InstallPluginDialogTitle"];

    public Geometry IconGeometry => _iconGeometry;

    public PluginManifest Manifest => _descriptor.Manifest;

    public string VersionDirectory => _descriptor.VersionDirectory;

    public bool IsInstalled => _isInstalled;

    public bool IsOpened => _settings.IsPluginOpened(PluginId);

    public ReadOnlyObservableCollection<PluginUiActionViewModel> UiActions { get; }

    public ReadOnlyObservableCollection<PluginUiValueViewModel> UiValues { get; }

    public PluginInputSurface? InputSurface => _uiSnapshot?.InputSurface;

    public bool HasPluginUi => IsRuntimeEnabled && _uiSnapshot is not null;

    public bool HasPluginUiUnavailable => IsRuntimeEnabled && _uiSnapshot is null;

    public bool IsPluginUiDisabled => !IsRuntimeEnabled;

    public bool HasUiActions => HasPluginUi && UiActions.Count > 0;

    public bool HasUiValues => HasPluginUi && UiValues.Count > 0;

    public bool HasInputSurface => HasPluginUi && InputSurface is not null;

    public string PluginUiStatusMessage => _uiSnapshot?.StatusMessage ?? string.Empty;

    public bool IsPluginUiActionEnabled => IsRuntimeEnabled
        && !IsOperationInProgress
        && !IsUiOperationInProgress;

    public bool IsPluginInputEnabled => IsRuntimeEnabled
        && !IsOperationInProgress
        && !IsUiOperationInProgress;

    public string PluginUiErrorMessage => _uiErrorMessage ?? string.Empty;

    public bool HasPluginUiError => !string.IsNullOrWhiteSpace(_uiErrorMessage);

    public bool IsRuntimeEnabled => _state.LifecycleState == PluginLifecycleState.Running;

    public string InstalledVersion => Manifest.Version;

    public string RuntimeMode => Manifest.Runtime.PreferredMode.ToString();

    public string RuntimeDescription => Manifest.Runtime.Background
        ? $"{RuntimeMode} · background metadata"
        : RuntimeMode;

    public bool IsRunning => LifecycleState == PluginLifecycleState.Running;

    public bool IsAttention => LifecycleState is PluginLifecycleState.Faulted or PluginLifecycleState.DisableFailed or PluginLifecycleState.RestartRequired or PluginLifecycleState.Quarantined;

    public bool IsBusy => IsOperationInProgress || IsUiOperationInProgress;

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

    public bool IsRuntimeActionEnabled => !IsOperationInProgress
        && !IsUiOperationInProgress
        && (LifecycleState is PluginLifecycleState.Disabled or PluginLifecycleState.Running)
        && SupportsOutOfProcess;

    public string RuntimeActionLabel => LifecycleState == PluginLifecycleState.Running
        ? _localization["DisablePlugin"]
        : _localization["EnablePlugin"];

    public bool IsInstallEnabled => !IsOperationInProgress
        && !IsUiOperationInProgress
        && LifecycleState is not PluginLifecycleState.Running
        and not PluginLifecycleState.Starting
        and not PluginLifecycleState.Stopping;

    public bool IsUninstallEnabled => IsInstallEnabled;

    public Brush StatusAccentBrush => LifecycleState switch
    {
        PluginLifecycleState.Running => ThemeBrush("HealthyBrush", HealthyBrush),
        PluginLifecycleState.Faulted or PluginLifecycleState.DisableFailed or PluginLifecycleState.RestartRequired
            => ThemeBrush("ErrorBrush", ErrorBrush),
        PluginLifecycleState.Disabled => ThemeBrush("MutedTextBrush", MutedBrush),
        _ => ThemeBrush("WarningBrush", WarningBrush)
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

    private bool IsOperationInProgress => Volatile.Read(ref _operationInProgress) != 0;

    private bool IsUiOperationInProgress => Volatile.Read(ref _uiOperationInProgress) != 0;

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
            RefreshPresentationSettings();
        });
    }

    internal void RefreshPresentationSettings()
    {
        OnPropertyChanged(nameof(StatusAccentBrush));
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
            OnPropertyChanged(nameof(HasPluginUi));
            OnPropertyChanged(nameof(HasPluginUiUnavailable));
            OnPropertyChanged(nameof(IsPluginUiDisabled));
            OnPropertyChanged(nameof(HasUiActions));
            OnPropertyChanged(nameof(HasUiValues));
            OnPropertyChanged(nameof(HasInputSurface));
            OnPropertyChanged(nameof(IsBusy));
            RefreshUiStateCore();
        });
    }

    private void ApplyUiSnapshot(PluginUiSnapshot? snapshot)
    {
        _uiDispatcher.Dispatch(() =>
        {
            if (_disposed)
            {
                return;
            }

            _uiSnapshot = snapshot;
            _uiActions.Clear();
            _uiValues.Clear();

            if (snapshot is not null)
            {
                foreach (var action in snapshot.Actions ?? Array.Empty<PluginUiAction>())
                {
                    _uiActions.Add(new PluginUiActionViewModel(this, action));
                }

                foreach (var value in snapshot.Values ?? Array.Empty<PluginUiValue>())
                {
                    _uiValues.Add(new PluginUiValueViewModel(value));
                }
            }

            OnPropertyChanged(nameof(HasPluginUi));
            OnPropertyChanged(nameof(HasPluginUiUnavailable));
            OnPropertyChanged(nameof(HasUiActions));
            OnPropertyChanged(nameof(HasUiValues));
            OnPropertyChanged(nameof(HasInputSurface));
            OnPropertyChanged(nameof(InputSurface));
            OnPropertyChanged(nameof(PluginUiStatusMessage));
            OnPropertyChanged(nameof(HasPluginUiError));
            OnPropertyChanged(nameof(PluginUiErrorMessage));
            OnPropertyChanged(nameof(IsBusy));
            RefreshUiStateCore();
        });
    }

    private void RefreshUiState()
    {
        _uiDispatcher.Dispatch(RefreshUiStateCore);
    }

    private void RefreshUiStateCore()
    {
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(IsPluginUiActionEnabled));
        OnPropertyChanged(nameof(IsPluginInputEnabled));
        foreach (var action in _uiActions)
        {
            action.RefreshEnabled();
        }
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

    private static string BuildIconLabel(string value)
    {
        var trimmed = value.Trim();
        var capitals = trimmed
            .Where(char.IsUpper)
            .Take(2)
            .ToArray();
        if (capitals.Length == 2)
        {
            return new string(capitals);
        }

        var letters = trimmed
            .Where(char.IsLetter)
            .Take(2)
            .ToArray();
        return letters.Length == 0
            ? "T"
            : new string(letters).ToUpperInvariant();
    }

    private static Brush ThemeBrush(string key, Brush fallback)
    {
        return System.Windows.Application.Current?.Resources[key] as Brush ?? fallback;
    }
}

public sealed class PluginUiActionViewModel : INotifyPropertyChanged
{
    private readonly PluginWorkspaceViewModel _workspace;

    internal PluginUiActionViewModel(
        PluginWorkspaceViewModel workspace,
        PluginUiAction descriptor)
    {
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    internal PluginUiAction Descriptor { get; }

    public string Label => Descriptor.Label;

    public string Description => Descriptor.Description ?? string.Empty;

    public bool HasDescription => !string.IsNullOrWhiteSpace(Descriptor.Description);

    public bool IsEnabled => Descriptor.IsEnabled && _workspace.IsPluginUiActionEnabled;

    internal string OperationKey => $"{_workspace.PluginId}:{Descriptor.Id}";

    internal void RefreshEnabled()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsEnabled)));
    }

    internal Task ExecuteAsync()
    {
        return _workspace.ExecuteUiActionAsync(this);
    }
}

public sealed class PluginUiValueViewModel
{
    internal PluginUiValueViewModel(PluginUiValue value)
    {
        ArgumentNullException.ThrowIfNull(value);
        Label = value.Label;
        Value = value.Value;
    }

    public string Label { get; }

    public string Value { get; }
}
