# ============================================================================
#  PCL2-DSH 启动器 —— 分发包打包脚本
# ----------------------------------------------------------------------------
#  用法（在 PCL-2.13.1.1 目录下）：
#      powershell -NoProfile -ExecutionPolicy Bypass -File tools\dist.ps1
#
#  产物：E:\DeepseekHarnessWP\dist\PCL2-DSH_v<版本>_<时间>.zip
#
#  为什么这样打包（每条都有实测依据，见 DEVNOTES）：
#    · 只带 7 个文件：exe 本身其实是"外壳"，它通过程序集引用依赖
#      MeloongCore.dll / MeloongCore.Wpf.dll / PCLCS.dll / Microsoft.Win32.Registry.dll
#      （用 ReflectionOnlyLoadFrom 读 exe 的引用表确认过）。
#      实测：把这 7 个文件单独放一个空目录启动，能正常起来并自己创建 DSH\ 与 PCL\。
#    · **不带 .pdb**：那是调试符号，运行时不需要（能省 2.8 MB）。
#    · **不带 bin\DSH**：里面是已下载的 dsh 版本（594 MB）与整合包实例（383 MB），
#      属于用户自己的环境与数据，不该跟着分发。收包人首次启动时启动器会自己下载。
#    · **不带 bin\PCL**：那是用户设置（主题、隐藏开关、整合包选择）与个人日志。
# ============================================================================

param(
    [string]$Version = "",
    [string]$Note = ""
)

$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $PSScriptRoot          # PCL-2.13.1.1
$BinDir = Join-Path $Root 'Plain Craft Launcher 2\bin'
$OutDir = 'E:\DeepseekHarnessWP\dist'

# 版本号从源码里读，避免手抄出错
if ([string]::IsNullOrWhiteSpace($Version)) {
    $ModBase = Join-Path $Root 'Plain Craft Launcher 2\Modules\Base\ModBase.vb'
    $m = Select-String -LiteralPath $ModBase -Pattern 'VersionDshBaseName As String = "([^"]+)"'
    if (-not $m) { throw "读不到 VersionDshBaseName，无法确定版本号" }
    $Version = $m.Matches[0].Groups[1].Value
}

$Stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$ZipName = if ([string]::IsNullOrWhiteSpace($Note)) {
    "PCL2-DSH_v${Version}_${Stamp}.zip"
} else {
    "PCL2-DSH_v${Version}_${Stamp}_${Note}.zip"
}
$ZipPath = Join-Path $OutDir $ZipName

# 必须随包的文件（实测最小可运行集合）
$Files = @(
    'Plain Craft Launcher 2.exe',
    'Plain Craft Launcher 2.exe.config',
    'Plain Craft Launcher 2.xml',
    'MeloongCore.dll',
    'MeloongCore.Wpf.dll',
    'PCLCS.dll',
    'Microsoft.Win32.Registry.dll'
)

# 不发的东西（顺手校验一下，防止哪天误加进来）
$Forbidden = @('DSH', 'PCL')

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$Stage = Join-Path $env:TEMP "pcldist-$Stamp"
if (Test-Path $Stage) { Remove-Item $Stage -Recurse -Force }
New-Item -ItemType Directory -Force -Path $Stage | Out-Null

Write-Host "[1/3] 收集文件…"
$Missing = @()
foreach ($F in $Files) {
    $Src = Join-Path $BinDir $F
    if (Test-Path $Src) {
        Copy-Item $Src $Stage -Force
        Write-Host ("      + {0}" -f $F)
    } else {
        $Missing += $F
    }
}
if ($Missing.Count -gt 0) {
    throw ("缺少必需文件，打包中止：`n  " + ($Missing -join "`n  ") + "`n请先构建（见 DEVNOTES §8b 构建手册）")
}

# 说明文件（收包人第一眼要看的东西）
$Readme = @"
PCL2-DSH 启动器  v$Version
================================================================
一个由 PCL2（Plain Craft Launcher 2）改造而来的 DeepSeek Harness 启动器。
可以像管理 Minecraft 整合包一样，给每个整合包隔离 dsh 的版本、插件、技能与配置。

----------------------------------------------------------------
怎么用
----------------------------------------------------------------
1. 解压到任意目录（路径里**不要有中文以外的特殊字符**，也尽量别放桌面）。
2. 双击「Plain Craft Launcher 2.exe」。
3. 首次启动会有一个两步引导：
     第 1 步：确认 Node.js 运行环境
     第 2 步：安装 dsh 本体（会从 npm 下载，约 1 分钟）
   跟着点就行，装完就能在「整合包管理」里启动 DeepSeek Harness。

