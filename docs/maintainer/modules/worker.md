# PluginWorker 模块说明

## 职责

在独立进程中加载指定插件目录，建立控制通道，转发启动、停止、操作和关闭请求，并在异常时退出或报告错误。

## 主要入口

- `src/ToolBox.PluginWorker/Program.cs`
- `src/ToolBox.Core/Plugins/OutOfProcessPluginSession.cs`
- `src/ToolBox.Core/Plugins/Worker/WorkerProtocol.cs`
- `src/ToolBox.Core/Plugins/Worker/WorkerProcessLauncher.cs`

## 不变量

- Worker 不能反向依赖 Host UI。
- Worker 启动失败、崩溃、停止超时和强制终止必须可诊断。
- `StopAsync`、等待退出和释放资源必须使用同一 `ShutdownDeadline`。
- Worker 退出后不得留下插件残留进程或未释放的会话资源。

## 修改时检查

必须运行 Core 的 Worker child-process、Crash、Hang 和 Unload 测试，并检查 Release 输出包含 Worker 可执行文件。
