using ToolBox.PluginSdk;
using Xunit;

namespace ToolBox.Core.Tests;

public sealed class PluginManifestTests
{
    [Fact]
    public void ValidManifestIsParsed()
    {
        var manifest = new PluginManifestParser().Parse(ValidManifestJson());

        Assert.Equal(PluginContract.ManifestFormatVersion, manifest.FormatVersion);
        Assert.Equal("com.toolbox.keyboard-test", manifest.Id);
        Assert.Equal(PluginContract.PluginApiMajor, manifest.PluginApiMajor);
        Assert.Equal(PluginExecutionMode.InProcess, manifest.Runtime.PreferredMode);
        Assert.Contains(PluginExecutionMode.InProcess, manifest.Runtime.SupportedModes);
    }

    [Fact]
    public void MalformedJsonProducesStructuredValidationError()
    {
        var exception = Assert.Throws<PluginManifestValidationException>(() =>
            new PluginManifestParser().Parse("{ \"formatVersion\": 1,"));

        var error = Assert.Single(exception.Errors);
        Assert.Equal("MANIFEST_JSON_INVALID", error.Code);
        Assert.Equal("$", error.Field);
    }

    [Fact]
    public void IncompatibleApiMajorIsRejected()
    {
        var json = ValidManifestJson().Replace(
            "\"pluginApiMajor\": 1",
            "\"pluginApiMajor\": 2",
            StringComparison.Ordinal);

        var exception = Assert.Throws<PluginManifestValidationException>(() =>
            new PluginManifestParser().Parse(json));

        Assert.Contains(exception.Errors, error => error.Code == "PLUGIN_API_MAJOR_UNSUPPORTED");
    }

    [Fact]
    public void PreferredModeMustBeSupported()
    {
        var json = ValidManifestJson().Replace(
            "\"preferredMode\": \"inProcess\"",
            "\"preferredMode\": \"outOfProcess\"",
            StringComparison.Ordinal);

        var exception = Assert.Throws<PluginManifestValidationException>(() =>
            new PluginManifestParser().Parse(json));

        Assert.Contains(exception.Errors, error => error.Code == "MANIFEST_PREFERRED_MODE_UNSUPPORTED");
    }

    private static string ValidManifestJson()
    {
        return """
        {
          "formatVersion": 1,
          "id": "com.toolbox.keyboard-test",
          "name": "Keyboard Test",
          "version": "0.1.0",
          "pluginApiMajor": 1,
          "publisher": "toolbox.official",
          "platform": {
            "os": "windows",
            "arch": "x64"
          },
          "runtime": {
            "supportedModes": ["inProcess"],
            "preferredMode": "inProcess",
            "background": false
          },
          "entryPoint": "ToolBox.KeyboardTest.KeyboardTestPlugin"
        }
        """;
    }
}
