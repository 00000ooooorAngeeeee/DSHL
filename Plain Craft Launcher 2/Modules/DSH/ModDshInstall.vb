' ============================================================================================
'  ModDshInstall —— Node 运行环境与 dsh 本体的下载安装
'
'  两件事：
'    1. 获取 Node.js：从 npmmirror 镜像索引选一个满足 dsh 要求的版本，下载 zip 并解压到
'       DSH\runtime\node\（内含 node.exe 与 npm.cmd）
'    2. 安装 dsh 本体：在 DSH\versions\<版本>\ 下用 npm install @deepseek-ai/dsh@<版本>
'
'  注意（DEVNOTES §8.3/§8.4）：
'    · dsh 的 npm tarball 不含依赖，必须走 npm install，不能只解压 tgz
'    · GitHub release 没有附件，所以"下载"= npm 安装
' ============================================================================================
Imports Newtonsoft.Json.Linq
Imports System.Text

Public Module ModDshInstall

#Region "Node.js 运行环境"

    ''' <summary>Node 可下载版本信息。</summary>
    Public Class DshNodeVersionInfo
        Public Property Version As String = ""      '如 v22.23.1
        Public Property Files As New List(Of String)
        Public Property ReleaseDate As String = ""
        Public Overrides Function ToString() As String
            Return Version
        End Function
    End Class

    ''' <summary>
    ''' 从镜像索引挑出最新的、满足 Node 版本要求的 Windows x64 版本号（形如 v22.23.1）。
    ''' </summary>
    Public Function DshPickNodeVersion(Optional RequireLts As Boolean = False) As DshNodeVersionInfo
        Dim Text As String = NetRequestByClientRetry(DshNodeIndexMirror, RequireJson:=True)
        Dim Array As JArray = JArray.Parse(Text)
        Dim Best As DshNodeVersionInfo = Nothing
        Dim BestKey As Long = -1
        For Each Item As JToken In Array
            Dim Ver As String = If(Item("version") Is Nothing, "", Item("version").ToString())
            If Not Ver.StartsWith("v") Then Continue For
            '必须包含 Windows x64 zip
            Dim Files As New List(Of String)
            If Item("files") IsNot Nothing Then
                For Each F As JToken In CType(Item("files"), JArray)
                    Files.Add(F.ToString())
                Next
            End If
            If Not Files.Any(Function(f) f.IndexOf("win-x64", StringComparison.OrdinalIgnoreCase) >= 0 AndAlso f.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) Then Continue For
            '版本要求
            Dim Major As Integer = 0
            Dim Dot As Integer = Ver.IndexOf("."c)
            If Dot > 1 Then Integer.TryParse(Ver.Substring(1, Dot - 1), Major)
            If Major < DshNodeMinMajor Then Continue For
            '是否 LTS
            Dim IsLts As Boolean = Item("lts") IsNot Nothing AndAlso Item("lts").Type <> JTokenType.Null AndAlso Item("lts").ToString() <> "false"
            If RequireLts AndAlso Not IsLts Then Continue For
            '比较大小
            Dim Key As Long = 0
            Try
                Dim Clean As String = Ver.TrimStart("v"c)
                Dim Nums = Clean.Split("."c)
                Key = CLng(Nums(0)) * 1000000L + CLng(Nums(1)) * 1000L + CLng(Nums(2))
            Catch
            End Try
            If Key > BestKey Then
                BestKey = Key
                Best = New DshNodeVersionInfo With {
                    .Version = Ver,
                    .Files = Files,
                    .ReleaseDate = If(Item("date") Is Nothing, "", Item("date").ToString())
                }
            End If
        Next
        If Best Is Nothing Then Throw New Exception($"镜像 {DshNodeIndexMirror} 中找不到满足要求的 Node.js（需要 >= v{DshNodeMinMajor}.19 的 win-x64 版本）")
        Return Best
    End Function

    ''' <summary>
    ''' 下载并解压 Node.js 到启动器自带的运行环境目录。
    ''' </summary>
    Public Sub DshInstallNode(Loader As LoaderTask(Of Integer, Integer), Version As String)
        Dim FileName As String = $"node-{Version}-win-x64.zip"
        Dim Url As String = DshNodeDownloadPrefix & Version & "/" & FileName
        Dim LocalZip As String = DshCacheRoot & FileName
        DirectoryUtils.Create(DshCacheRoot)

        '1. 下载
        DshLog($"正在从镜像下载 Node.js {Version}")
        NetDownloadByLoader(Url, LocalZip, LoaderToSyncProgress:=Loader)

        '2. 解压
        If Loader.State = LoadState.Canceled Then Return
        DshLog("正在解压 Node.js")
        Dim TempExtract As String = DshCacheRoot & "node-temp\"
        Try
            If DirectoryUtils.Exists(TempExtract) Then DirectoryUtils.Delete(TempExtract)
        Catch
        End Try
        DirectoryUtils.Create(TempExtract)
        FileUtils.ExtractToDirectory(LocalZip, TempExtract)
        If Loader.State = LoadState.Canceled Then Return

        '3. 找到解压出来的一级目录（形如 node-v22.23.1-win-x64\）并移动
        Dim ExtractedRoot As String = Nothing
        For Each Dir As String In DirectoryUtils.EnumerateDirectories(TempExtract)
            ExtractedRoot = Dir
            Exit For
        Next
        If ExtractedRoot Is Nothing OrElse Not FileUtils.Exists(PathUtils.AddSlashSuffix(ExtractedRoot) & "node.exe") Then
            Throw New Exception("解压后的 Node.js 目录结构异常，未找到 node.exe")
        End If

        DshLog("正在部署 Node.js 运行环境")
        If DirectoryUtils.Exists(DshRuntimeRoot) Then
            Try
                DirectoryUtils.Delete(DshRuntimeRoot, toRecycleBin:=True)
            Catch ex As Exception
                Throw New Exception($"无法替换旧的 Node 运行环境，请手动删除 {DshRuntimeRoot} 后重试：" & ex.Message)
            End Try
        End If
        DirectoryUtils.Create(DshRuntimeRoot)
        DirectoryUtils.Move(ExtractedRoot, PathUtils.RemoveSlashSuffix(DshRuntimeRoot))
        Try
            DirectoryUtils.Delete(TempExtract)
            FileUtils.Delete(LocalZip)
        Catch
        End Try

        '4. 验证
        Dim Ver As String = DshNodeVersion()
        If Ver Is Nothing Then Throw New Exception("Node.js 安装完成但无法执行，请检查杀毒软件是否拦截")
        DshLog($"Node.js 安装完成：{Ver}")
    End Sub

    ''' <summary>导出：一键安装最新合适的 Node.js。</summary>
    Public DshNodeInstallLoader As New LoaderTask(Of Integer, Integer)("DSH Node Install", AddressOf DshNodeInstallMain)

    Private Sub DshNodeInstallMain(Loader As LoaderTask(Of Integer, Integer))
        Dim Info As DshNodeVersionInfo = DshPickNodeVersion()
        DshLog($"已选择 Node.js {Info.Version}（发布于 {Info.ReleaseDate}）")
        DshInstallNode(Loader, Info.Version)
    End Sub

