namespace ToolBox.Core.Diagnostics;

public sealed record HostDiagnosticsSnapshot(
    string LaunchAttemptId,
    string SessionId,
    string HostVersion,
    StartupStage Stage,
    DateTimeOffset UpdatedAtUtc,
    string? LastErrorCode,
    string? LastErrorMessage,
    string? LastPluginStarting,
    string? LastPluginStarted);

public sealed class HostDiagnostics
{
    private readonly object _gate = new();
    private readonly string _launchAttemptId;
    private readonly string _sessionId;
    private readonly string _hostVersion;
    private StartupStage _stage = StartupStage.Created;
    private DateTimeOffset _updatedAtUtc = DateTimeOffset.UtcNow;
    private string? _lastErrorCode;
    private string? _lastErrorMessage;
    private string? _lastPluginStarting;
    private string? _lastPluginStarted;

    public HostDiagnostics(string launchAttemptId, string sessionId, string hostVersion)
    {
        _launchAttemptId = RequireValue(launchAttemptId, nameof(launchAttemptId));
        _sessionId = RequireValue(sessionId, nameof(sessionId));
        _hostVersion = RequireValue(hostVersion, nameof(hostVersion));
    }

    public event Action<HostDiagnosticsSnapshot>? Changed;

    public HostDiagnosticsSnapshot Snapshot()
    {
        lock (_gate)
        {
            return SnapshotUnsafe();
        }
    }

    public void TransitionTo(StartupStage stage)
    {
        HostDiagnosticsSnapshot snapshot;

        lock (_gate)
        {
            _stage = stage;
            _updatedAtUtc = DateTimeOffset.UtcNow;
            snapshot = SnapshotUnsafe();
        }

        NotifyChanged(snapshot);
    }

    public void RecordFailure(string errorCode, Exception exception)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);
        ArgumentNullException.ThrowIfNull(exception);

        HostDiagnosticsSnapshot snapshot;

        lock (_gate)
        {
            _stage = StartupStage.Faulted;
            _updatedAtUtc = DateTimeOffset.UtcNow;
            _lastErrorCode = errorCode;
            _lastErrorMessage = exception.Message;
            snapshot = SnapshotUnsafe();
        }

        NotifyChanged(snapshot);
    }

    public void RecordPluginStarting(string pluginId)
    {
        var snapshot = UpdatePlugin(pluginId, starting: true);
        NotifyChanged(snapshot);
    }

    public void RecordPluginStarted(string pluginId)
    {
        var snapshot = UpdatePlugin(pluginId, starting: false);
        NotifyChanged(snapshot);
    }

    private HostDiagnosticsSnapshot UpdatePlugin(string pluginId, bool starting)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);

        lock (_gate)
        {
            _updatedAtUtc = DateTimeOffset.UtcNow;

            if (starting)
            {
                _lastPluginStarting = pluginId;
            }
            else
            {
                _lastPluginStarted = pluginId;
            }

            return SnapshotUnsafe();
        }
    }

    private HostDiagnosticsSnapshot SnapshotUnsafe()
    {
        return new HostDiagnosticsSnapshot(
            _launchAttemptId,
            _sessionId,
            _hostVersion,
            _stage,
            _updatedAtUtc,
            _lastErrorCode,
            _lastErrorMessage,
            _lastPluginStarting,
            _lastPluginStarted);
    }

    private void NotifyChanged(HostDiagnosticsSnapshot snapshot)
    {
        try
        {
            Changed?.Invoke(snapshot);
        }
        catch
        {
            // Diagnostics subscribers must not be able to break the Host lifecycle.
        }
    }

    private static string RequireValue(string value, string parameterName)
    {
        return string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("A non-empty value is required.", parameterName)
            : value;
    }
}
