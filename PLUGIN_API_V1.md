# ToolBox Plugin API v1 Freeze

冻结日期：2026-08-26。

Phase 10 将 `ToolBox.PluginSdk` 的根命名空间公共面冻结为 Plugin API v1。`PluginContract.PluginApiMajor` 和 Manifest `pluginApiMajor` 均为 `1`。

## 稳定公共面

以下类型属于 `ToolBox.PluginSdk` v1 稳定契约：

- `IPlugin`、`IPluginContext`、`IPluginLifetimeScope`
- `IResourceManager`、`IResourceLease`、`ResourceKey`、`ResourceAccessMode`、`ResourceConflictException`
- `IServiceBroker`、`IServiceLease<T>`
- `PluginContract`、`PluginExecutionMode`
- `PluginLifecycle`、`PluginLifecycleState`、`PluginLifecycleTransitionException`、`PluginState`
- `PluginManifest`、`PluginPlatform`、`PluginRuntime`
- `PluginManifestParser`、`PluginManifestParserOptions`
- `PluginManifestValidationError`、`PluginManifestValidationException`

`PluginApiV1CompatibilityTests` 会锁定这些导出类型、接口成员形状、枚举数值、常量、Manifest JSON 字段名和 API 不兼容错误码。

`tests/Fixtures/PluginSdkCompatibility` 还保留一个按固定 `ToolBox.PluginSdk` 0.0.1 reference 编译的 `LegacyPlugin.dll`。测试不会重新用当前 SDK 编译它，也不会把旧 SDK DLL 放进插件目录；当前 ALC 必须通过共享程序集加载它。

## 兼容性规则

Plugin API 1.x 必须保持二进制和语义兼容：

- 不删除、重命名或改变现有公共类型、接口成员、参数/返回类型、泛型约束或异常属性。
- v1 接口不追加必需成员；新能力使用新接口或新的 API major。
- 不改变现有枚举数值、`PluginContract` 常量和生命周期转移语义。
- Manifest v1 的 JSON 字段名、字段含义和 `PLUGIN_API_MAJOR_UNSUPPORTED` 错误码保持不变；可选新增字段必须保持旧解析器可读。
- `ToolBox.PluginSdk.Experimental` 是实验区，不属于 v1 稳定承诺；Keyboard & Mouse Test 与 Phone Audio Relay 当前只把各自 contract 作为版本耦合的产品兼容桥接，不把它们宣称为稳定 API。
- Core、Host、Worker、Package Installer 的实现可以演进，但不得把 Core/Host 类型泄漏到 PluginSdk 稳定契约。

## 已验证路径

冻结后兼容性验证已通过：旧版 LegacyPlugin DLL 能在当前 API v1 Host 中完成加载、启动、停止和 ALC 卸载。其他已验证路径包括 HappyPath、Crash、Hang、UnloadLeak、Worker child-process、Protocol mismatch、Keyboard & Mouse Test、Phone Audio Relay，以及 `.tpk` 安装/卸载和恶意包 Fixture。Updater、权限强制和安全沙箱仍不在当前范围内。
