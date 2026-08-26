using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using ToolBox.Core.Diagnostics;
using ToolBox.Core.Packaging;

namespace ToolBox.Host;

public sealed class MainWindowViewModel : INotifyPropertyChanged, IDisposable
{
    private static readonly Brush HealthyBrush = CreateBrush("#92E6B5");
    private static readonly Brush WarningBrush = CreateBrush("#F5B85B");
    private static readonly Brush ErrorBrush = CreateBrush("#FF8F86");

    private readonly HostDiagnostics _diagnostics;
    private readonly IStructuredLogger _logger;
    private readonly Dispatcher _dispatcher;
    private HostDiagnosticsSnapshot _snapshot;
    private bool _disposed;

    public MainWindowViewModel(
        HostDiagnostics diagnostics,
        IStructuredLogger logger,
        string? keyboardTestPluginDirectory,
        PluginPackageInstaller packageInstaller)
    {
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        _snapshot = _diagnostics.Snapshot();
        KeyboardTest = new KeyboardTestViewModel(
            _logger,
            keyboardTestPluginDirectory,
            packageInstaller);

        RecentEvents = new ObservableCollection<DiagnosticEventViewModel>();
        _diagnostics.Changed += OnDiagnosticsChanged;
        _logger.EventWritten += OnEventWritten;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<DiagnosticEventViewModel> RecentEvents { get; }

    public KeyboardTestViewModel KeyboardTest { get; }

    public string StatusLabel => _snapshot.Stage switch
    {
        StartupStage.Healthy => "Healthy",
        StartupStage.Faulted => "Faulted",
        StartupStage.Stopping => "Stopping",
        StartupStage.Stopped => "Stopped",
        _ => "Starting"
    };

    public string StatusDescription => _snapshot.Stage switch
    {
        StartupStage.Healthy => "The Host shell is running and its lifecycle is visible.",
        StartupStage.Faulted => $"The Host reported {_snapshot.LastErrorCode ?? "an unknown failure"}.",
        StartupStage.Stopping => "The Host is closing its diagnostic session.",
        StartupStage.Stopped => "The Host closed cleanly.",
        _ => "The Host is moving through its startup contract."
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
        StartupStage.LoggingReady => "Logging ready",
        StartupStage.CoreReady => "Core ready",
        StartupStage.ShellReady => "Shell ready",
        StartupStage.Healthy => "Healthy",
        StartupStage.Stopping => "Stopping",
        StartupStage.Stopped => "Stopped",
        StartupStage.Faulted => "Faulted",
        _ => "Created"
    };

    public string HostVersion => _snapshot.HostVersion;

    public string SessionId => _snapshot.SessionId;

    public string LaunchAttemptId => _snapshot.LaunchAttemptId;

    public string UpdatedText => _snapshot.UpdatedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    public int EventCount => RecentEvents.Count;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _diagnostics.Changed -= OnDiagnosticsChanged;
        _logger.EventWritten -= OnEventWritten;
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

        while (RecentEvents.Count > 40)
        {
            RecentEvents.RemoveAt(RecentEvents.Count - 1);
        }

        OnPropertyChanged(nameof(EventCount));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
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
