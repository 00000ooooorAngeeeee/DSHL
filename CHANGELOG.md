# CHANGELOG

本文件记录 **PCL2 → DeepSeekHarness 启动器** 魔改版本的全部变更。
版本号规则见 `DEVNOTES.md` §6：修 bug 进补丁位，加功能进次版本位。

---

## [v0.5.2] — 2026-09-24

### 修复：第二次从启动页点「整合包管理」会显示「个性化」
上一版我用 `IsPageSwitched` 做守卫，**但诊断日志证明这个条件在读的时候还是 `False`**：
```
切换主要页面：Setup, SetupManager
设置页 Loaded（DSH）：IsPageSwitched=False, PageID=SetupManager, Manager=True, UI=False
设置页 Loaded（DSH）：无人指定子页面，默认选中「个性化」   ← 覆盖发生在这里
```
**时序真相**：`PageChange` 会先设置 `PageID = SetupManager`、`ItemManager.Checked = True`，
然后在 `PageChangeRun` 里把控件挂到可视树 —— 此时触发 `Loaded`，
**而 `IsPageSwitched = True` 是在这之后才执行的**。所以 `Loaded` 里读到 `IsPageSwitched = False`，
守卫失效，于是执行了"默认选中个性化"把刚设好的管理页覆盖掉。

**修法**：守卫改为直接看**已经是正确目标值**的那两个状态：
```vb
If IsPageSwitched OrElse PageID = PageSubType.SetupManager OrElse ItemManager.Checked Then Return
```
另外在 `FormMain.PageChange` 的 `PageChangeActual` **之后**再加一次纠正（`SetChecked` 幂等，重复无害），
用来对抗"某条我没枚举到的路径里 Loaded 晚于一切"这种情况。

**实机确认**（加了临时诊断日志，逐次访问都验证）：
- 第 1 次：`IsPageSwitched=False, PageID=SetupManager, Manager=True`，**不再出现"默认选中个性化"** ✔
- 第 2 次：同上 ✔

### 教训（DEVNOTES #73）
**用"某个标志位"做时序守卫时，必须确认这个标志位在读取点已经被赋值。**
`IsPageSwitched` 名字看起来正合适，但它的赋值发生在 `Loaded` 触发之后 —— 名字对不代表时序对。
更稳的做法是判断**目标状态本身**（`PageID` / `ItemManager.Checked`），而不是判断"有没有人操作过"。

### 变更
- `ModBase.vb`：版本号 `0.5.1` → `0.5.2`。
- `PageSetupLeft.xaml.vb`：守卫条件改用 `PageID` / `ItemManager.Checked`；保留诊断日志。
- `FormMain.xaml.vb`：`PageChangeActual` 之后补一次选中项纠正。
- `DEVNOTES.md`：新增 #73（时序守卫要用目标状态而不是标志位）。

---

### 修复：启动页点「整合包管理」跳到了「个性化」页
从启动页点「整合包管理」会走 `FormMain.PageChange(Setup, SetupManager)`，它已经
`SetChecked(ItemManager)` 了；但紧接着 `PageSetupLeft.Loaded` 又**无条件**
`ItemUI.SetChecked(True)`，把选中项覆盖成「个性化」，右面板也就跟着显示错页。

**修法**：用本类已有的 `IsPageSwitched` 标记做守卫（`PageChange` 开头会把它置 `True`），
即"已经有人明确指定了子页面，就别再改选中项"。

### 修复：点「整合包管理」卡顿约 1 秒
`PageDshManager` 被**创建了两次**：
- `BtnVersion_Click` 里一次
- `FormMain.PageChange` 的 Setup 分支里我又补了一次

每次构造都要读整合包列表、插件、技能等，所以有可感知的卡顿。
删掉 `FormMain` 里那句后，导航耗时从 **~1s 降到 ~0.53s**（实测日志）。

### 变更：设置页左栏去掉「联机」
用户明确不需要。`ItemLink` 保持 PCL 原本的 `Collapsed`。

最终左栏为：**个性化 / DSH 运行环境 / 整合包管理**。

### 两条通用教训（记入 DEVNOTES #71/#72）
1. **初始化代码里"设默认值"的动作，一定要先检查有没有人已经显式设过。**
2. **同一个对象的"按需创建"只留一处**，别在调用链的两端各写一遍。

### 实机确认
- 日志：`切换主要页面：Setup, SetupManager` ✔
- 截图：左栏「整合包管理」为选中态，右面板显示整合包管理页 ✔
- 无异常

---

### 变更：设置页左栏改成用户确认的最终形态（B 方案）
上一版我把设置页左栏收得太狠——`UiHiddenSetupUi` 也设成了 True，导致左栏只剩一项、
随后又被 PCL 的「可选子页面少于 2 个就整块收起左栏」规则藏掉，**整条左栏都消失了**。
用户确认他想要的（附截图）是：

