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
        Assert.Equal(4, document.RootElement.GetProperty("SchemaVersion").GetInt32());
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
    public void LegacyCornerRadiusValuesAreClampedToTheNewSafeRange()
    {
        using var fixture = new SettingsFixture("{\"CornerRadius\":24}");

        var settings = new HostSettingsService(fixture.SettingsPath);

        Assert.Equal(20, settings.CornerRadius);

        settings.SetAppearanceOption(cornerRadius: 8);

        Assert.Equal(12, settings.CornerRadius);
    }

    [Fact]
    public void AppearanceAndCardPreferencesRoundTrip()
    {
        using var fixture = new SettingsFixture();
        var settings = new HostSettingsService(fixture.SettingsPath);

        settings.SetTheme("ember");
        settings.SetOverviewTitle("我的工具空间");
        settings.SetOverviewHeroTitle("工具状态\n看得见。");
        settings.SetOverviewHealthTitle("没有待解决的问题。");
        settings.SetTitleBarCenterText("ToolBox 工作台");
        settings.SetDefaultPluginCardSize("featured");
        settings.SetPluginCardSize("com.example.plugin", "compact");
        settings.SetAppearanceOption(dynamicGlow: false, reduceMotion: true, transparency: false, cornerRadius: 20, backgroundBrightness: 115);
        settings.SetPluginManagementOption(confirmEnable: true, confirmUninstall: false, showDiagnostics: true);

        var reloaded = new HostSettingsService(fixture.SettingsPath);

        Assert.Equal("ember", reloaded.Theme);
        Assert.Equal("我的工具空间", reloaded.OverviewTitle);
        Assert.Equal("工具状态\n看得见。", reloaded.OverviewHeroTitle);
        Assert.Equal("没有待解决的问题。", reloaded.OverviewHealthTitle);
        Assert.Equal("ToolBox 工作台", reloaded.TitleBarCenterText);
        Assert.Equal("featured", reloaded.DefaultPluginCardSize);
        Assert.Equal("compact", reloaded.GetPluginCardSize("com.example.plugin"));
        Assert.False(reloaded.DynamicGlow);
        Assert.True(reloaded.ReduceMotion);
        Assert.False(reloaded.Transparency);
        Assert.Equal(20, reloaded.CornerRadius);
        Assert.Equal(115, reloaded.BackgroundBrightness);
        Assert.True(reloaded.ConfirmEnable);
        Assert.False(reloaded.ConfirmUninstall);
        Assert.True(reloaded.ShowDiagnostics);
    }

    [Fact]
    public void NoOpUpdatesDoNotRaiseChangeNotifications()
    {
        using var fixture = new SettingsFixture();
        var settings = new HostSettingsService(fixture.SettingsPath);
        var changeCount = 0;
        settings.Changed += (_, _) => changeCount++;

        settings.SetTheme("field");
        settings.SetCloseBehavior(CloseBehavior.MinimizeToTray);
        settings.SetAppearanceOption(
            dynamicGlow: true,
            reduceMotion: false,
            transparency: true,
            cornerRadius: 16,
            backgroundBrightness: 100);
        settings.SetPluginManagementOption(
            confirmEnable: false,
            confirmUninstall: true,
            showDiagnostics: false);

        Assert.Equal(0, changeCount);
    }

    [Fact]
    public void ConcurrentPluginPreferenceUpdatesKeepEveryField()
    {
        using var fixture = new SettingsFixture();
        var settings = new HostSettingsService(fixture.SettingsPath);
        var pluginIds = Enumerable.Range(0, 20)
            .Select(index => $"com.example.plugin-{index}")
            .ToArray();

        Parallel.ForEach(pluginIds, pluginId => settings.SetPluginOpened(pluginId, opened: false));

        var reloaded = new HostSettingsService(fixture.SettingsPath);
        foreach (var pluginId in pluginIds)
        {
            Assert.False(reloaded.IsPluginOpened(pluginId));
        }
    }

    [Fact]
    public void ConcurrentAppearanceUpdatesKeepEveryField()
    {
        using var fixture = new SettingsFixture();
        var settings = new HostSettingsService(fixture.SettingsPath);

        Parallel.Invoke(
            () => settings.SetAppearanceOption(dynamicGlow: false),
            () => settings.SetAppearanceOption(reduceMotion: true),
            () => settings.SetAppearanceOption(transparency: false),
            () => settings.SetAppearanceOption(cornerRadius: 20),
            () => settings.SetAppearanceOption(backgroundBrightness: 115));

        var reloaded = new HostSettingsService(fixture.SettingsPath);

        Assert.False(reloaded.DynamicGlow);
        Assert.True(reloaded.ReduceMotion);
        Assert.False(reloaded.Transparency);
        Assert.Equal(20, reloaded.CornerRadius);
        Assert.Equal(115, reloaded.BackgroundBrightness);
    }

    [Fact]
    public void CustomTextTruncationDoesNotSplitSurrogatePairs()
    {
        using var fixture = new SettingsFixture();
        var settings = new HostSettingsService(fixture.SettingsPath);

        settings.SetOverviewHeroTitle(new string('a', 79) + "😀");

        Assert.Equal(79, settings.OverviewHeroTitle!.Length);
        Assert.False(char.IsHighSurrogate(settings.OverviewHeroTitle[^1]));
    }

    [Fact]
    public void TitleBarCenterTextIsSingleLineTrimmedAndUnicodeSafe()
    {
        using var fixture = new SettingsFixture();
        var settings = new HostSettingsService(fixture.SettingsPath);

        settings.SetTitleBarCenterText("  工具\r\n\t管理  ");

        Assert.Equal("工具 管理", settings.TitleBarCenterText);

        settings.SetTitleBarCenterText(new string('a', 31) + "😀");

        Assert.Equal(31, settings.TitleBarCenterText!.Length);
        Assert.False(char.IsHighSurrogate(settings.TitleBarCenterText[^1]));
    }

    [Fact]
    public void EmptyTitleBarCenterTextFallsBackToDefaultAndResetClearsIt()
    {
        using var fixture = new SettingsFixture();
        var settings = new HostSettingsService(fixture.SettingsPath);
        settings.SetTitleBarCenterText("自定义标题");

        settings.SetTitleBarCenterText(" \r\n ");

        Assert.Null(settings.TitleBarCenterText);
        settings.SetTitleBarCenterText("再次设置");
        settings.ResetAppearance();
        Assert.Null(settings.TitleBarCenterText);
    }

    [Fact]
    public void SuccessfulWriteLeavesNoTemporarySettingsFiles()
    {
        using var fixture = new SettingsFixture();
        var settings = new HostSettingsService(fixture.SettingsPath);

        settings.SetCloseBehavior(CloseBehavior.Exit);

        Assert.Empty(Directory.EnumerateFiles(fixture.DirectoryPath, ".*.tmp"));
    }

    [Fact]
    public void FailedWriteCleansUpItsUniqueTemporaryFile()
    {
        using var fixture = new SettingsFixture();
        var settingsPath = Path.Combine(fixture.DirectoryPath, "settings-directory");
        Directory.CreateDirectory(settingsPath);
        var settings = new HostSettingsService(settingsPath);

        settings.SetCloseBehavior(CloseBehavior.Exit);

        Assert.Empty(Directory.EnumerateFiles(fixture.DirectoryPath, ".*.tmp"));
        Assert.Equal(CloseBehavior.Exit, settings.CloseBehavior);
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

        public string DirectoryPath => _directory;

        public void Dispose()
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
    }
}
