# 当前任务

状态：实施完成，文档已补齐，待创建提交并推送 GitHub；未发布（2026-09-05）

## 本次追加任务：面向 GitHub 插件开发者完善文档

- 将根 README、SDK README、第三方插件开发教程、Manifest v2 和运行时文档统一为外部开发者可执行的路径：下载匹配版本 DevKit、配置本地 SDK feed、创建 .NET 10 类库、实现 `IPlugin`、编写 Manifest、打包签名、安装验收和提交插件仓库。
- 补充 DevKit 内容说明、项目/`NuGet.config` 示例、Manifest 最小模板、平台能力 ID、版本一致性、签名私钥边界、插件仓库结构、Pull Request 清单和通用 UI 命令/控件提交语义。
- 明确插件只依赖 `ToolBox.PluginSdk`，不引用 Host/Core/Worker；明确插件运行文件、`Data\Plugins`、`Data\PluginData` 和 UI 默认页行为。
- 本次仅修改文档，不改变 SDK、Host、Worker 或安装器实现；前序 0.6.0 实现及其未提交修改将与文档一起形成提交。

## 本次文档验证

- `git diff --check`：通过；仅有 Git 既有的 LF/CRLF 转换提示。
- 已核对文档中的 DevKit 文件布局、`ToolBox.PluginSdk` 包版本、`New-PluginPackage.ps1` 参数、Manifest v2 字段和平台能力 ID 与当前实现一致。
- 前序实现验证仍有效：Core 70/70、Host 49/49、Release 构建 0 警告/0 错误、安装器隔离安装/升级/卸载验证通过。

## 本次追加任务：UI 修复、数据目录迁移与安装程序

- 范围：仍仅修改 `软件/` 仓库；保留前序未提交修改，不修改插件仓库，不提交、不推送、不发布。
- UI 修复：共享 `PrimaryButtonStyle` 已明确设置亮色主按钮、深色文字、对齐、悬停、按下、禁用和键盘焦点状态；通用下拉框使用选项模板显示插件提供的中文 `Label`，不再显示 ViewModel 类型名。
- 数据目录：正常运行使用 `<安装目录>\Data\Plugins`、`PluginData`、`Logs` 和 `ui-settings.json`；验收模式继续使用隔离根目录。设置页和“打开数据目录”使用 Host 实际路径。
- 旧数据迁移：首次启动将旧安装目录 `Plugins`、`%LocalAppData%\ToolBox\Plugins`、`Logs` 和 `ui-settings.json` 按目标目录映射复制；目标已有文件优先，写入 `.legacy-data-migration-v1.complete` 后幂等跳过；不删除源目录，失败写入 Host 日志并保留源数据。
- 安装程序：新增 `installer\ToolBox.iss`、`tools\Invoke-InstallerBuild.ps1` 和 `tools\Invoke-InstallerValidation.ps1`。使用 Inno Setup 6 生成 `ToolBox-Setup-v0.6.0.exe`，默认安装到 `%LocalAppData%\Programs\ToolBox`，安装包只含 Host/PluginWorker，不含 HelloPlugin；升级保留 `Data`，卸载不删除 `Data`。Release Validation、CI 和发布校验清单已纳入安装包及 SHA-256。
- 安装构建脚本使用独立 `--artifacts-path` 和临时发布目录，避免与正在运行的验收 Host 共享构建中间文件；可长期复用，也支持显式传入 `ISCC.exe` 路径。

## 本次追加验证

- `dotnet test ToolBox.sln -c Release --artifacts-path .artifacts-final --disable-build-servers -p:NuGetAudit=false`：Core 70/70、Host 49/49 通过。
- `dotnet build ToolBox.sln -c Release --artifacts-path .artifacts-final --no-restore --disable-build-servers`：通过，0 警告、0 错误。
- `pwsh -NoProfile -ExecutionPolicy Bypass -File .\tools\Invoke-InstallerBuild.ps1 -Version 0.6.0`：Inno Setup 6.7.3 编译成功，生成 `artifacts\installer\ToolBox-Setup-v0.6.0.exe`。
- `pwsh -NoProfile -ExecutionPolicy Bypass -File .\tools\Invoke-InstallerValidation.ps1 -SetupPath .\artifacts\installer\ToolBox-Setup-v0.6.0.exe`：隔离首次安装、升级、数据保留、无测试插件和卸载后数据保留均通过。
- PowerShell 解析检查：`Invoke-InstallerBuild.ps1`、`Invoke-InstallerValidation.ps1`、`Invoke-ReleaseValidation.ps1`、`Start-UiAcceptance.ps1` 通过。
- `git diff --check`：通过；`git status` 确认仅软件仓库存在本任务及前序未提交修改。
- 本次最终安装包 SHA-256：`F1F5DFFD501CE9FD2C6EF5B56F5AC55812E05A453665CFD9ED6F25DF40698A06`。

