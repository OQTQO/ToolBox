# ToolBox 第三方插件开发

本教程描述不修改 ToolBox Host 源码的插件开发路径。

## 0. 版本和开发环境

当前开发版基线为 ToolBox `0.6.0`、Plugin API major `1`、Manifest format `2`、.NET `10` 和 Windows `x64`。插件必须使用与目标 Host 匹配的 SDK/DevKit；不要把 Host、Core 或 Worker 的项目引用复制进插件。

开始前准备：

- Windows x64 和 .NET 10 SDK；
- 与目标 Host 相同版本的 `ToolBox-PluginDevKit-<version>.zip`；
- PowerShell 5.1 或 PowerShell 7；
- 插件自己的 GitHub 仓库，不要把发布私钥提交到仓库。

DevKit 包含本地 `sdk` NuGet 源、`tools` 打包脚本、契约文档和 `samples/HelloPlugin` 示例。

## 1. 创建项目

插件只引用 SDK。SDK 由 ToolBox GitHub Release 的 `ToolBox-PluginDevKit` 提供，默认通过本地 NuGet feed 恢复，不依赖 NuGet.org 上的未发布包。

最简单的起点是复制 DevKit 中的 `samples/HelloPlugin`，再修改命名空间、Plugin ID 和 Manifest。也可以新建一个类库：

```powershell
dotnet new classlib --framework net10.0 --name MyPlugin
Set-Location .\MyPlugin
Copy-Item ..\toolbox-devkit\NuGet.config .\NuGet.config
```

项目文件至少需要以下内容：

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <RootNamespace>Example.MyPlugin</RootNamespace>
    <AssemblyName>MyPlugin</AssemblyName>
    <Version>1.0.0</Version>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="ToolBox.PluginSdk" Version="0.6.0" />
    <None Update="manifest.json"
          CopyToOutputDirectory="PreserveNewest"
          CopyToPublishDirectory="PreserveNewest" />
  </ItemGroup>
</Project>
```

`NuGet.config` 中的本地源必须指向 DevKit 的 `sdk` 目录；如果项目位于 DevKit 外部，使用绝对路径或正确的相对路径：

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="toolbox-local-sdk" value="..\toolbox-devkit\sdk" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
  </packageSources>
</configuration>
```

恢复和构建：

```powershell
dotnet restore --configfile .\NuGet.config
dotnet build --configuration Release --no-restore
```

不要引用 `ToolBox.Host`、`ToolBox.Core` 或 `ToolBox.PluginWorker`。SDK 的公共入口是 `ToolBox.PluginSdk`。

## 2. 实现 IPlugin

最小插件需要实现 `Id`、`StartAsync`、`StopAsync` 和 `DisposeAsync`：

```csharp
using ToolBox.PluginSdk;

public sealed class MyPlugin : IPlugin
{
    public string Id => "com.example.my-plugin";

    public ValueTask StartAsync(IPluginContext context, CancellationToken cancellationToken)
    {
        context.LifetimeScope.Track(RunAsync(context.LifetimeToken));
        return ValueTask.CompletedTask;
    }

    public ValueTask StopAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static async Task RunAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(30), token);
        }
    }
}
```

长任务必须绑定 `context.LifetimeToken`，资源必须通过 `context.LifetimeScope` 注册或追踪。插件不能假设自己运行在 Host 进程或拥有 WPF 页面。

## 3. 提供可操作界面（可选）

只实现 `IPlugin` 的插件可以作为纯后台插件运行。若插件需要在 ToolBox 中显示按钮、状态数据或键鼠输入区域，额外实现 `IPluginUiProvider`：

```csharp
public sealed class MyPlugin : IPlugin, IPluginUiProvider
{
    public PluginUiSnapshot GetSnapshot() => new(
        "插件当前状态",
        [new PluginUiValue("计数", "0")],
        [new PluginUiAction("run", "执行一次")],
        null);

    public ValueTask<PluginUiSnapshot> ExecuteAsync(
        string actionId,
        string? argument,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // 根据 actionId 执行插件自己的功能，然后返回最新快照。
        return ValueTask.FromResult(GetSnapshot());
    }

    public ValueTask<PluginUiSnapshot> HandleInputAsync(
        PluginInputEvent input,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // 如果没有 InputSurface，可以直接返回当前快照。
        return ValueTask.FromResult(GetSnapshot());
    }
}
```

