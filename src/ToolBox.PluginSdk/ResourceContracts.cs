namespace ToolBox.PluginSdk;

public enum ResourceAccessMode
{
    Shared,
    Exclusive
}

public readonly record struct ResourceKey
{
    public ResourceKey(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value.Trim();
    }

    public string Value { get; }

    public override string ToString()
    {
        return Value;
    }

    public static implicit operator ResourceKey(string value)
    {
        return new ResourceKey(value);
    }
}

public interface IResourceManager
{
    IResourceLease Acquire(ResourceKey key, ResourceAccessMode accessMode);
}

public interface IResourceLease : IDisposable
{
    ResourceKey Key { get; }

    ResourceAccessMode AccessMode { get; }

    string OwnerPluginId { get; }

    bool IsReleased { get; }
}

public sealed class ResourceConflictException : InvalidOperationException
{
    public ResourceConflictException(
        ResourceKey resourceKey,
        ResourceAccessMode requestedAccessMode,
        IReadOnlyList<string> currentOwners)
        : base(CreateMessage(resourceKey, requestedAccessMode, currentOwners))
    {
        ArgumentNullException.ThrowIfNull(currentOwners);

        ResourceKey = resourceKey;
        RequestedAccessMode = requestedAccessMode;
        CurrentOwners = currentOwners;
    }

    public ResourceKey ResourceKey { get; }

    public ResourceAccessMode RequestedAccessMode { get; }

    public IReadOnlyList<string> CurrentOwners { get; }

    public string CurrentOwner => CurrentOwners.Count == 0
        ? string.Empty
        : CurrentOwners[0];

    private static string CreateMessage(
        ResourceKey resourceKey,
        ResourceAccessMode requestedAccessMode,
        IReadOnlyList<string> currentOwners)
    {
        var owners = currentOwners.Count == 0
            ? "unknown owner"
            : string.Join(", ", currentOwners);

        return $"Resource '{resourceKey}' cannot be acquired as {requestedAccessMode}; current holder(s): {owners}.";
    }
}
