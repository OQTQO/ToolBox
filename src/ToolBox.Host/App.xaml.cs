using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using ToolBox.Core.Diagnostics;
using ToolBox.Core.Packaging;

namespace ToolBox.Host;

[SuppressMessage("Design", "CA1001:TypesThatOwnDisposableFieldsShouldBeDisposable", Justification = "WPF Application owns and closes runtime resources in OnExit.")]
public partial class App : Application
{
    private readonly string _hostVersion = typeof(App).Assembly.GetName().Version?.ToString(3) ?? "0.1.0";
    private StructuredLogger? _logger;
    private HostDiagnostics? _diagnostics;
    private MainWindowViewModel? _viewModel;
    private PluginPackageInstaller? _packageInstaller;
    private bool _shutdownStarted;

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
                $"ToolBox Host could not start.\n\n{exception.Message}",
                "ToolBox Host",
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

        try
        {
            _diagnostics?.TransitionTo(StartupStage.Stopping);
            _logger?.Info("Host", "Host shutdown started.");

            _viewModel?.Dispose();

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
        }

        base.OnExit(e);
    }

    private void StartHost()
    {
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
            _packageInstaller ?? throw new InvalidOperationException("Package installer was not initialized."));

        var mainWindow = new MainWindow(_viewModel);
        MainWindow = mainWindow;
        mainWindow.Show();

        Advance(StartupStage.LoggingReady, "Structured logging is online.");
        Advance(StartupStage.CoreReady, "Host core services are initialized.");
        Advance(StartupStage.ShellReady, "WPF shell is displayed.");
        Advance(StartupStage.Healthy, "Host is ready for the next platform phase.");
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
