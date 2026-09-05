using System.Windows;
using System.Windows.Controls;
using ToolBox.Core.Diagnostics;
using ToolBox.Core.Packaging;
using ToolBox.Core.Plugins;
using ToolBox.PluginSdk;
using Xunit;

namespace ToolBox.Host.Tests;

public sealed class PluginUiRenderingTests
{
    [Fact]
    public void StandardCommandsUseHostLocalizationAndCommandTargets()
    {
        using var fixture = new Fixture();
        var localization = new LocalizationService(fixture.Settings);

        localization.SetLanguage(AppLanguage.Chinese);
        Assert.Equal(
            "刷新 客厅音箱",
            localization.GetPluginUiCommandLabel(
                PluginUiCommand.Refresh,
                "客厅音箱",
                "插件刷新"));

        localization.SetLanguage(AppLanguage.English);
        Assert.Equal(
            "Refresh Speaker",
            localization.GetPluginUiCommandLabel(
                PluginUiCommand.Refresh,
                "Speaker",
                "Plugin refresh"));
        Assert.Equal(
            "Plugin refresh",
            localization.GetPluginUiCommandLabel(
                PluginUiCommand.Custom,
                null,
                "Plugin refresh"));
    }

    [Fact]
    public void HostEncodesMultiSelectAndInvariantNumberValues()
    {
        using var fixture = new Fixture();
        using var workspace = fixture.CreateWorkspace();

        var multiSelect = new PluginUiElementViewModel(
            workspace,
            new PluginUiElement
            {
                Id = "devices",
                Kind = PluginUiElementKind.MultiSelect,
                ActionId = "devices",
                Options =
                [
                    new PluginUiOption("speaker", "客厅音箱"),
                    new PluginUiOption("phone", "我的手机")
                ],
                Values = ["speaker"]
            });

        Assert.Equal("[\"speaker\"]", multiSelect.BuildArgument());
        multiSelect.Options[1].IsSelected = true;
        Assert.Equal("[\"speaker\",\"phone\"]", multiSelect.BuildArgument());

        var numberBox = new PluginUiElementViewModel(
            workspace,
            new PluginUiElement
            {
                Id = "volume",
                Kind = PluginUiElementKind.NumberBox,
                ActionId = "volume",
                Value = "1.50",
                Minimum = 0,
                Maximum = 2
            });

        Assert.Equal("1.5", numberBox.BuildArgument());
        numberBox.Value = "3.25";
        Assert.Equal("2", numberBox.BuildArgument());
    }

