# Keyboard & Mouse Test 产品范围

状态：最小产品化与首轮 hardening 已完成（2026-08-26）。

这是 Plugin API v1 冻结后的第一个正式产品插件候选。它从现有 KeyboardTest Architecture Spike 演进，但不修改已冻结的 `ToolBox.PluginSdk` 根命名空间公共面。

## 已完成的最小实现

- Package Installer 提供激活版本目录读取；Host 只加载 `state.json` 指向的当前版本，不再回退到手工复制目录。
- `Keyboard & Mouse Test` 使用 `.tpk` 安装路径完成 InProcess 加载、局部输入、设置、独占资源、Stop/Unload 和卸载验证。
- Host UI 已从 Architecture Spike 文案切换为正式产品路径，并明确呈现未安装、Disabled、Enabled、Faulted 和 Restart required 状态。
- Host 提供 `.tpk` 文件选择入口；已安装但未启用时可安装更新，运行中的插件不会被热替换。
- 产品验收覆盖真实包安装、激活版本解析、输入计数、设置生效、资源冲突、ALC 卸载和卸载后的激活状态清空。
- 激活状态只有在事务 `Committed` 时才允许被 Host 解析；事务未提交或激活目录缺失时 fail-closed。
- 产品包已验证 `0.1.0 → 0.2.0` 升级、卸载当前版本后回退到上一版本，并重新加载上一版本。

## 产品身份

- Plugin ID：`com.toolbox.keyboard-test`
- 首个运行模式：InProcess
- 首个运行平台：Windows x64
- 首个交互面：Host 自有的局部 WPF 测试区域
- 资源键：`keyboard.test.surface`
- 资源模式：Exclusive

## v0.1 最小用户路径

```text
安装 .tpk
↓
Host 发现当前激活版本
↓
用户进入 Keyboard & Mouse Test
↓
Enable
↓
在局部测试区域产生按键/鼠标事件
↓
显示最后输入、按键计数、鼠标计数和设置状态
↓
Disable
↓
确认 Stop、资源释放、ALC 卸载和 Disabled 状态
```

## 必须实现

- 从 Package Installer 的激活版本加载，不依赖手工复制 DLL。
- 通过 Plugin API v1 生命周期启动、停止、Dispose 和 ALC 卸载。
- 启用时独占 `keyboard.test.surface`；冲突必须显示为插件失败，不得伪装成 Disabled。
- 观察局部测试区域内的 key down、可选 key up、鼠标按钮和局部坐标。
- 支持 `IncludeKeyUpEvents`、`IncludeMouseEvents` 两个最小设置。
- 在 Host UI 中显示 Enabled、Stopping、Faulted、Restart required、Disabled 等真实状态。
- 设置和小型状态使用独立 PluginData 的 Config/State 目录；升级快照由 Phase 9 Installer 负责。
- 提供 InProcess 端到端测试：安装/发现、启动、输入、设置、资源冲突、停止、卸载和数据保留。

## 稳定边界

- 不新增或修改 `ToolBox.PluginSdk` v1 根命名空间接口。
- `ToolBox.PluginSdk.Experimental` 当前只表示兼容桥接区，不得被文档或包元数据宣称为稳定 API。
- 产品特定输入扩展若需要长期稳定承诺，必须单独提出兼容性方案；本范围不把它偷偷并入 API v1。
- Host 负责窗口、局部输入事件和 UI 线程切换；插件负责业务状态、设置和资源 Lease。

## 明确不做

- Low-level global hook、Raw Input、Native DLL。
- 复杂键盘布局、宏、录制回放、手势识别和输入注入。
- Android、Bluetooth、音频、远程控制或联网遥测。
- 第三方权限强制、安全沙箱、插件商城和生产 Updater。
- OutOfProcess 键鼠产品模式；只有在 InProcess 路径验收后另行评估。

## 产品化验收门槛

1. Package Installer 安装包并激活版本，Host 不读取未激活版本。
2. Enable 成功后资源 Lease 数为 1；第二实例启动得到明确冲突并进入 Faulted。
3. 局部区域事件能够更新计数、最后输入和设置摘要。
4. Disable 完成后资源 Lease 数为 0，ALC 不存活，状态为 Disabled。
5. Stop/Unload 失败时保留 `DisableFailed` 或 `RestartRequired`，不隐藏失败。
6. 完整 Release 构建、测试、Host 烟测和临时资源清理通过。
