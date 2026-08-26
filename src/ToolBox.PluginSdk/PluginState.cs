namespace ToolBox.PluginSdk;

public sealed record PluginState(
    string PluginId,
    string PluginVersion,
    PluginLifecycleState LifecycleState,
    DateTimeOffset UpdatedAtUtc,
    string? LastErrorCode = null,
    string? LastErrorMessage = null)
{
    public static PluginState CreateNotInstalled(
        string pluginId,
        string pluginVersion,
        DateTimeOffset? updatedAtUtc = null)
    {
        ValidateIdentity(pluginId, pluginVersion);

        return new PluginState(
            pluginId,
            pluginVersion,
            PluginLifecycleState.NotInstalled,
            updatedAtUtc ?? DateTimeOffset.UtcNow);
    }

    public static PluginState CreateInstalled(
        PluginManifest manifest,
        DateTimeOffset? updatedAtUtc = null)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ValidateIdentity(manifest.Id, manifest.Version);

        return new PluginState(
            manifest.Id,
            manifest.Version,
            PluginLifecycleState.Installed,
            updatedAtUtc ?? DateTimeOffset.UtcNow);
    }

    public PluginState TransitionTo(
        PluginLifecycleState nextState,
        DateTimeOffset? updatedAtUtc = null,
        string? errorCode = null,
        string? errorMessage = null)
    {
        PluginLifecycle.EnsureTransition(LifecycleState, nextState);

        var failureState = nextState is
            PluginLifecycleState.DisableFailed or
            PluginLifecycleState.RestartRequired or
            PluginLifecycleState.Faulted or
            PluginLifecycleState.Quarantined;

        return this with
        {
            LifecycleState = nextState,
            UpdatedAtUtc = updatedAtUtc ?? DateTimeOffset.UtcNow,
            LastErrorCode = failureState ? errorCode : null,
            LastErrorMessage = failureState ? errorMessage : null
        };
    }

    private static void ValidateIdentity(string pluginId, string pluginVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginVersion);
    }
}
