namespace ToolBox.Core.Lifetime;

public sealed record PluginShutdownOptions
{
    public PluginShutdownOptions(TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero
            || timeout == System.Threading.Timeout.InfiniteTimeSpan
            || timeout > TimeSpan.FromMilliseconds(int.MaxValue))
        {
            throw new ArgumentOutOfRangeException(
                nameof(timeout),
                "Shutdown timeout must be positive and fit within the .NET cancellation timer range.");
        }

        Timeout = timeout;
    }

    public static PluginShutdownOptions Default { get; } = new(TimeSpan.FromSeconds(5));

    public TimeSpan Timeout { get; }
}
