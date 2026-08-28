# Manifest v1

Manifest 必须位于 `.tpk` 根目录，`formatVersion` 必须是 `1`，`pluginApiMajor` 必须与 SDK 主版本兼容。

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `formatVersion` | number | Manifest 格式版本，当前为 `1`。 |
| `id` | string | 稳定 Plugin ID，也是安装目录名，必须是安全路径段。 |
| `name` | string | 状态卡片显示名称。 |
| `version` | string | 插件版本；必须与 `package.json.pluginVersion` 相同。 |
| `pluginApiMajor` | number | SDK API 主版本，当前为 `1`。 |
| `publisher` | string | 发布者描述，不是身份认证。 |
| `platform.os` | string | 当前支持 `windows`。 |
| `platform.arch` | string | 当前支持 `x64`。 |
| `runtime.supportedModes` | array | 第三方动态插件必须包含 `outOfProcess`。 |
| `runtime.preferredMode` | string | 当前应为 `outOfProcess`。 |
| `runtime.background` | boolean | 后台插件标记，目前只用于描述和诊断。 |
| `entryPoint` | string | `Namespace.Type, AssemblyName`。 |

```json
{
  "formatVersion": 1,
  "id": "com.example.plugin",
  "name": "Example Plugin",
  "version": "1.0.0",
  "pluginApiMajor": 1,
  "publisher": "example.com",
  "platform": { "os": "windows", "arch": "x64" },
  "runtime": {
    "supportedModes": ["outOfProcess"],
    "preferredMode": "outOfProcess",
    "background": false
  },
  "entryPoint": "Example.Plugin, Example"
}
```

Manifest 通过后会经过 `.tpk` 基础结构检查和安装事务处理。`publisher` 当前只是显示字段，不做身份认证。
