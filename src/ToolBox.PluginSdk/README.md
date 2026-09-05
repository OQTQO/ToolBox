# ToolBox.PluginSdk

ToolBox 第三方插件的稳定 Plugin API v1。插件只需要引用这个包，实现 `IPlugin`，再用 ToolBox 的 `.tpk` 打包脚本发布。

## 获取 SDK

SDK 不要求发布到 NuGet.org。插件作者应从 ToolBox GitHub Release 下载与 Host 匹配的 `ToolBox-PluginDevKit-<version>.zip`，并通过压缩包根目录的 `NuGet.config` 使用本地 `sdk/ToolBox.PluginSdk.<version>.nupkg`：

```powershell
dotnet restore --configfile ..\toolbox-devkit\NuGet.config
dotnet build --configuration Release
```

源代码开发者也可以在本仓库执行 `dotnet pack src\ToolBox.PluginSdk\ToolBox.PluginSdk.csproj -c Release`，但插件发布时必须固定并记录实际支持的 ToolBox/SDK 版本，不要依赖本机任意未发布构建。

Host、Core 和 PluginWorker 不属于 SDK 公共依赖。需要用户操作的插件可选实现 `IPluginUiProvider`，通过通用数据契约提供状态、按钮和输入区域，不需要引用 WPF。

0.6.0 开发版面向 .NET 10 并使用 Manifest v2：插件必须从 `PluginCapabilityContract` 选择并说明至少一个能力。最终 `.tpk` 还必须由发布者 RSA 私钥签名；签名工具、信任库和安装策略属于 ToolBox Core，不属于插件 SDK。

插件 UI 仍是可选能力。除原有 `Values`、`Actions` 和 `InputSurface` 外，`PluginUiSnapshot.Elements` 支持按钮、菜单、下拉单选、多选、开关、复选框、单选组、文本框、数字框和滑块；`Status` 支持状态、进度和取消动作；`Dialog` 支持由 Host 显示的确认窗口。插件只传输这些数据对象，不能引用 WPF、XAML、HTML 或 Host 类型。

交互统一通过现有 `ExecuteAsync(string actionId, string? argument, ...)` 回传：单值直接传字符串，多选传 JSON 字符串数组，开关/复选框传小写 `true` 或 `false`，数字使用不变文化格式。下拉、多选、开关、复选框、单选组和滑块默认立即提交；文本框和数字框默认在失焦或 Enter 时提交。

需要主动推送扫描、连接或进度变化的插件可以同时实现 `IPluginUiUpdateSource`。Worker 会通过 `ui.updated` 事件转发最新快照；该接口同样只包含 SDK 数据契约。
