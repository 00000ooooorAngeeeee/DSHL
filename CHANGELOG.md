# CHANGELOG

本文件记录 **PCL2 → DeepSeekHarness 启动器** 魔改版本的全部变更。
版本号规则见 `DEVNOTES.md` §6：修 bug 进补丁位，加功能进次版本位。

---

## [v0.3.1] — 2026-09-24

**实机运行验证后的 bug 修复**。本轮真正启动了编译产物并跟日志排查，共发现并修掉 3 个会导致功能不可用的缺陷。

### 修复
1. **启动页「新建整合包」按钮点了没反应（死循环）**
   实机日志证据：
   ```
   按 下 按 钮：新建整合包
   普通弹窗：无法启动 → 还没有任何整合包，请先在「启动」页新建一个……
   ```
   `RefreshDshButtonsUI` 在没有整合包时把按钮文案设为「新建整合包」，但 `LaunchButtonClick`
   没有对应分支，落到 `DshLaunchStart(Nothing)` 弹出"无法启动"。现在按钮文案即行为：
   「下载 dsh」→ 版本下载页、「新建整合包」→ 新建向导、「打开 DeepSeekHarness」→ 再开浏览器、
   其余 → 启动。并加了兜底分支。
2. **profile 初始化命令错误，必然失败**（`DshEnsureProfile`）
   原来是 `--from-default-profile <name> --dump-config`，实测报：
   `dsh: profile "web" is shipped and cannot be a custom profile target; omit --from-default-profile to use it`。
   `web` / `headless` 是官方内置 profile，不能用 `--from-default-profile` 作为自定义目标。
   正确做法是直接 boot（会从内置模板自动初始化），改为 `--profile <name> --dump-config`，
   实测能生成 `package.json` / `cordis.yml` / `cordis.patch.yml` / `pnpm-workspace.yaml`。
3. **`DshRunInfo` 把非零退出码当失败**（`ModDshHome`）
   实测 `dsh --profile web --dump-config` **输出正常但退出码为 1**。原实现会抛异常，
   导致 profile 初始化每次都走不到"成功"分支。现在只记录警告，成功与否交给调用方按产出文件判断。

### 变更
- 首次启动引导不再强制创建整合包（原来会在引导里弹出创建向导），改为「Node → dsh 版本」两步，
  整合包交由启动页按钮创建，流程更线性、也可随时中断。
- `ModBase.vb`：版本号 `0.3.0` → `0.3.1`。

### 实机验证记录（本轮已确认可用）
- 编译产物可正常启动：进程稳定、主窗口出现（`Plain Craft Launcher　`，900×550）、无异常日志。
- `ModDshInstance` 整合包扫描加载器正常工作（`整合包列表加载完成，共 0 个`）。
- 首次启动引导弹窗正常触发并能走完流程。
- 版本列表数据链路实测：npm 26 个版本 + GitHub 21 个 release → 合并 **28 个版本**，
  分类 alpha 13 / rc 15 / stable 0，发布时间倒序正确（最新 `0.1.7-rc.1` = 2026-09-23 21:30 本地时区）；
  自动识别出 2 个「GitHub 有 tag 但 npm 未发布」的版本并会在界面上标注为不可安装。
- npm 安装命令实测通过：官方源 584 包 / 国内镜像 585 包，dry-run 54s / 44s，
  真实安装 `0.1.7-rc.1` 用时 62.7s（512 包），`lib\bin.js` 入口存在且可执行。
- **插件开关实现所依赖的 patch 格式得到官方 dump 印证**：
  `dsh --dump-config` 输出正是 `- id: xxx` / `name: '@deepseek-ai/...'` / `disabled: true` 结构。

### 仍待验证
- 实际启动一个整合包（`dsh web`）与浏览器自动打开的全链路。
- 技能/插件开关的实际生效效果。
- `PageDshManager` 在 UI 线程调用 `WaitForExit()` 是否会卡顿。

---

## [v0.3.0] — 2026-09-24

