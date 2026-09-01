# 当前任务

状态：进行中。准备发布 ToolBox v0.5.0。

## 任务

- 编号：2026-09-01-release-0-5-0
- 目标：将已提交的 Host UI 最终版本按仓库发布流程创建 v0.5.0 Release。
- 仓库：ToolBox 软件仓库。
- 范围：生产版本号、当前版本文档、GitHub tag 和 Release 工作流；不修改插件协议或读取本地签名私钥。
- 验收：版本一致性通过，`main` 推送成功，`v0.5.0` 标签触发 Release，GitHub Release 资产生成并可下载。

## 决策

- v0.5.0 采用当前 `main` 的 Host UI 04 最终实现，UI 大幅重构适合使用 minor 版本递增。
- Release 资产由 `.github/workflows/release.yml` 和受保护的 GitHub Actions Secret 生成；本地不拼装签名 Release。

## 已完成

- 将 Host、Core、PluginSdk、PluginWorker 和 HelloPlugin 的发布版本统一为 0.5.0。
- 同步当前 README、插件开发文档、SDK README、发布文档和样例验证脚本。

## 验证

- 版本一致性校验：Host、Core、PluginSdk、PluginWorker 和 HelloPlugin 均为 0.5.0，SDK 包版本和样例 Manifest 一致。
- `dotnet build ToolBox.sln --configuration Release --no-restore -p:TreatWarningsAsErrors=true`：通过，0 警告、0 错误。
- `git diff --check`：通过；仅有既有的 LF/CRLF 转换提示。
- 待运行：GitHub Actions Release 工作流。

## 已知边界

- GitHub Actions 的签名 Secret、Windows runner 和 Release 资产生成依赖 GitHub 外部状态。
- 本地 `.audit-obj/`、`.dotnet-cli-home/` 缓存目录不属于发布提交。

## 下一步

1. 提交并推送 0.5.0 版本变更。
2. 创建并推送不可变标签 `v0.5.0`，等待 Release 工作流完成。
