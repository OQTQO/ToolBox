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

安装任意合法 `.tpk` 后，Host 会按包内 Manifest 动态显示插件。用户可以在通用状态卡片中启用、停止或卸载插件；`background` 目前只用于描述和诊断。

## 用户使用

在设置页选择本地 `.tpk`，然后在插件状态卡片中启用、停用或卸载。插件用户数据与运行文件分离，版本回退会保留现有 Config/State 行为。

## 第三方开发

从 [第三方插件开发教程](docs/plugin-development.md) 开始；字段详情见 [Manifest 规范](docs/plugin-manifest.md)，生命周期和 Worker 见 [运行时说明](docs/plugin-runtime.md)。可直接参考 [HelloPlugin](samples/HelloPlugin)，KeyboardMouse 和 AudioRelay 位于独立的 [ToolBox-Plugins](https://github.com/OQTQO/ToolBox-Plugins) 仓库。

仓库会先生成本地 SDK NuGet，再构建 HelloPlugin 并打包：

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\Validate-PluginSamples.ps1
```

通用 `.tpk` 打包入口是 `tools/New-PluginPackage.ps1`。

## 本地构建

```powershell
dotnet restore ToolBox.sln
dotnet build ToolBox.sln --configuration Release
dotnet test ToolBox.sln --configuration Release
```

当前版本是 v0.2 开发方向。ToolBox 的 GitHub Release 同时提供 `ToolBox-PluginDevKit`；历史 v0.1.1 保持不改。维护资料、发布流程和历史上下文位于 [`docs/maintainer/`](docs/maintainer/) 与 [`docs/archive/`](docs/archive/)。

## 当前边界

当前版本不提供签名验证、权限 enforcement、沙箱、插件商城或自动更新。插件通过 GitHub Release 以 `.tpk` 文件分发，用户下载后在本地安装；SHA-256 仅用于包完整性校验，不代表发布者身份。
