# 插件包安全契约

ToolBox 0.4 只安装 Manifest v2、package format 2 的已签名 `.tpk`。插件必须使用目标软件发布的 .NET 10 SDK/DevKit 重新构建，旧包不兼容。

## 包结构

```text
manifest.json
package.json
signature.json
runtime/...
```

`package.json` 包含 Manifest 和 runtime 的 SHA-256 清单。`signature.json` 使用 `rsa-sha256` 对 `package.json` 原始字节签名，并携带 DER 证书、公钥签名值、发布者 ID 和固定 payload 名称。证书必须处于有效期，发布者 ID 必须与 Manifest 完全一致。

## 信任决策

安装器采用 TOFU（首次使用时信任）：首次主动安装通过密码学验证的发布者会建立 `publisherId → certificateSha256` 绑定；之后同名发布者换证书会被阻止。信任库位于插件数据根下的 `.platform/trusted-publishers.json`，不随插件版本卸载。

换钥必须由未来明确的迁移命令完成，不能通过普通插件升级静默替换。泄露或撤销处理当前依赖本地 blocked 策略和软件发布更新，尚未提供在线吊销服务。

## 能力声明

Manifest 的 `capabilities` 是必填数组，每项包含平台定义的 `id`、`required` 和面向用户的 `reason`。未知能力直接拒绝，插件仓库不能自行扩展平台权限词汇。

能力声明目前提供审查与策略数据，不是操作系统沙箱。Worker 进程仍以当前用户身份运行；文件、网络、注册表和设备权限不会因为声明自动收窄。