`PluginUiAction` 会被 Host 渲染成按钮，`PluginUiValue` 会被渲染成状态项。按钮的 `Argument` 可用于传递设备 ID 等插件数据。需要键鼠输入时，在 `PluginUiSnapshot.InputSurface` 返回 `PluginInputSurface`，再在 `HandleInputAsync` 中处理 `KeyDown`、`KeyUp`、`MouseDown` 和 `MouseUp`。这些都是 SDK 数据契约，不需要引用 WPF，也不需要修改 ToolBox Host。

插件操作在独立 Worker 中执行。每次操作完成后插件返回新的 `PluginUiSnapshot`，Host 据此刷新通用工作区。

### 3.1 使用通用控件、状态和对话框

新 UI 能力仍然是数据，不需要创建任何 WPF、XAML 或 HTML：

```csharp
public PluginUiSnapshot GetSnapshot()
{
    return new PluginUiSnapshot("就绪", [], [], null)
    {
        Elements =
        [
            new PluginUiElement
            {
                Id = "device",
                Kind = PluginUiElementKind.Select,
                Label = "设备",
                ActionId = "select-device",
                Command = PluginUiCommand.Connect,
                Options = [
                    new PluginUiOption("speaker", "客厅音箱"),
                    new PluginUiOption("phone", "我的手机")
                ]
            },
            new PluginUiElement
            {
                Id = "scan",
                Kind = PluginUiElementKind.Action,
                ActionId = "scan",
                Command = PluginUiCommand.Refresh,
                Style = PluginUiActionStyle.Primary
            },
            new PluginUiElement
            {
                Id = "automatic",
                Kind = PluginUiElementKind.Toggle,
                Label = "自动连接",
                ActionId = "automatic",
                Value = "false"
            }
        ],
        Status = new PluginUiStatus
        {
            Kind = PluginUiStatusKind.Information,
            Message = "已准备好"
        }
    };
}
```

选择设备时 Host 会调用 `ExecuteAsync("select-device", "speaker", ...)`；多选控件的参数是 JSON 数组，例如 `["speaker","phone"]`；开关和复选框的参数是 `true` 或 `false`。数字框和滑块使用不变文化格式，例如 `1.5`，不依赖 Windows 当前区域设置。

`PluginUiUpdateMode` 可将控件设为立即提交或确认后提交；默认下拉、多选、开关、复选框、单选组和滑块立即提交，文本框和数字框在失焦或 Enter 时提交。没有 `ActionId` 的交互控件不会执行调用。

扫描、连接和导入等耗时操作可以同时实现 `IPluginUiUpdateSource`，在后台任务中触发 `SnapshotUpdated` 推送状态或进度。Worker 事件名称为 `ui.updated`；Host 会在操作页顶部显示状态、确定/不确定进度和可选的取消动作。

对话框只提供 `PluginUiDialog` 的标题、消息、类型、默认动作和取消动作。Host 负责创建模态窗口，关闭窗口或按 Esc 时执行取消动作；相同对话框 ID 不会重复弹出。

## 4. 编写 Manifest

在项目根目录放置 `manifest.json`，并在项目文件中设置复制到输出目录。`entryPoint` 指向实现类型和程序集；完整字段见 [Manifest v2](plugin-manifest.md)。第三方动态插件必须声明并支持 `outOfProcess`，并从平台能力目录选择至少一个能力 ID。旧 Manifest v1 不会被安装。

最小结构如下：

```json
{
  "formatVersion": 2,
  "id": "com.example.my-plugin",
  "name": "我的插件",
  "version": "1.0.0",
  "pluginApiMajor": 1,
  "publisher": "example.com",
  "platform": { "os": "windows", "arch": "x64" },
  "runtime": {
    "supportedModes": ["outOfProcess"],
    "preferredMode": "outOfProcess",
    "background": false
  },
  "capabilities": [{
    "id": "host.background.execution",
    "required": true,
    "reason": "插件需要在启用期间运行后台任务。"
  }],
  "entryPoint": "Example.MyPlugin.MyPlugin, MyPlugin"
}
```

`id` 必须稳定且不能含路径分隔符；`version` 必须与项目版本和打包参数一致；`publisher` 必须与签名证书对应的发布者身份一致。能力 ID 不是 Windows 权限沙箱，插件仍需自行遵守最小权限和数据安全原则。

