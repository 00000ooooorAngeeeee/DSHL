# DEVNOTES — PCL2 魔改为 DeepSeekHarness 启动器

> 这份文档是给"未来的我"（下一次继续这个项目的 AI/人）看的。
> 记录**目标、约束、已核实的外部事实、避坑清单、进度**。
> 改动前请先读 §7 的"工作流程"，并遵守 §8 的"注意事项"。

最后更新：2026-09-24 ・ 启动器版本：`v0.7.0`

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
       被回写的开关就会一直是 False。**注意**：v0.4.1 时我误判成"PCL 稳定地把 UiHiddenSetupUi 回写成 False"，
    实际上那是**用户正在「功能隐藏」页手动勾选**造成的（我在同一时间读到了中间状态）。
    修正到 `InitializeComponent()` 之前后，5 个开关全部按预期落盘（v0.4.2 实机确认）。
       现在放在 `InitializeComponent()` 之前（`FormMain` 构造函数里，主题刷新之后）。

57. **「正版 / 离线」这排按钮不在 `PanLogin` 里，而在 `PanType` 网格里**（`PageLaunchLeft.xaml` 行 30~57）。
    `PanLogin` 只是登录**页面**的容器（`RefreshPage` 往里塞 `PageLoginLegacy` 之类的控件）。
    想彻底隐藏账号相关 UI，两个都要 `Collapsed`。
    另外 `BtnMore` 与 `BtnVersion` 在 DSH 模式下功能重复（都进整合包管理），只留 `BtnVersion`。

58. **XAML 里写换行要写 `&#10;`，写成 `&#38;#10;` 会变成字面量**（双重转义）。
    实机表现是界面上直接显示「&#10;」这串字符。

59. **用户手动配置过的隐藏开关要"固定"下来，别用自己的默认值覆盖**：
    需求原话是"我希望最终的设置页长这样（我用了 PCL 原版的「功能隐藏」功能），请你固定一下这个结果"。
    做法：把用户勾选的那几个键在 `DshApplyModeHideSettings()` 里显式写成 True，
    同时把**没勾**的键显式写成 False（避免旧配置残留导致行为不确定）。
    当前固定值：`UiHiddenSetupLaunch/Ui/System = True`、`UiHiddenSetupLink = False`、
    `UiHiddenPageOther = True`、`UiHiddenPageDownload/PageSetup = False`。

60. **排查"设置被改"类问题时，先确认不是用户正在改**（本次的教训）：
    v0.4.1 我看到 `Setup.ini` 里 `UiHiddenSetupUi` 是 False 就判定"PCL 会回写这个键"，
    其实用户当时正在「功能隐藏」页里手动勾选——我读到的是中间状态。
    **同一份配置文件被两个人同时动时，别急着下根因结论**；
    正确做法是先问一句，或者观察多次启动是否稳定复现。

61. **"进入后第一次"和"从别的页面返回"走的是不同的刷新路径**（用户报的两个 bug 都是这个根源）：
    · 设置页左栏：`PageSetupUI.HiddenRefresh()` 会被多条路径调用，第一次进设置页与返回设置页
      的时机不同，导致"第一次左栏漏出 MC 条目、返回后才是干净的"。
      → 修法：把 DSH 的收紧规则放进 `HiddenRefresh()` **末尾**，任何一次刷新都会重新施加。
    · 启动页「正版/离线」：`RefreshPage` 的 `UnknownType` 分支里有 `PanType.Visibility = Visible`，
      每次进启动页都会执行，把上一轮的隐藏覆盖掉。
      → 修法：不要逐个元素打补丁，而是给整个登录区套一层容器 `PanLoginArea`，只折叠这一处。
      **教训：只要 PCL 会在多处给同一个元素赋 Visibility，就要想办法用一个统一的父容器"总开关"盖住它。**

62. **`PageSetupUI.HiddenRefresh()` 里有一条"可选子页面少于 2 个就隐藏整个左栏"的规则**：
    `FrmSetupLeft.PanItem.Visibility = If(AvaliableCount < 2 AndAlso Not HiddenForceShow, Collapsed, Visible)`。
    DSH 模式下把设置子页面全隐后正好命中这条，左栏整块消失——**这正是期望效果**，不是 bug。

63. **XAML 里搬动元素时要注意别把 `x:Name` 弄重**：我先加了一层容器又保留了同名元素，
    短暂出现了两个 `PanLogin`。改完要用缩进/标签配对检查一遍（本次写了脚本统计 `<Grid>`/`</Grid>` 深度，
    最终深度应等于 1）。

