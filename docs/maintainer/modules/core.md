# Core 模块说明

## 职责

负责插件包检查、安装事务、动态目录发现、生命周期运行时、资源与服务 Lease、诊断和数据保留。

## 主要入口

- `Packaging/PluginPackageInspector.cs`
- `Packaging/PluginPackageInstaller.cs`
- `Plugins/InstalledPluginCatalog.cs`
- `Plugins/PluginDiscovery.cs`
- `Plugins/OutOfProcessPluginRuntime.cs`
- `Plugins/Worker/WorkerProcessLauncher.cs`

## 不变量

- 只接受合法 Manifest 和安全包路径。
- 安装使用 staging、提交状态和失败回退。
- 目录发现忽略 `.staging`、未提交和损坏状态，不阻止 Host 启动。
- 动态插件统一优先走 OutOfProcess runtime。
- 关闭、终止、等待退出和释放共享同一个 `ShutdownDeadline`。
- 插件数据目录不随运行版本目录删除。

## 修改时检查

必须运行 `ToolBox.Core.Tests`，并保留安装回退、安全 ZIP、Worker 超时、资源冲突和 API 兼容性测试。
