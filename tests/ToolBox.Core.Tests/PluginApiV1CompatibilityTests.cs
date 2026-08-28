using System.Reflection;
using System.Text.Json.Serialization;
using ToolBox.PluginSdk;
using Xunit;

namespace ToolBox.Core.Tests;

public sealed class PluginApiV1CompatibilityTests
{
    private static readonly string[] FrozenStableTypeNames =
    [
        "ToolBox.PluginSdk.IPlugin",
        "ToolBox.PluginSdk.IPluginContext",
        "ToolBox.PluginSdk.IPluginLifetimeScope",
        "ToolBox.PluginSdk.IResourceLease",
        "ToolBox.PluginSdk.IResourceManager",
        "ToolBox.PluginSdk.IServiceBroker",
        "ToolBox.PluginSdk.IServiceLease`1",
        "ToolBox.PluginSdk.PluginContract",
        "ToolBox.PluginSdk.PluginExecutionMode",
        "ToolBox.PluginSdk.PluginLifecycle",
        "ToolBox.PluginSdk.PluginLifecycleState",
        "ToolBox.PluginSdk.PluginLifecycleTransitionException",
        "ToolBox.PluginSdk.PluginManifest",
        "ToolBox.PluginSdk.PluginManifestParser",
        "ToolBox.PluginSdk.PluginManifestParserOptions",
        "ToolBox.PluginSdk.PluginManifestValidationError",
        "ToolBox.PluginSdk.PluginManifestValidationException",
        "ToolBox.PluginSdk.PluginPlatform",
        "ToolBox.PluginSdk.PluginRuntime",
        "ToolBox.PluginSdk.PluginState",
        "ToolBox.PluginSdk.ResourceAccessMode",
        "ToolBox.PluginSdk.ResourceConflictException",
        "ToolBox.PluginSdk.ResourceKey"
    ];

    [Fact]
    public void StableExportedTypeSetMatchesV1Baseline()
    {
        var actual = typeof(IPlugin).Assembly
            .GetExportedTypes()
            .Where(type => string.Equals(type.Namespace, "ToolBox.PluginSdk", StringComparison.Ordinal))
            .Select(type => type.FullName!)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(FrozenStableTypeNames, actual);
    }

    [Fact]
    public void ProductContractsAreNotExportedByTheSdk()
    {
        var actual = typeof(IPlugin).Assembly
            .GetExportedTypes()
            .Where(type => string.Equals(type.Namespace, "ToolBox.PluginSdk.Experimental", StringComparison.Ordinal))
            .Select(type => type.FullName!)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(actual);
    }

    [Fact]
    public void V1ConstantsAndEnumNumbersAreFrozen()
    {
        Assert.Equal(1, PluginContract.PluginApiMajor);
        Assert.Equal(1, PluginContract.ManifestFormatVersion);
        Assert.Equal("windows", PluginContract.SupportedOs);
        Assert.Equal("x64", PluginContract.SupportedArchitecture);

        Assert.Equal(0, (int)PluginExecutionMode.InProcess);
        Assert.Equal(1, (int)PluginExecutionMode.OutOfProcess);
        Assert.Equal(0, (int)ResourceAccessMode.Shared);
        Assert.Equal(1, (int)ResourceAccessMode.Exclusive);

        Assert.Equal(0, (int)PluginLifecycleState.NotInstalled);
        Assert.Equal(1, (int)PluginLifecycleState.Installed);
        Assert.Equal(2, (int)PluginLifecycleState.Disabled);
        Assert.Equal(3, (int)PluginLifecycleState.Starting);
        Assert.Equal(4, (int)PluginLifecycleState.Running);
        Assert.Equal(5, (int)PluginLifecycleState.Stopping);
        Assert.Equal(6, (int)PluginLifecycleState.DisableFailed);
        Assert.Equal(7, (int)PluginLifecycleState.RestartRequired);
        Assert.Equal(8, (int)PluginLifecycleState.Faulted);
        Assert.Equal(9, (int)PluginLifecycleState.Quarantined);
    }