64. **PCL 的自绘控件在 UI Automation 里既没有 InvokePattern 也没有 SelectionItemPattern**，
    所以**没法用 UIA 编程导航**（`TryGetCurrentPattern` 全返回 False），
    用 Tab/Enter 也容易被页面内的可聚焦控件吃掉焦点。
    → 验证界面只能靠"直接启动到目标页面 + 截图/读日志"，需要用户配合操作的地方就别硬凑。

65. **改 PCL 的公共方法前，先看它开头有没有"提前返回"**（这次的关键）：
    `PageSetupUI.HiddenRefresh()` 第一行是
        If FrmMain.PanTitleSelect Is Nothing OrElse Not FrmMain.PanTitleSelect.IsLoaded Then Return
    第一次进设置页时 `PanTitleSelect` 还没 Loaded，于是**整个函数体被跳过**——
    我把 DSH 规则加在它末尾，结果第一次进设置页根本不执行。
    用户给出的现象链把这一点暴露得很清楚：
        启动 → 设置（左栏漏出 MC 条目，不正常）→ 个性化（规则生效，正常）
            → 任意页面 → 再进设置（又 return，不正常）
    → 修法：把规则抽成独立的 `DshApplySetupLeftVisibility()`，
      由 `PageSetupLeft.Loaded` **直接调用**（不依赖 HiddenRefresh 的时机），
      同时仍保留在 HiddenRefresh 末尾（覆盖"改设置项后"的刷新）。
    **教训：依赖"某个公共方法一定会跑"之前，先读它开头的守卫条件。**

66. **用户给的现象链比任何日志都好用**：
    "启动→设置（不正常）→个性化（正常）→任意页面（不正常）"这一串直接定位到了
    "不同进入路径下同一个方法是否执行"这个差异，比我反复截图猜快得多。
    **遇到"时好时坏"的 bug，先请用户描述复现路径。**

67. **设置页左栏"最终形态"是：联机 / 个性化 / DSH 运行环境 / 整合包管理**（用户确认）。
    对应的固定开关：`UiHiddenSetupLaunch = True`、`UiHiddenSetupSystem = True`、
    `UiHiddenSetupUi = False`（个性化**保留**——用户要用主题/背景等启动器设置）、
    `UiHiddenSetupLink = False`、`UiHiddenPageOther = True`。
    注意两点：
      · `UiHiddenSetupUi` 之前被我误设为 True，导致左栏只剩一项；现在改回 False。
      · 左栏条数不能再少：PCL 有 `AvaliableCount < 2` 就整块收起左栏的规则，
        而「联机」被 PCL 硬编码成 Collapsed、不计入，所以实际可见项是 个性化 / DSH / 整合包管理 三项。

68. **「整合包管理」条目从"隐藏占位"升级为"可见条目"**：
    它原本只为占住下标 5 而存在（`Visibility="Collapsed"`），入口只有启动页那个按钮，太深。
    现在 `PageCheck` 的 `Handles` 里加上了 `ItemManager.Check`，并在 DSH 模式下设为可见，
    于是设置页左栏可以直接进整合包管理。**加入 Handles 是必须的**，否则点它不会切页。

69. **用户说"应该是 B 方案"时，指的是选项本身而不是措辞**：
    我在上一条回复里给了 A/B 两个选项，用户回图并说"应该是B方案，如图是我预期的效果"。
    → 遇到这种"给两个方案让用户选"的情况，用户回一个字母 + 截图，就以**截图为准**去核对，
      不要只按字母推断（截图能确认我理解对了没有，字母不能）。

70. **设置页左栏最终形态：个性化 / DSH 运行环境 / 整合包管理**（用户明确不需要「联机」）。
    `ItemLink` 保持 PCL 原本的 Collapsed 即可（它本来就被硬编码隐藏）。

71. **`PageSetupLeft.Loaded` 里"默认选中某项"必须让位给显式导航**（用户报的 bug）：
    从启动页点「整合包管理」会走 `FormMain.PageChange(Setup, SetupManager)`，
    它已经 `SetChecked(ItemManager)` 了；但紧接着 `PageSetupLeft.Loaded` 又无条件
    `ItemUI.SetChecked(True)`，把选中项覆盖成「个性化」，右面板也就跟着显示错页。
    → 修法：用本类已有的 `IsPageSwitched` 标记做守卫（`PageChange` 开头会把它置 True），
      `If IsPageSwitched Then Return`，即"已经有人明确指定了子页面，就别再改选中项"。
    **通用教训：初始化代码里"设默认值"的动作，一定要先检查有没有人已经显式设过。**

