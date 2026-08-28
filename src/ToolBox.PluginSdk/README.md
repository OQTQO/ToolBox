# ToolBox.PluginSdk

ToolBox 第三方插件的稳定 Plugin API v1。插件只需要引用这个包，实现 `IPlugin`，再用 ToolBox 的 `.tpk` 打包脚本发布。

Host、Core 和 PluginWorker 不属于 SDK 公共依赖。
