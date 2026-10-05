# AI 项目工作台
中文 Windows 原生桌面应用，管理独立项目、任务、知识来源、冲突和 AI 接手材料。当前是 v0.4 开发原型，尚未完成生产验收。

日常开发在大写 DEV；main 初始只有公开介绍和骨架。重大版本合并 main 由项目用户决定，不自动合并。见 [分支规范](docs/BRANCHING.md)、[GitHub 工作流](docs/GITHUB-WORKFLOW.md) 和 [发行策略](docs/RELEASE-POLICY.md)。

## 当前实现与边界
- 本项目内部独立项目目录、同源可编辑 Markdown、任务与知识管理、显式冲突、精简接手包导出。
- 来源/hash/证据过期重核、只读真实 Git 作用域事实。
- 内容备份、预览与确认后恢复到新副本、关闭后重新打开。
- 使用纯虚构合成夹具；不扫描用户其他项目，不接真实 AI/provider。
- 环境安装清理、远端 GitHub 自动化、后台同步、RAG、云同步、签名/自更新未实现。外部桌面键鼠/物理 DPI 未验收。

## 开发与运行
技术：C#/WPF，net8.0-windows，无第三方包。需要 Windows、已安装 .NET SDK 8.0.424（本 repo global.json 固定）、.NET8 WindowsDesktop Runtime、现有 Git。缺失工具时先报告，不自动安装系统组件。

代码 Git 根与本地项目数据根分开。先在自行选择的短路径 Projects 目录创建一个全新工作区，在其 repo 子目录克隆本仓库；不要复用其他项目根或覆盖已有文件：
1. 克隆当前仓库到新工作区的 repo，切换 DEV。
2. 确认工作区外层不存在 project.json，再把 repo/workspace-marker.json 复制为外层 project.json。
3. 只在外层创建 runs/temp 等输出目录。原始输入、数据、日志、缓存、发行目录在外层，永不加入本仓库。
4. 在 repo 用新 PowerShell 子进程执行 build.ps1；SDK缓存、临时、构建输出限定该工作区。不修改全局 PATH、Git 身份或系统设置。
5. 运行外层 releases/v0.4.0/AIProjectWorkbench.exe。默认空本地模式，示例需显式切换；完整路径结构和 project.json 不能丢失。

验证命令：同一 exe --self-test；内部 WPF 检查 --ui-smoke。均只创建本工作区 runs 新夹具，不把内部渲染当外部桌面输入。详见 [测试](docs/TESTING.md)、[基线](docs/VALIDATION-BASELINE.md)、[运行与恢复](docs/RUNBOOK.md)。

## 公开范围
只有经白名单审查的源码、合成夹具与必要开发规范。没有用户原始资料、实际数据、私人日志/截图、工具、缓存、签名 URL 或发行二进制。本地历史交付 ZIP 不属于公开仓库发行资产；今后发行须按公开安全候选流程重新准备。
