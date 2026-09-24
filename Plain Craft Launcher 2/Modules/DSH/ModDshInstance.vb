' ============================================================================================
'  ModDshInstance —— 整合包（DSH 实例）的数据模型与扫描
'
'  本模块把"整合包"这个概念落地：
'    · 一个整合包 = DSH\instances\<名称>\ 目录
'    · 它的 DSH_HOME 就在 DSH\instances\<名称>\DSH_HOME\  → 技能/插件/配置/会话全部隔离
'    · 它绑定的 dsh 版本号写在 instance.json 里  → 版本也隔离
'    · 它自己的设置（端口、工作区…）写在 DSH\instances\<名称>\PCL\Setup.ini
'
'  约定见 DEVNOTES.md §2、§8。
' ============================================================================================
Public Module ModDshInstance

#Region "数据模型"

    ''' <summary>整合包（DSH 实例）的清单，序列化为 instance.json。</summary>
    Public Class DshInstanceManifest
        ''' <summary>清单格式版本，便于以后升级。</summary>
        Public Property ManifestVersion As Integer = 1
        ''' <summary>显示的整合包名称。</summary>
        Public Property Name As String = ""
        ''' <summary>该整合包绑定的 dsh 版本号，例如 0.1.7-rc.1。</summary>
        Public Property DshVersion As String = ""
        ''' <summary>DSH 使用的 profile 名，默认 web。</summary>
        Public Property Profile As String = "web"
        ''' <summary>监听端口；0 表示自动分配。</summary>
        Public Property Port As Integer = DshDefaultPort
        ''' <summary>dsh 的工作目录（workspace）。为空表示使用整合包目录下的 workspace\。</summary>
        Public Property Workspace As String = ""
        ''' <summary>创建时间。</summary>
        Public Property CreateTime As String = ""
        ''' <summary>备注。</summary>
        Public Property Comment As String = ""
        ''' <summary>是否启用"启动后自动打开浏览器"。</summary>
        Public Property AutoOpenBrowser As Boolean = True
        ''' <summary>启动时附加的自定义环境变量（备用）。</summary>
        Public Property ExtraEnv As New Dictionary(Of String, String)
    End Class

    ''' <summary>一个整合包实例。</summary>
    Public Class DshInstance
        ''' <summary>整合包名称（等于目录名）。</summary>
        Public Property Name As String = ""
        ''' <summary>整合包目录，以 \ 结尾。</summary>
        Public Property PathInstance As String = ""
        ''' <summary>该整合包的 DSH_HOME，以 \ 结尾。</summary>
        Public Property PathDshHome As String = ""
        ''' <summary>该整合包的 PCL 设置文件（版本独立设置存这里）。</summary>
        Public Property PathSetupIni As String = ""
        ''' <summary>instance.json 路径。</summary>
        Public Property PathManifest As String = ""
        ''' <summary>该整合包绑定的 dsh 版本号。</summary>
        Public Property DshVersion As String = ""
        ''' <summary>profile 名。</summary>
        Public Property Profile As String = "web"
        ''' <summary>监听端口。</summary>
        Public Property Port As Integer = DshDefaultPort
        ''' <summary>dsh 工作目录，以 \ 结尾。</summary>
        Public Property Workspace As String = ""
        ''' <summary>启动后是否自动打开浏览器。</summary>
        Public Property AutoOpenBrowser As Boolean = True
        ''' <summary>备注。</summary>
        Public Property Comment As String = ""
        ''' <summary>创建时间。</summary>
        Public Property CreateTime As String = ""
        ''' <summary>读取清单时的错误信息；为空表示正常。</summary>
        Public Property ErrorMessage As String = ""

        ''' <summary>该整合包绑定的 dsh 是否已安装。</summary>
        Public ReadOnly Property IsVersionInstalled As Boolean
            Get
                Return DshVersionInstalled(DshVersion)
            End Get
        End Property

        ''' <summary>该整合包是否已创建好 DSH_HOME。</summary>
        Public ReadOnly Property IsHomeReady As Boolean
            Get
                Return DirectoryUtils.Exists(PathDshHome)
            End Get
        End Property

        ''' <summary>
        ''' 实际是否要在启动后自动打开浏览器。
        ''' 实例清单里显式关闭过就用实例的，否则跟随全局设置。
        ''' </summary>
        Public ReadOnly Property EffectiveAutoOpenBrowser As Boolean
            Get
                If Not AutoOpenBrowser Then Return False
                Return DshSetting("DshAutoOpenBrowser", True)
            End Get
        End Property

        ''' <summary>该整合包能否启动（版本已装 + 工作区可写）。</summary>
        Public ReadOnly Property CanLaunch As Boolean
            Get
                Return IsVersionInstalled AndAlso String.IsNullOrEmpty(ErrorMessage)
            End Get
        End Property

        Public Overrides Function ToString() As String
            Return Name
        End Function

        Public Overrides Function Equals(obj As Object) As Boolean
            If Not (TypeOf obj Is DshInstance) Then Return False
            Return DirectCast(obj, DshInstance).PathInstance = PathInstance
        End Function
        Public Overrides Function GetHashCode() As Integer
            Return If(PathInstance, "").GetHashCode()
        End Function
    End Class

    ''' <summary>
    ''' 当前选中的整合包。为 Nothing 表示尚未选择/没有整合包。
    ''' </summary>
    Public Property DshInstanceSelected As DshInstance
        Get
            Return _DshInstanceSelected
        End Get
        Set(Value As DshInstance)
            If _DshInstanceSelected IsNot Nothing AndAlso Value IsNot Nothing AndAlso _DshInstanceSelected.PathInstance = Value.PathInstance Then Return
            _DshInstanceSelected = Value
            If Value Is Nothing Then
                DshSetSetting("DshInstanceSelected", "")
                Logger.Info("当前选择的整合包：无")
            Else
                DshSetSetting("DshInstanceSelected", Value.Name)
                Logger.Info($"当前选择的整合包：{Value.Name}（dsh {Value.DshVersion}）")
            End If
        End Set
    End Property
    Private _DshInstanceSelected As DshInstance

    ''' <summary>已扫描到的整合包列表。</summary>
    Public DshInstanceList As New List(Of DshInstance)

