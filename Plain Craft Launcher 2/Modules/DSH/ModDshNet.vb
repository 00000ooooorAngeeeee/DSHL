' ============================================================================================
'  ModDshNet —— DSH 版本信息的联网获取
'
'  数据来源（DEVNOTES §3.2 已核实）：
'    · GitHub Releases API：https://api.github.com/repos/deepseek-ai/deepseek-harness/releases
'      → 权威的"发布时间"，tag 形如 dsh-v0.1.7-rc.1。注意：所有 release 都没有附件。
'    · npm registry：https://registry.npmjs.org/@deepseek-ai/dsh
'      → 权威的"可安装版本"，含 time 字段（发布时间）与 dist.tarball。
'
'  策略：以 GitHub 发布时间为准；用 npm 的版本集合补齐 GitHub 没有的版本；
'        GitHub 拿不到时完全回退到 npm 的 time 字段。
' ============================================================================================
Imports Newtonsoft.Json.Linq
Imports System.Text

Public Module ModDshNet

#Region "数据模型"

    ''' <summary>一个可下载的 DSH 版本。</summary>
    Public Class DshVersionInfo
        ''' <summary>版本号，如 0.1.7-rc.1。</summary>
        Public Property Version As String = ""
        ''' <summary>发布时间（本地时区）。</summary>
        Public Property PublishTime As DateTime = DateTime.MinValue
        ''' <summary>发布时间是否已获取到（GitHub/npm 都没给出时为 False）。</summary>
        Public Property HasPublishTime As Boolean = False
        ''' <summary>分组：alpha / rc / stable。</summary>
        Public Property Channel As String = "rc"
        ''' <summary>GitHub tag 名，如 dsh-v0.1.7-rc.1；没有则为空。</summary>
        Public Property TagName As String = ""
        ''' <summary>更新说明（GitHub release body），可能很长；为空表示没有。</summary>
        Public Property ReleaseNotes As String = ""
        ''' <summary>npm tarball 地址；为空表示该版本不在 npm 上，无法用 npm 安装。</summary>
        Public Property TarballUrl As String = ""
        ''' <summary>本地是否已安装该版本。</summary>
        Public Property Installed As Boolean = False

        ''' <summary>用于排序的键。</summary>
        Public ReadOnly Property SortKey As Long
            Get
                Return DshVersionSortKey(Version)
            End Get
        End Property

        ''' <summary>发布时间的展示文本。</summary>
        Public ReadOnly Property PublishTimeText As String
            Get
                If Not HasPublishTime Then Return "发布时间未知"
                Return PublishTime.ToString("yyyy'-'MM'-'dd HH':'mm")
            End Get
        End Property

        Public Overrides Function ToString() As String
            Return Version
        End Function
    End Class

    ''' <summary>版本列表加载结果。</summary>
    Public Class DshVersionListResult
        ''' <summary>全部版本，已按发布时间倒序。</summary>
        Public Property Versions As New List(Of DshVersionInfo)
        ''' <summary>数据来源描述，用于界面提示。</summary>
        Public Property SourceName As String = ""
        ''' <summary>获取过程中出现的非致命警告。</summary>
        Public Property Warnings As New List(Of String)

        Public Function Where(Channel As String) As List(Of DshVersionInfo)
            Return Versions.Where(Function(v) v.Channel = Channel).ToList()
        End Function
    End Class

#End Region

#Region "版本列表加载器"

    ''' <summary>
    ''' 加载 DSH 全部历史版本。输入参数无用，输出 DshVersionListResult。
    ''' </summary>
    Public DshVersionListLoader As New LoaderTask(Of Integer, DshVersionListResult)("DSH Version List", AddressOf DshVersionListLoad) With {.ReloadTimeout = 5 * 60 * 1000}

    Private Sub DshVersionListLoad(Loader As LoaderTask(Of Integer, DshVersionListResult))
        Dim Result As New DshVersionListResult()
        Dim Table As New Dictionary(Of String, DshVersionInfo)(StringComparer.OrdinalIgnoreCase)

        '1. npm（可安装版本 + 发布时间）
        Loader.Progress = 0.1
        Try
            LoadDshVersionsFromNpm(Table, Result)
            Result.SourceName = "npm registry"
        Catch ex As Exception
            Logger.Warn(ex, "从 npm registry 获取 DSH 版本列表失败")
            Result.Warnings.Add("npm registry 获取失败：" & ex.Message)
        End Try
        If Loader.State = LoadState.Canceled Then Return

        '2. GitHub Releases（权威发布时间 + 更新说明）
        Loader.Progress = 0.5
        Try
            LoadDshVersionsFromGithub(Table, Result)
            Result.SourceName = If(Result.SourceName = "", "GitHub Releases", Result.SourceName & " + GitHub Releases")
        Catch ex As Exception
            Logger.Warn(ex, "从 GitHub 获取 DSH 版本列表失败")
            Result.Warnings.Add("GitHub Releases 获取失败（已使用 npm 时间）：" & ex.Message)
        End Try
        If Loader.State = LoadState.Canceled Then Return

        '3. 汇总
        Loader.Progress = 0.9
        If Table.Count = 0 Then Throw New Exception("未能获取到任何 DSH 版本，请检查网络连接")

        '标记已安装
        Dim Installed As List(Of String) = DshInstalledVersions()
        For Each Info As DshVersionInfo In Table.Values
            Info.Installed = Installed.Contains(Info.Version)
        Next

        Result.Versions = Table.Values.
            OrderByDescending(Function(v) v.PublishTime).
            ThenByDescending(Function(v) v.SortKey).
            ToList()
        Loader.Output = Result
        Loader.Progress = 1
        Logger.Info($"DSH 版本列表加载完成：{Result.Versions.Count} 个版本（来源：{Result.SourceName}）")
    End Sub

    ''' <summary>刷新版本列表。</summary>
    Public Sub DshRefreshVersionList()
        DshVersionListLoader.Start(0, IsForceRestart:=True)
    End Sub

#End Region

#Region "npm 数据源"

    ''' <summary>npm registry 地址（按设置选择官方或镜像）。</summary>
    Public ReadOnly Property DshNpmRegistryActive As String
        Get
            If DshSetting("DshNpmSource", 0) = 1 Then Return DshNpmRegistryMirror
            Return DshNpmRegistry
        End Get
    End Property

    Private Sub LoadDshVersionsFromNpm(Table As Dictionary(Of String, DshVersionInfo), Result As DshVersionListResult)
        Dim Url As String = DshNpmRegistryActive & DshPackageName.Replace("/", "%2F")
        Dim Text As String = NetRequestByClientRetry(Url, RequireJson:=True)
        Dim Root As JObject = JObject.Parse(Text)

        '发布时间表
        Dim TimeNode As JObject = TryCast(Root("time"), JObject)
        Dim VersionsNode As JObject = TryCast(Root("versions"), JObject)
        If VersionsNode Is Nothing Then Throw New Exception("npm 返回内容缺少 versions 字段")

        For Each Prop As JProperty In VersionsNode.Properties()
            Dim Version As String = Prop.Name
            If Version = "created" OrElse Version = "modified" Then Continue For
            Dim Info As DshVersionInfo = Nothing
            If Not Table.TryGetValue(Version, Info) Then
                Info = New DshVersionInfo With {.Version = Version, .Channel = DshVersionChannel(Version)}
                Table(Version) = Info
            End If
            '时间
            If TimeNode IsNot Nothing AndAlso TimeNode(Version) IsNot Nothing Then
                Dim Raw As String = TimeNode(Version).ToString()
                Dim Parsed As DateTime
                If DateTime.TryParse(Raw, Globalization.CultureInfo.InvariantCulture,
                                     Globalization.DateTimeStyles.AdjustToUniversal Or Globalization.DateTimeStyles.AssumeUniversal, Parsed) Then
                    Info.PublishTime = Parsed.ToLocalTime()
                    Info.HasPublishTime = True
                End If
            End If
            'tarball
            Dim Dist As JObject = TryCast(Prop.Value("dist"), JObject)
            If Dist IsNot Nothing AndAlso Dist("tarball") IsNot Nothing Then
                Info.TarballUrl = Dist("tarball").ToString()
            End If
        Next
    End Sub