```
联机
个性化
DSH 运行环境
整合包管理
```

现在固定的开关改为：

| 开关 | 值 | 说明 |
|---|---|---|
| `UiHiddenSetupLaunch` | True | 隐藏「启动」（MC 启动设置） |
| `UiHiddenSetupSystem` | True | 隐藏「其他」 |
| `UiHiddenSetupUi` | **False** | **保留「个性化」** —— 主题、背景图片、背景音乐这些用户要用 |
| `UiHiddenSetupLink` | False | 保留「联机」 |
| `UiHiddenPageOther` | True | 顶部导航隐藏「更多」 |

默认子页面也改为「个性化」（即用户截图里的选中项）。

### 变更：「整合包管理」从隐藏占位升级为可见条目
原来它只是为占住下标 5 而存在的 `Visibility="Collapsed"` 占位，入口只有启动页那个按钮，太深。
现在：
- DSH 模式下设为可见（带上了图标），设置页左栏可直接进整合包管理
- `PageCheck` 的 `Handles` 里补上了 `ItemManager.Check`（**不加这一条点了不会切页**）

### 说明
- 「联机」这一项由 PCL 硬编码为 `Collapsed`，本版在 DSH 模式下覆盖回可见，以贴合用户要求的形态
  （点进去是 PCL 自带的联机页，DSH 模式下不使用，但保留更接近用户预期的布局）。
- 左栏条数不能再少：PCL 有 `AvaliableCount < 2` 就整块收起左栏的规则，而「联机」不计入该计数，
  所以实际计数项是 个性化 / DSH 运行环境 / 整合包管理。

### 实机确认
左栏四项与用户截图一致；右面板正常显示内容；无异常。

### 变更清单
- `ModBase.vb`：版本号 `0.4.4` → `0.5.0`。
- `ModDshBase.vb`：`DshApplyModeHideSettings()` 里 `UiHiddenSetupUi` 改为 False。
- `PageSetupUI.xaml.vb`：`DshApplySetupLeftVisibility()` 保留 联机/个性化/整合包管理，强制左栏可见。
- `PageSetupLeft.xaml` / `.xaml.vb`：`ItemManager` 加图标、接入 `Handles`；默认子页面改为「个性化」。
- `DEVNOTES.md`：新增 3 条（#67 设置页最终形态、#68 整合包管理条目要接 Handles、
  #69 用户回字母＋截图时以截图为准）。

---

### 修复：设置页左栏「时好时坏」的真正根因（用户给出的现象链定位到的）
用户描述的现象链是决定性的：
```
启动 → 设置（左栏漏出 启动/个性化/其他，不正常）
     → 个性化（正常）
     → 任意页面 → 再进设置（又不正常）
```
**根因**：`PageSetupUI.HiddenRefresh()` 的第一行是
```vb
If FrmMain.PanTitleSelect Is Nothing OrElse Not FrmMain.PanTitleSelect.IsLoaded Then Return
```
第一次进设置页时 `PanTitleSelect` 还没 Loaded，**整个函数体被跳过** —— 我上一版把 DSH 规则加在它末尾，
所以第一次进设置页根本不执行。点一下「个性化」之后它才 Loaded、规则生效（于是"正常"）；
再切走后状态又变（于是"又不正常"）。

**修法**：把规则抽成独立的 `Public Shared Sub DshApplySetupLeftVisibility()`，由
`PageSetupLeft.Loaded` **直接调用**（不依赖 `HiddenRefresh()` 的时机），
同时仍保留在 `HiddenRefresh()` 末尾以覆盖"改设置项后"的刷新。

**实机确认**（两种进入路径都测了）：
- 启动器直接落在设置页 → 左栏整块消失，只剩 DSH 内容 ✔
- 第二次启动（上次停在设置页，等价于"从别的页面进设置"）→ 同样只剩 DSH 内容 ✔

### 教训（记入 DEVNOTES #65/#66）
- **依赖"某个公共方法一定会跑"之前，先读它开头的守卫条件。**
- **遇到"时好时坏"的 bug，先请用户描述复现路径** —— 这次比我自己反复截图猜快得多。

### 变更
- `ModBase.vb`：版本号 `0.4.3` → `0.4.4`。
- `PageSetupUI.xaml.vb`：新增 `DshApplySetupLeftVisibility()`；`HiddenRefresh()` 末尾改为调用它。
- `PageSetupLeft.xaml.vb`：`Loaded` 里直接调用它；删除已被取代的自定义 `ApplyDshModeVisibility()`。
- `DEVNOTES.md`：新增 2 条（#65 公共方法的提前返回要先看、#66 让用户描述复现路径）。

---

