# ToolBox

ToolBox 是一个面向 Windows 的通用插件外壳。Host 不包含具体产品代码，只负责安装、发现、展示状态并管理插件生命周期。

## 运行链路

```text
第三方项目 → ToolBox.PluginSdk NuGet → manifest.json → .tpk
          → ToolBox 安装器 → 已提交的动态插件目录
          → 通用 WPF 状态卡片 → ToolBox.PluginWorker
          → IPlugin.StartAsync / StopAsync
```

四个边界分别是：

- `ToolBox.PluginSdk`：第三方唯一依赖，稳定 Plugin API v1；
- `ToolBox.Core`：Manifest、安装事务、目录发现、生命周期和 Worker 通信；
- `ToolBox.PluginWorker`：隔离执行插件的进程外 Worker；
- `ToolBox.Host`：与插件类型无关的 WPF 外壳和状态卡片。

安装任意合法 `.tpk` 后，Host 会按包内 Manifest 动态显示插件。用户可以在通用状态卡片中启用、停止或卸载插件；实现 `IPluginUiProvider` 的插件还会显示通用状态、操作按钮或键鼠输入区域。`background` 目前只用于描述和诊断。

## 用户使用

下载 GitHub Release 中的 `ToolBox-vX.Y.Z-win-x64.zip`，解压后运行其中的 `ToolBox.Host.exe`。Host 和 `ToolBox.PluginWorker.exe` 必须保留在同一目录。然后在设置页选择本地 `.tpk`，再在插件状态卡片中启用、停用或卸载。插件用户数据与运行文件分离，版本回退会保留现有 Config/State 行为。

## 第三方开发

从 [第三方插件开发教程](docs/plugin-development.md) 开始；字段详情见 [Manifest 规范](docs/plugin-manifest.md)，生命周期和 Worker 见 [运行时说明](docs/plugin-runtime.md)。可直接参考 [HelloPlugin](samples/HelloPlugin)，KeyboardMouse 和 AudioRelay 位于独立的 [ToolBox-Plugins](https://github.com/OQTQO/ToolBox-Plugins) 仓库。

仓库会先生成本地 SDK NuGet，再构建 HelloPlugin 并打包：

```powershell
pwsh -NoProfile -File .\tools\Validate-PluginSamples.ps1
```

通用 `.tpk` 打包入口是 `tools/New-PluginPackage.ps1`。

### 给插件作者的快速路径

插件作者不需要引用或修改 `ToolBox.Host`。从 ToolBox GitHub Release 下载与目标 Host 相同版本的 `ToolBox-PluginDevKit-<version>.zip`，解压后使用其中的本地 `sdk/`、`tools/` 和 `NuGet.config` 创建插件项目：

```powershell
Expand-Archive .\ToolBox-PluginDevKit-0.6.0.zip -DestinationPath .\toolbox-devkit
Copy-Item .\toolbox-devkit\samples\HelloPlugin .\MyPlugin -Recurse
Set-Location .\MyPlugin
dotnet restore --configfile ..\toolbox-devkit\NuGet.config
dotnet build --configuration Release
```

然后按 [插件开发教程](docs/plugin-development.md) 修改 `IPlugin`、`manifest.json` 和可选的通用 UI，使用 DevKit 中的 `tools\New-PluginPackage.ps1` 生成 `.tpk`。插件包必须由插件发布者签名；不要把 PKCS#8 私钥提交到 GitHub。

版本必须保持一致：插件的 `ToolBox.PluginSdk`、Manifest 的 `pluginApiMajor`、`formatVersion` 和目标 Host/DevKit 版本分别遵循 [Plugin API v1](docs/plugin-api-v1.md) 与 [Manifest v2](docs/plugin-manifest.md)。0.6.0 插件目标为 .NET 10、Windows x64 和 `outOfProcess`。

## 本地构建

```powershell
dotnet restore ToolBox.sln
dotnet build ToolBox.sln --configuration Release
dotnet test ToolBox.sln --configuration Release
```

## 一键 UI 验收

使用长期可复用的验收入口准备并启动隔离 Host：

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File .\tools\Start-UiAcceptance.ps1
```

也可以直接双击 `tools\Start-UiAcceptance.cmd`。

脚本会构建并验证 0.6.0 Host/Worker、生成 HelloPlugin 测试包，将测试包自动安装并启用，然后打开插件详情的“操作”页。验收数据、插件目录和日志默认位于 `artifacts\ui-acceptance`，不会使用正式插件数据。

需要从干净状态重新验收时使用 `-ResetAcceptanceData`；只启动上一次已经准备好的验收资源时使用 `-SkipBuild`。脚本只生成本地验收资产，不提交、推送或发布；双击 `tools\Start-UiAcceptance.cmd` 也会调用同一入口。

当前开发版本与 SDK 版本为 0.6.0，统一基于 .NET 10。ToolBox 的正式 Release 资产仍保持历史版本不变；维护资料、发布流程和历史上下文位于 [`docs/maintainer/`](docs/maintainer/) 与 [`docs/archive/`](docs/archive/)。

## 当前边界

ToolBox 0.6.0 延续 Manifest v2 和 `.tpk` 的 RSA-SHA256 发布者签名校验，并以 TOFU 策略绑定发布者 ID 与证书指纹；Manifest v2 还必须声明平台定义的能力。签名提供包真实性与发布者密钥连续性，但 Worker 仍不是操作系统权限沙箱，当前也不提供插件商城或自动更新。
