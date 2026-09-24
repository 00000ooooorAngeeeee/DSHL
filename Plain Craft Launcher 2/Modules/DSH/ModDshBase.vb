' ============================================================================================
'  ModDshBase —— DeepSeekHarness 启动器的路径、运行环境与通用工具
'
'  本模块是整个 DSH 改造的地基，职责：
'    1. 定义 DSH 相关的全部根目录（运行环境 / 版本仓库 / 整合包实例）
'    2. 检测与管理 Node.js 运行环境（node.exe、npm）
'    3. 计算某个 dsh 版本的安装路径
'    4. 提供进程环境变量准备、端口探测等通用能力
'
'  约定见 DEVNOTES.md §2 目录布局、§8 注意事项。
' ============================================================================================
Public Module ModDshBase

#Region "常量"

    ''' <summary>当前内置的 DSH 默认版本（仅在用户未做任何选择时使用，不代表已安装）。</summary>
    Public Const DshDefaultVersion As String = "0.1.7-rc.1"

    ''' <summary>DSH 默认监听端口。</summary>
    Public Const DshDefaultPort As Integer = 3080

    ''' <summary>npm 包名。</summary>
    Public Const DshPackageName As String = "@deepseek-ai/dsh"

    ''' <summary>官网仓库（用于展示与获取发布时间）。</summary>
    Public Const DshGithubRepo As String = "deepseek-ai/deepseek-harness"

    ''' <summary>npm registry（官方）。</summary>
    Public Const DshNpmRegistry As String = "https://registry.npmjs.org/"

    ''' <summary>npm registry 国内镜像。</summary>
    Public Const DshNpmRegistryMirror As String = "https://registry.npmmirror.com/"

    ''' <summary>Node.js 镜像索引（国内）。</summary>
    Public Const DshNodeIndexMirror As String = "https://npmmirror.com/mirrors/node/index.json"

    ''' <summary>Node.js 镜像下载前缀。</summary>
    Public Const DshNodeDownloadPrefix As String = "https://npmmirror.com/mirrors/node/"

    ''' <summary>要求的 Node.js 最低主版本（dsh 依赖 chokidar 5，要求 >= 20.19）。</summary>
    Public Const DshNodeMinMajor As Integer = 20

#End Region

#Region "根目录"

    ''' <summary>
    ''' DSH 数据根目录，以 \ 结尾，位于启动器所在目录下的 DSH\。
    ''' </summary>
    Public ReadOnly Property DshRoot As String
        Get
            Return Paths.Base & "DSH\"
        End Get
    End Property

    ''' <summary>共用 Node 运行环境目录（node\node.exe 与 node\npm.cmd）。</summary>
    Public ReadOnly Property DshRuntimeRoot As String
        Get
            Return DshRoot & "runtime\node\"
        End Get
    End Property

    ''' <summary>下载缓存目录。</summary>
    Public ReadOnly Property DshCacheRoot As String
        Get
            Return DshRoot & "cache\"
        End Get
    End Property

    ''' <summary>启动器自带（内置）的 Node 可执行文件路径，即 runtime\node\node.exe。</summary>
    Public ReadOnly Property DshBundledNode As String
        Get
            Return DshRuntimeRoot & "node.exe"
        End Get
    End Property

    ''' <summary>启动器自带 Node 对应的 npm 命令路径。</summary>
    Public ReadOnly Property DshBundledNpm As String
        Get
            Return DshRuntimeRoot & "npm.cmd"
        End Get
    End Property

    ''' <summary>整合包实例根目录。</summary>
    Public ReadOnly Property DshInstanceRoot As String
        Get
            Return DshRoot & "instances\"
        End Get
    End Property

#End Region

#Region "设置读取（带安全兜底）"

    ''' <summary>DSH 设置项的本地缓存，见 DshSetting 的说明。</summary>
    Private ReadOnly DshSettingCache As New ConcurrentDictionary(Of String, Object)

    ''' <summary>
    ''' 安全读取一个全局设置项。设置项不存在或读取异常时返回默认值，绝不抛出。
    ''' 用于在设置项尚未注册（例如旧版本配置文件）时仍能启动。
    ''' 重要（DEVNOTES §8.7）：不能用 Settings.Get 去"探测"设置项是否存在——
    ''' PCL 内部会执行 CTypeDynamic(Value, Entry.Type)，把 Nothing 转成该类型的默认值，
    ''' 因此 `Settings.Get(Key) Is Nothing` 永远不成立。这里额外维护一份本地缓存，
    ''' 让未注册的键也能安全返回默认值。
    ''' </summary>
    Public Function DshSetting(Of T)(Key As String, DefaultValue As T) As T
        Dim Cached As Object = Nothing
        If DshSettingCache.TryGetValue(Key, Cached) AndAlso Cached IsNot Nothing Then
            Try
                Return CTypeDynamic(Cached, GetType(T))
            Catch
                Return DefaultValue
            End Try
        End If
        Try
            Dim Value As Object = Settings.Get(Key)
            If Value Is Nothing Then Return DefaultValue
            DshSettingCache(Key) = Value
            Return CTypeDynamic(Value, GetType(T))
        Catch
            Return DefaultValue
        End Try
    End Function

    ''' <summary>安全写入一个全局设置项。</summary>
    Public Sub DshSetSetting(Key As String, Value As Object)
        DshSettingCache(Key) = Value
        Try
            Settings.Set(Key, Value)
        Catch ex As Exception
            Logger.Warn(ex, $"写入设置项失败：{Key}")
        End Try
    End Sub

    ''' <summary>dsh 本体（版本仓库）位置，以 \ 结尾。</summary>
    Public ReadOnly Property DshVersionRoot As String
        Get
            Dim Custom As String = DshSetting("DshVersionRoot", "")
            If String.IsNullOrWhiteSpace(Custom) Then Return DshRoot & "versions\"
            Return PathUtils.AddSlashSuffix(Custom.Replace("$", Paths.Base))
        End Get
    End Property

    ''' <summary>
    ''' Node 运行环境位置（node.exe 所在目录），以 \ 结尾。
    ''' 优先用户设置，其次启动器自带，最后回退到系统安装位置。
    ''' </summary>
    Public ReadOnly Property DshRuntimeRootEffective As String
        Get
            Dim Custom As String = DshSetting("DshRuntimeRoot", "")
            If Not String.IsNullOrWhiteSpace(Custom) Then Return PathUtils.AddSlashSuffix(Custom.Replace("$", Paths.Base))
            If FileUtils.Exists(DshBundledNode) Then Return DshRuntimeRoot
            '回退：系统 PATH 中的 node
            Dim SystemNode As String = DshFindNodeInPath()
            If SystemNode IsNot Nothing Then Return PathUtils.AddSlashSuffix(System.IO.Path.GetDirectoryName(SystemNode))
            Return DshRuntimeRoot
        End Get
    End Property

    ''' <summary>当前生效的 node.exe 路径；找不到时返回 Nothing。</summary>
    Public ReadOnly Property DshNodeExe As String
        Get
            Dim Root As String = DshRuntimeRootEffective
            If FileUtils.Exists(Root & "node.exe") Then Return Root & "node.exe"
            Return Nothing
        End Get
    End Property

    ''' <summary>当前生效的 npm.cmd 路径；找不到时返回 Nothing。</summary>
    Public ReadOnly Property DshNpmCmd As String
        Get
            Dim Root As String = DshRuntimeRootEffective
            If FileUtils.Exists(Root & "npm.cmd") Then Return Root & "npm.cmd"
            '某些 Node 安装把 npm 放在 node_modules\npm\bin 下，启动器不处理这种罕见情况
            Return Nothing
        End Get
    End Property

    ''' <summary>在系统 PATH 中查找 node.exe。</summary>
    Public Function DshFindNodeInPath() As String
        Try
            Dim PathEnv As String = Environment.GetEnvironmentVariable("PATH")
            If String.IsNullOrEmpty(PathEnv) Then Return Nothing
            For Each Dir As String In PathEnv.Split(";"c)
                If String.IsNullOrWhiteSpace(Dir) Then Continue For
                Dim Candidate As String = Nothing
                Try
                    Candidate = PathUtils.AddSlashSuffix(Dir.Trim()) & "node.exe"
                Catch
                    Continue For
                End Try
                If FileUtils.Exists(Candidate) Then Return Candidate
            Next
        Catch ex As Exception
            Logger.Warn(ex, "在 PATH 中查找 Node 失败")
        End Try
        Return Nothing
    End Function

#End Region

#Region "dsh 版本安装"

    ''' <summary>某个 dsh 版本在本地的安装目录（以 \ 结尾）。</summary>
    Public Function DshVersionPath(Version As String) As String
        Return PathUtils.AddSlashSuffix(DshVersionRoot & Version)
    End Function

    ''' <summary>某个 dsh 版本的 bin.js（CLI 入口）路径。</summary>
    Public Function DshBinJs(Version As String) As String
        Return DshVersionPath(Version) & "node_modules\" & DshPackageName.Replace("/", "\") & "\lib\bin.js"
    End Function

    ''' <summary>该 dsh 版本是否已安装完成（bin.js 存在，且标记文件存在）。</summary>
    Public Function DshVersionInstalled(Version As String) As Boolean
        If String.IsNullOrWhiteSpace(Version) Then Return False
        Return FileUtils.Exists(DshBinJs(Version)) AndAlso FileUtils.Exists(DshVersionPath(Version) & ".dsh-installed")
    End Function

    ''' <summary>扫描版本仓库中已安装的所有版本，按版本号倒序。</summary>
    Public Function DshInstalledVersions() As List(Of String)
        Dim Result As New List(Of String)
        Try
            If DirectoryUtils.Exists(DshVersionRoot) Then
                For Each Dir As String In DirectoryUtils.EnumerateDirectories(DshVersionRoot)
                    Dim Name As String = PathUtils.GetLastPart(Dir)
                    If DshVersionInstalled(Name) Then Result.Add(Name)
                Next
            End If
        Catch ex As Exception
            Logger.Warn(ex, "扫描已安装的 DSH 版本失败")
        End Try
        Return Result.OrderByDescending(Function(v) DshVersionSortKey(v)).ToList()
    End Function

    ''' <summary>
    ''' 生成用于排序的版本键：把 0.1.7-rc.1 变成可比较的数值。
    ''' 正式版 > rc > alpha。
    ''' </summary>
    Public Function DshVersionSortKey(Version As String) As Long
        Try
            Dim Base As String = Version
            Dim Stage As Integer = 2 '默认按 rc
            Dim StageNum As Integer = 0
            If Base.Contains("-") Then
                Dim Parts = Base.Split("-"c)
                Base = Parts(0)
                Dim Label As String = Parts(1).ToLowerInvariant()
                If Label.StartsWith("alpha") Then Stage = 0 Else If Label.StartsWith("rc") Then Stage = 1 Else Stage = 2
                Dim DotIndex As Integer = Label.LastIndexOf("."c)
                If DotIndex >= 0 Then Integer.TryParse(Label.Substring(DotIndex + 1), StageNum)
            End If
            Dim Nums = Base.Split("."c)
            Dim Major As Integer = 0, Minor As Integer = 0, Patch As Integer = 0
            If Nums.Length > 0 Then Integer.TryParse(Nums(0), Major)
            If Nums.Length > 1 Then Integer.TryParse(Nums(1), Minor)
            If Nums.Length > 2 Then Integer.TryParse(Nums(2), Patch)
            Return CLng(Major) * 1000000000L + CLng(Minor) * 10000000L + CLng(Patch) * 100000L + CLng(Stage) * 10000L + CLng(StageNum)
        Catch
            Return 0
        End Try
    End Function

    ''' <summary>判断版本标签类型：alpha / rc / stable。参数为完整版本号（如 0.1.7-rc.1）。</summary>
    Public Function DshVersionChannel(Version As String) As String
        Dim Lower As String = If(Version, "").ToLowerInvariant()
        If Lower.Contains("-alpha") Then Return "alpha"
        If Lower.Contains("-rc") Then Return "rc"
        If Lower.Contains("-beta") Then Return "rc" 'beta 归入 rc 分组展示
        Return "stable"
    End Function