### 修复：两个「首次正常、往返后失效」的隐藏 bug（用户反馈）
用户报的两条现象，根因是同一个：**「第一次进入」和「从别的页面返回」走的是不同的刷新路径。**

1. **设置页左栏：第一次进会漏出「启动 / 个性化 / 其他」，返回后才干净**
   `PageSetupUI.HiddenRefresh()` 会被多条刷新路径调用，而第一次进设置页与返回设置页的调用时机不同。
   修法：把 DSH 的收紧规则放进 `HiddenRefresh()` **末尾**，这样任何一次刷新都会重新施加，结果稳定。
   顺带说明：DSH 下把设置子页面全隐后，正好命中 PCL 自己的规则
   `PanItem.Visibility = If(AvaliableCount < 2 ..., Collapsed, Visible)` —— 整个左栏会消失，
   **这正是期望效果**（只留右面板的 DSH 设置）。

2. **启动页：「正版 / 离线」第一次藏住了，返回后又冒出来**
   `PageLaunchLeft.RefreshPage` 的 `UnknownType` 分支里有 `PanType.Visibility = Visibility.Visible`，
   每次进启动页都会执行，把上一轮的隐藏覆盖掉。
   修法：不再逐个元素打补丁，而是给**整个登录区**套一层容器 `PanLoginArea`
   （内含 `PanLogin` 登录页面宿主 / `PanTypeOne` 登录方式标签 / `PanType` 正版离线按钮），
   DSH 模式下只折叠这一处，盖住 PCL 的所有零散赋值。
   实机确认：启动页只剩「下载 dsh」与「整合包管理」，账号 UI 全部消失。

**教训**：只要 PCL 会在多处给同一个元素赋 `Visibility`，就要用一个统一的父容器做「总开关」，
而不是逐个打补丁——补丁很容易漏，而且不同进入路径下表现不一致。

### 变更
- `ModBase.vb`：版本号 `0.4.2` → `0.4.3`。
- `PageSetupUI.xaml.vb`：`HiddenRefresh()` 末尾追加 DSH 收紧规则。
- `PageLaunchLeft.xaml`：新增 `PanLoginArea` 容器包住登录区（**未移除任何元素**，
  `PanLogin` 仍是登录页面宿主，只是换了父级）。
- `PageLaunchLeft.xaml.vb`：改为折叠 `PanLoginArea` 一处。
- `DEVNOTES.md`：新增 4 条（#61 两种刷新路径的差异与"总开关"思路、
  #62 PCL 的左栏自动隐藏规则、#63 搬动 XAML 元素要查重名与标签配对、
  #64 PCL 自绘控件无法用 UIA 编程导航）。

### 待用户确认
往返切换（启动 → 设置 → 启动）是否两个页面都稳定。我无法编程导航 PCL 的自绘控件
（UIA 里既没有 InvokePattern 也没有 SelectionItemPattern，Tab 焦点也容易被页面内控件吃掉），
所以这一步需要你点几下确认。

---

### 变更：把「功能隐藏」的结果固定下来（用户要求）
用户在 PCL 原版的「功能隐藏」页手动勾选出了想要的形态，要求固化。现在启动时由
`DshApplyModeHideSettings()` 显式写入下列开关，并且**固定成这个结果**：

| 开关 | 值 | 效果 |
|---|---|---|
| `UiHiddenSetupLaunch` | True | 设置页隐藏「启动」（全是 MC 启动设置） |
| `UiHiddenSetupUi` | True | 设置页隐藏「个性化」 |
| `UiHiddenSetupSystem` | True | 设置页隐藏「其他」 |
| `UiHiddenSetupLink` | False | 保留（顶部导航本来就硬编码隐藏了「联机」） |
| `UiHiddenPageOther` | True | 顶部导航隐藏「更多」 |
| `UiHiddenPageDownload` | False | **必须保留可见**，dsh 版本要从这里装（DEVNOTES #54） |
| `UiHiddenPageSetup` | False | 保留 |

**实机确认结果**（截图 + UIA）：
- 顶部导航：`启动 / 下载 / 设置`
- 设置页左栏：只有 `DSH 运行环境`
- 设置页右面板：运行环境状态 ＋ DSH 运行环境 ＋ 启动行为，**零 Minecraft 内容**
- 落盘校验：启动后 `Setup.ini` 里 7 个开关全部为我固定的值，无异常

### 更正 v0.4.1 里的一处误判
v0.4.1 的 CHANGELOG 写了「设置页『个性化』条目偶发仍显现，原因是 PCL 会把 `UiHiddenSetupUi`
回写成 False，尚未解决」。**这个判断是错的**——当时用户正在「功能隐藏」页手动勾选，
我读到的是中间状态。修正 `DshApplyModeHideSettings()` 的调用时机（放在 `InitializeComponent()`
之前）后，5 个开关全部按预期落盘，该问题不再存在。
教训已记入 DEVNOTES #60：**同一份配置被两个人同时动时，别急着下根因结论**。

