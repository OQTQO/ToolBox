using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using ToolBox.Core.Diagnostics;
using ToolBox.Core.Packaging;
using ToolBox.PluginSdk;
using Brush = System.Windows.Media.Brush;
using Application = System.Windows.Application;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;

namespace ToolBox.Host;

public enum ShellPage
{
    Overview,
    KeyboardTest,
    AudioRelay,
    Settings
}

public sealed class MainWindowViewModel : INotifyPropertyChanged, IDisposable
{
    private const string KeyboardPluginId = "com.toolbox.keyboard-test";
    private const string AudioRelayPluginId = "com.toolbox.audio-relay";
    private static readonly Brush HealthyBrush = CreateBrush("#92E6B5");
    private static readonly Brush WarningBrush = CreateBrush("#F5B85B");
    private static readonly Brush ErrorBrush = CreateBrush("#FF8F86");

    private readonly HostDiagnostics _diagnostics;
    private readonly IStructuredLogger _logger;
    private readonly LocalizationService _localization;
    private readonly HostSettingsService _settings;
    private readonly Dispatcher _dispatcher;
    private HostDiagnosticsSnapshot _snapshot;
    private ShellPage _selectedPage = ShellPage.Overview;
    private string? _pluginManagerError;
    private bool _disposed;

    internal MainWindowViewModel(
        HostDiagnostics diagnostics,
        IStructuredLogger logger,
        string? keyboardTestPluginDirectory,
        string? audioRelayPluginDirectory,
        PluginPackageInstaller packageInstaller,
        LocalizationService localization,
        HostSettingsService settings)
    {
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _localization = localization ?? throw new ArgumentNullException(nameof(localization));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        _snapshot = _diagnostics.Snapshot();

        KeyboardTest = new KeyboardTestViewModel(
            _logger,
            keyboardTestPluginDirectory,
            packageInstaller,
            _localization);
        AudioRelay = new AudioRelayViewModel(
            _logger,
            audioRelayPluginDirectory,
            packageInstaller,
            _localization);
        RecentEvents = new ObservableCollection<DiagnosticEventViewModel>();

        KeyboardTest.PropertyChanged += OnPluginPropertyChanged;
        AudioRelay.PropertyChanged += OnPluginPropertyChanged;
        _diagnostics.Changed += OnDiagnosticsChanged;
        _logger.EventWritten += OnEventWritten;
        _localization.LanguageChanged += OnLanguageChanged;
        _settings.Changed += OnSettingsChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<DiagnosticEventViewModel> RecentEvents { get; }

    public KeyboardTestViewModel KeyboardTest { get; }

    public AudioRelayViewModel AudioRelay { get; }

    public string WindowTitle => T("AppTitle");

    public bool IsOverviewPage => _selectedPage == ShellPage.Overview;

    public bool IsKeyboardPage => _selectedPage == ShellPage.KeyboardTest;

    public bool IsAudioRelayPage => _selectedPage == ShellPage.AudioRelay;

    public bool IsSettingsPage => _selectedPage == ShellPage.Settings;

    public bool IsKeyboardInstalled => KeyboardTest.IsInstalled;

    public bool IsAudioRelayInstalled => AudioRelay.IsInstalled;

    public bool IsKeyboardOpened => IsKeyboardInstalled && _settings.IsPluginOpened(KeyboardPluginId);

    public bool IsAudioRelayOpened => IsAudioRelayInstalled && _settings.IsPluginOpened(AudioRelayPluginId);

    public bool HasOpenedPlugins => OpenedPluginCount > 0;

    public int OpenedPluginCount => (IsKeyboardOpened ? 1 : 0) + (IsAudioRelayOpened ? 1 : 0);

    public bool CloseToTray => _settings.CloseBehavior == CloseBehavior.MinimizeToTray;

    public bool CloseDirectly => _settings.CloseBehavior == CloseBehavior.Exit;

    public string KeyboardOpenedStateLabel => IsKeyboardOpened ? T("StatusOpened") : T("StatusClosed");

    public string AudioRelayOpenedStateLabel => IsAudioRelayOpened ? T("StatusOpened") : T("StatusClosed");

    public bool HasInstalledPlugins => InstalledPluginCount > 0;

    public bool HasNoInstalledPlugins => !HasInstalledPlugins;

    public int InstalledPluginCount => (IsKeyboardInstalled ? 1 : 0) + (IsAudioRelayInstalled ? 1 : 0);

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
        if ((page == ShellPage.KeyboardTest && !IsKeyboardOpened)
            || (page == ShellPage.AudioRelay && !IsAudioRelayOpened))
        {
            page = ShellPage.Overview;
        }

        if (_selectedPage == page)
        {
            return;
        }

        _selectedPage = page;
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

    public async Task ToggleKeyboardOpenedAsync()
    {
        await SetPluginOpenedAsync(KeyboardPluginId, !IsKeyboardOpened);
    }

    public async Task ToggleAudioRelayOpenedAsync()
    {
        await SetPluginOpenedAsync(AudioRelayPluginId, !IsAudioRelayOpened);
    }

    public async Task InstallPackageAsync(string packagePath)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);
        ClearPluginManagerError();

