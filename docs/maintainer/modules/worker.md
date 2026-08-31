# PluginWorker 模块说明

## 职责

在独立进程中加载指定插件目录，建立控制通道，转发启动、停止、操作和关闭请求，并在异常时退出或报告错误。

## 主要入口

- `src/ToolBox.PluginWorker/Program.cs`：最小进程入口。
- `src/ToolBox.PluginWorker/WorkerArguments.cs`：启动参数解析。
- `src/ToolBox.PluginWorker/WorkerEntryPoint.cs`：管道握手和控制循环。
- `src/ToolBox.PluginWorker/WorkerRequestHandler.cs`：插件请求执行和协议结果映射。
- `src/ToolBox.Core/Plugins/OutOfProcessPluginSession.cs`
- `src/ToolBox.Core/Plugins/Worker/WorkerProtocol.cs`
- `src/ToolBox.Core/Plugins/Worker/WorkerProcessLauncher.cs`

## 不变量

- Worker 不能反向依赖 Host UI。
- Worker 启动失败、崩溃、停止超时和强制终止必须可诊断。
- `StopAsync`、等待退出和释放资源必须使用同一 `ShutdownDeadline`。
- Worker 退出后不得留下插件残留进程或未释放的会话资源。
- 控制管道只允许当前用户连接。
- 插件 UI 请求必须有上限；超时后终止 Worker 并保留 `RestartRequired` 状态。
- 控制循环必须持续读取 `Cancel` 和 `Heartbeat`，不得被正在执行的插件请求占住；插件请求仍保持单请求串行执行。
- `Cancel` 只取消相同 `requestId` 的活动请求，并把令牌传入可取消的插件 API；Host 必须排空该请求的终止响应后再复用读取器。
- 单条 JSON Lines 消息的读写上限固定为 1,048,576 个字符，超限使用 `WORKER_MESSAGE_TOO_LARGE`，不得先无界读取再检查。
- 插件不配合取消时，Host 在 1 秒确认窗口后终止 Worker 并进入 `RestartRequired`；进程边界仍是最终兜底。
- 入口、参数解析、控制循环和插件请求处理保持分层；协议变更不得重新堆回 `Program.cs`。

## 修改时检查

必须运行 Core 的 Worker child-process、request cancellation、message limit、UI timeout、Crash、Hang 和 Unload 测试，并检查 Release 输出包含本次构建的 Worker 可执行文件。使用 `--artifacts-path` 时也必须验证 Worker 与 Fixture 从相同 artifacts 根目录复制，不能回退到默认 `bin/` 的旧产物。
