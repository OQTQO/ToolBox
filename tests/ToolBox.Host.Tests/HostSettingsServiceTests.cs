using System.Text.Json;
using ToolBox.Host;
using Xunit;

namespace ToolBox.Host.Tests;

public sealed class HostSettingsServiceTests
{
    [Fact]
    public void LegacyLanguageOnlySettingsMigratesAndUsesSafeDefaults()
    {
        using var fixture = new SettingsFixture("{\"Language\":\"Chinese\"}");

        var settings = new HostSettingsService(fixture.SettingsPath);

        Assert.Equal(AppLanguage.Chinese, settings.Language);
        Assert.Equal(CloseBehavior.MinimizeToTray, settings.CloseBehavior);
        Assert.True(settings.IsPluginOpened("com.toolbox.keyboard-test"));

        settings.SetCloseBehavior(CloseBehavior.Exit);

        using var document = JsonDocument.Parse(File.ReadAllText(fixture.SettingsPath));
        Assert.Equal(2, document.RootElement.GetProperty("SchemaVersion").GetInt32());
        Assert.Equal("Chinese", document.RootElement.GetProperty("Language").GetString());
        Assert.Equal("Exit", document.RootElement.GetProperty("CloseBehavior").GetString());
    }

    [Fact]
    public void PreferencesRoundTripAcrossServiceInstances()
    {
        using var fixture = new SettingsFixture();
        var settings = new HostSettingsService(fixture.SettingsPath);

        settings.SetLanguage(AppLanguage.English);
        settings.SetCloseBehavior(CloseBehavior.Exit);
        settings.SetPluginOpened("com.toolbox.keyboard-test", opened: false);
        settings.SetPluginOpened("com.toolbox.audio-relay", opened: true);

        var reloaded = new HostSettingsService(fixture.SettingsPath);

        Assert.Equal(AppLanguage.English, reloaded.Language);
        Assert.Equal(CloseBehavior.Exit, reloaded.CloseBehavior);
        Assert.False(reloaded.IsPluginOpened("com.toolbox.keyboard-test"));
        Assert.True(reloaded.IsPluginOpened("com.toolbox.audio-relay"));
    }

    [Fact]
    public void RemovingPluginPreferenceRestoresOpenedByDefaultBehavior()
    {
        using var fixture = new SettingsFixture();
        var settings = new HostSettingsService(fixture.SettingsPath);
        settings.SetPluginOpened("com.toolbox.keyboard-test", opened: false);

        settings.RemovePlugin("com.toolbox.keyboard-test");

        Assert.True(settings.IsPluginOpened("com.toolbox.keyboard-test"));
        Assert.True(new HostSettingsService(fixture.SettingsPath)
            .IsPluginOpened("com.toolbox.keyboard-test"));
    }

    [Fact]
    public void MalformedSettingsFallBackWithoutBlockingStartup()
    {
        using var fixture = new SettingsFixture("{ definitely-not-json }");

        var settings = new HostSettingsService(fixture.SettingsPath);

        Assert.Equal(CloseBehavior.MinimizeToTray, settings.CloseBehavior);
        Assert.True(settings.IsPluginOpened("com.toolbox.audio-relay"));
    }

    [Fact]
    public void AppearanceAndCardPreferencesRoundTrip()
    {
        using var fixture = new SettingsFixture();
        var settings = new HostSettingsService(fixture.SettingsPath);

        settings.SetTheme("ember");
        settings.SetOverviewTitle("我的工具空间");
        settings.SetDefaultPluginCardSize("featured");
        settings.SetPluginCardSize("com.example.plugin", "compact");
        settings.SetAppearanceOption(dynamicGlow: false, reduceMotion: true, transparency: false, cornerRadius: 22, backgroundBrightness: 115);
        settings.SetPluginManagementOption(confirmEnable: true, confirmUninstall: false, showDiagnostics: true);

        var reloaded = new HostSettingsService(fixture.SettingsPath);

        Assert.Equal("ember", reloaded.Theme);
        Assert.Equal("我的工具空间", reloaded.OverviewTitle);
        Assert.Equal("featured", reloaded.DefaultPluginCardSize);
        Assert.Equal("compact", reloaded.GetPluginCardSize("com.example.plugin"));
        Assert.False(reloaded.DynamicGlow);
        Assert.True(reloaded.ReduceMotion);
        Assert.False(reloaded.Transparency);
        Assert.Equal(22, reloaded.CornerRadius);
        Assert.Equal(115, reloaded.BackgroundBrightness);
        Assert.True(reloaded.ConfirmEnable);
        Assert.False(reloaded.ConfirmUninstall);
        Assert.True(reloaded.ShowDiagnostics);
    }

    private sealed class SettingsFixture : IDisposable
    {
        private readonly string _directory;

        public SettingsFixture(string? contents = null)
        {
            _directory = Path.Combine(Path.GetTempPath(), "ToolBox.Host.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
            SettingsPath = Path.Combine(_directory, "ui-settings.json");
            if (contents is not null)
            {
                File.WriteAllText(SettingsPath, contents);
            }
        }

        public string SettingsPath { get; }

        public void Dispose()
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
    }
}
