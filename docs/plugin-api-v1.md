# ToolBox Plugin API v1

`ToolBox.PluginSdk` 的根命名空间公共面由 `PluginContract.PluginApiMajor = 1` 标识。第三方应只依赖 NuGet 包，不引用 Host、Core 或 Worker。

稳定类型包括 `IPlugin`、`IPluginContext`、`IPluginLifetimeScope`、资源/服务 Lease、`PluginManifest`、`PluginManifestParser`、`PluginLifecycleState` 和 `PluginState`。接口形状、枚举数值、常量、Manifest JSON 字段名及 API 不兼容错误码由兼容性测试锁定。

v1 不删除或重命名公共成员，不改变参数/返回类型、枚举数值和生命周期语义。新能力使用新的兼容契约，不把 Host/Core 类型泄漏到 SDK。`IPluginUiProvider` 是可选的附加契约：不实现它不会影响已有纯后台插件；实现后可提供 Host 通用渲染的状态项、操作按钮和键鼠输入区域。

Keyboard/Audio 的专用契约不属于 SDK，已经随各自实现放入独立的 `ToolBox-Plugins` 仓库；它们不是第三方稳定 API。
