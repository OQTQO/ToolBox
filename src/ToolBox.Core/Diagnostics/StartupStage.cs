namespace ToolBox.Core.Diagnostics;

public enum StartupStage
{
    Created = 0,
    LoggingReady = 1,
    CoreReady = 2,
    ShellReady = 3,
    Healthy = 4,
    Stopping = 5,
    Stopped = 6,
    Faulted = 7
}