### 变更
- `ModBase.vb`：版本号 `0.4.1` → `0.4.2`。
- `ModDshBase.vb`：`DshApplyModeHideSettings()` 改为显式固定 7 个开关（含显式写 False 的项）。
- `DEVNOTES.md`：新增 2 条（#59 用户手动配过的开关要固定、#60 别急着判定"设置被改"），
  并更正了 #56 里关于"被回写"的表述。

---

### 变更：把下载页 / 设置页 / 启动页里的 Minecraft 内容全部隐藏（用户要求）
| 页面 | 处理 |
|---|---|
| 下载页 | 隐藏「原版游戏」与「社区资源」整组（Mod / 整合包 / 数据包 / 资源包 / 光影包），只留「DSH 版本」 |
| 设置页 | 隐藏「启动 / 联机 / 个性化 / 其他」四个子页面（里面全是 MC 设置），只留「DSH 运行环境」；默认子页面也改成 DSH 运行环境 |
| 启动页 | 隐藏账号区域（`PanLogin` 登录面板块 ＋ `PanType` 的「正版 / 离线」按钮）；`BtnMore` 与 `BtnVersion` 功能重复，只留后者 |

**实现要点（不能直接删元素）**：`FormMain.PageChange` 里
`CType(FrmDownloadLeft.PanItem.Children(SubType), MyListItem)` / `FrmSetupLeft...` 都是**按下标取控件**的，
删掉元素会让 `DownloadDsh=8` / `SetupDsh=4` 全部错位并抛 `ArgumentOutOfRangeException`。
所以一律用 `Visibility = Collapsed`（不渲染也不占位）。

设置页那一组走 **PCL 自带的「功能隐藏」开关**（`UiHiddenSetup*` / `UiHiddenPageOther`），
因为 `PageSetupUI.HiddenRefresh()` 会按这些开关重新设置一遍显隐、把自己设的 Visibility 覆盖掉。

### 修复：`&#10;` 被当字面量显示
「DSH 运行环境」页的说明文字里写成了 `&#38;#10;`（双重转义），界面上直接显示「&#10;」这串字符。
改成 `&#10;`。

### 踩坑记录（都写进 DEVNOTES 了）
- **写设置项必须在 UI 线程**：后台线程调用 `Settings.Set` 会抛
  `InvalidOperationException：调用线程无法访问此对象，因为另一个线程拥有该对象`。
- **写设置项必须在 `InitializeComponent()` 之前**：`InitializeComponent` 会把「个性化 → 功能隐藏」里那些
  复选框（`SettingService.Key="UiHiddenSetupUi"` 等）建出来，绑定初始化时会拿内存里的旧值**回写设置项**。
  实测放在 `InitializeComponent()` 之后时，5 个开关里只有 2 个保住了 `True`。
- **「正版 / 离线」不在 `PanLogin` 里**，而在 `PageType` 同级的 `PanType` 网格里，两个都要隐藏。

### 变更
- `ModBase.vb`：版本号 `0.4.0` → `0.4.1`。
- `ModDshBase.vb`：新增 `DshApplyModeHideSettings()`。
- `FormMain.xaml.vb`：构造函数里在 `InitializeComponent()` **之前**写隐藏开关；
  `PageChange` 进入设置页且未指定子页面时，改用 `PageSetupLeft.PageID`（避免条件式地显示 MC 设置）。
- `PageDownloadLeft.xaml.vb`：新增 `ApplyDshModeVisibility()`，默认子页面改为 `DownloadDsh`。
- `PageSetupLeft.xaml.vb`：隐藏 MC 条目（含 `HiddenRefresh` 之后的兜底），默认子页面改为 `SetupDsh`。
- `PageLaunchLeft.xaml.vb`：隐藏 `PanLogin` / `PanType` / `BtnMore`。
- `PageSetupDsh.xaml`：修正 `&#38;#10;`。
- `DEVNOTES.md`：新增 4 条（#55 隐藏页面要走 PCL 的开关、#56 写设置项的两个硬约束、
  #57 正版离线按钮在 PanType 而不是 PanLogin、#58 XAML 换行要写 `&#10;`）。

### ⚠️ 一处未完全解决的问题（如实记录）
设置页左栏的「个性化」条目偶发仍会显现。原因是那个开关（`UiHiddenSetupUi`）被
「功能隐藏」页的复选框回写成 `False`，而 `PageSetupUI.HiddenRefresh()` 据此把它设回可见。
已用「在 InitializeComponent 之前写入」缓解（另外三个开关已稳定生效），但该键仍出现过被回写的情况，
**下一次要继续查**。其余部分（下载页、顶部导航、启动页账号 UI、设置页默认子页面）均已实机确认。

