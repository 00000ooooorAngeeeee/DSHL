# CHANGELOG

本文件记录 **PCL2 → DeepSeekHarness 启动器** 魔改版本的全部变更。
版本号规则见 `DEVNOTES.md` §6：修 bug 进补丁位，加功能进次版本位。

---

## [v0.3.5] — 2026-09-24

**修复用户反馈的两个 UI 问题**（都已在真实界面中复核通过）。

### 修复
1. **「加载中」加载完了也不消失**（用户截图反馈）
   根因：`PageLoaderInit(LoaderUi, PanLoader, PanContent, ...)` 只切换 **PanLoader / PanContent / PanAlways
   的 Visibility**，并不会去动加载环本身。我把 `<local:MyLoading>` 写成了 `PanLoad` 的**兄弟节点**，
   所以加载完成后它一直挂在界面上。官方页面的写法是把它放在容器**内部**：
   ```xml
   <local:MyCard HorizontalAlignment="Center" VerticalAlignment="Center" x:Name="PanLoad" UseAnimation="False">
       <local:MyLoading Text="正在获取 DSH 版本列表" x:Name="Load" ShowProgress="True" />
   </local:MyCard>
   ```
   已按官方写法重排，并把 `PanLoad` 由 `StackPanel` 改为居中卡片（加载环居中显示更好看）。
2. **版本对话框没有「取消」按钮**（用户反馈）
   根因：`MyMsgBox` 最多三个按钮，我原来把三个位置分给了「安装 / 安装并绑定 / 查看更新说明」；
   在没选中整合包时中间那个还是空的，于是用户**完全没有取消的出口**。
   现在：① 主操作（安装 / 重新安装）② 有整合包时「安装并绑定到 X」，否则「查看更新说明」
   ③ **始终是「取消」**。并在"还没选整合包"时于正文补一句说明。

### 新增
- 版本条目增加**右键菜单**（PCL 标准做法）：`重新安装该版本` / `查看更新说明` / `卸载该版本`，
  按状态自动置灰。次要且有破坏性的操作移到这里，左键弹窗就只剩"安装 / 绑定 / 取消"三件事。

### 变更
- `ModBase.vb`：版本号 `0.3.4` → `0.3.5`。
- `DEVNOTES.md`：新增 3 条（#32 加载环必须放 PanLoad 内部、#33 MyMsgBox 要留取消出口、
  #34 用 PrintWindow + UIA 可靠复核 UI）。

### 复核证据（真实界面）
- 进入「下载 → DSH 版本」后加载环正常消失；UI 树读出两个分组与 **28 个版本**
  （含 `npm 无此版本` 与 `已安装` 标注）。
- 点击版本条目弹出的对话框，UI 树读出三个按钮：
  `'安装'`、`'安装并绑定到「新整合包」'`、`'取消'`。

---

## [v0.3.4] — 2026-09-24

**🎯 需求 1 在真实 GUI 中端到端验证通过**，并修掉验证过程中暴露的 1 个隐蔽缺陷。

### 验证证据（真实运行的日志，不是推断）
```
[DSH 启动] 启动命令：node.exe "...\bin\DSH\versions\0.1.7-rc.1\node_modules\@deepseek-ai\dsh\lib\bin.js"
                     --profile web --host 127.0.0.1 --port 3421 --no-open
[DSH]      dsh web: http://127.0.0.1:3421/?token=NeBXMAzgXkPWRencoW0LUBSy8ngIEwSy-GpjPwmGcX0
[DSH 启动] DeepSeekHarness 已就绪：http://127.0.0.1:3421/?token=NeBXMAzg...X0
[ModDshLaunch] 正在用默认浏览器打开：http://127.0.0.1:3421/?token=NeBXMAzg...X0
```
对该地址发起的实际 HTTP 请求：**200**，32,959 字节，**`<title>DeepSeek Harness</title>`**，
最终重定向到干净地址（token 已换成 cookie）。不带 token 访问为 401 —— 鉴权行为符合预期。

