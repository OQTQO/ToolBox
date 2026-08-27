# ToolBox

ToolBox 是一个面向 Windows 的插件平台原型。

当前实现已完成 v0.1 原型的 Phase 0–10：

- 可编译的 .NET/WPF 工程与测试基础；
- WPF Host Shell，提供启动状态、Session、Launch Attempt 和事件流；
- 不依赖第三方日志库的结构化 JSONL 日志；
- 异步写入、按大小滚动和保留数量/期限限制；
- 全局异常捕获与基础 Host Diagnostics；
- 稳定的启动、显示、退出流程。
- PluginSdk、生命周期状态、Manifest 校验、InProcess/OutOfProcess Worker 隔离；
- PluginLifetimeScope、统一 Shutdown Deadline、故障状态与 Quarantine；
- Resource Manager 的 Shared/Exclusive Lease 冲突仲裁；
- Service Broker 的懒启动、Lease 复用、引用计数和空闲停止；
- Keyboard & Mouse Test 产品包的真实加载、输入事件、资源占用和卸载验证；
- `.tpk` 安全 ZIP 校验、staging 安装、Manifest/API/哈希校验、版本并存、原子状态和 Config/State 快照；
- BadZipPackage、BadManifestPackage、IncompatibleApiPlugin 及哈希篡改攻击测试；
- Plugin API v1 稳定公共面、接口签名、枚举/常量、Manifest 字段与兼容性规则冻结；
- LegacyPlugin 旧版 SDK 编译兼容性 Fixture 的直接加载与卸载验证；
- Keyboard & Mouse Test 正式产品插件的最小产品化与首轮 hardening 已完成：Host 只读取已提交的激活版本，提供 `.tpk` 安装/更新入口，支持局部输入、设置、资源冲突、升级回退和真实卸载验收。
- Phone Audio Relay 产品插件：接收已配对 Android 手机的蓝牙 A2DP 媒体音频并送入 Windows 正常输出混音，不接管电脑应用声音；提供设备刷新、选择、连接、断开、状态恢复和 `.tpk` 发布包。

个人学习版发布已完成，不需要服务器或官方认证；下一阶段仅在未来需要公开分发时再考虑 v0.2 签名真实性。Updater、强制权限和安全沙箱仍不在 v0.1 原型范围内。范围见 [PRODUCT_KEYBOARD_MOUSE_SCOPE.md](PRODUCT_KEYBOARD_MOUSE_SCOPE.md) 与 [PHONE_AUDIO_RELAY.md](PHONE_AUDIO_RELAY.md)，API 冻结记录见 [PLUGIN_API_V1.md](PLUGIN_API_V1.md)。

本地 `.tpk` 生成方式与“完整性不等于真实性”的发布边界见 [PACKAGE_RELEASE_POLICY.md](PACKAGE_RELEASE_POLICY.md)。

GitHub 的 CI、Tag 发布和 Release 附件流程见 [CHANGELOG.md](CHANGELOG.md) 与 `.github/workflows/release.yml`。

## 本地构建

需要安装受支持的 .NET SDK（项目目标为 `net8.0` / `net8.0-windows`）：

```powershell
dotnet restore ToolBox.sln
dotnet build ToolBox.sln --configuration Release
dotnet test ToolBox.sln --configuration Release
dotnet run --project src/ToolBox.Host/ToolBox.Host.csproj
```

发布前执行统一 dry-run：

```powershell
.\tools\Invoke-ReleaseValidation.ps1 `
  -Configuration Release `
  -OutputDirectory .\artifacts\release-validation
```

该命令使用 Host 项目版本，依次执行干净构建、`-warnaserror`、完整测试、自包含 Windows x64 发布、两个 `.tpk` 生成、包结构/版本/payload 哈希检查以及发布 SHA-256 反向核对。CI 和 Tag Release 调用同一脚本。

生成 Phone Audio Relay 安装包：

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\New-AudioRelayPackage.ps1 `
  -Configuration Release -Version 0.1.0 -OutputDirectory .\artifacts
```

Host 运行日志默认写入：

```text
%LocalAppData%\ToolBox\Logs
```
