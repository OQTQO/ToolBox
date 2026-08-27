using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using ToolBox.Core.Resources;
using ToolBox.Core.Services;
using ToolBox.PluginSdk;

namespace ToolBox.Core.Plugins;

public sealed class InProcessPluginRuntime
{
    private readonly PluginDiscovery _discovery;

    public InProcessPluginRuntime(
        PluginDiscovery? discovery = null,
        ResourceManager? resourceManager = null,
        ServiceBroker? serviceBroker = null)
    {
        _discovery = discovery ?? new PluginDiscovery();
        ResourceManager = resourceManager ?? new ResourceManager();
        ServiceBroker = serviceBroker ?? new ServiceBroker();
    }

    public ResourceManager ResourceManager { get; }

    public ServiceBroker ServiceBroker { get; }

    public IReadOnlyList<DiscoveredPlugin> Discover(string pluginsRoot)
    {
        return _discovery.Discover(pluginsRoot);
    }

    public DiscoveredPlugin DiscoverSingle(string pluginDirectory)
    {
        return _discovery.DiscoverSingle(pluginDirectory);
    }

    [SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Keep Load instance-based as the runtime boundary for future host services.")]
    public LoadedInProcessPlugin Load(DiscoveredPlugin discoveredPlugin)
    {
        return Load(discoveredPlugin, PluginExecutionMode.InProcess);
    }

    [SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Keep Load instance-based as the runtime boundary for future host services.")]
    public LoadedInProcessPlugin Load(
        DiscoveredPlugin discoveredPlugin,
        PluginExecutionMode requestedMode)
    {
        ArgumentNullException.ThrowIfNull(discoveredPlugin);

        if (discoveredPlugin.Manifest.Runtime is null
            || !discoveredPlugin.Manifest.Runtime.SupportedModes.Contains(requestedMode))
        {
            throw new PluginLoadException(
                "PLUGIN_RUNTIME_MODE_UNSUPPORTED",
                $"Plugin '{discoveredPlugin.Manifest.Id}' does not support '{requestedMode}' execution.");
        }

        var entryPoint = PluginEntryPoint.Parse(discoveredPlugin.Manifest.EntryPoint);
        var pluginAssemblyPath = ResolvePluginAssembly(discoveredPlugin.DirectoryPath, entryPoint.AssemblyName);
        var loadContext = new PluginAssemblyLoadContext(pluginAssemblyPath);

        try
        {
            var assembly = loadContext.LoadPluginAssembly(pluginAssemblyPath);
            var pluginType = assembly.GetType(entryPoint.TypeName, throwOnError: false, ignoreCase: false);

            if (pluginType is null)
            {
                throw new PluginLoadException(
                    "PLUGIN_ENTRY_TYPE_NOT_FOUND",
                    $"Entry point type '{entryPoint.TypeName}' was not found in '{pluginAssemblyPath}'.");
            }

            if (!typeof(IPlugin).IsAssignableFrom(pluginType))
            {
                throw new PluginLoadException(
                    "PLUGIN_ENTRY_TYPE_INVALID",
                    $"Entry point type '{entryPoint.TypeName}' does not implement IPlugin.");
            }

            if (Activator.CreateInstance(pluginType) is not IPlugin plugin)
            {
                throw new PluginLoadException(
                    "PLUGIN_INSTANCE_CREATE_FAILED",
                    $"Entry point type '{entryPoint.TypeName}' could not be instantiated.");
            }

            if (!string.Equals(plugin.Id, discoveredPlugin.Manifest.Id, StringComparison.Ordinal))
            {
                AwaitDisposeOnFailure(plugin);
                throw new PluginLoadException(
                    "PLUGIN_ID_MISMATCH",
                    $"Plugin instance id '{plugin.Id}' does not match manifest id '{discoveredPlugin.Manifest.Id}'.");
            }

            return new LoadedInProcessPlugin(
                discoveredPlugin,
                loadContext,
                plugin,
                ResourceManager,
                ServiceBroker);
        }
        catch
        {
            loadContext.Unload();
            throw;
        }
    }

    public LoadedInProcessPlugin Load(string pluginDirectory)
    {
        return Load(DiscoverSingle(pluginDirectory));
    }

    private static string ResolvePluginAssembly(string pluginDirectory, string assemblyName)
    {
        var searchDirectories = new[]
        {
            pluginDirectory,
            Path.Combine(pluginDirectory, "runtime")
        };

        var assemblyPath = searchDirectories
            .Where(Directory.Exists)
            .SelectMany(directory => Directory.EnumerateFiles(
                directory,
                "*.dll",
                SearchOption.TopDirectoryOnly))
            .FirstOrDefault(path => string.Equals(
                Path.GetFileNameWithoutExtension(path),
                assemblyName,
                StringComparison.OrdinalIgnoreCase));

        return assemblyPath ?? throw new PluginLoadException(
            "PLUGIN_ENTRY_ASSEMBLY_NOT_FOUND",
            $"Entry point assembly '{assemblyName}' was not found in '{pluginDirectory}'.");
    }

    private static void AwaitDisposeOnFailure(IPlugin plugin)
    {
        try
        {
            plugin.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        catch
        {
            // The original identity mismatch is the actionable load failure.
        }
    }

    private sealed record PluginEntryPoint(string TypeName, string AssemblyName)
    {
        public static PluginEntryPoint Parse(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new PluginLoadException(
                    "PLUGIN_ENTRY_POINT_INVALID",
                    "Plugin entryPoint must be an assembly-qualified type name.");
            }

            var separatorIndex = value.IndexOf(',');

            if (separatorIndex <= 0 || separatorIndex == value.Length - 1)
            {
                throw new PluginLoadException(
                    "PLUGIN_ENTRY_POINT_INVALID",
                    "Plugin entryPoint must use the format 'Namespace.Type, AssemblyName'.");
            }

            var typeName = value[..separatorIndex].Trim();
            var assemblyText = value[(separatorIndex + 1)..].Trim();

            try
            {
                var assemblyName = new AssemblyName(assemblyText).Name;

                if (string.IsNullOrWhiteSpace(assemblyName))
                {
                    throw new ArgumentException("Assembly name is empty.", nameof(value));
                }

                return new PluginEntryPoint(typeName, assemblyName);
            }
            catch (Exception exception) when (exception is ArgumentException or FileLoadException)
            {
                throw new PluginLoadException(
                    "PLUGIN_ENTRY_POINT_INVALID",
                    "Plugin entryPoint contains an invalid assembly name.",
                    exception);
            }
        }
    }
}
