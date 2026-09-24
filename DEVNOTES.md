# DEVNOTES — PCL2 魔改为 DeepSeekHarness 启动器

> 这份文档是给"未来的我"（下一次继续这个项目的 AI/人）看的。
> 记录**目标、约束、已核实的外部事实、避坑清单、进度**。
> 改动前请先读 §7 的"工作流程"，并遵守 §8 的"注意事项"。

最后更新：2026-09-24 ・ 启动器版本：`v0.3.9`

---

## 1. 项目目标（用户原始 8 条需求）

| # | 需求 | 实现落点 |
|---|---|---|
| 1 | 「启动 Minecraft」改为「启动 DeepSeekHarness」，点击后自动用系统默认浏览器打开 DSH | `Modules\DSH\ModDshLaunch.vb`、`Pages\PageLaunch\PageLaunchLeft.xaml(.vb)` |
| 2 | 启用版本隔离（类似 MC 整合包）：每个整合包的 dsh 版本、插件、技能相互隔离 | `Modules\DSH\ModDshInstance.vb`，隔离靠 `DSH_HOME` + 独立的 dsh 安装目录 |
| 3 | 下载界面可下载 DSH 所有历史版本，按 alpha / rc 分类，按发布时间排序并标注时间 | `Pages\PageDownload\Dsh\PageDownloadDsh.xaml(.vb)`、`Modules\DSH\ModDshDownload.vb` |
| 4 | 整合包独立管理自己的插件、技能、dsh 版本；未启动时可开关启用 | `Pages\PageInstance\PageInstancePlugin.xaml(.vb)`、`PageInstanceSkill.xaml(.vb)` |
| 5 | 设置中可改 dsh 运行环境和本体位置；第一次启动引导用户选择 | `Pages\PageSetup\PageSetupDsh.xaml(.vb)` + 引导窗口 |
| 6 | 版本控制：本轮为 v0.1.0，之后每次修 bug / 调功能都改版本 | `Modules\Base\ModBase.vb` + `CHANGELOG.md` |
| 7 | 直接在 `PCL-2.13.1.1` 文件夹内改，每次修改前备份 | `tools\backup.ps1` + git |
| 8 | 留一份 markdown 记录 coding 目标与注意事项 | 本文件 |

**范围决定（用户已确认）**

- **A. 彻底改成 DSH 启动器**：保留 PCL2 的 UI 风格与控件库，把 Minecraft 概念全换成 DSH 概念（版本→DSH 版本、Mod→插件、资源包→技能）。不做"最小侵入"，不新写第二套 UI。
- **隔离粒度 B**：**共用一份 Node**，每个整合包有**独立的 dsh 版本目录 + 独立的 DSH_HOME**。
- **Node 运行时 A**：启动器自动下载并管理 Node，首次启动引导一键安装。
- **导航 A**：保留 PCL2 现有顶栏结构（启动 / 下载 / 设置 + 版本选择页），只替换内容，不重排 `FormMain.PageType`。
- **先补齐依赖再改代码**：`MeloongCore` 子模块必须先克隆，否则源码无法编译。
- **导入 A**：新建整合包时提供"从现有 DSH_HOME 导入"（用户本机已有 `E:\DSHarness\.dsh`，含 20 个技能）。

---

## 2. 目录布局（本项目约定）

```
E:\DeepseekHarnessWP\
├─ PCL-2.13.1.1\               ← 唯一改动区（git 仓库根）
│  ├─ DEVNOTES.md              ← 本文件
│  ├─ CHANGELOG.md             ← 版本变更记录
│  ├─ tools\backup.ps1         ← 备份脚本
│  ├─ backups\                 ← （实际放在上一级，见下）
│  ├─ MeloongCore\             ← 子模块，已克隆，勿手改
│  ├─ PCLCS\                   ← C# 子项目
│  └─ Plain Craft Launcher 2\  ← VB.NET WPF 主工程
└─ backups\                    ← zip 快照存放处（在 git 仓库之外，避免污染）
```

**运行时（启动器生成，不在 git 里）**

```
<PCL 根>\DSH\
├─ runtime\node\                         ← 共用 Node（node.exe + npm）
├─ versions\<dsh 版本>\node_modules\@deepseek-ai\dsh\   ← 共享版本仓库
└─ instances\<整合包名>\
   ├─ DSH_HOME\                          ← 该包的 DSH_HOME
   │  ├─ profiles\web\                   ← profile：package.json / cordis.yml / node_modules（插件在这里）
   │  ├─ skills\                         ← 技能（关掉的移到 skills\.disabled\）
   │  ├─ sessions\  storages\  cache\  logs\
   │  └─ .credentials.yaml
   ├─ PCL\Setup.ini                      ← 该包的独立设置（Settings Sources.Instance）
   └─ instance.json                      ← 清单：锁定版本 / 插件 / 技能 / 工作区
```

---

## 3. 已核实的 DSH 外部事实（2026-09-24 实测，不要凭记忆改）

### 3.1 npm 包

- 包名 **`@deepseek-ai/dsh`**，bin **`dsh`**，`type: module`，入口 `lib/bin.js`。
- 本机已装：`C:\Users\Maa\AppData\Roaming\npm\node_modules\@deepseek-ai\dsh`，版本 **0.1.7-rc.1**。
- **发布的 tarball 极小**（0.1.5-rc.3：16 KB / 10 个文件，`dist.unpackedSize` 仅 48904），
  **不含依赖** → 必须 `npm install`，不能只解压 tarball。
- `dist-tags`（实测）：`latest=0.1.5-rc.3`、`next=0.1.7-rc.1`、`alpha=0.1.7-alpha.2`。
- 共 **26 个已发布版本**，只有 `-alpha.N` / `-rc.N` 两种后缀，**没有 stable**。
- 版本与发布时间从 `https://registry.npmjs.org/@deepseek-ai/dsh` 的 `time` 字段取（权威、含时区）。

### 3.2 GitHub 仓库

- 仓库 `https://github.com/deepseek-ai/deepseek-harness`，默认分支 `master`，MIT。
- **21 个 release，全部 `prerelease: true`，每个 0 个附件** → 不能靠 release 资产下载。
- tag 命名 **`dsh-v<semver>`**（例：`dsh-v0.1.7-rc.1`）；另有 1 个无 release 的 tag。
- API：`https://api.github.com/repos/deepseek-ai/deepseek-harness/releases?per_page=100`
  返回 `tag_name` + `published_at`（UTC）。**注意：`github.com` 的 HTML 页面在部分网络下无法抓取，用 api.github.com。**

### 3.3 CLI 行为

