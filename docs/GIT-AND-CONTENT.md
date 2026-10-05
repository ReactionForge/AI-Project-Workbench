# Git来源与项目内容恢复
project_id: ai-project-workbench
revision: v0.4.0
role: observed_implementation
verification: needs_verification（完整外部桌面输入未验收）

Git仅读所属沙盒项目repo/.git，不搜索父仓库，不初始化、提交、切换、清理、推送或访问账户。真实目录及元数据不得包含链接、commondir、alternates、外接worktree配置或include/filter配置。系统/全局Git配置禁用，fsmonitor和hooks关闭，可选写锁关闭；配置在检查时持只读共享锁。仅调用固定只读命令。已有Git 2.53.0.windows.3足够，无安装。

任务Scope为repo或repo/<相对目录>时包括该范围，RelatedFiles的repo路径并入精确选择。符号模块标签只保留明确登记文件；没有路径时只核对分支/提交，不宣称扫描全部代码。status --porcelain=v1 -z和ls-files --stage -z解析真实工作树与index；包含clean、dirty、staged、untracked、删除及重命名记录、文件SHA。Git忽略文件不纳入；缓存/疑似凭据不读取正文，列EXCLUDED。子模块/链接未开放。两次读取不一致显示changed-during-read；不可用显示unavailable，不能当干净。未提交仓库显示unborn，未配置显示not-configured。

来源锚点加入Git快照指纹，分支/提交/index/范围内文件变化使旧包过期或证据需重核。范围外未登记dirty文件不触发；新提交仍要求重新核对版本。RecordEvidence记录当时真实提交，旧v0.3任务的unknown历史字段保持原值，未迁移。文件关联verified仍只是哈希关联，不等于完整语义验收。

内容备份先预览包含/排除项及登记引用问题，再确认创建唯一ZIP。仅递归选中项目自身目录：源码、Markdown事实及历史、外层输入、assets、data、runs证据、releases、archive安全内容；保持相对路径和空目录。排除.git、代理私有/凭据目录、依赖/缓存、临时/导出输出、写锁与未提交临时文件、常见私钥和.env等；小型常见文本另作保守敏感检测，不记录检测到的值。规则不能识别所有隐蔽凭据，也不是恶意外部写入隔离系统。若现行事实文档被排除/缺失，禁止创建声称完整的内容包；其他遗漏明确进入清单。任意Markdown嵌套链接不自动解析；任务文件/证据和路径形式知识来源检查，绝对引用保留原文提示核对，不读取外部内容。

清单包含project_id、demo、版本、现行SourceStamp、所有文件路径/字节/SHA、目录、排除与引用问题、备份时各任务Git来源摘要。Git对象历史未包含，摘要不等于Git仓库备份。无密钥或签名，SHA可验证内容一致，不能证明外部档案作者身份。每文件128MiB、总展开1GiB、5000文件/5000目录；超过即停止，不隐瞒部分失败。预览后源变化拒绝；ZIP候选生成后复查源和ZIP所有条目再宣布落盘。

恢复仅支持本工作区创建的内容ZIP。UI列表来自本Root/runs/exports，无任意目录浏览。预览校验schema、身份、数量、SHA、大小和安全路径；拒绝绝对/..路径、Windows设备/ADS/尾点空格、Unicode非规范路径、大小写重复、符号链接、文件/父目录冲突、缓存/凭据路径及疑似凭据内容。实际展开数量/字节也受限。所有哈希是完整校验，不是仅看清单的mock。

确认恢复后先写runs/restore-staging的新候选，将事实源加载并核对版本/SourceStamp，再同盘Directory.Move到runs/restored/restore-<guid>。原项目与ZIP不覆盖；冲突拒绝，失败候选保留以便检查，不自动删除。恢复缺.git，旧Git证据须重新核对；不会谎称恢复提交/远端/账户。未模拟真实断电或恶意并发路径替换。

恢复窗口可编辑自己的新副本。关闭后使用顶部“恢复与副本”→“打开恢复副本”，无需先创建项目；或以原生exe --workspace <本Root/runs/restored/restore-guid>启动。recovery.json记录来源和模式，不复制现行知识；仅接受这个限定层级和有效标记/现行事实。示例仍示例，本地不混入。原v0.2.1/v0.3.0及既有App数据不自动迁移。

依据：稳定机器格式与NUL路径见[Git status](https://git-scm.com/docs/git-status)，索引格式见[Git ls-files](https://git-scm.com/docs/git-ls-files)，只读可选锁变量见[Git环境变量](https://git-scm.com/docs/git)。产品限制与安全守卫是本项目实现，不声称Git替本App提供完整沙箱。
