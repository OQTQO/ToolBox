using System.Reflection;
using System.Text.Json.Serialization;
using ToolBox.Core.Plugins.Worker;
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
        "ToolBox.PluginSdk.IPluginUiProvider",
        "ToolBox.PluginSdk.IResourceLease",
        "ToolBox.PluginSdk.IResourceManager",
        "ToolBox.PluginSdk.IServiceBroker",
        "ToolBox.PluginSdk.IServiceLease`1",
        "ToolBox.PluginSdk.PluginCapability",
        "ToolBox.PluginSdk.PluginCapabilityContract",
        "ToolBox.PluginSdk.PluginContract",
        "ToolBox.PluginSdk.PluginExecutionMode",
        "ToolBox.PluginSdk.PluginInputEvent",
        "ToolBox.PluginSdk.PluginInputEventType",
        "ToolBox.PluginSdk.PluginInputSurface",
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
        "ToolBox.PluginSdk.PluginUiAction",
        "ToolBox.PluginSdk.PluginUiSnapshot",
        "ToolBox.PluginSdk.PluginUiValue",
        "ToolBox.PluginSdk.ResourceAccessMode",
        "ToolBox.PluginSdk.ResourceConflictException",
        "ToolBox.PluginSdk.ResourceKey"
    ];

    private static readonly string[] AddedUiTypeNames =
    [
        "ToolBox.PluginSdk.IPluginUiUpdateSource",
        "ToolBox.PluginSdk.PluginUiActionStyle",
        "ToolBox.PluginSdk.PluginUiCommand",
        "ToolBox.PluginSdk.PluginUiDialog",
        "ToolBox.PluginSdk.PluginUiDialogKind",
        "ToolBox.PluginSdk.PluginUiElement",
        "ToolBox.PluginSdk.PluginUiElementKind",
        "ToolBox.PluginSdk.PluginUiMenuItem",
        "ToolBox.PluginSdk.PluginUiOption",
        "ToolBox.PluginSdk.PluginUiProgress",
        "ToolBox.PluginSdk.PluginUiSnapshotUpdatedEventArgs",
        "ToolBox.PluginSdk.PluginUiStatus",
        "ToolBox.PluginSdk.PluginUiStatusKind",
        "ToolBox.PluginSdk.PluginUiUpdateMode"
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

        var expected = FrozenStableTypeNames
            .Concat(AddedUiTypeNames)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expected, actual);
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
    public void SdkUiContractDoesNotReferenceHostMarkupOrWpf()
    {
        var assembly = typeof(IPlugin).Assembly;
        var forbiddenReferences = assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .Where(name => name.Contains("Presentation", StringComparison.OrdinalIgnoreCase)
                || name.Contains("WindowsBase", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Html", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.Empty(forbiddenReferences);
        Assert.DoesNotContain(
            assembly.GetExportedTypes(),
            type => type.FullName?.Contains("System.Windows", StringComparison.Ordinal) == true
                || type.FullName?.Contains("System.Xaml", StringComparison.Ordinal) == true
                || type.FullName?.Contains("System.Web", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void V1ConstantsAndEnumNumbersAreFrozen()
    {
        Assert.Equal(1, PluginContract.PluginApiMajor);
        Assert.Equal(2, PluginContract.ManifestFormatVersion);
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

        AssertMethod(
            typeof(IPluginUiProvider),
            nameof(IPluginUiProvider.GetSnapshot),
            typeof(PluginUiSnapshot));
        AssertMethod(
            typeof(IPluginUiProvider),
            nameof(IPluginUiProvider.ExecuteAsync),
            typeof(ValueTask<PluginUiSnapshot>),
            typeof(string),
            typeof(string),
            typeof(CancellationToken));
        AssertMethod(
            typeof(IPluginUiProvider),
            nameof(IPluginUiProvider.HandleInputAsync),
            typeof(ValueTask<PluginUiSnapshot>),
            typeof(PluginInputEvent),
            typeof(CancellationToken));

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
    public void LegacyPluginUiSnapshotConstructionRemainsCompatible()
    {
        var snapshot = new PluginUiSnapshot(
            "legacy",
            [new PluginUiValue("count", "1")],
            [new PluginUiAction("refresh", "Refresh")],
            null);

        Assert.Equal("legacy", snapshot.StatusMessage);
        Assert.Single(snapshot.Values);
        Assert.Single(snapshot.Actions);
        Assert.Empty(snapshot.Elements);
        Assert.Null(snapshot.Status);
        Assert.Null(snapshot.Dialog);
    }

    [Fact]
    public void LegacyPluginUiJsonDefaultsNewFieldsWithoutChangingThePayload()
    {
        var snapshot = WorkerProtocol.DeserializePayload<PluginUiSnapshot>("""
            {
              "statusMessage": "legacy",
              "values": [{ "label": "count", "value": "1" }],
              "actions": [{ "id": "refresh", "label": "Refresh" }],
              "inputSurface": null
            }
            """);

        Assert.Equal("legacy", snapshot.StatusMessage);
        Assert.Single(snapshot.Values);
        Assert.Single(snapshot.Actions);
        Assert.Empty(snapshot.Elements);
        Assert.Null(snapshot.Status);
        Assert.Null(snapshot.Dialog);
    }

    [Fact]
    public void NewPluginUiContractRoundTripsEveryElementKind()
    {
        var snapshot = new PluginUiSnapshot("ready", [], [], null)
        {
            Elements =
            [
                new PluginUiElement { Id = "value", Kind = PluginUiElementKind.Value, Label = "Value", Value = "x" },
                new PluginUiElement { Id = "action", Kind = PluginUiElementKind.Action, ActionId = "save", Command = PluginUiCommand.Save },
                new PluginUiElement { Id = "menu", Kind = PluginUiElementKind.Menu, MenuItems = [new PluginUiMenuItem { Id = "more", Label = "More", ActionId = "more" }] },
                new PluginUiElement { Id = "select", Kind = PluginUiElementKind.Select, ActionId = "select", Options = [new PluginUiOption("a", "A")] },
                new PluginUiElement { Id = "multi", Kind = PluginUiElementKind.MultiSelect, ActionId = "multi", Values = ["a"] },
                new PluginUiElement { Id = "toggle", Kind = PluginUiElementKind.Toggle, ActionId = "toggle", Value = "true" },
                new PluginUiElement { Id = "check", Kind = PluginUiElementKind.CheckBox, ActionId = "check" },
                new PluginUiElement { Id = "radio", Kind = PluginUiElementKind.RadioGroup, ActionId = "radio" },
                new PluginUiElement { Id = "text", Kind = PluginUiElementKind.TextBox, ActionId = "text" },
                new PluginUiElement { Id = "number", Kind = PluginUiElementKind.NumberBox, ActionId = "number", Minimum = 0, Maximum = 10 },
                new PluginUiElement { Id = "slider", Kind = PluginUiElementKind.Slider, ActionId = "slider", Minimum = 0, Maximum = 100, Step = 5 }
            ],
            Status = new PluginUiStatus
            {
                Kind = PluginUiStatusKind.Progress,
                Message = "Scanning",
                Progress = new PluginUiProgress
                {
                    Value = 5,
                    Maximum = 10,
                    CancelActionId = "cancel"
                }
            },
            Dialog = new PluginUiDialog
            {
                Id = "confirm-1",
                Kind = PluginUiDialogKind.Confirmation,
                Title = "Confirm",
                Message = "Continue?",
                Actions = [new PluginUiAction("yes", "Yes")],
                DefaultActionId = "yes",
                CancelActionId = "no"
            }
        };

        var roundTrip = WorkerProtocol.DeserializePayload<PluginUiSnapshot>(
            WorkerProtocol.SerializePayload(snapshot));

        Assert.Equal(snapshot.StatusMessage, roundTrip.StatusMessage);
        Assert.Equal(snapshot.Elements.Count, roundTrip.Elements.Count);
        Assert.Equal(snapshot.Elements.Select(element => element.Id), roundTrip.Elements.Select(element => element.Id));
        Assert.Equal(snapshot.Status!.Kind, roundTrip.Status!.Kind);
        Assert.Equal(snapshot.Status.Message, roundTrip.Status.Message);
        Assert.Equal(snapshot.Status.Progress!.Value, roundTrip.Status.Progress!.Value);
        Assert.Equal(snapshot.Dialog!.Id, roundTrip.Dialog!.Id);
        Assert.Equal(snapshot.Dialog.Kind, roundTrip.Dialog.Kind);
        Assert.Equal(snapshot.Dialog.Actions.Select(action => action.Id), roundTrip.Dialog.Actions.Select(action => action.Id));
        Assert.Equal(11, roundTrip.Elements.Count);
        Assert.Equal(PluginUiCommand.Save, roundTrip.Elements[1].Command);
        Assert.Equal(PluginUiStatusKind.Progress, roundTrip.Status!.Kind);
        Assert.Equal(PluginUiDialogKind.Confirmation, roundTrip.Dialog!.Kind);
    }

    [Fact]
    public void UnknownPluginUiValuesFallBackWithoutBreakingTheSnapshot()
    {
        var snapshot = WorkerProtocol.DeserializePayload<PluginUiSnapshot>("""
            {
              "statusMessage": "legacy",
              "values": [],
              "actions": [],
              "inputSurface": null,
              "elements": [{
                "id": "future",
                "kind": "futureControl",
                "command": "futureCommand",
                "style": "futureStyle",
                "updateMode": "futureMode"
              }],
              "status": { "kind": "futureStatus", "message": "still readable" }
            }
            """);

        var element = Assert.Single(snapshot.Elements);
        Assert.Equal(PluginUiElementKind.Unknown, element.Kind);
        Assert.Equal(PluginUiCommand.Custom, element.Command);
        Assert.Equal(PluginUiActionStyle.Default, element.Style);
        Assert.Equal(PluginUiUpdateMode.Default, element.UpdateMode);
        Assert.Equal(PluginUiStatusKind.Information, snapshot.Status!.Kind);
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
        AssertJsonName(typeof(PluginManifest), nameof(PluginManifest.Capabilities), "capabilities");
        AssertJsonName(typeof(PluginManifest), nameof(PluginManifest.EntryPoint), "entryPoint");

        var exception = Assert.Throws<PluginManifestValidationException>(() =>
            new PluginManifestParser().Parse("""
            {
              "formatVersion": 2,
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
              "capabilities": [{
                "id": "host.background.execution",
                "required": true,
                "reason": "Runs compatibility checks."
              }],
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
