# 同源Markdown约定
project_id: ai-project-workbench
role: observed_implementation
revision: v0.4.0
verification: needs_verification（外部桌面输入未验收）

每个受管理沙盒项目有repo/docs/CURRENT.md。该入口指定完整快照目录、文件列表、版本和原基线哈希；当前快照内PROJECT.md、STATE.md、tasks/*.md、knowledge/*.md是可编辑的现行事实。App每次加载直接解析这些Markdown，不另保存JSON正文副本。原始输入在外层docs，历史与差异副本在archive。其他快照不作为当前要求；入口和历史不能被AI自行切换。App自身产品设计源为本repo/docs，区别于受管理项目的实例。

字段约定：frontmatter每行key: JSON值，属于JSON兼容YAML。保留标题、wb字段标记、project_id、record_version和ID。允许编辑项目名称/目标正文；任务目标、已做、未做、下一步，以及owner/state/edit_scope/related_files等已登记元数据；知识正文及登记来源/角色/范围/生命周期/验证元数据。STATE元数据和固定事实入口段由App管理。未知字段、额外正文或不一致身份/版本会报错并保留原文，不能默默丢弃后覆盖。新增任务/知识先用App生成完整记录；任意Markdown格式、YAML多行扩展和文件增删尚不支持。JSON只用于内存克隆及旧版本显式升级输入。

外部编辑使现场SourceStamp改变。点击刷新后App读出同一新正文并显示待核对；普通保存拒绝，旧窗口不能生成接手包或记录证据。差异页展示原基线与当前全文（并排来源语义，暂非逐行彩色diff）。本人明确确认保存新基线；改变的知识进入needs_verification，最后验证时间独立保留。确认不是App自动重写要求或证明内容正确。AI只能提出待批准方案，不把改文件视为新的授权。保存期间外部变化和版本不一致保留输入并拒写；读取首尾指纹不一致拒绝混合版本。无文件系统事务或恶意ABA防护；外部AI也须顺序接力，旧AI停写。

一致性：创建完整新快照和差异基线并flush，再写准备日志format-v3.marker，再原子替换CURRENT（旧指针CURRENT.previous）。中断前未切换的快照不活跃；首次完整准备可显式恢复。恢复验证完整文件与身份，保留当前指针到archive。缺CURRENT且有v3标记不得回退JSON。准备日志是恢复证据，不是第二现行事实源。文件锁约束App写入；外部编辑者不受锁强制隔离。未模拟真实断电。

任务包关联PROJECT/STATE/当前任务和相关active知识，保留来源版本、现场SHA256与登记文件SHA/MISSING。related_files必须是所属项目相对普通路径，拒绝越界、链接及Windows别名/设备名；CURRENT/快照自动关联。仅读取用户明确登记文件和规范，不扫描代码树。v0.4只读Git纳入任务repo范围及登记路径，提交/分支/index/dirty/untracked来源变化触发过期；范围外/忽略文件不扫描。已有v0.3的历史worktree字段保留，当前事实由任务包重新读取；last_verified_code_commit仅在新证据确认时记录真实提交。证据哈希一致只表示关联有效，不表示测试语义通过。

旧JSON实例默认只读；显式升级生成薄模板与Markdown，保留旧JSON及README历史。缺规范修复仅创建缺失文件，保留自定义内容；规范内容变化仍需核对。