---

### 变更：隐藏顶部导航的「更多」页（用户要求）
「更多」下面全是 Minecraft 相关的内容（帮助 / 关于 / 百宝箱 / 反馈 / 投票），对 DSH 启动器没有意义。

**实现方式有讲究**：只把 `Visibility` 设成 `Collapsed`，**没有**从 `PanTitleSelect` 里移除元素——
`FormMain.PageChange` 会拿顶级页枚举值当 `PanTitleSelect.Children` 的下标，
移除元素会让下标整体错位并抛 `ArgumentOutOfRangeException`（与 v0.3.6 修复的崩溃是同一个机制）。

### 变更：把「整合包管理」的入口移到启动页
隐藏「更多」会连带丢掉一个入口——**整合包管理原本挂在「更多」页的左栏里**。
所以把启动页左栏的按钮在 DSH 模式下复用：

| 原来 | 现在 |
|---|---|
| 「版本选择」（进 PCL 的版本选择页） | 「**整合包管理**」（直连整合包管理页，管 dsh 版本 / 插件 / 技能） |

入口反而更显眼，且不再依赖「更多」页。

**「任务管理」不受影响**：它的入口是右上角那个按钮（`BtnExtraDownload`，ToolTip 就是"任务管理"），
不依赖「更多」页。

### 修复：隐藏「更多」页时会静默失败
第一版实机冒烟测出：
```
W [FormMain] 隐藏「更多」页失败：调用线程无法访问此对象，因为另一个线程拥有该对象。
```
原因：`FormMain` 的第三阶段初始化跑在 `RunInNewThread` 里，在**后台线程**改 UI 元素的 `Visibility`
会抛 `InvalidOperationException`——而且被我自己的 `Try/Catch` 挡住了，界面静默不变。
现在用 `RunInUi(...)` 包装。修复后实机日志：
```
I [FormMain] DSH 模式：已隐藏顶部导航的「更多」页
```

### 变更
- `ModBase.vb`：版本号 `0.3.9` → `0.4.0`。
- `FormMain.xaml.vb`：DSH 模式下用 `RunInUi` 隐藏 `BtnTitleSelect4`。
- `PageLaunchLeft.xaml.vb`：`BtnVersion` 在 DSH 模式下改文案为「整合包管理」，
  `BtnVersion_Click` 直连整合包管理页（走 `Setup` 子页面路由）。
- `DEVNOTES.md`：新增 2 条（#53 隐藏导航项只能改 Visibility 且必须 RunInUi、
  #54 隐藏页面前先查清挂在它下面的入口）。

---

## [v0.3.9] — 2026-09-24

### 修复：任务管理器卡片没显示安装的是哪个版本（用户反馈）
任务卡片的标题**只在创建时读一次 `Loader.Name`**（`PageSpeedLeft` 里 `Title="…Loader.Name…"`），
之后的刷新循环只更新副标题与控制项。所以「显示哪个版本」必须在 `Start` 之前把 `Loader.Name`
设成带版本号的形式。现在卡片标题与子任务名都是 `安装 dsh 0.1.7-rc.1`（实测日志确认）。

### 新增：真实的文件计数（用户反馈"没显示剩余文件数量"）
实测 npm 的可观测行为：
| 阶段 | `node_modules` 文件数 |
|---|---|
| 下载阶段（约 10~30 秒） | npm 把包下到**自己的缓存**，目录里恒为 1 个文件 |
| 解压阶段（约 20~40 秒） | 1 → 3382 → 19306 → 27455 |
| 结束时 | `node_modules\.package-lock.json` 的 `packages` 节点数 = 精确包总数（实测 511~512） |

于是加了一个 400ms 采样的文件计数看门狗，每 2 秒把「正在写入文件：N 个（共 M 个包）」写进日志与进度文字，
并用「文件数 / 估算总文件数」的平方根曲线推进进度（`0.15 + 0.71 × √Ratio`）；
下载阶段（文件数 ≤ 8）用一条很慢的时间曲线兜底，避免进度条纹丝不动。
实测日志：
```
暂存目录：…\install-0.1.7-rc.1-661657\（存在=True）
正在写入文件：1 个
正在写入文件：3382 个
正在写入文件：19306 个
正在写入文件：27455 个
npm 依赖图已就绪：共 511 个包
```

### 说明：任务管理器左栏的「下载速度」为什么仍然是 0
`PageSpeedLeft` 里：
```vb
LabSpeed.Text = StringUtils.FormatByteSize(NetManager.Speed) & "/s"
LabFile.Text  = If(NetManager.FileRemain < 0, "0*", NetManager.FileRemain)
LabThread.Text = NetTaskThreadCount & " / " & NetTaskThreadLimit
```
这三项都来自 **PCL 自己的网络栈**（`NetManager` / `NetTaskThreadCount`），
而 `NetManager.Speed` 是 **ReadOnly**，由 PCL 的 `LoaderDownload` 内部驱动。
npm 是**子进程**、完全不走 PCL 的下载器，所以这三项在架构上就测不到——
本版已经给出能测的那一项（已写入文件数），速率与线程数则无法提供，**不做假数据**。