----------------------------------------------------------------
运行前置条件
----------------------------------------------------------------
* Windows 10 / 11（需要 .NET Framework 4.8，Win10 1809 以后都是自带的）
* **Node.js 18 或更高版本**（必须自己装：https://nodejs.org/ ）
  启动器会在「设置 → DSH 运行环境」里检测它；没装的话那一步会提示。

----------------------------------------------------------------
第一次用建议点一遍
----------------------------------------------------------------
* 「设置 → DSH 运行环境」：可以改 Node 位置、dsh 本体位置、npm 下载源
  （国内建议保持默认的 npmmirror 镜像）
* 「设置 → 整合包管理 → 新建整合包」：给整合包起个名字
  （dsh 会装到这个整合包自己独立的目录里，互不影响）
* 「下载 → DSH 版本」：可以选装别的 dsh 版本（alpha / rc 分开列，带发布时间）
* 插件：在「整合包管理 → 插件 → 安装插件…」里
    - 可以**搜索** npm 上的 dsh 插件
    - 或者一键装「dshmarket」—— 那是个可视化插件市场，装完在 dsh 界面里就能逛

----------------------------------------------------------------
关于隔离（这个启动器的重点）
----------------------------------------------------------------
每个整合包有自己独立的：
  * dsh 版本（各自的 node_modules）
  * DSH_HOME（技能、插件、配置、会话）
  * pnpm 与 pnpm 的包缓存（连依赖缓存都不共用）
所以 A 包的插件/技能绝不会影响到 B 包。

----------------------------------------------------------------
常见问题
----------------------------------------------------------------
Q: 提示找不到 Node.js？
A: 装一下 https://nodejs.org/ 的 LTS 版即可；装完重启启动器。
   已经装过但启动器没识别到，可以在「设置 → DSH 运行环境」里手动指定 node.exe。

Q: 装 dsh 时下载很慢 / 失败？
A: 「设置 → DSH 运行环境 → npm 下载源」保持国内镜像（npmmirror）；
   或者用「下载 → DSH 版本」里的刷新按钮重试。

Q: 装插件失败？
A: 插件安装依赖 pnpm，启动器会自动准备（用 Node 自带的 corepack 装到整合包目录里），
   首次会下载一次 pnpm。装完的报错信息里会带上 npm 的原始输出，按提示排查即可。

Q: 想彻底卸载？
A: 删掉整个解压出来的目录就是彻底卸载 —— 所有东西（dsh 版本、整合包数据、插件）
   都在这个目录里面，不写注册表、不动系统环境变量。

----------------------------------------------------------------
本包内容
----------------------------------------------------------------
    Plain Craft Launcher 2.exe         启动器本体
    Plain Craft Launcher 2.exe.config  运行配置（.NET Framework 版本）
    Plain Craft Launcher 2.xml         界面文本资源
    MeloongCore.dll                    依赖库
    MeloongCore.Wpf.dll                依赖库
    PCLCS.dll                          依赖库
    Microsoft.Win32.Registry.dll       依赖库

首次启动后，启动器会在这个目录里自己创建：
    DSH\      dsh 版本仓库、整合包实例（可以很大，几百 MB 起）
    PCL\      启动器自己的设置与日志

----------------------------------------------------------------
来源与许可
----------------------------------------------------------------
本启动器基于 Meloong-Git/PCL 的开源版本 2.13.1.1 改造（Apache-2.0）。
DeepSeek Harness 由 DeepSeek 提供，遵循其各自的许可。
请遵守相应开源许可再分发。
"@
$ReadmePath = Join-Path $Stage '使用说明.txt'
# 注意：这里必须写成带 BOM 的 UTF-8，否则记事本会按 GBK 打开而显示乱码
[System.IO.File]::WriteAllText($ReadmePath, $Readme, (New-Object System.Text.UTF8Encoding($true)))

Write-Host "[2/3] 校验（确保没把个人环境打进去）…"
foreach ($Bad in $Forbidden) {
    if (Test-Path (Join-Path $Stage $Bad)) { throw "打包目录里出现了不该有的 $Bad，已中止" }
}

Write-Host "[3/3] 压缩…"
if (Test-Path $ZipPath) { Remove-Item $ZipPath -Force }
Compress-Archive -Path (Join-Path $Stage '*') -DestinationPath $ZipPath -CompressionLevel Optimal
Remove-Item $Stage -Recurse -Force

$ZipMB = [math]::Round((Get-Item $ZipPath).Length / 1MB, 2)
Write-Host ""
Write-Host "打包完成：$ZipPath"
Write-Host "  版本：v$Version"
Write-Host "  大小：$ZipMB MB"
Write-Host "  内容：7 个程序文件 + 使用说明.txt"
Write-Host ""
Write-Host "收包人需要自备：.NET Framework 4.8（Win10 1809+ 自带）、Node.js 18+"
