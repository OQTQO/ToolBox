using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using ToolBox.Core.Diagnostics;
using ToolBox.Core.Packaging;
using ToolBox.Core.Plugins;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace ToolBox.Host;

[SuppressMessage("Design", "CA1001:TypesThatOwnDisposableFieldsShouldBeDisposable", Justification = "WPF Application owns and closes runtime resources in OnExit.")]
public partial class App : Application, IHostApplicationCommands
{
    private readonly string _hostVersion = typeof(App).Assembly.GetName().Version?.ToString(3) ?? "0.2.2";
    private readonly HostLifetimeState _lifetime = new();
    private readonly HostRestartService _restartService = new();
    private StructuredLogger? _logger;
    private HostDiagnostics? _diagnostics;
    private MainWindowViewModel? _viewModel;
    private PluginPackageInstaller? _packageInstaller;
    private LocalizationService? _localization;
    private HostSettingsService? _settings;
    private TrayIconService? _trayIcon;
    private MainWindow? _mainWindow;
    private HostShutdownCoordinator? _shutdownCoordinator;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        try
        {
            StartHost();
        }
        catch (Exception exception)
        {
            RecordException("HOST_STARTUP_FAILED", exception);

            MessageBox.Show(
                string.Format(
                    System.Globalization.CultureInfo.CurrentCulture,
                    _localization?["StartupFailed"] ?? "ToolBox Host could not start.\n\n{0}",
                    exception.Message),
                _localization?["AppTitle"] ?? "ToolBox Host",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (!_lifetime.TryBeginShutdown(out var exitPlan))
        {
            base.OnExit(e);
            return;
        }

        _shutdownCoordinator ??= CreateShutdownCoordinator();
        _shutdownCoordinator.Run(exitPlan);

        base.OnExit(e);
    }

    private void StartHost()
    {
        _settings = new HostSettingsService();
        _localization = new LocalizationService(_settings);
        ThemeService.Apply(_settings.Theme, _settings.Transparency, _settings.DynamicGlow, _settings.BackgroundBrightness, _settings.CornerRadius);
        var sessionId = Guid.NewGuid().ToString("N");
        var launchAttemptId = Guid.NewGuid().ToString("N");

        _diagnostics = new HostDiagnostics(launchAttemptId, sessionId, _hostVersion);
        _logger = new StructuredLogger(new LoggerOptions(), sessionId, _hostVersion);

        var pluginsRoot = Path.Combine(AppContext.BaseDirectory, "Plugins");
        var pluginDataRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ToolBox",
            "Plugins");

        _packageInstaller = new PluginPackageInstaller(pluginsRoot, pluginDataRoot);
        var pluginCatalog = new InstalledPluginCatalog(_packageInstaller);
        var pluginRuntime = new OutOfProcessPluginRuntime(
            Path.Combine(AppContext.BaseDirectory, "ToolBox.PluginWorker.exe"));

        _viewModel = new MainWindowViewModel(
            _diagnostics,
            _logger,
            pluginCatalog,
            _packageInstaller,
            pluginRuntime,
            _localization,
            _settings,
            new WpfHostUiDispatcher(Dispatcher));

        _mainWindow = new MainWindow(_viewModel, this);
        MainWindow = _mainWindow;
        _trayIcon = new TrayIconService(_localization);
        _trayIcon.OpenRequested += OnTrayOpenRequested;
        _trayIcon.ExitRequested += OnTrayExitRequested;
        _mainWindow.Show();

        Advance(StartupStage.LoggingReady, "Structured logging is online.");
        Advance(StartupStage.CoreReady, "Host core services are initialized.");
        Advance(StartupStage.ShellReady, "WPF shell is displayed.");
        Advance(StartupStage.Healthy, "Host is ready for the next platform phase.");
    }

    internal void HideMainWindowToTray()
    {
        if (_lifetime.IsShutdownRequested || _mainWindow is null)
        {
            return;
        }

        _mainWindow.Hide();
        _trayIcon?.ShowBackgroundNotification();
        _logger?.Info("Host", "Main window hidden to the system tray.");
    }

    internal void RequestShutdown()
    {
        if (!_lifetime.TryRequestShutdown())
        {
            return;
        }

        _mainWindow?.PrepareForShutdown();
        Shutdown();
    }

