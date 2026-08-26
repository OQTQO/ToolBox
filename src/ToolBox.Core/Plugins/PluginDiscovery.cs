using ToolBox.PluginSdk;

namespace ToolBox.Core.Plugins;

public sealed class PluginDiscovery
{
    private readonly PluginManifestParser _manifestParser;

    public PluginDiscovery(PluginManifestParser? manifestParser = null)
    {
        _manifestParser = manifestParser ?? new PluginManifestParser();
    }

    public IReadOnlyList<DiscoveredPlugin> Discover(string pluginsRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginsRoot);

        if (!Directory.Exists(pluginsRoot))
        {
            return Array.Empty<DiscoveredPlugin>();
        }

        var discovered = new List<DiscoveredPlugin>();

        foreach (var directory in Directory.EnumerateDirectories(pluginsRoot).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            var manifestPath = Path.Combine(directory, "manifest.json");

            if (File.Exists(manifestPath))
            {
                discovered.Add(ReadManifest(directory, manifestPath));
            }
        }

        return discovered;
    }

    public DiscoveredPlugin DiscoverSingle(string pluginDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginDirectory);

        var fullDirectory = Path.GetFullPath(pluginDirectory);

        if (!Directory.Exists(fullDirectory))
        {
            throw new DirectoryNotFoundException($"Plugin directory '{fullDirectory}' does not exist.");
        }

        var manifestPath = Path.Combine(fullDirectory, "manifest.json");

        if (!File.Exists(manifestPath))
        {
            throw new PluginLoadException(
                "PLUGIN_MANIFEST_NOT_FOUND",
                $"Plugin manifest was not found at '{manifestPath}'.");
        }

        return ReadManifest(fullDirectory, manifestPath);
    }

    private DiscoveredPlugin ReadManifest(string directory, string manifestPath)
    {
        try
        {
            var json = File.ReadAllText(manifestPath);
            var manifest = _manifestParser.Parse(json);
            return new DiscoveredPlugin(directory, manifestPath, manifest);
        }
        catch (PluginManifestValidationException)
        {
            throw;
        }
        catch (IOException exception)
        {
            throw new PluginLoadException(
                "PLUGIN_MANIFEST_READ_FAILED",
                $"Plugin manifest could not be read from '{manifestPath}'.",
                exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new PluginLoadException(
                "PLUGIN_MANIFEST_READ_DENIED",
                $"Access to plugin manifest '{manifestPath}' was denied.",
                exception);
        }
    }
}
