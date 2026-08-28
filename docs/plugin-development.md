# ToolBox 第三方插件开发

本教程描述不修改 ToolBox Host 源码的插件开发路径。

## 1. 创建项目

插件只引用 SDK。SDK 由 ToolBox GitHub Release 的 `ToolBox-PluginDevKit` 提供，朋友之间不需要发布或依赖 NuGet.org：

```xml
<PackageReference Include="ToolBox.PluginSdk" Version="0.2.0" />
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

## 3. 编写 Manifest

在项目输出旁放置 `manifest.json`。`entryPoint` 指向实现类型和程序集；完整字段见 [Manifest v1](plugin-manifest.md)。第三方动态插件必须声明并支持 `outOfProcess`。

当前信任本地和朋友提交的插件。安装后会显示在通用工作区，用户可以在状态卡片中启用、停止或禁用它；`background` 目前只用于描述和诊断。

## 4. 发布和打包

先发布或构建插件，再运行通用脚本：

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\New-PluginPackage.ps1 `
  -RuntimeDirectory .\bin\Release\net8.0 `
  -ManifestPath .\manifest.json `
  -Version 1.0.0 `
  -OutputDirectory .\artifacts
```

脚本生成根目录 `manifest.json`、`package.json` 和 `runtime/` 文件，计算 SHA-256，使用确定性 ZIP，并排除 `ToolBox.PluginSdk.*` 私有副本。Host 安装器会继续执行路径、大小、压缩比、Manifest、API、平台、哈希和事务校验。

## 5. 在 GitHub 协作

官方和朋友插件放在 [ToolBox-Plugins](https://github.com/OQTQO/ToolBox-Plugins)。新增插件时，在 `plugins/<PluginName>/` 下建立独立项目和 README，提交 Pull Request。插件仓库的 GitHub Actions 会编译并生成 `.tpk`，用户从插件仓库的 Release 下载后在 ToolBox 中安装。

ToolBox 主仓库只维护 Host、Core、Worker、SDK 和文档，不会因为新增插件而修改 Host 源码。

## 6. 验证

仓库 Sample 的验证命令如下：

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\Validate-PluginSamples.ps1
```

验证脚本会先打包 SDK 到本地 feed，再通过 NuGet 构建 HelloPlugin，最后生成 `.tpk`。第三方项目只需要复用这条“SDK → runtime → Manifest → package”路径，不需要将类型加入 Host。
