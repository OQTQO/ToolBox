# ToolBox.PluginSdk

ToolBox 第三方插件的稳定 Plugin API v1。插件只需要引用这个包，实现 `IPlugin`，再用 ToolBox 的 `.tpk` 打包脚本发布。

Host、Core 和 PluginWorker 不属于 SDK 公共依赖。需要用户操作的插件可选实现 `IPluginUiProvider`，通过通用数据契约提供状态、按钮和输入区域，不需要引用 WPF。

0.5.0 面向 .NET 10 并使用 Manifest v2：插件必须从 `PluginCapabilityContract` 选择并说明至少一个能力。最终 `.tpk` 还必须由发布者 RSA 私钥签名；签名工具、信任库和安装策略属于 ToolBox Core，不属于插件 SDK。
