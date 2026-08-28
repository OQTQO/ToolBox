# Changelog

所有重要版本变更都记录在这里。GitHub Release 会以版本 Tag 为准生成对应发布记录。

## [Unreleased]

### Changed

- 本地、CI 与 Tag Release 统一调用 `Invoke-ReleaseValidation.ps1`，在推送标签前即可完成完整发布 dry-run。
- 插件包使用稳定条目排序和固定 ZIP 时间戳；相同源码、版本与 SDK 输入生成逐字节一致的 `.tpk`。
- 发布验证会反向检查准确附件集合、插件身份/版本、包内 payload 清单及 SHA-256。
- Host 使用显式且幂等的退出/重启意图与有序关闭管线；单个资源释放失败不再阻断其余清理步骤。
- 主窗口通过窄生命周期命令接口请求托盘隐藏、退出和重启，重启可执行文件解析与启动参数已独立测试。
- Host 导航、插件页选择与设置页插件管理改由统一工作区集合驱动；产品专属页面从主窗口拆分为独立视图。
- 工作区状态投影通过可注入 UI 调度边界更新，测试不再依赖真实 WPF 消息循环。

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
