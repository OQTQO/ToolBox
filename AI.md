# ToolBox 软件仓库 AI 入口

## 项目定位

ToolBox 是 Windows 上的通用插件外壳。平台负责安装、发现、生命周期、Worker 隔离、通用状态 UI 和诊断；具体工具能力由独立插件实现。

## 先读什么

1. `docs/maintainer/tasks/active.md`
2. 与任务相关的 `docs/maintainer/modules/*.md`
3. 相关 `docs/maintainer/decisions/*.md`
4. 最后用 `rg` 定位源码和测试

不要默认读取历史归档、全部源码或所有插件文档。

涉及界面、主题、排版或动画时，再读取 `docs/maintainer/ui-design.md`；它是 UI 长期基线，不是每个任务都要加载的上下文。

## 固定边界

- `src/ToolBox.PluginSdk`：第三方唯一依赖，稳定 Plugin API v1。
- `src/ToolBox.Core`：Manifest、`.tpk` 校验与安装、动态目录、生命周期、数据保留和 Worker 会话。
- `src/ToolBox.PluginWorker`：进程外插件 Worker 和控制协议。
- `src/ToolBox.Host`：与具体插件无关的 WPF 外壳、通用 UI、设置、托盘和事件流；生产代码不引用具体插件类型。
- `samples/HelloPlugin`：教学和兼容性验证样例，不是内置产品。
- AudioRelay、KeyboardMouse 的实现只在独立插件仓库；软件测试可以保留脱钩的测试或兼容性 ID，但不得形成 Host 生产依赖。

## 不可违反的规则

1. 新增普通插件不得修改 Host 源码。
2. 第三方动态插件必须支持 `outOfProcess`，安装后不自动启动。
3. Host 不引用具体插件类型、页面、业务字段或 Plugin ID。
4. 安装必须保持事务性、路径安全、状态提交和失败回退。
5. 插件的 Config/State 数据与运行版本分离，升级或回退不得无故丢失。
6. SDK 公共 API v1 修改必须先检查兼容性测试和 Manifest 协议。
7. 停止失败必须保留 Faulted、DisableFailed 或 RestartRequired 等真实状态。
8. 不在本阶段假设存在签名、权限 enforcement、沙箱、商城或自动更新。

## 源码地图

| 需求 | 主要入口 |
| --- | --- |
| 插件安装和回退 | `src/ToolBox.Core/Packaging/PluginPackageInstaller.cs` |
| 包检查 | `src/ToolBox.Core/Packaging/PluginPackageInspector.cs` |
| 动态发现 | `src/ToolBox.Core/Plugins/InstalledPluginCatalog.cs`、`PluginDiscovery.cs` |
| 进程外运行 | `src/ToolBox.Core/Plugins/OutOfProcessPluginRuntime.cs`、`OutOfProcessPluginSession.cs` |
| Worker 协议 | `src/ToolBox.Core/Plugins/Worker/`、`src/ToolBox.PluginWorker/Program.cs` |
| SDK 契约 | `src/ToolBox.PluginSdk/` |
| 通用 Host UI | `src/ToolBox.Host/MainWindow.xaml`、`MainWindowViewModel.cs`、`PluginWorkspaceViewModel.cs` |
| Host 设置和主题 | `src/ToolBox.Host/HostSettingsService.cs`、`ThemeService.cs` |

## 验证命令

```powershell
dotnet test ToolBox.sln --configuration Release
```

修改任务结束前必须更新 `docs/maintainer/tasks/active.md`，写明文件、测试、未解决问题和下一步。未经用户要求，不清理或回滚工作区已有修改。
