# v0.5：只读发布检查与审查计划
project_id: ai-project-workbench
role: observed_implementation
revision: v0.5.0-rc.1
verification: needs_verification（外部键鼠/物理DPI未运行）

入口：原生 App 左栏“发布计划”。当前沙盒项目与“工作台自身仓库”是显式不同范围，仅允许本 Root/repo 或 data/<当前模式>/Projects/<id>/repo。未选择、没有独立 .git、损坏/链接/共享/外部配置/过滤器/子模块/冲突索引均报错，不发现父级或其他目录。便携交付不带 .git，因此空仓库报错是正常边界。

1. 本地检查：真实分支/HEAD、index blob、工作文件SHA256、dirty/untracked、逐文件 index-vs-HEAD 与 working-vs-index diff。首尾两次来源指纹不一致则拒绝结果。忽略文件不枚举、不检查、不获得公开许可；它们不是本清单的发布内容。不要把这个检查当作完整历史秘密审计。
2. 联网核对（只读）：使用现有 gh 的 user/repo/ref API，验证账号、规范目标名称、可见性、push权限和真实分支SHA。不登录、不初始化、不 fetch、不修改 remote 或认证。未找到 gh 可登记已安装 gh.exe 完整路径；设置仅在本 Root/runs/publication，不改 PATH。凭据不记录、不显示。
3. 状态分维：工作区 committed_clean/uncommitted/unknown；远端 synced/committed_unpushed/behind/diverged/unknown。SHA相等不意味着工作区没有变化。祖先对象缺失、浅历史、查询错误或网络/授权失败均未知。保留最后成功观测的SHA/时间作为历史，不能冒称当前同步。观测只代表记录时刻，不是持续同步。
4. 目标确认：本项目元数据绑定（账号、仓库、分支、可见性、配置指纹），须五分钟内有成功只读观测；不等于发布授权。目标、账号或分支改变需重新核对。main没有执行入口。
5. 精确范围：勾选文件，index/working差异独立展开。凭据/私人路径/邮箱等可疑行隐藏；敏感文件名、原始输入/数据/证据/缓存/产物、5 MiB阈值、二进制/截断预览提示风险。保守规则会误报，也不能检出所有秘密。超过1000文件/25 MiB工作内容/单命令2M字符停止；不能把未读内容当安全。暂存 blob 同样检查，不只检查工作副本。
6. 本地领先时列实际 commit 范围和远端SHA到HEAD差异；所有可达历史blob尚未全面审计，计划仍需人工审查。
7. 可选证据：登记本 Root/runs 或 releases 相对文件，计算SHA256并绑定当前HEAD与工作区指纹；标记 unverified_manual_association，不自动解析日志为PASS、不推断构建出处。证据字节变化时拒绝导出。实际外部键鼠/DPI、push、main合并和Release仍显示not_run。
8. 导出前重查本地；旧预览或证据变化拒绝。唯一文件名 JSON写入本Root/runs/publication/<模式-项目或self>/plan-*，仅包含所选文件及脱敏diff。无绑定/未知远端/main/脏工作区/风险/历史待审/过期观测标 blocked_or_review_required。CanExecute 永远 false；没有任何提交、推送、PR、main合并或Release执行入口。

取消、返回、关闭和切换范围中断本操作子进程并忽略过期回调；已完成的审查文件保留。重开可读历史检查和绑定，历史一律标需重查。API失败不删除本地commit/绑定、不修改账户。读取期间恶意ABA改回、完整秘密识别、所有历史审计、用户任意仓库导入、实时文件监听、恢复Git历史和完整网络故障自动化测试不在保证内。

实现：src/Publication.cs、PublicationUI.cs、PublicationTests.cs；规范与代码均在repo同源。真实本地Git夹具仅在runs，远端状态纯函数/缓存/绑定合成输入明确不是真实GitHub集成。真实账号/目标API另用所属repo --publication-check --online，结果记录当时来源指纹和SHA。

文件勾选仅决定审查导出范围，Git push实际会传播整个可达提交历史；不可把勾选文件误当作部分推送能力。本轮没有push入口。
