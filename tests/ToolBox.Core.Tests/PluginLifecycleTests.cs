using ToolBox.PluginSdk;
using Xunit;

namespace ToolBox.Core.Tests;

public sealed class PluginLifecycleTests
{
    [Fact]
    public void HappyPathLifecycleIsExplicit()
    {
        var manifest = new PluginManifestParser().Parse(ValidManifestJson());
        var initialTime = new DateTimeOffset(2026, 8, 25, 8, 0, 0, TimeSpan.Zero);
        var state = PluginState.CreateInstalled(manifest, initialTime);

        state = state.TransitionTo(PluginLifecycleState.Disabled, initialTime.AddSeconds(1));
        state = state.TransitionTo(PluginLifecycleState.Starting, initialTime.AddSeconds(2));
        state = state.TransitionTo(PluginLifecycleState.Running, initialTime.AddSeconds(3));
        state = state.TransitionTo(PluginLifecycleState.Stopping, initialTime.AddSeconds(4));
        state = state.TransitionTo(PluginLifecycleState.Disabled, initialTime.AddSeconds(5));

        Assert.Equal(PluginLifecycleState.Disabled, state.LifecycleState);
        Assert.Equal(initialTime.AddSeconds(5), state.UpdatedAtUtc);
        Assert.Null(state.LastErrorCode);
    }

    [Fact]
    public void InvalidLifecycleTransitionIsNotHidden()
    {
        var manifest = new PluginManifestParser().Parse(ValidManifestJson());
        var state = PluginState.CreateInstalled(manifest);

        var exception = Assert.Throws<PluginLifecycleTransitionException>(() =>
            state.TransitionTo(PluginLifecycleState.Running));

        Assert.Equal(PluginLifecycleState.Installed, exception.From);
        Assert.Equal(PluginLifecycleState.Running, exception.To);
    }

    [Fact]
    public void FaultedStateKeepsFailureMetadata()
    {
        var manifest = new PluginManifestParser().Parse(ValidManifestJson());
        var state = PluginState.CreateInstalled(manifest)
            .TransitionTo(PluginLifecycleState.Disabled)
            .TransitionTo(PluginLifecycleState.Starting)
            .TransitionTo(PluginLifecycleState.Faulted, errorCode: "PLUGIN_START_FAILED", errorMessage: "fixture failure");

        Assert.Equal(PluginLifecycleState.Faulted, state.LifecycleState);
        Assert.Equal("PLUGIN_START_FAILED", state.LastErrorCode);
        Assert.Equal("fixture failure", state.LastErrorMessage);
    }

    private static string ValidManifestJson()
    {
        return """
        {
          "formatVersion": 2,
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
          "capabilities": [{
            "id": "host.ui.input-events",
            "required": true,
            "reason": "Displays input events sent to the plugin UI."
          }],
          "entryPoint": "ToolBox.KeyboardTest.KeyboardTestPlugin"
        }
        """;
    }
}