## 任务

- 编号：2026-09-05-generic-plugin-ui-contract-0-6-0
- 目标：在软件仓库增量完善通用插件 UI 契约、Worker 通信和 WPF Host，使所有插件可复用按钮、菜单、选择控件、状态、进度和对话框能力。
- 范围：仅 `软件/`；不修改 `插件/`，不增加音频插件或设备类型专用分支。
- 基线：软件仓库 `main`，实施前 HEAD `917deb8`，工作区干净；Plugin API major 和 Worker protocol major 均保持 `1`。
- 版本：Core、Host、PluginSdk、PluginWorker 和 HelloPlugin 统一为开发版本 `0.6.0`；测试 Fixture 保持原有版本。

## 实施结果

- SDK 保留原有 `IPluginUiProvider` 三个成员、四参数 `PluginUiSnapshot` 构造函数、旧版 `PluginUiAction`/`PluginUiValue`/`PluginInputSurface` 用法和 `PluginContract.PluginApiMajor = 1`。
- 新增纯数据 UI 类型：元素、菜单、选项、标准/媒体命令、样式、更新模式、状态、进度、取消动作和对话框；新增可选 `IPluginUiUpdateSource`。
- Worker 使用单一出站写入队列，`ui.updated` 高频更新采用最新值覆盖；Core Host 使用单一读取泵路由响应、错误、心跳和主动更新。
- WPF Host 在旧版 UI 之后按声明顺序渲染新版元素，支持原生控件模板、分组标题、状态/进度、取消、模态对话框、中文标准命令和无障碍名称。
- 有效 UI 默认进入详情“操作”页，纯后台插件进入“概览”；未加载快照时先读取，用户手动切换后不被异步结果覆盖。
- 更新 SDK README、v1 契约文档、插件开发文档、SDK/Worker/Host 维护文档、README、安全说明和 0.6.0 开发版变更记录。
- 更新 HelloPlugin 与示例验证脚本的 0.6.0 版本基线。
- 新增长期可复用的 `tools/Start-UiAcceptance.ps1`：构建本地验收资产、自动安装并启用测试插件、启动隔离的 WPF Host；Host 新增通用验收启动参数，不识别具体插件类型。
- 增加 `tools/Start-UiAcceptance.cmd` 双击入口；真实启动检查确认 Host/Worker 正常运行、`HelloPlugin 0.6.0` 自动安装并启用。
- 真实启动发现并修复进度条只读属性被 WPF 按 TwoWay 绑定的问题；修复后日志无 `HOST_DISPATCHER_UNHANDLED` 或 `HOST_UI_ACCEPTANCE_START_FAILED`。
- 根据验收截图补齐 Host 通用控件的视觉层：主按钮使用可渲染的内容呈现器和紧凑布局；下拉框、复选框、单选框、滑块使用统一的 ToolBox 配色、圆角、焦点和禁用态；新元素以操作卡片和分组标题呈现，保留无障碍名称。

## 验证