#End Region

#Region "dsh 本体安装"

    ''' <summary>
    ''' 在一个空目录里用 npm 安装指定版本的 @deepseek-ai/dsh，然后把 node_modules 搬到目标版本目录。
    ''' 必须在非 UI 线程调用；Loader 用于汇报进度与支持取消。
    ''' </summary>
    Public Sub DshInstallVersion(Loader As LoaderBase, Version As String)
        If String.IsNullOrWhiteSpace(Version) Then Throw New Exception("未指定要安装的 dsh 版本")
        Dim NodeExe As String = DshNodeExe
        If NodeExe Is Nothing Then Throw New Exception("尚未配置 Node.js 运行环境，请先在设置中安装或指定 node.exe")
        Dim NpmCmd As String = DshNpmCmd
        If NpmCmd Is Nothing Then Throw New Exception("未找到 npm.cmd，请确认 Node.js 运行环境完整（应包含 node.exe 与 npm.cmd）")

        Dim Target As String = DshVersionPath(Version)
        If DshVersionInstalled(Version) Then
            DshLog($"dsh {Version} 已安装，跳过")
            Return
        End If

        '1. 准备临时安装目录
        Dim Stage As String = DshCacheRoot & "install-" & Version & "\"
        Try
            If DirectoryUtils.Exists(Stage) Then DirectoryUtils.Delete(Stage)
        Catch
        End Try
        DirectoryUtils.Create(Stage)
        FileUtils.Write(Stage & "package.json",
            "{""name"":""dsh-stage"",""private"":true,""version"":""1.0.0""}",
            New UTF8Encoding(False))

        '2. npm install
        Dim Registry As String = DshNpmRegistryActive
        DshLog($"正在安装 dsh {Version}（源：{Registry}）")
        Dim Args As String = $"install --prefix ""{PathUtils.RemoveSlashSuffix(Stage)}"" --no-audit --no-fund --loglevel=error --registry={Registry} ""{DshPackageName}@{Version}"""

        DshRunNpm(Loader, NpmCmd, Args, Stage)

        '3. 校验
        Dim InstalledBin As String = Stage & "node_modules\" & DshPackageName.Replace("/", "\") & "\lib\bin.js"
        If Not FileUtils.Exists(InstalledBin) Then
            Throw New Exception($"npm 安装结束但未找到 dsh 入口文件：{InstalledBin}。请检查网络或换用国内源后重试")
        End If
        If Loader.State = LoadState.Canceled Then Return

        '4. 搬到版本仓库
        DshLog($"正在部署到版本仓库：{Target}")
        If DirectoryUtils.Exists(Target) Then
            Try
                DirectoryUtils.Delete(Target, toRecycleBin:=True)
            Catch ex As Exception
                Throw New Exception($"无法覆盖已存在的版本目录 {Target}：" & ex.Message)
            End Try
        End If
        DirectoryUtils.Create(Target)
        DirectoryUtils.Move(Stage & "node_modules", PathUtils.RemoveSlashSuffix(Target & "node_modules"))

        '5. 写标记与元数据
        Dim PkgJson As String = Target & "node_modules\" & DshPackageName.Replace("/", "\") & "\package.json"
        Dim RealVersion As String = Version
        Try
            If FileUtils.Exists(PkgJson) Then
                Dim Pkg As JObject = JObject.Parse(FileUtils.ReadAsString(PkgJson))
                If Pkg("version") IsNot Nothing Then RealVersion = Pkg("version").ToString()
            End If
        Catch
        End Try
        '注意日期格式字符串里的单引号必须成对：yyyy'-'MM'-'dd HH':'mm':'ss'
        '（之前多写了一个引号，运行时抛 FormatException"无法为字符 ' 找到匹配的引号字符"，实机踩到过）
        Dim InstallStamp As String = Now.ToString("yyyy'-'MM'-'dd HH':'mm':'ss")
        FileUtils.Write(Target & ".dsh-installed",
            $"version={RealVersion}{vbCrLf}installed={InstallStamp}{vbCrLf}node={NodeExe}{vbCrLf}registry={Registry}{vbCrLf}",
            New UTF8Encoding(False))

        '6. 清理
        Try
            DirectoryUtils.Delete(Stage)
        Catch
        End Try
        DshLog($"dsh {RealVersion} 安装完成")
    End Sub

    ''' <summary>
    ''' 执行一次 npm 命令，实时把输出喂给 Loader 的日志与进度。
    ''' </summary>
    Private Sub DshRunNpm(Loader As LoaderBase, NpmCmd As String, Arguments As String, WorkingDirectory As String)
        Dim Info As New ProcessStartInfo With {
            .FileName = "cmd.exe",
            .Arguments = $"/c """"{NpmCmd}"" {Arguments}""",
            .UseShellExecute = False,
            .CreateNoWindow = True,
            .RedirectStandardOutput = True,
            .RedirectStandardError = True,
            .StandardOutputEncoding = Encoding.UTF8,
            .StandardErrorEncoding = Encoding.UTF8
        }
        Info.WorkingDirectory = WorkingDirectory
        Dim Proc As Process = StartProcess(Info)
        Dim Buffer As New StringBuilder()
        Dim Handler As DataReceivedEventHandler =
            Sub(Sender As Object, E As DataReceivedEventArgs)
                If E.Data Is Nothing Then Return
                SyncLock Buffer
                    Buffer.AppendLine(E.Data)
                End SyncLock
                'npm 用 "reify" 之类的字样输出进度，这里只把有意义的行写进日志
                Dim Text As String = E.Data.Trim()
                If Text.Length > 0 AndAlso Text.Length < 200 AndAlso Not Text.Contains("⸨") Then DshLog(Text)
            End Sub
        AddHandler Proc.OutputDataReceived, Handler
        AddHandler Proc.ErrorDataReceived, Handler
        Proc.BeginOutputReadLine()
        Proc.BeginErrorReadLine()

        '等待，同时响应取消
        While Not Proc.HasExited
            If Loader.State = LoadState.Canceled Then
                Try
                    Proc.Kill()
                Catch
                End Try
                Throw New Exception("安装已被用户取消")
            End If
            Thread.Sleep(120)
        End While
        Proc.WaitForExit()

        If Proc.ExitCode <> 0 Then
            Dim Tail As String = ""
            SyncLock Buffer
                Dim Lines = Buffer.ToString().Split({vbCrLf, vbLf}, StringSplitOptions.RemoveEmptyEntries)
                Tail = Lines.Reverse().Take(6).Reverse().Join(vbCrLf)
            End SyncLock
            Throw New Exception($"npm 安装失败（退出码 {Proc.ExitCode}）：{vbCrLf}{Tail}")
        End If
    End Sub

    ''' <summary>
    ''' 待安装的 dsh 版本号。
    ''' 为什么用模块变量而不是 Loader.Input：LoaderBase.WaitForExit() 内部会执行
    ''' Start(Nothing, ...)，而 Start 会无条件覆盖 Me.Input，导致外部
    ''' `Start(版本号)` 设置的输入在 `WaitForExit()` 时被冲成 Nothing（实机已验证）。
    ''' 所以这里用显式的"待办"变量，先设值再启动加载器，绕开这个机制。
    ''' 调用方顺序：DshRequestVersionInstall(版本) → DshVersionInstallLoader.Start(0)
    ''' </summary>
    Private DshPendingInstallVersion As String = Nothing

    ''' <summary>请求安装某个 dsh 版本（线程安全地设置待安装版本）。</summary>
    Public Sub DshRequestVersionInstall(Version As String)
        If String.IsNullOrWhiteSpace(Version) Then Throw New Exception("未指定要安装的 dsh 版本")
        SyncLock DshInstallLock
            DshPendingInstallVersion = Version
        End SyncLock
    End Sub
    Private ReadOnly DshInstallLock As New Object

    ''' <summary>安装版本列表加载器：安装完成后刷新已安装状态。</summary>
    Public DshVersionInstallLoader As New LoaderTask(Of Integer, Integer)("DSH Version Install", AddressOf DshVersionInstallMain)

    ''' <summary>
    ''' 安装成功后的回调（由下载页设置，用于"安装并绑定到整合包"）。
    ''' 在 UI 线程执行，执行后会被清空。
    ''' </summary>
    Public InstallAfterAction As Action = Nothing

    Private Sub DshVersionInstallMain(Loader As LoaderTask(Of Integer, Integer))
        '取出待安装版本（优先模块变量，其次兼容 Loader.Input 被显式赋值的情况）
        Dim Version As String = Nothing
        SyncLock DshInstallLock
            Version = DshPendingInstallVersion
            DshPendingInstallVersion = Nothing
        End SyncLock
        If String.IsNullOrWhiteSpace(Version) Then
            '兼容直接 Start(版本字符串) 的调用方式（此时 Loader.Input 是 Object，可能装着字符串）
            Try
                Dim Raw As Object = Loader.Input
                If Raw IsNot Nothing Then Version = CStr(Raw)
            Catch
            End Try
        End If
        If String.IsNullOrWhiteSpace(Version) Then Throw New Exception("未指定要安装的 dsh 版本（请用 DshRequestVersionInstall 先设置版本）")

        DshInstallVersion(Loader, Version)
        '刷新版本列表的"已安装"标记
        RunInUi(Sub()
                    DshRefreshVersionList()
                    Hint($"dsh {Version} 安装完成", HintType.Green)
                    Dim Action_ As Action = InstallAfterAction
                    InstallAfterAction = Nothing
                    If Action_ IsNot Nothing Then Action_.Invoke()
                    DshRefreshInstanceList()
                End Sub)
    End Sub

#End Region

#Region "卸载"

    ''' <summary>卸载某个 dsh 版本（移动目录到回收站）。</summary>
    Public Sub DshUninstallVersion(Version As String)
        Dim Target As String = DshVersionPath(Version)
        If Not DirectoryUtils.Exists(Target) Then Throw New Exception($"该版本未安装：{Version}")
        '被整合包占用时不允许卸载
        Dim Used As List(Of String) = DshInstanceList.Where(Function(i) i.DshVersion = Version).Select(Function(i) i.Name).ToList()
        If Used.Any() Then Throw New Exception($"以下整合包正在使用该版本，请先改绑或删除它们：{Used.Join("、")}")
        DirectoryUtils.Delete(Target, toRecycleBin:=True)
        Logger.Info($"已卸载 dsh {Version}")
    End Sub

#End Region

End Module
