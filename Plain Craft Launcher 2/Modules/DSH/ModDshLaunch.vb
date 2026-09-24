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

    ''' <summary>
    ''' 从一行输出里提取启动器要打开的那个地址。
    '''
    ''' 重要（2026-09-24 实测）：dsh web 启动后会打印
    '''     dsh web: http://127.0.0.1:3412/?token=8D70xOWT...V2Ss
    ''' 这个 token 是**唯一鉴权输入**，必须原样带给浏览器，否则：
    '''     · 不带 token 访问  /            → HTTP 401（用户只会看到一个未授权页）
    '''     · 带 token 访问    /?token=...  → HTTP 303，换发 cookie 并重定向到干净路径
    '''     · 带 cookie 再访问 /            → HTTP 200（正常进 GUI）
    ''' 所以这里必须把 query string 一起抓下来，不能只截到端口号。
    ''' </summary>
    Private Function DshExtractUrl(Text As String) As String
        Try
            Dim M As System.Text.RegularExpressions.Match =
                System.Text.RegularExpressions.Regex.Match(Text, "https?://(?:127\.0\.0\.1|localhost|\[::1\]):\d+[^\s""'<>)]*")
            If M.Success Then Return M.Value.TrimEnd("."c, ","c, ";"c)
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
            Dim Reuse As String = If(DshWebUrl <> "", DshWebUrl, DshCachedUrlLoad(Instance))
            If Reuse = "" Then Reuse = DshLocalUrl(Instance.Port)
            If Instance.EffectiveAutoOpenBrowser Then DshOpenBrowser(Reuse)
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
        '
        '为什么不能"看到端口通就复用"（实机踩坑）：dsh 的访问 token 是**进程级**的，
        '启动器无法事后拼出来；而且如果端口上是一个启动器不知道的 dsh 进程，
        '直接打开干净 URL 只会得到 401。更糟的是：若我们就这么返回，用户以为启动成功了，
        '实际并没有发起新的启动。所以这里的策略是：
        '  ① 端口上确实有本实例此前启动的服务，且我们缓存过它的带 token 地址，且该地址**当前有效**
        '     → 复用它开浏览器；
        '  ② 端口被占用但没有有效缓存 → 换一个空闲端口，重新启动一个我们自己的实例。
        Loader.Progress = 0.2
        Dim Port As Integer = Instance.Port
        If Port <= 0 Then Port = DshDefaultPort
        If DshPortInUse(Port) Then
            Dim Cached As String = DshCachedUrlLoad(Instance)
            If Cached <> "" AndAlso DshUrlUsable(Cached) AndAlso Cached.Contains($":{Port}") Then
                DshLog($"端口 {Port} 上本实例的服务仍在运行，复用缓存的访问地址", Loader)
                DshWebUrl = Cached
                Instance.Port = Port
                If Instance.EffectiveAutoOpenBrowser Then RunInUi(Sub() DshOpenBrowser(Cached))
                RunInUi(Sub() Hint($"整合包「{Instance.Name}」的服务仍在运行，已打开浏览器", HintType.Green))
                Return
            End If
            Dim ProbeCode As Integer = DshProbeHttpStatus(Port)
            DshLog($"端口 {Port} 已被占用（HTTP {ProbeCode}），且没有可用的访问地址缓存。", Loader)
            If ProbeCode = 401 OrElse ProbeCode = 200 OrElse ProbeCode = 303 Then
                DshLog("该端口上确实有 dsh 在跑，但它的访问 token 属于别的进程，启动器无法复用；" &
                       "如果你希望启动器接管它，请先手动结束那个进程（或在整合包管理页点「关闭 DSH」）。", Loader)
            End If
            Dim NewPort As Integer = DshFindFreePort(Port + 1)
            If NewPort <= 0 Then Throw New Exception($"端口 {Port} 被占用，且找不到可用端口")
            DshLog($"改用空闲端口 {NewPort} 重新启动", Loader)
            Port = NewPort
            Instance.Port = NewPort
            DshWriteManifest(Instance)
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

        '6. 等待就绪：优先等输出里的带 token 地址，其次轮询端口
        Loader.Progress = 0.5
        Dim Deadline As Long = GetTimeMs() + 90 * 1000
        Dim PortReady As Boolean = False
        Do Until GetTimeMs() > Deadline
            If Loader.State = LoadState.Canceled Then
                DshLog("启动已取消", Loader)
                Return
            End If
            If Proc.HasExited Then
                Throw New Exception($"dsh 进程意外退出（退出码 {Proc.ExitCode}）。" & vbCrLf & DshRecentOutput())
            End If
            '带 token 的地址是最可靠的"就绪"信号；只有拿不到时才退回端口探测
            If DshWebUrl <> "" Then Exit Do
            If Not PortReady Then PortReady = DshPortInUse(Port)
            '端口通了之后再给输出一点时间（token 行通常紧随其后）
            If PortReady AndAlso GetTimeMs() > Deadline - 85 * 1000 Then Exit Do
            '进度在 0.5~0.9 之间缓慢推进
            Loader.Progress = Math.Min(0.9, 0.5 + (GetTimeMs() - (Deadline - 90 * 1000)) / 900000.0)
            Thread.Sleep(200)
        Loop

        '兜底：没抓到 token 地址时，按端口拼一个干净地址（会 401，但至少让用户看到日志提示）
        Dim GotTokenUrl As Boolean = DshWebUrl <> ""
        If Not GotTokenUrl Then DshWebUrl = DshLocalUrl(Port)
        If Not DshPortInUse(Port) Then DshLog("端口探测未通过，但仍按已推断的地址继续", Loader)

        '7. 打开浏览器（需求 1 的核心）
        Loader.Progress = 0.95
        DshLog($"DeepSeekHarness 已就绪：{DshWebUrl}", Loader)
        If Not GotTokenUrl Then
            DshLog("⚠ 没能从 dsh 输出里解析出带 ?token= 的地址。dsh 的访问 token 是进程独有的，" &
                   "直接访问不带 token 的地址会得到 401。请查看下方日志里的原始 `dsh web:` 行。", Loader)
        End If
        If Instance.EffectiveAutoOpenBrowser Then
            RunInUi(Sub() DshOpenBrowser(DshWebUrl))
        Else
            DshLog("已按设置跳过自动打开浏览器", Loader)
        End If
        '缓存这次可用的带 token 地址：下次启动器重启后若该 dsh 仍在后台跑，可以直接复用
        If GotTokenUrl Then DshCachedUrlSave(Instance, DshWebUrl)

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
            Dim Inst As DshInstance = DshProcessInstance
            DshCurrentProcess.Kill()
            Logger.Info($"已关闭整合包 {Name} 的 DeepSeekHarness 进程")
            If Not Quiet Then Hint($"已关闭 {Name} 的 DeepSeekHarness", HintType.Green)
            '进程没了，缓存的 token 地址也随之失效，清掉避免下次误复用
            If Inst IsNot Nothing Then
                Try
                    Dim P As String = Inst.PathInstance & ".pcl-web-url"
                    If FileUtils.Exists(P) Then FileUtils.Delete(P)
                Catch
                End Try
            End If
            DshWebUrl = ""
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

    ''' <summary>
    ''' 诊断用：返回当前 DSH 运行状态的一句话描述（写进日志便于排查"退出时是否结束进程"之类的行为）。
    ''' </summary>
    Public Function DshStateText() As String
        Dim StopOnExit As Boolean = DshSetting("DshStopOnExit", False)
        Return $"DshIsRunning={DshIsRunning}, DshStopOnExit={StopOnExit}, " &
               $"实例={If(DshProcessInstance Is Nothing, "无", DshProcessInstance.Name)}, " &
               $"端口={If(DshProcessInstance Is Nothing, -1, DshProcessInstance.Port)}, " &
               $"URL={If(DshWebUrl = "", "（未捕获）", DshWebUrl)}"
    End Function

