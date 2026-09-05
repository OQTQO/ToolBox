# 插件运行时

动态插件安装完成后进入 `Disabled`，用户可以在通用状态卡片中启用或停止插件；`runtime.background` 目前只用于描述和诊断。Host 通过 `OutOfProcessPluginRuntime` 启动 `ToolBox.PluginWorker`，Worker 加载插件并调用 `IPlugin.StartAsync`。停用时按同一个 `ShutdownDeadline` 调用 `StopAsync`、发送 Worker shutdown、等待退出、必要时终止进程树并释放句柄。

主要状态包括：`Disabled`、`Starting`、`Running`、`Stopping`、`DisableFailed`、`RestartRequired`、`Faulted` 和 `Quarantined`。停止失败保留故障状态，不会伪装成 `Disabled`。

插件程序集、依赖和后台任务都在独立 Worker 进程内。Host 不加载插件类型，也不接受插件注入 WPF 页面；它只渲染 SDK 定义的通用 `IPluginUiProvider` 数据。插件可选提供状态值、按钮和键鼠输入区域，按钮/输入通过 Worker 转发，返回的新快照再刷新工作区。未提供 `IPluginUiProvider` 的插件仍可作为纯后台插件运行。

Worker 控制管道只允许当前用户连接。每条 JSON Lines 控制消息最多 1,048,576 个字符；Host 和 Worker 都会在反序列化或发送前执行同一限制，超限返回 `WORKER_MESSAGE_TOO_LARGE`。

Worker 的控制消息读取与插件请求执行相互独立，但同一 Worker 仍只串行执行一个插件请求。Host 取消请求时会发送带原 `requestId` 的 `Cancel`；Worker 把取消令牌传给插件的启动、停止、UI Action 和输入调用，并以 `WORKER_REQUEST_CANCELLED` 结束该请求。Host 在收到终止响应后才复用通道；插件若在 1 秒内不响应取消，Worker 会被终止，插件进入 `RestartRequired`。

通用 UI 请求默认必须在 15 秒内完成；超时会先走上述取消流程，随后终止 Worker，把插件标记为 `RestartRequired`，并允许用户重新启动恢复。同步 `GetSnapshot()` 和忽略取消令牌的插件代码无法被协作式抢占，仍由进程边界提供确定性兜底。

`IPluginContext.LifetimeScope` 的 Token 会在插件卸载时取消。通过 `Track(Task)` 追踪后台任务，通过 `Register(IDisposable)` 或 `Register(IAsyncDisposable)` 注册资源释放。独占资源使用 `IResourceManager.Acquire` 获取 `IResourceLease`；服务使用 `IServiceBroker` 获取 `IServiceLease<T>`。

运行文件位于 Host 数据目录的 `Plugins/<pluginId>/versions/<version>/`，正常安装目录布局为 `<安装目录>\Data\Plugins\...`；只有安装器提交并写入 `state.json` 的 active version 才会被发现。插件 Config/State 数据位于 `<安装目录>\Data\PluginData\...`，与运行文件分离，卸载运行文件不会默认删除用户数据；`.staging`、未提交版本、无效 Manifest 和损坏状态不会进入工作区。

Host 会在首次启动时把旧安装目录和 `%LocalAppData%\ToolBox` 中的插件、日志及设置复制到新的 `Data` 目录。迁移以目标文件为准，不删除旧目录；插件不得把用户数据写到运行程序集目录，应使用 SDK 提供的插件上下文和数据服务。

实现 `IPluginUiProvider` 后，详情页默认进入“操作”页；纯后台插件默认进入“概览”页。插件只返回 SDK 数据契约，不能传递 WPF、XAML、HTML 或自定义页面。

安装前必须通过 Manifest v2 能力校验、package format 2 哈希和发布者签名验证，并满足本地 TOFU 信任绑定。进程外运行降低 Host 进程耦合，但仍不是完整权限沙箱；当前不提供商城或自动更新。

## 发布包烟雾验证

`ToolBox.Host.exe` 支持无窗口烟雾模式：重复传入 `--smoke-test-package <path>`，并通过 `--smoke-test-worker`、`--smoke-test-root`、`--smoke-test-result` 指定隔离 Worker、工作目录和 JSON 结果。该模式对每个真实 `.tpk` 执行安装、Worker 启用及 UI 快照、停用和卸载，任一阶段失败都会返回非零退出码。它复用 Host 的通用运行链路，不包含具体插件 ID 或类型。
