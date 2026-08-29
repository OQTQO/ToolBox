# v0.2 发布与验证

## SDK 与 Sample

先用 SDK 项目生成本地 NuGet，再构建 HelloPlugin：

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\Validate-PluginSamples.ps1
```

该脚本验证 `ToolBox.PluginSdk` 包、NuGet-only HelloPlugin、Manifest、runtime 文件和 `.tpk`。

ToolBox 的 Release 提供 `ToolBox-v<version>-win-x64.zip`，其中必须同时包含 `ToolBox.Host.exe` 和 `ToolBox.PluginWorker.exe`；用户应解压整个 ZIP 后运行 Host。Release 另外包含 `ToolBox-PluginDevKit-<version>.zip`，供插件仓库和朋友的独立插件项目使用。KeyboardMouse、AudioRelay 等具体插件由 `ToolBox-Plugins` 仓库单独构建和发布。

## 通用插件包

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\New-PluginPackage.ps1 `
  -RuntimeDirectory .\bin\Release\net8.0 `
  -ManifestPath .\manifest.json `
  -Version 1.0.0 `
  -OutputDirectory .\artifacts
```

包使用固定时间戳和稳定条目顺序生成确定性 ZIP，包含根 `manifest.json`、`package.json` 和 `runtime/`。脚本计算 SHA-256，并排除 `ToolBox.PluginSdk.*` 私有文件。已有包必须显式传入 `-Overwrite` 才会覆盖。

## 发布边界

当前包是用户主动选择的本地学习版，不提供签名验证、权限 enforcement、沙箱、商城或自动更新。SHA-256 只表示内容完整性，不证明发布者身份。

主程序的完整验证入口仍是：

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\Invoke-ReleaseValidation.ps1 `
  -Configuration Release `
  -OutputDirectory .\artifacts\release-validation
```

`v0.1.1` 的历史发布资料已归档，不应把旧的产品专用脚本恢复为新的开发入口。