#Region "访问地址缓存"

    ''' <summary>
    ''' 缓存某个整合包当前可用的"带 token 访问地址"。
    ''' dsh 的 token 是进程级的，启动器重启后无法重新拼出来；缓存它，
    ''' 就能在"上次退出时 dsh 仍在后台运行"的情况下直接复用（前提是该地址仍有效）。
    ''' </summary>
    Public Sub DshCachedUrlSave(Instance As DshInstance, Url As String)
        If Instance Is Nothing OrElse String.IsNullOrWhiteSpace(Url) Then Return
        Try
            FileUtils.Write(Instance.PathInstance & ".pcl-web-url", Url.Trim(), New UTF8Encoding(False))
        Catch ex As Exception
            Logger.Warn(ex, "缓存访问地址失败")
        End Try
    End Sub

    ''' <summary>读取缓存的访问地址；没有则返回空字符串。</summary>
    Public Function DshCachedUrlLoad(Instance As DshInstance) As String
        If Instance Is Nothing Then Return ""
        Try
            Dim P As String = Instance.PathInstance & ".pcl-web-url"
            If Not FileUtils.Exists(P) Then Return ""
            Return FileUtils.ReadAsString(P).Trim()
        Catch
            Return ""
        End Try
    End Function

    ''' <summary>测试一个带 token 的地址当前是否仍然可用（303/200 视为可用）。</summary>
    Public Function DshUrlUsable(Url As String) As Boolean
        If String.IsNullOrWhiteSpace(Url) Then Return False
        Try
            Dim Req As Net.HttpWebRequest = CType(Net.WebRequest.Create(Url), Net.HttpWebRequest)
            Req.Method = "GET"
            Req.Timeout = 5000
            Req.AllowAutoRedirect = False
            Req.UserAgent = $"PCL2-DSH/{VersionBaseName}"
            Using Resp As Net.HttpWebResponse = CType(Req.GetResponse(), Net.HttpWebResponse)
                Return True
            End Using
        Catch ex As Net.WebException
            '303 会以异常形式抛出（AllowAutoRedirect=False）
            If ex.Response IsNot Nothing Then
                Try
                    Dim Code As Integer = CInt(CType(ex.Response, Net.HttpWebResponse).StatusCode)
                    Return Code = 303 OrElse Code = 200
                Catch
                End Try
            End If
            Return False
        Catch
            Return False
        End Try
    End Function

#End Region

    ''' <summary>在设置页/实例页刷新时，同步一次进程状态。</summary>
    Public Function DshProcessAlive(Instance As DshInstance) As Boolean
        If Instance Is Nothing Then Return False
        If DshIsRunning AndAlso DshProcessInstance IsNot Nothing AndAlso DshProcessInstance.PathInstance = Instance.PathInstance Then Return True
        Return DshPortInUse(Instance.Port)
    End Function

#End Region

End Module
