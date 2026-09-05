# Host 模块说明

## 职责

提供通用 WPF 外壳：导航、设置、主题、通用插件卡片、详情弹窗、生命周期操作、诊断事件和托盘行为。

## 主要入口

- `MainWindow.xaml`：布局、资源和通用模板。
- `MainWindow.xaml.cs`：窗口事件、动画和通用视觉行为。
- `MainWindowViewModel.cs`：页面、诊断和设置状态。
- `MainWindowViewModel.PluginManagement.cs`：插件筛选、安装、卸载、刷新和集合投影。
- `PluginWorkspaceViewModel.cs`：单个插件的通用状态与展示投影。
- `PluginWorkspaceViewModel.Operations.cs`：单插件生命周期、通用 UI 快照和输入/操作转发。
- `HostSettingsService.cs`：本地设置和卡片尺寸覆盖。
- `HostSmokeCommandLine.cs`、`HostPackageSmokeRunner.cs`：无窗口的发布包烟雾模式，仍使用真实 Host 安装、目录、ViewModel 和 Worker 链路，并输出机器可读 JSON。
- `HostLaunchOptions.cs`：普通 Host 的 UI 验收启动参数；可指定隔离根目录并自动安装任意测试 `.tpk`，不包含具体插件分支。
- `tools/Start-UiAcceptance.ps1`、`tools/Start-UiAcceptance.cmd`：构建、准备并启动可重复的 WPF UI 验收环境；后者供 Windows 双击启动。
- `tools/Invoke-InstallerBuild.ps1`、`tools/Invoke-InstallerValidation.ps1`：使用 Inno Setup 生成并验证可长期复用的 `Setup.exe`；安装程序只包含 Host/Worker，不包含测试插件。

## 边界与不变量

- 不引用 AudioRelay、KeyboardMouse 或朋友插件类型。
- 不按具体 Plugin ID 增加布局或业务分支。
- 插件内容只能来自 SDK 通用协议。
- 详情页在加载到有效插件 UI 后默认进入“操作”页；纯后台插件进入“概览”页。未加载快照时先读取快照，用户手动切换页签后不被后台更新覆盖。
- 新版 `Elements` 按声明顺序追加到旧版 `Values`、`Actions` 和输入面板之后；`Group` 只插入标题，不改变元素顺序。状态和进度固定显示在操作页顶部。
- 控件、菜单、状态和对话框全部由 Host 原生 WPF 模板渲染；插件文本按普通文本处理，不解释 XAML、HTML 或脚本。
- UI 集合修改必须回到 WPF Dispatcher。
- 停止、卸载和安装失败必须显示真实结果，不能伪装成功。
- ViewModel partial 文件按职责分层但共享同一通用模型；不得借拆分引入具体插件分支。
- `--smoke-test-package` 只用于通用发布验证；不得在烟雾执行器中写入具体插件 ID、类型或容错分支。
- `--ui-acceptance-root` 和 `--ui-acceptance-package` 只改变验收启动的存储根和初始包，不改变普通启动的默认用户数据路径。
- 普通安装的可变数据位于 `<安装目录>\Data`：插件包在 `Plugins`，插件数据在 `PluginData`，日志在 `Logs`，设置文件为 `ui-settings.json`；首次启动会复制旧安装数据并保留旧目录。

## 修改时检查

先检查 `docs/maintainer/ui-design.md`、`ToolBox.Host.Tests` 和动态工作区测试；视觉改动需验证 1440×900、1920×1080 和窄窗口。
