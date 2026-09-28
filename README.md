# PCL DSHL

> **DeepSeekHarness（dsh）与 Minecraft 双模式启动器**
> —— 基于 [Plain Craft Launcher 2](https://github.com/Meloong-Git/PCL) 的第三方二次创作

**本软件是第三方基于 PCL 独立进行二次创作的产物，并非 PCL 官方发布。**
软件名以 `Plain Craft Launcher (PCL)` 的前缀 `PCL` 开头、后缀 `DSHL` 用于表明第三方修改，
以避免与官方 PCL 混淆。

---

## 这是什么

一份 **PCL 2.13.1.1 源码的魔改版**。在原版 PCL 的基础上，把"启动 Minecraft"扩展成
**同时能管理 DeepSeekHarness（`dsh`）**——保留 PCL 原有的界面风格与 MC 启动能力，
另加一套完整的 dsh 运行环境管理。

### 主要功能

| 功能 | 说明 |
|---|---|
| **DSH 启动 / 关闭** | 启动页按钮一键启动 `dsh web`，自动打开浏览器（带 token 的地址）；运行中变为「打开 DSH 页面」+ 红色「关闭 DSH」 |
| **整合包级隔离** | 每个整合包有独立的 `DSH_HOME`、独立的 dsh 版本、独立的插件与技能、独立的 pnpm store —— 互不污染，也不影响系统里全局安装的 dsh |
| **DSH 版本管理** | 「下载 → DSH 版本」列出 npm registry + GitHub Releases 的全部历史版本（alpha / rc 分组，按时间倒序，标注发布时间） |
| **插件 / 技能管理** | 「设置 → 整合包管理」里按包名安装 / 卸载插件（`dsh plugin --profile web add <包名>`），按名称搜索 npm，扫描并启停技能 |
| **Node.js 自动管理** | 启动器自动下载并管理 Node.js 运行时，也支持导入现有的 `DSH_HOME` 与手动指定 node.exe |
| **两种按钮布局** | 按该整合包端口上是否真的有 dsh 在运行，自动切换「未启动」/「运行中」两套底部布局 |
| **主题汉化补充** | 补回了开源版里被删掉的「主题编号 → HSL」映射表，个性化改主题色可正常生效 |

### 与 Minecraft 的关系

**原版 PCL 的 Minecraft 功能完整保留**（启动、下载、整合包管理、账号、崩溃分析等）。
包括 `McLaunchPrecheck` 在内的一切正版购买劝导与赞助劝导逻辑均未改动。

---

## 署名与致谢

**原作者：[龙腾猫跃](https://meloong.com/afd/p/0164034c016c11ebafcb52540025c377)（LTCat）**

- PCL 项目主页：https://github.com/Meloong-Git/PCL
- PCL 下载：https://meloong.com/afd/p/0164034c016c11ebafcb52540025c377
- 帮助文档库：https://github.com/LTCatt/PCL2Help
- 赞助 PCL：[爱发电](https://meloong.com/afd/a/LTCat)

PCL 的界面、动画、下载、Minecraft 启动等绝大部分代码都来自原作者与社区，
本项目只是在此基础上做 DSH 方向的扩展。**如果觉得好用，请去赞助原版 PCL。**

`DeepSeekHarness` 与 `@deepseek-ai/dsh` 的版权归 DeepSeek 所有。

---

## 许可

本存储库继续使用 PCL 原有的 [LICENCE](LICENCE)（《PCL 分发有限许可》+《PCL 存储库合理使用指南》），
未做任何修改。

按该指南的要求，本项目：

1. 明确声明自己是**第三方基于 PCL 独立创作的产物**（见本文开头）；
2. 名称以 `PCL` 开头并带第三方修改后缀（`PCL DSHL`）；
3. 在「关于」页面保留了龙腾猫跃的署名与赞助链接，并公开源代码；
4. 保留了原版的 Minecraft 正版购买劝导与赞助劝导（`McLaunchPrecheck` 未改动）；
5. 界面主色相保持 PCL 的默认**蓝色**，未实现与赞助解锁类似的换色功能。

---

## 构建

**环境要求**

| 项 | 版本 |
|---|---|
| .NET Framework | 4.8（Windows 10 1809+ 自带） |
| .NET SDK | 9.0.100（用于 msbuild；仓库已含 `global.json` 固定版本） |
| Node.js | 18+（运行时需要，用于跑 dsh） |

**步骤**

```powershell
# 1. 还原（必须先 restore 再 build；不要删 obj\，里面有还原产物）
dotnet restore "Plain Craft Launcher 2\Plain Craft Launcher 2.vbproj"

# 2. 编译
dotnet msbuild "Plain Craft Launcher 2\Plain Craft Launcher 2.vbproj" -p:Configuration=Debug -v:m -nologo
```

产物在 `Plain Craft Launcher 2\bin\`。

> ⚠️ 编译前请先关掉正在运行的启动器，否则 exe 被占用会报 `MSB3021 / MSB3027`。
>
> ⚠️ 本仓库含一个 git 子模块 `MeloongCore`（指向 GitHub）。**网络访问 GitHub 受限时
> 请用 `git clone --recurse-submodules` 并自行配置代理**，否则需要手动补齐该目录。

---

## 文档

| 文件 | 内容 |
|---|---|
| [DEVNOTES.md](DEVNOTES.md) | **开发笔记**：115 条踩坑记录 + 构建手册 + 附录 A「已定稿的 UI 形态」 |
| [CHANGELOG.md](CHANGELOG.md) | 从 v0.1.0 起的完整变更历史 |

`DEVNOTES.md` 是这个项目最有价值的部分 —— 里面记录了每一步"为什么这么写"，
包括我犯过的错误、被实测数据推翻的假设、以及从中学到的通用规则。
如果你想继续改这个项目，**强烈建议先读它**。

---

## 免责声明

本项目为个人学习与自用目的开发，按原样提供，不对任何后果负责。
使用前请自行备份重要数据（尤其是 `DSH_HOME` 里的对话记录）。

请勿用于商业倒卖，也请勿以暗示"官方出品"的方式分发。
