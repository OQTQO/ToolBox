# v0.6 发布与验证

构建机必须安装 `global.json` 指定的 .NET 10 SDK。CI 使用 `actions/setup-dotnet` 的 `10.0.x` 通道；Release 中的示例签名包必须从受保护 secret 获取稳定证书和 PKCS#8 私钥。

## SDK 与 Sample

先用 SDK 项目生成本地 NuGet，再构建 HelloPlugin：

```powershell
pwsh -File .\tools\Validate-PluginSamples.ps1
```

该脚本验证 `ToolBox.PluginSdk` 包、NuGet-only HelloPlugin、Manifest、runtime 文件和 `.tpk`。

ToolBox 的 Release 提供以下软件资产：

- `ToolBox-v<version>-win-x64.zip`：便携 ZIP，包含 `ToolBox.Host.exe` 和 `ToolBox.PluginWorker.exe`。
- `ToolBox-Setup-v<version>.exe`：Inno Setup 6 安装程序，默认安装到 `%LocalAppData%\Programs\ToolBox`，用户数据保存在安装目录的 `Data` 下。
- `ToolBox-PluginDevKit-<version>.zip`：供插件仓库和独立插件项目使用的 SDK 开发包。

安装程序只包含软件本体，不包含 HelloPlugin。测试插件由 `Start-UiAcceptance.ps1` 在隔离验收目录中自动构建和安装。KeyboardMouse、AudioRelay 等具体插件由 `ToolBox-Plugins` 仓库单独构建和发布。

## 通用插件包

```powershell
pwsh -File .\tools\New-PluginPackage.ps1 `
  -RuntimeDirectory .\bin\Release\net10.0 `
  -ManifestPath .\manifest.json `
  -Version 1.0.0 `
  -OutputDirectory .\artifacts `
  -SigningCertificatePath .\publisher.cer `
  -SigningPrivateKeyPath .\publisher.pk8
```

包使用固定时间戳和稳定条目顺序生成确定性 ZIP，包含根 `manifest.json`、`package.json`、`signature.json` 和 `runtime/`。脚本计算 SHA-256、生成 RSA-SHA256 分离签名，并排除 `ToolBox.PluginSdk.*` 私有文件。已有包必须显式传入 `-Overwrite` 才会覆盖。

## 发布边界

Release 只允许使用受保护的稳定私钥生成签名包。Host 会验证签名并绑定发布者证书指纹；Manifest 能力声明用于审查，但当前 Worker 仍不是权限沙箱，也不提供商城或自动更新。

主程序的完整验证入口仍是：

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\Invoke-ReleaseValidation.ps1 `
  -Configuration Release `
  -OutputDirectory .\artifacts\release-validation
```

本地只构建安装程序时，机器需要安装 Inno Setup 6，并执行：

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\Invoke-InstallerBuild.ps1 `
  -Version 0.6.0 `
  -Configuration Release
```

若已拥有 Host/Worker 的 self-contained 发布目录，可通过 `-HostPublishDirectory` 和 `-WorkerPublishDirectory` 复用已有构建，避免重复发布。安装器验收使用：

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\Invoke-InstallerValidation.ps1 `
  -SetupPath .\artifacts\installer\ToolBox-Setup-v0.6.0.exe
```

`Start-UiAcceptance.ps1` 会显式跳过正式安装器构建，以保持日常 WPF 验收不依赖 Inno Setup；正式 Release Validation 和 CI 会构建并校验安装器。

`v0.1.1` 的历史发布资料已归档，不应把旧的产品专用脚本恢复为新的开发入口。