退出流程同样验证通过：`DshStopOnExit=True` → 弹「是否一并关闭」→ 选「一并关闭」→
dsh 进程结束、端口释放（HTTP 000）、缓存的 token 地址被自动清除。

### 修复
- **"端口通就复用"的策略是错的**。dsh 的访问 token 是**进程级**的，启动器无法事后拼出来；
  而端口上可能是启动器不知道的 dsh 进程（实测遇到过：那个进程其实是**启动失败**的，
  它的 401 会误导我们"复用"，用户却以为启动成功了）。
  改为：把带 token 的可用地址**缓存到实例目录的 `.pcl-web-url`**，复用时先验证该地址当前仍可用
  （303/200），验证不过就换空闲端口重新启动我们自己的实例。关闭 dsh 时一并清除缓存。

### 新增
- `DshCachedUrlSave` / `DshCachedUrlLoad` / `DshUrlUsable`：访问地址的缓存、读取与有效性校验。
- `DshStateText`：一行打印 `DshIsRunning` / `DshStopOnExit` / 实例 / 端口 / URL 的诊断信息，
  在 `EndProgram` 里打日志——排查"退出时是否结束进程"这类问题时非常省事。

### 变更
- `EndProgram` 的退出处理重写：先打诊断日志，再按设置询问，并把用户的选择也记进日志。
- `ModBase.vb`：版本号 `0.3.3` → `0.3.4`。
- `DEVNOTES.md`：新增 3 条记录（#29 端口复用陷阱、#30 测试脚本写 INI 的 BOM 坑、
  #31 UI 自动化验证 PCL 界面的可行手段），并把"需求 1 已端到端验证"的证据链写进待验证清单上方。

### 本轮验证方法（不依赖人手动点击）
PCL 是自绘控件，UI Automation 里按钮只暴露为 `ControlType.Text` 且 `BoundingRectangle` 在 150% DPI 下不可信。
改用：`PostMessage(hwnd, WM_CLOSE)` 触发正常关闭流程 + `keybd_event` 发回车（PCL 的「回车 = 点启动按钮」）
+ **以日志文件判定动作是否发生** + 窗口相对坐标小网格扫描定位控件。

### 验证结果总览（8 条需求）
| 需求 | 验证方式 | 结果 |
|---|---|---|
| 1 启动按钮 → dsh + 自动开浏览器 | 真实 GUI 端到端 + HTTP 实测 | ✅ 通过 |
| 2 整合包级版本隔离 | 实例独立 DSH_HOME、端口 3080→3081 自动避让、全局实例未受影响 | ✅ 通过 |
| 3 下载页 alpha/rc 分类 + 时间倒序 | 真实 API 数据：28 个版本、alpha 13 / rc 15、倒序正确、2 个 npm 缺失版本被标注 | ✅ 通过 |
| 4 插件/技能开关 | patch 格式经 dsh dump 实证生效；pom 安装命令 dry-run + 真实安装通过；技能改名法按源码核对+等价验证 | ✅ 通过 |
| 5 设置改环境/本体位置 + 首次引导 | 设置页控件在 UI 树中确认；引导流程实机走完 | ✅ 通过 |
| 6 版本号 / CHANGELOG | v0.1.0 → v0.3.4，每轮都记 | ✅ 通过 |
| 7 备份 | 6 个 zip 快照 + git tag | ✅ 通过 |
| 8 DEVNOTES | 31 条避坑记录 + 构建手册 + 证据链 | ✅ 通过 |

---

## [v0.3.3] — 2026-09-24

**机制级实测**：直接复刻启动器会发出的命令，验证需求 1（自动开浏览器）与需求 4（插件/技能开关）背后的真实机制，
发现并修掉 2 个会导致功能完全不可用的缺陷。