    [Fact]
    public void TemplateSelectorMapsEverySupportedElementKindAndIgnoresUnknownKinds()
    {
        using var fixture = new Fixture();
        using var workspace = fixture.CreateWorkspace();
        var selector = new PluginUiElementTemplateSelector
        {
            ValueTemplate = new DataTemplate(),
            ActionTemplate = new DataTemplate(),
            MenuTemplate = new DataTemplate(),
            SelectControlTemplate = new DataTemplate(),
            MultiSelectTemplate = new DataTemplate(),
            ToggleTemplate = new DataTemplate(),
            CheckBoxTemplate = new DataTemplate(),
            RadioGroupTemplate = new DataTemplate(),
            TextBoxTemplate = new DataTemplate(),
            NumberBoxTemplate = new DataTemplate(),
            SliderTemplate = new DataTemplate()
        };

        var expected = new Dictionary<PluginUiElementKind, DataTemplate>
        {
            [PluginUiElementKind.Value] = selector.ValueTemplate!,
            [PluginUiElementKind.Action] = selector.ActionTemplate!,
            [PluginUiElementKind.Menu] = selector.MenuTemplate!,
            [PluginUiElementKind.Select] = selector.SelectControlTemplate!,
            [PluginUiElementKind.MultiSelect] = selector.MultiSelectTemplate!,
            [PluginUiElementKind.Toggle] = selector.ToggleTemplate!,
            [PluginUiElementKind.CheckBox] = selector.CheckBoxTemplate!,
            [PluginUiElementKind.RadioGroup] = selector.RadioGroupTemplate!,
            [PluginUiElementKind.TextBox] = selector.TextBoxTemplate!,
            [PluginUiElementKind.NumberBox] = selector.NumberBoxTemplate!,
            [PluginUiElementKind.Slider] = selector.SliderTemplate!
        };

        foreach (var pair in expected)
        {
            var element = new PluginUiElementViewModel(
                workspace,
                new PluginUiElement { Id = pair.Key.ToString(), Kind = pair.Key });

            Assert.Same(pair.Value, selector.SelectTemplate(element, null));
        }

        var unknown = new PluginUiElementViewModel(
            workspace,
            new PluginUiElement { Id = "unknown", Kind = PluginUiElementKind.Unknown });
        Assert.Null(selector.SelectTemplate(unknown, null));
        Assert.Equal(
            HostUiState.PluginDetailsTabs.Operations,
            HostUiState.PluginDetailsTabs.GetDefault(hasPluginUi: true));
        Assert.Equal(
            HostUiState.PluginDetailsTabs.Overview,
            HostUiState.PluginDetailsTabs.GetDefault(hasPluginUi: false));
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _root = Path.Combine(
            Path.GetTempPath(),
            "ToolBox.Host.PluginUi",
            Guid.NewGuid().ToString("N"));
        private PluginWorkspaceViewModel? _workspace;
        private PluginPackageInstaller? _installer;
        private StructuredLogger? _logger;

        public Fixture()
        {
            Directory.CreateDirectory(_root);
            Settings = new HostSettingsService(Path.Combine(_root, "settings.json"));
        }

        public HostSettingsService Settings { get; }

        public PluginWorkspaceViewModel CreateWorkspace()
        {
            var pluginsRoot = Path.Combine(_root, "plugins");
            var dataRoot = Path.Combine(_root, "data");
            var logsRoot = Path.Combine(_root, "logs");
            var versionDirectory = Path.Combine(pluginsRoot, "com.example.ui", "versions", "1.0.0");
            Directory.CreateDirectory(versionDirectory);

            var manifest = new PluginManifest(
                2,
                "com.example.ui",
                "UI fixture",
                "1.0.0",
                1,
                "toolbox.tests",
                new PluginPlatform("windows", "x64"),
                new PluginRuntime(
                    [PluginExecutionMode.OutOfProcess],
                    PluginExecutionMode.OutOfProcess,
                    Background: true),
                [new PluginCapability(
                    PluginCapabilityContract.BackgroundExecution,
                    true,
                    "Exercises generic Host UI rendering.")],
                "Example.Plugin, Example");
            var discovered = new DiscoveredPlugin(
                versionDirectory,
                Path.Combine(versionDirectory, "manifest.json"),
                manifest);
            var descriptor = new InstalledPluginDescriptor(
                manifest.Id,
                versionDirectory,
                discovered);
            var installer = _installer = new PluginPackageInstaller(pluginsRoot, dataRoot);
            var logger = _logger = new StructuredLogger(
                new LoggerOptions { DirectoryPath = logsRoot },
                "plugin-ui-tests",
                "0.6.0");
            var runtime = new OutOfProcessPluginRuntime(
                Path.Combine(_root, "ToolBox.PluginWorker.exe"));

            _workspace = new PluginWorkspaceViewModel(
                descriptor,
                installer,
                runtime,
                logger,
                new LocalizationService(Settings),
                Settings,
                ImmediateHostUiDispatcher.Instance);
            return _workspace;
        }

        public void Dispose()
        {
            _workspace?.Dispose();
            _workspace = null;
            _logger?.DisposeAsync().AsTask().GetAwaiter().GetResult();
            _logger = null;
            _installer?.Dispose();
            _installer = null;
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
    }
}