    [Fact]
    public void StableInterfaceMemberShapesRemainCompatible()
    {
        AssertProperty(typeof(IPlugin), "Id", typeof(string));
        AssertMethod(
            typeof(IPlugin),
            nameof(IPlugin.StartAsync),
            typeof(ValueTask),
            typeof(IPluginContext),
            typeof(CancellationToken));
        AssertMethod(
            typeof(IPlugin),
            nameof(IPlugin.StopAsync),
            typeof(ValueTask),
            typeof(CancellationToken));
        Assert.Contains(typeof(IAsyncDisposable), typeof(IPlugin).GetInterfaces());

        AssertProperty(typeof(IPluginContext), nameof(IPluginContext.PluginId), typeof(string));
        AssertProperty(
            typeof(IPluginContext),
            nameof(IPluginContext.LifetimeToken),
            typeof(CancellationToken));
        AssertProperty(
            typeof(IPluginContext),
            nameof(IPluginContext.LifetimeScope),
            typeof(IPluginLifetimeScope));
        AssertProperty(
            typeof(IPluginContext),
            nameof(IPluginContext.Resources),
            typeof(IResourceManager));
        AssertProperty(
            typeof(IPluginContext),
            nameof(IPluginContext.Services),
            typeof(IServiceBroker));

        AssertProperty(
            typeof(IPluginLifetimeScope),
            nameof(IPluginLifetimeScope.LifetimeToken),
            typeof(CancellationToken));
        AssertProperty(
            typeof(IPluginLifetimeScope),
            nameof(IPluginLifetimeScope.IsStopping),
            typeof(bool));
        AssertMethod(
            typeof(IPluginLifetimeScope),
            nameof(IPluginLifetimeScope.Track),
            typeof(void),
            typeof(Task));
        AssertMethod(
            typeof(IPluginLifetimeScope),
            nameof(IPluginLifetimeScope.Register),
            typeof(IDisposable),
            typeof(IDisposable));
        AssertMethod(
            typeof(IPluginLifetimeScope),
            nameof(IPluginLifetimeScope.Register),
            typeof(IDisposable),
            typeof(IAsyncDisposable));
        AssertMethod(
            typeof(IPluginLifetimeScope),
            nameof(IPluginLifetimeScope.Register),
            typeof(IDisposable),
            typeof(Func<CancellationToken, ValueTask>));

        AssertMethod(
            typeof(IResourceManager),
            nameof(IResourceManager.Acquire),
            typeof(IResourceLease),
            typeof(ResourceKey),
            typeof(ResourceAccessMode));
        AssertProperty(typeof(IResourceLease), nameof(IResourceLease.Key), typeof(ResourceKey));
        AssertProperty(
            typeof(IResourceLease),
            nameof(IResourceLease.AccessMode),
            typeof(ResourceAccessMode));
        AssertProperty(
            typeof(IResourceLease),
            nameof(IResourceLease.OwnerPluginId),
            typeof(string));
        AssertProperty(typeof(IResourceLease), nameof(IResourceLease.IsReleased), typeof(bool));
        Assert.Contains(typeof(IDisposable), typeof(IResourceLease).GetInterfaces());

        var acquireService = typeof(IServiceBroker)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Single(method => method.Name == nameof(IServiceBroker.AcquireAsync));
        Assert.True(acquireService.IsGenericMethodDefinition);
        var serviceTypeParameter = Assert.Single(acquireService.GetGenericArguments());
        Assert.Equal(
            GenericParameterAttributes.ReferenceTypeConstraint,
            serviceTypeParameter.GenericParameterAttributes
                & GenericParameterAttributes.ReferenceTypeConstraint);
        Assert.Equal(typeof(string), acquireService.GetParameters()[0].ParameterType);
        Assert.Equal(typeof(CancellationToken), acquireService.GetParameters()[1].ParameterType);
        Assert.Equal(typeof(ValueTask<>), acquireService.ReturnType.GetGenericTypeDefinition());
        var serviceLeaseType = acquireService.ReturnType.GetGenericArguments()[0];
        Assert.Equal(typeof(IServiceLease<>), serviceLeaseType.GetGenericTypeDefinition());
        Assert.Same(serviceTypeParameter, serviceLeaseType.GetGenericArguments()[0]);

        var serviceLeaseTypeParameter = Assert.Single(typeof(IServiceLease<>).GetGenericArguments());
        Assert.True(
            serviceLeaseTypeParameter.GenericParameterAttributes
                .HasFlag(GenericParameterAttributes.Covariant));
        AssertProperty(typeof(IServiceLease<>), nameof(IServiceLease<object>.ServiceKey), typeof(string));
        AssertProperty(typeof(IServiceLease<>), nameof(IServiceLease<object>.OwnerPluginId), typeof(string));
        AssertProperty(typeof(IServiceLease<>), nameof(IServiceLease<object>.IsReleased), typeof(bool));
        Assert.Equal(serviceLeaseTypeParameter, typeof(IServiceLease<>).GetProperty("Service")!.PropertyType);
        Assert.Contains(typeof(IDisposable), typeof(IServiceLease<>).GetInterfaces());
        Assert.Contains(typeof(IAsyncDisposable), typeof(IServiceLease<>).GetInterfaces());
    }

