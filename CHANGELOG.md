# CHANGELOG

本文件记录 **PCL2 → DeepSeekHarness 启动器** 魔改版本的全部变更。
版本号规则见 `DEVNOTES.md` §6：修 bug 进补丁位，加功能进次版本位。

---

## [v0.1.0] — 2026-09-24

### 新增
- `DEVNOTES.md`：项目目标、DSH 外部事实速查、PCL2 源码导航、踩坑清单、进度表。
- `tools\backup.ps1`：源码快照脚本（zip + git tag）。
- `CHANGELOG.md`：本文件。
- `.gitignore` 追加：`/backups/`、`*.bak`、`/DSH/`。

### 变更
- 无源码改动。

### 工程
- 克隆 `MeloongCore` git 子模块（原先为空目录，导致源码无法编译）。
- 在 `PCL-2.13.1.1\` 初始化 git 仓库，建立基线提交 `chore(baseline)`。

### 已核实的关键事实（供后续版本参考）
- DSH npm 包 `@deepseek-ai/dsh`，共 26 个版本，仅 alpha / rc，无 stable。
- GitHub 仓库 21 个 release 全部无附件；版本列表需走 GitHub API 或 npm registry。
- dsh 的 npm tarball 不含依赖，安装必须走 `npm install`。
- 隔离靠环境变量 `DSH_HOME`；`dsh web` 默认端口 3080，支持 `--port` / `--no-open`。
