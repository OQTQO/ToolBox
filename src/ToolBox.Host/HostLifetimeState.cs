namespace ToolBox.Host;

internal enum HostExitIntent
{
    Shutdown,
    Restart
}

internal sealed record HostExitPlan(
    HostExitIntent Intent,
    string? RestartExecutablePath);

internal sealed class HostLifetimeState
{
    private readonly object _gate = new();
    private HostExitPlan? _requestedPlan;
    private bool _shutdownStarted;

    public bool IsShutdownRequested
    {
        get
        {
            lock (_gate)
            {
                return _requestedPlan is not null;
            }
        }
    }

    public bool TryRequestShutdown()
    {
        lock (_gate)
        {
            if (_requestedPlan is not null || _shutdownStarted)
            {
                return false;
            }

            _requestedPlan = new HostExitPlan(HostExitIntent.Shutdown, RestartExecutablePath: null);
            return true;
        }
    }

    public bool TryRequestRestart(string executablePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);

        lock (_gate)
        {
            if (_requestedPlan is not null || _shutdownStarted)
            {
                return false;
            }

            _requestedPlan = new HostExitPlan(HostExitIntent.Restart, executablePath);
            return true;
        }
    }

    public bool TryBeginShutdown(out HostExitPlan plan)
    {
        lock (_gate)
        {
            if (_shutdownStarted)
            {
                plan = _requestedPlan
                    ?? new HostExitPlan(HostExitIntent.Shutdown, RestartExecutablePath: null);
                return false;
            }

            _shutdownStarted = true;
            _requestedPlan ??= new HostExitPlan(HostExitIntent.Shutdown, RestartExecutablePath: null);
            plan = _requestedPlan;
            return true;
        }
    }
}
