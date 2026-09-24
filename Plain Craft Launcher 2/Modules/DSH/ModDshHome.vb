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
            '重要（实测）：cordis.patch.yml 必须是「顶层 YAML 数组」，空文件或纯注释都会让 dsh 启动直接失败：
            '  Error: overlay ...\cordis.patch.yml must be a top-level YAML array of loader patch entries
            '所以空态必须写 []，注释只能放在 [] 上面。
            FileUtils.Write(ProfileDir & "cordis.patch.yml", DshEmptyPatchText(), New UTF8Encoding(False))
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
    ''' 该整合包专属的 pnpm 内容寻址仓库（store）路径。
    ''' ★ 为什么必须显式指定（实机验证）：pnpm 默认把 store 放在 <DSH_HOME 所在盘>\.pnpm-store，
    '''   实测落到了 **E:\DSHarness\.pnpm-store —— 那正是用户全局 DSH 的 store**，
    '''   同一个盘上所有整合包会共用它，直接破坏"整合包之间隔离"这条铁律。
    ''' ★ 怎么指定才有效（三种方式都实测过）：
    '''     ✘ 环境变量 npm_config_store_dir —— pnpm 12 不认，实测仍用全局 store
    '''     ✘ 环境变量 PNPM_STORE_DIR       —— 同样不认
    '''     ✘ 写 profile 的 .npmrc           —— 也不认（pnpm 读的是它自己的配置链）
    '''     ✔ **命令行 --store-dir** —— 实测生效（生成 <DSH_HOME>\pnpm-store\v11）
    '''   所以这里返回路径，由调用方以 `--store-dir` 透传给 pnpm
    '''   （dsh plugin 本来就是把这些参数原样转给 pnpm 的）。
    ''' </summary>
    Public Function DshPnpmStoreDir(Instance As DshInstance) As String
        Return PathUtils.RemoveSlashSuffix(Instance.PathDshHome & "pnpm-store")
    End Function

    ''' <summary>
    ''' profile 里"框架自带"的基础 bundle 名单。
    ''' 为什么要专门列一份（实机踩坑）：`dsh plugin add` 成功后会**自动把包名也写进**
    ''' `dsh.profile.bundles`（实测：装 dshmarket 后 bundles 变成
    ''' [dsh-base, dsh-web-app, dshmarket]）。如果拿"在 bundles 里"当作内置包的判据，
    ''' 用户刚装的插件就会被当成内置包**隐藏掉、也没法卸载** ——
    ''' 界面表现就是"装完了列表里看不到"，即用户反馈的"似乎不会真的安装"。
    ''' </summary>
    Private ReadOnly DshBaseBundleNames As String() = {
        "@deepseek-ai/dsh-base",
        "@deepseek-ai/dsh-web-app",
        "@deepseek-ai/dsh-app-boot"
    }

    ''' <summary>
    ''' 扫描整合包的插件列表：
    '''   1. profile package.json 的 dependencies（用户装的插件）
    '''   2. node_modules 下实际存在的包（以文件系统为准，能反映真实状态）
    '''   3. profile package.json 的 dsh.profile.bundles 里**非基础**的条目（dsh plugin add 会写进来）
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
                        Dim Name As String = B.ToString()
                        '只有框架自带的那几个才算"内置"；其余出现在 bundles 里的是 dsh plugin add 写的，
                        '属于用户插件，必须照常列出（否则刚装的插件会"消失"，见 DshBaseBundleNames 的说明）
                        If DshBaseBundleNames.Contains(Name, StringComparer.OrdinalIgnoreCase) Then
                            BuiltInNames.Add(Name)
                        Else
                            Deps(Name) = ""
                        End If
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
                        '只过滤框架自带的基础包，不去按 dsh- 前缀一刀切
                        If BuiltInNames.Contains(Full) Then Continue For
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
            Dim InstalledDir As String = NmDir & Pair.Key.Replace("/", "\")
            Dim Plugin As New DshPlugin With {
                .PackageName = Pair.Key,
                .Version = If(DshReadInstalledVersion(InstalledDir) <> "", DshReadInstalledVersion(InstalledDir), Pair.Value),
                .IsBuiltIn = BuiltInNames.Contains(Pair.Key),
                .Enabled = Not Disabled.ContainsKey(Pair.Key),
                .Installed = DirectoryUtils.Exists(InstalledDir),
                .Description = DshReadPluginDescription(InstalledDir)
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

    ''' <summary>
    ''' 读取 cordis.patch.yml 中所有被 disable 的插件（包名 → 原因）。
    ''' 按"块"解析而非按行：name 与 disabled 可能不在同一行，块之间也可能有别的键。
    ''' </summary>
    Private Function DshReadDisabledPlugins(Instance As DshInstance) As Dictionary(Of String, String)
        Dim Result As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
        Dim PatchPath As String = DshProfileDir(Instance) & "cordis.patch.yml"
        If Not FileUtils.Exists(PatchPath) Then Return Result
        Try
            For Each Block As String In DshParsePatchBlocks(PatchPath)
                Dim BlockName As String = Nothing
                Dim Disabled As Boolean = False
                For Each Line As String In Block.Split({vbCrLf, vbLf}, StringSplitOptions.RemoveEmptyEntries)
                    Dim T As String = Line.Trim()
                    If T.StartsWith("name:") Then
                        BlockName = T.Substring(5).Trim().Trim(""""c, "'"c)
                    ElseIf T.StartsWith("disabled:") Then
                        Dim V As String = T.Substring(9).Trim().ToLowerInvariant()
                        If V = "true" OrElse V = "yes" OrElse V = "on" OrElse V = "1" Then Disabled = True
                    End If
                Next
                If Disabled AndAlso Not String.IsNullOrWhiteSpace(BlockName) Then
                    Result(BlockName) = "在 cordis.patch.yml 中被禁用"
                End If
            Next
        Catch ex As Exception
            Logger.Warn(ex, $"读取 cordis.patch.yml 失败：{Instance.Name}")
        End Try
        Return Result
    End Function

    ''' <summary>
    ''' 安装一个插件到该整合包的 profile（走 dsh 的 plugin 命令，dsh 会把参数透传给 pnpm）。
    '''
    ''' ★ 两个关键点（都是实机踩坑换来的）：
    '''  1. **参数必须按数组传给 node**，不能拼成命令行字符串。dsh 的 plugin 命令是把参数
    '''     原样转给 pnpm 的；一旦被 Windows/引号规则二次解析，pnpm 收到的可能整体变成一个畸形"包名"。
    '''     实测失败时 dsh 自己的日志原样是：
    '''         Command failed with exit code 1: pnpm add "dsh plugin --profile web add dshmarket"
    '''     即**整条命令被当成了一个包名**，于是必然失败（用户看到的就是"似乎不会真的安装"）。
    '''  2. **pnpm 是硬依赖**，必须先备好。缺失时 dsh 回 exitCode 127 并提示
    '''     "pnpm was not found; install pnpm and make it available on PATH"。
    ''' </summary>
    Public Sub DshInstallPlugin(Loader As LoaderBase, Instance As DshInstance, PackageName As String)
        PackageName = If(PackageName, "").Trim()
        If String.IsNullOrWhiteSpace(PackageName) Then Throw New Exception("未指定要安装的插件包名")
        If Not DshVersionInstalled(Instance.DshVersion) Then Throw New Exception($"请先安装整合包绑定的 dsh {Instance.DshVersion}")
        DshEnsureProfile(Loader, Instance)
        DshEnsurePnpm(Loader, Instance.PathDshHome) '缺 pnpm 就先备好，否则这一步必然失败
        DshLog($"正在安装插件：{PackageName}", Loader)
        Dim Out As String = DshRunCliArgs(Loader, Instance,
            {DshBinJs(Instance.DshVersion), "plugin", "--profile", Instance.Profile,
             "--store-dir", DshPnpmStoreDir(Instance), "add", PackageName},
            15 * 60 * 1000)
        'dsh plugin add 成功时会把包名**自动写进 package.json 的 dsh.profile.bundles**
        '（实测确认），那会让它在界面上被当成"内置包"隐藏、也无法用 patch 关掉。
        '这里把它从 bundles 里摘掉，只留在 dependencies —— 之后启用/关闭由 cordis.patch.yml 负责。
        DshRemoveFromProfileBundles(Instance, PackageName)
        '确认真的落盘了，否则给出明确错误（不要静默"成功"）
        Dim PkgDir As String = DshProfileDir(Instance) & "node_modules\" & PackageName.Replace("/", "\")
        If Not DirectoryUtils.Exists(PkgDir) Then
            Throw New Exception($"插件安装命令已执行但未在 profile 里找到该包：{PackageName}{vbCrLf}" &
                                "常见原因：包名拼写错误、该包未发布到 npm、或网络不通。" & vbCrLf &
                                "命令输出末尾：" & vbCrLf & DshTail(Out, 6))
        End If
        DshLog($"插件安装完成：{PackageName}", Loader)
    End Sub

    ''' <summary>卸载一个插件（同样按数组传参）。</summary>
    Public Sub DshRemovePlugin(Loader As LoaderBase, Instance As DshInstance, PackageName As String)
        PackageName = If(PackageName, "").Trim()
        If String.IsNullOrWhiteSpace(PackageName) Then Throw New Exception("未指定要卸载的插件包名")
        DshEnsurePnpm(Loader, Instance.PathDshHome)
        DshLog($"正在卸载插件：{PackageName}", Loader)
        DshRunCliArgs(Loader, Instance,
            {DshBinJs(Instance.DshVersion), "plugin", "--profile", Instance.Profile,
             "--store-dir", DshPnpmStoreDir(Instance), "remove", PackageName},
            10 * 60 * 1000)
        DshRemoveFromProfileBundles(Instance, PackageName)
        DshLog($"插件已卸载：{PackageName}", Loader)
    End Sub

    ''' <summary>
    ''' 当前生效的 npm registry（带结尾斜杠）。
    ''' 读设置项 `DshNpmSource`（设置页「npm 下载源」下拉框：0=官方 1=国内镜像），
    ''' 这样搜索插件时也沿用用户选的源，不会出现"安装走镜像、搜索却走官方"的割裂。
    ''' </summary>
    Public Function DshNpmRegistryUrl() As String
        Try
            If DshSetting("DshNpmSource", 1) = 0 Then Return DshNpmRegistry
        Catch
        End Try
        Return DshNpmRegistryMirror
    End Function

    ''' <summary>一个搜索结果（用于"找插件"界面）。</summary>
    Public Class DshPluginSearchItem
        Public Property PackageName As String = ""
        Public Property Version As String = ""
        Public Property Description As String = ""
        Public Property Downloads As Long = 0
        ''' <summary>是否看起来确实是 dsh 插件（keywords 含 dsh-plugin，或 package.json 里有 dsh.bundle）。</summary>
        Public Property LooksLikePlugin As Boolean = False

        Public Overrides Function ToString() As String
            Return PackageName
        End Function
    End Class

    ''' <summary>
    ''' 在 npm registry 里搜索插件包。
    ''' 解决用户反馈的"包名通常很难找"：直接按关键词搜，不用先去 npm 网站翻。
    '''
    ''' 说明：
    '''   · **沿用用户/启动器已配置的 registry**（见 DshNpmRegistry）——
    '''     国内用户通常设了 npmmirror 镜像，硬编码 registry.npmjs.org 在他们那里又慢又可能不通。
    '''   · npm 的搜索不做"这是不是 dsh 插件"的过滤，所以这里同时看 keywords 与 dsh 字段自己判断
    '''     （LooksLikePlugin），并把像插件的排前面。
    ''' 必须在后台线程调用（会联网）。
    ''' </summary>
    Public Function DshSearchPlugins(Keyword As String, Optional Limit As Integer = 20) As List(Of DshPluginSearchItem)
        Dim Result As New List(Of DshPluginSearchItem)
        Dim Query As String = If(Keyword, "").Trim()
        If Query = "" Then Return Result
        Dim Url As String = DshNpmRegistryUrl() & "-/v1/search?text=" &
                            Uri.EscapeDataString(Query) & "&size=" & Limit.ToString()
        DshLog($"搜索插件：{Url}")
        Dim Text As String = NetRequestByClientRetry(Url, RequireJson:=True)
        Dim Json As JObject = JObject.Parse(Text)
        Dim Objects As JArray = TryCast(Json("objects"), JArray)
        If Objects Is Nothing Then Return Result
        For Each Item As JToken In Objects
            Try
                Dim Pkg As JObject = TryCast(Item("package"), JObject)
                If Pkg Is Nothing Then Continue For
                Dim One As New DshPluginSearchItem With {
                    .PackageName = If(Pkg("name") Is Nothing, "", Pkg("name").ToString()),
                    .Version = If(Pkg("version") Is Nothing, "", Pkg("version").ToString()),
                    .Description = If(Pkg("description") Is Nothing, "", Pkg("description").ToString())
                }
                If One.PackageName = "" Then Continue For
                Dim Score As JObject = TryCast(Item("score"), JObject)
                Dim Detail As JObject = TryCast(Score("detail"), JObject)
                If Detail IsNot Nothing AndAlso Detail("popularity") IsNot Nothing Then
                    One.Downloads = CLng(Detail("popularity").ToString() * 1000000) '仅用于排序
                End If
                '判断像不像 dsh 插件：keywords 里含 dsh-plugin，或带了 dsh.bundle 字段
                Dim Keywords As JArray = TryCast(Pkg("keywords"), JArray)
                If Keywords IsNot Nothing Then
                    For Each K As JToken In Keywords
                        Dim Ks As String = K.ToString().ToLowerInvariant()
                        If Ks.Contains("dsh-plugin") OrElse Ks = "dsh" OrElse Ks.Contains("deepseek-harness") Then
                            One.LooksLikePlugin = True
                            Exit For
                        End If
                    Next
                End If
                If Pkg("dsh") IsNot Nothing Then One.LooksLikePlugin = True
                Result.Add(One)
            Catch
            End Try
        Next
        '像插件的排前面，其次按热度
        Return Result.OrderByDescending(Function(I) I.LooksLikePlugin).ThenByDescending(Function(I) I.Downloads).ToList()
    End Function

    ''' <summary>
    ''' 推荐插件清单（内置兜底，避免搜索接口不通时无从下手）。
    ''' dshmarket 是社区做的**可视化插件市场**，装上后在 dsh 界面里就能逛插件市场，
    ''' 从根本上解决"包名难找"（用户实测该包可用）。
    ''' </summary>
    Public Function DshRecommendedPlugins() As List(Of DshPluginSearchItem)
        Return New List(Of DshPluginSearchItem) From {
            New DshPluginSearchItem With {
                .PackageName = "dshmarket",
                .Description = "可视化插件市场：在 dsh 界面里浏览、搜索、一键安装社区插件（装完后就不必再手找包名了）",
                .LooksLikePlugin = True},
            New DshPluginSearchItem With {
                .PackageName = "dsh-plugin-manager",
                .Description = "插件管理器（若该包不存在会给出明确报错，可改用搜索）",
                .LooksLikePlugin = True}
        }
    End Function

    ''' <summary>
    ''' 只清理"历史遗留"：早期版本把 npm 包装进过 bundles，这里把 @deepseek-ai/ 开头的包名去掉。
    '''
    ''' ★★ 千万不要把这个函数改成"把用户插件从 bundles 里摘掉"——上一版我那样做过，是错的：
    '''   dshmarket 的 package.json 里写着 `"dsh": { "bundle": { "patch": "./cordis.patch.yml" } }`，
    '''   说明它是**bundle 类型插件**，`dsh.profile.bundles` 正是它的加载入口。
    '''   把它摘出去 → 插件根本不会被加载（装了等于没装），比"看不到"严重得多。
    ''' 正确的做法是让**扫描逻辑**别把非基础 bundle 当内置包（见 DshBaseBundleNames）。
    ''' </summary>
    Public Sub DshRemoveFromProfileBundles(Instance As DshInstance, PackageName As String)
        If Not PackageName.StartsWith("@deepseek-ai/", StringComparison.OrdinalIgnoreCase) Then Return
        Try
            Dim PkgPath As String = DshProfileDir(Instance) & "package.json"
            If Not FileUtils.Exists(PkgPath) Then Return
            Dim Pkg As JObject = JObject.Parse(FileUtils.ReadAsString(PkgPath))
            Dim Bundles As JArray = TryCast(Pkg.SelectToken("dsh.profile.bundles"), JArray)
            If Bundles Is Nothing Then Return
            Dim Hit As JToken = Bundles.FirstOrDefault(Function(B) String.Equals(B.ToString(), PackageName, StringComparison.OrdinalIgnoreCase))
            If Hit Is Nothing Then Return
            Hit.Remove()
            FileUtils.Write(PkgPath, Pkg.ToString(Formatting.Indented) & vbCrLf, NewUTF8())
            DshLog($"已把历史遗留的 npm 包 {PackageName} 从 profile bundles 里摘出（实际代码仍在 node_modules 里）")
        Catch ex As Exception
            Logger.Warn(ex, $"清理 profile bundles 里的 {PackageName} 失败")
        End Try
    End Sub

    ''' <summary>取一段输出的末尾若干行，用于错误提示。</summary>
    Private Function DshTail(Text As String, Lines As Integer) As String
        If String.IsNullOrWhiteSpace(Text) Then Return "（无输出）"
        Return Text.Split({vbCrLf, vbLf}, StringSplitOptions.RemoveEmptyEntries).Reverse().Take(Lines).Reverse().Join(vbCrLf)
    End Function

    ''' <summary>
    ''' cordis.patch.yml 的"空"内容。
    ''' 必须是合法的顶层 YAML 数组（`[]`），注释写在它上面——
    ''' 空文件或纯注释都会让 dsh 直接启动失败（实机踩过）。
    ''' </summary>
    Public Function DshEmptyPatchText() As String
        Return "# 本文件由 PCL2-DSH 启动器维护：顶层 YAML 数组形式的 loader patch 列表。" & vbCrLf &
               "# 关掉插件的条目形如：" & vbCrLf &
               "#   - id: <插件 id>" & vbCrLf &
               "#     name: ""@deepseek-ai/xxx""" & vbCrLf &
               "#     disabled: true" & vbCrLf &
               "# 空列表必须写成 []，不能留空文件或只有注释。" & vbCrLf &
               "[]" & vbCrLf
    End Function

    ''' <summary>按 yaml 条目切分 patch 文件；每个块的文本原样保留。</summary>
    Private Function DshParsePatchBlocks(PatchPath As String) As List(Of String)
        Dim Blocks As New List(Of String)
        If Not FileUtils.Exists(PatchPath) Then Return Blocks
        Dim Current As New List(Of String)
        For Each Raw As String In FileUtils.ReadAsLines(PatchPath)
            Dim Line As String = Raw
            '忽略文件头部的注释与 []（这些由我们自己重新生成）
            Dim Trimmed As String = Line.Trim()
            If Trimmed.StartsWith("- ") OrElse Trimmed.StartsWith("-") AndAlso Trimmed.Length = 1 Then
                If Current.Count > 0 Then Blocks.Add(Current.Join(vbCrLf))
                Current = New List(Of String) From {Line}
            ElseIf Current.Count > 0 Then
                Current.Add(Line)
            End If
        Next
        If Current.Count > 0 Then Blocks.Add(Current.Join(vbCrLf))
        Return Blocks
    End Function

    ''' <summary>判断一个 patch 块是否针对指定包名。</summary>
    Private Function DshPatchBlockMatches(Block As String, PackageName As String) As Boolean
        For Each Line As String In Block.Split({vbCrLf, vbLf}, StringSplitOptions.RemoveEmptyEntries)
            Dim T As String = Line.Trim()
            If T.StartsWith("name:") Then
                Dim V As String = T.Substring(5).Trim().Trim(""""c, "'"c)
                If String.Equals(V, PackageName, StringComparison.OrdinalIgnoreCase) Then Return True
            End If
        Next
        Return False
    End Function

    ''' <summary>由包名推导 patch 里用的 id。</summary>
    Private Function DshPluginIdFromPackage(PackageName As String) As String
        Dim Id As String = If(PackageName, "").Trim()
        If Id.StartsWith("@") Then
            Dim Slash As Integer = Id.IndexOf("/"c)
            If Slash >= 0 Then Id = Id.Substring(Slash + 1)
        End If
        If Id.StartsWith("dsh-", StringComparison.OrdinalIgnoreCase) Then Id = Id.Substring(4)
        Return If(Id, "").Trim()
    End Function

    ''' <summary>
    ''' 启用/关闭一个插件：在 profile 的 cordis.patch.yml 里加/去一条 disabled 记录。
    ''' 这是 dsh 官方的 patch 层机制，关闭后插件不会被挂载（改完需要重启该整合包的 dsh 才完全生效）。
    ''' </summary>
    Public Sub DshSetPluginEnabled(Instance As DshInstance, Plugin As DshPlugin, Enabled As Boolean)
        If Plugin Is Nothing Then Throw New Exception("未指定插件")
        If Plugin.Enabled = Enabled Then Return
        Dim PatchPath As String = DshProfileDir(Instance) & "cordis.patch.yml"

        '保留用户自己写的、与本次无关的 patch 块；丢掉本插件已有的记录（避免重复）
        Dim Kept As New List(Of String)
        For Each Block As String In DshParsePatchBlocks(PatchPath)
            If Not DshPatchBlockMatches(Block, Plugin.PackageName) AndAlso Block.Trim() <> "[]" Then Kept.Add(Block)
        Next

        If Not Enabled Then
            Dim Id As String = DshPluginIdFromPackage(Plugin.PackageName)
            Dim Entry As String = $"- id: {Id}" & vbCrLf &
                                  $"  name: ""{Plugin.PackageName}""" & vbCrLf &
                                  "  disabled: true" & vbCrLf &
                                  $"  # 由 PCL2-DSH 启动器关闭于 {Now:yyyy'-'MM'-'dd HH':'mm':'ss}"
            Kept.Add(Entry)
        End If

        '输出：注释头 + （[] 或全部块）
        Dim Text As String
        If Kept.Count = 0 Then
            Text = DshEmptyPatchText()
        Else
            Text = "# 本文件由 PCL2-DSH 启动器维护（顶层 YAML 数组，不能为空文件）。" & vbCrLf &
                   Kept.Join(vbCrLf) & vbCrLf
        End If
        FileUtils.Write(PatchPath, Text, NewUTF8())
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

    ''' <summary>
    ''' 用整合包的环境执行 dsh CLI，**参数按数组传**（推荐新代码一律用这个）。
    '''
    ''' 为什么必须按数组传：`ProcessStartInfo.Arguments` 是一个字符串，要交给 Windows 再解析一次，
    ''' 引号/空格稍有出入就会把多个参数粘成一个。实测 `dsh plugin add` 失败时，
    ''' dsh 自己的诊断日志是：
    '''     Command failed with exit code 1: pnpm add "dsh plugin --profile web add dshmarket"
    ''' —— **整条命令被当成一个包名**传给了 pnpm。
    '''
    ''' 注意：本项目是 .NET Framework 4.8，**没有** `ProcessStartInfo.ArgumentList`
    ''' （那是 .NET Core 2.1+ 的 API，直接写会报 BC30456"不是 ProcessStartInfo 的成员"）。
    ''' 所以这里按 Windows 的规则手工转义，等价于 ArgumentList 的行为。
    ''' </summary>
    Public Function DshRunCliArgs(Loader As LoaderBase, Instance As DshInstance, Args As IEnumerable(Of String), TimeoutMs As Integer) As String
        Dim Info As ProcessStartInfo = DshNewStartInfo(DshNodeExe, DshQuoteArgs(Args), Instance.PathInstance)
        DshApplyEnvironment(Info, Instance.PathDshHome, DshRuntimeRootEffective)
        Return DshRunInfoWithLoader(Loader, Info, TimeoutMs)
    End Function

    ''' <summary>
    ''' 按 Windows 命令行规则把参数数组拼成一个命令行字符串。
    ''' 规则：① 没有空格/制表/引号的参数原样输出；② 其余用双引号包住；
    '''       ③ 参数内部的 `"` 写成 `\"`；④ 反斜杠只有在后面紧跟引号（或被结尾补的引号影响）时才加倍。
    ''' </summary>
    Public Function DshQuoteArgs(Args As IEnumerable(Of String)) As String
        Dim Parts As New List(Of String)
        For Each A As String In Args
            Parts.Add(DshQuoteOneArg(If(A, "")))
        Next
        Return String.Join(" ", Parts)
    End Function

    Private Function DshQuoteOneArg(A As String) As String
        If A.Length > 0 AndAlso Not A.Any(Function(C) C = " "c OrElse C = vbTab OrElse C = """"c) Then Return A
        Dim Sb As New StringBuilder()
        Sb.Append(""""c)
        Dim Backslashes As Integer = 0
        For Each C As Char In A
            If C = "\"c Then
                Backslashes += 1
            ElseIf C = """"c Then
                '引号前的反斜杠要加倍，再加一个转义反斜杠
                Sb.Append(New String("\"c, Backslashes * 2 + 1))
                Sb.Append(""""c)
                Backslashes = 0
            Else
                Sb.Append(New String("\"c, Backslashes))
                Sb.Append(C)
                Backslashes = 0
            End If
        Next
        '结尾的反斜杠要加倍，否则会把收尾引号转义掉
        Sb.Append(New String("\"c, Backslashes * 2))
        Sb.Append(""""c)
        Return Sb.ToString()
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