### 顺带修掉一个静默 bug
`DirectoryUtils.EnumerateFiles` 的参数顺序是 `(folder, includeSubDirectories, searchPattern)`，
不是 .NET 的 `(path, searchPattern, searchOption)`。我按 .NET 顺序传参（第三个参数给 `SearchOption`），
被隐式转成 Boolean 抛异常，而外面套了 `Try/Catch` 于是**静默返回 0**——文件计数一直是 0 的根因。
现在改为显式传 `includeSubDirectories`，并逐项容错（npm 正在写文件，枚举途中可能消失）。

### 变更
- `ModBase.vb`：版本号 `0.3.8` → `0.3.9`。
- `ModDshInstall.vb`：新增 `DshSetInstallTaskName`、`DshInstallFileWatcher`、`DshCountFiles`、
  `DshReadExpectedPackageCount`、`DshSetProgressText`；`DshInstallStart` 先设任务名再启动。
- `DEVNOTES.md`：新增 4 条（#49 卡片标题只在创建时读 Name、#50 速度为何测不到、
  #51 npm 真实进度的观测方式、#52 EnumerateFiles 参数顺序坑）。

### 待你在真机确认
任务管理器卡片标题应显示 `安装 dsh 0.1.7-rc.1`，副标题会随进度更新为
「正在写入文件：N 个（共 M 个包）」。左栏的下载速度仍是 `0 B/s`（原因见上）。

---

## [v0.3.8] — 2026-09-24

### 新增：安装 dsh 接入 PCL 的「任务管理器（后台下载队列）」
上一版我理解错了需求——以为你要的是页面内的进度环。你要的是 PCL **原版那套后台下载机制**，
现在按 `LoaderTaskbar` 的约定接上了。

**PCL 的机制**（`LoaderTaskbar` ＋ 每 50ms 的 `LoaderTaskbarProgressRefresh`）：
- **右下角的下载按钮**会显示进度（`FrmMain.BtnExtraDownload.Progress`）
- **Windows 任务栏**出现进度条
- **「更多 → 任务管理」**里出现一张任务卡片，可以看到子项进度、也能取消

实现要点：
- 安装任务包了一层 `LoaderCombo(Of Integer)`。**必须包**——`LoaderTaskbarAdd` 只接受 `LoaderCombo(Of T)`，
  而且任务管理页会调用 `GetLoaderList()`，那是 `LoaderCombo` 才有的方法，
  把 `LoaderTask` 直接塞进 `LoaderTaskbar` 会抛 `MissingMethodException`。
- 任务栏的**注册/清理放在模块层**，不放在页面事件里：安装是后台任务，用户随时可能切走页面；
  若清理逻辑随页面销毁，任务管理器里会残留一张永远不消失的卡片。

### 修复：同一次安装会跑两遍 worker（隐蔽且后果严重）
实测日志：
```
11:20:39.441  <16 · L/安装 dsh 版本>  正在安装 dsh 0.1.7-rc.1     ← 第一次执行，正常
11:20:39.443  <16 · L/安装 dsh 版本>  启动进程：npm.cmd install…  ← npm 真的起来了
11:20:39.436  <13 · Invoke 72>       LoaderTask 状态改变：Loading ← 同一任务又被 Start 一次
11:20:39.447  <24 · L/安装 dsh 版本>  出错：已经有一个 dsh 版本正在安装中
```
根因：**`LoaderBase.Start(Input, IsForceRestart:=True)` 对【正在运行】的加载器也会返回 True**，
于是会 `TriggerThreadInterrupt()` 并在**新线程上再跑一遍 LoadDelegate**。
我写成"先 Start 子任务、再 Start 组合"，于是 worker 被执行两次：第二次撞上并发守卫抛错，
**界面显示"安装失败"，而 npm 进程还在后台偷偷下 500 个包**——最糟糕的失败模式。
修法：只 `Start` 最外层的组合，让组合的 `Update()` 去启动子任务（它按输入相等性判断，不会重启运行中的任务）。

### 修复：另外两处
- **并发守卫加 OwnerThread**：只用一个 Boolean 挡并发，在"同线程多次 Start"的场景会把自己锁死。
  现在记录持有线程，只挡**别的**线程（后一次由于上一处修复已不会发生，但仍作保险）。
- **去掉重复刷新**：安装完成时模块和页面各刷了一次版本列表，日志里出现
  `加载线程 DSH Version List 已中断但线程正常运行至结束，输出被弃用`。现在只在一处触发。

