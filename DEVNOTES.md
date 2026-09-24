# DEVNOTES — PCL2 魔改为 DeepSeekHarness 启动器

> 这份文档是给"未来的我"（下一次继续这个项目的 AI/人）看的。
> 记录**目标、约束、已核实的外部事实、避坑清单、进度**。
> 改动前请先读 §7 的"工作流程"，并遵守 §8 的"注意事项"。

最后更新：2026-09-24 ・ 启动器版本：`v0.3.4`

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
| v0.4.0 | — | 术语清理：启动页/关于页的 Minecraft 残留文案、账号与皮肤入口隐藏 | ⏳ 待做 |
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

1. **没有实机运行验证过**。v0.3.0 只做到「编译通过 + 静态审查」，没有真正启动过整合包。
   第一次运行请重点检查：
   - `DshEnsureFirstRun` 的弹窗顺序是否会卡住 UI（`RunInUiWait` + `WaitForExit` 的组合）
   - `DshInstanceListLoader.WaitForExit()` 在 UI 线程被调用会不会死锁
     （`DshEnsureFirstRun` 在后台线程调用，应该没问题；`PageDshManager` 里在 UI 线程调用过，需要实测）
   - `ModDshLaunch` 的 stdout 时序：dsh 是否真的把 URL 打到 stdout
2. **技能开关的实现依赖 dsh 的扫描规则**（只认一层目录 + `SKILL.md`），已按源码核实，但没实测过。
3. **插件开关写 `cordis.patch.yml` 的格式**是按 dsh 文档推断的（`- name: X` / `disabled: true`），
   需要实测确认；`DshReadDisabledPlugins` 的解析也是按这个格式写的。
4. **DshManager 页面的布局没有设计稿**，是直出实现的，视觉上还需要打磨。
5. `ModDshInstall` 的 npm 安装没有接 PCL 的下载任务栏（`LoaderTaskbar`），进度只在日志里。
6. 启动页仍然显示 Minecraft 的账号/皮肤区域（已设为 `IsHitTestVisible = False` 并隐藏部分元素），
   但没有彻底移除，术语清理留待 v0.5.0。
