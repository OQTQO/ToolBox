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

## 本地构建

```powershell
dotnet restore ToolBox.sln
dotnet build ToolBox.sln --configuration Release
dotnet test ToolBox.sln --configuration Release
```

当前平台与 SDK 版本为 0.5.0，统一基于 .NET 10。ToolBox 的 GitHub Release 同时提供对应版本的 `ToolBox-PluginDevKit`；历史 Release 保持不改。维护资料、发布流程和历史上下文位于 [`docs/maintainer/`](docs/maintainer/) 与 [`docs/archive/`](docs/archive/)。

## 当前边界

ToolBox 0.4 强制验证 `.tpk` 的 RSA-SHA256 发布者签名，并以 TOFU 策略绑定发布者 ID 与证书指纹；Manifest v2 还必须声明平台定义的能力。签名提供包真实性与发布者密钥连续性，但 Worker 仍不是操作系统权限沙箱，当前也不提供插件商城或自动更新。