#End Region

#Region "路径计算"

    ''' <summary>某个整合包的目录，以 \ 结尾。</summary>
    Public Function DshInstancePath(Name As String) As String
        Return PathUtils.AddSlashSuffix(DshInstanceRoot & Name)
    End Function

    ''' <summary>
    ''' 由名称构造一个尚未落盘的整合包对象（所有路径都算好）。
    ''' </summary>
    Public Function DshBuildInstance(Name As String) As DshInstance
        Dim PathDsh As String = DshInstancePath(Name)
        Dim Result As New DshInstance With {
            .Name = Name,
            .PathInstance = PathDsh,
            .PathDshHome = PathDsh & "DSH_HOME\",
            .PathSetupIni = PathDsh & "PCL\Setup.ini",
            .PathManifest = PathDsh & "instance.json",
            .DshVersion = "",
            .Profile = "web",
            .Port = DshDefaultPort,
            .Workspace = PathDsh & "workspace\",
            .AutoOpenBrowser = True,
            .CreateTime = Now.ToString("yyyy'-'MM'-'dd HH':'mm':'ss")
        }
        Return Result
    End Function

#End Region

#Region "读 / 写 instance.json"

    ''' <summary>读取一个整合包的 instance.json；不存在或损坏时返回带 ErrorMessage 的对象。</summary>
    Public Function DshReadManifest(Instance As DshInstance) As DshInstance
        If Not FileUtils.Exists(Instance.PathManifest) Then
            Instance.ErrorMessage = "缺少 instance.json"
            Return Instance
        End If
        Try
            Dim Manifest As DshInstanceManifest = FileUtils.ReadAsJson(Instance.PathManifest).ToObject(Of DshInstanceManifest)()
            If Manifest Is Nothing Then Throw New Exception("清单内容为空")
            Instance.DshVersion = If(Manifest.DshVersion, "")
            Instance.Profile = If(String.IsNullOrWhiteSpace(Manifest.Profile), "web", Manifest.Profile)
            Instance.Port = If(Manifest.Port <= 0, DshDefaultPort, Manifest.Port)
            Instance.Workspace = If(String.IsNullOrWhiteSpace(Manifest.Workspace), Instance.PathInstance & "workspace\", Manifest.Workspace.Replace("$", Paths.Base))
            Instance.Workspace = PathUtils.AddSlashSuffix(Instance.Workspace)
            Instance.AutoOpenBrowser = Manifest.AutoOpenBrowser
            Instance.Comment = If(Manifest.Comment, "")
            Instance.CreateTime = If(Manifest.CreateTime, "")
            Instance.ErrorMessage = ""
        Catch ex As Exception
            Instance.ErrorMessage = "instance.json 解析失败：" & ex.Message
            Logger.Warn(ex, $"读取整合包清单失败：{Instance.PathManifest}")
        End Try
        Return Instance
    End Function

    ''' <summary>把整合包对象写回 instance.json。</summary>
    Public Sub DshWriteManifest(Instance As DshInstance)
        Try
            DirectoryUtils.Create(Instance.PathInstance)
            Dim Manifest As New DshInstanceManifest With {
                .ManifestVersion = 1,
                .Name = Instance.Name,
                .DshVersion = Instance.DshVersion,
                .Profile = Instance.Profile,
                .Port = Instance.Port,
                .Workspace = Instance.Workspace.Replace(Paths.Base, "$"),
                .AutoOpenBrowser = Instance.AutoOpenBrowser,
                .Comment = Instance.Comment,
                .CreateTime = If(Instance.CreateTime, Now.ToString("yyyy'-'MM'-'dd HH':'mm':'ss"))
            }
            FileUtils.Write(Instance.PathManifest, JsonConvert.SerializeObject(Manifest, Formatting.Indented), New UTF8Encoding(False))
            Instance.ErrorMessage = ""
        Catch ex As Exception
            Logger.Error(ex, $"写入整合包清单失败：{Instance.PathManifest}", LogBehavior.Toast)
            Throw
        End Try
    End Sub

