using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using ToolBox.Core.Diagnostics;
using ToolBox.Core.Packaging;
using ToolBox.Core.Plugins;
using ToolBox.PluginSdk;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;

namespace ToolBox.Host;

public enum ShellPage
{
    Overview,
    Plugin,
    Settings
}

public sealed class MainWindowViewModel : INotifyPropertyChanged, IDisposable
{
    private static readonly Brush HealthyBrush = CreateBrush("#92E6B5");
    private static readonly Brush WarningBrush = CreateBrush("#F5B85B");
    private static readonly Brush ErrorBrush = CreateBrush("#FF8F86");

    private readonly HostDiagnostics _diagnostics;
    private readonly IStructuredLogger _logger;
    private readonly InstalledPluginCatalog _pluginCatalog;
    private readonly PluginPackageInstaller _packageInstaller;
    private readonly PluginPackageInspector _packageInspector;
    private readonly OutOfProcessPluginRuntime _runtime;
    private readonly LocalizationService _localization;
    private readonly HostSettingsService _settings;
    private readonly IHostUiDispatcher _uiDispatcher;
    private readonly ObservableCollection<PluginWorkspaceViewModel> _pluginWorkspaces = [];
    private readonly ObservableCollection<PluginWorkspaceViewModel> _installedPluginWorkspaces = [];
    private readonly ObservableCollection<PluginWorkspaceViewModel> _openedPluginWorkspaces = [];
    private HostDiagnosticsSnapshot _snapshot;
    private ShellPage _selectedPage = ShellPage.Overview;
    private PluginWorkspaceViewModel? _selectedPluginWorkspace;
    private string? _pluginManagerError;
    private bool _disposed;