    [Fact]
    public void ManifestJsonNamesAndCompatibilityCodesRemainStable()
    {
        AssertJsonName(typeof(PluginManifest), nameof(PluginManifest.FormatVersion), "formatVersion");
        AssertJsonName(typeof(PluginManifest), nameof(PluginManifest.Id), "id");
        AssertJsonName(typeof(PluginManifest), nameof(PluginManifest.Name), "name");
        AssertJsonName(typeof(PluginManifest), nameof(PluginManifest.Version), "version");
        AssertJsonName(typeof(PluginManifest), nameof(PluginManifest.PluginApiMajor), "pluginApiMajor");
        AssertJsonName(typeof(PluginManifest), nameof(PluginManifest.Publisher), "publisher");
        AssertJsonName(typeof(PluginManifest), nameof(PluginManifest.Platform), "platform");
        AssertJsonName(typeof(PluginManifest), nameof(PluginManifest.Runtime), "runtime");
        AssertJsonName(typeof(PluginManifest), nameof(PluginManifest.EntryPoint), "entryPoint");

        var exception = Assert.Throws<PluginManifestValidationException>(() =>
            new PluginManifestParser().Parse("""
            {
              "formatVersion": 1,
              "id": "com.toolbox.compatibility",
              "name": "Compatibility",
              "version": "1.0.0",
              "pluginApiMajor": 2,
              "publisher": "toolbox",
              "platform": { "os": "windows", "arch": "x64" },
              "runtime": {
                "supportedModes": ["inProcess"],
                "preferredMode": "inProcess",
                "background": false
              },
              "entryPoint": "Compatibility.Plugin, Compatibility"
            }
            """));

        var error = Assert.Single(exception.Errors);
        Assert.Equal("PLUGIN_API_MAJOR_UNSUPPORTED", error.Code);
        Assert.Equal("pluginApiMajor", error.Field);
    }

    private static void AssertProperty(Type declaringType, string name, Type propertyType)
    {
        var property = declaringType.GetProperty(
            name,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);

        Assert.NotNull(property);
        Assert.Equal(propertyType, property!.PropertyType);
    }

    private static void AssertMethod(
        Type declaringType,
        string name,
        Type returnType,
        params Type[] parameterTypes)
    {
        var method = declaringType
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
            .Single(candidate => candidate.Name == name
                && candidate.GetParameters()
                    .Select(parameter => parameter.ParameterType)
                    .SequenceEqual(parameterTypes));

        Assert.Equal(returnType, method.ReturnType);
    }

    private static void AssertJsonName(Type declaringType, string propertyName, string jsonName)
    {
        var property = declaringType.GetProperty(propertyName);
        Assert.NotNull(property);
        var attribute = property!.GetCustomAttribute<JsonPropertyNameAttribute>();
        Assert.NotNull(attribute);
        Assert.Equal(jsonName, attribute!.Name);
    }
}