### 实机验证证据
```
11:24:27.957  安装 dsh 版本 已加入任务列表
11:24:28.920  按下附加按钮：任务管理
11:24:29.067  [PageSpeedLeft] 新建任务管理卡片：安装 dsh 版本      ← 任务管理器卡片出现
11:24:32.721  [MyIconButton] 按下图标按钮：BtnCancel
11:24:32.723  [PageSpeedLeft] 关闭任务管理卡片：安装 dsh 版本，且移出任务列表
```
另一轮完整安装（未被中断）的证据：
```
added 512 packages in 55s
安装完成 → dsh 0.1.7-rc.1 安装完成
版本仓库 0.1.7-rc.1：bin.js=True  marker=True
worker 执行次数 = 1，互斥触发 = 0，异常 = 0
```

### 变更
- `ModBase.vb`：版本号 `0.3.7` → `0.3.8`。
- `ModDshInstall.vb`：新增 `DshVersionInstallTask`（`LoaderTask`）与 `DshVersionInstallLoader`（`LoaderCombo`）、
  `DshInstallInit`、`DshInstallStart`、`DshInstallAddToTaskbar`、`DshInstallRemoveFromTaskbar`、
  `DshInstallStateChanged`、`DshInstallCleanup`；并发守卫加 OwnerThread 与诊断日志。
- `PageDownloadDsh` / `ModDshSetup`：改用 `DshInstallStart`（不再手动 Start 子任务）。
- `DEVNOTES.md`：新增 5 条（#44 任务管理器机制、#45 IsForceRestart 会重跑 worker、
  #46 并发守卫要认线程、#47 重复刷新会中断加载器、#48 别用鼠标自动化验证界面）。

### 待实机确认
右下角下载按钮与 Windows 任务栏上的**进度条观感**需要你在真机上看一次（我无法在不控制鼠标的前提下截图确认）。

---
## [v0.3.7] — 2026-09-24

### 新增：安装 dsh 时显示真实进度（用户反馈"下载时没有下载进度"）
原来的问题有两层：
1. **进度环根本没接上加载器**。`PageLoaderInit` 只负责 PanLoader / PanContent / PanAlways 的显隐与动画，
   **它不会设置 `MyLoading.State`**——而 `MyLoading.ShowProgress` 显示百分比的前提正是
   `State` 指向一个 Loader（官方页面都是一行行手写 `LoadOptiFine.State = DlOptiFineListLoader` 的）。
2. **安装过程压根没汇报进度**。`Loader.Progress` 从头到尾没被更新过，所以即使接上了也永远是 0%。

现在：
- 安装加载器接到进度环上（`LoadInstall.State = DshVersionInstallLoader`），显示百分比。
- 安装过程按阶段真实汇报：
  | 阶段 | 进度 |
  |---|---|
  | 开始准备 | 10% |
  | npm 下载安装 500+ 个包 | 12% → 88%（按耗时的饱和曲线推进，不会假满） |
  | 校验入口文件 | 90% |
  | 部署到版本仓库 | 94% |
  | 完成 | 100% |
- 解析 npm 输出给出**阶段文案**，例如
  「正在下载安装包……」「正在解压并写入文件……」「正在编译原生模块（这一步较慢，可能需要几分钟）……」
  「正在收尾……」（koffi 那类原生模块要本地编译，不给提示时用户会以为卡死了）。
- 新增**安装进度浮层**（半透明遮罩 + 进度环 + 阶段文案 + 取消按钮）。
  为什么不用页面级的加载环切换：本页有两个加载器（版本列表 / 安装），
  页面级 `PageLoaderState` 只管版本列表那个，安装时不会自动切换。
- 新增**取消安装**按钮。取消时走 `taskkill /PID x /T /F` 结束整棵进程树（见 v0.3.6）。

### 修复：去掉首次启动的「开源版本说明」弹窗（用户要求）
`FormMain` 初始化线程里，当 `VersionBranchMain = "OpenSource"` 时会弹一个"该版本中无法使用以下特性"的框，
列的是 CurseForge API / 正版登录 / 更新通知 / 主题切换 / 百宝箱——**全是 Minecraft 相关，对 DSH 启动器毫无意义**。
已在 DSH 模式下跳过（新增 `DshModeEnabledForStartup()`，因为启动早期拿不到 `PageLaunchLeft` 的 Protected 方法，
且此时界面尚未建好，必须容错）。

### 修复：第 4 处 Click 签名坑
`PageDownloadDsh` 的 `Refresh_Click` 同样写成了 `EventArgs` —— 右上角"重新获取版本列表"按钮一点就崩。
**规律：凡是 XAML 里 `Click=` 绑定的处理函数，参数一律 `(sender As Object, e As MouseButtonEventArgs)`。**
（`PageDshManager.Refresh_Click` 虽然签名也不对，但 XAML 里没引用，只是 `IRefreshable` 的配套方法，无隐患。）

