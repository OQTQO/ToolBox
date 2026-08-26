using ToolBox.PluginSdk;

namespace ToolBox.Core.Plugins;

public sealed record DiscoveredPlugin(
    string DirectoryPath,
    string ManifestPath,
    PluginManifest Manifest);
