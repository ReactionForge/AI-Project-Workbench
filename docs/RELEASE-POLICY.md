# 发行策略
role: approved_workflow / proposed_automation
revision: RELEASE-1
当前仅建立文档规范，不启用 CI、签名、rulesets、自动发布或 GitHub Release。

## 小型项目适配
本项目采用“DEV 集成 → 冻结候选 commit → 检查与人工验收 → 用户决定合并 main → 核验最终 commit → draft 发行候选 → 核验全部资产 → 用户发布”。
[PowerToys](https://github.com/microsoft/PowerToys/blob/main/doc/devdocs/processes/release-process.md) 的候选回归清单与 draft 资产校验适合保留；不照搬其多人签收和全架构流程。借鉴 [VS Code](https://github.com/microsoft/vscode/wiki/Release-Process) 的候选冻结、smoke 与关键修复重验；本项目仍按用户的 DEV/main 规则，不改用它们的分支名称。

日常小变更可在短期 feature 分支审阅后进入 DEV；复杂并行不作为默认。大版本 DEV → main 独立用户决策，push 成功不算稳定发布批准。
若 merge/squash 改变 commit SHA，核对最终 main SHA 和 diff；不能把旧 DEV 的测试锚点伪称为最终 commit 已测试。必要时重建/重验。
优先验证一次后推广同一份字节与 SHA256；重复构建未必同字节。rc 改为 stable 若必须重新构建，重新验证，禁止沿用旧包 hash 或测试结论。

## 候选记录
记录版本、批准需求修订、源 commit、SDK/命令、依赖状态、测试输入与通过/失败/未运行项、文档版本、每项资产名称/字节/SHA256。示例见 examples/release-manifest.example.json。
SDK 固定在本 repo 的 global.json，不自动安装缺失 SDK。当前没有第三方 PackageReference，不虚构锁文件。未来引入依赖时按 [NuGet PackageReference](https://learn.microsoft.com/en-us/nuget/consume-packages/package-references-in-project-files) 审核锁文件和 locked-mode；锁更新与新增源须进精确 diff。

## 发行门禁
使用 RELEASE-CHECKLIST.md 和 MANUAL-WPF-MATRIX.md。必须写安装/运行要求、恢复方法、真实未验证项、兼容性及已知限制。外部键鼠/物理 DPI 未测时只能如实列为未验收，不能让内部渲染替代。
公开资产从审查过的公开 commit 构建，只含合法公开源码与必要运行文件。当前本地历史 ZIP 含私人交付资料，不公开上传到 GitHub；未来需另建安全发行候选，不改旧发行。
draft 完成后下载并校验每一资产的 hash，再经用户选择 publish。参考 [immutable releases](https://docs.github.com/en/code-security/concepts/supply-chain-security/immutable-releases)，启用此设置需另行确认。release attestation 与 build provenance 分开，不因上传 hash 就声称有供应链证明。
不能重写已发布 tag、覆盖旧资产或 force push main。缺陷通过用户批准的 hotfix/revert PR 和新 patch 版本处理，保留原发布记录。

## 后续 CI 与供应链（未启用）
仅文档示例 docs/ci/windows-validation.example.yml，不放到可触发 .github/workflows 中。启用前明确事件、hosted runner 成本/权限、资产范围和 secrets。
按 [GitHub 安全使用指南](https://docs.github.com/en/actions/reference/security/secure-use)：PR 使用只读权限、无发行 secrets；Action 审查后固定完整 commit SHA；不以 pull_request_target/workflow_run 执行不可信代码。绝不把用户电脑设为公开 PR 的 self-hosted runner。
[Artifact attestations](https://docs.github.com/en/actions/concepts/security/artifact-attestations) 与 SBOM 后置，另行批准写权限及执行方案；不生成签名凭据，不声称已签名、SLSA 等级或安全认证。WinGet/Store、自更新、自动合并后置。
