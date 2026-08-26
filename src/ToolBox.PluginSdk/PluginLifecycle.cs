namespace ToolBox.PluginSdk;

public static class PluginLifecycle
{
    public static bool CanTransition(PluginLifecycleState from, PluginLifecycleState to)
    {
        if (from == to)
        {
            return true;
        }

        return (from, to) switch
        {
            (PluginLifecycleState.NotInstalled, PluginLifecycleState.Installed) => true,
            (PluginLifecycleState.Installed, PluginLifecycleState.Disabled) => true,
            (PluginLifecycleState.Disabled, PluginLifecycleState.Starting) => true,
            (PluginLifecycleState.Starting, PluginLifecycleState.Running or PluginLifecycleState.Faulted) => true,
            (PluginLifecycleState.Running, PluginLifecycleState.Stopping or PluginLifecycleState.Faulted) => true,
            (PluginLifecycleState.Stopping, PluginLifecycleState.Disabled or PluginLifecycleState.DisableFailed or PluginLifecycleState.RestartRequired) => true,
            (PluginLifecycleState.DisableFailed, PluginLifecycleState.RestartRequired) => true,
            (PluginLifecycleState.Faulted, PluginLifecycleState.Quarantined or PluginLifecycleState.RestartRequired) => true,
            (PluginLifecycleState.Quarantined, PluginLifecycleState.Disabled) => true,
            (PluginLifecycleState.RestartRequired, PluginLifecycleState.Disabled) => true,
            _ => false
        };
    }

    public static void EnsureTransition(PluginLifecycleState from, PluginLifecycleState to)
    {
        if (!CanTransition(from, to))
        {
            throw new PluginLifecycleTransitionException(from, to);
        }
    }
}
