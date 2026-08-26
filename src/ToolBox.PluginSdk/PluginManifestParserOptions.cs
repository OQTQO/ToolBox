namespace ToolBox.PluginSdk;

public sealed record PluginManifestParserOptions
{
    public int SupportedPluginApiMajor { get; init; } = PluginContract.PluginApiMajor;

    public string SupportedOs { get; init; } = PluginContract.SupportedOs;

    public string SupportedArchitecture { get; init; } = PluginContract.SupportedArchitecture;
}
