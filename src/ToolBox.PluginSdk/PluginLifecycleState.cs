namespace ToolBox.PluginSdk;

public enum PluginLifecycleState
{
    NotInstalled,
    Installed,
    Disabled,
    Starting,
    Running,
    Stopping,
    DisableFailed,
    RestartRequired,
    Faulted,
    Quarantined
}
