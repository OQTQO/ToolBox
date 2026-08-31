using System.Text.Json.Serialization;

namespace ToolBox.PluginSdk;

public sealed record PluginManifest(
    [property: JsonPropertyName("formatVersion")] int FormatVersion,
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("pluginApiMajor")] int PluginApiMajor,
    [property: JsonPropertyName("publisher")] string Publisher,
    [property: JsonPropertyName("platform")] PluginPlatform Platform,
    [property: JsonPropertyName("runtime")] PluginRuntime Runtime,
    [property: JsonPropertyName("capabilities")] PluginCapability[] Capabilities,
    [property: JsonPropertyName("entryPoint")] string EntryPoint);

public sealed record PluginCapability(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("required")] bool? Required,
    [property: JsonPropertyName("reason")] string Reason);

public static class PluginCapabilityContract
{
    public const string BackgroundExecution = "host.background.execution";

    public const string HostUiInputEvents = "host.ui.input-events";

    public const string WindowsBluetoothAudioReceiver = "windows.bluetooth.audio-receiver";

    public static bool IsKnown(string capabilityId)
    {
        return capabilityId is BackgroundExecution
            or HostUiInputEvents
            or WindowsBluetoothAudioReceiver;
    }
}

public sealed record PluginPlatform(
    [property: JsonPropertyName("os")] string Os,
    [property: JsonPropertyName("arch")] string Arch);

public sealed record PluginRuntime(
    [property: JsonPropertyName("supportedModes")] PluginExecutionMode[] SupportedModes,
    [property: JsonPropertyName("preferredMode")] PluginExecutionMode PreferredMode,
    [property: JsonPropertyName("background")] bool Background);
