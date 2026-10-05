# 启动与使用
直接运行 <本项目工作区>/releases/v0.4.0/AIProjectWorkbench.exe。
它是原生桌面App，没有浏览器或Web服务。依赖现有.NET8 WindowsDesktop；本机已确认8.0.30。无需管理员、安装器或新增SDK。便携包需整体解压，保留project.json及相对目录；仅拷exe会因缺少项目标记而拒绝启动。

正常启动为空白本地模式：
1. 新建项目：只填名称与目标，项目目录使用安全生成ID，不把用户名称当路径。
2. 新建任务：填目标、负责人、范围、状态与下一步。保存失败保留输入；关闭存在修改时选择保存/放弃/留在此处。
3. 知识与冲突：记录正文、来源、角色和适用范围。显式冲突对象用当前项目另一知识ID；历史默认隐藏，搜索只在当前项目。
4. 任务页预览接手→交接页检查→复制或导出。文件名使用ID/时间/随机后缀，不静默覆盖；路径在成功落盘后显示。
5. 验证证据需是当前沙盒项目runs内已有文件，只核对文件、哈希与锚点。无证据显示unknown，来源变化显示needs_verification。
6. 外部修改后点击“刷新项目文档”，核对原基线/当前文件差异。普通保存被阻止；本人明确确认才保存新基线。AI改文件不等于批准。受支持字段见MARKDOWN-CONTRACT.md。
7. 备份项目记录ZIP（不含资产/证据）；恢复上一版本先确认，当前文件被保留到该项目archive。
8. 查看独立示例会打开不同模式，返回本地模式时不会混入示例记录。

数据：data/local/Projects/<project-id>/repo/docs/CURRENT.md → snapshots/<snapshot>/PROJECT、STATE、tasks、knowledge Markdown。
示例：data/examples/Projects/...，全部虚构。
没有后台服务、账户或真实环境操作。关闭窗口即结束前台App。
开发重建：repo/build.ps1。所有工具缓存、构建、测试与渲染在本项目runs。

已知限制：外部桌面输入及实际DPI未验收；知识语义判断人工完成；Git仅任务范围/登记文件、独立.git；linked worktree/子模块不支持；恢复针对上一完整快照，首次完整准备可显式恢复；轻量记录ZIP不含资产/证据；完整内容ZIP见下；无其他项目导入/安装/删除/云同步执行。本版为有边界的单机MVP，不代表所有设计功能的生产实现。

v0.2.1保持原文件不变供回退参考。旧JSON项目仅显示升级预览，未经确认不迁移；新v0.3项目自动生成目录及AGENTS/WORKFLOW/TESTING薄规范。

内容备份：交接页→预览完整内容备份→核对包含/排除/登记引用→确认创建。ZIP位于本Root/runs/exports，源变化需重新预览。任何缓存/凭据/链接/Git元数据遗漏按清单公开；现行事实文档不可恢复时阻止创建。
恢复：顶部“恢复与副本”（本地空状态也可用）→预览ZIP恢复→选择本工作区ZIP→核对源ID、SHA、目标与排除→确认恢复到新副本。原项目、ZIP保留；失败候选在runs/restore-staging。关闭后“打开恢复副本”重开，或运行exe --workspace "<本Root>/runs/restored/restore-guid"。仅支持这个限定层级；示例模式保持隔离。绝对引用不自动改写，需要人工核对。
Git：任务Scope用repo或repo/<目录>，RelatedFiles登记精确路径；读取已有本项目repo/.git，不自动初始化/提交。暂无Git如实not-configured；提交/分支/index或相关内容变化需重新预览/核对。完整说明见GIT-AND-CONTENT.md。

公开源码的工作区准备和分支开发见 README.md；GitHub/发行规则见 GITHUB-WORKFLOW.md、RELEASE-POLICY.md。
