using System.Reflection;
using System.Runtime.Loader;
using ToolBox.PluginSdk;

namespace ToolBox.Core.Plugins;

internal sealed class PluginAssemblyLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _dependencyResolver;
    private readonly Assembly _sharedPluginSdkAssembly = typeof(IPlugin).Assembly;

    public PluginAssemblyLoadContext(string pluginAssemblyPath)
        : base($"ToolBox.Plugin.{Path.GetFileNameWithoutExtension(pluginAssemblyPath)}", isCollectible: true)
    {
        _dependencyResolver = new AssemblyDependencyResolver(pluginAssemblyPath);
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (string.Equals(
                assemblyName.Name,
                _sharedPluginSdkAssembly.GetName().Name,
                StringComparison.OrdinalIgnoreCase))
        {
            return _sharedPluginSdkAssembly;
        }

        var assemblyPath = _dependencyResolver.ResolveAssemblyToPath(assemblyName);
        return assemblyPath is null ? null : LoadFromAssemblyPath(assemblyPath);
    }
}
