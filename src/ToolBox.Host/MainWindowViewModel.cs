using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
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
    Activity,
    Settings
}

public enum SettingsSection
{
    Appearance,
    Plugins,
    Runtime,
    About
}

public sealed partial class MainWindowViewModel : INotifyPropertyChanged, IDisposable
{
    private static readonly Brush HealthyBrush = CreateBrush("#CFFF52");
    private static readonly Brush WarningBrush = CreateBrush("#8C9B51");
    private static readonly Brush ErrorBrush = CreateBrush("#A94D3E");

    private readonly HostDiagnostics _diagnostics;
    private readonly IStructuredLogger _logger;
    private readonly InstalledPluginCatalog _pluginCatalog;
    private readonly PluginPackageInstaller _packageInstaller;
    private readonly PluginPackageInspector _packageInspector;
    private readonly OutOfProcessPluginRuntime _runtime;
    private readonly LocalizationService _localization;
    private readonly HostSettingsService _settings;
    private readonly IHostUiDispatcher _uiDispatcher;
    private readonly string _dataRoot;
    private readonly ObservableCollection<PluginWorkspaceViewModel> _pluginWorkspaces = [];
    private readonly ObservableCollection<PluginWorkspaceViewModel> _installedPluginWorkspaces = [];
    private readonly ObservableCollection<PluginWorkspaceViewModel> _openedPluginWorkspaces = [];
    private readonly ObservableCollection<PluginWorkspaceViewModel> _visiblePluginWorkspaces = [];
    private HostDiagnosticsSnapshot _snapshot;
    private ShellPage _selectedPage = ShellPage.Overview;
    private PluginWorkspaceViewModel? _selectedPluginWorkspace;
    private string? _pluginManagerError;
    private string _pluginSearchText = string.Empty;
    private string _pluginFilter = HostUiState.PluginFilters.All;
    private string _pluginSort = HostUiState.PluginSorts.Name;
    private SettingsSection _settingsSection = SettingsSection.Appearance;
    private bool _disposed;

    internal MainWindowViewModel(
        HostDiagnostics diagnostics,
        IStructuredLogger logger,
        InstalledPluginCatalog pluginCatalog,
        PluginPackageInstaller packageInstaller,
        OutOfProcessPluginRuntime runtime,
        LocalizationService localization,
        HostSettingsService settings,
        IHostUiDispatcher? uiDispatcher = null,
        string? dataRoot = null)
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
        _dataRoot = Path.GetFullPath(
            string.IsNullOrWhiteSpace(dataRoot)
                ? Path.Combine(AppContext.BaseDirectory, "Data")
                : dataRoot);
        _snapshot = _diagnostics.Snapshot();

        RecentEvents = new ObservableCollection<DiagnosticEventViewModel>();
        _diagnostics.Changed += OnDiagnosticsChanged;
        _logger.EventWritten += OnEventWritten;
        _localization.LanguageChanged += OnLanguageChanged;
        _settings.Changed += OnSettingsChanged;

        PluginWorkspaces = new ReadOnlyObservableCollection<PluginWorkspaceViewModel>(_pluginWorkspaces);
        InstalledPluginWorkspaces = new ReadOnlyObservableCollection<PluginWorkspaceViewModel>(_installedPluginWorkspaces);
        OpenedPluginWorkspaces = new ReadOnlyObservableCollection<PluginWorkspaceViewModel>(_openedPluginWorkspaces);
        VisiblePluginWorkspaces = new ReadOnlyObservableCollection<PluginWorkspaceViewModel>(_visiblePluginWorkspaces);
        RefreshPluginWorkspaces();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<DiagnosticEventViewModel> RecentEvents { get; }

    public ReadOnlyObservableCollection<PluginWorkspaceViewModel> PluginWorkspaces { get; }

    public ReadOnlyObservableCollection<PluginWorkspaceViewModel> InstalledPluginWorkspaces { get; }

    public ReadOnlyObservableCollection<PluginWorkspaceViewModel> OpenedPluginWorkspaces { get; }

    public ReadOnlyObservableCollection<PluginWorkspaceViewModel> VisiblePluginWorkspaces { get; }