72. **同一个页面实例被创建两次会造成可感知的卡顿**（用户报"点整合包管理卡顿约 1 秒"）：
    `BtnVersion_Click` 里已经 `If FrmDshManager Is Nothing Then FrmDshManager = New PageDshManager`，
    而 `FormMain.PageChange` 的 Setup 分支里我又补了一句同样的创建 —— 于是 `PageDshManager` 被构造两次
    （每次构造都要读整合包列表、插件、技能等）。删掉 FormMain 里那句后导航耗时从 ~1s 降到 ~0.53s。
    **教训：同一个对象的"按需创建"只留一处，别在调用链的两端各写一遍。**

73. **用"某个标志位"做时序守卫时，必须确认这个标志位在读取点已经被赋值。**
    案例：`PageSetupLeft.Loaded` 里要判断"是否已经有人明确指定了子页面"，
    我用 `IsPageSwitched`（名字看起来正合适）做守卫，但诊断日志显示读到的仍是 False：
        切换主要页面：Setup, SetupManager
        设置页 Loaded（DSH）：IsPageSwitched=False, PageID=SetupManager, Manager=True, UI=False
        设置页 Loaded（DSH）：无人指定子页面，默认选中「个性化」   ← 覆盖发生在这里
    时序真相：`PageChange` 先设 `PageID=SetupManager`、`ItemManager.Checked=True`，
    再在 `PageChangeRun` 里把控件挂到可视树 —— 挂载时触发 `Loaded`，
    **而 `IsPageSwitched = True` 是在挂载之后才执行的**。
    → 更稳的做法是判断**目标状态本身**（`PageID` / `ItemManager.Checked`），
      而不是判断"有没有人操作过"。**名字对不代表时序对。**

74. **给"猜不出来的时序 bug"加一次性诊断日志，比反复读代码快得多。**
    这次我复现不出用户的现象、读代码也觉得自洽，于是直接在两处打印
    `IsPageSwitched / PageID / ItemManager.Checked / ItemUI.Checked`，
    一次运行就把真相打在日志里了。**日志要打印"判断条件用到的所有量"，而不只是结论。**

75. **PCL 开源版删掉的不只是"密钥"，还有一整块功能逻辑** —— 排查问题时先确认"原版是不是本来就没有"。
    实例（用户报"个性化里改主题颜色不生效"）：
      · `ModSecret.vb` 开头写着"由于包含加解密等安全信息，本文件中的部分代码已被删除"；
      · 被删掉的部分里包含 **「主题编号 → HSL」的映射表**。`ThemeRefresh` 本身还在，
        但它只用当前 HSL 全局变量（`ColorHue/ColorSat/ColorLightAdjust`）重算颜色，
        编号→HSL 这一步没了 → 点主题时 hue 不变，**颜色自然纹丝不动**；
      · `ThemeCheckAll` / `ThemeCheckOne` / `ThemeUnlock` 也被留成了空壳
        （空实现 / 恒返回 True / 恒返回 False）→ 主题单选按钮不反映已保存的主题、隐藏主题永远灰着。
    → 结论：**"某功能不生效"要先看是不是开源版本来就没有**，别先怀疑自己的改动。
      证据链：`SettingService` 里 `OnChanged:=AddressOf ThemeRefresh` 是**原版就有的**，
      说明设计上确实靠它切主题，缺的只是映射表。

76. **主题单选按钮没有 `Tag`，编号在 `local:SettingService.Value` 里**：
    `SettingService.GetValue(控件)` 返回那个字符串。第一版我用 `Val(Box.Tag)` 取值，
    全部取到 0（没有 Tag 就是 Nothing/0）→ 选错主题。
    PCL 自己的写法（`MyRadioBox.RefreshSetting`）就是 `Checked = NewValue = SettingService.GetValue(Me)`，
    照着它写就对了。

77. **不要手动给 `MyRadioBox.Checked` 逐个赋值**：`MyRadioBox.SetChecked` 里有
    "最多一个选中 / 一个都没选就自动选第一个"的联动逻辑。我在循环里给每个按钮赋值，
    与它打架，实测出现"明明设了 2（小草绿），最后选中的是 4（橡木棕）"。
    → 正解：**交给 `SettingService.RefreshSettings(Me)`** —— 它会对每个 `ISettingControl`
      调用 `RefreshSetting`，按已保存的设置勾选。自己只负责"挂事件"和"放开锁定的项"。

