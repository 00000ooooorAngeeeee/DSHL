' ============================================================================================
'  ModDshHome —— 整合包 DSH_HOME 的内部管理：技能、插件、profile
'
'  已核实的事实（DEVNOTES §3.4）：
'    · $DSH_HOME/skills/ 是用户级技能根，只扫描"一层"：
'        - 目录 bundle：<skills>\<name>\SKILL.md
'        - 平铺文件：  <skills>\<name>.md
'      技能名必须匹配 /^[a-z0-9]+(?:-[a-z0-9]+)*$/（小写字母数字加连字符），
'      所以把目录名改成 .disabled~xxx 或把 SKILL.md 改名，都能让 dsh 彻底看不见它。
'      → 这就是"技能开关"的实现方式，纯文件操作，不需要改配置、不需要重启。
'    · 插件按 profile 隔离：$DSH_HOME/profiles/<profile>/package.json 的 dependencies +
'      该目录下的 node_modules；用 `dsh plugin --profile <name> add/remove <pkg>` 管理（内部走 pnpm）。
'      → "插件开关"通过 profile 的 cordis.patch.yml 做 id 定向的 disable 来实现。
' ============================================================================================
Imports Newtonsoft.Json.Linq
Imports System.Text

Public Module ModDshHome

#Region "profile 初始化"

    ''' <summary>
    ''' 确保整合包的 profile 目录存在。
    ''' 优先让 dsh 自己的 --from-default-profile 生成官方模板（最不容易出错），
    ''' 失败时回退到写一份最小可用模板。
    ''' </summary>
    Public Sub DshEnsureProfile(Loader As LoaderBase, Instance As DshInstance)
        Dim ProfileDir As String = Instance.PathDshHome & "profiles\" & Instance.Profile & "\"
        If FileUtils.Exists(ProfileDir & "package.json") AndAlso FileUtils.Exists(ProfileDir & "cordis.yml") Then
            DshLog($"profile {Instance.Profile} 已存在", Loader)
            Return
        End If
        DirectoryUtils.Create(ProfileDir)

        '尝试用 dsh 自己初始化（需要该版本已安装）
        '实测结论（2026-09-24 验证）：
        '   web / headless 等是「官方内置 profile」，不能用 --from-default-profile 作为自定义 profile 目标
        '   （会报 "profile web is shipped and cannot be a custom profile target"）。
        '   正确做法是直接 boot 它，dsh 会在首次使用时从内置模板自动初始化出
        '   package.json / cordis.yml / cordis.patch.yml / pnpm-workspace.yaml。
        '   这里用 --dump-config：只组装配置并打印，不真正启动 web 服务，也不占端口。
        If DshVersionInstalled(Instance.DshVersion) AndAlso DshNodeExe IsNot Nothing Then
            Try
                DshLog($"正在用 dsh 初始化 profile：{Instance.Profile}", Loader)
                Dim Args As String = $"""{DshBinJs(Instance.DshVersion)}"" --profile {Instance.Profile} --dump-config"
                Dim Info As ProcessStartInfo = DshNewStartInfo(DshNodeExe, Args, Instance.PathInstance)
                DshApplyEnvironment(Info, Instance.PathDshHome, DshRuntimeRootEffective)
                '注意：dsh --dump-config 实测退出码为 1（输出正常），所以不能依赖退出码，
                '直接看 profile 文件是否被生成即可；DshRunInfo 对非零退出码只是记录警告。
                DshRunInfo(Info, 120000)
            Catch ex As Exception
                Logger.Warn(ex, "用 dsh 初始化 profile 失败，将使用内置模板")
            End Try
        End If

        '回退模板
        If Not FileUtils.Exists(ProfileDir & "package.json") Then
            DshLog("写入内置的 profile 模板", Loader)
            FileUtils.Write(ProfileDir & "package.json",
                "{""name"":""dsh-profile-" & Instance.Profile & """,""private"":true,""dependencies"":{}," &
                """dsh"":{""profile"":{""bundles"":[""@deepseek-ai/dsh-base"",""@deepseek-ai/dsh-web-app""]}}}" & vbCrLf,
                New UTF8Encoding(False))
        End If
        If Not FileUtils.Exists(ProfileDir & "cordis.yml") Then
            FileUtils.Write(ProfileDir & "cordis.yml",
                "# dsh profile root — an empty entry list. The tree is composed as patches." & vbCrLf & "[]" & vbCrLf,
                New UTF8Encoding(False))
        End If
        If Not FileUtils.Exists(ProfileDir & "cordis.patch.yml") Then
            FileUtils.Write(ProfileDir & "cordis.patch.yml", "" & vbCrLf, New UTF8Encoding(False))
        End If
        If Not FileUtils.Exists(ProfileDir & "pnpm-workspace.yaml") Then
            FileUtils.Write(ProfileDir & "pnpm-workspace.yaml",
                "packages:" & vbCrLf & "  - ." & vbCrLf & vbCrLf & "nodeLinker: hoisted" & vbCrLf & "autoInstallPeers: false" & vbCrLf,
                New UTF8Encoding(False))
        End If
        DirectoryUtils.Create(ProfileDir & "node_modules\")
        DshLog("profile 初始化完成", Loader)
    End Sub

#End Region

#Region "技能：扫描 / 开关"

    ''' <summary>一个技能。</summary>
    Public Class DshSkill
        ''' <summary>技能目录或文件的名字（含 .disabled~ 前缀，原样保留）。</summary>
        Public Property FolderName As String = ""
        ''' <summary>技能名（frontmatter 里的 name；解析失败时用目录名）。</summary>
        Public Property Name As String = ""
        ''' <summary>描述。</summary>
        Public Property Description As String = ""
        ''' <summary>何时使用（可选）。</summary>
        Public Property WhenToUse As String = ""
        ''' <summary>完整路径（目录或 md 文件）。</summary>
        Public Property Path As String = ""
        ''' <summary>是否为目录 bundle（False 表示平铺 .md 文件）。</summary>
        Public Property IsBundle As Boolean = True
        ''' <summary>是否启用。</summary>
        Public Property Enabled As Boolean = True
        ''' <summary>解析警告。</summary>
        Public Property Warning As String = ""

        Public Overrides Function ToString() As String
            Return Name
        End Function
    End Class

    Private Const DshSkillDisabledPrefix As String = ".disabled~"

    ''' <summary>技能根目录。</summary>
    Public Function DshSkillRoot(Instance As DshInstance) As String
        Return Instance.PathDshHome & "skills\"
    End Function

    ''' <summary>扫描整合包的全部技能（含被关闭的）。</summary>
    Public Function DshScanSkills(Instance As DshInstance) As List(Of DshSkill)
        Dim Result As New List(Of DshSkill)
        Dim Root As String = DshSkillRoot(Instance)
        If Not DirectoryUtils.Exists(Root) Then Return Result
        Try
            '目录 bundle
            For Each Dir As String In DirectoryUtils.EnumerateDirectories(Root)
                Dim FolderName As String = PathUtils.GetLastPart(Dir)
                If FolderName = ".system" OrElse FolderName.StartsWith(".") AndAlso Not FolderName.StartsWith(DshSkillDisabledPrefix) Then Continue For
                Dim Skill As New DshSkill With {
                    .FolderName = FolderName,
                    .Path = Dir,
                    .IsBundle = True,
                    .Enabled = Not FolderName.StartsWith(DshSkillDisabledPrefix)
                }
                Dim SkillFile As String = PathUtils.AddSlashSuffix(Dir) & "SKILL.md"
                If FileUtils.Exists(SkillFile) Then
                    DshParseSkillFrontmatter(FileUtils.ReadAsString(SkillFile), Skill)
                Else
                    '有些技能 bundle 可能整体被改名（SKILL.md.disabled），也算被关闭
                    Dim DisabledFile As String = SkillFile & DshSkillDisabledPrefix
                    If FileUtils.Exists(DisabledFile) Then
                        Skill.Enabled = False
                        DshParseSkillFrontmatter(FileUtils.ReadAsString(DisabledFile), Skill)
                    Else
                        Skill.Name = FolderName
                        Skill.Warning = "未找到 SKILL.md"
                    End If
                End If
                If String.IsNullOrWhiteSpace(Skill.Name) Then Skill.Name = FolderName.Replace(DshSkillDisabledPrefix, "")
                Result.Add(Skill)
            Next

            '平铺的 .md 技能
            For Each File As String In DirectoryUtils.EnumerateFiles(Root, searchPattern:="*.md")
                Dim FileName As String = PathUtils.GetLastPart(File)
                Dim Skill As New DshSkill With {
                    .FolderName = FileName,
                    .Path = File,
                    .IsBundle = False,
                    .Enabled = True
                }
                DshParseSkillFrontmatter(FileUtils.ReadAsString(File), Skill)
                If String.IsNullOrWhiteSpace(Skill.Name) Then Skill.Name = PathUtils.GetFileNameWithoutExtension(FileName)
                Result.Add(Skill)
            Next
            '被关闭的平铺技能（xxx.md.disabled~）
            For Each File As String In DirectoryUtils.EnumerateFiles(Root, searchPattern:="*.md" & DshSkillDisabledPrefix)
                Dim FileName As String = PathUtils.GetLastPart(File)
                Dim Skill As New DshSkill With {
                    .FolderName = FileName.Replace(".md" & DshSkillDisabledPrefix, ""),
                    .Path = File,
                    .IsBundle = False,
                    .Enabled = False
                }
                DshParseSkillFrontmatter(FileUtils.ReadAsString(File), Skill)
                If String.IsNullOrWhiteSpace(Skill.Name) Then Skill.Name = PathUtils.GetFileNameWithoutExtension(FileName.Replace(DshSkillDisabledPrefix, ""))
                Result.Add(Skill)
            Next
        Catch ex As Exception
            Logger.Warn(ex, $"扫描技能失败：{Instance.Name}")
        End Try
        Return Result.OrderBy(Function(s) s.Name, StringComparer.CurrentCulture).ToList()
    End Function

    ''' <summary>极简 frontmatter 解析：只取 name / description / whenToUse。</summary>
    Private Sub DshParseSkillFrontmatter(Content As String, Skill As DshSkill)
        Try
            If String.IsNullOrWhiteSpace(Content) Then Return
            Dim Text As String = Content.Replace(vbCrLf, vbLf)
            If Not Text.StartsWith("---") Then
                Skill.Warning = "缺少 YAML frontmatter"
                Return
            End If
            Dim EndIndex As Integer = Text.IndexOf(vbLf & "---", 3)
            If EndIndex < 0 Then
                Skill.Warning = "frontmatter 没有结束标记"
                Return
            End If
            Dim Front As String = Text.Substring(3, EndIndex - 3)
            For Each Line As String In Front.Split(vbLf)
                Dim Trimmed As String = Line.Trim()
                If Trimmed.StartsWith("name:") Then Skill.Name = Trimmed.Substring(5).Trim().Trim(""""c, "'"c)
                If Trimmed.StartsWith("description:") Then Skill.Description = Trimmed.Substring(12).Trim().Trim(""""c, "'"c)
                If Trimmed.StartsWith("whenToUse:") Then Skill.WhenToUse = Trimmed.Substring(10).Trim().Trim(""""c, "'"c)
            Next
            If String.IsNullOrWhiteSpace(Skill.Name) Then Skill.Warning = "frontmatter 缺少 name"
        Catch ex As Exception
            Skill.Warning = "解析失败：" & ex.Message
        End Try
    End Sub

    ''' <summary>
    ''' 启用/关闭一个技能。实现方式：改名（目录加/去 .disabled~ 前缀，或把 SKILL.md 改名）。
    ''' 这是 dsh 官方扫描器天然忽略的形态，不需要改配置。
    ''' </summary>
    Public Sub DshSetSkillEnabled(Instance As DshInstance, Skill As DshSkill, Enabled As Boolean)
        If Skill Is Nothing Then Throw New Exception("未指定技能")
        If Skill.Enabled = Enabled Then Return
        Dim Root As String = DshSkillRoot(Instance)
        If Skill.IsBundle Then
            Dim OldDir As String = PathUtils.RemoveSlashSuffix(Skill.Path)
            Dim FolderName As String = PathUtils.GetLastPart(OldDir)
            Dim NewName As String = If(Enabled, FolderName.Replace(DshSkillDisabledPrefix, ""), DshSkillDisabledPrefix & FolderName)
            Dim NewDir As String = PathUtils.AddSlashSuffix(PathUtils.RemoveLastPart(OldDir)) & NewName
            DirectoryUtils.Move(OldDir, PathUtils.RemoveSlashSuffix(NewDir))
            Skill.Path = NewDir
            Skill.FolderName = NewName
            '目录名不变但 SKILL.md 被改名的情形
            If Enabled AndAlso Not FileUtils.Exists(NewDir & "SKILL.md") AndAlso FileUtils.Exists(NewDir & "SKILL.md" & DshSkillDisabledPrefix) Then
                FileUtils.Move(NewDir & "SKILL.md" & DshSkillDisabledPrefix, NewDir & "SKILL.md")
            ElseIf Not Enabled AndAlso FileUtils.Exists(NewDir & "SKILL.md") Then
                '目录已经带了 .disabled~ 前缀，文件不用再改
            End If
        Else
            If Enabled Then
                Dim OldFile As String = Skill.Path
                If OldFile.EndsWith(".md" & DshSkillDisabledPrefix) Then
                    Dim NewFile As String = OldFile.Substring(0, OldFile.Length - DshSkillDisabledPrefix.Length)
                    FileUtils.Move(OldFile, NewFile)
                    Skill.Path = NewFile
                End If
            Else
                Dim OldFile As String = Skill.Path
                If Not OldFile.EndsWith(DshSkillDisabledPrefix) Then
                    Dim NewFile As String = OldFile & DshSkillDisabledPrefix
                    FileUtils.Move(OldFile, NewFile)
                    Skill.Path = NewFile
                End If
            End If
        End If
        Skill.Enabled = Enabled
        Logger.Info($"技能 {Skill.Name} 已{(If(Enabled, "启用", "关闭"))}（整合包 {Instance.Name}）")
    End Sub

    ''' <summary>删除一个技能。</summary>
    Public Sub DshDeleteSkill(Instance As DshInstance, Skill As DshSkill)
        If Skill Is Nothing Then Throw New Exception("未指定技能")
        If Skill.IsBundle Then
            DirectoryUtils.Delete(Skill.Path, toRecycleBin:=True)
        Else
            FileUtils.Delete(Skill.Path, toRecycleBin:=True)
        End If
        Logger.Info($"技能 {Skill.Name} 已删除（整合包 {Instance.Name}）")
    End Sub

