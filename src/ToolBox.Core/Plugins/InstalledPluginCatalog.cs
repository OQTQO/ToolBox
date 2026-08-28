using ToolBox.Core.Packaging;
using ToolBox.PluginSdk;

namespace ToolBox.Core.Plugins;

public sealed record InstalledPluginDescriptor(
    string PluginId,
    string VersionDirectory,
    DiscoveredPlugin DiscoveredPlugin)
{
    public PluginManifest Manifest => DiscoveredPlugin.Manifest;
}

public sealed record PluginCatalogIssue(
    string PluginId,
    string ErrorCode,
    string Message,
    Exception Exception);

public sealed record InstalledPluginCatalogSnapshot(
    IReadOnlyList<InstalledPluginDescriptor> Plugins,
    IReadOnlyList<PluginCatalogIssue> Issues);

/// <summary>
/// Resolves only committed active versions. A broken plugin root becomes a
/// diagnostic issue and cannot prevent the Host from discovering other plugins.
/// </summary>
public sealed class InstalledPluginCatalog
{
    private readonly PluginPackageInstaller _packageInstaller;
    private readonly PluginDiscovery _discovery;

    public InstalledPluginCatalog(
        PluginPackageInstaller packageInstaller,
        PluginDiscovery? discovery = null)
    {
        _packageInstaller = packageInstaller ?? throw new ArgumentNullException(nameof(packageInstaller));
        _discovery = discovery ?? new PluginDiscovery();
    }

    public InstalledPluginCatalogSnapshot Scan()
    {
        var plugins = new List<InstalledPluginDescriptor>();
        var issues = new List<PluginCatalogIssue>();

        foreach (var pluginId in _packageInstaller.GetInstalledPluginIds())
        {
            try
            {
                var versionDirectory = _packageInstaller.GetActiveVersionDirectory(pluginId);
                if (versionDirectory is null)
                {
                    continue;
                }

                var discovered = _discovery.DiscoverSingle(versionDirectory);
                if (!string.Equals(discovered.Manifest.Id, pluginId, StringComparison.Ordinal))
                {
                    throw new PluginPackageException(
                        "PACKAGE_PLUGIN_ID_MISMATCH",
                        $"The active package manifest id '{discovered.Manifest.Id}' does not match plugin root '{pluginId}'.");
                }

                plugins.Add(new InstalledPluginDescriptor(pluginId, versionDirectory, discovered));
            }
            catch (Exception exception)
            {
                issues.Add(new PluginCatalogIssue(
                    pluginId,
                    exception is PluginPackageException packageException
                        ? packageException.ErrorCode
                        : exception is PluginLoadException loadException
                            ? loadException.ErrorCode
                            : "PLUGIN_DISCOVERY_FAILED",
                    exception.Message,
                    exception));
            }
        }

        return new InstalledPluginCatalogSnapshot(plugins, issues);
    }
}