- 语法：`dsh [--profile] <name> [options] [app-args...]`；`dsh web` ≡ `dsh --profile web`。
- web profile 的参数：`--host <host>`、`--port <port>`（0=系统分配）、`--trusted-host`、`--no-open`。
  **`--host 0.0.0.0` 是显式错误**。默认 `host=127.0.0.1`、`port=3080`。
- 启动就绪后会输出 GUI URL，并把它写进环境变量 **`DSH_WEB_URL`**。
- **本机当前 GUI：`http://127.0.0.1:3080`**（正在运行，改代码时别把它当测试目标打死）。
- 启动器自身的参数（必须**放在最前**）：`--profile`、`--from-default-profile`、`--patch <path>`（可重复）、
  `--dump-config`、`--dump-config-schema`、`--dump-default-config`、`-V/--version`、`--help`。
  第一个不被识别的 token 之后的内容全部交给 profile 的 app。
- profile 名 `desktop` 被保留（Electron 占用），不要用。

### 3.4 磁盘布局与隔离杠杆

| 目的 | 杠杆 |
|---|---|
| **隔离一切用户态数据** | 环境变量 **`DSH_HOME`**（优先级：显式配置 > `$DSH_HOME` > `~/.dsh`；纯空白视为未设置） |
| `DSH_HOME` 里有什么 | `profiles/`、`skills/`、`sessions/`、`storages/`、`.credentials.yaml`、`.anonymous-user-id`、`logs/`、`cache/`、`attachments/`、`cordis.patch.yml`、`AGENTS.md` |
| 隔离 dsh 本体版本 | 各自独立的 `node_modules\@deepseek-ai\dsh` 安装目录 |
| 隔离插件 | 每 profile 的 `profiles/<name>/package.json` + 该目录下的 `node_modules`；用 `dsh plugin --profile <name> <pnpm 参数>` 管理 |
| 隔离技能 | `$DSH_HOME/skills/`（目录 bundle `<name>/SKILL.md`，或平铺 `<name>.md`），**不递归发现嵌套的 SKILL.md** |
| 追加技能根 | profile 里挂载 `@deepseek-ai/dsh-skill-filesystem` 并配置 `customSkillDirs: [...]` |
| 其他相关环境变量 | `DSH_AGENTS_HOME`、`DSH_BUNDLED_SKILL_DIR`、`DSH_TELEMETRY_DISABLED`、`DSH_WEB_URL`、`DSH_TOOLS_MODE`、`DSH_PROFILE`、`DSH_PROFILE_DIR` |

- 技能根优先级：`<项目>/.dsh/skills`(100) > `<项目>/.agents/skills`(200) > `customSkillDirs`(300)
  > `$DSH_HOME/skills`(400) > `$DSH_AGENTS_HOME/skills`(500)；`.system` 子目录会被跳过。
- 用户本机现有 DSH_HOME：`E:\DSHarness\.dsh`（**User 级环境变量 `DSH_HOME` 已设为它**）。
  → 启动器启动整合包时**必须显式覆盖** `DSH_HOME`，否则会污染用户正在用的环境！
- `C:\Users\Maa\.dsh` 不存在。

---

## 4. PCL2 源码导航（改哪里）

主工程根目录简称 **SR** = `Plain Craft Launcher 2\`。

### 4.1 启动链路

| 位置 | 内容 |
|---|---|
| `SR\Pages\PageLaunch\PageLaunchLeft.xaml:65` | `<local:MyButton x:Name="BtnLaunch" Text="正在加载">`（启动按钮） |
| `SR\Pages\PageLaunch\PageLaunchLeft.xaml.vb:611` | `LaunchButtonClick()`；`:626` 判断 `BtnLaunch.Text = "启动游戏"` → `McLaunchStart()` |
| `SR\Modules\Minecraft\ModLaunch.vb:31,78,97` | `McLaunchStart` / `McLaunchLoader` / 实际实现 |
| `SR\Modules\Minecraft\ModLaunch.vb:2101-2126` | `McLaunchRun`：`ProcessStartInfo(javaw.exe)` + `StartProcess` |
| `SR\Modules\Base\ModBase.vb:876-887` | `StartProcess(ProcessStartInfo)` 封装 |
| `SR\FormMain.xaml.vb:837-841` | 回车键触发 `FrmLaunchLeft.LaunchButtonClick()` |

### 4.2 实例（"整合包"）与版本隔离

| 位置 | 内容 |
|---|---|
| `SR\Modules\Minecraft\ModMinecraft.vb:218` | `Public Class McInstance` |
| `SR\Modules\Minecraft\ModMinecraft.vb:223,227-266` | `PathVersion`、`PathIndie`（隔离目录计算 + 自动迁移） |
| `SR\Modules\Minecraft\ModMinecraft.vb:675-679` | 构造函数：`PathVersion = McFolderSelected & "versions\" & Name` |
| `SR\Modules\Minecraft\ModMinecraft.vb:8-24` | `McFolderSelected`（MC 根目录），`$` 代表启动器目录 |
| `SR\Pages\PageInstance\PageInstanceSetup.xaml(.vb)` | 版本设置页（"版本隔离"开关所在） |
| `SR\Pages\PageInstance\PageInstanceMod.xaml(.vb)` | 版本 Mod 管理页（→ 改成插件管理页的样板） |
| `SR\Modules\Minecraft\ModWatcher.vb` | 监视游戏进程（→ 改成监视 dsh 进程） |

### 4.3 下载页框架

| 位置 | 内容 |
|---|---|
| `SR\Pages\PageDownload\PageDownloadLeft.xaml` | 左侧分类 `StackPanel Name="PanItem"`，子项是 `MyListItem ... Tag="N"` |
| `SR\Pages\PageDownload\PageDownloadLeft.xaml.vb:14,20-44,94-140` | `PageCheck` / `PageGet`（`Select Case ID`）/ `Refresh` —— **新增分类必须同时改这三处** |
| `SR\FormMain.xaml.vb:1203-1224` | `Public Enum PageSubType`，**数值必须等于 StackPanel 里的下标** |
| `SR\FormMain.xaml.vb:1441` | `PageChangeAnim(FrmDownloadLeft, FrmDownloadLeft.PageGet(SubType))` |
| `SR\Pages\PageDownload\Resource\PageResource.xaml(.vb)` | 通用"列表+筛选"控件（社区资源用） |
| `SR\Modules\Minecraft\ModDownload.vb:1131` | `DlFabricListOfficialLoader` —— **网络下载 Loader 的标准写法样板** |
| `SR\PCLCS\Constants.cs:7-36` | `ResourceTypes` / `ResourcePlatforms`（C#，只支持 CurseForge/Modrinth） |

> 结论：**DSH 版本文档页不复用 `PageResource`**（它是给 CurseForge/Modrinth 社区资源用的，
> 平台枚举里没有 GitHub）。DSH 版本页用普通 `MyListItem` + `LoaderTask` 自己画，更简单可控。