### 修复
1. **打开的浏览器地址缺少 `?token=`，用户只会看到 401**（需求 1 的致命缺陷）
   实测三种访问结果：
   | 请求 | 结果 |
   |---|---|
   | `GET /`（不带 token） | **HTTP 401** |
   | `GET /?token=XXXX`（首次） | **HTTP 303**，换发 cookie 并重定向到干净路径 |
   | `GET /`（带 cookie） | **HTTP 200**（正常进 GUI） |

   源码印证：`browserAuth.authenticatedUrl()` 把**进程级** `launchToken` 作为唯一鉴权输入写进 URL
   （`url.searchParams.set(TOKEN_QUERY, this.launchToken)`），而 `localWebUrl()` 返回的是不带 token 的干净地址。
   我原来的正则 `https?://127\.0\.0\.1:\d+` 只截到端口号，把 `?token=...` 丢掉了——**点启动必然看到未授权页**。
   修复：正则补上 `[^\s"'<>)]*` 把 query string 一起抓下来，并调整就绪判定（优先等带 token 的地址，
   拿到后才开浏览器）；同时区分"端口被占用"的三种情形——用新增的 `DshProbeHttpStatus` 探测，
   是 dsh（401/303/200）就复用并提示 token 是每进程独有的，不是 dsh 就自动换端口。
2. **`cordis.patch.yml` 写空文件会让整个 profile 起不来**
   实测报错：`Error: overlay ...\cordis.patch.yml must be a top-level YAML array of loader patch entries`。
   我原来的内建 profile 模板往这个文件里写了一个空行，`DshSetPluginEnabled` 清掉最后一条后也会留下纯注释——
   两种都会让该整合包的 dsh **完全无法启动**。
   修复：空态统一写 `[]`（注释放在上面），并把插件开关改为**按 YAML 块解析/重建**，保留用户自己写的无关 patch。

### 实证的机制（需求 4 的开关能力已确认可用）
- **插件开关的 patch 格式被 dsh 正确应用**：写入
  `- id: plugin-manager` / `name: "@deepseek-ai/dsh-plugin-manager"` / `disabled: true` 后，
  `--dump-config` 输出变为
  `# == @deepseek-ai/dsh-base, patched by ...\cordis.patch.yml` + `disabled: true`
  （原值 `!!js '!ctx.get(''profileContext'')'` 被覆盖）→ **关闭生效**；
  移除条目后 `disabled` 恢复原值 → **启用生效**；多条 patch 可共存。
- **技能开关的改名法成立**：技能发现只扫技能根一层、认定 `<root>\<name>\SKILL.md` 且 `<name>` 匹配
  `/^[a-z0-9]+(?:-[a-z0-9]+)*$/`（按 dsh 源码逐条核对）。等价验证：加 `.disabled~` 前缀后不再被发现，
  去掉前缀即恢复。（`--dump-config` 看不到技能，技能目录是运行时服务，无法用 dump 验证。）

### 端到端验证记录（沿用 v0.3.2 的环境）
- `dsh web --host 127.0.0.1 --port 3412 --no-open` 实测启动成功，stdout 输出带 token 的地址。
- 该实例的 profile 由 `dsh --profile web --dump-config` 正常初始化（4 个文件齐备）。

### 变更
- `ModDshBase.vb`：新增 `DshProbeHttpStatus`（判断占用端口的是不是 dsh）。
- `ModDshLaunch.vb`：URL 正则带上 query string；就绪判定与端口占用处理重写；补充 token 相关日志与提示。
- `ModDshHome.vb`：新增 `DshEmptyPatchText` / `DshParsePatchBlocks` / `DshPatchBlockMatches` /
  `DshPluginIdFromPackage`；`DshSetPluginEnabled` 改为块级重建；`DshReadDisabledPlugins` 改为块级解析
  （支持 `true/yes/on/1` 四种真值写法）。
- `ModBase.vb`：版本号 `0.3.2` → `0.3.3`。
- `DEVNOTES.md`：新增 3 条避坑记录（#26 token、#27 patch 顶层数组、#28 技能改名法）。

### 仍待验证
- 在真实 GUI 里点一次「启动 DeepSeekHarness」，确认浏览器打开的是 GUI 而不是 401（机制已实证，差最后一步点按）。
- 关闭启动器再重启后，对"上次留下的 dsh 仍在跑"这一情形的实际体验。

---

## [v0.3.2] — 2026-09-24

**端到端跑通「首次引导 → npm 安装 dsh → 新建整合包 → 导入现有 DSH_HOME」全流程**后修掉的 3 个真 bug。

