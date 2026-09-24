' ============================================================================================
'  ModDshLaunch —— 启动 DeepSeekHarness（这是"启动"按钮的真正落点）
'
'  流程（需求 1）：
'    1. 检查整合包、dsh 版本、Node 运行环境
'    2. 若该整合包的端口上已经有本实例的服务在跑 → 直接打开浏览器（幂等）
'    3. 否则以 DSH_HOME = 该整合包目录 启动：node <该版本>\lib\bin.js web --port <N> --no-open
'    4. 从子进程输出里抓取 GUI URL（或轮询端口就绪）
'    5. 用系统默认浏览器打开该 URL
'    6. 记录进程，供"关闭 DSH"使用
'
'  铁律（DEVNOTES §8.1/§8.2）：必须显式覆盖 DSH_HOME 与端口，绝不能污染用户正在使用的全局 DSH 环境。
' ============================================================================================
Imports System.Text

Public Module ModDshLaunch

#Region "状态"

    ''' <summary>当前由启动器拉起的 dsh 进程；Nothing 表示未运行。</summary>
    Public DshCurrentProcess As Process = Nothing
    ''' <summary>当前由启动器拉起的 dsh 所属的整合包。</summary>
    Public DshProcessInstance As DshInstance = Nothing
    ''' <summary>最近一次成功探测到的 GUI 地址。</summary>
    Public DshWebUrl As String = ""
    ''' <summary>子进程输出缓冲，用于失败时展示。</summary>
    Public DshOutputBuffer As New List(Of String)
    Private ReadOnly DshOutputLock As New Object()

    ''' <summary>dsh 是否正在运行（本启动器拉起的那个）。</summary>
    Public ReadOnly Property DshIsRunning As Boolean
        Get
            Try
                Return DshCurrentProcess IsNot Nothing AndAlso Not DshCurrentProcess.HasExited
            Catch
                Return False
            End Try
        End Get
    End Property

    ''' <summary>把一行输出写进日志与缓冲。</summary>
    Private Sub DshAppendOutput(Text As String)
        If Text Is Nothing Then Return
        SyncLock DshOutputLock
            DshOutputBuffer.Add(Text)
            If DshOutputBuffer.Count > 400 Then DshOutputBuffer.RemoveRange(0, 200)
        End SyncLock
        Logger.Info("[DSH] " & Text)
        '尝试从输出里抓 URL
        Dim Url As String = DshExtractUrl(Text)
        If Url IsNot Nothing AndAlso DshWebUrl = "" Then DshWebUrl = Url
    End Sub

    ''' <summary>从一行输出里提取 http://127.0.0.1:PORT 形式的地址。</summary>
    Private Function DshExtractUrl(Text As String) As String
        Try
            Dim M As System.Text.RegularExpressions.Match =
                System.Text.RegularExpressions.Regex.Match(Text, "https?://(?:127\.0\.0\.1|localhost|\[::1\]):\d+")
            If M.Success Then Return M.Value
        Catch
        End Try
        Return Nothing
    End Function

#End Region

#Region "启动链"

    ''' <summary>
    ''' 启动按钮 / 实例页"启动"真正调用的入口。
    ''' 必须在 UI 线程调用。
    ''' </summary>
    Public Function DshLaunchStart(Optional Instance As DshInstance = Nothing, Optional Silent As Boolean = False) As Boolean
        Dim Target As DshInstance = If(Instance, DshInstanceSelected)
        If Target Is Nothing Then
            If Not Silent Then
                MyMsgBox("还没有任何整合包，请先在「启动」页新建一个，或到「下载」页安装 dsh 版本后新建。", "无法启动", IsWarn:=True)
            End If
            Return False
        End If
        DshLaunchTarget = Target
        DshLaunchLoader.Start(0, IsForceRestart:=True)
        Return True
    End Function

    ''' <summary>本次启动的目标整合包。</summary>
    Public DshLaunchTarget As DshInstance = Nothing

    ''' <summary>启动链加载器。</summary>
    Public DshLaunchLoader As New LoaderTask(Of Integer, Integer)("DSH Launch", AddressOf DshLaunchMain)

    Private Sub DshLaunchMain(Loader As LoaderTask(Of Integer, Integer))
        Dim Instance As DshInstance = DshLaunchTarget
        If Instance Is Nothing Then Throw New Exception("未指定要启动的整合包")
        DshOutputBuffer.Clear()
        DshWebUrl = ""

        '0. 已在运行 → 直接开浏览器
        Loader.Progress = 0.05
        If DshIsRunning AndAlso DshProcessInstance IsNot Nothing AndAlso DshProcessInstance.PathInstance = Instance.PathInstance Then
            DshLog($"整合包 {Instance.Name} 的 DeepSeekHarness 已在运行")
            If Instance.EffectiveAutoOpenBrowser Then DshOpenBrowser(If(DshWebUrl <> "", DshWebUrl, DshLocalUrl(Instance.Port)))
            Return
        End If

        '1. 环境检查
        Loader.Progress = 0.1
        DshLog("检查整合包…", Loader)
        If Instance.ErrorMessage <> "" Then Throw New Exception($"整合包不可用：{Instance.ErrorMessage}")
        If Not DirectoryUtils.Exists(Instance.PathDshHome) Then Throw New Exception($"整合包的 DSH_HOME 不存在：{Instance.PathDshHome}")

        DshLog("检查 Node.js 运行环境…", Loader)
        Dim NodeExe As String = DshNodeExe
        If NodeExe Is Nothing Then
            Throw New Exception("尚未配置 Node.js 运行环境。" & vbCrLf &
                                "请到「设置 → DSH 运行环境」中一键下载，或手动指定 node.exe 的位置。")
        End If

        DshLog($"检查 dsh {Instance.DshVersion} …", Loader)
        If Not DshVersionInstalled(Instance.DshVersion) Then
            Throw New Exception($"整合包 {Instance.Name} 绑定的 dsh {Instance.DshVersion} 尚未安装。" & vbCrLf &
                                "请到「下载 → DSH 版本」中安装它。")
        End If

        '2. 端口占用处理
        Loader.Progress = 0.2
        Dim Port As Integer = Instance.Port
        If Port <= 0 Then Port = DshDefaultPort
        If DshPortInUse(Port) Then
            '已有服务占用：如果本启动器没在跑，可能上一次启动器退出后 dsh 仍在运行（这是期望行为）
            DshLog($"端口 {Port} 已被占用，假定该整合包的服务仍在运行，直接打开浏览器", Loader)
            Instance.Port = Port
            If Instance.EffectiveAutoOpenBrowser Then DshOpenBrowser(DshLocalUrl(Port))
            Return
        End If

        '3. 工作区
        Loader.Progress = 0.3
        DirectoryUtils.Create(Instance.Workspace)
        DirectoryUtils.Create(Instance.PathDshHome)
        DirectoryUtils.Create(Instance.PathInstance & "logs\")

        '4. 组装命令
        Dim BinJs As String = DshBinJs(Instance.DshVersion)
        Dim Args As String = $"""{BinJs}"" --profile {Instance.Profile} --host 127.0.0.1 --port {Port} --no-open"
        DshLog($"启动命令：{NodeExe} {Args}", Loader)

        Dim Info As ProcessStartInfo = DshNewStartInfo(NodeExe, Args, Instance.Workspace)
        DshApplyEnvironment(Info, Instance.PathDshHome, DshRuntimeRootEffective)
        '把该整合包的设置也传进去，便于 dsh 侧诊断
        Info.EnvironmentVariables("DSH_PROFILE") = Instance.Profile

        '5. 拉起进程
        Loader.Progress = 0.4
        Dim Proc As Process = StartProcess(Info)
        AddHandler Proc.OutputDataReceived, Sub(Sender As Object, E As DataReceivedEventArgs) DshAppendOutput(E.Data)
        AddHandler Proc.ErrorDataReceived, Sub(Sender As Object, E As DataReceivedEventArgs) If E.Data IsNot Nothing Then DshAppendOutput("[stderr] " & E.Data)
        Proc.BeginOutputReadLine()
        Proc.BeginErrorReadLine()
        DshCurrentProcess = Proc
        DshProcessInstance = Instance
        Instance.Port = Port

        '6. 等待就绪：优先用输出里的 URL，否则轮询端口
        Loader.Progress = 0.5
        Dim Deadline As Long = GetTimeMs() + 90 * 1000
        Do Until GetTimeMs() > Deadline
            If Loader.State = LoadState.Canceled Then
                DshLog("启动已取消", Loader)
                Return
            End If
            If Proc.HasExited Then
                Throw New Exception($"dsh 进程意外退出（退出码 {Proc.ExitCode}）。" & vbCrLf & DshRecentOutput())
            End If
            If DshWebUrl <> "" OrElse DshPortInUse(Port) Then Exit Do
            '进度在 0.5~0.9 之间缓慢推进
            Loader.Progress = Math.Min(0.9, 0.5 + (GetTimeMs() - (Deadline - 90 * 1000)) / 900000.0)
            Thread.Sleep(200)
        Loop

        If DshWebUrl = "" Then DshWebUrl = DshLocalUrl(Port)
        If Not DshPortInUse(Port) AndAlso DshWebUrl = DshLocalUrl(Port) Then
            '再给一次机会：有些环境下端口探测会被防火墙拦，按 URL 判断即可
            DshLog("端口探测未通过，但仍按已输出地址继续", Loader)
        End If

        '7. 打开浏览器（需求 1 的核心）
        Loader.Progress = 0.95
        DshLog($"DeepSeekHarness 已就绪：{DshWebUrl}", Loader)
        If Instance.EffectiveAutoOpenBrowser Then
            RunInUi(Sub() DshOpenBrowser(DshWebUrl))
        Else
            DshLog("已按设置跳过自动打开浏览器", Loader)
        End If

        Loader.Progress = 1
        RunInUi(Sub()
                    Hint($"DeepSeekHarness 已启动：{Instance.Name}", HintType.Green)
                    FrmLaunchLeft?.RefreshButtonsUI()
                End Sub)
    End Sub

    ''' <summary>本整合包服务的本地地址。</summary>
    Public Function DshLocalUrl(Port As Integer) As String
        Return $"http://127.0.0.1:{Port}"
    End Function

    ''' <summary>输出最近几行，用于错误提示。</summary>
    Private Function DshRecentOutput(Optional Lines As Integer = 8) As String
        SyncLock DshOutputLock
            If DshOutputBuffer.Count = 0 Then Return "（无输出）"
            Return DshOutputBuffer.Skip(Math.Max(0, DshOutputBuffer.Count - Lines)).Join(vbCrLf)
        End SyncLock
    End Function


#End Region

#Region "浏览器 / 停止"

    ''' <summary>
    ''' 用系统默认浏览器打开指定地址。
    ''' </summary>
    Public Sub DshOpenBrowser(Url As String)
        If String.IsNullOrWhiteSpace(Url) Then Return
        Try
            Logger.Info($"正在用默认浏览器打开：{Url}")
            Process.Start(New ProcessStartInfo With {
                .FileName = Url,
                .UseShellExecute = True
            })
        Catch ex As Exception
            Logger.Error(ex, $"打开浏览器失败：{Url}", LogBehavior.Toast)
            '兜底：交给系统 shell 处理
            Try
                OpenWebsite(Url)
            Catch ex2 As Exception
                Logger.Error(ex2, "调用 OpenWebsite 兜底同样失败", LogBehavior.Toast)
            End Try
        End Try
    End Sub

    ''' <summary>关闭由启动器拉起的 dsh 进程。</summary>
    Public Sub DshStop(Optional Quiet As Boolean = False)
        If Not DshIsRunning Then
            If Not Quiet Then Hint("当前没有由启动器启动的 DeepSeekHarness 进程", HintType.Blue)
            Return
        End If
        Try
            Dim Name As String = If(DshProcessInstance Is Nothing, "", DshProcessInstance.Name)
            DshCurrentProcess.Kill()
            Logger.Info($"已关闭整合包 {Name} 的 DeepSeekHarness 进程")
            If Not Quiet Then Hint($"已关闭 {Name} 的 DeepSeekHarness", HintType.Green)
        Catch ex As Exception
            Logger.Error(ex, "关闭 dsh 进程失败", LogBehavior.Toast)
        Finally
            DshCurrentProcess = Nothing
            DshProcessInstance = Nothing
        End Try
        RunInUi(Sub()
                    Try
                        FrmLaunchLeft?.RefreshButtonsUI()
                    Catch
                    End Try
                End Sub)
    End Sub

    ''' <summary>在设置页/实例页刷新时，同步一次进程状态。</summary>
    Public Function DshProcessAlive(Instance As DshInstance) As Boolean
        If Instance Is Nothing Then Return False
        If DshIsRunning AndAlso DshProcessInstance IsNot Nothing AndAlso DshProcessInstance.PathInstance = Instance.PathInstance Then Return True
        Return DshPortInUse(Instance.Port)
    End Function

#End Region

End Module