    public IReadOnlyList<PluginWorkspaceViewModel> OverviewPluginWorkspaces =>
        InstalledPluginWorkspaces.Take(4).ToArray();

    public string WindowTitle => T("AppTitle");

    public bool IsOverviewPage => _selectedPage == ShellPage.Overview;

    public bool IsPluginPage => _selectedPage == ShellPage.Plugin;

    public bool IsSettingsPage => _selectedPage == ShellPage.Settings;

    public bool IsActivityPage => _selectedPage == ShellPage.Activity;

    public PluginWorkspaceViewModel? SelectedPluginWorkspace => _selectedPluginWorkspace;

    public bool HasSelectedPlugin => _selectedPluginWorkspace is not null;

    public int RunningPluginCount => InstalledPluginWorkspaces.Count(workspace => workspace.LifecycleState == PluginLifecycleState.Running);

    public int AttentionPluginCount => InstalledPluginWorkspaces.Count(workspace => workspace.LifecycleState is
        PluginLifecycleState.Faulted or PluginLifecycleState.DisableFailed or PluginLifecycleState.RestartRequired or PluginLifecycleState.Quarantined);

    public string OverviewTitle => _settings.OverviewTitle ?? T("OverviewDefaultTitle");

    public bool HasCustomOverviewTitle => !string.IsNullOrWhiteSpace(_settings.OverviewTitle);

    public string OverviewHealthTitle => _settings.OverviewHealthTitle ?? T("OverviewHealthTitleDefault");

    public bool HasCustomOverviewHealthTitle => !string.IsNullOrWhiteSpace(_settings.OverviewHealthTitle);

    public string TitleBarCenterText => _settings.TitleBarCenterText ?? T("DesktopToolManagement");

    public string OverviewHealthStatusLabel => AttentionPluginCount > 0
        ? T("Attention")
        : _snapshot.Stage == StartupStage.Healthy
            ? T("NoActionNeeded")
            : StatusLabel;

    public string OverviewHealthHeadline => AttentionPluginCount > 0
        ? T("HealthAttentionHeadline")
        : _snapshot.Stage == StartupStage.Healthy
            ? OverviewHealthTitle
            : StatusLabel;

    public string OverviewHealthDescription => AttentionPluginCount > 0
        ? string.Format(
            CultureInfo.CurrentCulture,
            T("HealthAttentionDescription"),
            AttentionPluginCount)
        : _snapshot.Stage == StartupStage.Healthy
            ? T("HealthCardDescription")
            : StatusDescription;

    public Brush OverviewHealthStatusBrush => AttentionPluginCount > 0
        ? ThemeBrush("ErrorBrush", ErrorBrush)
        : StatusAccentBrush;

    public string PageTitle => _selectedPage switch
    {
        ShellPage.Plugin => T("NavPlugins"),
        ShellPage.Activity => T("NavActivity"),
        ShellPage.Settings => T("Settings"),
        _ => OverviewTitle
    };

    public string PageDescription => _selectedPage switch
    {
        ShellPage.Plugin => T("PluginPageDescription"),
        ShellPage.Activity => T("ActivityPageDescription"),
        ShellPage.Settings => T("SettingsPageDescription"),
        _ => T("OverviewPageDescription")
    };

    public string HeaderStatusLabel => _snapshot.Stage == StartupStage.Healthy
        ? T("HostConnected")
        : StatusLabel;

    public string Theme => _settings.Theme;

    public bool DynamicGlow => _settings.DynamicGlow;

    public bool ReduceMotion => _settings.ReduceMotion;

    public bool Transparency => _settings.Transparency;

    public int CornerRadius => _settings.CornerRadius;

    public int BackgroundBrightness => _settings.BackgroundBrightness;

    public bool ConfirmEnable => _settings.ConfirmEnable;

    public bool ConfirmUninstall => _settings.ConfirmUninstall;

    public bool ShowDiagnostics => _settings.ShowDiagnostics;

    public SettingsSection SettingsSection => _settingsSection;

    public bool IsAppearanceSettings => _settingsSection == SettingsSection.Appearance;

