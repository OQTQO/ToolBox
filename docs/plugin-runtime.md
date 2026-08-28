# 插件运行时

动态插件安装完成后进入 `Disabled`，用户可以在通用状态卡片中启用或停止插件；`runtime.background` 目前只用于描述和诊断。Host 通过 `OutOfProcessPluginRuntime` 启动 `ToolBox.PluginWorker`，Worker 加载插件并调用 `IPlugin.StartAsync`。停用时按同一个 `ShutdownDeadline` 调用 `StopAsync`、发送 Worker shutdown、等待退出、必要时终止进程树并释放句柄。

主要状态包括：`Disabled`、`Starting`、`Running`、`Stopping`、`DisableFailed`、`RestartRequired`、`Faulted` 和 `Quarantined`。停止失败保留故障状态，不会伪装成 `Disabled`。

插件程序集、依赖和后台任务都在独立 Worker 进程内。Host 只持有 Manifest、安装版本、生命周期状态和错误事件；首版没有插件自定义 WPF 页面、声明式设置或插件命令。

`IPluginContext.LifetimeScope` 的 Token 会在插件卸载时取消。通过 `Track(Task)` 追踪后台任务，通过 `Register(IDisposable)` 或 `Register(IAsyncDisposable)` 注册资源释放。独占资源使用 `IResourceManager.Acquire` 获取 `IResourceLease`；服务使用 `IServiceBroker` 获取 `IServiceLease<T>`。

运行文件位于 `Plugins/<pluginId>/versions/<version>/`，只有安装器提交并写入 `state.json` 的 active version 才会被发现。插件 Config/State 数据与运行文件分离，卸载运行文件不会默认删除用户数据；`.staging`、未提交版本、无效 Manifest 和损坏状态不会进入工作区。

当前不提供签名验证、权限 enforcement、沙箱、商城或自动更新。进程外运行降低 Host 进程耦合，但不是完整安全沙箱。