78. **`ModSecret` 是模块，访问不到窗体的控件**：写 `FrmSetupUI.RadioLauncherTheme14` 报
    `未声明"RadioLauncherTheme14"`。要写成 `FrmSetupUI.RadioLauncherTheme14`。
    另外**不要在 `ThemeCheckAll` 里碰控件** —— 启动早期它被 `FormMain` 调用时
    `FrmSetupUI` 还是 `Nothing`，会抛"未将对象引用设置到对象的实例"（实测踩过）。

79. **重装/覆盖 dsh 版本前必须先结束占用该版本的 dsh 进程**（用户实报的 bug）。
    现象：装到"正在部署到版本仓库"时报
        无法将文件夹删除到回收站，回退到永久删除：...\versions\0.1.7-rc.1\
        （COMException: HRESULT 0x80270000）
        无法覆盖已存在的版本目录：对路径"...\node_modules\@koromix\koffi-win32-x64\win32_x64\koffi.node"的访问被拒绝
    根因链：
      · `@koromix/koffi-win32-x64` 是**平台专用包**（文件名带 -win32-x64 的那种不会被 npm 去重），
        所以它必然是 `node_modules` 下的**独立原生 DLL**（koffi.node）；
      · 之前这个版本装到一半失败、残留了残缺目录，但当时启动器已经把它 pull 起来过，
        **那个进程还在跑并加载着 koffi.node**；
      · Windows 锁住被加载的 DLL → 删不掉旧目录 → 覆盖失败。
    修法（`DshStopVersionProcesses`）：按命令行匹配（`Win32_Process.CommandLine` 含该版本目录路径）
    找出所有 `node.exe` 并结束整棵进程树，然后 `DshClearRunningState()` 复位启动器的运行状态，
    再删目录。
    实测：日志出现「已结束 1 个占用该版本目录的进程」，随后 512 包正常落盘、`bin.js` 与 marker 齐全。

80. **删目录不要走回收站**：对这种"被占用的原生 DLL"，`toRecycleBin:=True` 会抛
    `COMException: HRESULT 0x80270000`，白绕一圈还把真实原因（哪个文件被锁）埋掉。
    直接删 + 重试 + 失败时**改名挪到一边**（改名比逐个删文件宽容得多，只要目录本身没被独占），
    并给用户人话提示（是哪个目录、可能是什么原因、怎么处理），不要只抛 COM 堆栈。

81. **用户明确要求：非必要不要删 `bin\DSH` 与 `bin\PCL`**。
    前者是已下载的 dsh 版本仓库（重装要下 500+ 个包、约 1 分钟），
    后者是用户设置（主题、隐藏开关、整合包选择）。**每次启动都要重下会很烦。**
    → 测试时如果要"干净环境"，优先用**临时目录 + junction**，或者只做只读检查；
      确实需要清就**先备份 `Setup.ini`**（本次备份到了 `E:\DeepseekHarnessWP\_setup_backup.ini`）。

