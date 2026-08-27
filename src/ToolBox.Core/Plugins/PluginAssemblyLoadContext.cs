using System.Reflection;
using System.Runtime.Loader;
using ToolBox.PluginSdk;

namespace ToolBox.Core.Plugins;

internal sealed class PluginAssemblyLoadContext : AssemblyLoadContext
{
    private const string WinRtRuntimeAssemblyName = "WinRT.Runtime";
    private const string WinRtRuntimePublicKeyToken = "99ea127f02d97709";
    private const string WindowsSdkAssemblyName = "Microsoft.Windows.SDK.NET";
    private const string WindowsSdkPublicKeyToken = "31bf3856ad364e35";
    private static readonly object ProcessSharedAssemblyGate = new();
    private static readonly Dictionary<string, Assembly> ProcessSharedAssemblies =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly AssemblyDependencyResolver _dependencyResolver;
    private readonly Assembly _sharedPluginSdkAssembly = typeof(IPlugin).Assembly;
    private readonly bool _usesProcessSharedWinRt;

    public PluginAssemblyLoadContext(string pluginAssemblyPath)
        : base($"ToolBox.Plugin.{Path.GetFileNameWithoutExtension(pluginAssemblyPath)}", isCollectible: true)
    {
        _dependencyResolver = new AssemblyDependencyResolver(pluginAssemblyPath);
        _usesProcessSharedWinRt =
            _dependencyResolver.ResolveAssemblyToPath(new AssemblyName(WinRtRuntimeAssemblyName)) is not null
            && _dependencyResolver.ResolveAssemblyToPath(new AssemblyName(WindowsSdkAssemblyName)) is not null;
    }

    public Assembly LoadPluginAssembly(string pluginAssemblyPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginAssemblyPath);
        return _usesProcessSharedWinRt
            ? LoadManagedAssemblyWithoutLock(pluginAssemblyPath)
            : LoadFromAssemblyPath(pluginAssemblyPath);
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

        if (string.Equals(
                assemblyName.Name,
                WinRtRuntimeAssemblyName,
                StringComparison.OrdinalIgnoreCase))
        {
            return LoadProcessSharedAssembly(
                assemblyName,
                WinRtRuntimePublicKeyToken);
        }

        if (string.Equals(
                assemblyName.Name,
                WindowsSdkAssemblyName,
                StringComparison.OrdinalIgnoreCase))
        {
            LoadProcessSharedAssembly(
                new AssemblyName(WinRtRuntimeAssemblyName),
                WinRtRuntimePublicKeyToken);
            return LoadProcessSharedAssembly(
                assemblyName,
                WindowsSdkPublicKeyToken);
        }

        var assemblyPath = _dependencyResolver.ResolveAssemblyToPath(assemblyName);
        if (assemblyPath is null)
        {
            return null;
        }

        return _usesProcessSharedWinRt
            ? LoadManagedAssemblyWithoutLock(assemblyPath)
            : LoadFromAssemblyPath(assemblyPath);
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        var libraryPath = _dependencyResolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        return libraryPath is null
            ? IntPtr.Zero
            : LoadUnmanagedDllFromPath(libraryPath);
    }

    private Assembly? LoadProcessSharedAssembly(
        AssemblyName requestedAssemblyName,
        string expectedPublicKeyToken)
    {
        lock (ProcessSharedAssemblyGate)
        {
            var simpleName = requestedAssemblyName.Name
                ?? throw new FileLoadException("A process-shared assembly must have a simple name.");

            if (ProcessSharedAssemblies.TryGetValue(simpleName, out var cachedAssembly))
            {
                EnsureCompatibleProcessSharedAssembly(
                    cachedAssembly.GetName(),
                    requestedAssemblyName,
                    expectedPublicKeyToken);
                return cachedAssembly;
            }

            var defaultAssembly = Default.Assemblies.FirstOrDefault(assembly =>
                string.Equals(
                    assembly.GetName().Name,
                    simpleName,
                    StringComparison.OrdinalIgnoreCase));
            if (defaultAssembly is not null)
            {
                EnsureCompatibleProcessSharedAssembly(
                    defaultAssembly.GetName(),
                    requestedAssemblyName,
                    expectedPublicKeyToken);
                ProcessSharedAssemblies[simpleName] = defaultAssembly;
                return defaultAssembly;
            }

            var assemblyPath = _dependencyResolver.ResolveAssemblyToPath(requestedAssemblyName);
            if (assemblyPath is null)
            {
                return null;
            }

            var candidateName = AssemblyName.GetAssemblyName(assemblyPath);
            EnsureCompatibleProcessSharedAssembly(
                candidateName,
                requestedAssemblyName,
                expectedPublicKeyToken);

            using var assemblyStream = new MemoryStream(File.ReadAllBytes(assemblyPath), writable: false);
            var sharedAssembly = Default.LoadFromStream(assemblyStream);
            ProcessSharedAssemblies[simpleName] = sharedAssembly;
            return sharedAssembly;
        }
    }

    private Assembly LoadManagedAssemblyWithoutLock(string assemblyPath)
    {
        using var assemblyStream = new MemoryStream(File.ReadAllBytes(assemblyPath), writable: false);
        var symbolsPath = Path.ChangeExtension(assemblyPath, ".pdb");
        if (!File.Exists(symbolsPath))
        {
            return LoadFromStream(assemblyStream);
        }

        using var symbolsStream = new MemoryStream(File.ReadAllBytes(symbolsPath), writable: false);
        return LoadFromStream(assemblyStream, symbolsStream);
    }

    private static void EnsureCompatibleProcessSharedAssembly(
        AssemblyName candidate,
        AssemblyName requested,
        string expectedPublicKeyToken)
    {
        var publicKeyToken = Convert.ToHexString(candidate.GetPublicKeyToken() ?? [])
            .ToLowerInvariant();
        if (!string.Equals(
                candidate.Name,
                requested.Name,
                StringComparison.OrdinalIgnoreCase)
            || !string.Equals(
                publicKeyToken,
                expectedPublicKeyToken,
                StringComparison.OrdinalIgnoreCase)
            || candidate.Version is null
            || requested.Version is not null && candidate.Version < requested.Version)
        {
            throw new FileLoadException(
                $"Process-shared assembly '{candidate.FullName}' is not compatible with '{requested.FullName}'.");
        }
    }
}