### 修复
1. **切换下载页必崩（`InvalidCastException`）——本轮最严重的问题**
   日志证据：
   ```
   程序出现未知错误：无法将类型为"System.Windows.Controls.TextBlock"的对象强制转换为类型"PCL.MyListItem"
     在 PCL.FormMain.PageChange 行号 1367
   ```
   根因：`PageChange` 用 `CType(FrmDownloadLeft.PanItem.Children(SubType), MyListItem)` 勾选条目，
   而 `PageDownloadLeft` 的 StackPanel 里**分组标题 TextBlock 也占下标**：
   原版是 `[0]原版游戏 [1]社区资源标题 [2..6]五个条目 [7]分组标题 [8]…`，枚举是 `DownloadMod=2 … DownloadShader=6`。
   我在维护过程中删掉了下标 7 的标题，`ItemDsh` 落到下标 7 而枚举也是 7（看似巧合其实内部全错位），
   于是 `Children(7)` 拿到 TextBlock 强转 `MyListItem` 崩溃。
   修复：恢复标题使 `ItemDsh` 回到下标 **8**，并把 `PageSubType.DownloadDsh` 与两处 `Tag` 同步改为 **8**，
   同时写了核对脚本确认 `PageDownloadLeft`（0/2/3/4/5/6/8）与 `PageSetupLeft`（0/1/2/3/4）下标与枚举全部一致。
2. **`.dsh-installed` 标记写入时抛 `FormatException`**
   `FormatException: 无法为字符"'"找到匹配的引号字符`。日期格式字符串 `yyyy'-'MM'-'dd HH':'mm':'ss'` 末尾多了一个引号。
   `ModDshHome` 的 cordis.patch 注释里也有同样问题。两处都改掉，并加了一个"逐文件检查日期格式引号奇偶性"的审计。
3. **整合包"不可启动"时提示为空的括号**
   `启动按钮：整合包 新整合包 尚不可启动（）`——那是因为 dsh 版本还没装完，`ErrorMessage` 为空。
   现在这种情况显示「整合包「X」绑定的 dsh Y 尚未安装，点此去安装」，避免误导。

### 端到端验证记录（本轮确认可用）
- dsh 版本安装链路完整跑通：首次引导点「安装推荐版本」→ 正确发出
  `npm install --prefix … --registry=https://registry.npmmirror.com/ "@deepseek-ai/dsh@0.1.7-rc.1"`，
  npm 成功安装 512 个包，`node_modules\@deepseek-ai\dsh\lib\bin.js` 入口存在。
- 新建整合包向导完整跑通：名称 → 版本 → 导入确认 → 工作区选择 → 创建。
- **「从现有 DSH_HOME 导入」实测成功**：从 `E:\DSHarness\.dsh` **导入 21 个技能**
  （含 `.disabled` 目录）+ `profiles\web`（含插件与配置）+ `.credentials.yaml` + `.anonymous-user-id`。
- **端口自动避让验证成功**：用户的全局 DSH 正占用 3080，新建的整合包自动分到 **3081**，
  不会干扰用户正在运行的实例。
- `instance.json` 与 `PCL\Setup.ini` 内容正确（版本、端口、工作区、创建时间）。
- 首次启动引导流程简化后可正常走完（Node 已存在 → 直接进第 2 步安装 dsh）。

### 变更
- `ModBase.vb`：版本号 `0.3.1` → `0.3.2`。
- `DEVNOTES.md`：新增 5 条避坑记录（#21 左列表下标必须与枚举一致、#22 `WaitForExit` 会覆盖 `Loader.Input`、
  #23 日期格式引号必须成对、#24 内置 profile 不能用 `--from-default-profile`、#25 `dsh --dump-config` 退出码为 1）。

### 仍待验证
- 实际启动整合包（`dsh web` 起来 + 浏览器自动打开）。
- 技能/插件开关的实际生效效果（格式已获官方 dump 印证，但没点过开关）。
- `PageDshManager` 在 UI 线程调用 `WaitForExit()` 是否卡顿。

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