### 4.4 设置项机制

| 位置 | 内容 |
|---|---|
| `SR\Pages\PageSetup\Settings.vb:5-197` | `Entries`：所有设置项的声明表，**新增设置必须在这里加一行** |
| `SR\Pages\PageSetup\Settings.vb:199-203` | `Enum Sources { Normal, Registry, Instance }`（`Instance` = 按版本独立，存到 `<PathVersion>PCL\Setup.ini`） |
| `SR\Pages\PageSetup\Settings.vb:217` | `New Setting(Key, 默认值, Optional Source, Optional Encrypted, Optional OnChanged)`，**类型由默认值推断** |
| `SR\Pages\PageSetup\Settings.vb:279/310/362` | `Settings.Set/Get/Get(Of T)`，`Instance:=Me` 表示读该实例的值 |
| `SR\Pages\PageSetup\SettingService.vb:15,28,36,46,57,69` | 附加属性 `SettingService.Key` / `.Value`、`ISettingControl`、刷新/重置/保存 |
| `SR\Pages\PageSetup\PageSetupUI.xaml:93` | 一行设置的样子：`<local:MyCheckBox local:SettingService.Key="UiLauncherLogo" .../>` |
| `SR\Pages\PageSetup\PageSetupLaunch.xaml:255` | `MyTextBox Key="LaunchAdvanceRun"`（文本框样例） |

### 4.5 网络 / JSON / 提示

| 函数 | 说明 |
|---|---|
| `NetRequestByClientRetry(Url, ...)` | 3 次重试、带备份 URL，**首选** |
| `NetRequestByClient(Url, Optional RequireJson:=False, Optional Headers As String(,))` | 单次请求 |
| `NetRequestByLoader(Url, ...)` | 走 Loader，有进度回调 |
| `NetDownloadByLoader(Url, LocalPath, LoaderToSyncProgress, Check)` | 下载文件到本地（**下载 Node / npm 包用这个**） |
| `String.DeserializeJson(Of T)()` / `FileUtils.ReadAsJson` | Newtonsoft.Json 13，扩展方法在 MeloongCore 里 |
| `Hint(Text, HintType.Blue/Green/Red)` | 右下角提示 |
| `MyMsgBox(Caption, Title, Button1..3, ...) As Integer` | 模态对话框，返回按钮序号 |
| `MyMsgBoxInput(...) As String` / `MyMsgBoxSelect(...) As Integer?` | 输入框 / 选择框 |

### 4.6 版本号与工程文件

| 位置 | 内容 |
|---|---|
| `SR\Modules\Base\ModBase.vb:9-27` | `VersionBaseName`、`VersionCode`、`BuildTypeDisplay`、`VersionDisplay` |
| `SR\My Project\AssemblyInfo.vb:54-55` | `AssemblyVersion` / `AssemblyFileVersion` |
| `SR\Plain Craft Launcher 2.vbproj` | 目标 **.NET Framework 4.8**、`OutputType=WinExe`、`AnyCPU`、`RootNamespace=PCL` |
| `SR\Plain Craft Launcher 2.vbproj:335,376,630` | 新增 `.vb` 要加 `<Compile Include=...>`；新增 `.xaml` 要加 `<Page Include=...>` 且 `.xaml.vb` 带 `<DependentUpon>` |

---

## 5. 构建环境

本机现状（2026-09-24）：

| 项 | 状态 |
|---|---|
| .NET Framework | ✅ 4.8（Release 533509） |
| 老 MSBuild 4.0 | ✅ `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\MSBuild.exe`（**编不了本工程**） |
| Visual Studio / MSBuild 17 | ❌ 未安装（正在用 winget 装 `Microsoft.DotNet.SDK.8`） |
| Node / npm | ✅ v22.23.1 / 10.9.8 |
| git | ✅ 2.54.0 |
| 官方 CI 做法 | `dotnet restore PCLCS\PCLCS.csproj` → `msbuild "Plain Craft Launcher 2\Plain Craft Launcher 2.vbproj" -p:Configuration=Debug` |

**编译注意**：`MeloongCore` 是 git 子模块，已克隆到 `PCL-2.13.1.1\MeloongCore`。
第一次编译前需要 `dotnet restore PCLCS\PCLCS.csproj`（要联网拉 NuGet）。

---

## 6. 版本号约定（需求 6）

- 启动器**自己的**版本独立编号，写进 `SR\Modules\Base\ModBase.vb` 的新常量 `VersionDshBaseName`，
  与 PCL 原版号并存（原号不动，便于对照上游）。
- 语义：`v0.1.0` 首个可用版本；之后**每修一个 bug 或调一次功能都 +1**（bug 修 `v0.1.1`，新功能 `v0.2.0`）。
- 每次改动必须在 `CHANGELOG.md` 追加一条（版本 / 日期 / 改了什么 / 涉及文件）。

---

## 7. 工作流程（每次动手都照这个来）

1. `git -C PCL-2.13.1.1 status` 确认工作区干净（不干净先提交或还原）。
2. `pwsh -File PCL-2.13.1.1\tools\backup.ps1 -Version vX.Y.Z -Note "本次要做什么"` → 生成 zip 快照 + git tag。
3. 改代码。
4. `dotnet restore` + `msbuild` 编译验证（**没编译通过不算完成**）。
5. 更新 `CHANGELOG.md` 与 `ModBase.vb` 里的版本号。
6. `git add -A && git commit`。
7. 更新本文件的 §9 进度表。

---

## 8. 注意事项 / 踩坑清单

1. **绝不要动用户正在运行的 DSH**：本会话的 GUI 就跑在 `E:\DSHarness\.dsh` + 端口 3080 上。
   启动整合包时必须显式设置 `DSH_HOME` 与独立端口，禁止 `--host 0.0.0.0`。
2. **`DSH_HOME` 是唯一隔离开关**，但它只在 `$DSH_HOME` 未设置时才回落到 `~/.dsh`。
   用户 User 级已设了 `DSH_HOME`，所以子进程环境变量**必须显式写入**，不能依赖继承。
3. **dsh 包不含依赖**，别写"下载 tarball 解压即用"的逻辑，要走 npm。
4. **GitHub release 没有附件**，别写"下载 release 资产"的逻辑；版本列表用 API/npm，安装用 npm。
5. `dsh` 自己的参数必须在最前，`--no-open` 属于 web profile（放在 `web` 之后），顺序别搞错。
6. **`PageSubType` 的数值 = 左侧列表下标**，加分类时三处（XAML 列表 / PageGet / PageSubType）必须同步，否则数组越界。
7. **设置项类型由默认值推断**（`Me.Type = If(Value, "").GetType`）：默认值写 `0` 就是 Integer，
   写 `""` 就是 String。类型写错会在 `CTypeDynamic` 上炸。
