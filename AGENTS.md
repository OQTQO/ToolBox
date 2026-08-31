# ToolBox 软件仓库代理规则

开始工作前：

1. 若存在 `..\WORKSPACE.md`，按其中路由确认仓库范围。
2. 运行 `powershell -ExecutionPolicy Bypass -File .\tools\Get-ProjectContext.ps1` 读取任务摘要、HEAD 与未提交修改。
3. 只加载任务相关的 `AI.md` 章节、模块文档、ADR、源码和测试，不默认读取历史归档。
4. 严重中断恢复、架构审计或跨仓库协议升级时使用 `Get-ProjectContext.ps1 -Full`。

本仓库是 ToolBox 平台契约权威源。插件冲突时由插件适配软件；不得为具体插件 ID、类型或页面在 Host/Core/Worker 中增加专用分支。

完成任务时更新当前任务文件并执行与风险匹配的验证。未经用户明确要求，不提交、推送、发布或回滚已有修改。
