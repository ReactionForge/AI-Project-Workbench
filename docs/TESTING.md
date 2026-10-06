# 验证入口
在repo运行 .\build.ps1，默认构建runs/v5-build。脚本所有环境变量仅作用于子进程环境，不改全局设置。
领域与持久化：runs/v5-build/AIProjectWorkbench.exe --self-test。
WPF内部视图与渲染：同exe --ui-smoke。
应用测试入口定义在src/Program.cs的Tests和Program。测试夹具为runs/mvp-test-*；只使用本项目生成数据。UI测试新建runs/v5-ui-<guid>隔离根；不升级现有示例。示例模式渲染使用该夹具内data/examples，与本地空白模式分开。

证据：
- runs/v5-build.log：0错误/0警告。
- runs/mvp-tests.log：逐项夹具PASS/FAIL；不是真实AI或其他项目集成。
- runs/mvp-ui.log：WPF真实对象/事件处理器检查，内部渲染125/150/200%分辨率，不等于物理OS缩放。
- runs/mvp-overview.png、mvp-handoff.png、mvp-knowledge.png、mvp-empty.png（若生成）。
外部键盘、鼠标、真实取消交互、物理DPI切换：未运行。支持原生桌面输入的工具未提供；当前解锁状态未核验。不得通过其他后门或锁屏规避伪称测试通过。
首次UI测试硬编码列表顺序失败，随后固定按名称排序并复测。保留失败摘要；正式日志对应最终版本。

人工检查（需桌面可用）：新建空白项目→任务→新增批准要求/观察实现并关联冲突→取消未保存编辑→重开仍无取消内容→接手预览/导出→重启→备份和恢复确认→Tab与Esc→125/150/200%实际显示。安装、删除、现有项目导入仍不启用。

v0.3最终：65项领域/持久化检查、5项WPF内部视图检查通过。覆盖外部编辑、旧窗口拒绝、未知正文保留、未批准拒写、登记源码哈希变化/缺失、Windows非法别名、重复生成、初次/上一快照恢复。并发中途读取以首尾指纹拒绝混合版本；恶意ABA改回不在保证内。不是断电/真实代理/OS集成测试。

v0.4入口：GitAssetTests.cs。真实Git仓库init/add/fixture commit/branch变化仅在新runs测试根，生产功能只有只读命令。真实内容ZIP/文件IO验证素材、数据、输入、证据、空目录、现行身份、示例隔离、重开、取消、重复/冲突、预览过期、恶意路径/重复/链接/损坏、源事实不一致及失败候选保留。不是用户仓库集成或真实断电测试。
新增WPF证据：mvp-git.png、mvp-backup-preview.png、mvp-restore-preview.png。含实际控件与路由处理器检查，文件备份操作真实落盘；确认恢复对话框的外部输入未测试。当前原生CU工具缺失，桌面解锁状态未重新核验。
首轮失败选择了历史PROJECT而非CURRENT现行文档，夹具修正后复测；失败日志与说明保留v4-first-test-failure.log/v4-failure-repair.md。

公开基线见 VALIDATION-BASELINE.md。人工验收矩阵见 MANUAL-WPF-MATRIX.md。私人原始日志/截图不公开；本文件提供复现命令，不代称公开 CI 已运行。

本轮v0.5新增入口与边界见PUBLICATION-PLAN.md；开发输出runs/v5-build，封存的v0.5.0-rc.1便携候选在releases新目录，旧发行不变。原生自检使用PublicationTests真实本地Git夹具，--publication-check --online仅核对所属repo与已安装gh，不修改认证或远端。