8. **新增实例级设置要带 `Source:=Sources.Instance`**，并在读取时传 `Instance:=...`，否则读的是全局值。
9. 技能**不递归发现**嵌套 `SKILL.md`，所以"关掉的技能"移到 `skills\.disabled\<name>\` 是安全做法
   （`<dshHome>/skills` 只扫一层；`.disabled` 是子目录，其内部不会被扫到 —— 但要实测确认）。
10. 插件走 pnpm，**首次使用需要联网**；npm/pnpm 的源要用国内镜像兜底。
11. `MeloongCore` 是子模块，**不要手改**，也不要把它加进本仓库的提交（用子模块引用即可）。
12. 改 `AssemblyInfo.vb` 的版本号要同步 `ModBase.vb`，否则"关于"页显示与实际不符。
13. 旧代码里 `Mc*` 命名一律保留（回退用），新增代码用 `Dsh*` 前缀，避免大范围重命名引入编译错误。
14. **VB 的集合初始值设定项 `{ }` 内部不能写独占一行的注释**，只能写行尾注释。
    否则解析器会在前一个元素的 `)` 处报 `BC30201 应为表达式`，后面的行全部连锁报 `BC30035 语法错误`，
    报错位置还会指到完全无关的行（这一个坑花了两小时）。说明性注释请写在 `From {` 之前。
15. **模块级变量名不要太像 BCL 类型**：`Public DshProcess As Process` 会让同模块内的 `Process` 解析成
    `DshProcess`，于是 `Dim Proc As Process` 报 `BC30182 应为类型`。同理 `Date`、`Error`、`Name` 都是保留字，
    不能做属性名。已把变量改名为 `DshCurrentProcess`。
16. **VB 订阅事件不能用 `+=`**，必须 `AddHandler X.Event, handler`；`Process.OutputDataReceived` 尤其如此
    （写成 `+=` 会报 `BC32022 ... 是事件，不能直接调用`）。
17. **`LoaderBase` 没有 `IsCanceled`**（只有 `LoaderTask(Of TIn, TOut)` 有）。通用取消判定请用
    `Loader.State = LoadState.Canceled`。
18. **`Logger.Error` 不要写成 `ErrorMessage`**：批量正则替换 `\.Error` 时会误伤 `Logger.Error(`，
    也会误伤 `Proc.ErrorDataReceived`。替换前务必先看命中清单。
19. **类名 / 方法名冲突**：`DshProcess`（变量）与 `System.Diagnostics.Process` 冲突就是这类问题的典型，
    新增类型与已有模块级标识符前先全局搜一下重名。
20. **PCL 的 `Settings.Get` 会 `CTypeDynamic`**（见 §8.7），所以 DSH 侧另建了本地缓存 `DshSettingCache`，
    未注册的键会安全回落到默认值，不会抛异常。

21. **批量正则改写要谨慎**：`Loader.Error` → `ErrorMessage` 这类“全局替换”很容易误伤（`Logger.Error` 就被伤过）。改完务必重新编译一遍。

22. **`--dump-config` 的正常输出，退出码却是 1**：`dsh --profile web --dump-config` 打印完配置后退出码为 1，别把它当失败。`DshRunInfo` 因此只记 `Logger.Warn`，不抛异常。

23. **profile 初始化命令**：不能写 `--from-default-profile web`（会报 `profile "web" is shipped and cannot be a custom profile target`），正确做法是 `dsh --profile web --dump-config`，它会在首次使用时从内置模板生成 `package.json` / `cordis.yml` / `cordis.patch.yml` / `pnpm-workspace.yaml`。

24. **`LoaderBase.WaitForExit()` 会清空 `Input`**：它内部调用 `Start(Nothing, ...)`，而 `Start` 无条件覆盖 `Me.Input`。所以“先 `Start(值)` 再 `WaitForExit()`”拿不到值。DSH 侧用模块级变量 `DshPendingInstallVersion` + `DshRequestVersionInstall()` 绕开。

25. **日期格式字符串里的单引号必须成对**：写错会抛 `FormatException: 无法为字符"'"找到匹配的引号字符`。正确写法是 `yyyy'-'MM'-'dd HH':'mm':'ss`。

26. **dsh Web GUI 必须带 `?token=` 打开，否则 401**（本项目最重要的发现）。实测：不带 token 访问 `/` → **401**；`/?token=XXXX` 首次访问 → **303**（换发 cookie 并重定向到干净路径）；带 cookie 再访问 → **200**。源码印证：`browserAuth.authenticatedUrl()` 把**进程级** `launchToken` 作为唯一鉴权输入塞进 URL，而 `localWebUrl()` 返回的是不带 token 的干净地址。→ 抓 URL 的正则**必须连 query string 一起抓**。

27. **`cordis.patch.yml` 必须是顶层 YAML 数组**：空文件或纯注释都会让 dsh **直接启动失败**（`Error: overlay ... must be a top-level YAML array of loader patch entries`）。空态必须写 `[]`，注释只能写在它**上面**。已验证 `- id: xxx` / `name: "@deepseek-ai/xxx"` / `disabled: true` 三段式能被 dsh 正确应用（dump 出来会变成 `# == @deepseek-ai/dsh-base, patched by .../cordis.patch.yml` 加 `disabled: true`）。

28. **技能开关的“改名法”可用**：技能发现只扫描技能根**一层**，认定 `<root>\<name>\SKILL.md` 存在、且 `<name>` 匹配 `/^[a-z0-9]+(?:-[a-z0-9]+)*$/`。给目录加 `.disabled~` 前缀即彻底消失，去掉即恢复；dsh 有 chokidar 监视，运行中也立即生效。（`--dump-config` 里看不到技能目录，它是运行时服务，不能用 dump 验证。）

29. **“端口通就复用”是错的**：dsh 的访问 token 是**进程级**的，启动器无法事后拼出；端口上还可能是启动器不知道、甚至**已启动失败**的 dsh 进程（它的 401 会误导我们复用）。正确做法：把带 token 的可用地址缓存到实例目录 `.pcl-web-url`，复用时先验证该地址仍可用（303/200），否则换空闲端口重新启动。

30. **测试脚本写 `Setup.ini` 千万别带 BOM**：用 `Out-File` 或带 BOM 的 UTF8 会让**首行的键名被污染**，于是 `DshStopOnExit:True` 读出默认值 False，表现为“退出时不结束 dsh”。必须用 `[System.IO.File]::WriteAllText($p, $text, (New-Object System.Text.UTF8Encoding($false)))`。

31. **UI 自动化验证 PCL 界面的可行手段**（用来完成“真点一次启动”的验证）：PCL 是自绘控件，UI Automation 里按钮只暴露为 `ControlType.Text`，且 `BoundingRectangle` 在 150% DPI 下**不可信**（y 值会远超窗口高度）。可靠替代：`PostMessage(hwnd, WM_CLOSE)` 触发正常关闭流程；`keybd_event` 发回车（PCL 有「回车＝点启动按钮」的快捷键）；**用日志文件判定动作是否发生**比读控件树稳；点击定位用「窗口相对坐标小网格扫描 ＋ 日志反馈」。

32. **加载环必须放在 `PanLoad` 容器【内部】**：`PageLoaderInit` 只切换 **PanLoader / PanContent / PanAlways 的 Visibility**，**不会去动加载环本身**。写成 `PanLoad` 的兄弟节点，加载完成后它会一直挂着（表现为“一直在加载中”）。官方写法：`<local:MyCard x:Name="PanLoad"><local:MyLoading x:Name="Load" .../></local:MyCard>`。

33. **`MyMsgBox` 最多三个按钮，要给用户留「取消」**：原来三个位置都被“安装／绑定／更新说明”占了，没选中整合包时中间还为空，用户**完全没有取消出口**。现在第三个按钮固定是「取消」，次要且破坏性的操作（卸载／重装／更新说明）移到条目**右键菜单**（PCL 标准做法：`GetObjectFromXML(<ContextMenu>...<local:MyMenuItem x:Name=.../>...</ContextMenu>)`，再按名字挂事件）。

34. **用 `PrintWindow` 而不是 `CopyFromScreen` 抓窗口截图**：`CopyFromScreen` 会抓到覆盖在上面的其它窗口（我第一次就抓成了浏览器）。`PrintWindow(hwnd, hdc, 2)`（PW_RENDERFULLCONTENT）可直接把窗口画进 bitmap，**不需要窗口在最前**；配合 UIA 枚举元素文本，能确定地复核“按钮文案／是否存在”。

35. **`FormMain.PageChange` 会拿顶级页枚举值当 `PanTitleSelect.Children` 的下标**：即 `CType(PanTitleSelect.Children(Stack), MyRadioButton)` —— 顶部导航只有 **5** 个 radio（Tag 0~4），所以**任何大于 4 的枚举值都不能作为顶级页面直接 PageChange**，否则抛 `ArgumentOutOfRangeException: index`。已有的 5~9（版本选择／任务管理／版本设置／资源详情／帮助详情）都是“副页面”，进入时不碰这句。→ 新增页面要么做副页面，要么挂在已有主页面下当子页面。「整合包管理」最初被做成顶级页（`DshManager = 10`），点「版本设置」直接崩，现改为 `PageChange(PageType.Setup, PageSubType.SetupManager)`。

36. **子页面必须在左列表里占住对应下标**：`PageChange` 里 `CType(FrmSetupLeft.PanItem.Children(SubType), MyListItem)` 是**按下标取控件**，所以 `PageSubType` 的数值必须与左栏 StackPanel 的子元素下标一一对应。「整合包管理」不想出现在设置左栏，就放一个 `Visibility="Collapsed"` 的占位 `MyListItem`（Tag=5）占位。另外这次崩溃现场说明：若之前停在「DSH 运行环境」（下标 4），`PageChange` 会把 SubType 缺省推导成 4，**越界与否取决于当前所在子页面**，不好复现。

37. **`Click` 处理函数签名写错不会编译报错，而是页面构造时抛 `XamlParseException`**：`无法从文本 "Xxx_Click" 创建 "Click"` → `无法绑定到目标方法，因其签名…与委托类型…不兼容`。表现是整个页面打不开并弹“程序出现未知错误”。**每个控件具体要什么签名见第 42 条的完整对照表**，不要凭“规律”猜。

38. **`MyLoading` 的进度必须自己接上**：`PageLoaderInit` **不会**把加载器赋给加载环（它只管显隐与动画）。要显示“xxx - 42%”，必须自己写 `LoadXxx.State = 某个Loader`（官方页面就是一行行这么赋的），并且该 Loader 的 `Progress`（0~1）要真的被更新，否则永远 0%。另外 `MyLoading` 的进度表现是“标题文字 ＋ 竖条矩形”，**没有横向进度条**（想要就得自己画）。

39. **同一页面上有多个加载器时，页面级 `PageLoaderState` 只管其中一个**：下载页的“版本列表”和“安装”是两个独立 Loader，安装时页面级加载环不会自己切换。所以安装进度用了专门的浮层（`PanInstall`），由安装加载器的 `OnStateChangedUi` 手动控制显隐。

40. **`VersionBranchMain = "OpenSource"` 的开源版本说明弹窗**在 `FormMain` 初始化线程里弹出，内容是 Minecraft 相关（CurseForge／正版登录／主题／百宝箱），对 DSH 启动器毫无意义，已在 DSH 模式下跳过。启动早期不能调 `PageLaunchLeft.DshModeEnabled()`（Protected），用 `DshModeEnabledForStartup()`（放在 ModDshBase，任何异常都当“启用”）。

41. **取消子进程要连整棵进程树**：`npm.cmd` 只是 `cmd.exe` 的外壳，真正干活的是 `node.exe` 子进程。只 `Kill()` 外壳会留下还在写文件的 npm，下次安装就撞 `ENOTEMPTY: directory not empty`，要用 `taskkill /PID x /T /F`。同理，**同版本安装必须加互斥**：两个 npm 同时写同一暂存目录会互相删文件（实测报 `ENOTEMPTY .../domino/test`）；每次安装还应使用**独一无二的暂存目录**并清理遗留。

42. **PCL 控件 `Click` 委托完整对照表（踩了 3 次才记全，务必对照）**：

    | 控件 | Click 委托 | 处理函数写法 |
    |---|---|---|
    | `MyButton` | `MouseButtonEventArgs` | `(sender As Object, e As MouseButtonEventArgs)` |
    | `MyIconButton` | **`EventArgs`** | `(sender As Object, e As EventArgs)` |
    | `MyListItem` | `MouseButtonEventArgs` | `(sender As Object, e As MouseButtonEventArgs)` |
    | `MyLoading` | `MouseButtonEventArgs` | `(sender As Object, e As MouseButtonEventArgs)` |
    | `MyListItem.Changed` | `RouteEventArgs` | `(sender As Object, e As RouteEventArgs)` |
    | `MyComboBox` | 标准 `SelectionChangedEventArgs` | `(sender As Object, e As SelectionChangedEventArgs)` |

    查证方法：直接看 `Controls\<控件>.xaml.vb` 里的 `Public Event Click(...)`。
    **注意**：XAML 里没被引用的同名方法（例如 `PageDshManager.Refresh_Click`，只是 `IRefreshable` 的配套）签名不匹配也无妨，别盲目“统一签名”。
    教训：v0.3.6 我以为找到了“一律写 MouseButtonEventArgs”的规律，把本来正确的 `MyIconButton` 处理函数改坏了，v0.3.7 才靠实机冒烟测出来。**“发现的规律”必须再实测一次才算数。**

43. **DEVNOTES 自身也要及时落盘**：本轮曾出现“会话里记了 20 多条避坑、文件里却只写到第 20 条”的脱节，靠核对行号才发现。**每轮结束时数一遍条目数**，确认新条目真的写进文件了。

44. **PCL 的下载进度不只在页面上——它有「任务管理器（后台下载队列）」**（用户指出，我一开始理解错了）。
    机制：`LoaderTaskbar`（进程级列表）＋ `LoaderTaskbarProgressRefresh()`（在 `TimerMain` 里每 50ms 跑一次）。
    · 任务栏进度 = `LoaderTaskbar.Select(Function(l) l.Progress).Average()`（平滑后）
    · 显示位置：右下角下载按钮 `FrmMain.BtnExtraDownload.Progress`、Windows 任务栏进度条、`Shell.TaskbarItemProgressState`
    · 「更多 → 任务管理」（`PageSpeedLeft`）会为列表里每个加载器建一张卡片
    · **`LoaderTaskbarAdd` 只接受 `LoaderCombo(Of T)`**；而且 `PageSpeedLeft.TaskRefresh` 会调用
      `GetLoaderList()` —— 那是 **LoaderCombo 才有的方法**，把 `LoaderTask` 直接塞进 `LoaderTaskbar`
      会抛 `MissingMethodException`。→ 安装任务必须包一层 `LoaderCombo`。
    · 注册/清理要挂在**模块**上而不是页面事件上：安装是后台任务，页面被切走后若清理逻辑随页面销毁，
      任务管理器里会残留一张永远不消失的卡片。

45. **`LoaderBase.Start(Input, IsForceRestart:=True)` 对【正在运行】的加载器也会返回 True**，
    于是会 `TriggerThreadInterrupt()` 并**在新线程上再跑一遍 `LoadDelegate`**（实机踩坑，后果很隐蔽）。
    我原来写成“先 Start 子任务、再 Start 组合”，同一次安装的 worker 被执行两次：
    第一次真的去跑 npm 了，第二次撞上并发守卫抛错 → **界面显示“安装失败”，而 npm 进程还在后台悄悄下 500 个包**。
    正确做法：**只 Start 最外层的组合**，让组合的 `Update()` 去启动子任务（它按“输入是否相同”判断，不会重启运行中的任务）。
    → 结论：**不要对可能正在运行的加载器传 `IsForceRestart:=True`**，改用输入相等性判断。

46. **并发守卫要记得“同线程重入”**：只用一个 `Boolean` 挡并发，在“同一线程里多次 Start”的场景下会把自己锁死。
    用 `OwnerThread` 记录持有线程，只挡**别的**线程。

47. **重复刷新列表会白白中断加载器**：安装完成时模块和页面各刷了一次版本列表，
    日志里出现 `加载线程 DSH Version List 已中断但线程正常运行至结束，输出被弃用`。
    同一件事只在一处触发刷新。

48. **`FrmMain.BtnExtraButton`（任务管理入口）本身就在右下角**：实机测试时我误点到了它，
    结果证明整条链路是通的（日志里能看到 `新建任务管理卡片：安装 dsh 版本`）。
    **验证 PCL 界面时不要用鼠标自动化**——用户的鼠标焦点随时可能被抢走，造成误操作
    （那次还顺带点了「取消」，把正在进行的安装中断了）。改用日志 + 静态审计。

49. **任务管理卡片的标题只在【创建时】读一次 `Loader.Name`**（`PageSpeedLeft` 里 `Title="…Loader.Name…"`），
    之后的刷新循环只更新副标题（子任务名）与控制项。所以“想显示安装的是哪个版本”**必须在 Start 之前**
    把 `Loader.Name` 设成带版本号的形式（`LoaderBase.Name` 是 `Public` **字段**，可写）。
    卡片是按名字 `RightCards(Loader.Name)` 索引的，中途改名还会造成索引错位。

50. **任务管理器左栏的“下载速度 / 剩余文件 / 线程数”来自 PCL 的网络栈，npm 装的东西永远是 0**：
    `LabSpeed.Text = FormatByteSize(NetManager.Speed)`、`LabFile.Text = NetManager.FileRemain`，
    而 `NetManager.Speed` 是 **ReadOnly**（由 `LoaderDownload` 内部驱动）。npm 是子进程、不走 PCL 的下载器，
    所以这三项在架构上就测不到。**能给出的真实数据是“已写入文件数”（见第 51 条）；速率测不出来，别去假装。**

51. **npm 的真实进度只能靠观察 `node_modules` 的文件数**（实测）：
    · 下载阶段（约 10~30 秒）：npm 把包下到**自己的缓存**，`node_modules` 里恒为 1 个文件；
    · 解压阶段（约 20~40 秒）：文件数在 20~40 秒内从 1 涨到 27000+，但只在**最后几次采样**才冲高；
    · 结束时 `node_modules\.package-lock.json` 出现，`packages` 节点数就是精确的包总数（实测 511~512）。
    所以：采样周期要短（400ms 太密、8 秒太疏，最终取 **2 秒记一次日志**），
    进度用“文件数 / 估算总文件数”的平方根曲线推进（`Pushed = 0.15 + 0.71 * Sqrt(Ratio)`），
    下载阶段（文件数 ≤ 8）用一条很慢的时间曲线兜底，避免进度条纹丝不动。

52. **`DirectoryUtils.EnumerateFiles` 的参数顺序是 `(folder, includeSubDirectories, searchPattern)`**，
    不是 .NET 的 `(path, searchPattern, searchOption)`。按 .NET 顺序传参（第三个参数给 `SearchOption`）
    会被隐式转成 Boolean 而抛异常；如果外面套了 `Try/Catch`，就会**静默返回 0**，非常难查（本次踩到）。
    另外枚举正在被写入的目录时要逐项容错（目录/文件可能在枚举途中消失）。

53. **隐藏顶部导航项只能改 `Visibility`，不能从 `PanTitleSelect` 里移除元素**：
    `FormMain.PageChange` 会拿顶级页枚举值当 `PanTitleSelect.Children` 的下标（见第 35 条），
    移除元素会让下标整体错位并抛 `ArgumentOutOfRangeException`。
    另外要注意：`FormMain` 的第三阶段初始化跑在 `RunInNewThread` 里，
    **在后台线程改 UI 元素的 Visibility 会抛** `InvalidOperationException: 调用线程无法访问此对象，
    因为另一个线程拥有该对象`（实机踩过，而且被我自己的 Try/Catch 挡住了、界面静默不变）。
    必须 `RunInUi(...)`。

54. **隐藏一个页面时先查清挂在它下面的入口**：
    「更多」页除了帮助/关于/宝箱，还挂着「整合包管理」（在它的左栏里）。直接隐藏会丢掉入口。
    处理方式：把启动页左栏原本的「版本选择」按钮在 DSH 模式下改文案为「整合包管理」并直连管理页，
    入口反而更显眼。**顺带确认「任务管理」不受影响**——它的入口是右上角那个按钮
    （`BtnExtraDownload`，ToolTip 就是"任务管理"），不依赖「更多」页。

55. **PCL 有现成的「功能隐藏」机制，隐藏页面要走它、不要自己设 Visibility**：
    `PageSetupUI.HiddenRefresh()`（`Handles Me.Loaded`）会按这些开关**重新设置**一遍显隐：
      `FrmSetupLeft.ItemLaunch.Visibility = If(... UiHiddenSetupLaunch ..., Collapsed, Visible)`
    开关本身也已注册（`Settings.vb` 行 160~173，每个都带 `OnChanged:=AddressOf PageSetupUI.HiddenRefresh`）。
    自己设 Visibility 会被它覆盖——除非把设置项真的设成 True。

56. **写设置项有两个硬约束（都踩过）**：
    ① **必须在 UI 线程**：`Settings.Set` 会碰控件绑定，后台线程调用抛
       `InvalidOperationException：调用线程无法访问此对象，因为另一个线程拥有该对象`。
    ② **必须在 `InitializeComponent()` 之前**：`InitializeComponent` 会把「个性化 → 功能隐藏」里那些
       复选框（`local:SettingService.Key="UiHiddenSetupUi"` 等）建出来，绑定初始化时会拿内存里的旧值
       把设置项回写。实测把写入放在 `InitializeComponent()` 之后，5 个开关里只有 2 个保住了 True，
       被回写的 `UiHiddenSetupUi` 就一直是 False → 设置页的「个性化」条目又冒出来。
       现在放在 `InitializeComponent()` 之前（`FormMain` 构造函数里，主题刷新之后）。

57. **「正版 / 离线」这排按钮不在 `PanLogin` 里，而在 `PanType` 网格里**（`PageLaunchLeft.xaml` 行 30~57）。
    `PanLogin` 只是登录**页面**的容器（`RefreshPage` 往里塞 `PageLoginLegacy` 之类的控件）。
    想彻底隐藏账号相关 UI，两个都要 `Collapsed`。
    另外 `BtnMore` 与 `BtnVersion` 在 DSH 模式下功能重复（都进整合包管理），只留 `BtnVersion`。

58. **XAML 里写换行要写 `&#10;`，写成 `&#38;#10;` 会变成字面量**（双重转义）。
    实机表现是界面上直接显示「&#10;」这串字符。

---

## 8b. 本地构建环境搭建记录（v0.3.0 完成）

本机没有 Visual Studio，折腾了很久才打通编译。**以下是可复现的完整步骤**，换机器照做即可：

```powershell
# 1. 装便携版 .NET 9 SDK（Program Files 无写权限，所以装到工作区）
#    下载：https://builds.dotnet.microsoft.com/dotnet/Sdk/9.0.101/dotnet-sdk-9.0.101-win-x64.zip
#    解压到 E:\DeepseekHarnessWP\tools\dotnet
#    （注意：网络限速约 150~340 KB/s，281 MB 需要 15~30 分钟，建议后台下载）

# 2. 还原（会拉 Microsoft.NETFramework.ReferenceAssemblies 等包）
E:\DeepseekHarnessWP\tools\dotnet\dotnet.exe restore "PCLCS\PCLCS.csproj"
E:\DeepseekHarnessWP\tools\dotnet\dotnet.exe restore "Plain Craft Launcher 2\Plain Craft Launcher 2.vbproj"

# 3. 编译
E:\DeepseekHarnessWP\tools\dotnet\dotnet.exe msbuild "Plain Craft Launcher 2\Plain Craft Launcher 2.vbproj" -p:Configuration=Debug -v:m
```

产物：`Plain Craft Launcher 2\obj\Debug\Plain Craft Launcher 2.exe`（也会复制到 `bin\`）。

> ⚠️ **`restore` 不能省，`obj\` 目录不能删！**
> net48 引用程序集的路径是 NuGet 写进 `obj\*.nuget.g.props` 的；删掉 `obj` 后直接 build 会重新报 `MSB3644`。
> 干净重建的正确做法是 `restore` → `msbuild`（或 `dotnet build`，它隐含 restore）。

**这套环境需要四个补丁**（都已写进仓库，前面踩的坑）：

| 补丁 | 位置 | 解决什么 |
|---|---|---|
| NuGet 引用程序集包 | `Directory.Build.props` | `MSB3644` 找不到 net48 引用程序集 |
| `VBRuntime` / `VBRuntimePath` | `Directory.Build.props` | `BC2017` 找不到 Microsoft.VisualBasic.dll |
| `Microsoft.WinFX.targets` 导入 | vbproj 末尾 | 不生成 `.g.vb`，几百个「找不到事件 Loaded」 |
| `Microsoft.VisualBasic` 显式 Reference | `Directory.Build.props` | XAML 编译器 `MC2000 Could not find assembly` |

> 还有一个坑：`AssemblySearchPaths` 一旦在 `Directory.Build.props` 里覆盖，会破坏
> `Newtonsoft.Json` 等 HintPath 引用（满屏 `JObject 未定义`），所以**不要动它**。
> 另外所有属性都必须加 `'$(MSBuildProjectExtension)' == '.vbproj'` 条件，
> 否则会把 .NET Framework 引用程序集泄漏给 netstandard2.0 的 C# 子项目（`CS0518 System.String 未定义`）。

---

## 9. 进度表

| 版本 | 日期 | 内容 | 状态 |
|---|---|---|---|
| v0.1.0 | 2026-09-24 | 建立基线：克隆 MeloongCore 子模块、初始化 git、备份脚本、DEVNOTES、CHANGELOG | ✅ 已完成 |
| v0.2.0 | 2026-09-24 | 打通本地构建环境（便携 .NET 9 SDK + 四个构建补丁），`dotnet msbuild` 可产出 exe | ✅ 已完成 |
| v0.3.0 | 2026-09-24 | DSH 基础设施 7 个模块 + 3 个新页面 + 启动按钮改造 + 14 个设置项；**编译通过，产出 5.72 MB exe** | ✅ 已完成 |
| v0.3.1 | 2026-09-24 | 实机运行验证 + 修 3 个真 bug（启动按钮死循环、profile 初始化命令错误、非零退出码误判） | ✅ 已完成 |
| v0.3.2 | 2026-09-24 | 端到端跑通整合包创建流程，再修 3 个真 bug（左列表下标错位导致崩溃、日期格式引号、空状态文案） | ✅ 已完成 |
| v0.3.3 | 2026-09-24 | 机制级实测：发现并修复「浏览器 401（丢 token）」与「patch.yml 空文件致 profile 起不来」；插件开关/patch 格式获实证 | ✅ 已完成 |
| v0.3.4 | 2026-09-24 | **在真实 GUI 里端到端验证通过**：回车点启动 → dsh 起来 → 抓到带 token 地址 → 浏览器打开 → HTTP 200 + `<title>DeepSeek Harness</title>`；并修复端口复用策略、增加 URL 缓存 | ✅ 已完成 |
| v0.3.5 | 2026-09-24 | 修用户反馈的两个 UI 问题：「加载中」不消失、版本对话框没有取消；次要操作移入右键菜单 | ✅ 已完成 |
| v0.3.6 | 2026-09-24 | 修用户反馈：「设置→DSH 运行环境」与「版本设置」两处页面打不开（handler 签名 / 顶级页下标越界）；安装加互斥与进程树清理 | ✅ 已完成 |
| v0.3.7 | 2026-09-24 | 安装过程显示真实进度（百分比 + 阶段文案 + 可取消）；去掉 DSH 模式下无意义的「开源版本说明」弹窗；修第 4 处 Click 签名坑 | ✅ 已完成 |
| v0.3.8 | 2026-09-24 | 安装任务接入 PCL 任务管理器（右下角下载按钮 + 任务栏进度 + 任务管理卡片），修掉"同一次安装跑两遍 worker"的隐蔽 bug | ✅ 已完成 |
| v0.3.8 | 2026-09-24 | 安装任务接入 PCL 任务管理器（右下角下载按钮 ＋ 任务栏进度 ＋ 任务管理卡片），修掉“同一次安装跑两遍 worker”的隐蔽 bug | ✅ 已完成 |
| v0.3.9 | 2026-09-24 | 任务卡片标题显示 dsh 版本号；加真实文件计数（已写入 N 个文件 / 共 M 个包）；查清速率为何测不到 | ✅ 已完成 |
| v0.4.0 | 2026-09-24 | 隐藏顶部导航「更多」页；把「整合包管理」入口移到启动页的按钮上 | ✅ 已完成 |
| v0.4.1 | 2026-09-24 | 隐藏下载页与设置页里全部 Minecraft 内容（含启动页账号 UI）；修 `&#10;` 字面量 | ✅ 已完成 |
| v0.5.0 | — | 术语清理：启动页/关于页的 Minecraft 残留文案、账号与皮肤入口 | ⏳ 待做 |：启动页/关于页的 Minecraft 残留文案、账号与皮肤入口隐藏 | ⏳ 待做 |
| v0.5.0 | — | 术语清理：启动页/关于页的 Minecraft 残留文案、账号与皮肤入口隐藏、联机页处理 | ☐ |
| v0.6.0 | — | 引导完善：Node 下载进度提示、失败重试、镜像源切换；首次启动引导的视觉打磨 | ☐ |
| v0.7.0 | — | 插件市场/技能导入的易用性（拖入 zip、从 URL 导入）；整合包导出/导入（.dshpack） | ☐ |
| v1.0.0 | — | 稳定性收尾、错误处理完善、文档完善 | ☐ |

### ✅ 需求 1 已在真实 GUI 中端到端验证通过（v0.3.4）

证据链（全部来自真实运行的日志）：
```
[DSH 启动] 启动命令：node.exe "...\bin\DSH\versions\0.1.7-rc.1\node_modules\@deepseek-ai\dsh\lib\bin.js" --profile web --host 127.0.0.1 --port 3421 --no-open
[DSH] dsh web: http://127.0.0.1:3421/?token=NeBXMAzgXkPWRencoW0LUBSy8ngIEwSy-GpjPwmGcX0
[DSH 启动] DeepSeekHarness 已就绪：http://127.0.0.1:3421/?token=NeBXMAzgXkPWRencoW0LUBSy8ngIEwSy-GpjPwmGcX0
[ModDshLaunch] 正在用默认浏览器打开：http://127.0.0.1:3421/?token=NeBXMAzgXkPWRencoW0LUBSy8ngIEwSy-GpjPwmGcX0
```
对该地址发起的实际请求：**HTTP 200**，32,959 字节，`<title>DeepSeek Harness</title>`，
最终重定向到干净地址（token 已换成 cookie）。不带 token 访问则是 401——鉴权符合预期。

退出流程也已验证：`DshStopOnExit=True` 时弹「是否一并关闭」→ 选「一并关闭」→ dsh 进程结束、
端口释放（HTTP 000）、缓存的 token 地址被清除。

### 已知未完成 / 待验证（下一次接手先看这里）

1. ~~没有实机运行验证过~~ → **已在 v0.3.4 端到端验证通过**（真实 GUI 点启动 → dsh 起来 → 抓到带 token 地址 → 浏览器打开 → HTTP 200 且 title 为 DeepSeek Harness）。
   第一次运行请重点检查：
   - `DshEnsureFirstRun` 的弹窗顺序是否会卡住 UI（`RunInUiWait` + `WaitForExit` 的组合）
   - `DshInstanceListLoader.WaitForExit()` 在 UI 线程被调用会不会死锁
     （`DshEnsureFirstRun` 在后台线程调用，应该没问题；`PageDshManager` 里在 UI 线程调用过，需要实测）
   - `ModDshLaunch` 的 stdout 时序：dsh 是否真的把 URL 打到 stdout
2. ~~技能开关没实测~~ → **已按源码核实并做等价验证**（见第 28 条），加 `.disabled~` 前缀即不再被发现。
3. ~~插件开关格式是推断的~~ → **已用 dsh 的 `--dump-config` 实证生效**（见第 27 条）。原文（`- name: X` / `disabled: true`），
   需要实测确认；`DshReadDisabledPlugins` 的解析也是按这个格式写的。
4. **DshManager 页面的布局没有设计稿**，是直出实现的，视觉上还需要打磨。
5. `ModDshInstall` 的 npm 安装没有接 PCL 的下载任务栏（`LoaderTaskbar`）；v0.3.7 起已改为在下载页显示进度浮层（百分比 ＋ 阶段文案 ＋ 取消），但仍未接入任务栏。**进度观感需实机装一次确认。**
6. 启动页仍然显示 Minecraft 的账号/皮肤区域（已设为 `IsHitTestVisible = False` 并隐藏部分元素），
   但没有彻底移除，术语清理留待 v0.5.0。
