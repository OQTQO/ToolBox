# Host 模块说明

## 职责

提供通用 WPF 外壳：导航、设置、主题、通用插件卡片、详情弹窗、生命周期操作、诊断事件和托盘行为。

## 主要入口

- `MainWindow.xaml`：布局、资源和通用模板。
- `MainWindow.xaml.cs`：窗口事件、动画和通用视觉行为。
- `MainWindowViewModel.cs`：页面、筛选、安装、刷新和设置状态。
- `PluginWorkspaceViewModel.cs`：单个插件的生命周期、通用 UI 快照和操作。
- `HostSettingsService.cs`：本地设置和卡片尺寸覆盖。

## 边界与不变量

- 不引用 AudioRelay、KeyboardMouse 或朋友插件类型。
- 不按具体 Plugin ID 增加布局或业务分支。
- 插件内容只能来自 SDK 通用协议。
- UI 集合修改必须回到 WPF Dispatcher。
- 停止、卸载和安装失败必须显示真实结果，不能伪装成功。

## 修改时检查

先检查 `docs/maintainer/ui-design.md`、`ToolBox.Host.Tests` 和动态工作区测试；视觉改动需验证 1440×900、1920×1080 和窄窗口。
