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
        '防并发（实机踩坑）：npm 装 dsh 会拉 500+ 个包。如果同时跑两个 npm 进程
        '（比如引导里点了一次、下载页又点一次），它们会在同一个暂存目录里互相删文件，
        '报出 `ENOTEMPTY: directory not empty, rmdir .../domino/test` 这种莫名其妙的错。
        '这里用一个模块级标志串行化，第二个请求直接告知用户等待即可。
        '注意：必须是**线程**级互斥，不能挡住同一个线程的重入（否则"先 Start 子任务再 Start 组合"
        '这类同线程的多次 Start 会把自己锁死）。用 OwnerThread 记录持有者。
        SyncLock DshInstallSlot
            If DshInstallRunning AndAlso DshInstallOwnerThread <> Thread.CurrentThread.ManagedThreadId Then
                Logger.Warn($"安装互斥触发：持有线程 {DshInstallOwnerThread}，当前线程 {Thread.CurrentThread.ManagedThreadId}")
                Throw New Exception("已经有一个 dsh 版本正在安装中，请等它完成后再试。" & vbCrLf &
                                    "（npm 需要下载 500 多个包，通常要 1~2 分钟）")
            End If
            DshInstallRunning = True
            DshInstallOwnerThread = Thread.CurrentThread.ManagedThreadId
        End SyncLock
        Logger.Info($"开始安装 dsh {Version}（线程 {Thread.CurrentThread.ManagedThreadId}）")
        Try
            DshInstallVersionCore(Loader, Version)
        Finally
            SyncLock DshInstallSlot
                DshInstallRunning = False
                DshInstallOwnerThread = -1
            End SyncLock
        End Try
    End Sub

    Private ReadOnly DshInstallSlot As New Object
    Private DshInstallRunning As Boolean = False
    Private DshInstallOwnerThread As Integer = -1

    ''' <summary>最近一次安装的状态文案（供界面显示）。</summary>
    Public ReadOnly Property DshInstallStatusText As String
        Get
            Return _DshInstallStatusText
        End Get
    End Property

    ''' <summary>安装状态文案变化事件（在调用线程触发，订阅方自行切回 UI 线程）。</summary>
    Public Event DshInstallStatusChanged(Text As String)

    ''' <summary>汇报一句安装进度文案。</summary>
    Private Sub DshReportStatus(Text As String, Optional NewProgress As Double = -1)
        If String.IsNullOrWhiteSpace(Text) Then Return
        _DshInstallStatusText = Text
        RaiseEvent DshInstallStatusChanged(Text)
        If NewProgress >= 0 Then DshLog(Text)
    End Sub
    Private _DshInstallStatusText As String = ""

    Private Sub DshInstallVersionCore(Loader As LoaderBase, Version As String)
        Dim NodeExe As String = DshNodeExe
        If NodeExe Is Nothing Then Throw New Exception("尚未配置 Node.js 运行环境，请先在设置中安装或指定 node.exe")
        Dim NpmCmd As String = DshNpmCmd
        If NpmCmd Is Nothing Then Throw New Exception("未找到 npm.cmd，请确认 Node.js 运行环境完整（应包含 node.exe 与 npm.cmd）")

        Dim Target As String = DshVersionPath(Version)
        If DshVersionInstalled(Version) Then
            DshLog($"dsh {Version} 已安装，跳过")
            Return
        End If

        '0. 清掉上次失败/取消留下的暂存目录（npm 可能留下被占用的文件，重试几次）
        DshCleanStaleStages(Version)

        '1. 准备临时安装目录（每次都换一个全新目录，避免与残留状态打架）
        Dim Stage As String = DshCacheRoot & "install-" & Version & "-" & GetTimeMs() & "\"
        DirectoryUtils.Create(Stage)
        FileUtils.Write(Stage & "package.json",
            "{""name"":""dsh-stage"",""private"":true,""version"":""1.0.0""}",
            New UTF8Encoding(False))

        '2. npm install
        Dim Registry As String = DshNpmRegistryActive
        DshLog($"正在安装 dsh {Version}（源：{Registry}）")
        Dim Args As String = $"install --prefix ""{PathUtils.RemoveSlashSuffix(Stage)}"" --no-audit --no-fund --loglevel=error --registry={Registry} ""{DshPackageName}@{Version}"""

        Loader.Progress = 0.1
        DshReportStatus("正在下载并安装 dsh（约 500 个包，通常 1~2 分钟）……", 0.1)
        DshLog($"暂存目录：{Stage}（存在={DirectoryUtils.Exists(Stage)}）")
        '文件计数看门狗：给出真实的"已写入文件数 / 包总数"并据此推进进度
        Dim WatchTicks As Integer = 0
        Dim LastLoggedCount As Integer = -1
        Dim TotalPkgs As Integer = 0
        Dim Watcher As New DshInstallFileWatcher(Stage,
            Sub(Count, Total)
                WatchTicks += 1
                If Total > 0 AndAlso TotalPkgs = 0 Then
                    TotalPkgs = Total
                    DshLog($"npm 依赖图已就绪：共 {Total} 个包")
                End If
                '每约 2 秒记一次日志（400ms 采样一次），便于事后确认看门狗真的在工作
                If WatchTicks Mod 5 = 0 AndAlso Count <> LastLoggedCount Then
                    LastLoggedCount = Count
                    DshLog($"正在写入文件：{Count} 个" & If(TotalPkgs > 0, $"（共 {TotalPkgs} 个包）", ""))
                End If
                '进度：实测 npm 是"先把包下到缓存、最后 1~2 阶段才解压提交"，
                '所以 node_modules 的文件数在下载阶段恒为 1，解压阶段才爆涨。
                '据此按"已达目标的文件数比例"推进 0.15~0.86；解压前用很慢的时间曲线兜底，
                '避免进度条在下载阶段完全不动（那段时间本来就无法观测）。
                Dim Pushed As Double
                If Count > 8 Then
                    Dim PerPkg As Double = Math.Max(4.0, Count / Math.Max(1.0, Total))
                    Dim EstTotalFiles As Double = Math.Max(1.0, PerPkg * Math.Max(Total, 512))
                    Dim Ratio As Double = Count / EstTotalFiles
                    Pushed = 0.15 + 0.71 * Math.Min(1.0, Math.Sqrt(Math.Max(0.0, Ratio)))
                Else
                    Pushed = 0.15 + 0.05 * Math.Min(1.0, WatchTicks / 75.0)
                End If
                If Pushed > Loader.Progress Then Loader.Progress = Pushed
                DshSetProgressText($"正在写入文件：{Count} 个" & If(TotalPkgs > 0, $"（共 {TotalPkgs} 个包）", ""))
                '注意：不要在这里改 Loader.Name —— 任务管理卡片只在**创建时**读一次 Name
                '（PageSpeedLeft 里 Title 用的就是 Loader.Name），刷新循环只更新副标题与控制项。
                '所以版本号必须在 DshInstallStart 里、启动之前就设好。
            End Sub,
            Sub(Count, Total)
                DshSetProgressText(If(Total > 0, $"已写入 {Count} 个文件（{Total} 个包）", $"已写入 {Count} 个文件"))
            End Sub)
        Watcher.StartWatch()
        Try
            DshRunNpm(Loader, NpmCmd, Args, Stage,
                      Sub(P, T)
                          If T <> "" Then DshReportStatus(T, -1)
                          If P >= 0 AndAlso P > Loader.Progress Then Loader.Progress = P
                      End Sub,
                      ProgressFrom:=0.12, ProgressTo:=0.5)
        Finally
            Watcher.RequestStop()
        End Try
        Loader.Progress = 0.9

        '3. 校验
        Dim InstalledBin As String = Stage & "node_modules\" & DshPackageName.Replace("/", "\") & "\lib\bin.js"
        If Not FileUtils.Exists(InstalledBin) Then
            Throw New Exception($"npm 安装结束但未找到 dsh 入口文件：{InstalledBin}。请检查网络或换用国内源后重试")
        End If
        If Loader.State = LoadState.Canceled Then Return
        DshReportStatus("正在部署到版本仓库……", 0.93)
        Loader.Progress = 0.93

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
        DshReportStatus("正在部署到版本仓库……", 0.94)
        Loader.Progress = 0.94
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
        DshReportStatus("安装完成", 1)
        Loader.Progress = 1
        DshLog($"dsh {RealVersion} 安装完成")
    End Sub

    ''' <summary>把加载器的名字（也就是任务管理器卡片上显示的标题）改成带版本号的形式。</summary>
    Private Sub DshSetInstallTaskName(Version As String)
        Dim NewName As String = $"安装 dsh {Version}"
        DshVersionInstallLoader.Name = NewName
        DshVersionInstallTask.Name = NewName
    End Sub

    ''' <summary>把加载器的名字改成带版本号的形式（任务管理器卡片标题会显示它）。</summary>
    Private Sub DshSetProgressText(Text As String)
        DshReportStatus(Text, -1)
    End Sub

    ''' <summary>
    ''' npm 安装期间的"文件计数看门狗"。
    '''
    ''' 为什么需要（实测结论）：npm 是子进程，既不经过 PCL 的网络栈（所以
    ''' NetManager.Speed / FileRemain 永远是 0），也不打印机器可读的进度。
    ''' 实测唯一的真实观测量是 `node_modules` 里已被解压提交的文件数：
    ''' 前 12 秒（npm 把包下到缓存）为 0，之后 30 秒内从 876 涨到 27538，最终 512 个包。
    ''' 于是用这个数字做出真实的"已写入多少文件 + 包总数 + 据此推进的百分比"。
    ''' 注意：**下载速率无法测**（看门狗只看得到解压提交，而 npm 的下载与解压是重叠的），
    ''' 所以任务管理器左栏的"下载速度 / 剩余文件"仍是 0 —— 这是 npm 架构的限制。
    ''' </summary>
    Private Class DshInstallFileWatcher
        Public ReadOnly Stage As String
        Public ReadOnly Report As Action(Of Integer, Integer) '已写入文件数, 包总数（0=未知）
        Public ReadOnly [Done] As Action(Of Integer, Integer)
        Public LastCount As Integer = 0
        Public LastTotal As Integer = 0
        Private _Run As Boolean = True
        Private _Expected As Integer = 0
        Public Sub New(Stage As String, Report As Action(Of Integer, Integer), [Done] As Action(Of Integer, Integer))
            Me.Stage = Stage
            Me.Report = Report
            Me.Done = [Done]
        End Sub
        Public Sub StartWatch()
            Dim Th As New Threading.Thread(AddressOf WatchLoop)
            Th.IsBackground = True
            Th.Start()
        End Sub
        Public Sub RequestStop()
            _Run = False
        End Sub
        Private Sub WatchLoop()
            Try
                While _Run
                    Dim N As Integer = DshCountFiles(Stage)
                    LastCount = N
                    '包总数只在 .package-lock.json 出现后读一次（那是 npm 解压阶段开始的标志）
                    If _Expected = 0 Then _Expected = DshReadExpectedPackageCount(Stage)
                    LastTotal = _Expected
                    Report(N, _Expected)
                    Threading.Thread.Sleep(400)
                End While
            Catch
            End Try
            Done(LastCount, LastTotal)
        End Sub
    End Class

    ''' <summary>
    ''' 递归统计目录下的文件数（失败时返回已统计到的数量）。
    ''' 参数顺序注意：DirectoryUtils.EnumerateFiles 是 (folder, includeSubDirectories, searchPattern)，
    ''' 不是 .NET 的 (path, searchPattern, searchOption) —— 按 .NET 顺序传参会让第三个参数
    ''' 被隐式转成 Boolean 而抛异常（实机踩过，计数永远返回 0）。
    ''' 另外这里逐项 Try/Catch：npm 正在往目录里写文件，枚举途中目录可能消失。
    ''' </summary>
    Private Function DshCountFiles(Root As String) As Integer
        Dim Count As Integer = 0
        Try
            If Not DirectoryUtils.Exists(Root) Then Return 0
            For Each F As String In DirectoryUtils.EnumerateFiles(Root, True)
                Count += 1
            Next
        Catch
        End Try
        Return Count
    End Function

    ''' <summary>从 npm 生成的 .package-lock.json 里读出精确的包总数（失败返回 0）。</summary>
    Private Function DshReadExpectedPackageCount(Stage As String) As Integer
        Try
            Dim P As String = Stage & "node_modules\.package-lock.json"
            If Not FileUtils.Exists(P) Then Return 0
            Dim J As JObject = JObject.Parse(FileUtils.ReadAsString(P))
            Dim Pkgs As JToken = J("packages")
            If Pkgs Is Nothing Then Return 0
            'packages 里包含根项目自身（""），所以减去 1
            Return Math.Max(0, Pkgs.Count() - 1)
        Catch
            Return 0
        End Try
    End Function

    ''' <summary>
    ''' 执行一次 npm 命令，实时把输出喂给 Loader 的日志与进度。
    ''' OnStatus 用于把"当前阶段文案"回报给界面（可空）。
    ''' </summary>
    Private Sub DshRunNpm(Loader As LoaderBase, NpmCmd As String, Arguments As String, WorkingDirectory As String,
                          Optional OnStatus As Action(Of Double, String) = Nothing,
                          Optional ProgressFrom As Double = 0.15, Optional ProgressTo As Double = 0.9)
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
        Dim StartTick As Long = GetTimeMs()
        Dim Report As Action =
            Sub()
                If OnStatus Is Nothing Then Return
                If Loader.State = LoadState.Canceled Then Return
                OnStatus(DshEstimateProgress(StartTick, ProgressFrom, ProgressTo), "")
            End Sub
        Dim Handler As DataReceivedEventHandler =
            Sub(Sender As Object, E As DataReceivedEventArgs)
                If E.Data Is Nothing Then Return
                SyncLock Buffer
                    Buffer.AppendLine(E.Data)
                End SyncLock
                'npm 用 "reify" 之类的字样输出进度，这里只把有意义的行写进日志
                Dim Text As String = E.Data.Trim()
                If Text.Length > 0 AndAlso Text.Length < 200 AndAlso Not Text.Contains("⸨") Then DshLog(Text)
                '根据输出内容给出更贴近实际的阶段文案
                If OnStatus IsNot Nothing Then
                    Dim Phase As String = DshNpmPhaseText(Text)
                    If Phase <> "" Then OnStatus(-1, Phase)
                End If
            End Sub
        AddHandler Proc.OutputDataReceived, Handler
        AddHandler Proc.ErrorDataReceived, Handler
        Proc.BeginOutputReadLine()
        Proc.BeginErrorReadLine()

        '等待，同时响应取消、并按时间估计进度
        While Not Proc.HasExited
            If Loader.State = LoadState.Canceled Then
                '必须连整棵进程树一起杀：npm.cmd 只是 cmd.exe 的外壳，
                '真正干活的是 node.exe 子进程。只杀 cmd.exe 会留下一个还在写文件的 npm，
                '下次安装就会撞上 `ENOTEMPTY: directory not empty` 之类的错（实机踩过）。
                DshKillProcessTree(Proc)
                Throw New Exception("安装已被用户取消")
            End If
            Report()
            Thread.Sleep(200)
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
    ''' 按耗时估算安装进度（0~1）。npm 不提供机器可读的百分比，所以用饱和曲线：
    ''' 开局涨得快、后面越来越慢，永远不会因为估得不准而卡在 100% 不动。
    ''' </summary>
    Private Function DshEstimateProgress(StartTick As Long, From As Double, [To] As Double) As Double
        Dim Seconds As Double = Math.Max(0, (GetTimeMs() - StartTick) / 1000.0)
        Dim Ratio As Double = Math.Log(1 + Seconds / 12.0) / Math.Log(1 + 150.0 / 12.0)
        Return From + ([To] - From) * Math.Min(1, Ratio)
    End Function

    ''' <summary>把 npm 的一行输出翻译成用户看得懂的阶段文案（没有匹配则返回空）。</summary>
    Private Function DshNpmPhaseText(Line As String) As String
        If Line = "" Then Return ""
        Dim L As String = Line.ToLowerInvariant()
        If L.Contains("cmakelists") OrElse L.Contains("rebuilding from source") OrElse L.Contains("prebuild-install") Then
            Return "正在编译原生模块（这一步较慢，可能需要几分钟）……"
        End If
        If L.Contains("added ") OrElse L.Contains("packages in ") Then Return "正在收尾……"
        If L.Contains("reify") Then Return "正在解压并写入文件……"
        If L.Contains("http fetch") OrElse L.Contains("cache hit") OrElse L.Contains("tarball") Then Return "正在下载安装包……"
        If L.Contains("npm warn deprecated") Then Return "正在处理依赖……"
        Return ""
    End Function

    ''' <summary>
    ''' 结束整棵进程树（先 taskkill /T /F，失败再退回 Process.Kill）。
    ''' npm.cmd → cmd.exe → node.exe 这条链必须整体结束。
    ''' </summary>
    Private Sub DshKillProcessTree(Proc As Process)
        If Proc Is Nothing Then Return
        Try
            If Proc.HasExited Then Return
        Catch
            Return
        End Try
        Try
            Dim Killer As New ProcessStartInfo With {
                .FileName = "taskkill.exe",
                .Arguments = $"/PID {Proc.Id} /T /F",
                .UseShellExecute = False,
                .CreateNoWindow = True,
                .RedirectStandardOutput = True,
                .RedirectStandardError = True
            }
            Dim K As Process = StartProcess(Killer)
            K.StandardOutput.ReadToEnd()
            K.StandardError.ReadToEnd()
            K.WaitForExit(8000)
            Logger.Info($"已结束安装进程树（PID {Proc.Id}）")
        Catch ex As Exception
            Logger.Warn(ex, "taskkill 结束进程树失败，回退到 Kill")
            Try
                Proc.Kill()
            Catch
            End Try
        End Try
    End Sub

    ''' <summary>
    ''' 清理某个版本遗留的暂存目录（上次失败或取消留下的）。
    ''' npm 可能还有文件句柄没释放，所以重试几次并放宽等待。
    ''' </summary>
    Private Sub DshCleanStaleStages(Version As String)
        Try
            If Not DirectoryUtils.Exists(DshCacheRoot) Then Return
            For Each Dir As String In DirectoryUtils.EnumerateDirectories(DshCacheRoot, searchPattern:="install-" & Version & "*")
                For Attempt As Integer = 1 To 4
                    Try
                        DirectoryUtils.Delete(Dir)
                        Logger.Info($"已清理遗留的安装暂存目录：{Dir}")
                        Exit For
                    Catch ex As Exception
                        If Attempt = 4 Then
                            Logger.Warn(ex, $"清理暂存目录失败（已放弃）：{Dir}")
                        Else
                            Thread.Sleep(600)
                        End If
                    End Try
                Next
            Next
        Catch ex As Exception
            Logger.Warn(ex, "扫描遗留暂存目录失败")
        End Try
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
    Public DshVersionInstallTask As New LoaderTask(Of Integer, Integer)("安装 dsh 版本", AddressOf DshVersionInstallMain)

    ''' <summary>
    ''' 安装任务（用于 PCL 的任务管理器 / 后台下载队列）。
    '''
    ''' 为什么要多包一层 LoaderCombo：`LoaderTaskbarAdd` 只接受 `LoaderCombo(Of T)`，
    ''' 而且任务管理页（PageSpeedLeft.TaskRefresh）里会调用 `GetLoaderList()` —— 那是
    ''' LoaderCombo 才有的方法，直接把 LoaderTask 塞进 LoaderTaskbar 会抛 MissingMethodException。
    ''' Combo 的 Progress 是其子加载器进度的加权平均，所以单个子任务时等价于子任务进度。
    ''' 登记之后：右下角的下载按钮会出现进度、Windows 任务栏出现进度条、
    ''' 「更多 → 任务管理」里出现一张卡片（都是 PCL 原有机制，见 LoaderTaskbarProgressRefresh）。
    ''' </summary>
    Public DshVersionInstallLoader As New LoaderCombo(Of Integer)("安装 dsh 版本", {DshVersionInstallTask})

    ''' <summary>模块初始化：把任务栏登记/清理挂到安装任务上（见 DshInstallStateChanged 的说明）。</summary>
    Public Sub DshInstallInit()
        Static Done As Boolean = False
        If Done Then Return
        Done = True
        AddHandler DshVersionInstallLoader.OnStateChangedUi, AddressOf DshInstallStateChanged
    End Sub

    ''' <summary>
    ''' 启动一次安装。
    '''
    ''' 为什么只启动组合、不手动启动子任务（实机踩坑，很重要）：
    ''' PCL 的 `LoaderBase.Start(Input, IsForceRestart:=True)` 即使对**正在运行**的加载器也返回 True，
    ''' 于是会 `TriggerThreadInterrupt()` 并**在新线程上再跑一遍 LoadDelegate**。
    ''' 我原来写成"先 Start 子任务、再 Start 组合"，结果同一次安装的 worker 被执行了两次：
    ''' 第一次真的去跑 npm 了，第二次撞上并发守卫抛错 → 界面显示"安装失败"，
    ''' 而 npm 进程还在后台悄悄下 500 个包（最糟糕的失败模式）。
    '''
    ''' 正确做法：只 `Start` 组合，让组合的 `Update()` 去启动子任务。
    ''' 输入传 Nothing，于是 `ShouldStart` 里"输入类型不匹配"判定为 False，
    ''' 运行中的子任务不会被重启，任务栏的进度也由组合统一对外呈现。
    ''' </summary>
    Public Sub DshInstallStart(Version As String)
        If String.IsNullOrWhiteSpace(Version) Then Throw New Exception("未指定要安装的 dsh 版本")
        '先改名字，再启动：任务管理器卡片是按加载器名字建的，
        '名字必须在卡片创建之前就带上版本号（用户反馈"没显示安装哪个版本"）
        DshSetInstallTaskName(Version)
        DshRequestVersionInstall(Version)
        DshVersionInstallLoader.Start(Nothing, IsForceRestart:=True)
    End Sub

    ''' <summary>把安装任务登记到任务管理器（后台下载队列）。</summary>
    Public Sub DshInstallAddToTaskbar()
        Try
            If Not LoaderTaskbar.Contains(DshVersionInstallLoader) Then
                LoaderTaskbarAdd(DshVersionInstallLoader)
            End If
            DshInstallTaskbarWatched = True
            RunInUi(Sub()
                        Try
                            FrmMain.BtnExtraDownload.ShowRefresh()
                        Catch
                        End Try
                    End Sub)
        Catch ex As Exception
            Logger.Warn(ex, "把 dsh 安装任务加入任务列表失败")
        End Try
    End Sub
    Private DshInstallTaskbarWatched As Boolean = False

    ''' <summary>
    ''' 任务结束后把它移出任务列表。
    ''' 正常情况下 LoaderTaskbarProgressRefresh 会自动移除，这里兜底（避免界面卡着一张旧卡片）。
    ''' </summary>
    Public Sub DshInstallRemoveFromTaskbar()
        Try
            If LoaderTaskbar.Contains(DshVersionInstallLoader) Then
                LoaderTaskbar.Remove(DshVersionInstallLoader)
                FrmSpeedLeft?.TaskRemove(DshVersionInstallLoader)
            End If
            'PCL 没有公开的 LoaderTaskbarRemove，直接操作列表，
            '并按 PCL 的日志格式自己记一行，方便日后排查
            LoaderTaskbar.Remove(DshVersionInstallLoader)
            Logger.Info($"{DshVersionInstallLoader.Name} 已移出任务列表")
            RunInUi(Sub()
                        Try
                            FrmMain.BtnExtraDownload.ShowRefresh()
                        Catch
                        End Try
                    End Sub)
        Catch ex As Exception
            Logger.Warn(ex, "把 dsh 安装任务移出任务列表失败")
        End Try
    End Sub

    ''' <summary>
    ''' 模块级跟踪安装任务的状态。
    ''' 为什么放在模块里而不是页面里（实机设计教训）：安装是**后台任务**，
    ''' 用户完全可能在安装途中切走页面。如果注册/清理逻辑挂在页面的事件处理器上，
    ''' 页面一旦销毁，任务栏里就会残留一张永远不消失的卡片。
    ''' 注意：这里只做任务栏生命周期管理；"刷新列表/提示"由 InstallAfterAction 与
    ''' DshVersionInstallMain 负责，重复刷新会让版本列表加载器被无谓地中断重跑（实机见过）。
    ''' </summary>
    Private Sub DshInstallStateChanged(Loader As LoaderBase, NewState As LoadState, OldState As LoadState)
        Select Case NewState
            Case LoadState.Loading
                DshInstallAddToTaskbar()
            Case LoadState.Finished, LoadState.Failed, LoadState.Canceled
                DshInstallCleanup()
        End Select
    End Sub

    ''' <summary>安装任务结束后的收尾。</summary>
    Public Sub DshInstallCleanup()
        DshInstallRemoveFromTaskbar()
        DshInstallTaskbarWatched = False
    End Sub

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