    internal MainWindowViewModel(
        HostDiagnostics diagnostics,
        IStructuredLogger logger,
        InstalledPluginCatalog pluginCatalog,
        PluginPackageInstaller packageInstaller,
        OutOfProcessPluginRuntime runtime,
        LocalizationService localization,
        HostSettingsService settings,
        IHostUiDispatcher? uiDispatcher = null)
    {
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _pluginCatalog = pluginCatalog ?? throw new ArgumentNullException(nameof(pluginCatalog));
        _packageInstaller = packageInstaller ?? throw new ArgumentNullException(nameof(packageInstaller));
        _packageInspector = new PluginPackageInspector();
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _localization = localization ?? throw new ArgumentNullException(nameof(localization));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _uiDispatcher = uiDispatcher ?? ImmediateHostUiDispatcher.Instance;
        _snapshot = _diagnostics.Snapshot();

        RecentEvents = new ObservableCollection<DiagnosticEventViewModel>();
        _diagnostics.Changed += OnDiagnosticsChanged;
        _logger.EventWritten += OnEventWritten;
        _localization.LanguageChanged += OnLanguageChanged;
        _settings.Changed += OnSettingsChanged;

        PluginWorkspaces = new ReadOnlyObservableCollection<PluginWorkspaceViewModel>(_pluginWorkspaces);
        InstalledPluginWorkspaces = new ReadOnlyObservableCollection<PluginWorkspaceViewModel>(_installedPluginWorkspaces);
        OpenedPluginWorkspaces = new ReadOnlyObservableCollection<PluginWorkspaceViewModel>(_openedPluginWorkspaces);
        RefreshPluginWorkspaces();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<DiagnosticEventViewModel> RecentEvents { get; }

    public ReadOnlyObservableCollection<PluginWorkspaceViewModel> PluginWorkspaces { get; }

    public ReadOnlyObservableCollection<PluginWorkspaceViewModel> InstalledPluginWorkspaces { get; }

    public ReadOnlyObservableCollection<PluginWorkspaceViewModel> OpenedPluginWorkspaces { get; }

    public string WindowTitle => T("AppTitle");

    public bool IsOverviewPage => _selectedPage == ShellPage.Overview;

    public bool IsPluginPage => _selectedPage == ShellPage.Plugin;

    public bool IsSettingsPage => _selectedPage == ShellPage.Settings;

    public PluginWorkspaceViewModel? SelectedPluginWorkspace => _selectedPluginWorkspace;

    public bool HasOpenedPlugins => OpenedPluginCount > 0;

    public int OpenedPluginCount => OpenedPluginWorkspaces.Count;

    public bool CloseToTray => _settings.CloseBehavior == CloseBehavior.MinimizeToTray;

    public bool CloseDirectly => _settings.CloseBehavior == CloseBehavior.Exit;

    public bool HasInstalledPlugins => InstalledPluginCount > 0;

    public bool HasNoInstalledPlugins => !HasInstalledPlugins;

    public int InstalledPluginCount => InstalledPluginWorkspaces.Count;

    public string InstalledPluginCountLabel => string.Format(
        CultureInfo.CurrentCulture,
        T("InstalledPluginCount"),
        InstalledPluginCount);

    public string PluginCountSummaryLabel => string.Format(
        CultureInfo.CurrentCulture,
        T("PluginCountSummary"),
        InstalledPluginCount,
        OpenedPluginCount);

    public string CurrentLanguageLabel => _localization.CurrentLanguage == AppLanguage.Chinese
        ? "中文"
        : "English";

    public bool HasPluginManagerError => !string.IsNullOrWhiteSpace(_pluginManagerError);

    public string PluginManagerError => _pluginManagerError ?? string.Empty;

    public string StatusLabel => _snapshot.Stage switch
    {
        StartupStage.Healthy => T("StatusHealthy"),
        StartupStage.Faulted => T("StatusFaulted"),
        StartupStage.Stopping => T("StatusStopping"),
        StartupStage.Stopped => T("StatusStopped"),
        _ => T("StatusStarting")
    };

    public string StatusDescription => _snapshot.Stage switch
    {
        StartupStage.Healthy => T("HostHealthyDescription"),
        StartupStage.Faulted => string.Format(
            CultureInfo.CurrentCulture,
            T("HostFaultedDescription"),
            _snapshot.LastErrorCode ?? T("UnknownFailure")),
        StartupStage.Stopping => T("HostStoppingDescription"),
        StartupStage.Stopped => T("HostStoppedDescription"),
        _ => T("HostStartingDescription")
    };

    public Brush StatusAccentBrush => _snapshot.Stage switch
    {
        StartupStage.Healthy => HealthyBrush,
        StartupStage.Faulted => ErrorBrush,
        StartupStage.Stopping or StartupStage.Stopped => WarningBrush,
        _ => WarningBrush
    };

    public string StageLabel => _snapshot.Stage switch
    {
        StartupStage.LoggingReady => T("StageLoggingReady"),
        StartupStage.CoreReady => T("StageCoreReady"),
        StartupStage.ShellReady => T("StageShellReady"),
        StartupStage.Healthy => T("StatusHealthy"),
        StartupStage.Stopping => T("StatusStopping"),
        StartupStage.Stopped => T("StatusStopped"),
        StartupStage.Faulted => T("StatusFaulted"),
        _ => T("StageCreated")
    };

    public string HostVersion => _snapshot.HostVersion;

    public string SessionId => _snapshot.SessionId;

    public string LaunchAttemptId => _snapshot.LaunchAttemptId;

    public string UpdatedText => _snapshot.UpdatedAtUtc
        .ToLocalTime()
        .ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    public int EventCount => RecentEvents.Count;

    public string EventCountLabel => string.Format(
        CultureInfo.CurrentCulture,
        T("CapturedEvents"),
        EventCount);

    public string Localize(string key) => T(key);

    public void SelectPage(ShellPage page)
    {
        if (page == ShellPage.Plugin && _selectedPluginWorkspace?.IsOpened != true)
        {
            page = ShellPage.Overview;
        }

        if (_selectedPage == page)
        {
            return;
        }

        _selectedPage = page;
        if (page != ShellPage.Plugin)
        {
            SetSelectedPluginWorkspace(null);
        }

        NotifyPageProperties();
    }

    public void SelectPluginWorkspace(PluginWorkspaceViewModel workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        if (!_pluginWorkspaces.Contains(workspace) || !workspace.IsOpened)
        {
            SelectPage(ShellPage.Overview);
            return;
        }

        SetSelectedPluginWorkspace(workspace);
        _selectedPage = ShellPage.Plugin;
        NotifyPageProperties();
    }

    public void SetLanguage(AppLanguage language)
    {
        _localization.SetLanguage(language);
    }

    internal void SetCloseBehavior(CloseBehavior behavior)
    {
        _settings.SetCloseBehavior(behavior);
    }

    public async Task ToggleWorkspaceOpenedAsync(PluginWorkspaceViewModel workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        EnsureWorkspace(workspace);

        ClearPluginManagerError();
        if (!await workspace.SetOpenedAsync(!workspace.IsOpened).ConfigureAwait(false))
        {
            CapturePluginError(workspace);
        }
    }

    public async Task ToggleWorkspaceRuntimeAsync(PluginWorkspaceViewModel workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        EnsureWorkspace(workspace);

        ClearPluginManagerError();
        if (!await workspace.SetRuntimeEnabledAsync(!workspace.IsRuntimeEnabled).ConfigureAwait(false))
        {
            CapturePluginError(workspace);
        }
    }

    public async Task InstallWorkspacePackageAsync(
        PluginWorkspaceViewModel workspace,
        string packagePath)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);
        EnsureWorkspace(workspace);

