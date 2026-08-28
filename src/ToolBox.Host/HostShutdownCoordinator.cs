namespace ToolBox.Host;

internal sealed record HostShutdownOperation(
    string Name,
    Action<HostExitPlan> Execute);

internal sealed record HostShutdownFailure(
    string OperationName,
    Exception Exception);

internal sealed record HostShutdownResult(
    bool Started,
    IReadOnlyList<HostShutdownFailure> Failures);

internal sealed record HostShutdownActions(
    Action TransitionToStopping,
    Action LogShutdownStarted,
    Action StopPluginViewModels,
    Action DisposeTray,
    Action TransitionToStopped,
    Action LogShutdownCompleted,
    Action DisposeLogger,
    Action DisposePackageInstaller,
    Action<string> LaunchReplacement);

internal sealed class HostShutdownCoordinator
{
    private readonly IReadOnlyList<HostShutdownOperation> _operations;
    private readonly Action<HostShutdownFailure> _reportFailure;
    private int _started;

    public HostShutdownCoordinator(
        IEnumerable<HostShutdownOperation> operations,
        Action<HostShutdownFailure> reportFailure)
    {
        ArgumentNullException.ThrowIfNull(operations);
        _reportFailure = reportFailure ?? throw new ArgumentNullException(nameof(reportFailure));
        _operations = operations.ToArray();

        if (_operations.Any(operation => string.IsNullOrWhiteSpace(operation.Name)))
        {
            throw new ArgumentException("Every shutdown operation requires a name.", nameof(operations));
        }
    }

    public static HostShutdownCoordinator CreateDefault(
        HostShutdownActions actions,
        Action<HostShutdownFailure> reportFailure)
    {
        ArgumentNullException.ThrowIfNull(actions);

        return new HostShutdownCoordinator(
        [
            new("diagnostics-stopping", _ => actions.TransitionToStopping()),
            new("log-shutdown-started", _ => actions.LogShutdownStarted()),
            new("stop-plugin-view-models", _ => actions.StopPluginViewModels()),
            new("dispose-tray", _ => actions.DisposeTray()),
            new("diagnostics-stopped", _ => actions.TransitionToStopped()),
            new("log-shutdown-completed", _ => actions.LogShutdownCompleted()),
            new("dispose-logger", _ => actions.DisposeLogger()),
            new("dispose-package-installer", _ => actions.DisposePackageInstaller()),
            new("launch-replacement", plan =>
            {
                if (plan.Intent == HostExitIntent.Restart
                    && !string.IsNullOrWhiteSpace(plan.RestartExecutablePath))
                {
                    actions.LaunchReplacement(plan.RestartExecutablePath);
                }
            })
        ],
        reportFailure);
    }

    public HostShutdownResult Run(HostExitPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        if (Interlocked.Exchange(ref _started, 1) != 0)
        {
            return new HostShutdownResult(Started: false, Array.Empty<HostShutdownFailure>());
        }

        var failures = new List<HostShutdownFailure>();
        foreach (var operation in _operations)
        {
            try
            {
                operation.Execute(plan);
            }
            catch (Exception exception)
            {
                var failure = new HostShutdownFailure(operation.Name, exception);
                failures.Add(failure);

                try
                {
                    _reportFailure(failure);
                }
                catch
                {
                    // Failure reporting must never prevent the remaining cleanup operations.
                }
            }
        }

        return new HostShutdownResult(Started: true, failures);
    }
}
