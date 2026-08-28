# 维护者架构

## 边界

- `src/ToolBox.PluginSdk`：稳定 API v1，仅包含第三方需要的接口、Manifest、生命周期、资源和服务 Lease。
- `src/ToolBox.Core`：安装器、安全 ZIP、active version 目录、Manifest 发现、进程内底层验证和进程外 Worker 会话。
- `src/ToolBox.PluginWorker`：最小协议宿主，加载指定插件目录并转发 start/stop/shutdown。
- `src/ToolBox.Host`：WPF 状态卡片、设置、托盘和事件流，不引用 Sample 类型、页面或 Plugin ID。

## 动态发现

安装器在 `Plugins/<id>/versions/<version>` 写入运行时文件，并在根目录提交 `state.json`。`InstalledPluginCatalog` 只遍历插件根目录，排除 `.staging`，解析 committed active version，再交给 `PluginDiscovery` 校验 Manifest。单个坏根目录变成诊断事件，不能阻止 Host 启动。

## 演进规则

新增插件不应修改 Host。需要新增能力时先判断是否属于 SDK v1；自定义 UI、权限、商城、签名或自动更新都必须作为明确的平台设计单独推进。