- `dotnet test tests\ToolBox.Core.Tests\ToolBox.Core.Tests.csproj --configuration Release --artifacts-path .artifacts-ui --no-restore --filter "FullyQualifiedName~PluginApiV1CompatibilityTests|FullyQualifiedName~WorkerProtocolTests|FullyQualifiedName~OutOfProcessPluginRuntimeTests"`：22/22 通过。
- `dotnet test ToolBox.sln --configuration Release --artifacts-path .artifacts-ui --no-restore`：Core 70/70、Host 47/47 通过（含验收启动参数测试）。
- `dotnet build ToolBox.sln --configuration Release --artifacts-path .artifacts-ui --no-restore`：通过，0 警告、0 错误。
- `dotnet build src\ToolBox.Host\ToolBox.Host.csproj --configuration Release --artifacts-path .artifacts-ui-verify --no-restore --disable-build-servers`：通过，0 警告、0 错误；确认新 WPF 控件模板可编译。
- `pwsh -NoProfile -ExecutionPolicy Bypass -File .\tools\Validate-PluginSamples.ps1 -Configuration Release -Version 0.6.0`：SDK 本地包、HelloPlugin 构建和 `com.toolbox.hello-0.6.0.tpk` 打包通过，0 警告、0 错误。
- `pwsh -NoProfile -ExecutionPolicy Bypass -File .\tools\Start-UiAcceptance.ps1 -Version 0.6.0 -Configuration Release -ResetAcceptanceData -PrepareOnly`：成功生成本地 Host 压缩包、HelloPlugin 测试包和隔离验收目录；其内部 Release 验证为 Core 70/70、Host 47/47、构建 0 警告/0 错误。
- `pwsh -NoProfile -ExecutionPolicy Bypass -File .\tools\Start-UiAcceptance.ps1 -Version 0.6.0 -SkipBuild`：真实启动成功，测试包自动安装、Worker 自动启动、插件自动启用；修复进度条绑定后验收日志无未处理 WPF 异常。
- `cmd /c ".\tools\Start-UiAcceptance.cmd -Version 0.6.0 -SkipBuild -PrepareOnly"`：双击包装入口解析和转发通过。
- `git diff --check`：通过；Git 仅提示既有的 LF/CRLF 转换提醒。
- `git status --short --branch`：保持 `main`，仅有本任务软件仓库未提交修改；未触碰插件仓库。
- 验收入口：`pwsh -NoProfile -ExecutionPolicy Bypass -File .\tools\Start-UiAcceptance.ps1 -PrepareOnly`（资源准备检查）以及无参数运行（实际启动窗口）。
- 本次真实验收资源位于 `artifacts\ui-acceptance`；最终启动进程使用隔离数据根，测试包已安装到 `data\Plugins\com.toolbox.hello\versions\0.6.0`。

## 已知边界

- 本任务未修改插件仓库；新音频插件适配需等待本软件契约确认后另建任务。
- 0.6.0 开发安装包未做 Authenticode 签名；正式发布前仍需处理证书和 SmartScreen 信任。
- 当前本地 Inno Setup 安装未提供 `ChineseSimplified.isl`，因此安装向导使用 Inno 默认语言；ToolBox Host、SDK 和插件 UI 文本仍按中文契约/本地化运行。
- 安装器自动化已覆盖静默安装、升级和卸载数据保留；WPF 主按钮、下拉框、数据目录显示和安装后窗口仍需用户在桌面环境做最终人工验收。
- SDK 对未知控件只安全忽略并回退未知枚举；Host 警告记录仍沿用现有 UI 错误/诊断路径，未新增独立诊断面板。
- WPF 视觉、键盘焦点和真实窗口尺寸仍需在用户桌面环境做最终人工验收；本轮已完成控件样式重做并重新生成验收包，视觉最终判断仍以用户桌面窗口为准。
- 0.6.0 仅为开发版本；验收脚本生成的 ZIP、`.tpk` 和临时签名只用于本地验收，不执行正式发布、提交或推送。
- 新增脚本默认会在 `artifacts\ui-acceptance` 生成本地验收资产，并使用临时验收签名；这不是发布流程。多次运行会复用验收数据，需干净状态时使用 `-ResetAcceptanceData`。

## 下一步

1. 用户使用 `Start-UiAcceptance.ps1` 完成 WPF 视觉、键盘焦点和交互人工验收。
2. 用户运行 `artifacts\installer\ToolBox-Setup-v0.6.0.exe` 验收默认安装目录、开始菜单入口、升级和卸载行为。
3. 用户确认通用 UI 契约和 Host 交互语义。
4. 在插件仓库按稳定的 0.6.0 SDK 契约适配正式版音频流转插件，并使用公共 `Elements`/`Status`/`Progress` 能力。
