# Changelog

所有重要版本变更都记录在这里。GitHub Release 会以版本 Tag 为准生成对应发布记录。

## [0.5.0] - 2026-09-01

### Changed

- 完成原生 WPF Host UI 04：使用 Windows 原生标题栏和窗口边框，统一通用插件卡片、插件页工具栏、详情抽屉、设置和托盘图标。
- 增加 PerMonitorV2 高 DPI 感知、响应式卡片布局、平滑滚动和通用 UI 回归测试。
- 将 Host、Core、PluginSdk、PluginWorker 和 HelloPlugin 发布版本统一为 0.5.0。
- 发布对应的 Windows x64 Host、HelloPlugin、PluginDevKit 和 SHA-256 校验清单。

### Verification

- 发布前通过版本一致性检查、Release 构建和 Host/Core 测试；GitHub Release 资产已生成。

## [0.4.0] - 2026-08-31

### Changed

- ToolBox Host 改为 Manifest 驱动的通用插件外壳，移除 Keyboard/Audio 产品专用页面、ViewModel 和注册器。
- 动态插件统一通过 `ToolBox.PluginWorker` 进程外运行，安装后进入可管理状态，用户从通用卡片启用。
- `ToolBox.PluginSdk` 可打包为 NuGet；新增 `HelloPlugin`、通用 `.tpk` 打包脚本和 Sample 验证入口。
- KeyboardMouse 和 AudioRelay 从主仓库迁移到独立的 `ToolBox-Plugins` 仓库，插件通过 GitHub Release 交换 `.tpk`。
- 历史维护资料迁移到 `docs/maintainer/`，第三方开发文档集中到 `docs/`。
- `.tpk` 升级为 Manifest v2 / package format 2，增加平台能力声明、RSA-SHA256 发布者签名、证书指纹 TOFU 信任和完整包校验。
- 插件运行和控制协议统一通过进程外 Worker，补齐取消、Heartbeat、消息大小限制、崩溃、超时和卸载测试。

## [0.2.2] - 2026-08-29

### Added

- 新增可选的 `IPluginUiProvider`：插件可以声明通用状态、数据项、操作按钮和键鼠输入区域。
- Host 通过 Worker 转发插件操作，不再需要为具体插件添加 WPF 页面或 Host 类型引用。
- HelloPlugin、KeyboardMouse 和 AudioRelay 提供可操作的通用工作区示例。

## [0.2.1] - 2026-08-29

### Fixed

- 安装、更新和卸载插件后的工作区刷新统一回到 WPF UI 调度线程，避免 `CollectionView` 跨线程异常。
- 卸载完成后立即重新扫描动态插件目录，已卸载插件不再残留在设置页和工作区列表。
- Release 改为发布同时包含 Host 与 `ToolBox.PluginWorker.exe` 的 Windows x64 ZIP，修复启用插件时找不到 Worker 的问题。

### Release assets

- `ToolBox-v0.2.1-win-x64.zip`：解压后同时运行 Host 与 Worker 所需的完整程序包。
- `HelloPlugin-0.2.1.tpk`：通用 Sample 插件包。
- `ToolBox-PluginDevKit-0.2.1.zip`：SDK NuGet、开发文档和打包工具。
- `SHA256SUMS-v0.2.1.txt`：发布附件完整性校验清单。

## [0.1.1] - 2026-08-28

### Changed

- 本地、CI 与 Tag Release 统一调用 `Invoke-ReleaseValidation.ps1`，在推送标签前即可完成完整发布 dry-run。
- 插件包使用稳定条目排序和固定 ZIP 时间戳；相同源码、版本与 SDK 输入生成逐字节一致的 `.tpk`。
- 发布验证会反向检查准确附件集合、插件身份/版本、包内 payload 清单及 SHA-256。
- Host 使用显式且幂等的退出/重启意图与有序关闭管线；单个资源释放失败不再阻断其余清理步骤。
- 主窗口通过窄生命周期命令接口请求托盘隐藏、退出和重启，重启可执行文件解析与启动参数已独立测试。
- Host 导航、插件页选择与设置页插件管理改由统一工作区集合驱动；产品专属页面从主窗口拆分为独立视图。
- 工作区状态投影通过可注入 UI 调度边界更新，测试不再依赖真实 WPF 消息循环。

### Verification

- Release 构建 0 警告/0 错误，完整测试 `86/86` 通过；自包含 `win-x64` Host、两个 `.tpk`、包内哈希与发布 SHA-256 使用同一入口验证。
- 两轮独立 dry-run 生成的四个候选附件逐字节一致。
- 用户完成最终候选的窗口、插件管理及 Android 手机音频物理验收。

### Release assets

- `ToolBox-Host-v0.1.1-win-x64.exe`：自包含 Windows x64 Host。
- `KeyboardMouse-0.1.1.tpk`：Keyboard & Mouse Test 个人学习插件包。
- `PhoneAudioRelay-0.1.1.tpk`：Phone Audio Relay 个人学习插件包。
- `SHA256SUMS-v0.1.1.txt`：三个程序包的 SHA-256 校验清单。

## [0.1.0] - 2026-08-26

### Added

- WPF Host Shell、结构化诊断日志和健康/关闭生命周期。
- Plugin API v1、InProcess/OutOfProcess 生命周期、资源 Lease 和服务 Broker。
- 安全 `.tpk` 安装、SHA-256 完整性校验、版本并存、激活状态和 Config/State 快照。
- Keyboard & Mouse Test 个人学习产品：局部输入面板、设置、资源冲突、升级回退和真实卸载。
- Host 内 `.tpk` 安装/更新入口。
- GitHub Actions CI 与 Tag 触发的 Release 资产生成流程。
- Phone Audio Relay Product 02：接收已配对 Android 手机的 A2DP 媒体音频并送入 Windows 正常输出混音。
- 雾银 B 方案 Host、Module T 图标、中英双语、托盘生命周期和插件安装/打开/运行三层状态。
- Phone Audio Relay 配对源刷新/选择、开始/停止接收、连接状态恢复、WinRT 重载和明确的重启边界。
- `PhoneAudioRelay-<version>.tpk` 打包脚本、Windows SDK/WinRT 运行依赖、真实平台与 ALC 卸载测试。

### Release assets

- `ToolBox-Host-v0.1.0-win-x64.exe`：自包含 Windows x64 Host。
- `KeyboardMouse-0.1.0.tpk`：Keyboard & Mouse Test 个人学习插件包。
- `PhoneAudioRelay-0.1.0.tpk`：Phone Audio Relay 个人学习插件包。
- `SHA256SUMS-v0.1.0.txt`：三个程序包的 SHA-256 校验清单。

当前版本面向个人学习，不提供服务器、官方认证或生产级自动更新。