#End Region

#Region "创建 / 删除 / 导入"

    ''' <summary>
    ''' 创建一个新的整合包目录结构。
    ''' 不联网、不安装 dsh，只建立目录与清单；版本安装由 ModDshInstall 负责。
    ''' </summary>
    ''' <param name="Name">整合包名称。</param>
    ''' <param name="DshVersion">要绑定的 dsh 版本；为空则用默认版本。</param>
    ''' <param name="Workspace">工作区目录；为空则用实例目录下的 workspace\。</param>
    Public Function DshCreateInstance(Name As String, Optional DshVersion As String = "", Optional Workspace As String = "") As DshInstance
        Dim Check As String = DshValidateInstanceName(Name)
        If Check IsNot Nothing Then Throw New Exception(Check)
        If DirectoryUtils.Exists(DshInstancePath(Name)) Then Throw New Exception($"整合包已存在：{Name}")

        Dim Instance As DshInstance = DshBuildInstance(Name)
        Instance.DshVersion = If(String.IsNullOrWhiteSpace(DshVersion), DshDefaultVersion, DshVersion)
        If Not String.IsNullOrWhiteSpace(Workspace) Then Instance.Workspace = PathUtils.AddSlashSuffix(Workspace)
        Instance.Port = DshFindFreePort(DshDefaultPort)

        '目录结构
        DirectoryUtils.Create(Instance.PathInstance)
        DirectoryUtils.Create(Instance.PathInstance & "PCL\")
        DirectoryUtils.Create(Instance.PathDshHome)
        DirectoryUtils.Create(Instance.PathDshHome & "skills\")
        DirectoryUtils.Create(Instance.PathDshHome & "profiles\")
        DirectoryUtils.Create(Instance.PathDshHome & "sessions\")
        DirectoryUtils.Create(Instance.PathDshHome & "storages\")
        DirectoryUtils.Create(Instance.PathDshHome & "logs\")
        DirectoryUtils.Create(Instance.PathDshHome & "skills\.disabled\")
        DirectoryUtils.Create(Instance.Workspace)
        DirectoryUtils.Create(Instance.PathInstance & "plugins\")
        DirectoryUtils.Create(Instance.PathInstance & "plugin-repo\")

        DshWriteManifest(Instance)

        '把用户级 DSH_HOME 环境变量在该实例的 Setup.ini 里记录下来，便于诊断
        WriteIni(Instance.PathSetupIni, "DshVersion", Instance.DshVersion)
        WriteIni(Instance.PathSetupIni, "DshPort", Instance.Port.ToString())

        Logger.Info($"已创建整合包：{Name}（dsh {Instance.DshVersion}，端口 {Instance.Port}）")
        Return Instance
    End Function

    ''' <summary>
    ''' 把现有 DSH_HOME 的技能与配置导入到目标整合包。仅复制，不删除源。
    ''' </summary>
    ''' <param name="Instance">目标整合包。</param>
    ''' <param name="SourceDshHome">源 DSH_HOME，以 \ 结尾。</param>
    ''' <param name="ImportSkills">是否导入 skills。</param>
    ''' <param name="ImportProfile">是否导入 profiles（含插件与配置）。</param>
    ''' <param name="ImportCredentials">是否导入 .credentials.yaml 与 .anonymous-user-id。</param>
    Public Sub DshImportFromHome(Instance As DshInstance, SourceDshHome As String, Optional ImportSkills As Boolean = True,
                                 Optional ImportProfile As Boolean = True, Optional ImportCredentials As Boolean = True)
        SourceDshHome = PathUtils.AddSlashSuffix(SourceDshHome.Replace("$", Paths.Base))
        If Not DirectoryUtils.Exists(SourceDshHome) Then Throw New Exception($"要导入的 DSH_HOME 不存在：{SourceDshHome}")
        If SourceDshHome = Instance.PathDshHome Then Throw New Exception("源目录与目标目录相同")

        DirectoryUtils.Create(Instance.PathDshHome)

        '1. 技能
        If ImportSkills AndAlso DirectoryUtils.Exists(SourceDshHome & "skills\") Then
            Dim Count As Integer = 0
            For Each Dir As String In DirectoryUtils.EnumerateDirectories(SourceDshHome & "skills\")
                Dim SkillName As String = PathUtils.GetLastPart(Dir)
                If SkillName.StartsWith(".") Then Continue For '跳过 .disabled 等隐藏目录
                DirectoryUtils.Copy(Dir, Instance.PathDshHome & "skills\" & SkillName)
                Count += 1
            Next
            '平铺的 .md 技能
            For Each File As String In DirectoryUtils.EnumerateFiles(SourceDshHome & "skills\", searchPattern:="*.md")
                DirectoryUtils.Copy(File, Instance.PathDshHome & "skills\" & PathUtils.GetLastPart(File))
                Count += 1
            Next
            Logger.Info($"已导入 {Count} 个技能到整合包 {Instance.Name}")
        End If

        '2. profile（含插件 node_modules 与 cordis 配置）
        If ImportProfile AndAlso DirectoryUtils.Exists(SourceDshHome & "profiles\") Then
            For Each Dir As String In DirectoryUtils.EnumerateDirectories(SourceDshHome & "profiles\")
                Dim ProfileName As String = PathUtils.GetLastPart(Dir)
                If ProfileName.StartsWith(".") Then Continue For
                DirectoryUtils.Copy(Dir, Instance.PathDshHome & "profiles\" & ProfileName)
            Next
            Logger.Info($"已导入 profile 到整合包 {Instance.Name}")
        End If

        '3. 凭证（可选）
        If ImportCredentials Then
            For Each FileName As String In {"\.credentials.yaml", "\.anonymous-user-id"}
                If FileUtils.Exists(SourceDshHome & FileName) Then
                    FileUtils.Copy(SourceDshHome & FileName, Instance.PathDshHome & FileName)
                End If
            Next
            Logger.Info($"已导入凭证文件到整合包 {Instance.Name}")
        End If

        '4. 顺带把 workspace 注册表也带上，避免历史会话丢失
        If FileUtils.Exists(SourceDshHome & "storages\workspace.json") Then
            DirectoryUtils.Create(Instance.PathDshHome & "storages\")
            FileUtils.Copy(SourceDshHome & "storages\workspace.json", Instance.PathDshHome & "storages\workspace.json")
        End If
    End Sub

    ''' <summary>删除整合包目录（移动到回收站）。</summary>
    Public Sub DshDeleteInstance(Instance As DshInstance)
        If Instance Is Nothing Then Throw New Exception("没有指定要删除的整合包")
        If Not DirectoryUtils.Exists(Instance.PathInstance) Then Throw New Exception($"整合包目录不存在：{Instance.PathInstance}")
        DirectoryUtils.Delete(Instance.PathInstance, toRecycleBin:=True)
        Logger.Info($"已删除整合包：{Instance.Name}")
    End Sub

