namespace ToolBox.PluginSdk;

public sealed class PluginLifecycleTransitionException : InvalidOperationException
{
    public PluginLifecycleTransitionException(PluginLifecycleState from, PluginLifecycleState to)
        : base($"Plugin lifecycle transition '{from}' -> '{to}' is not allowed.")
    {
        From = from;
        To = to;
    }

    public PluginLifecycleState From { get; }

    public PluginLifecycleState To { get; }
}