当前安装策略支持本地和受信任发布者提交的插件。安装后会显示在通用工作区，用户可以在状态卡片中启用、停止或禁用它；如果实现了 `IPluginUiProvider`，启用后还会显示插件自己声明的通用操作入口。`background` 目前只用于描述和诊断。

## 5. 发布和打包

先构建插件，再运行通用脚本。脚本会把插件运行文件放到 `.tpk` 的 `runtime/`，生成根目录 `manifest.json`、`package.json`、`signature.json`，并验证哈希和签名：

```powershell
dotnet build --configuration Release
pwsh -File ..\toolbox-devkit\tools\New-PluginPackage.ps1 `
  -RuntimeDirectory .\bin\Release\net10.0 `
  -ManifestPath .\manifest.json `
  -Version 1.0.0 `
  -OutputDirectory .\artifacts `
  -SigningCertificatePath .\publisher.cer `
  -SigningPrivateKeyPath .\publisher.pk8
```

脚本生成根目录 `manifest.json`、`package.json`、`signature.json` 和 `runtime/` 文件，计算 SHA-256，并用 PKCS#8 RSA 私钥签署 package metadata。Host 安装器会继续执行路径、大小、压缩比、Manifest、能力、API、平台、哈希、签名、发布者信任和事务校验。私钥只能保存在受保护的本地/CI secret 中，不能提交到仓库。

首次开发可使用本地验收证书测试流程，但该证书只适用于本机测试，不能作为正式发布身份。正式包必须使用插件发布者自己的证书，并在发布说明中记录证书指纹。输出包默认是 `artifacts\<plugin-id>-<version>.tpk`；如果文件已经存在，必须显式传入 `-Overwrite` 才会覆盖。

安装验证建议：

1. 在 ToolBox 设置页选择本地 `.tpk` 安装；
2. 确认插件初始状态为停用，再手动启用；
3. 检查 Worker 启动、UI 快照、按钮参数、停止和卸载；
4. 确认卸载运行文件后插件数据仍按预期保留；
5. 查看 Host 日志，确认没有 `WORKER_MESSAGE_TOO_LARGE`、签名、Manifest 或取消超时错误。

## 6. 在 GitHub 协作

官方及第三方插件放在 [ToolBox-Plugins](https://github.com/OQTQO/ToolBox-Plugins)。新增插件时，在 `plugins/<PluginName>/` 下建立独立项目和 README，提交 Pull Request。插件仓库的 GitHub Actions 会编译并生成 `.tpk`，用户从插件仓库的 Release 下载后在 ToolBox 中安装。

推荐的插件仓库结构：

```text
plugins/MyPlugin/
├─ MyPlugin.csproj
├─ manifest.json
├─ src/                        插件实现
├─ README.md                   功能、权限、版本和验收说明
└─ tests/                      不依赖 Host 类型的单元/契约测试
```

Pull Request 至少应说明：支持的 ToolBox/SDK 版本、Plugin ID、能力声明及理由、签名发布者、运行模式、数据目录、手动验收步骤和已知限制。普通插件不应修改 ToolBox Host；如果发现公共契约缺少能力，应先在 ToolBox 软件仓库提出平台契约变更。

ToolBox 主仓库只维护 Host、Core、Worker、SDK 和文档，不会因为新增插件而修改 Host 源码。

## 7. 验证

仓库 Sample 的验证命令如下：

```powershell
pwsh -File .\tools\Validate-PluginSamples.ps1
```

验证脚本会先打包 SDK 到本地 feed，再通过 NuGet 构建 HelloPlugin，最后生成 `.tpk`。第三方项目只需要复用这条“SDK → runtime → Manifest → package”路径，不需要将类型加入 Host。

第三方插件项目的最小验证命令是：

```powershell
dotnet restore --configfile .\NuGet.config
dotnet build --configuration Release --no-restore
pwsh -File ..\toolbox-devkit\tools\New-PluginPackage.ps1 `
  -RuntimeDirectory .\bin\Release\net10.0 `
  -ManifestPath .\manifest.json `
  -Version 1.0.0 `
  -OutputDirectory .\artifacts `
  -SigningCertificatePath .\publisher.cer `
  -SigningPrivateKeyPath .\publisher.pk8
```

如果插件没有 UI，验证生命周期和停止/卸载即可；如果实现了 `IPluginUiProvider`，还要验证默认进入“操作”页、标准命令中文标签、控件值编码、进度推送、取消和确认对话框。