#End Region

#Region "运行环境检测"

    ''' <summary>检测结果：Node 是否可用。</summary>
    Public Function DshNodeReady() As Boolean
        Return DshNodeExe IsNot Nothing
    End Function

    ''' <summary>读取 node.exe 的版本号，如 "v22.23.1"；失败返回 Nothing。</summary>
    Public Function DshNodeVersion() As String
        Dim Exe As String = DshNodeExe
        If Exe Is Nothing Then Return Nothing
        Try
            Dim Output As String = DshRunAndCapture(Exe, "--version", Nothing, 8000)
            If String.IsNullOrWhiteSpace(Output) Then Return Nothing
            For Each Line As String In Output.Split({vbCrLf, vbLf}, StringSplitOptions.RemoveEmptyEntries)
                Dim Text As String = Line.Trim()
                If Text.StartsWith("v") Then Return Text
            Next
            Return Output.Trim()
        Catch ex As Exception
            Logger.Warn(ex, "读取 Node 版本失败")
            Return Nothing
        End Try
    End Function

    ''' <summary>
    ''' 同步执行一个进程并捕获标准输出。用于短命令（node --version、npm --version 等）。
    ''' 注意：必须在非 UI 线程调用。
    ''' </summary>
    Public Function DshRunAndCapture(FileName As String, Arguments As String, WorkingDirectory As String, TimeoutMs As Integer) As String
        Dim Info As New ProcessStartInfo With {
            .FileName = FileName,
            .Arguments = Arguments,
            .UseShellExecute = False,
            .CreateNoWindow = True,
            .RedirectStandardOutput = True,
            .RedirectStandardError = True
        }
        If Not String.IsNullOrWhiteSpace(WorkingDirectory) Then Info.WorkingDirectory = WorkingDirectory
        Dim Proc As Process = StartProcess(Info)
        Dim Out As String = Proc.StandardOutput.ReadToEnd()
        Proc.StandardError.ReadToEnd()
        If Not Proc.WaitForExit(TimeoutMs) Then
            Try
                Proc.Kill()
            Catch
            End Try
            Throw New Exception($"命令执行超时（{TimeoutMs} ms）：{FileName} {Arguments}")
        End If
        Return Out
    End Function

    ''' <summary>探测 TCP 端口是否已被占用。</summary>
    Public Function DshPortInUse(Port As Integer) As Boolean
        Dim Client As Net.Sockets.TcpClient = Nothing
        Try
            Client = New Net.Sockets.TcpClient()
            Dim Task = Client.ConnectAsync("127.0.0.1", Port)
            Return Task.Wait(500) AndAlso Client.Connected
        Catch
            Return False
        Finally
            If Client IsNot Nothing Then Client.Close()
        End Try
    End Function

    ''' <summary>
    ''' 从 StartPort 开始寻找一个空闲端口（最多尝试 200 个）。
    ''' </summary>
    Public Function DshFindFreePort(StartPort As Integer) As Integer
        For i As Integer = 0 To 199
            Dim Port As Integer = StartPort + i
            If Port > 65535 Then Exit For
            If Not DshPortInUse(Port) Then Return Port
        Next
        Return 0 '交给 dsh 自己分配
    End Function

    ''' <summary>
    ''' 探测端口上的 HTTP 服务状态码，用于判断"占用这个端口的到底是不是 dsh"。
    ''' 实测：dsh 的 GET / 不带 token 返回 401、带 token 返回 303，两种都说明是 dsh；
    ''' 被别的程序占用时会返回别的状态码或直接连不上（返回 0）。
    ''' </summary>
    Public Function DshProbeHttpStatus(Port As Integer) As Integer
        Try
            Dim Req As Net.HttpWebRequest = CType(Net.WebRequest.Create($"http://127.0.0.1:{Port}/"), Net.HttpWebRequest)
            Req.Method = "GET"
            Req.Timeout = 3000
            Req.AllowAutoRedirect = False
            Req.UserAgent = $"PCL2-DSH/{VersionBaseName}"
            Using Resp As Net.HttpWebResponse = CType(Req.GetResponse(), Net.HttpWebResponse)
                Return CInt(Resp.StatusCode)
            End Using
        Catch ex As Net.WebException
            '401 / 403 之类的状态码会以异常形式抛出，从 ex.Response 里取回来
            If ex.Response IsNot Nothing Then
                Try
                    Return CInt(CType(ex.Response, Net.HttpWebResponse).StatusCode)
                Catch
                End Try
            End If
            Return 0
        Catch
            Return 0
        End Try
    End Function

#End Region

#Region "进程环境"

    ''' <summary>
    ''' 为 dsh 子进程准备环境变量。
    ''' 关键点（DEVNOTES §8.2）：必须显式写入 DSH_HOME，不能依赖继承，
    ''' 否则会污染用户正在使用的全局 DSH 环境。
    ''' </summary>
    Public Sub DshApplyEnvironment(Info As ProcessStartInfo, DshHome As String, NodeRoot As String)
        '隔离根目录
        Info.EnvironmentVariables("DSH_HOME") = DshHome

        '把 Node 目录放到 PATH 最前，保证 dsh 与 pnpm 能找到 node
        Dim OldPath As String = Info.EnvironmentVariables("Path")
        If OldPath Is Nothing Then OldPath = Environment.GetEnvironmentVariable("PATH")
        If OldPath Is Nothing Then OldPath = ""
        Dim Parts As New List(Of String) From {NodeRoot}
        Parts.AddRange(OldPath.Split(";"c))
        Info.EnvironmentVariables("Path") = Parts.Where(Function(p) Not String.IsNullOrWhiteSpace(p)).Distinct().Join(";"c)

        '避免 dsh 尝试自己打开浏览器（由启动器统一打开，便于记录 URL 与失败提示）
        Info.EnvironmentVariables("BROWSER") = "none"

        '可选：关闭遥测
        If DshSetting("DshDisableTelemetry", True) Then Info.EnvironmentVariables("DSH_TELEMETRY_DISABLED") = "1"
    End Sub

    ''' <summary>
    ''' 生成一个只含设置项、无副作用的 ProcessStartInfo，用于启动 dsh。
    ''' </summary>
    Public Function DshNewStartInfo(NodeExe As String, Arguments As String, WorkingDirectory As String) As ProcessStartInfo
        Dim Info As New ProcessStartInfo With {
            .FileName = NodeExe,
            .Arguments = Arguments,
            .UseShellExecute = False,
            .CreateNoWindow = True,
            .RedirectStandardOutput = True,
            .RedirectStandardError = True,
            .StandardOutputEncoding = Encoding.UTF8,
            .StandardErrorEncoding = Encoding.UTF8
        }
        Info.WorkingDirectory = WorkingDirectory
        Return Info
    End Function

#End Region

#Region "日志"

    ''' <summary>
    ''' 统一日志出口：同时写入 PCL 日志（带 [DSH] 前缀）与启动器日志窗口。
    ''' 注意：PCL 的 LoaderBase 没有 Log 方法，日志一律走 Logger。
    ''' </summary>
    Public Sub DshLog(Text As String, Optional Loader As LoaderBase = Nothing)
        If String.IsNullOrEmpty(Text) Then Return
        If Loader IsNot Nothing Then Text = $"[{Loader.Name}] {Text}"
        Logger.Info("[DSH] " & Text)
    End Sub

    ''' <summary>日志输出一个异常。</summary>
    Public Sub DshLogError(ex As Exception, Text As String)
        Logger.Error(ex, "[DSH] " & Text, LogBehavior.Toast)
    End Sub

#End Region

#Region "整合包名称校验"

    ''' <summary>校验整合包名称是否合法（不能含路径分隔符与非法字符）。</summary>
    Public Function DshValidateInstanceName(Name As String) As String
        If String.IsNullOrWhiteSpace(Name) Then Return "整合包名称不能为空"
        If Name.Length > 60 Then Return "整合包名称过长（最多 60 个字符）"
        For Each c As Char In Path.GetInvalidFileNameChars()
            If Name.Contains(c.ToString()) Then Return $"整合包名称不能包含字符：{c}"
        Next
        If Name.EndsWith(".") OrElse Name.EndsWith(" ") Then Return "整合包名称不能以点或空格结尾"
        If Name = "." OrElse Name = ".." Then Return "整合包名称不合法"
        Return Nothing
    End Function

    ''' <summary>生成一个不与现有实例冲突的新整合包名称。</summary>
    Public Function DshNewInstanceName() As String
        For i As Integer = 1 To 999
            Dim Candidate As String = "新整合包" & If(i = 1, "", i.ToString())
            If Not DirectoryUtils.Exists(DshInstanceRoot & Candidate) Then Return Candidate
        Next
        Return "新整合包" & GetTimeMs()
    End Function

#End Region

End Module
