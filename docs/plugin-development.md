# ToolBox 第三方插件开发

本教程描述不修改 ToolBox Host 源码的插件开发路径。

## 1. 创建项目

插件只引用 SDK。SDK 由 ToolBox GitHub Release 的 `ToolBox-PluginDevKit` 提供，朋友之间不需要发布或依赖 NuGet.org：

```xml
<PackageReference Include="ToolBox.PluginSdk" Version="0.4.0" />
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

## 4. 编写 Manifest

在项目输出旁放置 `manifest.json`。`entryPoint` 指向实现类型和程序集；完整字段见 [Manifest v2](plugin-manifest.md)。第三方动态插件必须声明并支持 `outOfProcess`，并从平台能力目录选择至少一个能力 ID。旧 Manifest v1 不会被安装。

当前信任本地和朋友提交的插件。安装后会显示在通用工作区，用户可以在状态卡片中启用、停止或禁用它；如果实现了 `IPluginUiProvider`，启用后还会显示插件自己声明的通用操作入口。`background` 目前只用于描述和诊断。

## 5. 发布和打包

先发布或构建插件，再运行通用脚本：

```powershell
pwsh -File .\tools\New-PluginPackage.ps1 `
  -RuntimeDirectory .\bin\Release\net10.0 `
  -ManifestPath .\manifest.json `
  -Version 1.0.0 `
  -OutputDirectory .\artifacts `
  -SigningCertificatePath .\publisher.cer `
  -SigningPrivateKeyPath .\publisher.pk8
```

脚本生成根目录 `manifest.json`、`package.json`、`signature.json` 和 `runtime/` 文件，计算 SHA-256，并用 PKCS#8 RSA 私钥签署 package metadata。Host 安装器会继续执行路径、大小、压缩比、Manifest、能力、API、平台、哈希、签名、发布者信任和事务校验。私钥只能保存在受保护的本地/CI secret 中，不能提交到仓库。

## 6. 在 GitHub 协作

官方和朋友插件放在 [ToolBox-Plugins](https://github.com/OQTQO/ToolBox-Plugins)。新增插件时，在 `plugins/<PluginName>/` 下建立独立项目和 README，提交 Pull Request。插件仓库的 GitHub Actions 会编译并生成 `.tpk`，用户从插件仓库的 Release 下载后在 ToolBox 中安装。

ToolBox 主仓库只维护 Host、Core、Worker、SDK 和文档，不会因为新增插件而修改 Host 源码。

## 7. 验证

仓库 Sample 的验证命令如下：

```powershell
pwsh -File .\tools\Validate-PluginSamples.ps1
```

验证脚本会先打包 SDK 到本地 feed，再通过 NuGet 构建 HelloPlugin，最后生成 `.tpk`。第三方项目只需要复用这条“SDK → runtime → Manifest → package”路径，不需要将类型加入 Host。
