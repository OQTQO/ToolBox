# 当前任务

状态：已完成（2026-09-01）。当前唯一验收基线是原生 WPF 软件窗口；后续仅需用户进行人工验收。

## 任务

- 编号：2026-09-01-host-ui-04-icon-card-cleanup
- 目标：按批注整改 WPF Host 的标题栏、插件页布局、卡片菜单和软件图标，不改变插件业务链路。
- 仓库：ToolBox 软件仓库。
- 范围：`src/ToolBox.Host`、Host UI 测试和维护文档；不修改 Core、Worker、SDK、插件 Manifest、插件协议或公开插件 API。

## 本轮决策

- 删除自定义标题栏左侧说明、中部 Host 状态和 `Ctrl K` 占位；改用 Windows 原生标题栏，`TitleBarCenterText` 保留为可编辑的系统窗口标题文案，默认“桌面工具管理”。
- 原生标题位置、系统按钮和窗口圆角服从 Windows；文案空值恢复当前语言默认值，单行最多 32 个字符，换行规范化为空格。
- “安装本地插件”只在插件页显示；概览、活动和设置页不保留重复安装入口。
- 删除侧栏底部搜索提示；`Ctrl+K` 仍直接切换到插件页并聚焦搜索框。
- 插件搜索框从卡片列表左边缘开始，占据筛选区以外的剩余宽度；排序按钮显示当前排序方式，最小窗口不产生横向溢出。
- 所有插件卡片固定使用信息完整的“重点”结构，不再提供紧凑/标准/重点切换；旧卡片尺寸字段仍读取并保存，仅用于兼容旧设置。
- 卡片“更多”菜单只保留“卸载”；移除隐藏入口，底层打开状态与插件生命周期实现保持不变。
- 软件和托盘使用统一的酸性绿/深墨色工具箱几何图标；窗口最小化、最大化/还原、关闭按钮交给 Windows 原生窗口框架。
- 放弃自绘透明窗口外框，改用 Windows 原生边框、圆角、阴影和缩放命中；WPF 内容区不再叠加 `WindowChrome`、DWM 或根级圆角裁切。
- 常规边框统一为整数像素；页面切换只做短时透明度过渡，详情抽屉保留位移动画但不使用大范围阴影；文字渲染使用自动 hinting/rendering。
- Host 通过 `ApplicationHighDpiMode=PerMonitorV2` 启用高 DPI 感知，保留布局取整和设备像素对齐。
- `TitleBarCenterText` 是 Host 内部可选设置字段；旧主题、透明度、彩场、圆角、卡片尺寸和旧概览文案字段继续兼容读取，旧圆角限制为 12–20px。

## 本轮交付

- 更新 `MainWindow.xaml`：切换到原生 Windows 窗口外框和标题栏，保留插件工具栏、设置文案区域和详情抽屉样式；不再显示侧栏搜索占位。
- 更新 `MainWindow.xaml.cs`：移除自绘标题栏、窗口控制、透明根级圆角裁切和 DWM 圆角覆盖；保留窗口标题文案延迟提交、搜索占位状态、无平移页面过渡，以及仅含卸载操作的上下文菜单。
- 更新 `HostSettingsService` / `MainWindowViewModel`：Schema 版本提升到 4，新增可选中央文案字段并保持旧 JSON 兼容。
- 更新 `App.xaml` / `ThemeService.cs`：清理未使用阴影资源，统一整数边框和自动文字渲染，提升辅助文字对比度。
- 删除设置页默认卡片尺寸选择和卡片菜单尺寸选择；保留 `Theme`、`DynamicGlow`、`Transparency`、`CornerRadius`、`PluginCardSizes` 等旧字段的兼容读写。
- 更新 `Assets/ToolBox.svg`、`ToolBox.ico`、`ToolBox-256.png`、`ToolBox.Tray.ico` 和托盘 PNG，并同步更新图标生成脚本。
- 新增 `app.manifest` 与 `ApplicationHighDpiMode` 配置，明确 PerMonitorV2。
- 增加中央文案规范化、Unicode 截断、空值回退和重置回归测试。
- 删除未跟踪的 `design-previews/rebuild-directions/`；历史已跟踪 HTML 文件保留作资料，不再修改或作为验收依据。

## 验证

- `dotnet build ToolBox.sln --configuration Release -p:TreatWarningsAsErrors=true`：通过，0 警告、0 错误。
- `dotnet test ToolBox.sln --configuration Release`：通过，Host 42/42、Core 64/64。
- `git diff --check`：通过；Git 仅提示既有 LF/CRLF 工作副本转换，无空白错误。
- `dotnet publish`：已用 `win-x64`、自包含、单文件方式重新生成 Host 与 Worker；`artifacts/ui-acceptance/ToolBox.Host.exe` 已成功启动且进程响应正常。
- 本次批注跟进：移除搜索框外层重复边框，插件“更多”菜单仅保留卸载，并将安装入口移入插件页搜索/筛选工具栏下方；按用户要求仅执行 Host 定向构建和验收包启动检查，未重复运行全量测试。
- 本次边框跟进：移除自绘透明窗口外框及根级圆角裁切，改用 Windows 原生窗口边框、系统圆角和窗口控制，避免黑色直线描边在圆角处断开；`TitleBarCenterText` 改作为系统标题文案，页面与插件卡片圆角不变。
- 新图标资源检查：应用/托盘 PNG 均为透明 RGBA，应用图标与托盘图标采用同一酸性绿工具箱标记。
- WPF 真实窗口验收包当前运行中，路径为 `artifacts/ui-acceptance/ToolBox.Host.exe`；进程路径已核对无误。
- 自动桌面捕获检查期间用户按下物理 Escape，按 Computer Use 安全规则停止后续自动输入；最大化图标、菜单焦点和滚轮手感仍由用户在可见窗口中完成最终确认。

## 已知边界

- 原生窗口边框和标题栏的具体颜色、圆角和阴影由 Windows 版本、主题和系统设置决定；WPF 只控制客户区内容。
- 没有真实插件时只能验证空目录和布局结构；安装真实 `.tpk` 后沿用原有安装、启停、详情和生命周期链路。
- HTML 预览历史文件不再维护，也不代表当前软件行为。

## 下一步

1. 用户直接检查已启动验收包中的新图标、Windows 原生窗口控制和标题文案、插件页对齐、四角和清晰度。
2. 在 1440×900、1280×800、1040×700、最大化及 100%/125%/150% DPI 下完成人工验收。
3. 用 0、1、2、3、4、5 个插件检查固定重点卡布局、筛选、排序、详情、启停和卸载流程。
