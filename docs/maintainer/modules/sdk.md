# Plugin SDK 模块说明

## 职责

提供第三方插件唯一依赖：`IPlugin`、`IPluginContext`、Manifest v1、生命周期、资源 Lease、服务 Lease 和通用 UI 契约。

## 主要入口

- `IPlugin.cs`
- `PluginManifest.cs`、`PluginManifestParser.cs`
- `PluginLifecycleState.cs`
- `ResourceContracts.cs`
- `ServiceContracts.cs`
- `PluginUiContracts.cs`

## 不变量

- `PluginApiMajor = 1` 的公共契约保持兼容。
- SDK 不引用 Host、WPF、Core 或具体插件。
- 插件通过 LifetimeScope 管理资源和服务 Lease。
- 通用 UI 只传输协议数据，不允许插件注入 WPF 页面。

## 修改时检查

先更新兼容性测试和开发文档，再考虑 SDK 版本变更；必须验证 HelloPlugin 和独立插件仓库仅通过 NuGet 构建。
