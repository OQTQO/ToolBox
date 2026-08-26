using ToolBox.PluginSdk;

namespace ToolBox.Core.Resources;

public sealed class ResourceManager : IDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<ResourceKey, List<ResourceLease>> _leases = new();
    private bool _disposed;

    public ResourceLease Acquire(
        string ownerPluginId,
        ResourceKey key,
        ResourceAccessMode accessMode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerPluginId);
        ValidateAccessMode(accessMode);

        ownerPluginId = ownerPluginId.Trim();

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_leases.TryGetValue(key, out var currentLeases)
                && currentLeases.Count > 0
                && IsConflict(currentLeases, accessMode))
            {
                throw new ResourceConflictException(
                    key,
                    accessMode,
                    currentLeases
                        .Select(lease => lease.OwnerPluginId)
                        .Distinct(StringComparer.Ordinal)
                        .ToArray());
            }

            currentLeases ??= new List<ResourceLease>();
            ResourceLease lease = null!;
            lease = new ResourceLease(
                key,
                accessMode,
                ownerPluginId,
                () => Release(key, lease));
            currentLeases.Add(lease);
            _leases[key] = currentLeases;
            return lease;
        }
    }

    public IResourceManager Bind(
        string ownerPluginId,
        IPluginLifetimeScope? lifetimeScope = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerPluginId);
        return new BoundResourceManager(this, ownerPluginId.Trim(), lifetimeScope);
    }

    public IReadOnlyList<string> GetCurrentOwners(ResourceKey key)
    {
        lock (_gate)
        {
            return _leases.TryGetValue(key, out var currentLeases)
                ? currentLeases
                    .Select(lease => lease.OwnerPluginId)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray()
                : Array.Empty<string>();
        }
    }

    public int GetActiveLeaseCount(ResourceKey key)
    {
        lock (_gate)
        {
            return _leases.TryGetValue(key, out var currentLeases)
                ? currentLeases.Count
                : 0;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _leases.Clear();
        }
    }

    private void Release(ResourceKey key, ResourceLease lease)
    {
        lock (_gate)
        {
            if (!_leases.TryGetValue(key, out var currentLeases))
            {
                return;
            }

            currentLeases.Remove(lease);

            if (currentLeases.Count == 0)
            {
                _leases.Remove(key);
            }
        }
    }

    private static bool IsConflict(
        IReadOnlyCollection<ResourceLease> currentLeases,
        ResourceAccessMode requestedAccessMode)
    {
        return requestedAccessMode == ResourceAccessMode.Exclusive
            || currentLeases.Any(lease => lease.AccessMode == ResourceAccessMode.Exclusive);
    }

    private static void ValidateAccessMode(ResourceAccessMode accessMode)
    {
        if (accessMode is not ResourceAccessMode.Shared and not ResourceAccessMode.Exclusive)
        {
            throw new ArgumentOutOfRangeException(nameof(accessMode), accessMode, "Unknown resource access mode.");
        }
    }

    private sealed class BoundResourceManager : IResourceManager
    {
        private readonly ResourceManager _manager;
        private readonly string _ownerPluginId;
        private readonly IPluginLifetimeScope? _lifetimeScope;

        public BoundResourceManager(
            ResourceManager manager,
            string ownerPluginId,
            IPluginLifetimeScope? lifetimeScope)
        {
            _manager = manager;
            _ownerPluginId = ownerPluginId;
            _lifetimeScope = lifetimeScope;
        }

        public IResourceLease Acquire(ResourceKey key, ResourceAccessMode accessMode)
        {
            var lease = _manager.Acquire(_ownerPluginId, key, accessMode);

            if (_lifetimeScope is null)
            {
                return lease;
            }

            try
            {
                _lifetimeScope.Register(lease);
                return lease;
            }
            catch
            {
                lease.Dispose();
                throw;
            }
        }
    }
}