#End Region

#Region "GitHub 数据源"

    Private Sub LoadDshVersionsFromGithub(Table As Dictionary(Of String, DshVersionInfo), Result As DshVersionListResult)
        Dim Url As String = $"https://api.github.com/repos/{DshGithubRepo}/releases?per_page=100"
        Dim Headers(,) As String = {{"Accept", "application/vnd.github+json"}, {"User-Agent", $"PCL2-DSH/{VersionBaseName}"}}
        Dim Text As String = NetRequestByClientRetry(Url, Headers:=Headers, RequireJson:=True)
        Dim Array As JArray = JArray.Parse(Text)
        If Array.Count = 0 Then Throw New Exception("GitHub 未返回任何 release")

        For Each Item As JToken In Array
            Dim TagName As String = If(Item("tag_name") Is Nothing, "", Item("tag_name").ToString())
            If String.IsNullOrWhiteSpace(TagName) Then Continue For
            'tag 形如 dsh-v0.1.7-rc.1，剥掉前缀
            Dim Version As String = TagName
            If Version.StartsWith("dsh-v", StringComparison.OrdinalIgnoreCase) Then Version = Version.Substring(5)
            If Version.StartsWith("v", StringComparison.OrdinalIgnoreCase) Then Version = Version.Substring(1)
            If Version = "" Then Continue For

            Dim Info As DshVersionInfo = Nothing
            If Not Table.TryGetValue(Version, Info) Then
                Info = New DshVersionInfo With {.Version = Version, .Channel = DshVersionChannel(Version)}
                Table(Version) = Info
            End If
            Info.TagName = TagName

            '发布时间（GitHub 的 published_at 是权威发布时刻）
            Dim TimeRaw As String = ""
            If Item("published_at") IsNot Nothing AndAlso Item("published_at").Type <> JTokenType.Null Then
                TimeRaw = Item("published_at").ToString()
            ElseIf Item("created_at") IsNot Nothing AndAlso Item("created_at").Type <> JTokenType.Null Then
                TimeRaw = Item("created_at").ToString()
            End If
            If TimeRaw <> "" Then
                Dim Parsed As DateTime
                If DateTime.TryParse(TimeRaw, Globalization.CultureInfo.InvariantCulture,
                                     Globalization.DateTimeStyles.AdjustToUniversal Or Globalization.DateTimeStyles.AssumeUniversal, Parsed) Then
                    Info.PublishTime = Parsed.ToLocalTime()
                    Info.HasPublishTime = True
                End If
            End If

            '更新说明
            If Item("body") IsNot Nothing AndAlso Item("body").Type <> JTokenType.Null Then
                Info.ReleaseNotes = Item("body").ToString()
            End If
        Next
    End Sub

#End Region

#Region "版本详情"

    ''' <summary>
    ''' 生成版本卡片的显示文本（用于列表项副标题）。
    ''' </summary>
    Public Function DshVersionDisplayText(Info As DshVersionInfo) As String
        Dim Parts As New List(Of String)
        Parts.Add(Info.PublishTimeText)
        Select Case Info.Channel
            Case "alpha" : Parts.Add("Alpha 测试版")
            Case "rc" : Parts.Add("RC 候选版")
            Case Else : Parts.Add("正式版")
        End Select
        If Info.Installed Then Parts.Add("已安装")
        If Info.TarballUrl = "" Then Parts.Add("npm 无此版本")
        Return Parts.Join("　·　")
    End Function

#End Region

End Module
