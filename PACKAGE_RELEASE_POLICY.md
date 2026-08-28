# ToolBox v0.1 Package Release Policy

状态：Keyboard & Mouse Test 个人学习版发布已确认；服务器、官方认证、生产分发与联网更新均不属于当前目标。

## 当前可发布边界

v0.1 的 `.tpk` 是不可信 ZIP 输入，安装器会执行路径、大小、压缩比、Manifest、API、平台、运行时结构和 SHA-256 校验。SHA-256 只证明完整性，不证明发布者身份。

因此当前包适合用于：

- 个人本机学习和演示；
- 自己保存、复制到自己的其他设备；
- 自动化安装、升级、卸载和回退验收。

当前包不提供官方身份认证，也不需要服务器；它只明确标注为个人学习版，不宣称生产级签名、生产级自动更新或生产级自动恢复。

## 生成产品包

先完成 Release 构建，再运行：

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\New-KeyboardMousePackage.ps1 `
  -Configuration Release `
  -Version 0.1.0 `
  -OutputDirectory .\artifacts
```

Phone Audio Relay 使用相同发布边界：

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\New-AudioRelayPackage.ps1 `
  -Configuration Release `
  -Version 0.1.0 `
  -OutputDirectory .\artifacts
```

Keyboard & Mouse 脚本只复制：

- `manifest.json`；
- `runtime/KeyboardTest.dll`；
- `runtime/KeyboardTest.deps.json`；
- 由以上文件生成 SHA-256 列表的 `package.json`。

脚本不会把 `ToolBox.PluginSdk.dll` 私有副本放进包，也不会覆盖已有包，除非显式传入 `-Overwrite`。

Phone Audio Relay 包另外携带 `Microsoft.Windows.SDK.NET.dll` 与 `WinRT.Runtime.dll`，它们是调用 Windows A2DP 接收 API 所需的运行时投影依赖；同样不携带私有 `ToolBox.PluginSdk.dll`。

GitHub 发布流程已写入 `.github/workflows/release.yml`：推送形如 `v0.1.0` 的 Tag 后，GitHub Actions 会先执行构建和测试，再生成自包含 Windows x64 Host、Keyboard & Mouse `.tpk`、Phone Audio Relay `.tpk` 和 SHA-256 清单，并创建同名 GitHub Release。

本地、CI 与 Release 共用同一个发布验证入口：

```powershell
.\tools\Invoke-ReleaseValidation.ps1 `
  -Version 0.1.0 `
  -Configuration Release `
  -OutputDirectory .\artifacts\release-validation
```

脚本会执行干净构建和完整测试，生成四个候选附件，再反向检查准确文件集合、包条目、插件身份、版本、payload 哈希和发布 SHA-256。两个产品包由共同的确定性 ZIP module 写入：条目稳定排序且使用固定时间戳，因此相同源码、版本和 SDK 输入产生逐字节一致的 `.tpk`。

## 版本与恢复规则

- `manifest.json.version` 与 `package.json.pluginVersion` 必须一致。
- 同一个 Plugin ID 的版本并存于 `PluginId/versions/Version`。
- 只有 `state.json.phase = committed` 的版本可被 Host 解析。
- 安装新版本前保留旧版本；卸载当前版本时，安装器选择剩余版本作为激活版本。
- Keyboard & Mouse Test 已验证 `0.1.0 → 0.2.0` 升级、卸载 `0.2.0` 后恢复 `0.1.0` 并重新加载。
- Config/State 快照只在 `automaticRollbackSupported = true` 时作为数据回退依据；Cache 和大量 UserData 不自动复制。

## v0.2 生产真实性契约

生产更新必须先验证官方公钥签名的 Update Manifest，再验证包 SHA-256，最后执行安装：

```text
Official Public Key
↓
Signed Update Manifest
↓
Signature Verification
↓
Package SHA-256
↓
Install
```

如果未来要面向陌生用户或公开分发，才需要落地上述签名链；个人学习版无需实现它。

## 发布前检查

- [ ] Release build 为 0 warning / 0 error。
- [ ] `dotnet test ToolBox.sln --configuration Release` 全部通过。
- [ ] `Invoke-ReleaseValidation.ps1` 在不推标签的情况下完成 dry-run。
- [ ] 连续两次 dry-run 的四个候选附件 SHA-256 完全一致。
- [ ] 包的 Plugin ID、版本、Manifest/API/platform 与目标 Host 匹配。
- [ ] 包内没有私有 `ToolBox.PluginSdk.dll`。
- [ ] Host 冒烟通过，激活状态为 committed，停止后无残留 Worker/ALC。
- [ ] 包的用途已经明确标注为个人学习版；不把完整性校验描述成官方认证。