### 新增（DSH 基础设施，7 个新模块）
- `Modules\DSH\ModDshBase.vb`：DSH 根目录体系、Node 运行环境检测（node.exe/npm.cmd/系统 PATH 回退）、
  dsh 版本安装路径与已安装扫描、版本号排序与 alpha/rc/stable 分类、端口探测与空闲端口分配、
  子进程环境变量装配（`DSH_HOME` / `PATH` / `BROWSER` / `DSH_TELEMETRY_DISABLED`）、日志出口、名称校验。
- `Modules\DSH\ModDshInstance.vb`：整合包（实例）数据模型 `DshInstance` 与清单 `instance.json` 的读写、
  创建/删除（回收站）/从现有 DSH_HOME 导入（技能 + profile + 凭证 + 工作区注册表）、实例列表扫描加载器。
- `Modules\DSH\ModDshNet.vb`：DSH 全部历史版本获取。以 GitHub Releases 的 `published_at` 为权威发布时间，
  用 npm registry 的 `versions`/`time` 补齐 GitHub 缺失的版本；失败自动降级；输出按发布时间倒序并分类。
- `Modules\DSH\ModDshInstall.vb`：Node.js 一键安装（npmmirror 索引选版本 → 下载 zip → 解压部署到
  `DSH\runtime\node\`）、dsh 本体安装（临时目录 `npm install --prefix` → 校验 `lib\bin.js` → 部署到版本仓库
  → 写 `.dsh-installed` 标记）、版本卸载（被整合包占用时拒绝）。
- `Modules\DSH\ModDshHome.vb`：整合包 DSH_HOME 内部管理。profile 初始化（优先 `--from-default-profile`，
  失败回退内置模板）、技能扫描（目录 bundle 与平铺 md、frontmatter 解析）、技能开关（改名实现，dsh 扫描器
  天然忽略）、插件扫描（profile package.json + node_modules + `cordis.patch.yml` 的 disabled 记录）、
  插件安装/卸载（走 `dsh plugin` → pnpm）、插件开关（写 profile 的 patch 层）、带超时与取消的 CLI 执行器。
- `Modules\DSH\ModDshLaunch.vb`：启动链路。校验整合包/Node/dsh 版本 → 端口占用时直接复用并在浏览器打开
  （幂等）→ `node <版本>\lib\bin.js --profile web --host 127.0.0.1 --port N --no-open` → 抓 stdout 里的
  GUI URL（正则）或轮询端口就绪 → **用系统默认浏览器打开**（需求 1）→ 进程与输出缓冲托管、关闭 dsh。
- `Modules\DSH\ModDshSetup.vb`：首次启动引导（欢迎 → Node → dsh 版本 → 新建整合包，逐步骤可跳过）、
  新建整合包向导（名称/绑定版本/是否导入现有 DSH_HOME/工作区）、校验规则用 `ValidateFunc` 实现。

### 新增（UI）
- `Pages\PageDownload\Dsh\PageDownloadDsh.xaml(.vb)`：下载页新增「DSH 版本」分类。
  Alpha / RC / 其他三个分组，组内按发布时间倒序，每条标注本地时区的发布时间、类型、安装状态；
  点击条目可安装、安装并绑定到当前整合包、卸载、查看更新说明。右侧刷新按钮与左侧列表刷新联动。
- `Pages\PageSetup\PageSetupDsh.xaml(.vb)`：设置页新增「DSH 运行环境」（需求 5）。
  运行环境状态卡片（Node 版本、已安装 dsh 版本、仓库与整合包路径）+ 一键下载 Node /
  跳转版本下载页 / 打开版本仓库；Node.js 位置、dsh 本体位置、npm 源可编辑；
  启动行为（自动开浏览器、退出时结束进程、遥测、默认版本）。
- `Pages\PageInstance\Dsh\PageDshManager.xaml(.vb)`：整合包管理页（需求 2、4）。
  整合包下拉选择 + 新建/删除/打开目录/启动/关闭 DSH；插件列表带开关与安装（npm 包名）；
  技能列表带开关、打开技能目录；信息区显示 dsh 版本、端口、运行状态、DSH_HOME 与工作区。

### 变更
- `FormMain.xaml.vb`：`PageType` 新增 `DshManager = 10`（整合包管理顶级页）；`PageSubType` 新增
  `DownloadDsh = 7`、`SetupDsh = 4`（下标与左右列表一致）；主页面路由新增 DshManager 分支；
  `EndProgram` 在退出时按设置询问并结束由启动器拉起的 dsh 进程。
- `Modules\ModMain.vb`：新增页面声明 `FrmDownloadDsh` / `FrmSetupDsh` / `FrmDshManager`。
- `Pages\PageSetup\Settings.vb`：新增 14 个设置项（11 个全局 + 3 个整合包级）。
- `Pages\PageSetup\PageSetupLeft.xaml(.vb)`：新增「DSH 运行环境」子页面入口与初始化支持。
- `Pages\PageDownload\PageDownloadLeft.xaml(.vb)`：新增「DeepSeekHarness → DSH 版本」分类入口与刷新支持。
- `Pages\PageLaunch\PageLaunchLeft.xaml.vb`：启动按钮改为 DSH 模式（需求 1）。
  新增 `DshModeEnabled()` 开关（默认开启，关闭后恢复原版 Minecraft 流程）；`RefreshDshButtonsUI()`
  按「加载中 / 无整合包 / 版本未装 / 可启动」四态驱动按钮文案与可用性；启动时扫描整合包列表并触发首次引导；
  「版本设置」按钮改为打开整合包管理页。
- `Modules\Base\ModBase.vb`：新增 `VersionDshBaseName`（当前 `0.3.0`）与 `VersionDshDisplay`。

### 工程 / 构建
- 新增 `Directory.Build.props`：本机没有 Visual Studio，用 .NET SDK 的 MSBuild 编译旧式 VB WPF 工程时，
  补齐 net48 引用程序集（NuGet 包）、`VBRuntime`/`VBRuntimePath`、XAML 编译器的 `KnownReferencePaths`
  与 `Microsoft.VisualBasic` 引用。所有设置都限定在 `.vbproj` 上，避免污染 netstandard2.0 的 C# 子项目。
- `Plain Craft Launcher 2.vbproj`：末尾显式 `Import` SDK 自带的 `Microsoft.WinFX.targets`
  （否则不生成 `.g.vb`，报几百个「找不到事件 Loaded」）。新增 7 个模块、3 个页面、2 个 XAML 的工程条目。
- 新增 `global.json`：固定使用 .NET 9 SDK（MeloongCore 与 PCLCS 声明 `LangVersion 13.0`）。
- **本机已可完整编译**：`dotnet msbuild "Plain Craft Launcher 2\Plain Craft Launcher 2.vbproj" -p:Configuration=Debug`
  → 产出 `obj\Debug\Plain Craft Launcher 2.exe`（5.72 MB）。

### 已核实的关键事实（供后续版本参考）
- DSH npm 包 `@deepseek-ai/dsh`，共 26 个版本，仅 alpha / rc，无 stable。
- GitHub 仓库 21 个 release 全部无附件；版本列表需走 GitHub API 或 npm registry。
- dsh 的 npm tarball 不含依赖，安装必须走 `npm install`。
- 隔离靠环境变量 `DSH_HOME`；`dsh web` 默认端口 3080，支持 `--port` / `--no-open`。
- 技能开关可用「改名」实现：dsh 的技能发现只认一层目录下的 `SKILL.md`，且技能名必须匹配
  `/^[a-z0-9]+(?:-[a-z0-9]+)*$/`；给目录加 `.disabled~` 前缀或给 `SKILL.md` 改名都会让它彻底消失，
  改回来即恢复，dsh 运行中也立即生效（有 chokidar 监视）。

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