        try
        {
            var manifest = ReadPackageManifest(packagePath);
            switch (manifest.Id)
            {
                case KeyboardPluginId:
                    if (!KeyboardTest.IsInstallEnabled)
                    {
                        throw new InvalidOperationException(T("DisablePluginBeforeUpdate"));
                    }

                    await KeyboardTest.InstallPackageAsync(packagePath);
                    CapturePluginError(KeyboardTest);
                    if (KeyboardTest.IsInstalled && !KeyboardTest.HasError)
                    {
                        _settings.SetPluginOpened(KeyboardPluginId, opened: true);
                    }
                    break;

                case AudioRelayPluginId:
                    if (!AudioRelay.IsInstallEnabled)
                    {
                        throw new InvalidOperationException(T("DisablePluginBeforeUpdate"));
                    }

                    await AudioRelay.InstallPackageAsync(packagePath);
                    CapturePluginError(AudioRelay);
                    if (AudioRelay.IsInstalled && !AudioRelay.HasError)
                    {
                        _settings.SetPluginOpened(AudioRelayPluginId, opened: true);
                    }
                    break;

                default:
                    throw new InvalidOperationException(string.Format(
                        CultureInfo.CurrentCulture,
                        T("UnsupportedPluginPackage"),
                        manifest.Id));
            }
        }
        catch (Exception exception)
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
    }

    public async Task UninstallKeyboardAsync()
    {
        ClearPluginManagerError();
        await KeyboardTest.UninstallAsync();
        CapturePluginError(KeyboardTest);
        if (!KeyboardTest.IsInstalled)
        {
            _settings.RemovePlugin(KeyboardPluginId);
        }
    }

    public async Task UninstallAudioRelayAsync()
    {
        ClearPluginManagerError();
        await AudioRelay.UninstallAsync();
        CapturePluginError(AudioRelay);
        if (!AudioRelay.IsInstalled)
        {
            _settings.RemovePlugin(AudioRelayPluginId);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        KeyboardTest.PropertyChanged -= OnPluginPropertyChanged;
        AudioRelay.PropertyChanged -= OnPluginPropertyChanged;
        _diagnostics.Changed -= OnDiagnosticsChanged;
        _logger.EventWritten -= OnEventWritten;
        _localization.LanguageChanged -= OnLanguageChanged;
        _settings.Changed -= OnSettingsChanged;
        AudioRelay.Dispose();
        KeyboardTest.Dispose();
    }

    private void OnDiagnosticsChanged(HostDiagnosticsSnapshot snapshot)
    {
        if (_dispatcher.CheckAccess())
        {
            ApplyDiagnostics(snapshot);
        }
        else
        {
            _dispatcher.BeginInvoke(new Action(() => ApplyDiagnostics(snapshot)));
        }
    }

    private void OnEventWritten(LogEvent entry)
    {
        if (_dispatcher.CheckAccess())
        {
            AddEvent(entry);
        }
        else
        {
            _dispatcher.BeginInvoke(new Action(() => AddEvent(entry)));
        }
    }

    private void OnPluginPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!string.Equals(e.PropertyName, nameof(KeyboardTestViewModel.IsInstalled), StringComparison.Ordinal)
            && !string.Equals(e.PropertyName, nameof(KeyboardTestViewModel.IsRuntimeEnabled), StringComparison.Ordinal))
        {
            return;
        }

        OnPropertyChanged(nameof(IsKeyboardInstalled));
        OnPropertyChanged(nameof(IsAudioRelayInstalled));
        NotifyPluginPresentationProperties();

        if ((_selectedPage == ShellPage.KeyboardTest && !IsKeyboardOpened)
            || (_selectedPage == ShellPage.AudioRelay && !IsAudioRelayOpened))
        {
            _selectedPage = ShellPage.Settings;
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
        OnPropertyChanged(nameof(KeyboardOpenedStateLabel));
        OnPropertyChanged(nameof(AudioRelayOpenedStateLabel));
        OnPropertyChanged(nameof(StatusLabel));
        OnPropertyChanged(nameof(StatusDescription));
        OnPropertyChanged(nameof(StageLabel));
        OnPropertyChanged(nameof(EventCountLabel));
    }

    private void NotifyPageProperties()
    {
        OnPropertyChanged(nameof(IsOverviewPage));
        OnPropertyChanged(nameof(IsKeyboardPage));
        OnPropertyChanged(nameof(IsAudioRelayPage));
        OnPropertyChanged(nameof(IsSettingsPage));
    }

    private async Task SetPluginOpenedAsync(string pluginId, bool opened)
    {
        ClearPluginManagerError();

        if (pluginId == KeyboardPluginId)
        {
            if (!KeyboardTest.IsInstalled)
            {
                return;
            }

            if (!opened && !await KeyboardTest.SetRuntimeEnabledAsync(enabled: false))
            {
                CapturePluginError(KeyboardTest);
                return;
            }
        }
        else if (pluginId == AudioRelayPluginId)
        {
            if (!AudioRelay.IsInstalled)
            {
                return;
            }

            if (!opened && !await AudioRelay.SetRuntimeEnabledAsync(enabled: false))
            {
                CapturePluginError(AudioRelay);
                return;
            }
        }
        else
        {
            throw new ArgumentOutOfRangeException(nameof(pluginId));
        }

        _settings.SetPluginOpened(pluginId, opened);
    }

    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        NotifyPluginPresentationProperties();
        OnPropertyChanged(nameof(CloseToTray));
        OnPropertyChanged(nameof(CloseDirectly));

        if ((_selectedPage == ShellPage.KeyboardTest && !IsKeyboardOpened)
            || (_selectedPage == ShellPage.AudioRelay && !IsAudioRelayOpened))
        {
            _selectedPage = ShellPage.Settings;
            NotifyPageProperties();
        }
    }

    private void NotifyPluginPresentationProperties()
    {
        OnPropertyChanged(nameof(IsKeyboardInstalled));
        OnPropertyChanged(nameof(IsAudioRelayInstalled));
        OnPropertyChanged(nameof(IsKeyboardOpened));
        OnPropertyChanged(nameof(IsAudioRelayOpened));
        OnPropertyChanged(nameof(HasInstalledPlugins));
        OnPropertyChanged(nameof(HasNoInstalledPlugins));
        OnPropertyChanged(nameof(HasOpenedPlugins));
        OnPropertyChanged(nameof(InstalledPluginCount));
        OnPropertyChanged(nameof(OpenedPluginCount));
        OnPropertyChanged(nameof(InstalledPluginCountLabel));
        OnPropertyChanged(nameof(PluginCountSummaryLabel));
        OnPropertyChanged(nameof(KeyboardOpenedStateLabel));
        OnPropertyChanged(nameof(AudioRelayOpenedStateLabel));
    }

    private void CapturePluginError(INotifyPropertyChanged plugin)
    {
        var error = plugin switch
        {
            KeyboardTestViewModel keyboard when keyboard.HasError => keyboard.ErrorMessage,
            AudioRelayViewModel audio when audio.RequiresHostRestart => T("RelayRestartRequiredDescription"),
            AudioRelayViewModel audio when audio.HasError => audio.ErrorMessage,
            _ => null
        };

        if (!string.IsNullOrWhiteSpace(error))
        {
            SetPluginManagerError(error);
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

    private static PluginManifest ReadPackageManifest(string packagePath)
    {
        using var archive = ZipFile.OpenRead(packagePath);
        var entry = archive.Entries.SingleOrDefault(candidate => string.Equals(
            candidate.FullName.Replace('\\', '/'),
            "manifest.json",
            StringComparison.Ordinal));
        if (entry is null || entry.Length > 1024 * 1024)
        {
            throw new PluginPackageException(
                "BAD_MANIFEST_PACKAGE",
                "The selected package does not contain a valid root manifest.json.");
        }

        using var stream = entry.Open();
        using var reader = new StreamReader(stream);
        return new PluginManifestParser().Parse(reader.ReadToEnd());
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
