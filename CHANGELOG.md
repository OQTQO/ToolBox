# Changelog

所有重要版本变更都记录在这里。GitHub Release 会以版本 Tag 为准生成对应发布记录。

## [0.1.0] - 2026-08-26

### Added

- WPF Host Shell、结构化诊断日志和健康/关闭生命周期。
- Plugin API v1、InProcess/OutOfProcess 生命周期、资源 Lease 和服务 Broker。
- 安全 `.tpk` 安装、SHA-256 完整性校验、版本并存、激活状态和 Config/State 快照。
- Keyboard & Mouse Test 个人学习产品：局部输入面板、设置、资源冲突、升级回退和真实卸载。
- Host 内 `.tpk` 安装/更新入口。
- GitHub Actions CI 与 Tag 触发的 Release 资产生成流程。

### Release assets

- `ToolBox-Host-v0.1.0-win-x64.exe`：自包含 Windows x64 Host。
- `KeyboardMouse-0.1.0.tpk`：Keyboard & Mouse Test 个人学习插件包。

当前版本面向个人学习，不提供服务器、官方认证或生产级自动更新。
