# ADR-0004：强制签名包、TOFU 发布者信任与平台能力目录

状态：Accepted  
适用版本：ToolBox 0.3 / Manifest v2 / `.tpk` package format 2

## 决策

ToolBox 不兼容旧的未签名或未声明能力的插件包。Manifest v2 必须包含至少一个由软件仓库定义的能力 ID；package format 2 必须包含根 `signature.json`，使用 RSA-SHA256 对 `package.json` 的原始 UTF-8 字节签名。

`package.json` 继续列出 `manifest.json` 与全部 runtime 文件的 SHA-256；`signature.json` 不进入哈希清单，避免签名自引用。签名因此同时绑定包身份、Manifest、能力声明与运行文件。

## 发布者信任

- `manifest.publisher` 必须与 `signature.json.publisherId` 完全一致。
- Host 首次成功验证某发布者时，将发布者 ID 与证书 DER 的 SHA-256 指纹写入本地信任库；用户主动安装该包即构成首次信任操作。
- 同一发布者后续必须使用相同证书；未经迁移流程的换钥返回 `PACKAGE_PUBLISHER_KEY_CHANGED`。
- 本地策略可将记录设为 blocked，返回 `PACKAGE_PUBLISHER_BLOCKED`。
- 自签名证书可以建立本地 TOFU 连续性，但不等于公有 CA 或官方身份背书。官方 Release 必须通过受保护的 CI secret 使用稳定私钥签名。

## 能力声明

能力 ID 只能由软件平台目录定义。插件不能发明能力语义，也不能要求 Host 为具体插件增加例外。当前目录：

- `host.background.execution`
- `host.ui.input-events`
- `windows.bluetooth.audio-receiver`

声明用于安装审查、诊断和未来策略执行；当前 Worker 仍是进程故障隔离，不是 Windows 权限沙箱。不得把能力声明描述成已经限制了文件、网络、注册表或设备访问。

## 结果

旧 Manifest v1、package format 1、缺少签名、未知能力、签名篡改、发布者字段不一致和发布者证书突变均被拒绝。插件仓库必须升级到软件发布的 SDK/DevKit 与签名工具，冲突时插件服从软件。
