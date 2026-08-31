# Manifest v2

Manifest 必须位于 `.tpk` 根目录，`formatVersion` 必须是 `2`，`pluginApiMajor` 必须与 SDK 主版本兼容。旧 Manifest v1 不兼容。

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `formatVersion` | number | Manifest 格式版本，当前为 `2`。 |
| `id` | string | 稳定 Plugin ID，也是安装目录名，必须是安全路径段。 |
| `name` | string | 状态卡片显示名称。 |
| `version` | string | 插件版本；必须与 `package.json.pluginVersion` 相同。 |
| `pluginApiMajor` | number | SDK API 主版本，当前为 `1`。 |
| `publisher` | string | 稳定发布者 ID，必须与包签名完全一致。 |
| `platform.os` | string | 当前支持 `windows`。 |
| `platform.arch` | string | 当前支持 `x64`。 |
| `runtime.supportedModes` | array | 第三方动态插件必须包含 `outOfProcess`。 |
| `runtime.preferredMode` | string | 当前应为 `outOfProcess`。 |
| `runtime.background` | boolean | 后台插件标记，目前只用于描述和诊断。 |
| `capabilities` | array | 至少一项平台定义的能力声明。 |
| `capabilities[].id` | string | 软件平台定义的能力 ID，未知 ID 会被拒绝。 |
| `capabilities[].required` | boolean | 插件缺少该能力时是否不能工作。 |
| `capabilities[].reason` | string | 512 字符以内的使用理由。 |
| `entryPoint` | string | `Namespace.Type, AssemblyName`。 |

```json
{
  "formatVersion": 2,
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
  "capabilities": [{
    "id": "host.background.execution",
    "required": true,
    "reason": "Runs scheduled plugin work while enabled."
  }],
  "entryPoint": "Example.Plugin, Example"
}
```

Manifest 通过后还会验证 package format 2 哈希、`signature.json`、证书有效期与本地发布者信任绑定。签名和能力的完整规则见 [`plugin-security.md`](plugin-security.md)。
