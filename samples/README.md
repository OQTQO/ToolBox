# ToolBox SDK 示例

这里保留一个最小的 `HelloPlugin`，只依赖 `ToolBox.PluginSdk`，用于验证第三方开发路径。

KeyboardMouse 和 AudioRelay 已迁移到独立的 [ToolBox-Plugins](https://github.com/OQTQO/ToolBox-Plugins) 仓库。它们不属于 ToolBox Host 内置代码。

在仓库根目录运行以下命令，可以生成本地 SDK 包、构建 HelloPlugin 并生成 `.tpk`：

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\Validate-PluginSamples.ps1
```
