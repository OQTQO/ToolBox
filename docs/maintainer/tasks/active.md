# 当前任务

状态：全部六个优化阶段完成，等待审查/提交与 ToolBox 0.4.0 发布。

## 任务

- 编号：2026-08-30-platform-hardening
- 目标：完成平台、Worker、安全、跨仓库权威规则、对话转接和 .NET 10 最终迁移。
- 仓库：ToolBox 软件仓库；配套插件适配在独立插件仓库执行。
- 权威源：软件仓库的平台实现、兼容性测试、已发布 SDK 和协议文档。

## 约束

- 软件契约优先；本阶段按用户要求不兼容旧 Manifest v1、package format 1 和未签名插件包。
- 不引入具体插件类型或 Plugin ID 到 Host 生产代码。
- 插件仓库与软件仓库冲突时，插件适配软件契约。
- 软件、测试、SDK、样例和插件统一使用 .NET 10；不承诺旧 SDK 二进制兼容，插件必须按目标软件 SDK 重建。

## 已完成

- 修复 CA1859/CA1822 导致的干净环境警告即错误构建失败。
- Worker 命名管道限制为当前用户。
- 通用插件 UI 请求增加默认 15 秒上限；超时终止 Worker 并进入 `RestartRequired`。
- 增加挂起 UI Action 回归测试，并验证 Worker 子进程树被清理。
- 新增 ADR-0003，明确平台契约由软件仓库定义、冲突时插件服从软件。
- 上下文导出增加工作区规则、Git HEAD 和权威源信息。
- 更新 Worker/运行时维护文档。
- 将 Worker 控制读取与插件请求执行解耦；活动请求期间仍可处理匹配 `requestId` 的取消和心跳，额外请求明确返回 `WORKER_REQUEST_BUSY`。
- 将请求取消令牌传入插件启动、停止、UI Action 和输入 API；Host 排空取消响应后再复用通道，插件 1 秒内不确认则终止 Worker 并进入 `RestartRequired`。
- 单条 Worker JSON Lines 消息增加 1,048,576 字符的双向读写上限，超限返回 `WORKER_MESSAGE_TOO_LARGE`。
- 增加取消后 Worker 继续可用、协议消息超限的回归测试。
- 修复 Core 测试项目在 `--artifacts-path` 模式仍从默认 `bin/` 复制旧 Worker/Fixture 的问题，隔离构建可验证本次产物。
- Worker 由 805 行单文件拆为最小入口、参数解析、控制循环和请求处理四层；最大文件降为 477 行。
- 将包 Manifest、元数据、哈希清单和运行时结构校验抽到 `PluginPackageValidator`；`PluginPackageInstaller` 聚焦安装事务、状态提交、回退和数据快照。
- 将 `MainWindowViewModel` 的插件管理流程和 `PluginWorkspaceViewModel` 的运行/UI 操作移入职责明确的 partial 文件，保持全部 WPF 属性名和绑定不变。
- 新增私带 `ToolBox.PluginSdk.dll` 与缺失入口程序集的包结构回归测试。
- Host 新增无窗口、机器可读的发布包烟雾模式；通过真实 `ToolBox.Host.exe` 复用安装器、动态目录、通用 ViewModel 和进程外 Worker，不引入具体插件分支。
- 插件仓库新增跨仓库编排脚本，在隔离产物目录中构建软件与插件、生成真实 `.tpk`，并验证安装、启用/UI 快照、停用和卸载四个阶段。
- 平台版本提升到 0.4.0；Manifest v2 强制声明软件定义的能力 ID，package format 2 强制 RSA-SHA256 分离签名，旧包明确拒绝。
- 安装器验证证书有效期、签名、发布者 ID 与 Manifest 绑定，并以 TOFU 信任库存储 `publisherId → certificateSha256`；同名发布者换钥或本地 blocked 策略会阻止安装。
- 新增 ADR-0004 与插件安全契约，明确签名真实性、TOFU 连续性、能力声明和 Worker 非沙箱边界。
- 插件仓库升级到 SDK 0.4.0；KeyboardMouse 声明 `host.ui.input-events`，AudioRelay 声明 `windows.bluetooth.audio-receiver`，打包和 Release 流程强制提供证书与 PKCS#8 私钥。
- 收尾审计移除旧 0.0.1 SDK/LegacyPlugin 二进制兼容夹具，修正 README 与 HelloPlugin 版本漂移，并确保安装事务失败时不会提前留下发布者 TOFU 信任记录。
- 收尾审计移除只能返回 `NotEvaluated` 的未接通包检查状态 API，并让软件与插件打包脚本在生成包时立即验证证书有效期和证书/私钥配对。
- 最终阶段将 Host、Core、Worker、SDK、全部测试夹具和 HelloPlugin 迁移到 .NET 10，使用 `global.json` 固定 SDK 10.0.400，并将 GitHub Actions 切换到 `10.0.x`。
- 平台与 SDK 升级到 0.4.0；修复 .NET 10 的 `X509CertificateLoader` 迁移和 WPF/WinForms 类型歧义，不通过关闭警告规避。
- 修复 Release 验证遗漏的强制签名参数；CI 使用一次性测试密钥，正式 Release 强制使用受保护 secret，并在隔离 artifacts 中生成自包含 Host/Worker、签名 HelloPlugin、DevKit 和 SHA-256 清单。

## 验证结果

- .NET SDK 10.0.400 严格隔离构建：通过，0 警告、0 错误。
- .NET 10 测试：Core 64 项、Host 28 项，共 92 项全部通过；新增正常取消与真实故障两类生命周期回归。
- 配套插件仓库 `tools/Validate-Plugins.ps1`：在 .NET 10 上通过，KeyboardMouse 4 项、AudioRelay 10 项；两个 `.tpk` 的内容校验和可复现打包均通过。
- 配套插件仓库 `tools/Invoke-ToolBoxHostSmokeTest.ps1`：通过；两个 Manifest v2 签名包均由真实 Host 完成签名/信任校验、安装、Worker 启用/UI 快照、停用和卸载。联合验证基于软件 HEAD `ecb727419e3f85f0c49ef74db14cee93133232d5` 加本任务未提交工作区变更。
- `tools/Get-ProjectContext.ps1`：执行通过。
- `git diff --check`：通过。
- `tools/Validate-PluginSamples.ps1`：SDK 0.4.0、net10.0 HelloPlugin 和签名包通过。
- `tools/Invoke-ReleaseValidation.ps1 -Version 0.4.0`：完整自包含 Release 资产、签名示例包、DevKit 和校验清单通过；门禁内真实 Host/Worker 的安装、启用、停用、卸载闭环通过。

## 已知边界

- 取消是协作式的：同步 `GetSnapshot()` 或忽略取消令牌的插件代码不能在线程内强制抢占，仍会由 1 秒确认窗口和 Worker 进程终止兜底。
- 进程外 Worker 仍是故障隔离，不是权限沙箱。
- TOFU 证明同一发布者密钥连续性，但首次信任仍来自用户主动安装；当前没有在线证书吊销与自动换钥流程。
- 能力声明已强制校验，但尚未收窄 Worker 的 Windows 文件、网络、注册表或设备权限。

## 下一步

1. 分别审查并提交软件、插件两个仓库的现有修改。
2. 先发布 ToolBox 0.4.0 与 DevKit，再合并/发布依赖 SDK 0.4.0 的插件仓库变更。
3. 软件 Release 完成后把插件 CI 的上游引用固定到已验证的完整 commit/tag。
