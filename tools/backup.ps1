<#
    备份脚本 — 每次修改源码前运行一次。

    用法（在 PCL-2.13.1.1 目录下）：
        pwsh -File tools\backup.ps1 -Note "v0.1.0 实现启动链路"

    行为：
      1. 把当前源码树（排除 bin/obj/backups）打包成
         ..\backups\PCL2-DSH_<版本>_<时间戳>_<备注>.zip
      2. 在 git 里打一个 tag（若 git 可用），方便 diff

    只读不改动源码，可安全重复运行。
#>
[CmdletBinding()]
param(
    [string]$Note = "manual",
    [string]$Version = "v0.1.0",
    [switch]$SkipGitTag
)

$ErrorActionPreference = 'Stop'
$src = Split-Path -Parent $PSScriptRoot          # PCL-2.13.1.1
$root = Split-Path -Parent $src                  # DeepseekHarnessWP
$backupRoot = Join-Path $root 'backups'
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$safeNote = ($Note -replace '[\\/:*?"<>|\s]+', '-')
$zip = Join-Path $backupRoot "PCL2-DSH_${Version}_${stamp}_${safeNote}.zip"

New-Item -ItemType Directory -Force -Path $backupRoot | Out-Null

# 收集要备份的文件：只备份源码与配置，排除构建产物和大文件
$exclude = @('\\bin\\', '\\obj\\', '\\backups\\', '\\\.git\\', '\\\.vs\\')
$files = Get-ChildItem -LiteralPath $src -Recurse -File | Where-Object {
    $p = $_.FullName
    ($exclude | Where-Object { $p -match $_ }).Count -eq 0 -and
    $_.Length -lt 20MB
}

Write-Host "备份 $($files.Count) 个文件 -> $zip"
$tmp = Join-Path $env:TEMP "pcl2dsh-bak-$stamp"
New-Item -ItemType Directory -Force -Path $tmp | Out-Null
foreach ($f in $files) {
    $rel = $f.FullName.Substring($src.Length).TrimStart('\')
    $dest = Join-Path $tmp $rel
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $dest) | Out-Null
    Copy-Item -LiteralPath $f.FullName -Destination $dest -Force
}
Compress-Archive -Path (Join-Path $tmp '*') -DestinationPath $zip -Force
Remove-Item -LiteralPath $tmp -Recurse -Force

Write-Host "完成：$zip ($([math]::Round((Get-Item -LiteralPath $zip).Length / 1MB, 2)) MB)"

if (-not $SkipGitTag) {
    Push-Location $src
    try {
        if (Test-Path -LiteralPath (Join-Path $src '.git')) {
            $tag = "${Version}-${stamp}"
            git tag -f $tag 2>&1 | Out-Null
            Write-Host "git tag: $tag"
        }
    } catch {
        Write-Host "git tag 跳过：$($_.Exception.Message)"
    } finally {
        Pop-Location
    }
}