    public bool IsPluginSettings => _settingsSection == SettingsSection.Plugins;

    public bool IsRuntimeSettings => _settingsSection == SettingsSection.Runtime;

    public bool IsAboutSettings => _settingsSection == SettingsSection.About;

    public string PluginSearchText => _pluginSearchText;

    public string PluginFilter => _pluginFilter;

    public string PluginSort => _pluginSort;

    public string PluginSortLabel => string.Format(
        CultureInfo.CurrentCulture,
        T("SortByValue"),
        _pluginSort switch
        {
            HostUiState.PluginSorts.Status => T("SortStatus"),
            HostUiState.PluginSorts.Version => T("SortVersion"),
            _ => T("SortName")
        });

    [SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "WPF binds this value through the view-model instance.")]
    public string ConfigDirectory => _dataRoot;

    public string StateDirectory => Path.Combine(ConfigDirectory, "PluginData");

    public string LogsDirectory => Path.Combine(ConfigDirectory, "Logs");

    public bool HasVisiblePlugins => VisiblePluginWorkspaces.Count > 0;

    public bool HasNoVisiblePlugins => !HasVisiblePlugins;

    public bool CloseToTray => _settings.CloseBehavior == CloseBehavior.MinimizeToTray;

    public bool CloseDirectly => _settings.CloseBehavior == CloseBehavior.Exit;

    public bool IsChineseLanguage => _localization.CurrentLanguage == AppLanguage.Chinese;

    public bool IsEnglishLanguage => _localization.CurrentLanguage == AppLanguage.English;

    public bool HasInstalledPlugins => InstalledPluginCount > 0;

    public bool HasNoInstalledPlugins => !HasInstalledPlugins;

    public int InstalledPluginCount => InstalledPluginWorkspaces.Count;

    public string InstalledPluginCountLabel => string.Format(
        CultureInfo.CurrentCulture,
        T("InstalledPluginCount"),
        InstalledPluginCount);

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
        StartupStage.Healthy => ThemeBrush("HealthyBrush", HealthyBrush),
        StartupStage.Faulted => ThemeBrush("ErrorBrush", ErrorBrush),
        StartupStage.Stopping or StartupStage.Stopped => ThemeBrush("WarningBrush", WarningBrush),
        _ => ThemeBrush("WarningBrush", WarningBrush)
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

    public bool HasNoEvents => EventCount == 0;

    public IReadOnlyList<DiagnosticEventViewModel> OverviewEvents =>
        RecentEvents.Where(entry => entry.IsOverviewRelevant).Take(4).ToArray();

    public bool HasNoOverviewEvents => OverviewEvents.Count == 0;

    public string EventCountLabel => string.Format(
        CultureInfo.CurrentCulture,
        T("CapturedEvents"),
        EventCount);

    public string Localize(string key) => T(key);

    public void SelectPage(ShellPage page)
    {
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
        if (!_pluginWorkspaces.Contains(workspace) || !workspace.IsInstalled)
        {
            SelectPage(ShellPage.Overview);
            return;
        }

        SetSelectedPluginWorkspace(workspace);
        _selectedPage = ShellPage.Plugin;
        NotifyPageProperties();
    }

    public void ClearSelectedPlugin()
    {
        SetSelectedPluginWorkspace(null);
    }

    public void ClearEvents()
    {
        RecentEvents.Clear();
        OnPropertyChanged(nameof(EventCount));
        OnPropertyChanged(nameof(HasNoEvents));
        OnPropertyChanged(nameof(EventCountLabel));
        OnPropertyChanged(nameof(OverviewEvents));
        OnPropertyChanged(nameof(HasNoOverviewEvents));
    }

    public void SetLanguage(AppLanguage language)
    {
        _localization.SetLanguage(language);
    }

    internal void SetCloseBehavior(CloseBehavior behavior)
    {
        _settings.SetCloseBehavior(behavior);
    }

    public void SelectSettingsSection(SettingsSection section)
    {
        if (_settingsSection == section)
        {
            return;
        }

        _settingsSection = section;
        OnPropertyChanged(nameof(SettingsSection));
        OnPropertyChanged(nameof(IsAppearanceSettings));
        OnPropertyChanged(nameof(IsPluginSettings));
        OnPropertyChanged(nameof(IsRuntimeSettings));
        OnPropertyChanged(nameof(IsAboutSettings));
    }

    public void SetOverviewTitle(string? title) => _settings.SetOverviewTitle(title);

    public void ResetOverviewTitle() => _settings.SetOverviewTitle(null);

    public void SetOverviewHealthTitle(string? title) => _settings.SetOverviewHealthTitle(title);

    public void ResetOverviewHealthTitle() => _settings.SetOverviewHealthTitle(null);

    public void SetTitleBarCenterText(string? text) => _settings.SetTitleBarCenterText(text);

    public void ResetTitleBarCenterText() => _settings.SetTitleBarCenterText(null);

    public void SetTheme(string theme)
    {
        _settings.SetTheme(theme);
        ThemeService.Apply(_settings.Theme, _settings.Transparency, _settings.DynamicGlow, _settings.BackgroundBrightness, _settings.CornerRadius);
    }

    public void SetAppearanceOption(bool? dynamicGlow = null, bool? reduceMotion = null, bool? transparency = null, int? cornerRadius = null, int? backgroundBrightness = null)
        => _settings.SetAppearanceOption(dynamicGlow, reduceMotion, transparency, cornerRadius, backgroundBrightness);

    public void SetPluginManagementOption(bool? confirmEnable = null, bool? confirmUninstall = null, bool? showDiagnostics = null)
        => _settings.SetPluginManagementOption(confirmEnable, confirmUninstall, showDiagnostics);

    internal void ReportUiFailure(string operation, Exception exception)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        ArgumentNullException.ThrowIfNull(exception);
        if (_disposed)
        {
            return;
        }

        SetPluginManagerError(exception.Message);
        _logger.Error(
            "Host UI",
            $"The UI operation '{operation}' failed.",
            errorCode: "HOST_UI_OPERATION_FAILED",
            exception: exception);
    }

    public void ResetAppearance()
    {
        _settings.ResetAppearance();
        ThemeService.Apply(_settings.Theme, _settings.Transparency, _settings.DynamicGlow, _settings.BackgroundBrightness, _settings.CornerRadius);
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
        else
        {
            RefreshVisiblePluginWorkspaces();
            NotifyOverviewStateProperties();
        }

        if (ReferenceEquals(_selectedPluginWorkspace, workspace) && !workspace.IsInstalled)
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
        OnPropertyChanged(nameof(HeaderStatusLabel));
        OnPropertyChanged(nameof(StatusDescription));
        OnPropertyChanged(nameof(StatusAccentBrush));
        OnPropertyChanged(nameof(StageLabel));
        OnPropertyChanged(nameof(UpdatedText));
        NotifyOverviewStateProperties();
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
        OnPropertyChanged(nameof(HasNoEvents));
        OnPropertyChanged(nameof(EventCountLabel));
        OnPropertyChanged(nameof(OverviewEvents));
        OnPropertyChanged(nameof(HasNoOverviewEvents));
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        _uiDispatcher.Dispatch(() =>
        {
            OnPropertyChanged(nameof(WindowTitle));
            OnPropertyChanged(nameof(CurrentLanguageLabel));
            OnPropertyChanged(nameof(IsChineseLanguage));
            OnPropertyChanged(nameof(IsEnglishLanguage));
            OnPropertyChanged(nameof(InstalledPluginCountLabel));
            OnPropertyChanged(nameof(PluginSortLabel));
            OnPropertyChanged(nameof(StatusLabel));
            OnPropertyChanged(nameof(HeaderStatusLabel));
            OnPropertyChanged(nameof(StatusDescription));
            OnPropertyChanged(nameof(StageLabel));
            OnPropertyChanged(nameof(EventCountLabel));
            OnPropertyChanged(nameof(OverviewTitle));
            OnPropertyChanged(nameof(HasCustomOverviewTitle));
            OnPropertyChanged(nameof(OverviewHealthTitle));
            OnPropertyChanged(nameof(HasCustomOverviewHealthTitle));
            OnPropertyChanged(nameof(TitleBarCenterText));
            OnPropertyChanged(nameof(OverviewHealthStatusLabel));
            OnPropertyChanged(nameof(OverviewHealthHeadline));
            OnPropertyChanged(nameof(OverviewHealthDescription));
            OnPropertyChanged(nameof(OverviewHealthStatusBrush));
            OnPropertyChanged(nameof(PageTitle));
            OnPropertyChanged(nameof(PageDescription));
        });
    }

    private void NotifyPageProperties()
    {
        OnPropertyChanged(nameof(IsOverviewPage));
        OnPropertyChanged(nameof(IsPluginPage));
        OnPropertyChanged(nameof(IsActivityPage));
        OnPropertyChanged(nameof(IsSettingsPage));
        OnPropertyChanged(nameof(PageTitle));
        OnPropertyChanged(nameof(PageDescription));
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
        OnPropertyChanged(nameof(HasSelectedPlugin));
    }

    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        _uiDispatcher.Dispatch(() =>
        {
            ThemeService.Apply(_settings.Theme, _settings.Transparency, _settings.DynamicGlow, _settings.BackgroundBrightness, _settings.CornerRadius);
            OnPropertyChanged(nameof(CloseToTray));
            OnPropertyChanged(nameof(CloseDirectly));
            OnPropertyChanged(nameof(OverviewTitle));
            OnPropertyChanged(nameof(HasCustomOverviewTitle));
            OnPropertyChanged(nameof(OverviewHealthTitle));
            OnPropertyChanged(nameof(HasCustomOverviewHealthTitle));
            OnPropertyChanged(nameof(TitleBarCenterText));
            OnPropertyChanged(nameof(PageTitle));
            OnPropertyChanged(nameof(PageDescription));
            OnPropertyChanged(nameof(Theme));
            OnPropertyChanged(nameof(DynamicGlow));
            OnPropertyChanged(nameof(ReduceMotion));
            OnPropertyChanged(nameof(Transparency));
            OnPropertyChanged(nameof(CornerRadius));
            OnPropertyChanged(nameof(BackgroundBrightness));
            OnPropertyChanged(nameof(ConfirmEnable));
            OnPropertyChanged(nameof(ConfirmUninstall));
            OnPropertyChanged(nameof(ShowDiagnostics));
            OnPropertyChanged(nameof(StatusAccentBrush));
            OnPropertyChanged(nameof(OverviewHealthStatusBrush));
            OnPropertyChanged(nameof(IsChineseLanguage));
            OnPropertyChanged(nameof(IsEnglishLanguage));
            foreach (var workspace in _pluginWorkspaces)
            {
                workspace.RefreshPresentationSettings();
            }
            RefreshVisiblePluginWorkspacesCore();
            NotifyOverviewStateProperties();
        });
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private string T(string key) => _localization[key];

    private static Brush ThemeBrush(string key, Brush fallback)
    {
        return System.Windows.Application.Current?.Resources[key] as Brush ?? fallback;
    }

    private void NotifyOverviewStateProperties()
    {
        OnPropertyChanged(nameof(RunningPluginCount));
        OnPropertyChanged(nameof(AttentionPluginCount));
        OnPropertyChanged(nameof(OverviewHealthStatusLabel));
        OnPropertyChanged(nameof(OverviewHealthHeadline));
        OnPropertyChanged(nameof(OverviewHealthDescription));
        OnPropertyChanged(nameof(OverviewHealthStatusBrush));
    }

    private static SolidColorBrush CreateBrush(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)!);
        brush.Freeze();
        return brush;
    }
}

public sealed class DiagnosticEventViewModel
{
    public DiagnosticEventViewModel(LogEvent entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        TimestampText = entry.Timestamp.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        Module = entry.Module;
        Message = entry.Message;
        IsOverviewRelevant = entry.Level >= LogLevel.Warning
            || !string.Equals(entry.Module, "Host", StringComparison.OrdinalIgnoreCase);
    }

    public string TimestampText { get; }
    public string Module { get; }
    public string Message { get; }
    public bool IsOverviewRelevant { get; }
}