        try
        {
            ClearPluginManagerError();
            var manifest = _packageInspector.ReadManifest(packagePath);
            EnsureOutOfProcessSupport(manifest);
            if (!string.Equals(manifest.Id, workspace.PluginId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(string.Format(
                    CultureInfo.CurrentCulture,
                    T("UnsupportedPluginPackage"),
                    manifest.Id));
            }

            await workspace.InstallPackageAsync(packagePath).ConfigureAwait(false);
            _settings.SetPluginOpened(manifest.Id, opened: true);
            RefreshPluginWorkspaces();
        }
        catch (Exception exception)
        {
            RecordPackageInstallFailure(exception);
        }
    }

    public async Task InstallPackageAsync(string packagePath)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);
        ClearPluginManagerError();

        try
        {
            var manifest = _packageInspector.ReadManifest(packagePath);
            EnsureOutOfProcessSupport(manifest);
            var existing = _pluginWorkspaces.SingleOrDefault(candidate => string.Equals(
                candidate.PluginId,
                manifest.Id,
                StringComparison.Ordinal));

            if (existing is not null)
            {
                await InstallWorkspacePackageAsync(existing, packagePath).ConfigureAwait(false);
                return;
            }

            var result = await _packageInstaller.InstallAsync(packagePath).ConfigureAwait(false);
            _settings.SetPluginOpened(result.PluginId, opened: true);
            _logger.Log(
                LogLevel.Information,
                "Package",
                $"Installed plugin package '{result.PluginId}' version '{result.Version}'.",
                pluginId: result.PluginId,
                pluginVersion: result.Version);
            RefreshPluginWorkspaces();
        }
        catch (Exception exception)
        {
            RecordPackageInstallFailure(exception);
        }
    }

    public async Task UninstallWorkspaceAsync(PluginWorkspaceViewModel workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        EnsureWorkspace(workspace);

        try
        {
            ClearPluginManagerError();
            var result = await workspace.UninstallAsync().ConfigureAwait(false);
            if (result.ActiveVersionAfterUninstall is null)
            {
                _settings.RemovePlugin(workspace.PluginId);
            }

            RefreshPluginWorkspaces();
        }
        catch (Exception exception)
        {
            SetPluginManagerError(exception.Message);
            _logger.Error(
                "Package",
                $"Plugin '{workspace.PluginId}' could not be uninstalled.",
                errorCode: exception is PluginPackageException packageException
                    ? packageException.ErrorCode
                    : "PACKAGE_UNINSTALL_FAILED",
                exception: exception);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _diagnostics.Changed -= OnDiagnosticsChanged;
        _logger.EventWritten -= OnEventWritten;
        _localization.LanguageChanged -= OnLanguageChanged;
        _settings.Changed -= OnSettingsChanged;
        foreach (var workspace in _pluginWorkspaces.ToArray())
        {
            workspace.PropertyChanged -= OnWorkspacePropertyChanged;
            workspace.Dispose();
        }

        _pluginWorkspaces.Clear();
        _installedPluginWorkspaces.Clear();
        _openedPluginWorkspaces.Clear();
    }

    private void RefreshPluginWorkspaces()
    {
        if (_disposed)
        {
            return;
        }

        var selectedPluginId = _selectedPluginWorkspace?.PluginId;
        foreach (var workspace in _pluginWorkspaces.ToArray())
        {
            workspace.PropertyChanged -= OnWorkspacePropertyChanged;
            workspace.Dispose();
        }

        _pluginWorkspaces.Clear();
        var snapshot = _pluginCatalog.Scan();
        foreach (var issue in snapshot.Issues)
        {
            _logger.Error(
                "Package",
                $"Plugin '{issue.PluginId}' was skipped during discovery: {issue.Message}",
                errorCode: issue.ErrorCode,
                exception: issue.Exception);
        }

        foreach (var descriptor in snapshot.Plugins)
        {
            var workspace = new PluginWorkspaceViewModel(
                descriptor,
                _packageInstaller,
                _runtime,
                _logger,
                _localization,
                _settings,
                _uiDispatcher);
            workspace.PropertyChanged += OnWorkspacePropertyChanged;
            _pluginWorkspaces.Add(workspace);
        }

        var selected = selectedPluginId is null
            ? null
            : _pluginWorkspaces.SingleOrDefault(workspace =>
                string.Equals(workspace.PluginId, selectedPluginId, StringComparison.Ordinal)
                && workspace.IsOpened);
        SetSelectedPluginWorkspace(selected);
        RefreshWorkspaceCollections();
    }

    private void OnDiagnosticsChanged(HostDiagnosticsSnapshot snapshot)
    {
        _uiDispatcher.Dispatch(() => ApplyDiagnostics(snapshot));
    }

    private void OnEventWritten(LogEvent entry)
    {
        _uiDispatcher.Dispatch(() => AddEvent(entry));
    }

    private void OnWorkspacePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not PluginWorkspaceViewModel workspace)
        {
            return;
        }

        if (string.Equals(e.PropertyName, nameof(PluginWorkspaceViewModel.IsInstalled), StringComparison.Ordinal)
            || string.Equals(e.PropertyName, nameof(PluginWorkspaceViewModel.IsOpened), StringComparison.Ordinal))
        {
            RefreshWorkspaceCollections();
        }

        if (ReferenceEquals(_selectedPluginWorkspace, workspace) && !workspace.IsOpened)
        {
            _selectedPage = ShellPage.Settings;
            SetSelectedPluginWorkspace(null);
            NotifyPageProperties();
        }
    }

    private void ApplyDiagnostics(HostDiagnosticsSnapshot snapshot)
    {
        if (_disposed)
        {
            return;
        }

        _snapshot = snapshot;
        OnPropertyChanged(nameof(StatusLabel));
        OnPropertyChanged(nameof(StatusDescription));
        OnPropertyChanged(nameof(StatusAccentBrush));
        OnPropertyChanged(nameof(StageLabel));
        OnPropertyChanged(nameof(UpdatedText));
    }

    private void AddEvent(LogEvent entry)
    {
        if (_disposed)
        {
            return;
        }

        RecentEvents.Insert(0, new DiagnosticEventViewModel(entry));
        while (RecentEvents.Count > 80)
        {
            RecentEvents.RemoveAt(RecentEvents.Count - 1);
        }

        OnPropertyChanged(nameof(EventCount));
        OnPropertyChanged(nameof(EventCountLabel));
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(WindowTitle));
        OnPropertyChanged(nameof(CurrentLanguageLabel));
        OnPropertyChanged(nameof(InstalledPluginCountLabel));
        OnPropertyChanged(nameof(PluginCountSummaryLabel));
        OnPropertyChanged(nameof(StatusLabel));
        OnPropertyChanged(nameof(StatusDescription));
        OnPropertyChanged(nameof(StageLabel));
        OnPropertyChanged(nameof(EventCountLabel));
    }

    private void NotifyPageProperties()
    {
        OnPropertyChanged(nameof(IsOverviewPage));
        OnPropertyChanged(nameof(IsPluginPage));
        OnPropertyChanged(nameof(IsSettingsPage));
    }

    private void SetSelectedPluginWorkspace(PluginWorkspaceViewModel? workspace)
    {
        if (ReferenceEquals(_selectedPluginWorkspace, workspace))
        {
            return;
        }

        if (_selectedPluginWorkspace is not null)
        {
            _selectedPluginWorkspace.IsSelected = false;
        }

        _selectedPluginWorkspace = workspace;
        if (_selectedPluginWorkspace is not null)
        {
            _selectedPluginWorkspace.IsSelected = true;
        }

        OnPropertyChanged(nameof(SelectedPluginWorkspace));
    }

    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(CloseToTray));
        OnPropertyChanged(nameof(CloseDirectly));
    }

    private void RefreshWorkspaceCollections()
    {
        ReplaceCollection(_installedPluginWorkspaces, _pluginWorkspaces.Where(workspace => workspace.IsInstalled));
        ReplaceCollection(_openedPluginWorkspaces, _pluginWorkspaces.Where(workspace => workspace.IsOpened));

        OnPropertyChanged(nameof(HasInstalledPlugins));
        OnPropertyChanged(nameof(HasNoInstalledPlugins));
        OnPropertyChanged(nameof(HasOpenedPlugins));
        OnPropertyChanged(nameof(InstalledPluginCount));
        OnPropertyChanged(nameof(OpenedPluginCount));
        OnPropertyChanged(nameof(InstalledPluginCountLabel));
        OnPropertyChanged(nameof(PluginCountSummaryLabel));
    }

    private void CapturePluginError(PluginWorkspaceViewModel workspace)
    {
        if (workspace.HasError)
        {
            SetPluginManagerError(workspace.ErrorMessage);
        }
    }

    private void EnsureWorkspace(PluginWorkspaceViewModel workspace)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_pluginWorkspaces.Contains(workspace))
        {
            throw new ArgumentOutOfRangeException(nameof(workspace));
        }
    }

    private static void ReplaceCollection(
        ObservableCollection<PluginWorkspaceViewModel> target,
        IEnumerable<PluginWorkspaceViewModel> source)
    {
        var items = source.ToArray();
        if (target.SequenceEqual(items))
        {
            return;
        }

        target.Clear();
        foreach (var item in items)
        {
            target.Add(item);
        }
    }

    private void ClearPluginManagerError()
    {
        SetPluginManagerError(null);
    }

    private void SetPluginManagerError(string? message)
    {
        _pluginManagerError = message;
        OnPropertyChanged(nameof(HasPluginManagerError));
        OnPropertyChanged(nameof(PluginManagerError));
    }

    private void RecordPackageInstallFailure(Exception exception)
    {
        SetPluginManagerError(exception.Message);
        _logger.Error(
            "Package",
            "The selected plugin package could not be installed.",
            errorCode: exception is PluginPackageException packageException
                ? packageException.ErrorCode
                : "PACKAGE_INSTALL_FAILED",
            exception: exception);
    }

    private static void EnsureOutOfProcessSupport(PluginManifest manifest)
    {
        if (manifest.Runtime is null
            || !manifest.Runtime.SupportedModes.Contains(PluginExecutionMode.OutOfProcess))
        {
            throw new PluginLoadException(
                "PLUGIN_RUNTIME_MODE_UNSUPPORTED",
                $"Plugin '{manifest.Id}' does not support the required 'outOfProcess' execution mode.");
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private string T(string key) => _localization[key];

    private static SolidColorBrush CreateBrush(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)!);
        brush.Freeze();
        return brush;
    }
}

public sealed class DiagnosticEventViewModel
{
    private static readonly Brush InformationBrush = CreateBrush("#A9C7E8");
    private static readonly Brush WarningBrush = CreateBrush("#F5B85B");
    private static readonly Brush ErrorBrush = CreateBrush("#FF8F86");

    public DiagnosticEventViewModel(LogEvent entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        TimestampText = entry.Timestamp.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        LevelText = entry.Level.ToString().ToUpperInvariant();
        Module = entry.Module;
        Message = entry.Message;
        LevelBrush = entry.Level switch
        {
            LogLevel.Warning => WarningBrush,
            LogLevel.Error or LogLevel.Critical => ErrorBrush,
            _ => InformationBrush
        };
    }

    public string TimestampText { get; }
    public string LevelText { get; }
    public string Module { get; }
    public string Message { get; }
    public Brush LevelBrush { get; }

    private static SolidColorBrush CreateBrush(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)!);
        brush.Freeze();
        return brush;
    }
}