    internal void RequestRestart()
    {
        if (_lifetime.IsShutdownRequested)
        {
            return;
        }

        if (!_restartService.TryGetExecutablePath(out var executablePath))
        {
            MessageBox.Show(
                _localization?["RestartUnavailableDescription"]
                    ?? "This running copy has no restartable executable path. Close and reopen ToolBox manually.",
                _localization?["RestartUnavailableTitle"] ?? "ToolBox cannot restart automatically",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            _logger?.Error(
                "Host",
                "Automatic restart was requested but this process has no restartable executable path.",
                errorCode: "HOST_RESTART_UNAVAILABLE");
            return;
        }

        if (!_lifetime.TryRequestRestart(executablePath))
        {
            return;
        }

        _mainWindow?.PrepareForShutdown();
        _logger?.Info("Host", "ToolBox restart requested after plugin lifecycle recovery.");
        Shutdown();
    }

    private void OnTrayOpenRequested(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(ShowMainWindow);
    }

    private void OnTrayExitRequested(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(RequestShutdown);
    }

    void IHostApplicationCommands.HideMainWindowToTray()
    {
        HideMainWindowToTray();
    }

    void IHostApplicationCommands.RequestShutdown()
    {
        RequestShutdown();
    }

    void IHostApplicationCommands.RequestRestart()
    {
        RequestRestart();
    }

    private void ShowMainWindow()
    {
        if (_lifetime.IsShutdownRequested || _mainWindow is null)
        {
            return;
        }

        if (!_mainWindow.IsVisible)
        {
            _mainWindow.Show();
        }

        if (_mainWindow.WindowState == WindowState.Minimized)
        {
            _mainWindow.WindowState = WindowState.Normal;
        }

        _mainWindow.Activate();
        _logger?.Info("Host", "Main window restored from the system tray.");
    }

    private void Advance(StartupStage stage, string message)
    {
        _diagnostics?.TransitionTo(stage);
        _logger?.Info("Host", message);
    }

    private HostShutdownCoordinator CreateShutdownCoordinator()
    {
        return HostShutdownCoordinator.CreateDefault(
            new HostShutdownActions(
                TransitionToStopping: () => _diagnostics?.TransitionTo(StartupStage.Stopping),
                LogShutdownStarted: () => _logger?.Info("Host", "Host shutdown started."),
                StopPluginViewModels: () =>
                {
                    try
                    {
                        _viewModel?.Dispose();
                    }
                    finally
                    {
                        _viewModel = null;
                    }
                },
                DisposeTray: () =>
                {
                    try
                    {
                        _trayIcon?.Dispose();
                    }
                    finally
                    {
                        _trayIcon = null;
                    }
                },
                TransitionToStopped: () => _diagnostics?.TransitionTo(StartupStage.Stopped),
                LogShutdownCompleted: () => _logger?.Info("Host", "Host shutdown completed."),
                DisposeLogger: () =>
                {
                    var logger = _logger;
                    _logger = null;
                    logger?.DisposeAsync().AsTask().GetAwaiter().GetResult();
                },
                DisposePackageInstaller: () =>
                {
                    try
                    {
                        _packageInstaller?.Dispose();
                    }
                    finally
                    {
                        _packageInstaller = null;
                    }
                },
                LaunchReplacement: _restartService.Launch),
            OnShutdownOperationFailed);
    }

    private void OnShutdownOperationFailed(HostShutdownFailure failure)
    {
        System.Diagnostics.Debug.WriteLine(
            $"ToolBox shutdown operation '{failure.OperationName}' failed: {failure.Exception}");
        RecordException(
            "HOST_SHUTDOWN_OPERATION_FAILED",
            new InvalidOperationException(
                $"Host shutdown operation '{failure.OperationName}' failed.",
                failure.Exception));

        if (string.Equals(failure.OperationName, "launch-replacement", StringComparison.Ordinal))
        {
            MessageBox.Show(
                _localization?["RestartLaunchFailed"]
                    ?? "ToolBox could not launch the replacement process. Close and reopen it manually.",
                _localization?["RestartUnavailableTitle"] ?? "ToolBox cannot restart automatically",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        RecordException("HOST_DISPATCHER_UNHANDLED", e.Exception);
        e.Handled = true;
    }

    private void OnUnhandledException(object? sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            RecordException("HOST_UNHANDLED", exception);
        }
        else
        {
            _logger?.Critical("Diagnostics", "An unknown unhandled exception was reported.", errorCode: "HOST_UNHANDLED");
        }
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        RecordException("HOST_UNOBSERVED_TASK", e.Exception);
        e.SetObserved();
    }

    private void RecordException(string errorCode, Exception exception)
    {
        try
        {
            _diagnostics?.RecordFailure(errorCode, exception);
            _logger?.Error("Diagnostics", "An application exception was captured.", errorCode: errorCode, exception: exception);
        }
        catch (Exception loggingException)
        {
            System.Diagnostics.Debug.WriteLine($"ToolBox exception logging failed: {loggingException}");
            System.Diagnostics.Debug.WriteLine($"Original exception: {exception}");
        }
    }
}
