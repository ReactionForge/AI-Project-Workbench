# 技术决策
role: observed_implementation
revision: v0.4.0
2026-10-05。
- 原生 WPF/C#，net8.0-windows，复用现有 SDK8.0.424和WindowsDesktop8.0.30，无第三方包。已有引用包足以编译，避免重型框架与系统工具安装。代价是仅Windows、需现有.NET8运行时，无跨平台承诺。
- 当前开发分支的 repo/docs 是本App代码的唯一现行知识。外层 docs/inputs 是外部输入参考；原始输入不公开。
- v0.3取代JSON现行事实：CURRENT原子指向完整Markdown快照，App/AI共读正文；旧JSON保留历史且须本人显式升级。外部正文可编辑；未知段落阻止读取/覆盖并保留源文件。历史基线仅用于差异，不是现行真相。具体字段与恢复读MARKDOWN-CONTRACT.md。
- 写入使用本项目文件锁、期望版本、来源文件指纹、完整快照flush及CURRENT的File.Replace；上一版本可恢复。恢复前用户确认，先把当前文件保留到archive。错误输入留在表单。
- 路径只允许根目录内、拒绝链接目录/文件与越界。证据限所属项目runs。此约束只覆盖App功能，不能限制工作台外工具。
- 来源锚点覆盖当前任务目标/范围、项目目标、相关有效知识、规范与登记文件SHA256；证据SHA256独立核对。v0.4加入真实分支、提交、index、范围内dirty/untracked及文件SHA；未配置/不可用真实显示，限定任务scope及登记路径。
- 只读统计已登记的文档与必要历史文件大小；不递归其他项目、不跟随链接。
- 备份ZIP包含当前Markdown/上一快照/规范/差异基线/清单，明确不含资产证据。另有v0.4安全项目内容ZIP、预览与独立副本恢复，不覆盖原件；.git/缓存/凭据排除。
- .NET官方首启变量：GENERATE_ASPNET_CERTIFICATE=false、ADD_GLOBAL_TOOLS_TO_PATH=false；SKIP_FIRST_TIME_EXPERIENCE不再支持，不能用它实现隔离。见[微软文档](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-environment-variables)。本项目构建已显式纠正，私人环境证据不公开。

- v0.4继续复用现有Git2.53.0.windows.3。不调用远端/账户，不变更用户仓库；测试提交仅新runs夹具。只读命令与配置/路径约束见GIT-AND-CONTENT.md。
- 内容恢复选择新独立副本而非覆盖原项目，避免批准事实与现行数据被静默替换；先完整校验暂存，再同盘目录激活，失败保留候选。顶部恢复入口在空项目时也可用；只列本工作区备份/恢复，不浏览其他项目。