#End Region

#Region "扫描与选择"

    ''' <summary>
    ''' 整合包列表扫描加载器。
    ''' 扫描 DSH\instances\ 下的每个子目录，读取 instance.json。
    ''' </summary>
    Public DshInstanceListLoader As New LoaderTask(Of Integer, List(Of DshInstance))("DSH Instance List", AddressOf DshInstanceListLoad)

    Private Sub DshInstanceListLoad(Loader As LoaderTask(Of Integer, List(Of DshInstance)))
        Dim Result As New List(Of DshInstance)
        Try
            Loader.Progress = 0.1
            DirectoryUtils.Create(DshRoot)
            DirectoryUtils.Create(DshInstanceRoot)

            Loader.Progress = 0.3
            For Each Dir As String In DirectoryUtils.EnumerateDirectories(DshInstanceRoot)
                If Loader.State = LoadState.Canceled Then Return
                Dim InstanceName As String = PathUtils.GetLastPart(Dir)
                If InstanceName.StartsWith(".") OrElse InstanceName.StartsWith("_") Then Continue For

                Dim Instance As DshInstance = DshBuildInstance(InstanceName)
                '既没有清单也没有 DSH_HOME 的目录不算整合包
                If Not FileUtils.Exists(Instance.PathManifest) AndAlso Not DirectoryUtils.Exists(Instance.PathDshHome) Then Continue For
                DshReadManifest(Instance)
                Result.Add(Instance)
            Next

            Loader.Progress = 0.9
            Result = Result.OrderBy(Function(i) i.Name, StringComparer.CurrentCulture).ToList()
            Loader.Output = Result

            '更新当前选择
            Dim SelectedName As String = DshSetting("DshInstanceSelected", "")
            Dim Selected As DshInstance = Nothing
            If Not String.IsNullOrWhiteSpace(SelectedName) Then
                Selected = Result.FirstOrDefault(Function(i) i.Name = SelectedName)
            End If
            If Selected Is Nothing AndAlso Result.Any() Then Selected = Result(0)
            DshInstanceList = Result
            RunInUi(Sub() DshInstanceSelected = Selected)

            Loader.Progress = 1
            Logger.Info($"整合包列表加载完成，共 {Result.Count} 个")
        Catch ex As Exception
            If Loader.State = LoadState.Canceled OrElse ex.IsCanceled Then Return
            Loader.Output = Result
            Logger.Error(ex, "加载整合包列表失败")
        End Try
    End Sub

    ''' <summary>要求下次加载时强制刷新整合包列表。</summary>
    Public Sub DshRefreshInstanceList()
        If DshInstanceListLoader.State = LoadState.Loading Then Return
        DshInstanceListLoader.Start(0, IsForceRestart:=True)
    End Sub

#End Region

End Module