#End Region

#Region "插件：扫描 / 管理"

    ''' <summary>一个插件。</summary>
    Public Class DshPlugin
        ''' <summary>npm 包名。</summary>
        Public Property PackageName As String = ""
        ''' <summary>版本。</summary>
        Public Property Version As String = ""
        ''' <summary>是否为 dsh 自带的基础包（bundles 提供的）。</summary>
        Public Property IsBuiltIn As Boolean = False
        ''' <summary>是否启用。</summary>
        Public Property Enabled As Boolean = True
        ''' <summary>本地是否已安装（node_modules 里存在）。</summary>
        Public Property Installed As Boolean = False
        ''' <summary>说明。</summary>
        Public Property Description As String = ""
        ''' <summary>关闭原因（当 Enabled=False 时）。</summary>
        Public Property DisabledReason As String = ""

        Public Overrides Function ToString() As String
            Return PackageName
        End Function
    End Class

    ''' <summary>profile 目录。</summary>
    Public Function DshProfileDir(Instance As DshInstance) As String
        Return Instance.PathDshHome & "profiles\" & Instance.Profile & "\"
    End Function

    ''' <summary>
    ''' 扫描整合包的插件列表：
    '''   1. profile package.json 里 dsh.profile.bundles 声明的内置包（不可直接卸载）
    '''   2. profile package.json 的 dependencies（用户装的插件）
    '''   3. node_modules 下实际存在的包（以文件系统为准，能反映真实状态）
    ''' 并读取 cordis.patch.yml 判断哪些被 disable 了。
    ''' </summary>
    Public Function DshScanPlugins(Instance As DshInstance) As List(Of DshPlugin)
        Dim Result As New List(Of DshPlugin)
        Dim ProfileDir As String = DshProfileDir(Instance)
        Dim PkgPath As String = ProfileDir & "package.json"
        Dim BuiltInNames As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        Dim Deps As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)

        If FileUtils.Exists(PkgPath) Then
            Try
                Dim Pkg As JObject = JObject.Parse(FileUtils.ReadAsString(PkgPath))
                Dim Bundles As JToken = Pkg.SelectToken("dsh.profile.bundles")
                If Bundles IsNot Nothing AndAlso Bundles.Type = JTokenType.Array Then
                    For Each B As JToken In CType(Bundles, JArray)
                        BuiltInNames.Add(B.ToString())
                    Next
                End If
                Dim DepsNode As JObject = TryCast(Pkg("dependencies"), JObject)
                If DepsNode IsNot Nothing Then
                    For Each P As JProperty In DepsNode.Properties()
                        Deps(P.Name) = P.Value.ToString()
                    Next
                End If
            Catch ex As Exception
                Logger.Warn(ex, $"读取 profile package.json 失败：{Instance.Name}")
            End Try
        End If

        '实际存在的包
        Dim NmDir As String = ProfileDir & "node_modules\"
        If DirectoryUtils.Exists(NmDir) Then
            For Each Dir As String In DirectoryUtils.EnumerateDirectories(NmDir)
                Dim FolderName As String = PathUtils.GetLastPart(Dir)
                If FolderName.StartsWith(".") Then Continue For
                If FolderName.StartsWith("@") Then
                    '作用域包
                    For Each Sub_ As String In DirectoryUtils.EnumerateDirectories(Dir)
                        Dim SubName As String = PathUtils.GetLastPart(Sub_)
                        If SubName.StartsWith(".") Then Continue For
                        Dim Full As String = FolderName & "/" & SubName
                        If Full.StartsWith("@deepseek-ai/dsh-") OrElse BuiltInNames.Contains(Full) Then Continue For '内置基础包不列出来，避免噪声
                        Deps(Full) = DshReadInstalledVersion(Sub_)
                    Next
                Else
                    If BuiltInNames.Contains(FolderName) Then Continue For
                    If Not Deps.ContainsKey(FolderName) Then Deps(FolderName) = DshReadInstalledVersion(Dir)
                End If
            Next
        End If

        Dim Disabled As Dictionary(Of String, String) = DshReadDisabledPlugins(Instance)

        For Each Pair As KeyValuePair(Of String, String) In Deps
            Dim Plugin As New DshPlugin With {
                .PackageName = Pair.Key,
                .Version = Pair.Value,
                .IsBuiltIn = BuiltInNames.Contains(Pair.Key),
                .Enabled = Not Disabled.ContainsKey(Pair.Key),
                .Installed = DirectoryUtils.Exists(NmDir & Pair.Key.Replace("/", "\")),
                .Description = DshReadPluginDescription(NmDir & Pair.Key.Replace("/", "\"))
            }
            If Not Plugin.Enabled Then Plugin.DisabledReason = Disabled(Plugin.PackageName)
            Result.Add(Plugin)
        Next
        Return Result.OrderBy(Function(p) p.PackageName, StringComparer.OrdinalIgnoreCase).ToList()
    End Function

    Private Function DshReadInstalledVersion(PackageDir As String) As String
        Try
            Dim PkgPath As String = PathUtils.AddSlashSuffix(PackageDir) & "package.json"
            If FileUtils.Exists(PkgPath) Then
                Dim Pkg As JObject = JObject.Parse(FileUtils.ReadAsString(PkgPath))
                If Pkg("version") IsNot Nothing Then Return Pkg("version").ToString()
            End If
        Catch
        End Try
        Return ""
    End Function

    Private Function DshReadPluginDescription(PackageDir As String) As String
        Try
            Dim PkgPath As String = PathUtils.AddSlashSuffix(PackageDir) & "package.json"
            If FileUtils.Exists(PkgPath) Then
                Dim Pkg As JObject = JObject.Parse(FileUtils.ReadAsString(PkgPath))
                If Pkg("description") IsNot Nothing Then Return Pkg("description").ToString()
            End If
        Catch
        End Try
        Return ""
    End Function

    ''' <summary>读取 cordis.patch.yml 中所有被 disable 的插件（id → 原因）。</summary>
    Private Function DshReadDisabledPlugins(Instance As DshInstance) As Dictionary(Of String, String)
        Dim Result As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
        Dim PatchPath As String = DshProfileDir(Instance) & "cordis.patch.yml"
        If Not FileUtils.Exists(PatchPath) Then Return Result
        Try
            Dim Lines = FileUtils.ReadAsLines(PatchPath)
            Dim CurrentName As String = Nothing
            For Each Raw As String In Lines
                Dim Line As String = Raw.Trim()
                If Line.StartsWith("-") Then
                    CurrentName = Nothing
                ElseIf Line.StartsWith("name:") Then
                    CurrentName = Line.Substring(5).Trim().Trim(""""c, "'"c)
                ElseIf Line.StartsWith("disabled:") AndAlso CurrentName IsNot Nothing Then
                    Dim Value As String = Line.Substring(9).Trim().ToLowerInvariant()
                    If Value = "true" Then Result(CurrentName) = "在 cordis.patch.yml 中被禁用"
                End If
            Next
        Catch ex As Exception
            Logger.Warn(ex, $"读取 cordis.patch.yml 失败：{Instance.Name}")
        End Try
        Return Result
    End Function

    ''' <summary>安装一个插件到该整合包的 profile（走 dsh plugin → pnpm）。</summary>
    Public Sub DshInstallPlugin(Loader As LoaderBase, Instance As DshInstance, PackageName As String)
        If String.IsNullOrWhiteSpace(PackageName) Then Throw New Exception("未指定要安装的插件包名")
        If Not DshVersionInstalled(Instance.DshVersion) Then Throw New Exception($"请先安装整合包绑定的 dsh {Instance.DshVersion}")
        DshEnsureProfile(Loader, Instance)
        DshLog($"正在安装插件：{PackageName}", Loader)
        Dim Args As String = $"""{DshBinJs(Instance.DshVersion)}"" plugin --profile {Instance.Profile} add ""{PackageName}"""
        DshRunCli(Loader, Instance, Args, 10 * 60 * 1000)
        DshLog($"插件安装完成：{PackageName}", Loader)
    End Sub

    ''' <summary>卸载一个插件。</summary>
    Public Sub DshRemovePlugin(Loader As LoaderBase, Instance As DshInstance, PackageName As String)
        If String.IsNullOrWhiteSpace(PackageName) Then Throw New Exception("未指定要卸载的插件包名")
        DshLog($"正在卸载插件：{PackageName}", Loader)
        Dim Args As String = $"""{DshBinJs(Instance.DshVersion)}"" plugin --profile {Instance.Profile} remove ""{PackageName}"""
        DshRunCli(Loader, Instance, Args, 10 * 60 * 1000)
        DshLog($"插件已卸载：{PackageName}", Loader)
    End Sub

    ''' <summary>
    ''' 启用/关闭一个插件：在 profile 的 cordis.patch.yml 里加/去一条 disabled 记录。
    ''' 这是 dsh 官方的 patch 层机制，关闭后插件不会被挂载。
    ''' </summary>
    Public Sub DshSetPluginEnabled(Instance As DshInstance, Plugin As DshPlugin, Enabled As Boolean)
        If Plugin Is Nothing Then Throw New Exception("未指定插件")
        If Plugin.Enabled = Enabled Then Return
        Dim PatchPath As String = DshProfileDir(Instance) & "cordis.patch.yml"
        Dim Lines As New List(Of String)
        If FileUtils.Exists(PatchPath) Then Lines.AddRange(FileUtils.ReadAsLines(PatchPath))

        '移除该插件已有的 patch 块
        Dim NewLines As New List(Of String)
        Dim i As Integer = 0
        While i < Lines.Count
            Dim Line As String = Lines(i)
            If Line.Trim() = "- name: """ & Plugin.PackageName & """" OrElse
               Line.Trim() = "- name: " & Plugin.PackageName Then
                '跳过这个块（直到下一个顶层 "- " 或文件结束）
                i += 1
                While i < Lines.Count AndAlso Not Lines(i).TrimStart().StartsWith("- ")
                    i += 1
                End While
                Continue While
            End If
            NewLines.Add(Line)
            i += 1
        End While

        If Not Enabled Then
            NewLines.Add($"- name: ""{Plugin.PackageName}""")
            NewLines.Add("  disabled: true")
            NewLines.Add($"  # 由 PCL2-DSH 启动器关闭于 {Now:yyyy'-'MM'-'dd HH':'mm':'ss'}")
        End If

        FileUtils.Write(PatchPath, NewLines.Join(vbCrLf) & vbCrLf, NewUTF8())
        Plugin.Enabled = Enabled
        Plugin.DisabledReason = If(Enabled, "", "在 cordis.patch.yml 中被禁用")
        Logger.Info($"插件 {Plugin.PackageName} 已{(If(Enabled, "启用", "关闭"))}（整合包 {Instance.Name}）")
    End Sub

#End Region

#Region "CLI 执行"

    Private Function NewUTF8() As Encoding
        Return New UTF8Encoding(False)
    End Function

    ''' <summary>用整合包的环境执行 dsh CLI，等待完成并返回输出。</summary>
    Public Function DshRunCli(Loader As LoaderBase, Instance As DshInstance, Arguments As String, TimeoutMs As Integer) As String
        Dim Info As ProcessStartInfo = DshNewStartInfo(DshNodeExe, Arguments, Instance.PathInstance)
        DshApplyEnvironment(Info, Instance.PathDshHome, DshRuntimeRootEffective)
        Return DshRunInfoWithLoader(Loader, Info, TimeoutMs)
    End Function

    ''' <summary>执行一个 ProcessStartInfo 并捕获全部输出（无 Loader）。</summary>
    Public Function DshRunInfo(Info As ProcessStartInfo, TimeoutMs As Integer) As String
        Return DshRunInfoWithLoader(Nothing, Info, TimeoutMs)
    End Function

    ''' <summary>执行一个 ProcessStartInfo 并捕获全部输出，支持进度回调与取消。</summary>
    Public Function DshRunInfoWithLoader(Loader As LoaderBase, Info As ProcessStartInfo, TimeoutMs As Integer) As String
        Dim Proc As Process = StartProcess(Info)
        Dim Buffer As New StringBuilder()
        Dim Handler As DataReceivedEventHandler =
            Sub(Sender As Object, E As DataReceivedEventArgs)
                If E.Data Is Nothing Then Return
                SyncLock Buffer
                    Buffer.AppendLine(E.Data)
                End SyncLock
                DshLog(E.Data, Loader)
            End Sub
        AddHandler Proc.OutputDataReceived, Handler
        AddHandler Proc.ErrorDataReceived, Handler
        Proc.BeginOutputReadLine()
        Proc.BeginErrorReadLine()
        Dim Deadline As Long = GetTimeMs() + TimeoutMs
        While Not Proc.HasExited
            If Loader IsNot Nothing AndAlso Loader.State = LoadState.Canceled Then
                Try
                    Proc.Kill()
                Catch
                End Try
                Throw New Exception("操作已取消")
            End If
            If GetTimeMs() > Deadline Then
                Try
                    Proc.Kill()
                Catch
                End Try
                Throw New Exception($"命令执行超时（{TimeoutMs / 1000} 秒）：{Info.FileName} {Info.Arguments}")
            End If
            Thread.Sleep(120)
        End While
        Proc.WaitForExit()
        Dim Text As String = ""
        SyncLock Buffer
            Text = Buffer.ToString()
        End SyncLock
        If Proc.ExitCode <> 0 Then
            '注意：dsh 的部分命令（实测 --dump-config）在正常输出时也会返回非零退出码，
            '所以这里只记录警告，把「是否成功」的判断权交给调用方（通常看产出文件是否存在）。
            Dim Tail As String = Text.Split({vbCrLf, vbLf}, StringSplitOptions.RemoveEmptyEntries).Reverse().Take(8).Reverse().Join(vbCrLf)
            Logger.Warn($"dsh 命令返回非零退出码 {Proc.ExitCode}。输出末尾：{vbCrLf}{Tail}")
        End If
        Return Text
    End Function

#End Region

End Module
