using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using ToolBox.Core.Diagnostics;
using ToolBox.Core.Packaging;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace ToolBox.Host;

[SuppressMessage("Design", "CA1001:TypesThatOwnDisposableFieldsShouldBeDisposable", Justification = "WPF Application owns and closes runtime resources in OnExit.")]
public partial class App : Application
{
    private readonly string _hostVersion = typeof(App).Assembly.GetName().Version?.ToString(3) ?? "0.1.0";
    private StructuredLogger? _logger;
    private HostDiagnostics? _diagnostics;
    private MainWindowViewModel? _viewModel;
    private PluginPackageInstaller? _packageInstaller;
    private LocalizationService? _localization;
    private HostSettingsService? _settings;
    private TrayIconService? _trayIcon;
    private MainWindow? _mainWindow;
    private bool _shutdownRequested;
    private bool _shutdownStarted;
    private bool _restartRequested;
    private string? _restartExecutablePath;

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
        if (_shutdownStarted)
        {
            base.OnExit(e);
            return;
        }

        _shutdownStarted = true;
        var restartRequested = _restartRequested;

        try
        {
            _diagnostics?.TransitionTo(StartupStage.Stopping);
            _logger?.Info("Host", "Host shutdown started.");

            _viewModel?.Dispose();
            _trayIcon?.Dispose();
            _trayIcon = null;

            _diagnostics?.TransitionTo(StartupStage.Stopped);
            _logger?.Info("Host", "Host shutdown completed.");
        }
        catch (Exception exception)
        {
            RecordException("HOST_SHUTDOWN_FAILED", exception);
        }
        finally
        {
            try
            {
                _logger?.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
            catch (Exception exception)
            {
                Debug.WriteLine($"ToolBox logger shutdown failed: {exception}");
            }

            _packageInstaller?.Dispose();
            _packageInstaller = null;

            if (restartRequested)
            {
                LaunchReplacementProcess();
            }
        }

        base.OnExit(e);
    }

    private void StartHost()
    {
        _settings = new HostSettingsService();
        _localization = new LocalizationService(_settings);
        var sessionId = Guid.NewGuid().ToString("N");
        var launchAttemptId = Guid.NewGuid().ToString("N");

        _diagnostics = new HostDiagnostics(launchAttemptId, sessionId, _hostVersion);
        _logger = new StructuredLogger(new LoggerOptions(), sessionId, _hostVersion);

        string? activeKeyboardTestDirectory = null;
        string? activeAudioRelayDirectory = null;
        var pluginsRoot = Path.Combine(AppContext.BaseDirectory, "Plugins");
        var pluginDataRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ToolBox",
            "Plugins");

        _packageInstaller = new PluginPackageInstaller(pluginsRoot, pluginDataRoot);
        activeKeyboardTestDirectory = ResolveActiveProductDirectory(
            "com.toolbox.keyboard-test",
            "Keyboard & Mouse Test");
        activeAudioRelayDirectory = ResolveActiveProductDirectory(
            "com.toolbox.audio-relay",
            "Phone Audio Relay");

        _viewModel = new MainWindowViewModel(
            _diagnostics,
            _logger,
            activeKeyboardTestDirectory,
            activeAudioRelayDirectory,
            _packageInstaller ?? throw new InvalidOperationException("Package installer was not initialized."),
            _localization,
            _settings);

        _mainWindow = new MainWindow(_viewModel);
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
        if (_shutdownRequested || _mainWindow is null)
        {
            return;
        }

        _mainWindow.Hide();
        _trayIcon?.ShowBackgroundNotification();
        _logger?.Info("Host", "Main window hidden to the system tray.");
    }

    internal void RequestShutdown()
    {
        if (_shutdownRequested)
        {
            return;
        }

        _shutdownRequested = true;
        _mainWindow?.PrepareForShutdown();
        Shutdown();
    }

    internal void RequestRestart()
    {
        if (_shutdownRequested)
        {
            return;
        }

        if (!TryGetRestartExecutable(out var executablePath))
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

        _restartExecutablePath = executablePath;
        _restartRequested = true;
        _shutdownRequested = true;
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

    private void LaunchReplacementProcess()
    {
        var executablePath = _restartExecutablePath;
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = executablePath,
                WorkingDirectory = Path.GetDirectoryName(executablePath) ?? AppContext.BaseDirectory,
                UseShellExecute = true
            });
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"ToolBox restart process could not be launched: {exception}");
            MessageBox.Show(
                _localization?["RestartLaunchFailed"]
                    ?? "ToolBox could not launch the replacement process. Close and reopen it manually.",
                _localization?["RestartUnavailableTitle"] ?? "ToolBox cannot restart automatically",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private static bool TryGetRestartExecutable(out string executablePath)
    {
        executablePath = string.Empty;
        var processPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(processPath)
            || !Path.IsPathFullyQualified(processPath)
            || !File.Exists(processPath)
            || !processPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            || string.Equals(Path.GetFileName(processPath), "dotnet.exe", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        executablePath = processPath;
        return true;
    }

    private void ShowMainWindow()
    {
        if (_shutdownRequested || _mainWindow is null)
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

    private string? ResolveActiveProductDirectory(string pluginId, string productName)
    {
        try
        {
            return _packageInstaller?.GetActiveVersionDirectory(pluginId);
        }
        catch (PluginPackageException exception)
        {
            _logger?.Error(
                "Package",
                $"The active {productName} package could not be resolved.",
                errorCode: exception.ErrorCode,
                exception: exception);
            return null;
        }
    }

    private void Advance(StartupStage stage, string message)
    {
        _diagnostics?.TransitionTo(stage);
        _logger?.Info("Host", message);
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
            Debug.WriteLine($"ToolBox exception logging failed: {loggingException}");
            Debug.WriteLine($"Original exception: {exception}");
        }
    }
}