### 变更
- `ModBase.vb`：版本号 `0.3.6` → `0.3.7`。
- `ModDshBase.vb`：新增 `DshModeEnabledForStartup()`。
- `ModDshInstall.vb`：`DshRunNpm` 支持进度回调与阶段识别；新增 `DshEstimateProgress`、`DshNpmPhaseText`、
  `DshInstallStatusChanged` 事件；各阶段设置 `Loader.Progress`。
- `PageDownloadDsh`：进度环接线、安装浮层、取消按钮、修正 `Refresh_Click` 签名。
- `DEVNOTES.md`：新增 4 条（#38 MyLoading 进度要自己接、#39 多加载器需手动切 UI、
  #40 开源版本说明弹窗、#41 Click 签名规律）。

### 待实机确认
安装过程的进度百分比与阶段文案需要在真机上装一次才能看到最终观感（本轮只做了静态审计与编译验证）。

---

## [v0.3.6] — 2026-09-24

**修复用户反馈的 3 个真 bug**（其中 2 个会导致页面完全打不开）。

### 修复
1. **「设置 → DSH 运行环境」打不开**
   ```
   无法从文本"InstallNode_Click"创建"Click"
   → 无法绑定到目标方法，因其签名…与委托类型的签名…不兼容
   ```
   根因：我写的处理函数签名是 `(sender As Object, e As EventArgs)`，但 PCL 的
   `MyButton` / `MyIconButton` / `MyListItem` 的 `Click` 委托是 **`MouseButtonEventArgs`**。
   VB 的 XAML 事件绑定是**运行时**解析的，所以编译期毫无报错，一构造页面就抛 `XamlParseException`。
   同类问题还藏在 `PageDshManager`（那里我误写成了 `MouseEventArgs`），一并修正——共 10 个处理函数。

2. **点「版本设置」崩溃：`ArgumentOutOfRangeException: index`**
   ```
   在 PCL.FormMain.PageChange 行号 1370
   在 PCL.PageLaunchLeft.BtnMore_Click 行号 839
   ```
   根因：`PageChange` 里有一句 `CType(PanTitleSelect.Children(Stack), MyRadioButton)`，
   **直接把顶级页枚举值当作顶部导航按钮的下标**。而顶部导航只有 5 个按钮（Tag 0~4），
   我把「整合包管理」做成了 `PageType.DshManager = 10` 的顶级页 → 必然越界。
   （原有 5~9 的"副页面"进入时走的是 `PageNameGet(Stack) <> ""` 另一条分支，所以不碰这句。）
   修法：把「整合包管理」改成挂在设置页下的子页面
   `PageChange(PageType.Setup, PageSubType.SetupManager)`，并在设置页左列表放一个
   `Visibility="Collapsed"` 的占位条目占住下标 5（`PageChange` 是按 `SubType` 下标取控件的）。

3. **npm 安装互相打架：`ENOTEMPTY: directory not empty, rmdir .../domino/test`**
   根因：你在首次启动引导里点了「安装推荐版本」，又在下载页点了「安装并绑定」——
   **两个 npm 进程同时往同一个暂存目录写**，互相删对方的文件。
   叠加第二个问题：取消安装时只 `Kill()` 掉了 `cmd.exe`，**npm 的 node.exe 子进程还在后台跑**，
   于是残留进程继续写、下一次安装撞上非空目录。
   修法：① 安装加全局互斥，第二个请求直接提示"已经有一个 dsh 版本正在安装中"；
   ② 每次安装用独一无二的暂存目录，并在开始前清理遗留暂存目录（带重试）；
   ③ 取消时用 `taskkill /PID x /T /F` **连整棵进程树一起结束**。

### 变更
- `ModBase.vb`：版本号 `0.3.5` → `0.3.6`。
- `DEVNOTES.md`：新增 3 条（#35 顶级页枚举会被当下标用、#36 子页面要在左列表占位、
  #37 Click 委托必须是 MouseButtonEventArgs），这三条都是"编译通过但一跑就崩"的坑。

### 静态复核证据
- 枚举审计：`Launch..Other = 0..4`（可作顶级页），`InstanceSelect..DshManager = 5..10`（均为副页面）。
- 全仓库已无 `PageChange(PageType.DshManager)` 残留。
- `SetupManager` 链路 5 处齐全：枚举定义 / FormMain 路由 / PageSetupLeft 的 PageGet 与 PageChange /
  左栏占位项 / 启动页入口。
- 设置页左列表下标连续：`0 ItemLaunch · 1 ItemLink · 2 ItemUI · 3 ItemSystem · 4 ItemDsh · 5 ItemManager`。

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