82. **★★ 安全红线：杀进程的匹配条件必须收紧到"只可能是我们自己的" ★★**
    （**我犯过一次严重错误，这条务必保留**）
    用户报"点「关闭 DSH」说没有进程，但状态栏显示运行中"。我加了三级兜底，其中第③级写成
    "扫描所有命令行里含 `@deepseek-ai\dsh\lib\bin.js` 的 node 进程" —— **这个特征太宽了**：
    用户**全局安装**的 dsh（跑在 3080）用的是同一个 npm 包，命令行里同样有这段路径，
    于是点「关闭 DSH」把**用户自己的全局 dsh 一起杀了**。实测日志同时打出两个 PID：
        兜底结束 dsh 进程 PID 25568   ← 用户的全局 dsh（3080）
        兜底结束 dsh 进程 PID 5312    ← PCL 的（3082）
    万幸全局 dsh 的 subprocess runner 把服务又拉起来了（3080 恢复 401），用户的 DSH_HOME 数据也完好，
    但这是一次真实的服务中断 —— **用户的全局环境绝不该被启动器碰**（DEVNOTES 顶部铁律本来就写着）。
    修法（三重收紧，缺一不可）：
      ① 特征只认**启动器自己的目录**：命令行里出现 `DshVersionRoot`（`<启动器>\DSH\versions\`）
         或 `DshRoot`（`<启动器>\DSH\`）才算"我们的"；
      ② 再加一道硬闸：命令行含 `\appdata\roaming\npm\` 的一律 `Continue For`（全局 npm 安装）；
      ③ 所有按路径/命令行匹配的杀进程处（`DshStopOwnDshProcesses`、`DshStopProcessesOfInstance`、
         `DshStopVersionProcesses`）**都要加**这两道，不能只加一处。
    实测验证：点「关闭 DSH」→ 只结束 PID 5312 → 3082 关闭、**3080 仍 401 完好**。
    **通用教训：任何"扫描系统进程然后杀掉"的代码，匹配条件必须先问"这条规则有没有可能命中
      用户环境里的别的东西？"—— 宁可少杀（失败让用户手动处理），也不能多杀。**

83. **"看内存引用"和"看端口"得出的结论会不一致，必须统一到一个判定函数**。
    用户报的第二半："勾选「关闭启动器时一并结束 dsh 进程」同样无效"。
    根因：`DshIsRunning` 只看内存里的 `DshCurrentProcess`，而整合包管理页的「状态」用的是
    `DshProcessAlive`（会回退到按端口判断）。进程引用一丢（DshStop 的 Finally、
    安装流程的 DshClearRunningState、进程自然退出后没人复位……），两者就给出矛盾结论。
    → 统一成 `DshInstanceIsAlive(Instance)`：① 内存引用指向它且进程活着 ②
      端口上有 dsh 在应答（401/303/200，别的程序占端口不会给这些码）。
      启动判断、"关闭 DSH"判断、退出时判断全部改用它。

84. **"关闭"这类操作的兜底要让路给失败，而不是扩大打击面**。
    三级兜底里前两级（内存引用 / 按实例目录与 DSH_HOME 匹配）是**有依据**的；
    第三级只是"实在找不到就扫一遍"，本身证据最弱。教训是：
    兜底级的匹配条件必须比正常级**更严**（我这里反了，写得更宽），否则兜底会变成误伤。

85. **PCL 的 `.vbproj` 用显式文件列表，新建的 `.vb` 必须手工登记**（否则 XAML 报"命名空间 PCL 中不存在标记"）。
    现象：新建 `Controls\MyWrapStretchPanel.vb` 后，XAML 里写 `<local:MyWrapStretchPanel>` 编译报
        MC3074: XML 命名空间"clr-namespace:PCL"中不存在标记"MyWrapStretchPanel"
    原因：PCL 的 vbproj **不是** SDK 风格的通配符包含，每个文件都有一条 `<Compile Include="..." />`。
    → 修法：照着同目录同类控件的写法补一行（放在 `Controls\MyResizer.vb` 附近即可）。
      **教训：这个仓库里"新建文件"不是把文件放进去就完事，还要登记。**

86. **固定列数的 Grid 在窗口变窄时会把右侧控件裁掉，应改用自适应容器**（用户反馈）。
    「整合包」卡片那行 5 个按钮原来是 `Grid` + 5 个 `Width="Auto"` + `SharedSizeGroup="Button"`，
    窗口一窄「启动 DeepSeekHarness / 关闭 DSH」就被切掉，看不到也点不到。
    换成自写的 `MyWrapStretchPanel`：
      · 一行放得下 → 把剩余宽度**平分**给各子项（与卡片内其他控件左右对齐）
      · 放不下     → 自动换行，保证每个按钮完整可见可点
    为什么不用 WPF 自带的 `WrapPanel`：它只换行、**不分配剩余宽度**，
    于是宽窗口下按钮挤在左边、右边留一大片空白，和卡片里其他控件对不齐。
    顺带去掉了 `SharedSizeGroup`：它强制 5 个按钮同宽，而「启动 DeepSeekHarness」文字更长，本来就该更宽。

87. **验证"窄窗口下的布局"不能靠截图**（实测教训）。
    我原想截图对比，结果发现 **PCL 在又窄又高的窗口下整个页面都不渲染内容，截图是纯空白**；
    而且靠 Tab 序列去点页面还会误触发别的按钮（实测把「启动」按了，直接打开了浏览器）。
    → 改成让控件把**自己的排布结果写进日志**（`LastLayoutText` = "可用宽 X → N 行：[每行几个]"），
      再逐档改窗口宽度读日志。这样任何尺寸下都能客观确认，也顺便留下了回归验证的手段。
    **通用教训：凡"肉眼不好判断/截图不可靠"的 UI 行为，都让控件把关键布局数据打进日志，
      再用改尺寸的方式逐档验证。**

88. **`dsh plugin` 把参数透传给 pnpm，所以 pnpm 是插件功能的硬依赖**（用户实报"似乎不会真的安装"）。
    实机证据链：
      · 系统 PATH 上没有 pnpm → 执行 `dsh plugin --profile web add dshmarket` 失败
      · dsh 自己的诊断日志（profile/.plugin-manager/logs/*/pnpm.log）写着：
            Command failed with exit code 1: pnpm add "dsh plugin --profile web add dshmarket"
        —— **整条命令被当成了一个包名**
      · dsh 源码 plugin.js 里也有对应的提示：
            if (result.exitCode === 127) process.stderr.write("dsh: pnpm was not found; install pnpm and make it available on PATH.\n")
    根因两层：
      ① 机器上根本没装 pnpm（只有 npm 与 corepack）；
      ② 我把参数拼成一个命令行字符串交给 `ProcessStartInfo.Arguments`，被二次解析后粘连。
    修法：
      ① 参数改**按数组传**（`DshRunCliArgs` + `DshQuoteArgs` 手工按 Windows 规则转义）。
         ⚠ **.NET Framework 4.8 没有 `ProcessStartInfo.ArgumentList`**（那是 .NET Core 2.1+ 的 API），
           直接写会报 BC30456"不是 ProcessStartInfo 的成员"，必须自己转义。
      ② `DshEnsurePnpm`：优先用 Node 自带的 **corepack** 把 pnpm 装到**整合包自己的目录**
         （`corepack enable --install-directory <DSH_HOME>\pnpm pnpm`，并设 `COREPACK_HOME` 到
          `<DSH_HOME>\corepack`）—— 这样不污染系统、删除整合包即彻底卸载；
         失败才退回 `npm install -g pnpm`。

89. **pnpm 的 store 位置只能靠命令行 `--store-dir` 指定，环境变量都不认**（隔离必需）。
    pnpm 默认把内容寻址仓库放在 `<DSH_HOME 所在盘>\.pnpm-store`，实测落到了
    **`E:\DSHarness\.pnpm-store` —— 用户全局 DSH 的 store**，同一盘上所有整合包共用它，
    直接破坏"整合包之间隔离"这条铁律。
    三种方式实测对比：
      ✘ 环境变量 `npm_config_store_dir` —— pnpm 12 不认（`pnpm store path` 仍返回全局那个）
      ✘ 环境变量 `PNPM_STORE_DIR`       —— 同样不认
      ✘ 写 profile 的 `.npmrc`           —— 也不认
      ✔ **命令行 `--store-dir <路径>`**  —— 生效（生成 `<DSH_HOME>\pnpm-store\v11`）
    → 所以 `DshPnpmStoreDir()` 返回路径，由 `dsh plugin ... --store-dir <路径> add/remove <包>` 透传。

90. **★ 别把插件从 `dsh.profile.bundles` 里摘掉 —— 那会让它根本不加载。**
    我第一版想当然地"装完把包名从 bundles 摘掉，好让界面能列出它、也能用 patch 关掉"。
    **这个判断是错的，而且后果比原 bug 严重**：dshmarket 的 package.json 里写着
        "dsh": { "bundle": { "patch": "./cordis.patch.yml" } }
    说明它是 **bundle 类型插件**，`dsh.profile.bundles` 就是它的加载入口；
    摘出去 = 装了等于没装。
    真正的问题是**扫描逻辑**：原来把"在 bundles 里"一律当作内置包隐藏，
    而 `dsh plugin add` 会把新装的包**自动写进 bundles**（实测确认），
    于是"装完列表里看不到"。
    → 正确修法：维护一份**框架自带的基础 bundle 白名单**（dsh-base / dsh-web-app / dsh-app-boot），
      只有白名单内的才算内置；bundles 里其余条目照常当用户插件列出。
    **教训：改数据结构的"归属"之前，先确认那个字段是不是这东西的加载入口。**

91. **判断"是不是 dsh 插件"要看 package.json 的 `dsh` 字段或 keywords**：
    dshmarket 的 `keywords` 含 `dsh-plugin`，且带 `dsh.bundle` 字段。
    搜索界面用这两条做标记与排序（`LooksLikePlugin`），比只按包名猜准得多。
    另外搜索 URL 要**沿用用户配置的 registry**（`DshNpmSource`：0=官方 1=国内镜像），
    否则会出现"安装走镜像、搜索走官方"的割裂（国内直连 registry.npmjs.org 又慢又可能不通）。

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
