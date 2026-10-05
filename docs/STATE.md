# 当前状态
project_id: ai-project-workbench
version: v0.4.0
role: observed_implementation
lifecycle: active
verification: needs_verification（完整外部桌面验收未完成）
当前任务：tasks/T-002.md；开发分支：DEV。

App 已实现同源 Markdown/任务/知识/接手导出与文件持久化；只读 Git 分支/commit/index/dirty/untracked 范围快照；安全内容 ZIP、确认后新副本恢复与重开。没有实现远端推送或后台自动化。
历史验证基线：构建0错误0警告、领域118项、内部WPF12项；细节和源码关联见 VALIDATION-BASELINE.md。外部键鼠/实际DPI未运行。不是真实 AI/provider 集成或用户其他项目测试。
GitHub 和发行流程现已文档化；CI示例仅存 docs/ci，不触发 Actions。公开推送状态依据当次本地/远端 SHA 核验，不静态冒称始终同步；main重大合并、Release、签名及新权限仍需独立用户决定。
现有发行和本项目数据不自动迁移；私人证据及原始设计输入保留在非公开工作区。下一步完成已授权公开源提交与远端核验；App 功能仍以现行源码为准。
