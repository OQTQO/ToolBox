# ToolBox Plugin API v1

`ToolBox.PluginSdk` 的根命名空间公共面由 `PluginContract.PluginApiMajor = 1` 标识。第三方应只依赖 NuGet 包，不引用 Host、Core 或 Worker。

稳定类型包括 `IPlugin`、`IPluginContext`、`IPluginLifetimeScope`、资源/服务 Lease、`PluginManifest`、`PluginManifestParser`、`PluginLifecycleState` 和 `PluginState`。接口形状、枚举数值、常量、Manifest JSON 字段名及 API 不兼容错误码由兼容性测试锁定。

v1 不删除或重命名公共成员，不改变参数/返回类型、枚举数值和生命周期语义。新能力使用新的兼容契约，不把 Host/Core 类型泄漏到 SDK。`IPluginUiProvider` 是可选的附加契约：不实现它不会影响已有纯后台插件；实现后可提供 Host 通用渲染的状态项、操作按钮和键鼠输入区域。

## 通用插件 UI 契约

`PluginUiSnapshot` 的原有四参数构造函数、`PluginUiAction`、`PluginUiValue` 和 `PluginInputSurface` 保持不变。0.6.0 新增的可选字段是 `Elements`、`Status` 和 `Dialog`，因此旧插件返回的 JSON 不含这些字段时仍按空集合/空值处理。

`PluginUiElement` 是 Host 的通用数据描述，`Kind` 可为 `Value`、`Action`、`Menu`、`Select`、`MultiSelect`、`Toggle`、`CheckBox`、`RadioGroup`、`TextBox`、`NumberBox` 或 `Slider`。选项使用 `PluginUiOption`，菜单使用 `PluginUiMenuItem`；`Group` 只影响 Host 插入分组标题，不改变声明顺序。标准 `PluginUiCommand` 覆盖刷新、搜索、扫描、连接、保存、导入导出、播放、静音和音量等常用命令，Host 负责本地化文字和图标；插件通过 `CommandTarget` 提供设备名、文件名等目标文本。

控件交互仍走 `ExecuteAsync(string actionId, string? argument, CancellationToken)`：

- 单值直接传 `Value`；多选传 JSON 字符串数组。
- 开关和复选框传小写 `true` 或 `false`。
- 数字框和滑块使用 `InvariantCulture` 的数字文本。
- 下拉、多选、开关、复选框、单选组和滑块默认立即提交；文本框、数字框默认在失焦或 Enter 时提交。
- 没有 `ActionId` 的交互元素由 Host 禁用或忽略，不会使 Host 崩溃。

`PluginUiStatus`/`PluginUiProgress` 描述信息、警告、错误、成功、忙碌、确定/不确定进度和取消动作；`PluginUiDialog` 描述信息、警告、错误或确认窗口。Host 自己创建 WPF 模态窗口，插件只提供标题、消息和普通 `PluginUiAction` 数据。

### 元素、命令和提交语义

| 契约字段 | 可用值/行为 |
| --- | --- |
| `PluginUiElement.Kind` | `Value`、`Action`、`Menu`、`Select`、`MultiSelect`、`Toggle`、`CheckBox`、`RadioGroup`、`TextBox`、`NumberBox`、`Slider`。未知值安全忽略。 |
| `PluginUiActionStyle` | `Default`、`Primary`、`Secondary`、`Compact`、`Icon`、`Destructive`。图标按钮仍必须提供普通 `Label`，Host 用于无障碍名称和悬浮提示。 |
| `PluginUiUpdateMode` | `Default`、`Immediate`、`Commit`；默认选择类控件立即提交，文本/数字框在失焦或 Enter 提交。 |
| `PluginUiStatusKind` | `Information`、`Warning`、`Error`、`Success`、`Busy`、`Progress`、`Cancelled`。 |
| `PluginUiDialogKind` | `Information`、`Warning`、`Error`、`Confirmation`。 |

标准 `PluginUiCommand` 由 Host 提供中文标签和图标，插件只传 `CommandTarget`：

```text
刷新 重试 搜索 扫描 启动 停止 暂停 继续 取消
连接 断开 重连 保存 应用 重置 添加 删除 复制 导入 导出
打开 关闭 设置 帮助 更多 播放 上一首 下一首 快退 快进
静音 取消静音 音量增加 音量减少
```

如果标准命令不适合，使用 `Custom` 并提供插件自己的 `Label`。动作和菜单项的 `Argument` 会原样传给 `ExecuteAsync`；多选的 `Values` 由 Host 编码为 JSON 数组。文本内容始终按普通文本处理，Host 不解析 XAML、HTML 或脚本。

需要主动推送 UI 的插件可额外实现：

```csharp
public interface IPluginUiUpdateSource
{
    event EventHandler<PluginUiSnapshotUpdatedEventArgs>? SnapshotUpdated;
}
```

Worker 通过同一控制通道发送 `ui.updated` 事件；协议主版本仍为 `1`。高频进度只保留最新值，普通响应、错误和取消结果不丢失。未知控件会安全忽略，未知命令回退为 `Custom`，未知样式、状态和更新模式回退为默认值。

所有 UI 类型只包含字符串、数字、枚举、列表和数据对象，不引用 WPF、XAML、HTML、脚本或 Host 类型。

Keyboard/Audio 的专用契约不属于 SDK，已经随各自实现放入独立的 `ToolBox-Plugins` 仓库；它们不是第三方稳定 API。
